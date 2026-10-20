using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Cli.Tests;

/// <summary>
/// One increment, and the two things that must still be true when its lease goes
/// underneath it: the meter reading goes up, and nothing else does.
/// </summary>
public sealed class IncrementTests
{
    private static async Task<(Claim Claim, Harness Harness)> HoldingAsync(Harness h, string key, Guid token)
    {
        h.Wire.Reply("POST", $"/api/hatch/issues/{key}/claim", HttpStatusCode.OK, Fixtures.Taken(token));
        h.Wire.Reply("DELETE", $"/api/hatch/issues/{key}/claim", HttpStatusCode.NoContent);

        var (claim, _) = await Claim.TakeAsync(h.Client, key, "test:/checkout", default, Harness.Beat);
        return (claim!, h);
    }

    [Fact]
    public async Task An_increment_posts_its_bill_and_asks_the_board_where_the_ticket_ended_up()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();
        var (claim, _) = await HoldingAsync(h, "AER-1", token);

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.True(report.Moved);
        Assert.Equal("In Progress -> In Review", report.Outcome);
        Assert.Equal("s-1", report.SessionId);
        Assert.Equal(1.5m, report.Cost);

        var billed = h.Wire.To("POST", "/api/hatch/issues/AER-1/work-log")[0].Read<WorkLogEntryRequest>();
        Assert.Equal("s-1", billed.SessionId);
        Assert.Equal("Did a thing", billed.Title);
        Assert.Equal("In detail.", billed.Summary);
        Assert.Equal(12, billed.Turns);
        Assert.NotEqual(default, billed.StartedAt);

        // The board read carries the runner's own token, so its own lease does
        // not fold its own dispatch.
        Assert.Contains($"heldToken={token}", h.Wire.To("GET", "/api/hatch/work/AER-1")[0].Query,
            StringComparison.OrdinalIgnoreCase);

        await claim.ReleaseAsync();
    }

    /// <summary>
    /// HA-850: the seam <see cref="IAgentAdapter"/> draws is real, not merely
    /// typed - a second adapter with its own id, its own resume wording and
    /// its own stream format runs an increment end to end, through the same
    /// claim/spawn/work-log path the first test above does, without a single
    /// piece of what is said, spawned or posted carrying Claude's own
    /// vocabulary.
    /// </summary>
    [Fact]
    public async Task A_second_adapter_runs_an_increment_without_claudes_own_vocabulary()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();
        var (claim, _) = await HoldingAsync(h, "AER-1", token);

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        h.Sessions.Behaviour = (_, onLine, _) =>
        {
            onLine?.Invoke("FAKE-RESULT fake-session-1|Did a fake thing|In fake detail.|7|2.5|45000");
            return Task.FromResult(new SessionResult(0, ""));
        };

        var adapter = new FakeAdapter();
        var report = await (h.Runtime with { Adapter = adapter }).Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.True(report.Moved);
        Assert.Equal("fake-session-1", report.SessionId);

        Assert.NotNull(adapter.WrittenHooks);
        Assert.Equal(adapter.WrittenHooks!.Opaque, h.Sessions.Spawned[0].HookSettings);

        var billed = h.Wire.To("POST", "/api/hatch/issues/AER-1/work-log")[0].Read<WorkLogEntryRequest>();
        Assert.Equal("fake-session-1", billed.SessionId);
        Assert.Equal("Did a fake thing", billed.Title);
        Assert.Equal("In fake detail.", billed.Summary);
        Assert.Equal(7, billed.Turns);
        Assert.Equal("fake-agent", billed.Agent);

        Assert.True(h.Say.Mentions("fake-agent"));

        var billedBody = h.Wire.To("POST", "/api/hatch/issues/AER-1/work-log")[0].Body;
        Assert.All(h.Say.Said, line => Assert.DoesNotContain("claude", line, StringComparison.OrdinalIgnoreCase));
        Assert.All(h.Sessions.Spawned, s => Assert.DoesNotContain("claude", s.Prompt, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("claude", billedBody, StringComparison.OrdinalIgnoreCase);

        await claim.ReleaseAsync();
    }

    /// <summary>The wire an increment under a fake adapter needs, and the one request it spawned.</summary>
    private static async Task<SessionRequest> RunFakeAsync(Harness h, FakeAdapter adapter)
    {
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        h.Sessions.Behaviour = (_, onLine, _) =>
        {
            onLine?.Invoke("FAKE-RESULT fake-session-1|Did a fake thing|In fake detail.|7|2.5|45000");
            return Task.FromResult(new SessionResult(0, ""));
        };

        await (h.Runtime with { Adapter = adapter }).Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);
        await claim.ReleaseAsync();

        return h.Sessions.Spawned[0];
    }

    [Fact]
    public async Task A_reader_that_mints_a_session_id_hands_it_to_the_spawn()
    {
        using var h = new Harness();
        var adapter = new FakeAdapter { OnReader = f => f.SessionId = "minted-1" };

        var request = await RunFakeAsync(h, adapter);

        Assert.Equal("minted-1", request.SessionId);
    }

    [Fact]
    public async Task The_claude_path_spawns_with_no_session_id_of_its_own_choosing()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.Null(h.Sessions.Spawned[0].SessionId);

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task A_hooks_write_that_failed_is_complained_of_where_the_agent_has_a_door_for_them()
    {
        using var h = new Harness();

        await RunFakeAsync(h, new FakeAdapter { HooksFail = true, CanHook = true });

        Assert.Contains(h.Say.Complained, line => line.Contains("could not write the hooks", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_agent_with_no_hooks_door_is_not_complained_of_for_having_none()
    {
        using var h = new Harness();

        await RunFakeAsync(h, new FakeAdapter { HooksFail = true, CanHook = false });

        Assert.DoesNotContain(h.Say.Complained, line => line.Contains("could not write the hooks", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_usage_limit_under_a_second_adapter_names_that_agent_not_claude()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Sessions.Behaviour = (_, onLine, _) =>
        {
            onLine?.Invoke("FAKE-RESULT fake-session-1|Did a fake thing|In fake detail.|7|2.5|45000");
            return Task.FromResult(new SessionResult(0, ""));
        };

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        var adapter = new FakeAdapter { Limit = new UsageLimit.Recognised(DateTimeOffset.UtcNow.AddHours(1), true, "session") };
        var report = await (h.Runtime with { Adapter = adapter }).Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.True(report.UsageLimited);
        Assert.Contains(h.Say.Said, l => l.Contains("out of Fake Agent usage", StringComparison.Ordinal));
        Assert.All(h.Say.Said, l => Assert.DoesNotContain("Claude", l, StringComparison.OrdinalIgnoreCase));

        await claim.ReleaseAsync();
    }

    /// <summary>
    /// HA-841: a board-wide playbook carries no file facts into the work-log row.
    /// </summary>
    [Fact]
    public async Task An_increment_bills_a_board_sourced_playbook_with_no_file_facts()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();
        var (claim, _) = await HoldingAsync(h, "AER-1", token);

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        var billed = h.Wire.To("POST", "/api/hatch/issues/AER-1/work-log")[0].Read<WorkLogEntryRequest>();
        Assert.Equal("board", billed.PlaybookSource);
        Assert.Null(billed.PlaybookFileTrunk);
        Assert.Null(billed.PlaybookFileSha);
        Assert.Null(billed.PlaybookFileHash);

        await claim.ReleaseAsync();
    }

    /// <summary>
    /// HA-841: a file-sourced playbook carries its repository's trunk, sha and hash into the work-log row.
    /// </summary>
    [Fact]
    public async Task An_increment_bills_a_file_sourced_playbook_with_its_file_facts()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();
        var (claim, _) = await HoldingAsync(h, "AER-1", token);

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        var playbookFile = new PlaybookFileReferenceDto(".hatch/playbooks.yaml", "canonical", "main", "abcdef0123456789", "deadbeef");
        var work = Fixtures.Work("AER-1", playbookFile: playbookFile) with
        {
            Playbook = Fixtures.Playbook(projectKey: "AER") with { Source = "file" },
        };

        await h.Runtime.Increment().RunAsync(
            work, h.Root, "opus", "high", quiet: false, claim, default);

        var billed = h.Wire.To("POST", "/api/hatch/issues/AER-1/work-log")[0].Read<WorkLogEntryRequest>();
        Assert.Equal("file", billed.PlaybookSource);
        Assert.Equal("main", billed.PlaybookFileTrunk);
        Assert.Equal("abcdef0123456789", billed.PlaybookFileSha);
        Assert.Equal("deadbeef", billed.PlaybookFileHash);

        await claim.ReleaseAsync();
    }

    /// <summary>
    /// HA-841: a board-wide row under a repo-sourced project still carries no file facts -
    /// the guard is on the row's own source, not on whether a file reference happens to be present.
    /// </summary>
    [Fact]
    public async Task An_increment_bills_a_board_sourced_row_with_no_file_facts_even_under_a_repo_sourced_project()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();
        var (claim, _) = await HoldingAsync(h, "AER-1", token);

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        var playbookFile = new PlaybookFileReferenceDto(".hatch/playbooks.yaml", "canonical", "main", "abcdef0123456789", "deadbeef");
        var work = Fixtures.Work("AER-1", playbookFile: playbookFile) with
        {
            Playbook = Fixtures.Playbook(),
        };

        await h.Runtime.Increment().RunAsync(
            work, h.Root, "opus", "high", quiet: false, claim, default);

        var billed = h.Wire.To("POST", "/api/hatch/issues/AER-1/work-log")[0].Read<WorkLogEntryRequest>();
        Assert.Equal("board", billed.PlaybookSource);
        Assert.Null(billed.PlaybookFileTrunk);
        Assert.Null(billed.PlaybookFileSha);
        Assert.Null(billed.PlaybookFileHash);

        await claim.ReleaseAsync();
    }

    /// <summary>
    /// HA-245: <see cref="IncrementReport.EndedInReview"/> is the board's own
    /// read of whether the ticket is sitting in the review column right now,
    /// taken in the same call <see cref="IncrementReport.Ended"/> is - not the
    /// playbook's stale target.
    /// </summary>
    [Fact]
    public async Task EndedInReview_is_set_from_the_boards_own_read_after_the_session()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();
        var (claim, _) = await HoldingAsync(h, "AER-1", token);

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review", inReview: true));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.True(report.EndedInReview);

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task The_posted_row_carries_requests_peak_context_and_prompt_chars()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();
        var (claim, _) = await HoldingAsync(h, "AER-1", token);
        var work = Fixtures.Work("AER-1");

        h.Sessions.Behaviour = (_, onLine, _) =>
        {
            onLine?.Invoke(Fixtures.AssistantUsage("msg-1", input: 10, output: 20, cacheCreate: 5, cacheRead: 5));
            onLine?.Invoke(Fixtures.AssistantUsage("msg-2", input: 100, output: 1, cacheCreate: 50, cacheRead: 50));
            onLine?.Invoke(Fixtures.Result(said: "```work-log\nDid a thing\n\nIn detail.\n```"));
            return Task.FromResult(new SessionResult(0, ""));
        };

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        await h.Runtime.Increment().RunAsync(work, h.Root, "opus", "high", quiet: false, claim, default);

        var billed = h.Wire.To("POST", "/api/hatch/issues/AER-1/work-log")[0].Read<WorkLogEntryRequest>();
        Assert.Equal(2, billed.Requests);
        Assert.Equal(200, billed.PeakContextTokens);
        Assert.Equal(Prompt.Compose(work).Length, billed.PromptChars);

        await claim.ReleaseAsync();
    }

    // ---- HA-222: the clamp ----

    [Fact]
    public async Task Crossing_the_playbooks_budget_writes_the_clamp_file_once()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();
        var (claim, _) = await HoldingAsync(h, "AER-1", token);
        var work = Fixtures.Work("AER-1") with { Playbook = Fixtures.Playbook(budget: 1) };
        string? clamp = null;

        h.Sessions.Behaviour = (request, onLine, _) =>
        {
            clamp = Path.Combine(Path.GetDirectoryName(request.HookSettings)!, "clamp.json");

            // 600,000 + 500,000 = 1,100,000 tokens, past the one-million budget.
            onLine?.Invoke(Fixtures.AssistantUsage("msg-1", input: 600_000, output: 500_000));
            Assert.True(File.Exists(clamp));
            var fact = System.Text.Json.JsonDocument.Parse(File.ReadAllText(clamp)).RootElement;
            Assert.Equal(1_100_000, fact.GetProperty("tokens").GetInt64());
            Assert.Equal(1, fact.GetProperty("requests").GetInt64());

            // A further line past the cross does not write it again.
            onLine?.Invoke(Fixtures.AssistantUsage("msg-2", input: 10, output: 10));
            Assert.Equal(1_100_000, System.Text.Json.JsonDocument.Parse(File.ReadAllText(clamp)).RootElement.GetProperty("tokens").GetInt64());

            onLine?.Invoke(Fixtures.Result(said: "```work-log\nDid a thing\n\nIn detail.\n```"));
            return Task.FromResult(new SessionResult(0, ""));
        };

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        await h.Runtime.Increment().RunAsync(work, h.Root, "opus", "high", quiet: false, claim, default);

        Assert.NotNull(clamp);
        await claim.ReleaseAsync();
    }

    // ---- HA-223: the budget file, for a --quiet session's own hook to read ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_playbook_with_a_budget_gets_the_budget_file_before_the_quiet_streamed_split(bool quiet)
    {
        using var h = new Harness();
        var token = Guid.NewGuid();
        var (claim, _) = await HoldingAsync(h, "AER-1", token);
        var work = Fixtures.Work("AER-1") with { Playbook = Fixtures.Playbook(budget: 2) };
        string? budgetPath = null;

        h.Sessions.Behaviour = (request, onLine, _) =>
        {
            budgetPath = Path.Combine(Path.GetDirectoryName(request.HookSettings)!, "budget");
            Assert.True(File.Exists(budgetPath));
            Assert.Equal("2000000", File.ReadAllText(budgetPath));

            onLine?.Invoke(Fixtures.Result(said: "```work-log\nDid a thing\n\nIn detail.\n```"));
            return Task.FromResult(new SessionResult(0, Fixtures.Result(said: "```work-log\nDid a thing\n\nIn detail.\n```")));
        };

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        await h.Runtime.Increment().RunAsync(work, h.Root, "opus", "high", quiet, claim, default);

        Assert.NotNull(budgetPath);
        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task A_playbook_with_no_budget_never_gets_a_budget_file()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();
        var (claim, _) = await HoldingAsync(h, "AER-1", token);
        var work = Fixtures.Work("AER-1") with { Playbook = Fixtures.Playbook(budget: null) };
        string? budgetPath = null;

        h.Sessions.Behaviour = (request, onLine, _) =>
        {
            budgetPath = Path.Combine(Path.GetDirectoryName(request.HookSettings)!, "budget");
            Assert.False(File.Exists(budgetPath));

            onLine?.Invoke(Fixtures.Result(said: "```work-log\nDid a thing\n\nIn detail.\n```"));
            return Task.FromResult(new SessionResult(0, ""));
        };

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        await h.Runtime.Increment().RunAsync(work, h.Root, "opus", "high", quiet: false, claim, default);

        Assert.NotNull(budgetPath);
        await claim.ReleaseAsync();
    }

    // ---- HA-222: the clamp ----

    [Theory]
    [InlineData(null)]
    [InlineData(10)]
    public async Task A_run_that_never_reaches_its_budget_or_has_none_never_writes_the_clamp_file(int? budget)
    {
        using var h = new Harness();
        var token = Guid.NewGuid();
        var (claim, _) = await HoldingAsync(h, "AER-1", token);
        var work = Fixtures.Work("AER-1") with { Playbook = Fixtures.Playbook(budget: budget) };
        string? clamp = null;

        h.Sessions.Behaviour = (request, onLine, _) =>
        {
            clamp = Path.Combine(Path.GetDirectoryName(request.HookSettings)!, "clamp.json");

            onLine?.Invoke(Fixtures.AssistantUsage("msg-1", input: 10, output: 20));
            Assert.False(File.Exists(clamp));

            onLine?.Invoke(Fixtures.Result(said: "```work-log\nDid a thing\n\nIn detail.\n```"));
            return Task.FromResult(new SessionResult(0, ""));
        };

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        await h.Runtime.Increment().RunAsync(work, h.Root, "opus", "high", quiet: false, claim, default);

        Assert.NotNull(clamp);
        Assert.False(File.Exists(clamp));
        await claim.ReleaseAsync();
    }

    // ---- HA-335: the hard limit ----

    [Fact]
    public async Task Crossing_the_hard_limit_stops_the_streamed_session_and_reports_it()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();
        var (claim, _) = await HoldingAsync(h, "AER-1", token);
        var work = Fixtures.Work("AER-1") with { Playbook = Fixtures.Playbook(budget: 5) };
        CancellationToken ct = default;

        h.Sessions.Behaviour = (_, onLine, sessionCt) =>
        {
            ct = sessionCt;

            // 7,000,000 + 500,000 = 7,500,000 tokens, exactly the hard limit a
            // 5M-token budget carries (budget * 3 / 2).
            onLine?.Invoke(Fixtures.AssistantUsage("msg-1", input: 7_000_000, output: 500_000));
            Assert.True(ct.IsCancellationRequested);
            Assert.Contains(h.Say.Said, l => l.Contains("the hard limit was hit at 7.5M tokens; stopping the session", StringComparison.Ordinal));

            onLine?.Invoke(Fixtures.Result(said: "```work-log\nDid a thing\n\nIn detail.\n```"));
            return Task.FromResult(new SessionResult(0, ""));
        };

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        var report = await h.Runtime.Increment().RunAsync(work, h.Root, "opus", "high", quiet: false, claim, default);

        Assert.True(report.HardLimited);
        Assert.Equal(7_500_000, report.HardLimitedAtTokens);
        Assert.Equal(1, report.HardLimitedAtRequests);

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task The_hard_stop_fires_once_not_on_every_further_line_past_the_cross()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();
        var (claim, _) = await HoldingAsync(h, "AER-1", token);
        var work = Fixtures.Work("AER-1") with { Playbook = Fixtures.Playbook(budget: 5) };

        h.Sessions.Behaviour = (_, onLine, _) =>
        {
            onLine?.Invoke(Fixtures.AssistantUsage("msg-1", input: 7_000_000, output: 500_000));
            Assert.Equal(1, h.Say.Said.Count(l => l.Contains("the hard limit was hit", StringComparison.Ordinal)));

            // A further line past the cross neither fires the line again nor
            // moves the recorded fact.
            onLine?.Invoke(Fixtures.AssistantUsage("msg-2", input: 10, output: 10));
            Assert.Equal(1, h.Say.Said.Count(l => l.Contains("the hard limit was hit", StringComparison.Ordinal)));

            onLine?.Invoke(Fixtures.Result(said: "```work-log\nDid a thing\n\nIn detail.\n```"));
            return Task.FromResult(new SessionResult(0, ""));
        };

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        var report = await h.Runtime.Increment().RunAsync(work, h.Root, "opus", "high", quiet: false, claim, default);

        Assert.Equal(7_500_000, report.HardLimitedAtTokens);
        await claim.ReleaseAsync();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(10)]
    public async Task A_run_that_never_reaches_15x_its_budget_or_has_none_never_sets_HardLimited(int? budget)
    {
        using var h = new Harness();
        var token = Guid.NewGuid();
        var (claim, _) = await HoldingAsync(h, "AER-1", token);
        var work = Fixtures.Work("AER-1") with { Playbook = Fixtures.Playbook(budget: budget) };

        h.Sessions.Behaviour = (_, onLine, _) =>
        {
            onLine?.Invoke(Fixtures.AssistantUsage("msg-1", input: 10, output: 20));
            onLine?.Invoke(Fixtures.Result(said: "```work-log\nDid a thing\n\nIn detail.\n```"));
            return Task.FromResult(new SessionResult(0, ""));
        };

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        var report = await h.Runtime.Increment().RunAsync(work, h.Root, "opus", "high", quiet: false, claim, default);

        Assert.False(report.HardLimited);
        Assert.Null(report.HardLimitedAtTokens);

        await claim.ReleaseAsync();
    }

    [Fact]
    public void HardLimited_reads_as_its_own_sentence_whether_or_not_anything_was_filed()
    {
        var withoutFiling = new IncrementReport { Key = "AER-1", From = "In Progress", To = "In Review", HardLimitedAtTokens = 7_500_000 };
        Assert.Equal("the hard limit was hit at 7.5M tokens, its continuation could not be filed", withoutFiling.Outcome);

        var withFiling = new IncrementReport
        {
            Key = "AER-1", From = "In Progress", To = "In Review", HardLimitedAtTokens = 7_500_000, Filed = ["AER-2"],
        };
        Assert.Equal("the hard limit was hit at 7.5M tokens, filed 1 under it", withFiling.Outcome);
    }

    [Fact]
    public async Task A_hard_limited_report_does_not_enter_the_stall_flagging_path_even_on_a_second_stall_in_a_row()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());
        var work = Fixtures.Work("AER-1", letGo: 1) with { Playbook = Fixtures.Playbook(budget: 5) };

        h.Sessions.Behaviour = (_, onLine, _) =>
        {
            onLine?.Invoke(Fixtures.AssistantUsage("msg-1", input: 7_000_000, output: 500_000));
            onLine?.Invoke(Fixtures.Result(said: "```work-log\nDid a thing\n\nIn detail.\n```"));
            return Task.FromResult(new SessionResult(0, ""));
        };

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", letGo: 1, children: []));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/comments",
            new CommentDto(1, "hatch", "…", "comment", null, null, DateTimeOffset.UnixEpoch));

        var report = await h.Runtime.Increment().RunAsync(work, h.Root, "opus", "high", quiet: false, claim, default);

        Assert.True(report.Stalled);
        Assert.True(report.HardLimited);

        // The clamp's own comment still goes up - this task leaves that alone -
        // but the stall-flagging path that a second stall in a row would
        // otherwise take does not: no question, and no comment about the
        // ticket being left where it was found.
        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-1/questions"));
        Assert.DoesNotContain(h.Wire.To("POST", "/api/hatch/issues/AER-1/comments"),
            c => c.Body.Contains("in a row to leave this ticket here", StringComparison.Ordinal));

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task A_quiet_run_posts_null_requests_and_peak_context_but_a_known_prompt_length()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();
        var (claim, _) = await HoldingAsync(h, "AER-1", token);
        var work = Fixtures.Work("AER-1");

        h.Sessions.Behaviour = (_, _, _) =>
            Task.FromResult(new SessionResult(0, Fixtures.Result(said: "```work-log\nDid a thing\n\nIn detail.\n```")));

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        await h.Runtime.Increment().RunAsync(work, h.Root, "opus", "high", quiet: true, claim, default);

        var billed = h.Wire.To("POST", "/api/hatch/issues/AER-1/work-log")[0].Read<WorkLogEntryRequest>();
        Assert.Null(billed.Requests);
        Assert.Null(billed.PeakContextTokens);
        Assert.Equal(Prompt.Compose(work).Length, billed.PromptChars);

        await claim.ReleaseAsync();
    }

    /// <summary>
    /// HA-116: a minute of Hatch not answering costs nothing. Two blips on the
    /// post-session read - a 503 and a 502 - are ridden out inside
    /// <see cref="HatchClient.Send"/> itself, so the increment never even sees
    /// a failure: it reads as moved, with no stall comment and no flag, exactly
    /// as if the outage had never happened.
    /// </summary>
    [Fact]
    public async Task A_post_session_read_that_fails_twice_then_answers_still_reads_as_moved()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var h = new Harness(clock: clock);
        var token = Guid.NewGuid();
        var (claim, _) = await HoldingAsync(h, "AER-1", token);

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Once("GET", "/api/hatch/work/AER-1", HttpStatusCode.ServiceUnavailable);
        h.Wire.Once("GET", "/api/hatch/work/AER-1", HttpStatusCode.BadGateway);
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        var running = h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        // Advances the fake clock past the backoff the two blips cost - real
        // milliseconds spent polling so the call's own seconds never have to
        // be.
        for (var i = 0; i < 10 && !running.IsCompleted; i++)
        {
            await Task.Delay(5);
            clock.Advance(TimeSpan.FromSeconds(15));
        }

        var report = await running;

        Assert.True(report.Moved);
        Assert.Equal("In Progress -> In Review", report.Outcome);
        Assert.Null(report.Flag);
        Assert.Equal(3, h.Wire.Count("GET", "/api/hatch/work/AER-1"));

        // A stall comment here would flag another runner's increment as ours,
        // exactly as it does today for a read that answers on the first try -
        // there is nothing here for anybody to see.
        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-1/comments"));

        await claim.ReleaseAsync();
    }

    /// <summary>
    /// HA-118: the first increment in a row to leave a ticket where it found
    /// it is let go quietly - one comment, no question - rather than flagged.
    /// Most of the time whatever happened is weather, and a retry a few
    /// minutes later just works, so the ticket goes straight back onto the
    /// board rather than costing a person a trip to answer a question about it.
    /// </summary>
    [Fact]
    public async Task A_ticket_that_did_not_move_is_let_go_the_first_time()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/comments",
            new CommentDto(1, "hatch", "…", "comment", null, null, DateTimeOffset.UnixEpoch));

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1", letGo: 0), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.True(report.Stalled);
        Assert.True(report.LetGo);
        Assert.Equal("let go", report.Flag);
        Assert.Equal(ClaimOutcomes.Dropped, report.ReleaseOutcome);

        // One comment naming the session, and no question: the ticket is free
        // for the very next pass to try again.
        var written = h.Wire.To("POST", "/api/hatch/issues/AER-1/comments");
        Assert.Single(written);
        Assert.Contains("let it go", written[0].Read<CommentCreateRequest>().Body, StringComparison.Ordinal);
        Assert.Contains("the session ended without moving it", written[0].Read<CommentCreateRequest>().Body, StringComparison.Ordinal);
        Assert.Contains("claude --resume s-1", written[0].Read<CommentCreateRequest>().Body, StringComparison.Ordinal);
        Assert.Null(written[0].Read<CommentCreateRequest>().Kind);

        await claim.ReleaseAsync();
    }

    /// <summary>The second increment in a row to do the same is flagged exactly as every stall used to be.</summary>
    [Fact]
    public async Task A_ticket_that_did_not_move_a_second_time_in_a_row_is_flagged_where_a_person_will_see_it()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", letGo: 1));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/comments",
            new CommentDto(1, "hatch", "…", "comment", null, null, DateTimeOffset.UnixEpoch));
        h.Wire.Json("PUT", "/api/hatch/issues/AER-1/stall", Fixtures.Issue("AER-1"));

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1", letGo: 1), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.True(report.Stalled);
        Assert.False(report.LetGo);
        Assert.Equal("flagged", report.Flag);
        Assert.Equal(ClaimOutcomes.Dropped, report.ReleaseOutcome);

        // A comment naming the session and mentioning this is the second
        // increment in a row, and the mark that actually stops the next pass
        // spending the same money the same way.
        var written = Assert.Single(h.Wire.To("POST", "/api/hatch/issues/AER-1/comments"));
        Assert.Contains("second increment in a row", written.Read<CommentCreateRequest>().Body, StringComparison.Ordinal);
        Assert.Contains("claude --resume s-1", written.Read<CommentCreateRequest>().Body, StringComparison.Ordinal);

        var marked = Assert.Single(h.Wire.To("PUT", "/api/hatch/issues/AER-1/stall"));
        Assert.Contains("without moving it", marked.Read<StallRequest>().Why, StringComparison.Ordinal);

        await claim.ReleaseAsync();
    }

    /// <summary>
    /// HA-962: a plain increment's own board-read verdict does not know what
    /// the session actually said - a session that wrote no work-log title is
    /// stalled even when the board shows the ticket moved, and the comment
    /// names the move rather than claiming it was left in place.
    /// </summary>
    [Fact]
    public async Task A_session_with_no_work_log_title_is_stalled_even_though_the_board_shows_it_moved()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Sessions.Behaviour = (_, onLine, _) =>
        {
            onLine?.Invoke(Fixtures.Result(said: "no fence here"));
            return Task.FromResult(new SessionResult(0, ""));
        };

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", letGo: 1, from: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/comments",
            new CommentDto(1, "hatch", "…", "comment", null, null, DateTimeOffset.UnixEpoch));
        h.Wire.Json("PUT", "/api/hatch/issues/AER-1/stall", Fixtures.Issue("AER-1"));

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1", letGo: 1), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.True(report.Stalled);
        Assert.False(report.Moved);

        var written = Assert.Single(h.Wire.To("POST", "/api/hatch/issues/AER-1/comments"));
        var body = written.Read<CommentCreateRequest>().Body;
        Assert.Contains("wrote no work-log", body, StringComparison.Ordinal);
        Assert.Contains("moved it from", body, StringComparison.Ordinal);
        Assert.DoesNotContain("where it found it", body, StringComparison.Ordinal);

        var marked = Assert.Single(h.Wire.To("PUT", "/api/hatch/issues/AER-1/stall"));
        Assert.Contains("wrote no work-log", marked.Read<StallRequest>().Why, StringComparison.Ordinal);

        await claim.ReleaseAsync();
    }

    /// <summary>
    /// HA-962: the same override, when the board shows a child filed instead
    /// of a move - the comment names the filed child rather than claiming
    /// nothing happened.
    /// </summary>
    [Fact]
    public async Task A_session_with_no_work_log_title_is_stalled_even_though_the_board_shows_a_child_filed()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Sessions.Behaviour = (_, onLine, _) =>
        {
            onLine?.Invoke(Fixtures.Result(said: "no fence here"));
            return Task.FromResult(new SessionResult(0, ""));
        };

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1",
            Fixtures.Work("AER-1", letGo: 1, children: [Fixtures.Card("AER-2")]));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/comments",
            new CommentDto(1, "hatch", "…", "comment", null, null, DateTimeOffset.UnixEpoch));
        h.Wire.Json("PUT", "/api/hatch/issues/AER-1/stall", Fixtures.Issue("AER-1"));

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1", letGo: 1), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.True(report.Stalled);
        Assert.Single(report.Filed);

        var written = Assert.Single(h.Wire.To("POST", "/api/hatch/issues/AER-1/comments"));
        var body = written.Read<CommentCreateRequest>().Body;
        Assert.Contains("wrote no work-log", body, StringComparison.Ordinal);
        Assert.Contains("filed 1 under it", body, StringComparison.Ordinal);
        Assert.DoesNotContain("where it found it", body, StringComparison.Ordinal);

        var marked = Assert.Single(h.Wire.To("PUT", "/api/hatch/issues/AER-1/stall"));
        Assert.Contains("wrote no work-log", marked.Read<StallRequest>().Why, StringComparison.Ordinal);

        await claim.ReleaseAsync();
    }

    /// <summary>
    /// HA-962: a result event that came back <c>is_error</c> is stalled even
    /// when it carried a work-log title - the title does not buy back trust
    /// in the session's own account once the result says it errored.
    /// </summary>
    [Fact]
    public async Task A_titled_result_that_errored_is_stalled_even_though_the_board_shows_it_moved()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Sessions.Behaviour = (_, onLine, _) =>
        {
            onLine?.Invoke(Fixtures.Result(said: "```work-log\nDid a thing\n\nIn detail.\n```", error: true));
            return Task.FromResult(new SessionResult(1, ""));
        };

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", letGo: 1, from: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/comments",
            new CommentDto(1, "hatch", "…", "comment", null, null, DateTimeOffset.UnixEpoch));
        h.Wire.Json("PUT", "/api/hatch/issues/AER-1/stall", Fixtures.Issue("AER-1"));

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1", letGo: 1), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.True(report.Stalled);
        Assert.False(report.Moved);

        var written = Assert.Single(h.Wire.To("POST", "/api/hatch/issues/AER-1/comments"));
        var body = written.Read<CommentCreateRequest>().Body;
        Assert.Contains("ended with an error", body, StringComparison.Ordinal);

        var marked = Assert.Single(h.Wire.To("PUT", "/api/hatch/issues/AER-1/stall"));
        Assert.Contains("ended with an error", marked.Read<StallRequest>().Why, StringComparison.Ordinal);

        await claim.ReleaseAsync();
    }

    /// <summary>
    /// HA-127: a ticket that did not move but gained children it did not have
    /// at dispatch is not a stall - it is progress, measured against the
    /// dispatch's own children. Neither let-go nor flagged, and no comment at
    /// all: the new keys speak for themselves.
    /// </summary>
    [Fact]
    public async Task A_ticket_that_did_not_move_but_filed_children_is_not_a_stall()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1",
            Fixtures.Work("AER-1", children: [Fixtures.Card("AER-2"), Fixtures.Card("AER-3")]));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.Equal(["AER-2", "AER-3"], report.Filed);
        Assert.Equal("filed 2 under it", report.Outcome);
        Assert.False(report.Stalled);
        Assert.False(report.LetGo);
        Assert.Null(report.Flag);
        Assert.Equal(ClaimOutcomes.Worked, report.ReleaseOutcome);

        // Neither a stall comment nor a question - filing is progress, and
        // nothing here needs a person's eye.
        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-1/comments"));

        await claim.ReleaseAsync();
    }

    /// <summary>
    /// HA-224: a session that writes <see cref="SessionHooks.Clamp"/> - whichever
    /// hook or callback did it - is read back onto the report once the session
    /// ends, the ticket is told about it in one comment, and the outcome reads
    /// as clamped rather than a plain "filed N under it".
    /// </summary>
    [Fact]
    public async Task A_clamped_session_that_filed_work_but_did_not_move_reports_clamped_and_comments_once()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Sessions.Behaviour = (request, onLine, _) =>
        {
            var clamp = Path.Combine(Path.GetDirectoryName(request.HookSettings)!, "clamp.json");
            File.WriteAllText(clamp, """{"tokens":820000,"requests":37}""");

            onLine?.Invoke(Fixtures.Result(said: "```work-log\nDid a thing\n\nIn detail.\n```"));
            return Task.FromResult(new SessionResult(0, ""));
        };

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1",
            Fixtures.Work("AER-1", children: [Fixtures.Card("AER-2"), Fixtures.Card("AER-3")]));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/comments",
            new CommentDto(1, "hatch", "…", "comment", null, null, DateTimeOffset.UnixEpoch));

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.True(report.Clamped);
        Assert.Equal(820_000, report.ClampedAtTokens);
        Assert.Equal(37, report.ClampedAtRequests);
        Assert.Equal(["AER-2", "AER-3"], report.Filed);
        Assert.Equal("clamped at 820k tokens, filed 2 under it", report.Outcome);
        Assert.False(report.Stalled);

        var written = h.Wire.To("POST", "/api/hatch/issues/AER-1/comments");
        Assert.Single(written);
        var body = written[0].Read<CommentCreateRequest>().Body;
        Assert.Contains("820k", body, StringComparison.Ordinal);
        Assert.Contains("37", body, StringComparison.Ordinal);

        await claim.ReleaseAsync();
    }

    /// <summary>
    /// HA-224, HA-127's own regression anchor: filed work with no clamp still
    /// reads exactly as it did before the clamp existed.
    /// </summary>
    [Fact]
    public async Task A_ticket_that_filed_work_without_a_clamp_reads_as_it_always_did()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1",
            Fixtures.Work("AER-1", children: [Fixtures.Card("AER-2"), Fixtures.Card("AER-3")]));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.False(report.Clamped);
        Assert.Equal(["AER-2", "AER-3"], report.Filed);
        Assert.Equal("filed 2 under it", report.Outcome);
        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-1/comments"));

        await claim.ReleaseAsync();
    }

    /// <summary>A child present at dispatch and gone by the after-read is not filed - only new keys count.</summary>
    [Fact]
    public async Task A_child_that_disappeared_does_not_count_as_filed()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", letGo: 0, children: []));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/comments",
            new CommentDto(1, "hatch", "…", "comment", null, null, DateTimeOffset.UnixEpoch));

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1", letGo: 0, children: [Fixtures.Card("AER-2")]), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.Empty(report.Filed);
        Assert.True(report.Stalled);
        Assert.True(report.LetGo);

        await claim.ReleaseAsync();
    }

    /// <summary>A ticket that moved and also filed children reads its arrow, not the filed phrase - Moved wins.</summary>
    [Fact]
    public async Task A_ticket_that_moved_and_filed_children_reads_the_arrow()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1",
            Fixtures.Work("AER-1", from: "In Review", children: [Fixtures.Card("AER-2")]));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.True(report.Moved);
        Assert.Single(report.Filed);
        Assert.Equal("In Progress -> In Review", report.Outcome);

        await claim.ReleaseAsync();
    }

    /// <summary>A conflict increment is still judged by its branch - filing a new child changes nothing there.</summary>
    [Fact]
    public async Task A_conflict_increment_that_still_conflicts_is_stalled_even_if_it_filed_a_child()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        var trunk = new string('a', 40);
        var tip = new string('b', 40);
        var verdict = new Verdict(MergeVerdicts.Conflicted, "main", trunk, "aer-1-thing", tip, ["a.txt"]);
        var found = new Rechecked([new Checked("/checkouts/repo0", verdict)], Unknown: false, Reported: true);
        var conflict = new ConflictRun(found, _ => Task.FromResult(found));

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1",
            Fixtures.Work("AER-1", from: "In Review", children: [Fixtures.Card("AER-2")]));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/comments", Fixtures.Comment());

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.ConflictWork("AER-1", letGo: 1), h.Root, "opus", "high", quiet: false, claim, default, conflict: conflict);

        Assert.True(report.Stalled);

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task A_ticket_already_waiting_on_a_question_gets_the_comment_and_not_a_second_question()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        // The question is already open before the increment starts, so the
        // session did not ask it - it is the second increment in a row to
        // leave the ticket where it was, which is what puts this on the
        // flagged path rather than a quiet let-go.
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", letGo: 1, questions: [Fixtures.Question(42)]));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", new[] { Fixtures.Question(42) });
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/comments",
            new CommentDto(1, "hatch", "…", "comment", null, null, DateTimeOffset.UnixEpoch));

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1", letGo: 1, questions: [Fixtures.Question(42)]), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.Equal("waiting on a question", report.Flag);
        Assert.Equal(ClaimOutcomes.Dropped, report.ReleaseOutcome);
        Assert.Single(h.Wire.To("POST", "/api/hatch/issues/AER-1/comments"));

        // The session raised no question of its own this time - the one open
        // is the same one that was already there.
        Assert.Equal(0, report.Asked);

        await claim.ReleaseAsync();
    }

    /// <summary>
    /// The session asked its own question on the way out: that already does
    /// everything a stall comment would, so nothing further is written and the
    /// claim reads as having worked rather than been dropped.
    /// </summary>
    [Fact]
    public async Task A_session_that_asked_its_own_question_is_not_let_go_or_flagged()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", new[] { Fixtures.Question(42) });

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.Equal(1, report.Asked);
        Assert.False(report.LetGo);
        Assert.Null(report.Flag);
        Assert.Equal(ClaimOutcomes.Worked, report.ReleaseOutcome);
        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-1/comments"));

        await claim.ReleaseAsync();
    }

    /// <summary>An erroring session's let-go comment quotes its own result text.</summary>
    [Fact]
    public async Task An_erroring_sessions_let_go_comment_quotes_its_result_text()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Sessions.Behaviour = (_, onLine, _) =>
        {
            onLine?.Invoke(Fixtures.Init());
            onLine?.Invoke(Fixtures.Result(error: true, said: "the build failed on main"));
            return Task.FromResult(new SessionResult(1, ""));
        };

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/comments",
            new CommentDto(1, "hatch", "…", "comment", null, null, DateTimeOffset.UnixEpoch));

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.True(report.LetGo);
        Assert.Equal(ClaimOutcomes.Dropped, report.ReleaseOutcome);

        var body = h.Wire.To("POST", "/api/hatch/issues/AER-1/comments")[0].Read<CommentCreateRequest>().Body;
        Assert.Contains("the session ended with an error", body, StringComparison.Ordinal);
        Assert.Contains("the build failed on main", body, StringComparison.Ordinal);

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task A_usage_limit_writes_no_stall_comment_and_asks_nothing()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Sessions.Behaviour = (_, onLine, _) =>
        {
            onLine?.Invoke(Fixtures.Init());
            onLine?.Invoke(Fixtures.Result(
                error: true, said: "You've hit your session limit · resets 7:40pm (America/New_York)"));
            return Task.FromResult(new SessionResult(1, ""));
        };

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.True(report.UsageLimited);
        Assert.True(report.UsageLimitResetKnown);
        Assert.False(report.Moved);
        Assert.Null(report.Flag);
        Assert.Equal(0, report.Asked);
        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-1/comments"));
        Assert.Contains(h.Say.Said, l => l.Contains("out of Claude usage", StringComparison.Ordinal));

        await claim.ReleaseAsync();
    }

    /// <summary>
    /// HA-1108: the stream's own <c>rate_limit_event</c> carries the account's
    /// own reset instant, epoch seconds with nothing to parse - and when its
    /// window matches the one the sentence named, it wins over the sentence's
    /// own (zone-parsed) reading. A LiveZoneClock, the same as HA-1107's probe
    /// test, so the runner's own zone is something other than UTC and the
    /// conversion has something to prove.
    /// </summary>
    [Fact]
    public async Task A_weekly_sentence_takes_a_seven_day_events_instant()
    {
        var offset = TimeSpan.FromHours(-4);
        using var h = new Harness(clock: new LiveZoneClock(offset));
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        var streamResetAt = DateTimeOffset.UtcNow.AddDays(3);

        h.Sessions.Behaviour = (_, onLine, _) =>
        {
            onLine?.Invoke(Fixtures.Init());
            onLine?.Invoke(Fixtures.RateLimitEvent(("seven_day", 1.0, streamResetAt.ToUnixTimeSeconds())));
            onLine?.Invoke(Fixtures.Result(
                error: true, said: "You've hit your weekly limit · resets Dec 25, 2026 at 11:40pm (UTC)"));
            return Task.FromResult(new SessionResult(1, ""));
        };

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.True(report.UsageLimited);
        Assert.Equal("weekly", report.UsageLimitWindow);
        Assert.Equal(streamResetAt.ToUnixTimeSeconds(), report.UsageLimitResetAt!.Value.ToUnixTimeSeconds());
        Assert.Equal(offset, report.UsageLimitResetAt!.Value.Offset);
        Assert.True(report.UsageLimitResetKnown);
        Assert.False(report.UsageLimitPassed);

        await claim.ReleaseAsync();
    }

    /// <summary>
    /// The same weekly sentence as above, but the stream's latest event only
    /// carries a <c>five_hour</c> window - "session" once <see cref="StreamRender"/>
    /// renames it - which does not match the sentence's "weekly". With no
    /// window of its own to correct from, the sentence's own reading stands
    /// unchanged.
    /// </summary>
    [Fact]
    public async Task A_weekly_sentence_does_not_take_a_five_hour_only_events_instant()
    {
        using var h = new Harness(clock: new LiveZoneClock(TimeSpan.FromHours(-4)));
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        var streamResetAt = DateTimeOffset.UtcNow.AddHours(2);

        h.Sessions.Behaviour = (_, onLine, _) =>
        {
            onLine?.Invoke(Fixtures.Init());
            onLine?.Invoke(Fixtures.RateLimitEvent(("five_hour", 1.0, streamResetAt.ToUnixTimeSeconds())));
            onLine?.Invoke(Fixtures.Result(
                error: true, said: "You've hit your weekly limit · resets Dec 25, 2026 at 11:40pm (UTC)"));
            return Task.FromResult(new SessionResult(1, ""));
        };

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.True(report.UsageLimited);
        Assert.Equal("weekly", report.UsageLimitWindow);
        Assert.Equal(new DateTimeOffset(2026, 12, 25, 23, 40, 0, TimeSpan.Zero), report.UsageLimitResetAt);
        Assert.True(report.UsageLimitResetKnown);
        Assert.False(report.UsageLimitPassed);

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task An_ordinary_error_still_stalls_rather_than_reading_as_a_usage_limit()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Sessions.Behaviour = (_, onLine, _) =>
        {
            onLine?.Invoke(Fixtures.Init());
            onLine?.Invoke(Fixtures.Result(error: true, said: "the build failed on main"));
            return Task.FromResult(new SessionResult(1, ""));
        };

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/comments",
            new CommentDto(1, "hatch", "…", "comment", null, null, DateTimeOffset.UnixEpoch));

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.False(report.UsageLimited);
        Assert.True(report.Stalled);
        Assert.Equal("let go", report.Flag);

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task A_quoted_limit_sentence_in_an_ordinary_successful_run_is_not_mistaken_for_one()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Sessions.Behaviour = (_, onLine, _) =>
        {
            onLine?.Invoke(Fixtures.Init());
            // Talking about this very feature, not reporting a real limit -
            // and the run ends cleanly, exit 0, with no result event lost.
            onLine?.Invoke(
                """{"type":"assistant","message":{"content":[{"type":"text","text":"The sample sentence is You've hit your session limit · resets 7:40pm (America/New_York)"}]}}""");
            return Task.FromResult(new SessionResult(0, ""));
        };

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", to: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.False(report.UsageLimited);

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task A_crash_with_no_result_event_still_reads_a_real_limit_off_the_assistants_last_words()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Sessions.Behaviour = (_, onLine, _) =>
        {
            onLine?.Invoke(Fixtures.Init());
            // The stream is cut off before a "result" event ever arrives -
            // the one place a limit hit mid-stream still shows up.
            onLine?.Invoke(
                """{"type":"assistant","message":{"content":[{"type":"text","text":"You've hit your session limit · resets 7:40pm (America/New_York)"}]}}""");
            return Task.FromResult(new SessionResult(1, ""));
        };

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.True(report.UsageLimited);

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task A_refused_heartbeat_stops_the_session_and_writes_nothing_past_the_work_log()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.Conflict,
            "\"this claim was taken over\"");
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        h.Sessions.Behaviour = FakeSessions.UntilStopped();

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.True(report.LostLease);
        Assert.Contains("the claim was taken", report.Flag!, StringComparison.Ordinal);

        // The session was stopped rather than left spending money on a ticket
        // this runner no longer holds.
        Assert.Equal(143, report.ExitCode);

        // A stall comment here would flag another runner's increment as ours,
        // and the ticket did not move because we stopped.
        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-1/comments"));

        // The meter reading goes up either way: money was spent on that ticket,
        // and a bill is not a claim to have done the work. (This run was killed
        // before it could report, so there is nothing to post - and that too is
        // said out loud rather than posted as a row of zeros.)
        Assert.Contains(h.Say.Complained, l => l.Contains("no work log entry", StringComparison.Ordinal));

        await claim.ReleaseAsync();
    }

    /// <summary>
    /// HA-169: a preemption notice is not a lost lease - the runner still owns
    /// the ticket, so the board read still carries its token, and the work log
    /// is still posted, the direct opposite of the lost-lease test above.
    /// </summary>
    [Fact]
    public async Task A_preemption_notice_puts_the_ticket_down_without_losing_the_lease()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.OK,
            Fixtures.Preempted("AER-9", "Trunk is down"));
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        h.Sessions.Behaviour = async (_, onLine, ct) =>
        {
            onLine?.Invoke(Fixtures.Init());
            onLine?.Invoke(Fixtures.Result(said: "```work-log\nGot most of the way\n\nAnd then stopped.\n```"));

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
            catch (OperationCanceledException)
            {
                return new SessionResult(143, "");
            }

            return new SessionResult(0, "");
        };

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.True(report.Preempted);
        Assert.Equal("AER-9", report.PreemptedKey);
        Assert.Equal("Trunk is down", report.PreemptedTitle);
        Assert.False(report.LostLease);
        Assert.True(report.Stalled);
        Assert.Equal(ClaimOutcomes.Preempted, report.ReleaseOutcome);
        Assert.Equal(143, report.ExitCode);

        // This runner still owns the ticket, unlike a lost lease: the board
        // read carries its token.
        Assert.Contains("heldToken", h.Wire.To("GET", "/api/hatch/work/AER-1")[0].Query,
            StringComparison.OrdinalIgnoreCase);

        // Money was spent either way, the same as every other path through here.
        Assert.Single(h.Wire.To("POST", "/api/hatch/issues/AER-1/work-log"));

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task A_run_that_reported_its_bill_before_the_lease_went_still_posts_it()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.Conflict,
            "\"this claim has expired\"");
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        h.Sessions.Behaviour = async (_, onLine, ct) =>
        {
            onLine?.Invoke(Fixtures.Init());
            onLine?.Invoke(Fixtures.Result(said: "```work-log\nGot most of the way\n\nAnd then stopped.\n```"));

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
            catch (OperationCanceledException)
            {
                return new SessionResult(143, "");
            }

            return new SessionResult(0, "");
        };

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.True(report.LostLease);
        Assert.Single(h.Wire.To("POST", "/api/hatch/issues/AER-1/work-log"));
        Assert.Equal("Got most of the way",
            h.Wire.To("POST", "/api/hatch/issues/AER-1/work-log")[0].Read<WorkLogEntryRequest>().Title);

        // The board read after a lost lease presents no token: this runner is no
        // longer the holder, and asking as one would be a lie about a lease.
        Assert.DoesNotContain("heldToken", h.Wire.To("GET", "/api/hatch/work/AER-1")[0].Query,
            StringComparison.OrdinalIgnoreCase);

        await claim.ReleaseAsync();
    }

    private const string ReleaseNotePath = "/api/hatch/issues/AER-1/release-note";
    private const string WorkLogPath = "/api/hatch/issues/AER-1/work-log";
    private const string NoteAndLog = "```release-note\nShipped it.\n```\n```work-log\nDid a thing\n```";

    private static void SaysNoteAndLog(Harness h) =>
        h.Sessions.Behaviour = (_, onLine, _) =>
        {
            onLine?.Invoke(Fixtures.Init());
            onLine?.Invoke(Fixtures.Result(said: NoteAndLog));
            return Task.FromResult(new SessionResult(0, ""));
        };

    private static void StubTheBoard(Harness h)
    {
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", WorkLogPath, Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());
    }

    [Fact]
    public async Task The_release_note_is_posted_under_the_claim_before_the_work_log_row()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();
        var (claim, _) = await HoldingAsync(h, "AER-1", token);
        StubTheBoard(h);
        h.Wire.Json("PUT", ReleaseNotePath, Fixtures.Issue("AER-1"));
        SaysNoteAndLog(h);

        await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        var put = Assert.Single(h.Wire.To("PUT", ReleaseNotePath));
        Assert.Equal("Shipped it.", put.Read<ReleaseNoteRequest>().Note);
        Assert.Contains($"heldToken={token}", put.Query, StringComparison.OrdinalIgnoreCase);

        var calls = h.Wire.Calls.ToList();
        Assert.True(
            calls.FindIndex(c => c.Route == $"PUT {ReleaseNotePath}") < calls.FindIndex(c => c.Route == $"POST {WorkLogPath}"));
        Assert.True(h.Say.Mentions("release note"));

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task A_session_that_wrote_no_release_note_posts_none()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());
        StubTheBoard(h);

        await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.Empty(h.Wire.To("PUT", ReleaseNotePath));

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task A_release_note_is_not_posted_after_the_lease_went()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.Conflict,
            "\"this claim has expired\"");
        h.Wire.Json("POST", WorkLogPath, Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());
        h.Wire.Json("PUT", ReleaseNotePath, Fixtures.Issue("AER-1"));

        h.Sessions.Behaviour = async (_, onLine, ct) =>
        {
            onLine?.Invoke(Fixtures.Init());
            onLine?.Invoke(Fixtures.Result(said: NoteAndLog));

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
            catch (OperationCanceledException)
            {
                return new SessionResult(143, "");
            }

            return new SessionResult(0, "");
        };

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.True(report.LostLease);
        Assert.Single(h.Wire.To("POST", WorkLogPath));
        Assert.Empty(h.Wire.To("PUT", ReleaseNotePath));

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task A_refused_release_note_does_not_fail_the_increment()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());
        StubTheBoard(h);
        h.Wire.Reply("PUT", ReleaseNotePath, HttpStatusCode.Conflict, "\"this issue is not claimed\"");
        SaysNoteAndLog(h);

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.True(report.Moved);
        Assert.Single(h.Wire.To("POST", WorkLogPath));
        Assert.Contains(h.Say.Complained, l => l.Contains("release note", StringComparison.Ordinal));

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task The_line_the_session_is_inside_of_reaches_the_board()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        List<string?> Carried() => h.Wire.To("POST", "/api/hatch/issues/AER-1/claim/heartbeat")
            .Select(c => c.Read<ClaimHeartbeatRequest>().Chatter)
            .Where(c => c is not null)
            .ToList();

        static bool Says(List<string?> lines) =>
            lines.Any(c => c!.Contains("make test-api", StringComparison.Ordinal));

        h.Sessions.Behaviour = async (_, onLine, ct) =>
        {
            onLine?.Invoke(Fixtures.Init());
            onLine?.Invoke(Fixtures.ToolUse("Bash", "make test-api"));

            // Inside the tool use until a heartbeat has carried it, rather than
            // for a span long enough that one usually has - Harness.Eventually's
            // rule, which this test is the one place that was not keeping. The
            // beat is 25ms and the old wait was eight of them, so a loaded
            // machine that missed the window failed a test about whether the
            // line reaches the board at all. Bounded and not asserted here: the
            // assertion after the run is what judges it, and a failure there
            // reads as the missing line rather than as a crashed session.
            var waited = Stopwatch.StartNew();
            try
            {
                while (!Says(Carried()) && waited.ElapsedMilliseconds < 5_000)
                {
                    await Task.Delay(Harness.Beat, ct);
                }
            }
            catch (OperationCanceledException)
            {
                return new SessionResult(143, "");
            }

            onLine?.Invoke(Fixtures.Result());
            return new SessionResult(0, "");
        };

        await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.Contains(Carried(), c => c!.Contains("make test-api", StringComparison.Ordinal));

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task A_run_that_never_said_what_it_spent_posts_no_row_of_zeros()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        h.Sessions.Behaviour = (_, onLine, _) =>
        {
            onLine?.Invoke(Fixtures.Init());
            return Task.FromResult(new SessionResult(1, ""));
        };

        await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-1/work-log"));
        Assert.Contains(h.Say.Complained, l => l.Contains("no work log entry", StringComparison.Ordinal));

        await claim.ReleaseAsync();
    }
    // ---- Hearing a message sent while it works ----

    private static async Task<(IncrementReport Report, Claim Claim)> RunWithAsync(Harness h, WorkDto work)
    {
        var (claim, _) = await HoldingAsync(h, work.Issue.Key, Guid.NewGuid());
        h.Wire.Reply("POST", $"/api/hatch/issues/{work.Issue.Key}/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", $"/api/hatch/issues/{work.Issue.Key}/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", $"/api/hatch/work/{work.Issue.Key}", Fixtures.Work(work.Issue.Key, from: "In Review"));
        h.Wire.Json("GET", $"/api/hatch/issues/{work.Issue.Key}/questions", Array.Empty<QuestionDto>());

        var report = await h.Runtime.Increment().RunAsync(work, h.Root, "opus", "high", quiet: false, claim, default);
        return (report, claim);
    }

    [Fact]
    public async Task The_spawned_session_is_given_a_settings_file_outside_the_checkout_with_both_hooks()
    {
        using var h = new Harness();
        string? path = null, text = null;
        h.Sessions.Behaviour = (request, onLine, _) =>
        {
            path = request.HookSettings;
            text = path is null ? null : File.ReadAllText(path);
            onLine?.Invoke(Fixtures.Result(said: "```work-log\nDid a thing\n\nIn detail.\n```"));
            return Task.FromResult(new SessionResult(0, ""));
        };

        var (_, claim) = await RunWithAsync(h, Fixtures.Work("AER-1"));

        Assert.NotNull(path);
        Assert.False(path!.StartsWith(h.Root, StringComparison.Ordinal), "the settings file is inside the checkout");
        Assert.StartsWith(h.Temp, path, StringComparison.Ordinal);

        var hooks = System.Text.Json.JsonDocument.Parse(text!).RootElement.GetProperty("hooks");
        var step = hooks.GetProperty("PostToolUse")[0];
        Assert.Equal("*", step.GetProperty("matcher").GetString());
        var stepHook = step.GetProperty("hooks")[0];
        Assert.Equal("command", stepHook.GetProperty("type").GetString());
        Assert.Equal(10, stepHook.GetProperty("timeout").GetInt32());
        Assert.Contains("inbox \"AER-1\" --hook post-tool-use --stamp ", stepHook.GetProperty("command").GetString(), StringComparison.Ordinal);
        Assert.Contains(Path.Combine(Path.GetDirectoryName(path)!, "inbox.stamp"), stepHook.GetProperty("command").GetString(), StringComparison.Ordinal);
        Assert.Contains(Path.Combine(Path.GetDirectoryName(path)!, "clamp.json"), stepHook.GetProperty("command").GetString(), StringComparison.Ordinal);
        Assert.Contains(Path.Combine(Path.GetDirectoryName(path)!, "budget"), stepHook.GetProperty("command").GetString(), StringComparison.Ordinal);

        var stop = hooks.GetProperty("Stop")[0].GetProperty("hooks")[0];
        Assert.Equal(10, stop.GetProperty("timeout").GetInt32());
        Assert.Contains("inbox \"AER-1\" --hook stop", stop.GetProperty("command").GetString(), StringComparison.Ordinal);
        Assert.EndsWith(
            $"--clamp \"{Path.Combine(Path.GetDirectoryName(path)!, "clamp.json")}\" --budget \"{Path.Combine(Path.GetDirectoryName(path)!, "budget")}\"",
            stop.GetProperty("command").GetString(), StringComparison.Ordinal);

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task The_hooks_directory_is_gone_after_the_run_and_the_checkout_was_never_written_to()
    {
        using var h = new Harness();
        string? directory = null;
        h.Sessions.Behaviour = (request, onLine, _) =>
        {
            directory = Path.GetDirectoryName(request.HookSettings);
            Assert.True(Directory.Exists(directory));
            onLine?.Invoke(Fixtures.Result(said: "```work-log\nDid a thing\n\nIn detail.\n```"));
            return Task.FromResult(new SessionResult(0, ""));
        };
        var before = Directory.GetFileSystemEntries(h.Root, "*", SearchOption.AllDirectories).Order().ToList();

        var (_, claim) = await RunWithAsync(h, Fixtures.Work("AER-1"));

        Assert.NotNull(directory);
        Assert.False(Directory.Exists(directory));
        Assert.Equal(before, Directory.GetFileSystemEntries(h.Root, "*", SearchOption.AllDirectories).Order().ToList());

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task The_hooks_directory_is_gone_when_the_session_is_stopped_too()
    {
        using var h = new Harness();
        string? directory = null;
        h.Sessions.Behaviour = (request, onLine, ct) =>
        {
            directory = Path.GetDirectoryName(request.HookSettings);
            return FakeSessions.UntilStopped()(request, onLine, ct);
        };
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.Conflict, "\"taken\"");
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        await h.Runtime.Increment().RunAsync(Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.NotNull(directory);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task Unread_messages_are_marked_by_id_before_the_session_is_spawned()
    {
        using var h = new Harness();
        var spawnedAfter = -1;
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/messages/deliver", new[] { Fixtures.Message(7), Fixtures.Message(9) });
        h.Sessions.Behaviour = (_, onLine, _) =>
        {
            spawnedAfter = h.Wire.Count("POST", "/api/hatch/issues/AER-1/messages/deliver");
            onLine?.Invoke(Fixtures.Result(said: "```work-log\nDid a thing\n\nIn detail.\n```"));
            return Task.FromResult(new SessionResult(0, ""));
        };

        var (_, claim) = await RunWithAsync(
            h, Fixtures.Work("AER-1", messages: [Fixtures.Message(7), Fixtures.Message(9)]));

        Assert.Equal(1, spawnedAfter);
        var marked = Assert.Single(h.Wire.To("POST", "/api/hatch/issues/AER-1/messages/deliver")).Read<MessageDeliverRequest>();
        Assert.Equal([7L, 9L], marked.Ids);

        // And they were said in the prompt the session was handed.
        Assert.Contains("use the other table", h.Sessions.Spawned[0].Prompt, StringComparison.Ordinal);

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task A_dispatch_with_no_messages_marks_nothing()
    {
        using var h = new Harness();

        var (_, claim) = await RunWithAsync(h, Fixtures.Work("AER-1", messages: []));

        Assert.Equal(0, h.Wire.Count("POST", "/api/hatch/issues/AER-1/messages/deliver"));
        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task A_failure_to_mark_them_does_not_stop_the_spawn()
    {
        using var h = new Harness();
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/messages/deliver", HttpStatusCode.InternalServerError, "boom");

        var (report, claim) = await RunWithAsync(h, Fixtures.Work("AER-1", messages: [Fixtures.Message(7)]));

        Assert.Single(h.Sessions.Spawned);
        Assert.True(report.Moved);
        Assert.Contains(h.Say.Complained, l => l.Contains("could not mark", StringComparison.Ordinal));
        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task A_quiet_run_is_wired_the_same_way()
    {
        using var h = new Harness();
        h.Sessions.Behaviour = (request, _, _) =>
        {
            Assert.NotNull(request.HookSettings);
            return Task.FromResult(new SessionResult(0, ""));
        };
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        await h.Runtime.Increment().RunAsync(Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: true, claim, default);

        Assert.Single(h.Sessions.Spawned);
        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task A_quiet_run_that_crashed_without_a_final_object_still_reads_the_limit_off_its_raw_output()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        // Killed before it ever printed its one closing object - quiet mode's
        // only account of the run is whatever made it to stdout regardless.
        h.Sessions.Behaviour = (_, _, _) =>
            Task.FromResult(new SessionResult(1, "You've hit your session limit · resets 7:40pm (America/New_York)"));

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: true, claim, default);

        Assert.True(report.UsageLimited);

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task A_quiet_run_whose_output_is_a_stream_reads_every_line_and_keeps_the_later_facts()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        // The leading line is only there to make this read as JSON; the fake
        // reader draws nothing for it, the way a real stream has plenty of
        // events that draw nothing. Handed over whole, the blob starts with no
        // FAKE-RESULT and the fake reader answers with nothing at all.
        h.Sessions.Behaviour = (_, _, _) => Task.FromResult(new SessionResult(0,
            "{\"type\":\"ignored\"}\n" +
            "FAKE-RESULT fake-session-1|First|first detail|3|1.0|10000\n" +
            "\n" +
            "FAKE-RESULT fake-session-2|Second|second detail|5|2.0|20000"));

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        var report = await (h.Runtime with { Adapter = new FakeAdapter() }).Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: true, claim, default);

        Assert.Equal("fake-session-2", report.SessionId);
        Assert.Contains(h.Say.Said, l => l.Contains("done in 3 turns", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains("done in 5 turns", StringComparison.Ordinal));

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task An_output_last_message_file_wins_over_the_streams_last_text_on_a_crash()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        var lastMessage = Path.Combine(h.Temp, "last-message.txt");
        File.WriteAllText(lastMessage, "the CLI's own final answer\n");

        h.Sessions.Behaviour = (_, _, _) => Task.FromResult(new SessionResult(1, "stream cut off mid-sent"));

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        var adapter = new FakeAdapter { OutputLastMessageFile = lastMessage };
        await (h.Runtime with { Adapter = adapter }).Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: true, claim, default);

        Assert.Equal("the CLI's own final answer", adapter.LimitAskedAbout);

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task A_missing_output_last_message_file_leaves_the_streams_last_text_standing()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Sessions.Behaviour = (_, _, _) => Task.FromResult(new SessionResult(1, "stream cut off mid-sent"));

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        var adapter = new FakeAdapter { OutputLastMessageFile = Path.Combine(h.Temp, "never-written.txt") };
        await (h.Runtime with { Adapter = adapter }).Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: true, claim, default);

        Assert.Equal("stream cut off mid-sent", adapter.LimitAskedAbout);

        await claim.ReleaseAsync();
    }

    // ---- The keyboard's k (HA-133): a skip is not an interrupt ----

    [Fact]
    public async Task A_cancellation_of_ct_alone_with_the_callers_token_untouched_reads_as_a_skip()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        using var skip = new CancellationTokenSource();

        h.Sessions.Behaviour = (_, onLine, _) =>
        {
            onLine?.Invoke(Fixtures.Result(said: "```work-log\nDid a thing\n\nIn detail.\n```"));
            // The keyboard's k, confirmed - cancels the increment's own token
            // only, never the caller's, which is the whole distinction this
            // ticket draws.
            skip.Cancel();
            return Task.FromResult(new SessionResult(0, ""));
        };

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, skip.Token, callerCt: CancellationToken.None);

        Assert.True(report.Skipped);
        Assert.False(report.Interrupted);
        Assert.Equal("skipped from the keyboard", report.Outcome);
        Assert.NotEqual(default, report.EndedAt);

        await claim.ReleaseAsync();
    }

    /// <summary>The existing Ctrl-C behaviour, unchanged: both tokens cancelled (or no callerCt at all) still reads as interrupted.</summary>
    [Fact]
    public async Task A_cancellation_of_both_tokens_still_reads_as_interrupted_and_not_a_skip()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        using var stopped = new CancellationTokenSource();

        h.Sessions.Behaviour = (_, onLine, _) =>
        {
            onLine?.Invoke(Fixtures.Result(said: "```work-log\nDid a thing\n\nIn detail.\n```"));
            stopped.Cancel();
            return Task.FromResult(new SessionResult(0, ""));
        };

        // No callerCt given - defaults to ct itself, so the two can never
        // disagree, exactly as every call site before this ticket.
        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, stopped.Token);

        Assert.True(report.Interrupted);
        Assert.False(report.Skipped);

        await claim.ReleaseAsync();
    }

    // ---- SetIncrementRunning (HA-133) ----

    [Fact]
    public async Task SetIncrementRunning_RoundTripsFalseTrueFalseAroundTheSpawn()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());
        var controls = new Controls(keysOn: true);

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        Assert.False(controls.Snapshot().Confirming == Confirming.Skip);

        var runningDuringSpawn = false;
        h.Sessions.Behaviour = (_, onLine, _) =>
        {
            runningDuringSpawn = controls.Press('k').Log is null && controls.Snapshot().Confirming == Confirming.Skip;
            controls.Press('x'); // takes the confirmation back - leaves Confirming.None behind
            onLine?.Invoke(Fixtures.Result(said: "```work-log\nDid a thing\n\nIn detail.\n```"));
            return Task.FromResult(new SessionResult(0, ""));
        };

        var increment = new Increment(
            h.Board, h.Sessions, new ClaudeAdapter(),
            new Settings { Base = "https://hatch.example", Key = "hatch_ak_test" }, h.Say,
            [new CheckoutEntry(h.Root, "https://example.test/repo.git", Standing: true)], h.Runtime.Readout, h.Temp, controls,
            h.Workspace.For, h.Board);

        await increment.RunAsync(Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.True(runningDuringSpawn, "k did not arm the skip confirmation while the session was spawning");

        await claim.ReleaseAsync();
    }

    [Fact]
    public void The_hooks_run_this_binary_where_it_is_hatch_and_the_bare_word_otherwise()
    {
        Assert.Equal("/opt/hatch/hatch", SessionHooks.Binary("/opt/hatch/hatch"));
        Assert.Equal("hatch", SessionHooks.Binary("/usr/share/dotnet/dotnet"));
        Assert.Equal("hatch", SessionHooks.Binary(null));
    }

    [Fact]
    public void The_session_settings_carry_rtks_bash_hook_where_rtk_is_on_path()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hatch-rtk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, "temp");
        try
        {
            var file = Path.Combine(dir, OperatingSystem.IsWindows() ? "rtk.exe" : "rtk");
            File.WriteAllText(file, "");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var rtk = SessionHooks.Rtk(null, dir);
            Assert.Equal(Path.Combine(dir, OperatingSystem.IsWindows() ? "rtk.exe" : "rtk"), rtk);

            using var hooks = SessionHooks.Write(temp, "AR-8", "hatch", rtk);
            Assert.NotNull(hooks);

            using var settings = System.Text.Json.JsonDocument.Parse(File.ReadAllText(hooks.Settings));
            var pre = settings.RootElement.GetProperty("hooks").GetProperty("PreToolUse")[0];
            Assert.Equal("Bash", pre.GetProperty("matcher").GetString());
            Assert.Equal($"\"{rtk}\" hook claude", pre.GetProperty("hooks")[0].GetProperty("command").GetString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void The_session_settings_are_unchanged_where_rtk_is_absent_or_off()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hatch-rtk-{Guid.NewGuid():N}");
        var empty = Path.Combine(dir, "empty");
        Directory.CreateDirectory(empty);
        try
        {
            var file = Path.Combine(dir, OperatingSystem.IsWindows() ? "rtk.exe" : "rtk");
            File.WriteAllText(file, "");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            Assert.Null(SessionHooks.Rtk("off", dir));
            Assert.Null(SessionHooks.Rtk(" OFF ", dir));
            Assert.Null(SessionHooks.Rtk(null, empty));

            using var without = SessionHooks.Write(dir, "AR-8", "hatch", rtk: null);
            using var bare = SessionHooks.Write(dir, "AR-8", "hatch");
            Assert.NotNull(without);
            Assert.NotNull(bare);

            var a = File.ReadAllText(without.Settings).Replace(without.Directory.Replace("\\", "\\\\"), "<dir>");
            var b = File.ReadAllText(bare.Settings).Replace(bare.Directory.Replace("\\", "\\\\"), "<dir>");
            Assert.DoesNotContain("PreToolUse", a);
            Assert.Equal(a, b);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---- The spawn-time playbook-file read (HA-809) ----

    private const string PlaybookPath = ".hatch/playbooks.yaml";
    private static readonly string JudgedSha = new('a', 40);
    private static readonly string TrunkTip = new('b', 40);
    private static readonly byte[] PlaybookBytes = "playbooks: []"u8.ToArray();
    private static readonly string PlaybookHash = Convert.ToHexStringLower(SHA256.HashData(PlaybookBytes));

    private static async Task<(IncrementReport Report, Harness H)> RunPlaybookCheckAsync(
        PlaybookFileReferenceDto? reference, Action<Harness>? arrange = null)
    {
        var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());
        h.Wire.Json("PUT", "/api/hatch/playbook-files", Fixtures.PlaybookFile());

        arrange?.Invoke(h);

        var repositories = new[] { new Checkouts.RepositoryLine(h.Root, "git@example.test:o/r.git", true, null) };
        var work = Fixtures.Work("AER-1", playbookFile: reference);

        var report = await h.Runtime.Increment().RunAsync(
            work, h.Root, "opus", "high", quiet: false, claim, default,
            repositories: repositories, runnerName: h.Runtime.RunnerName);

        await claim.ReleaseAsync();
        return (report, h);
    }

    [Fact]
    public async Task The_spawn_time_read_matches_hash_and_spawns_as_today()
    {
        var (report, h) = await RunPlaybookCheckAsync(
            Fixtures.PlaybookFileReference(path: PlaybookPath, trunk: "main", sha: JudgedSha, hash: PlaybookHash),
            arrange: h =>
            {
                h.Workspace.HeadsFor[h.Root] = new RemoteHeads("main", new Dictionary<string, string> { ["main"] = TrunkTip });
                h.Workspace.BlobsFor[(h.Root, "main", PlaybookPath)] = new BlobRead(BlobOutcome.Found, PlaybookBytes, null);
            });
        using var _h = h;

        Assert.False(report.PlaybookStale);
        Assert.Equal(h.Runtime.RunnerName, Assert.Single(h.Sessions.Spawned).RunnerName);
        Assert.True(report.Moved);
        Assert.Single(h.Sessions.Spawned);
        Assert.Contains($"heads {h.Root}", h.Workspace.Calls);
        Assert.Contains($"fetch {h.Root}", h.Workspace.Calls);
        Assert.Contains($"read-blob {h.Root} main {PlaybookPath}", h.Workspace.Calls);
    }

    [Fact]
    public async Task The_spawn_time_read_refuses_a_moved_file_without_counting_a_stall()
    {
        var (report, h) = await RunPlaybookCheckAsync(
            Fixtures.PlaybookFileReference(path: PlaybookPath, trunk: "main", sha: JudgedSha, hash: "stale-hash"),
            arrange: h =>
            {
                h.Workspace.HeadsFor[h.Root] = new RemoteHeads("main", new Dictionary<string, string> { ["main"] = TrunkTip });
                h.Workspace.BlobsFor[(h.Root, "main", PlaybookPath)] = new BlobRead(BlobOutcome.Found, PlaybookBytes, null);
            });
        using var _h = h;

        Assert.True(report.PlaybookStale);
        Assert.Contains(PlaybookPath, report.Flag);
        Assert.Contains("main", report.Flag);
        Assert.Contains(JudgedSha, report.Flag);
        Assert.Contains(TrunkTip, report.Flag);
        Assert.Empty(h.Sessions.Spawned);
        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-1/work-log"));
        Assert.Empty(h.Wire.To("PUT", "/api/hatch/issues/AER-1/stall"));
        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-1/comments"));

        var put = Assert.Single(h.Wire.To("PUT", "/api/hatch/playbook-files"));
        var request = put.Read<PlaybookFileRequest>();
        Assert.Equal("git@example.test:o/r.git", request.Remote);
        Assert.Equal("main", request.Trunk);
        Assert.Equal(TrunkTip, request.Sha);
        Assert.Equal(Convert.ToBase64String(PlaybookBytes), request.Content);
        Assert.Null(request.Url);
        Assert.Equal(h.Runtime.RunnerName, request.Runner);
    }

    [Fact]
    public async Task The_spawn_time_read_compares_hashes_not_shas_so_a_moved_trunk_with_the_same_bytes_still_spawns()
    {
        var (report, h) = await RunPlaybookCheckAsync(
            Fixtures.PlaybookFileReference(path: PlaybookPath, trunk: "main", sha: JudgedSha, hash: PlaybookHash),
            arrange: h =>
            {
                // TrunkTip differs from JudgedSha, but the bytes behind it do not.
                h.Workspace.HeadsFor[h.Root] = new RemoteHeads("main", new Dictionary<string, string> { ["main"] = TrunkTip });
                h.Workspace.BlobsFor[(h.Root, "main", PlaybookPath)] = new BlobRead(BlobOutcome.Found, PlaybookBytes, null);
            });
        using var _h = h;

        Assert.False(report.PlaybookStale);
        Assert.Single(h.Sessions.Spawned);
        Assert.Empty(h.Wire.To("PUT", "/api/hatch/playbook-files"));
    }

    [Fact]
    public async Task The_spawn_time_read_refuses_when_the_file_is_missing_where_the_reference_expected_content()
    {
        var (report, h) = await RunPlaybookCheckAsync(
            Fixtures.PlaybookFileReference(path: PlaybookPath, trunk: "main", sha: JudgedSha, hash: PlaybookHash),
            arrange: h =>
            {
                h.Workspace.HeadsFor[h.Root] = new RemoteHeads("main", new Dictionary<string, string> { ["main"] = TrunkTip });
                // BlobsFor left unset: FakeWorkspace.ReadBlob defaults to BlobOutcome.Absent.
            });
        using var _h = h;

        Assert.True(report.PlaybookStale);
        Assert.Contains("is missing", report.Flag);
        Assert.Empty(h.Sessions.Spawned);

        var put = Assert.Single(h.Wire.To("PUT", "/api/hatch/playbook-files"));
        Assert.Null(put.Read<PlaybookFileRequest>().Content);
    }

    [Fact]
    public async Task The_spawn_time_read_spawns_anyway_when_origin_does_not_answer_the_heads_call()
    {
        var (report, h) = await RunPlaybookCheckAsync(
            Fixtures.PlaybookFileReference(path: PlaybookPath, trunk: "main", sha: JudgedSha, hash: PlaybookHash));
        // HeadsFor left unset: FakeWorkspace.Heads() defaults to null.
        using var _h = h;

        Assert.False(report.PlaybookStale);
        Assert.Single(h.Sessions.Spawned);
        Assert.Empty(h.Wire.To("PUT", "/api/hatch/playbook-files"));
        Assert.True(h.Say.Mentions("origin did not answer"));
    }

    [Fact]
    public async Task The_spawn_time_read_spawns_anyway_when_the_fetch_fails()
    {
        var (report, h) = await RunPlaybookCheckAsync(
            Fixtures.PlaybookFileReference(path: PlaybookPath, trunk: "main", sha: JudgedSha, hash: PlaybookHash),
            arrange: h =>
            {
                h.Workspace.HeadsFor[h.Root] = new RemoteHeads("main", new Dictionary<string, string> { ["main"] = TrunkTip });
                h.Workspace.FetchAnswer = false;
            });
        using var _h = h;

        Assert.False(report.PlaybookStale);
        Assert.Single(h.Sessions.Spawned);
        Assert.Empty(h.Wire.To("PUT", "/api/hatch/playbook-files"));
        Assert.True(h.Say.Mentions("could not fetch"));
    }

    [Fact]
    public async Task The_spawn_time_read_spawns_anyway_when_the_blob_read_fails()
    {
        var (report, h) = await RunPlaybookCheckAsync(
            Fixtures.PlaybookFileReference(path: PlaybookPath, trunk: "main", sha: JudgedSha, hash: PlaybookHash),
            arrange: h =>
            {
                h.Workspace.HeadsFor[h.Root] = new RemoteHeads("main", new Dictionary<string, string> { ["main"] = TrunkTip });
                h.Workspace.BlobsFor[(h.Root, "main", PlaybookPath)] = new BlobRead(BlobOutcome.Failed, null, "git exited 128");
            });
        using var _h = h;

        Assert.False(report.PlaybookStale);
        Assert.Single(h.Sessions.Spawned);
        Assert.Empty(h.Wire.To("PUT", "/api/hatch/playbook-files"));
        Assert.True(h.Say.Mentions("could not read"));
    }

    [Fact]
    public async Task The_spawn_time_read_is_skipped_entirely_for_a_table_sourced_project()
    {
        var (report, h) = await RunPlaybookCheckAsync(reference: null);
        using var _h = h;

        Assert.False(report.PlaybookStale);
        Assert.Single(h.Sessions.Spawned);
        Assert.DoesNotContain(h.Workspace.Calls, c => c.StartsWith("heads ", StringComparison.Ordinal));
        Assert.DoesNotContain(h.Workspace.Calls, c => c.StartsWith("fetch ", StringComparison.Ordinal));
        Assert.DoesNotContain(h.Workspace.Calls, c => c.StartsWith("read-blob ", StringComparison.Ordinal));
    }
}

using System.Net;

namespace Hatch.Cli.Tests;

/// <summary>
/// The walk: the queue picks, the claim decides, and the named read is the
/// payload for the ticket already held.
/// </summary>
public sealed class PickerTests
{
    private const string Queue = "/api/hatch/work/queue";

    private static string Held(string runner) =>
        $"\"hatch is working this from {runner}, last heard from 8 seconds ago\"";

    /// <summary>
    /// A picker over the harness's own checkout by default, so the tests below
    /// that do not care about checkouts at all do not have to spell one out -
    /// and the ones that do can hand in their own.
    /// </summary>
    private static Picker MakePicker(
        Harness h, IReadOnlyList<CheckoutEntry>? checkouts = null, string? standingRoot = null,
        string agent = "claude", ModelFamilies? families = null, IAgentAdapter? adapter = null) =>
        new(h.Board, "test:/checkout", h.Say, checkouts ?? h.Runtime.Checkouts, standingRoot ?? h.Root, null,
            agent: agent, families: families, adapter: adapter);

    /// <summary>Claude's family and one more, so a runner can be the other agent.</summary>
    private static readonly ModelFamilies TwoAgents = new([
        ModelFamilies.Default.Rows[0], new ModelFamily("fake-agent", "^fake-[a-z0-9-]+$", "fake-1")]);

    private const string Claimed = "/api/hatch/issues/AER-2/claim";

    [Fact]
    public async Task The_queue_read_names_the_runner_so_the_board_can_defer_a_ticket_it_let_go()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());

        await MakePicker(h).PickAsync(null, 0, default, Harness.Beat);

        Assert.Contains("runner=test%3A%2Fcheckout", h.Wire.To("GET", Queue).Single().Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_row_pinned_to_another_agents_family_is_skipped_before_it_is_claimed()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();

        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1", model: "claude-opus-5"), Fixtures.Row("AER-2", model: "opus") });
        h.Wire.Reply("POST", Claimed, HttpStatusCode.OK, Fixtures.Taken(token));
        h.Wire.Json("GET", "/api/hatch/work/AER-2", Fixtures.Work("AER-2"));
        h.Wire.Reply("DELETE", Claimed, HttpStatusCode.NoContent);

        var picked = await MakePicker(h, agent: "fake-agent", families: TwoAgents)
            .PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Claimed, picked.Outcome);
        Assert.Equal("AER-2", picked.Work!.Issue.Key);
        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-1/claim"));
        Assert.Equal(
            ["  AER-1  pinned to claude-opus-5, a claude model - this runner's agent is fake-agent"],
            picked.Skipped);

        await picked.Claim!.ReleaseAsync();
    }

    [Theory]
    [InlineData("qwen3-coder:30b", "claude", "pinned to qwen3-coder:30b, an ollama model - this runner's agent is claude")]
    [InlineData("gpt-6-luna", "claude", "pinned to gpt-6-luna, a codex model - this runner's agent is claude")]
    [InlineData("claude-opus-5", "codex", "pinned to claude-opus-5, a claude model - this runner's agent is codex")]
    [InlineData("qwen3-coder:30b", "ollama", null)]
    [InlineData("opus", "claude", null)]
    [InlineData(null, "claude", null)]
    public void WrongAgent_takes_the_article_from_the_owners_first_letter(string? model, string agent, string? expected)
    {
        var why = Picker.WrongAgent(model, agent, ModelFamilies.Default);

        Assert.Equal(expected, why);
    }

    [Fact]
    public async Task A_row_pinned_to_an_ollama_model_waits_for_an_ollama_runner()
    {
        using var h = new Harness();

        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1", model: "qwen3-coder:30b") });

        var picked = await MakePicker(h).PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Skipped, picked.Outcome);
        Assert.Equal(
            ["  AER-1  pinned to qwen3-coder:30b, an ollama model - this runner's agent is claude"],
            picked.Skipped);
        Assert.Null(picked.Work);
        Assert.DoesNotContain(h.Wire.Calls, c => c.Path.EndsWith("/claim", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_board_whose_only_clear_row_is_pinned_to_another_agent_is_skipped_and_nothing_is_claimed()
    {
        using var h = new Harness();

        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1", model: "claude-opus-5") });

        var picked = await MakePicker(h, agent: "fake-agent", families: TwoAgents)
            .PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Skipped, picked.Outcome);
        Assert.Single(picked.Skipped!);
        Assert.Empty(picked.Busy);
        Assert.Null(picked.Work);
        Assert.DoesNotContain(h.Wire.Calls, c => c.Path.EndsWith("/claim", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("fake-agent")]
    public async Task An_alias_and_an_unpinned_row_are_never_skipped(string agent)
    {
        using var h = new Harness();
        var token = Guid.NewGuid();

        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-2", model: "opus") });
        h.Wire.Reply("POST", Claimed, HttpStatusCode.OK, Fixtures.Taken(token));
        h.Wire.Json("GET", "/api/hatch/work/AER-2", Fixtures.Work("AER-2"));
        h.Wire.Reply("DELETE", Claimed, HttpStatusCode.NoContent);

        var picked = await MakePicker(h, agent: agent, families: TwoAgents)
            .PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Claimed, picked.Outcome);
        Assert.Null(picked.Skipped);
        await picked.Claim!.ReleaseAsync();

        h.Wire.Replace("GET", Queue, HttpStatusCode.OK, System.Text.Json.JsonSerializer.Serialize(new[] { Fixtures.Row("AER-2") }, Fixtures.Json));
        var unpinned = await MakePicker(h, agent: agent, families: TwoAgents)
            .PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Claimed, unpinned.Outcome);
        Assert.Null(unpinned.Skipped);
        await unpinned.Claim!.ReleaseAsync();
    }

    [Fact]
    public async Task The_claude_runner_takes_a_claude_pin_and_skips_the_other_agents()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();

        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1", model: "fake-1"), Fixtures.Row("AER-2", model: "claude-opus-5") });
        h.Wire.Reply("POST", Claimed, HttpStatusCode.OK, Fixtures.Taken(token));
        h.Wire.Json("GET", "/api/hatch/work/AER-2", Fixtures.Work("AER-2"));
        h.Wire.Reply("DELETE", Claimed, HttpStatusCode.NoContent);

        var picked = await MakePicker(h, families: TwoAgents)
            .PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Claimed, picked.Outcome);
        Assert.Equal("AER-2", picked.Work!.Issue.Key);
        Assert.Equal(
            ["  AER-1  pinned to fake-1, a fake-agent model - this runner's agent is claude"],
            picked.Skipped);

        await picked.Claim!.ReleaseAsync();
    }

    [Fact]
    public async Task Skipped_rows_do_not_spend_the_attempts()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();

        var rows = Enumerable.Range(1, 6).Select(n => Fixtures.Row($"AER-{n + 10}", model: "claude-opus-5"))
            .Append(Fixtures.Row("AER-2")).ToArray();
        h.Wire.Json("GET", Queue, rows);
        h.Wire.Reply("POST", Claimed, HttpStatusCode.OK, Fixtures.Taken(token));
        h.Wire.Json("GET", "/api/hatch/work/AER-2", Fixtures.Work("AER-2"));
        h.Wire.Reply("DELETE", Claimed, HttpStatusCode.NoContent);

        var picked = await MakePicker(h, agent: "fake-agent", families: TwoAgents)
            .PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Claimed, picked.Outcome);
        Assert.Equal("AER-2", picked.Work!.Issue.Key);
        Assert.Equal(6, picked.Skipped!.Count);

        await picked.Claim!.ReleaseAsync();
    }

    [Fact]
    public async Task A_hop_row_pinned_to_another_family_is_not_skipped()
    {
        using var h = new Harness();

        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1", hop: true, model: "claude-opus-5") });
        h.Wire.Json("POST", "/api/hatch/work/AER-1/hop", Fixtures.Issue("AER-1"));

        var picked = await MakePicker(h, agent: "fake-agent", families: TwoAgents)
            .PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Hopped, picked.Outcome);
        Assert.Null(picked.Skipped);
    }

    [Fact]
    public async Task A_row_whose_denied_tools_its_adapter_cannot_honour_is_skipped_before_it_is_claimed()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();
        var adapter = new FakeAdapter { CanDenyRefusal = "cannot deny Bash(git push:*)" };

        h.Wire.Json("GET", Queue, new[]
        {
            Fixtures.Row("AER-1", deniedTools: ["Bash(git push:*)"]), Fixtures.Row("AER-2"),
        });
        h.Wire.Reply("POST", Claimed, HttpStatusCode.OK, Fixtures.Taken(token));
        h.Wire.Json("GET", "/api/hatch/work/AER-2", Fixtures.Work("AER-2"));
        h.Wire.Reply("DELETE", Claimed, HttpStatusCode.NoContent);

        var picked = await MakePicker(h, adapter: adapter).PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Claimed, picked.Outcome);
        Assert.Equal("AER-2", picked.Work!.Issue.Key);
        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-1/claim"));
        Assert.Equal(["  AER-1  cannot deny Bash(git push:*)"], picked.Skipped);

        await picked.Claim!.ReleaseAsync();
    }

    [Fact]
    public async Task A_board_whose_only_clear_row_is_refused_by_the_adapter_is_skipped_and_nothing_is_claimed()
    {
        using var h = new Harness();
        var adapter = new FakeAdapter { CanDenyRefusal = "cannot deny Bash(git push:*)" };

        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1", deniedTools: ["Bash(git push:*)"]) });

        var picked = await MakePicker(h, adapter: adapter).PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Skipped, picked.Outcome);
        Assert.Single(picked.Skipped!);
        Assert.Empty(picked.Busy);
        Assert.Null(picked.Work);
        Assert.DoesNotContain(h.Wire.Calls, c => c.Path.EndsWith("/claim", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_hop_row_refused_by_the_adapter_is_not_skipped()
    {
        using var h = new Harness();
        var adapter = new FakeAdapter { CanDenyRefusal = "cannot deny Bash(git push:*)" };

        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1", hop: true, deniedTools: ["Bash(git push:*)"]) });
        h.Wire.Json("POST", "/api/hatch/work/AER-1/hop", Fixtures.Issue("AER-1"));

        var picked = await MakePicker(h, adapter: adapter).PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Hopped, picked.Outcome);
        Assert.Null(picked.Skipped);
    }

    [Fact]
    public async Task A_denied_tools_row_is_never_skipped_with_no_adapter_or_one_that_does_not_refuse()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();

        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-2", deniedTools: ["Bash(git push:*)"]) });
        h.Wire.Reply("POST", Claimed, HttpStatusCode.OK, Fixtures.Taken(token));
        h.Wire.Json("GET", "/api/hatch/work/AER-2", Fixtures.Work("AER-2"));
        h.Wire.Reply("DELETE", Claimed, HttpStatusCode.NoContent);

        var picked = await MakePicker(h).PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Claimed, picked.Outcome);
        Assert.Null(picked.Skipped);
        await picked.Claim!.ReleaseAsync();

        var unrefusing = await MakePicker(h, adapter: new FakeAdapter())
            .PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Claimed, unrefusing.Outcome);
        Assert.Null(unrefusing.Skipped);
        await unrefusing.Claim!.ReleaseAsync();
    }

    private const string Unpulled =
        "pinned to qwen3-coder:30b, which this runner's ollama has not pulled - ollama pull qwen3-coder:30b would";

    [Fact]
    public async Task A_row_whose_model_its_adapter_cannot_serve_is_skipped_before_it_is_claimed()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();
        var adapter = new FakeAdapter { CanServeRefusal = Unpulled };

        h.Wire.Json("GET", Queue, new[]
        {
            Fixtures.Row("AER-1", model: "qwen3-coder:30b"), Fixtures.Row("AER-2"),
        });
        h.Wire.Reply("POST", Claimed, HttpStatusCode.OK, Fixtures.Taken(token));
        h.Wire.Json("GET", "/api/hatch/work/AER-2", Fixtures.Work("AER-2"));
        h.Wire.Reply("DELETE", Claimed, HttpStatusCode.NoContent);

        var picked = await MakePicker(h, agent: "ollama", adapter: adapter).PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Claimed, picked.Outcome);
        Assert.Equal("AER-2", picked.Work!.Issue.Key);
        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-1/claim"));
        Assert.Equal([$"  AER-1  {Unpulled}"], picked.Skipped);

        await picked.Claim!.ReleaseAsync();
    }

    [Fact]
    public async Task A_board_whose_only_clear_row_is_unserved_by_the_adapter_is_skipped_and_nothing_is_claimed()
    {
        using var h = new Harness();
        var adapter = new FakeAdapter { CanServeRefusal = Unpulled };

        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1", model: "qwen3-coder:30b") });

        var picked = await MakePicker(h, agent: "ollama", adapter: adapter).PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Skipped, picked.Outcome);
        Assert.Equal([$"  AER-1  {Unpulled}"], picked.Skipped);
        Assert.Empty(picked.Busy);
        Assert.Null(picked.Work);
        Assert.DoesNotContain(h.Wire.Calls, c => c.Path.EndsWith("/claim", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_hop_row_unserved_by_the_adapter_is_not_skipped()
    {
        using var h = new Harness();
        var adapter = new FakeAdapter { CanServeRefusal = Unpulled };

        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1", hop: true, model: "qwen3-coder:30b") });
        h.Wire.Json("POST", "/api/hatch/work/AER-1/hop", Fixtures.Issue("AER-1"));

        var picked = await MakePicker(h, adapter: adapter).PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Hopped, picked.Outcome);
        Assert.Null(picked.Skipped);
    }

    [Fact]
    public async Task A_pinned_row_is_never_skipped_with_no_adapter_or_one_that_does_not_refuse()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();

        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-2", model: "qwen3-coder:30b") });
        h.Wire.Reply("POST", Claimed, HttpStatusCode.OK, Fixtures.Taken(token));
        h.Wire.Json("GET", "/api/hatch/work/AER-2", Fixtures.Work("AER-2"));
        h.Wire.Reply("DELETE", Claimed, HttpStatusCode.NoContent);

        var picked = await MakePicker(h, agent: "ollama").PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Claimed, picked.Outcome);
        Assert.Null(picked.Skipped);
        await picked.Claim!.ReleaseAsync();

        var unrefusing = await MakePicker(h, agent: "ollama", adapter: new FakeAdapter())
            .PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Claimed, unrefusing.Outcome);
        Assert.Null(unrefusing.Skipped);
        await unrefusing.Claim!.ReleaseAsync();
    }

    [Fact]
    public void The_skipped_report_names_the_header_then_each_row()
    {
        var report = Picker.SkippedReport(["  AER-1  why", "  AER-2  why too"]).ToList();

        Assert.Equal(
            [
                "hatch: skipped - one this agent cannot take",
                "hatch:   AER-1  why",
                "hatch:   AER-2  why too",
            ],
            report);
    }

    [Fact]
    public async Task The_first_clear_row_is_claimed_and_read_back_with_the_token()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();

        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1", "a question is open"), Fixtures.Row("AER-2") });
        h.Wire.Reply("POST", "/api/hatch/issues/AER-2/claim", HttpStatusCode.OK, Fixtures.Taken(token));
        h.Wire.Json("GET", "/api/hatch/work/AER-2", Fixtures.Work("AER-2"));
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-2/claim", HttpStatusCode.NoContent);

        var picked = await MakePicker(h)
            .PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Claimed, picked.Outcome);
        Assert.Equal("AER-2", picked.Work!.Issue.Key);

        // The runner's own lease must not fold the runner's own dispatch, which
        // is what the token on the second read is for.
        Assert.Contains($"heldToken={token}", h.Wire.To("GET", "/api/hatch/work/AER-2")[0].Query,
            StringComparison.OrdinalIgnoreCase);

        // The blocked row was never reached for.
        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-1/claim"));

        await picked.Claim!.ReleaseAsync();
    }

    /// <summary>HA-582: the match is only known after the take, so the playbook rides a heartbeat rather than the claim itself.</summary>
    [Fact]
    public async Task A_claim_against_a_row_with_a_playbook_attaches_it_after_the_match_resolves()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();

        h.Wire.Replace("GET", Queue, HttpStatusCode.OK, System.Text.Json.JsonSerializer.Serialize(new[] { Fixtures.Row("AER-2") }, Fixtures.Json));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-2/claim", HttpStatusCode.OK, Fixtures.Taken(token));
        h.Wire.Json("GET", "/api/hatch/work/AER-2", Fixtures.Work("AER-2"));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-2/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-2/claim", HttpStatusCode.NoContent);

        var picked = await MakePicker(h)
            .PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Claimed, picked.Outcome);
        var sent = Assert.Single(
            h.Wire.To("POST", "/api/hatch/issues/AER-2/claim/heartbeat").Select(c => c.Read<ClaimHeartbeatRequest>()),
            b => b.PlaybookId == Fixtures.Playbook().Id);
        Assert.Equal(token, sent.Token);

        await picked.Claim!.ReleaseAsync();
    }

    /// <summary>A failed attach is weather, not a reason to let the claim go - the walk keeps the ticket either way.</summary>
    [Fact]
    public async Task A_failed_playbook_attach_does_not_cost_the_dispatch()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();

        h.Wire.Replace("GET", Queue, HttpStatusCode.OK, System.Text.Json.JsonSerializer.Serialize(new[] { Fixtures.Row("AER-2") }, Fixtures.Json));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-2/claim", HttpStatusCode.OK, Fixtures.Taken(token));
        h.Wire.Json("GET", "/api/hatch/work/AER-2", Fixtures.Work("AER-2"));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-2/claim/heartbeat", HttpStatusCode.InternalServerError, "boom");
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-2/claim", HttpStatusCode.NoContent);

        var picked = await MakePicker(h)
            .PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Claimed, picked.Outcome);
        Assert.Contains(h.Say.Complained, l => l.Contains("AER-2", StringComparison.Ordinal));

        await picked.Claim!.ReleaseAsync();
    }

    [Fact]
    public async Task A_ticket_taken_between_the_scan_and_the_claim_moves_the_pass_on()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();

        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1"), Fixtures.Row("AER-2") });
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.Conflict, Held("other:/tree"));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-2/claim", HttpStatusCode.OK, Fixtures.Taken(token));
        h.Wire.Json("GET", "/api/hatch/work/AER-2", Fixtures.Work("AER-2"));
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-2/claim", HttpStatusCode.NoContent);

        var picked = await MakePicker(h)
            .PickAsync(null, 0, default, Harness.Beat);

        // Two loops that started in the same second end up on two tickets, and
        // neither idles because the other got there first.
        Assert.Equal(Pick.Claimed, picked.Outcome);
        Assert.Equal("AER-2", picked.Work!.Issue.Key);
        Assert.Contains(picked.Busy, b => b.Contains("AER-1", StringComparison.Ordinal));

        await picked.Claim!.ReleaseAsync();
    }

    [Fact]
    public async Task A_board_whose_every_candidate_is_taken_is_busy_and_says_who_has_them()
    {
        using var h = new Harness();

        var rows = Enumerable.Range(1, 8).Select(n => Fixtures.Row($"AER-{n}")).ToArray();
        h.Wire.Json("GET", Queue, rows);
        foreach (var row in rows)
            h.Wire.Reply("POST", $"/api/hatch/issues/{row.Issue.Key}/claim", HttpStatusCode.Conflict,
                Held("other:/tree"));

        var picked = await MakePicker(h)
            .PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Busy, picked.Outcome);

        // A bounded walk: a board where the first five are all being worked is a
        // board where waiting an interval is the honest thing to do.
        Assert.Equal(Picker.Attempts, picked.Busy.Count);
        Assert.Equal(Picker.Attempts, h.Wire.Calls.Count(c => c.Path.EndsWith("/claim", StringComparison.Ordinal)));

        var report = Picker.BusyReport(null, picked.Busy).ToList();
        Assert.Contains(report, l => l.Contains("being worked by another runner", StringComparison.Ordinal));
        Assert.Contains(report, l => l.Contains("other:/tree", StringComparison.Ordinal));

        // And it does not read like the empty board, which is the other thing a
        // pass can find and the one that means the night is over.
        Assert.DoesNotContain(report, l => l.Contains("nothing on the board", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_board_with_nothing_clear_is_idle_rather_than_busy()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1", "a question is open") });

        var picked = await MakePicker(h)
            .PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Idle, picked.Outcome);
        Assert.Empty(h.Wire.Calls.Where(c => c.Path.EndsWith("/claim", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_ticket_that_changed_under_us_gives_the_lease_straight_back()
    {
        using var h = new Harness();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1"), Fixtures.Row("AER-2") });
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(first));
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", blocked: "a question is open here"));
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-1/claim", HttpStatusCode.NoContent);
        h.Wire.Reply("POST", "/api/hatch/issues/AER-2/claim", HttpStatusCode.OK, Fixtures.Taken(second));
        h.Wire.Json("GET", "/api/hatch/work/AER-2", Fixtures.Work("AER-2"));
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-2/claim", HttpStatusCode.NoContent);

        var picked = await MakePicker(h)
            .PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Claimed, picked.Outcome);
        Assert.Equal("AER-2", picked.Work!.Issue.Key);

        // The one it could not use was let go of, not left held until the TTL.
        Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));

        await picked.Claim!.ReleaseAsync();
    }

    /// <summary>Throws instead of ever answering - what a clone this walk cannot even attempt looks like.</summary>
    private sealed class ThrowingClone : IClone
    {
        public string? Run() => throw new InvalidOperationException("git could not start");
    }

    [Fact]
    public async Task Clones_Resolve_throwing_after_the_take_releases_the_claim_once()
    {
        using var h = new Harness();
        var workspace = Path.Combine(h.Temp, "clones");
        var repo = Fixtures.Repository(
            "https://example.test/owner/repo.git", canonical: "example.test/owner/repo", primary: true, matchedRemote: null);

        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1") });
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", repositories: [repo]));
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-1/claim", HttpStatusCode.NoContent);

        var picker = new Picker(
            h.Board, "test:/checkout", h.Say, h.Runtime.Checkouts, h.Root, null, workspace, (_, _) => new ThrowingClone());

        var failed = await Assert.ThrowsAsync<PickFailedException>(
            () => picker.PickAsync(null, 0, default, Harness.Beat));
        Assert.Equal("AER-1", failed.Key);

        Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
    }

    [Fact]
    public async Task A_refused_comment_after_a_failed_clone_does_not_stop_the_walk()
    {
        using var h = new Harness();
        var workspace = Path.Combine(h.Temp, "clones");
        var repo = Fixtures.Repository(
            "https://example.test/owner/repo.git", canonical: "example.test/owner/repo", primary: true, matchedRemote: null);

        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1"), Fixtures.Row("AER-2") });
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", repositories: [repo]));
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-1/claim", HttpStatusCode.NoContent);
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/comments", HttpStatusCode.InternalServerError, "boom");

        h.Wire.Reply("POST", "/api/hatch/issues/AER-2/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        h.Wire.Json("GET", "/api/hatch/work/AER-2", Fixtures.Work("AER-2"));
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-2/claim", HttpStatusCode.NoContent);

        h.Clone.Error = "could not clone";

        var picker = new Picker(
            h.Board, "test:/checkout", h.Say, h.Runtime.Checkouts, h.Root, null, workspace, h.Clone.Factory);

        var picked = await picker.PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Claimed, picked.Outcome);
        Assert.Equal("AER-2", picked.Work!.Issue.Key);
        Assert.Single(picked.CloneFailures!);

        await picked.Claim!.ReleaseAsync();
    }

    [Fact]
    public async Task A_board_that_will_not_answer_is_neither_idle_nor_busy()
    {
        using var h = new Harness();
        h.Wire.Reply("GET", Queue, HttpStatusCode.InternalServerError, "boom");

        var picked = await MakePicker(h)
            .PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Unreadable, picked.Outcome);
    }

    /// <summary>
    /// A 400 is the dispatcher saying this will never succeed as asked -
    /// today, a --mine pass whose key belongs to nobody - and that is a
    /// different situation from the ordinary weather a 500 or a dropped
    /// connection is. Refused ends the night; Unreadable tries again.
    /// </summary>
    [Fact]
    public async Task A_400_from_the_queue_read_is_refused_rather_than_unreadable()
    {
        using var h = new Harness();
        h.Wire.Reply("GET", Queue, HttpStatusCode.BadRequest,
            "\"this key belongs to nobody, so it has no tickets of its own\"");

        var picked = await MakePicker(h).PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Refused, picked.Outcome);
        Assert.Contains("this key belongs to nobody", picked.Refusal);
    }

    [Fact]
    public async Task Mine_is_sent_when_asked_for_and_not_otherwise()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());

        await MakePicker(h).PickAsync(null, 0, default, Harness.Beat, mine: true);
        await MakePicker(h).PickAsync(null, 0, default, Harness.Beat);

        var calls = h.Wire.To("GET", Queue);
        Assert.Contains("mine=true", calls[0].Query, StringComparison.Ordinal);
        Assert.DoesNotContain("mine=true", calls[1].Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_epic_narrows_the_scan_and_nothing_else()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());

        await MakePicker(h).PickAsync("AER-1", -420, default, Harness.Beat);

        var asked = h.Wire.To("GET", Queue)[0].Query;
        Assert.Contains("ancestorKey=AER-1", asked, StringComparison.Ordinal);
        Assert.Contains("offsetMinutes=-420", asked, StringComparison.Ordinal);
    }

    // ---- Which checkout a claimed dispatch resolves to ----

    [Fact]
    public async Task The_queue_read_carries_the_standing_checkouts_remote_and_standing()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());

        await MakePicker(h).PickAsync(null, 0, default, Harness.Beat);

        var asked = h.Wire.To("GET", Queue)[0].Query;
        Assert.Contains($"remote={Uri.EscapeDataString(h.Runtime.Checkouts[0].Remote!)}", asked, StringComparison.Ordinal);
        Assert.Contains("standing=true", asked, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_dispatch_matching_a_non_standing_checkout_spawns_there_with_the_others_on_add_dir()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();
        var other = Path.Combine(h.Temp, "other");
        Directory.CreateDirectory(other);

        var checkouts = new[]
        {
            h.Runtime.Checkouts[0],
            new CheckoutEntry(other, "https://example.test/other.git", Standing: false),
        };

        var repos = new[]
        {
            Fixtures.Repository("https://example.test/other.git", primary: true, matchedRemote: "https://example.test/other.git"),
            Fixtures.Repository(h.Runtime.Checkouts[0].Remote!, matchedRemote: h.Runtime.Checkouts[0].Remote),
        };

        h.Wire.Replace("GET", Queue, HttpStatusCode.OK, System.Text.Json.JsonSerializer.Serialize(new[] { Fixtures.Row("AER-2") }, Fixtures.Json));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-2/claim", HttpStatusCode.OK, Fixtures.Taken(token));
        h.Wire.Json("GET", "/api/hatch/work/AER-2", Fixtures.Work("AER-2", repositories: repos));
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-2/claim", HttpStatusCode.NoContent);

        var picked = await MakePicker(h, checkouts).PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Claimed, picked.Outcome);
        Assert.NotNull(picked.Chosen);
        Assert.Equal(other, picked.Chosen!.Root);
        Assert.Equal([h.Root], picked.Chosen.AddDirs);

        await picked.Claim!.ReleaseAsync();
    }

    [Fact]
    public async Task A_primary_that_matched_nothing_this_runner_has_releases_and_walks_on()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();

        var repos = new[] { Fixtures.Repository("https://example.test/elsewhere.git", primary: true, matchedRemote: null) };

        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-3") });
        h.Wire.Reply("POST", "/api/hatch/issues/AER-3/claim", HttpStatusCode.OK, Fixtures.Taken(token));
        h.Wire.Json("GET", "/api/hatch/work/AER-3", Fixtures.Work("AER-3", repositories: repos));
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-3/claim", HttpStatusCode.NoContent);

        var picked = await MakePicker(h).PickAsync(null, 0, default, Harness.Beat);

        // The one candidate on the board walked past, same as any other ticket
        // that changed under us between the two reads - so a lone one reads as
        // busy rather than idle, exactly as it would for any other refusal.
        Assert.Equal(Pick.Busy, picked.Outcome);
        Assert.Contains(picked.Busy, b => b.Contains("AER-3", StringComparison.Ordinal));
        Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-3/claim"));
    }

    // ---- The hop ----

    [Fact]
    public async Task A_hop_row_takes_no_claim_and_calls_the_hop_route()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1", hop: true) });
        h.Wire.Json("POST", "/api/hatch/work/AER-1/hop", Fixtures.Issue("AER-1"));

        var picked = await MakePicker(h).PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Hopped, picked.Outcome);
        Assert.Equal("AER-1", picked.Hopped!.Key);
        Assert.Equal("In Progress", picked.Hopped.From);
        Assert.Equal("In Review", picked.Hopped.To);
        Assert.Empty(h.Wire.Calls.Where(c => c.Path.EndsWith("/claim", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_hop_row_carrying_HopKind_Parent_reasons_as_parent_pulled()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1", hop: true, hopKind: HopKinds.Parent) });
        h.Wire.Json("POST", "/api/hatch/work/AER-1/hop", Fixtures.Issue("AER-1"));

        var picked = await MakePicker(h).PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Hopped, picked.Outcome);
        Assert.Equal("parent pulled, no session", picked.Hopped!.Reason);
    }

    [Fact]
    public async Task A_hop_row_carrying_HopKind_Epic_reasons_as_epic()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1", hop: true, hopKind: HopKinds.Epic) });
        h.Wire.Json("POST", "/api/hatch/work/AER-1/hop", Fixtures.Issue("AER-1"));

        var picked = await MakePicker(h).PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Hopped, picked.Outcome);
        Assert.Equal("epic, no session", picked.Hopped!.Reason);
    }

    [Fact]
    public async Task A_hop_row_carrying_HopKind_Under_reasons_as_under_the_epic()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1", hop: true, hopKind: HopKinds.Under, hopUnder: "HA-86") });
        h.Wire.Json("POST", "/api/hatch/work/AER-1/hop", Fixtures.Issue("AER-1"));

        var picked = await MakePicker(h).PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Hopped, picked.Outcome);
        Assert.Equal("under HA-86, no session", picked.Hopped!.Reason);
    }

    [Fact]
    public async Task A_hop_row_carrying_HopKind_SendBack_reasons_as_sent_back()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1", hop: true, hopKind: HopKinds.SendBack) });
        h.Wire.Json("POST", "/api/hatch/work/AER-1/hop", Fixtures.Issue("AER-1"));

        var picked = await MakePicker(h).PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Hopped, picked.Outcome);
        Assert.Equal("sent back, no session", picked.Hopped!.Reason);
    }

    [Fact]
    public async Task A_hop_row_carrying_HopKind_Approve_reasons_as_pre_approved()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1", hop: true, hopKind: HopKinds.Approve) });
        h.Wire.Json("POST", "/api/hatch/work/AER-1/hop", Fixtures.Issue("AER-1"));

        var picked = await MakePicker(h).PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Hopped, picked.Outcome);
        Assert.Equal("pre-approved, no session", picked.Hopped!.Reason);
    }

    [Fact]
    public async Task A_hop_row_carrying_HopKind_Close_reasons_as_pre_approved()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1", hop: true, hopKind: HopKinds.Close) });
        h.Wire.Json("POST", "/api/hatch/work/AER-1/hop", Fixtures.Issue("AER-1"));

        var picked = await MakePicker(h).PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Hopped, picked.Outcome);
        Assert.Equal("pre-approved, no session", picked.Hopped!.Reason);
    }

    [Fact]
    public async Task A_409_from_the_hop_route_walks_on_to_the_next_row()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();

        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1", hop: true), Fixtures.Row("AER-2") });
        h.Wire.Reply("POST", "/api/hatch/work/AER-1/hop", HttpStatusCode.Conflict, "\"AER-1 is not a hop\"");
        h.Wire.Reply("POST", "/api/hatch/issues/AER-2/claim", HttpStatusCode.OK, Fixtures.Taken(token));
        h.Wire.Json("GET", "/api/hatch/work/AER-2", Fixtures.Work("AER-2"));
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-2/claim", HttpStatusCode.NoContent);

        var picked = await MakePicker(h).PickAsync(null, 0, default, Harness.Beat);

        Assert.Equal(Pick.Claimed, picked.Outcome);
        Assert.Equal("AER-2", picked.Work!.Issue.Key);
        Assert.Contains(picked.Busy, b => b.Contains("AER-1", StringComparison.Ordinal));

        await picked.Claim!.ReleaseAsync();
    }
}

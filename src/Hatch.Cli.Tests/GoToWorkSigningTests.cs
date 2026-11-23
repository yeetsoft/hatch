using System.Net;

namespace Hatch.Cli.Tests;

/// <summary>
/// A machine that cannot sign a commit, and the loop's answer to it: ask git
/// before each increment, make the commits unsigned where the project allows
/// it, and where it does not, let the ticket go without spawning.
/// </summary>
public sealed class GoToWorkSigningTests
{
    private const string Queue = "/api/work/queue";

    private static readonly SigningCheck Locked = new(Signing.Fails, "error: gpg failed to sign the data: no secret key");

    /// <summary>One ticket in review, with the project's rule on unsigned commits, and everything a pass on it reads.</summary>
    private static void Ticket(Harness h, bool allowsUnsigned, int letGo = 0, string key = "AER-1")
    {
        h.Wire.Json("GET", Queue, new[] { Fixtures.Row(key) });
        h.Wire.Reply("POST", $"/api/issues/{key}/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        h.Wire.Reply("POST", $"/api/issues/{key}/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Reply("DELETE", $"/api/issues/{key}/claim", HttpStatusCode.NoContent);
        h.Wire.Json("GET", $"/api/work/{key}", Fixtures.Work(key, from: "In Review", letGo: letGo, allowsUnsignedCommits: allowsUnsigned));
        h.Wire.Json("POST", $"/api/issues/{key}/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("POST", $"/api/issues/{key}/comments", Fixtures.Comment());
        h.Wire.Json("PUT", $"/api/issues/{key}/stall", Fixtures.Issue(key));
        h.Wire.Json("GET", $"/api/issues/{key}/questions", Array.Empty<QuestionDto>());
    }

    private static async Task<int> Once(Harness h) => await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

    // ---- Where the project allows unsigned commits ----

    [Fact]
    public async Task A_checkout_that_cannot_sign_has_its_commits_made_unsigned_for_the_increment_and_only_that_one()
    {
        using var h = new Harness { CanSign = Locked };
        Ticket(h, allowsUnsigned: true);

        var onWhileRunning = false;
        var inner = h.Sessions.Behaviour;
        h.Sessions.Behaviour = (request, onLine, ct) =>
        {
            onWhileRunning = h.Runtime.Unsigned.On;
            return inner(request, onLine, ct);
        };

        Assert.Equal(0, await Once(h));

        var spawned = Assert.Single(h.Sessions.Spawned);
        Assert.True(spawned.Unsigned);
        Assert.True(onWhileRunning, "the runner's own git calls were unsigned while the session ran");
        Assert.Contains("Commits here are unsigned", spawned.Prompt, StringComparison.Ordinal);

        // The next increment starts without it.
        Assert.False(h.Runtime.Unsigned.On);
    }

    [Fact]
    public async Task The_check_is_made_after_the_reset_and_before_the_branch_is_prepared()
    {
        using var h = new Harness { CanSign = new SigningCheck(Signing.Works) };
        Ticket(h, allowsUnsigned: true);

        await Once(h);

        // The first ask is the start-up one, before any ticket; the last is the increment's.
        var (path, calls) = h.SigningAsked.Last();
        Assert.Equal(h.Runtime.Root, path);
        Assert.Contains(calls, c => c.StartsWith("prepare", StringComparison.Ordinal));
        Assert.DoesNotContain(calls, c => c.StartsWith("enter", StringComparison.Ordinal));
        Assert.Contains(h.Workspace.Calls, c => c.StartsWith("enter", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_checkout_that_signs_is_not_overridden_and_is_told_what_to_do_if_one_commit_still_cannot()
    {
        using var h = new Harness { CanSign = new SigningCheck(Signing.Works) };
        Ticket(h, allowsUnsigned: true);

        await Once(h);

        var spawned = Assert.Single(h.Sessions.Spawned);
        Assert.False(spawned.Unsigned);
        Assert.Contains("`--no-gpg-sign`", spawned.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_checkout_not_set_to_sign_is_left_alone_and_the_prompt_says_nothing()
    {
        using var h = new Harness { CanSign = new SigningCheck(Signing.Off) };
        Ticket(h, allowsUnsigned: true);

        await Once(h);

        var spawned = Assert.Single(h.Sessions.Spawned);
        Assert.False(spawned.Unsigned);
        Assert.DoesNotContain("gpg", spawned.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(h.Say.Complained, l => l.Contains("cannot sign", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failure_is_not_asked_again_for_thirty_minutes_and_is_asked_again_after()
    {
        using var h = new Harness { CanSign = Locked };
        Ticket(h, allowsUnsigned: true);

        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var runtime = h.Runtime with { Clock = clock };

        await runtime.CanSignAsync(runtime.Root, default);
        var asked = h.SigningAsked.Count;
        clock.Advance(TimeSpan.FromMinutes(29));
        await runtime.CanSignAsync(runtime.Root, default);
        Assert.Equal(asked, h.SigningAsked.Count);

        clock.Advance(TimeSpan.FromMinutes(2));
        await runtime.CanSignAsync(runtime.Root, default);
        Assert.Equal(asked + 1, h.SigningAsked.Count);
    }

    // ---- Where it allows only signed ones ----

    [Fact]
    public async Task A_project_that_allows_only_signed_commits_is_let_go_with_nothing_spawned()
    {
        using var h = new Harness { CanSign = Locked };
        Ticket(h, allowsUnsigned: false);

        Assert.Equal(0, await Once(h));

        Assert.Empty(h.Sessions.Spawned);
        Assert.DoesNotContain(h.Workspace.Calls, c => c.StartsWith("enter", StringComparison.Ordinal));

        var comment = Assert.Single(h.Wire.To("POST", "/api/issues/AER-1/comments"));
        Assert.Contains("cannot sign commits", comment.Body, StringComparison.Ordinal);
        Assert.Contains("gpg failed to sign the data: no secret key", comment.Body, StringComparison.Ordinal);
        Assert.Empty(h.Wire.To("PUT", "/api/issues/AER-1/stall"));

        var released = Assert.Single(h.Wire.To("DELETE", "/api/issues/AER-1/claim"));
        Assert.Contains("outcome=dropped", released.Query, StringComparison.Ordinal);
        Assert.Contains("failure=signing", released.Query, StringComparison.Ordinal);
        Assert.Contains($"failureWhy={Uri.EscapeDataString("error: gpg failed to sign the data: no secret key")}", released.Query, StringComparison.Ordinal);
        Assert.False(h.Runtime.Unsigned.On);
    }

    [Fact]
    public async Task A_checkout_not_set_to_sign_is_refused_the_same_way_where_only_signed_commits_are_allowed()
    {
        using var h = new Harness { CanSign = new SigningCheck(Signing.Off) };
        Ticket(h, allowsUnsigned: false);

        await Once(h);

        Assert.Empty(h.Sessions.Spawned);
        var comment = Assert.Single(h.Wire.To("POST", "/api/issues/AER-1/comments"));
        Assert.Contains("cannot sign commits", comment.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_project_that_allows_only_signed_commits_gets_a_session_where_signing_works()
    {
        using var h = new Harness { CanSign = new SigningCheck(Signing.Works) };
        Ticket(h, allowsUnsigned: false);

        await Once(h);

        var spawned = Assert.Single(h.Sessions.Spawned);
        Assert.False(spawned.Unsigned);
        Assert.Contains("Never commit unsigned", spawned.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(h.Wire.To("POST", "/api/issues/AER-1/comments"), c => c.Body.Contains("cannot sign", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_second_refusal_in_a_row_is_flagged_and_waits_as_any_other_stall_does()
    {
        using var h = new Harness { CanSign = Locked };
        Ticket(h, allowsUnsigned: false, letGo: 1);

        await Once(h);

        Assert.Empty(h.Sessions.Spawned);
        var comment = Assert.Single(h.Wire.To("POST", "/api/issues/AER-1/comments"));
        Assert.Contains("cannot sign commits", comment.Body, StringComparison.Ordinal);
        Assert.Contains("second increment in a row", comment.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("no session to resume", comment.Body, StringComparison.Ordinal);
        var stall = Assert.Single(h.Wire.To("PUT", "/api/issues/AER-1/stall"));
        Assert.Contains("cannot sign", stall.Body, StringComparison.Ordinal);
    }

    // ---- A loop that could never work anything (HA-2657) ----

    private static ProjectDto Project(string key, bool allowsUnsigned) =>
        new(1, key, key, 0, DateTimeOffset.UnixEpoch, null, null, [], null, [], false, false, AllowsUnsignedCommits: allowsUnsigned);

    [Fact]
    public async Task A_loop_that_cannot_sign_where_no_project_allows_it_exits_78_before_anything_is_claimed()
    {
        using var h = new Harness { CanSign = Locked };
        Ticket(h, allowsUnsigned: false);
        h.Wire.Json("GET", "/api/projects", new[] { Project("AER", false) });

        Assert.Equal(78, await Once(h));

        Assert.Empty(h.Sessions.Spawned);
        Assert.Empty(h.Wire.To("POST", "/api/issues/AER-1/claim"));
        Assert.Empty(h.Wire.To("GET", Queue));
        Assert.Contains(h.Say.Complained, l => l.Contains("AER", StringComparison.Ordinal) && l.Contains("only signed commits", StringComparison.Ordinal));
        Assert.Contains(h.Say.Complained, l => l.Contains("no secret key", StringComparison.Ordinal));
        Assert.Contains(h.Say.Complained, l => l.Contains("allow unsigned commits", StringComparison.Ordinal));

        // Nothing is left held: a second loop on the same checkout takes the lock.
        using var again = LoopLock.Take(h.Runtime.Root, h.Runtime.TempDirectory, out var refusal);
        Assert.NotNull(again);
    }

    [Fact]
    public async Task A_loop_that_cannot_sign_runs_as_usual_where_a_project_allows_it()
    {
        using var h = new Harness { CanSign = Locked };
        Ticket(h, allowsUnsigned: true);
        h.Wire.Json("GET", "/api/projects", new[] { Project("AER", false), Project("OTH", true) });

        Assert.Equal(0, await Once(h));

        Assert.Single(h.Sessions.Spawned);
    }

    [Fact]
    public async Task A_loop_that_cannot_sign_carries_on_against_a_board_that_does_not_list_projects()
    {
        using var h = new Harness { CanSign = Locked };
        Ticket(h, allowsUnsigned: false);

        Assert.Equal(0, await Once(h));

        Assert.Empty(h.Sessions.Spawned);
    }

    [Fact]
    public async Task A_ticket_let_go_for_want_of_a_signer_ends_the_loop_with_78_where_no_project_allows_it()
    {
        // Signing works at the start and does not at the first ticket.
        using var h = new Harness { CanSign = new SigningCheck(Signing.Works), CanSignLater = Locked };
        Ticket(h, allowsUnsigned: false);
        h.Wire.Json("GET", "/api/projects", new[] { Project("AER", false) });

        Assert.Equal(78, await new GoToWorkCommand(h.Runtime).RunAsync([], default));

        Assert.Empty(h.Sessions.Spawned);
        var released = Assert.Single(h.Wire.To("DELETE", "/api/issues/AER-1/claim"));
        Assert.Contains("outcome=dropped", released.Query, StringComparison.Ordinal);
        Assert.Contains(h.Say.Said, l => l.Contains("no project this runner serves allows unsigned commits", StringComparison.Ordinal));
    }

    // ---- The start-up line ----

    [Fact]
    public async Task A_checkout_that_cannot_sign_is_said_once_at_the_start_with_what_git_said()
    {
        using var h = new Harness { CanSign = Locked };
        Ticket(h, allowsUnsigned: true);

        await Once(h);

        var line = Assert.Single(h.Say.Complained, l => l.Contains("git cannot sign commits here", StringComparison.Ordinal));
        Assert.Contains("gpg failed to sign the data: no secret key", line, StringComparison.Ordinal);
        Assert.Contains("unsigned where a project allows it", line, StringComparison.Ordinal);
        Assert.Contains(h.Runtime.Root, line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Signing.Off)]
    [InlineData(Signing.Works)]
    public async Task A_checkout_that_signs_or_is_not_set_to_is_not_mentioned_at_the_start(Signing state)
    {
        using var h = new Harness { CanSign = new SigningCheck(state) };
        Ticket(h, allowsUnsigned: true);

        await Once(h);

        Assert.DoesNotContain(h.Say.Complained, l => l.Contains("sign", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(h.Say.Said, l => l.Contains("cannot sign", StringComparison.OrdinalIgnoreCase));
    }

    // ---- What the board is told about signing (HA-2655) ----

    [Fact]
    public async Task The_beat_after_a_changed_check_carries_the_new_answer()
    {
        using var h = new Harness { CanSign = new SigningCheck(Signing.Works) };
        Ticket(h, allowsUnsigned: true, key: "AER-1");
        Ticket(h, allowsUnsigned: true, key: "AER-2");
        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1"), Fixtures.Row("AER-2") });
        h.Wire.Json("POST", "/api/runners/sessions", new RunnerInstructionDto("running", null, 2, null, null));

        // The machine's key gets locked while the first session runs.
        var inner = h.Sessions.Behaviour;
        h.Sessions.Behaviour = (request, onLine, ct) =>
        {
            h.CanSign = Locked;
            return inner(request, onLine, ct);
        };

        await new GoToWorkCommand(h.Runtime).RunAsync(["--max-runs", "2"], default);

        var beats = h.Wire.To("POST", "/api/runners/sessions").Select(c => c.Read<RunnerHeartbeatRequest>()).ToList();
        Assert.Equal(RunnerSigning.Works, beats[0].Signing);
        Assert.Equal(RunnerSigning.Fails, beats[^1].Signing);
        Assert.Equal(Locked.Why, beats[^1].SigningWhy);
    }
}

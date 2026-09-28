using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Cli.Tests;

/// <summary>
/// The loop: what a pass does in what order, what it waits for, and what it
/// stops for.
/// </summary>
public sealed class GoToWorkTests
{
    private const string Queue = "/api/hatch/work/queue";

    private static string Held(string runner) =>
        $"\"hatch is working this from {runner}, last heard from 8 seconds ago\"";

    /// <summary>A board with one clear ticket on it, and everything an increment on it reads.</summary>
    private static Guid OneTicket(Harness h, string key = "AER-1")
    {
        var token = Guid.NewGuid();

        h.Wire.Json("GET", Queue, new[] { Fixtures.Row(key) });
        h.Wire.Reply("POST", $"/api/hatch/issues/{key}/claim", HttpStatusCode.OK, Fixtures.Taken(token));
        h.Wire.Reply("POST", $"/api/hatch/issues/{key}/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Reply("DELETE", $"/api/hatch/issues/{key}/claim", HttpStatusCode.NoContent);
        h.Wire.Json("GET", $"/api/hatch/work/{key}", Fixtures.Work(key, from: "In Review"));
        h.Wire.Json("POST", $"/api/hatch/issues/{key}/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", $"/api/hatch/issues/{key}/questions", Array.Empty<QuestionDto>());

        return token;
    }

    [Fact]
    public async Task A_pass_claims_before_it_resets_the_workspace_and_before_it_spawns()
    {
        using var h = new Harness();
        OneTicket(h);

        // What had already happened by the time the tree was asked to make
        // itself current. A ticket held is a ticket nothing else will start, and
        // a reset before the claim would be a fetch spent on an increment that
        // never happens.
        var claimedByThen = false;
        var spawnedByThen = false;
        h.Workspace.Watching = () =>
        {
            claimedByThen = h.Wire.Count("POST", "/api/hatch/issues/AER-1/claim") == 1;
            spawnedByThen = h.Sessions.Spawned.Count > 0;
        };

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        Assert.True(claimedByThen, "the ticket was claimed before the workspace was reset");
        Assert.False(spawnedByThen, "nothing was spawned before the workspace was reset");
        Assert.Single(h.Workspace.Prepared);
        Assert.Single(h.Sessions.Spawned);

        // ...and let go of when the increment ended.
        Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
    }

    [Fact]
    public async Task A_tree_that_will_not_reset_ends_the_night_and_gives_the_ticket_back()
    {
        using var h = new Harness();
        OneTicket(h);
        h.Workspace.Answer = Reset.Never;

        await new GoToWorkCommand(h.Runtime).RunAsync([], default);

        // Nothing was spawned onto a tree the loop could not make current, and
        // the lease did not sit there until its TTL.
        Assert.Empty(h.Sessions.Spawned);
        Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
        Assert.Contains(h.Say.Said, l => l.Contains("the workspace could not be reset", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_fetch_that_did_not_answer_is_a_wait_and_the_ticket_still_goes_back()
    {
        using var h = new Harness();
        OneTicket(h);
        h.Workspace.Answer = Reset.Later;

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        Assert.Empty(h.Sessions.Spawned);
        Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
        Assert.Contains(h.Say.Complained, l => l.Contains("the workspace is not ready", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_busy_board_waits_rather_than_ending_and_does_not_read_like_an_empty_one()
    {
        using var h = new Harness();
        var rows = Enumerable.Range(1, 5).Select(n => Fixtures.Row($"AER-{n}")).ToArray();

        h.Wire.Json("GET", Queue, rows);
        foreach (var row in rows)
            h.Wire.Reply("POST", $"/api/hatch/issues/{row.Issue.Key}/claim", HttpStatusCode.Conflict,
                Held("other:/tree"));

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        Assert.Contains(h.Say.Said, l => l.Contains("being worked by another runner", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains("other:/tree", StringComparison.Ordinal));
        Assert.DoesNotContain(h.Say.Said, l => l.Contains("nothing on the board", StringComparison.Ordinal));

        // Nothing was spawned, and the tree was never touched for a pass that
        // had nothing to do.
        Assert.Empty(h.Sessions.Spawned);
        Assert.Empty(h.Workspace.Prepared);
        Assert.Contains(h.Say.Said, l => l.Contains("--once, and the pass is done", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_empty_board_reads_like_an_empty_board()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        Assert.Contains(h.Say.Said, l => l.Contains("nothing on the board is an agent's to move", StringComparison.Ordinal));

        // `--once` is one pass and out, so it does not promise to ask again.
        Assert.DoesNotContain(h.Say.Said, l => l.Contains("waiting, and asking again", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_night_with_no_CLI_to_spawn_ends_before_it_reads_the_board()
    {
        using var h = new Harness();
        OneTicket(h);
        h.Sessions.Missing = "hatch: no claude CLI on PATH.";

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        Assert.Contains(h.Say.Complained, l => l.Contains("no claude CLI on PATH", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains("there is no claude CLI to spawn", StringComparison.Ordinal));
        Assert.Empty(h.Sessions.Spawned);

        // Nothing was read and nothing was claimed: the question is asked before
        // the board, because a night that cannot spend should not take a ticket
        // off it to find that out.
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task An_interrupt_gives_the_ticket_back_and_still_prints_the_tally()
    {
        using var h = new Harness();
        OneTicket(h);
        using var interrupting = new CancellationTokenSource();

        h.Sessions.Behaviour = FakeSessions.UntilStopped();

        var running = new GoToWorkCommand(h.Runtime).RunAsync([], interrupting.Token);
        await h.Sessions.StartedWithin();
        await interrupting.CancelAsync();
        await running;

        Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
        Assert.Contains(h.Say.Said, l => l.Contains("increment(s) in", StringComparison.Ordinal));
    }

    // ---- More than one checkout in play ----

    private static (CheckoutEntry Primary, CheckoutEntry Other, WorkRepositoryDto[] Repositories) TwoCheckouts(Harness h)
    {
        var other = Path.Combine(h.Temp, "other");
        Directory.CreateDirectory(other);

        var primary = h.Runtime.Checkouts[0];
        var entry = new CheckoutEntry(other, "https://example.test/other.git", Standing: false);

        var repos = new[]
        {
            Fixtures.Repository(primary.Remote!, primary: true, matchedRemote: primary.Remote),
            Fixtures.Repository(entry.Remote!, matchedRemote: entry.Remote),
        };

        return (primary, entry, repos);
    }

    [Fact]
    public async Task Two_checkouts_reset_in_order_before_one_spawn()
    {
        using var h = new Harness();
        var (primary, other, repos) = TwoCheckouts(h);
        var runtime = h.Runtime with { Checkouts = [primary, other] };

        var token = Guid.NewGuid();
        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1") });
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(token));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-1/claim", HttpStatusCode.NoContent);
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review", repositories: repos));
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        Assert.Equal(0, await new GoToWorkCommand(runtime).RunAsync(["--once"], default));

        Assert.Equal([primary.Path, other.Path], h.Workspace.Prepared);
        var spawned = Assert.Single(h.Sessions.Spawned);
        Assert.Equal(primary.Path, spawned.Root);
        Assert.Equal([other.Path], spawned.AddDirs);
    }

    [Fact]
    public async Task Never_on_the_second_checkout_ends_the_increment_with_nothing_spawned()
    {
        using var h = new Harness();
        var (primary, other, repos) = TwoCheckouts(h);
        var runtime = h.Runtime with { Checkouts = [primary, other] };
        h.Workspace.AnswerFor[other.Path] = Reset.Never;

        var token = Guid.NewGuid();
        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1") });
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(token));
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-1/claim", HttpStatusCode.NoContent);
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", repositories: repos));

        await new GoToWorkCommand(runtime).RunAsync([], default);

        Assert.Equal([primary.Path, other.Path], h.Workspace.Prepared);
        Assert.Empty(h.Sessions.Spawned);
        Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
        Assert.Contains(h.Say.Said, l => l.Contains("the workspace could not be reset", StringComparison.Ordinal));
    }

    // ---- --repo: checkouts a person names, rather than the one standing in ----

    private static string Tree(Harness h, string name, string origin)
    {
        var path = Path.Combine(h.Temp, name);
        Directory.CreateDirectory(path);
        Git(path, "init", "--quiet");
        Git(path, "remote", "add", "origin", origin);
        return path;
    }

    private static void Git(string dir, params string[] args)
    {
        var start = new ProcessStartInfo { FileName = "git", WorkingDirectory = dir, UseShellExecute = false };
        foreach (var arg in args) start.ArgumentList.Add(arg);

        using var process = Process.Start(start)!;
        process.WaitForExit();
    }

    [Fact]
    public async Task Repo_twice_serves_three_checkouts_with_the_standing_one_first_and_declares_three_remotes()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());

        // The standing checkout's origin is re-read from git, same as every
        // other checkout's - Harness's own bare .git directory has none set,
        // so this test gives it the remote it already claims to have.
        Git(h.Root, "init", "--quiet");
        Git(h.Root, "remote", "add", "origin", h.Runtime.Checkouts[0].Remote!);

        var b = Tree(h, "b", "https://example.test/b.git");
        var c = Tree(h, "c", "https://example.test/c.git");

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once", "--repo", b, "--repo", c], default));

        var call = Assert.Single(h.Wire.To("GET", Queue));
        Assert.Contains($"remote={Uri.EscapeDataString(h.Runtime.Checkouts[0].Remote!)}", call.Query);
        Assert.Contains($"remote={Uri.EscapeDataString("https://example.test/b.git")}", call.Query);
        Assert.Contains($"remote={Uri.EscapeDataString("https://example.test/c.git")}", call.Query);
        Assert.Contains("standing=true", call.Query);
    }

    [Fact]
    public async Task Repo_given_on_the_command_line_ignores_whatever_HATCH_REPOS_produced()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());

        var fromSettings = new CheckoutEntry(
            Path.Combine(h.Temp, "from-settings"), "https://example.test/from-settings.git", Standing: false);
        var runtime = h.Runtime with { Checkouts = [h.Runtime.Checkouts[0], fromSettings] };

        var fromFlag = Tree(h, "from-flag", "https://example.test/from-flag.git");

        Assert.Equal(0, await new GoToWorkCommand(runtime).RunAsync(["--once", "--repo", fromFlag], default));

        var call = Assert.Single(h.Wire.To("GET", Queue));
        Assert.Contains($"remote={Uri.EscapeDataString("https://example.test/from-flag.git")}", call.Query);
        Assert.DoesNotContain("from-settings.git", call.Query);
    }

    [SkippableFact]
    public async Task Two_spellings_of_one_repo_path_is_one_checkout_and_one_lock()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());

        var tree = Tree(h, "Named", "https://example.test/named.git");
        var lowered = Path.Combine(h.Temp, "named");
        Skip.IfNot(Directory.Exists(lowered), "this filesystem is case-sensitive, so there is one spelling");

        // If the two spellings were taken as two checkouts, the second
        // LoopLock.Take would refuse itself - the pid file the first left
        // behind is this same process's.
        Assert.Equal(
            0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once", "--repo", tree, "--repo", lowered], default));
        Assert.DoesNotContain(h.Say.Complained, l => l.Contains("already running", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_lock_held_on_the_second_named_checkout_refuses_naming_it_and_releases_the_first()
    {
        using var h = new Harness();
        var other = Tree(h, "other", "https://example.test/other.git");

        using var held = LoopLock.Take(other, h.Temp, out _);
        Assert.NotNull(held);

        Assert.Equal(1, await new GoToWorkCommand(h.Runtime).RunAsync(["--once", "--repo", other], default));

        Assert.Contains(h.Say.Complained, l => l.Contains("already running in", StringComparison.Ordinal));
        Assert.Contains(h.Say.Complained, l => l.Contains(other, StringComparison.Ordinal));

        // The standing checkout's lock - taken first - was released rather
        // than left behind: a second loop can take it.
        using var retaken = LoopLock.Take(h.Root, h.Temp, out var refusal);
        Assert.NotNull(retaken);
        Assert.Equal("", refusal);

        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task A_missing_repo_path_refuses_before_any_lock_and_before_any_claim()
    {
        using var h = new Harness();
        var missing = Path.Combine(h.Temp, "does-not-exist");

        Assert.Equal(1, await new GoToWorkCommand(h.Runtime).RunAsync(["--once", "--repo", missing], default));

        Assert.Contains(h.Say.Complained, l => l.Contains(missing, StringComparison.Ordinal));
        Assert.Empty(h.Wire.Calls);

        using var takeable = LoopLock.Take(h.Root, h.Temp, out var refusal);
        Assert.NotNull(takeable);
        Assert.Equal("", refusal);
    }

    [Fact]
    public async Task Started_outside_a_checkout_with_repo_is_named_after_the_first_and_sends_no_standing()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());

        var elsewhere = Tree(h, "elsewhere", "https://example.test/elsewhere.git");
        var runtime = h.Runtime with { Checkouts = [] };
        var runnerName = await Checkout.RunnerAsync(
            null, Checkout.Host(), elsewhere, null, default, h.Runtime.RunnersPath);

        Assert.Equal(0, await new GoToWorkCommand(runtime).RunAsync(["--once", "--repo", elsewhere], default));

        Assert.Contains(h.Wire.Calls, c => c.Path == $"/api/hatch/runners/{Uri.EscapeDataString(runnerName)}");

        var call = Assert.Single(h.Wire.To("GET", Queue));
        Assert.DoesNotContain("standing=true", call.Query);
        Assert.Contains($"remote={Uri.EscapeDataString("https://example.test/elsewhere.git")}", call.Query);
    }

    [Fact]
    public async Task A_second_loop_in_this_checkout_is_refused_and_the_first_keeps_its_lock()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());

        using var held = LoopLock.Take(h.Root, h.Temp, out _);
        Assert.NotNull(held);

        Assert.Equal(1, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));
        Assert.Contains(h.Say.Complained, l => l.Contains("already running in", StringComparison.Ordinal));

        // Nothing was read and nothing was spawned: the refusal is before the
        // board, because a loop that cannot run should not be asking.
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task Two_loops_in_two_checkouts_both_get_going()
    {
        using var first = new Harness();
        using var second = new Harness();

        foreach (var h in (Harness[])[first, second])
        {
            h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
            h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());
        }

        Assert.Equal(0, await new GoToWorkCommand(first.Runtime).RunAsync(["--once"], default));
        Assert.Equal(0, await new GoToWorkCommand(second.Runtime).RunAsync(["--once"], default));

        Assert.DoesNotContain(first.Say.Complained, l => l.Contains("already running", StringComparison.Ordinal));
        Assert.DoesNotContain(second.Say.Complained, l => l.Contains("already running", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_loop_carries_no_flag_of_its_own_across_a_night()
    {
        using var h = new Harness();
        OneTicket(h);

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        // `work --model` is one operator's opinion about one increment; what the
        // loop carries is the playbook's, which already has the ticket's own
        // overrides folded into it.
        var spawned = Assert.Single(h.Sessions.Spawned);
        Assert.Equal("opus", spawned.Model);
        Assert.Equal("high", spawned.Effort);
        Assert.Equal(h.Root, spawned.Root);
    }

    [Fact]
    public async Task It_takes_where_to_look_or_a_ticket_and_a_ticket_is_never_the_answer()
    {
        using var h = new Harness();

        Assert.Equal(1, await new GoToWorkCommand(h.Runtime).RunAsync(["AER-1"], default));
        Assert.Contains(h.Say.Complained, l => l.Contains("does not take a ticket", StringComparison.Ordinal));

        Assert.Equal(1, await new GoToWorkCommand(h.Runtime).RunAsync(["AER-1", "--under", "AER-9"], default));
        Assert.Contains(h.Say.Complained, l => l.Contains("not both", StringComparison.Ordinal));

        // Zero is refused rather than clamped: it reads as "as fast as possible"
        // and means a board asked the same question thousands of times a minute.
        Assert.Equal(1, await new GoToWorkCommand(h.Runtime).RunAsync(["--interval", "0"], default));
        Assert.Equal(1, await new GoToWorkCommand(h.Runtime).RunAsync(["--until", "tea time"], default));

        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task A_stop_file_that_is_already_there_is_refused_rather_than_read_as_an_empty_board()
    {
        using var h = new Harness();
        var stop = Path.Combine(h.Temp, "stop");
        File.WriteAllText(stop, "");

        Assert.Equal(1, await new GoToWorkCommand(h.Runtime).RunAsync(["--stop-file", stop], default));
        Assert.Contains(h.Say.Complained, l => l.Contains("already exists", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Max_runs_bounds_the_night()
    {
        using var h = new Harness();
        OneTicket(h);

        await new GoToWorkCommand(h.Runtime).RunAsync(["--max-runs", "2"], default);

        Assert.Equal(2, h.Sessions.Spawned.Count);
        Assert.Equal(2, h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim").Count);
        Assert.Contains(h.Say.Said, l => l.Contains("--max-runs 2 reached", StringComparison.Ordinal));
    }

    // ---- Coming back as a newer version of itself ----

    /// <summary>The totals one incarnation left for the next.</summary>
    private static void Carrying(Harness h, NightState state) => Assert.True(state.Write(h.NightState));

    [Fact]
    public async Task A_loop_whose_own_source_changed_at_the_reset_comes_back_as_the_new_one()
    {
        using var h = new Harness();
        OneTicket(h);

        // The change lands exactly where a real one would: inside the reset,
        // which is the moment the new source arrives on disk.
        var resets = 0;
        h.Workspace.Watching = () =>
        {
            if (++resets == 2) h.Self.Print = FakeSelf.Of(("src/Hatch.Cli/GoToWork.cs", "after"));
        };

        Assert.Equal(
            GoToWorkCommand.RestartExitCode,
            await new GoToWorkCommand(h.Supervised).RunAsync([], default));

        // Nothing was spawned on the pass that decided to restart, and the
        // ticket it had claimed went back before the process exited: a restart
        // holds no claim, or the version coming back would find its own work
        // being worked by a runner that no longer exists.
        Assert.Single(h.Sessions.Spawned);
        Assert.Equal(2, h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim").Count);

        Assert.Contains(h.Say.Said, l => l.Contains("the loop's own source changed", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains("src/Hatch.Cli/GoToWork.cs", StringComparison.Ordinal));

        // And the one increment that did run is counted once, in the file the
        // next incarnation reads.
        var carried = NightState.Read(h.NightState);
        Assert.NotNull(carried);
        Assert.Equal(1, carried.Runs);
        Assert.Equal(1.5m, carried.Spent);
        Assert.Equal(1, carried.Restarts);
        Assert.Single(carried.Stalled);
    }

    [Fact]
    public async Task A_loop_whose_source_did_not_change_carries_on_and_leaves_nothing_behind()
    {
        using var h = new Harness();
        OneTicket(h);

        Assert.Equal(0, await new GoToWorkCommand(h.Supervised).RunAsync(["--max-runs", "2"], default));

        Assert.Equal(2, h.Sessions.Spawned.Count);
        Assert.DoesNotContain(h.Say.Said, l => l.Contains("restarting", StringComparison.Ordinal));

        // A state file on disk means "somebody is coming back". A night that
        // ended does not leave one for the next night to read.
        Assert.False(File.Exists(h.NightState));
    }

    [Fact]
    public async Task A_loop_that_has_been_up_long_enough_restarts_without_having_claimed_anything()
    {
        using var h = new Harness();
        OneTicket(h);

        // The backstop, and the reason there is one: an idle loop never resets,
        // so it never sees a change, and would run the version it started with
        // until somebody came and stopped it.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 2, 0, 0, TimeSpan.Zero))
        {
            AutoAdvanceAmount = TimeSpan.FromMinutes(40),
        };

        var runtime = h.Supervised with { Clock = clock };

        Assert.Equal(
            GoToWorkCommand.RestartExitCode,
            await new GoToWorkCommand(runtime).RunAsync([], default));

        Assert.Contains(h.Say.Said, l => l.Contains("this loop has been running", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains("restarting to pick up anything that landed", StringComparison.Ordinal));

        // Read where no claim is held, so there is nothing to give back and
        // nothing half-started.
        Assert.Empty(h.Sessions.Spawned);
        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-1/claim"));
    }

    [Fact]
    public async Task An_age_of_zero_turns_the_backstop_off_and_a_negative_one_is_refused()
    {
        using var h = new Harness();
        OneTicket(h);

        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 2, 0, 0, TimeSpan.Zero))
        {
            AutoAdvanceAmount = TimeSpan.FromHours(4),
        };

        var runtime = h.Supervised with { Clock = clock };

        Assert.Equal(0, await new GoToWorkCommand(runtime).RunAsync(["--restart-after", "0", "--max-runs", "1"], default));
        Assert.Single(h.Sessions.Spawned);
        Assert.DoesNotContain(h.Say.Said, l => l.Contains("has been running", StringComparison.Ordinal));

        // Refused rather than clamped: a loop that is always too old is a loop
        // that does nothing but restart.
        Assert.Equal(1, await new GoToWorkCommand(h.Supervised).RunAsync(["--restart-after", "-1"], default));
        Assert.Contains(h.Say.Complained, l => l.Contains("--restart-after takes a number of minutes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task What_a_restart_carried_counts_against_the_bounds_that_were_typed()
    {
        using var h = new Harness();
        OneTicket(h);
        Carrying(h, new NightState { Runs = 2, Spent = 5m, Restarts = 1 });

        Assert.Equal(0, await new GoToWorkCommand(h.Supervised).RunAsync(["--max-runs", "3"], default));

        // Two increments were already spent before this process existed, so
        // --max-runs 3 buys one more and not three.
        Assert.Single(h.Sessions.Spawned);
        Assert.Contains(h.Say.Said, l => l.Contains("--max-runs 3 reached", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_restart_cannot_outspend_the_budget_by_restarting()
    {
        using var h = new Harness();
        OneTicket(h);
        Carrying(h, new NightState { Runs = 2, Spent = 5m });

        Assert.Equal(0, await new GoToWorkCommand(h.Supervised).RunAsync(["--max-spend", "6"], default));

        Assert.Single(h.Sessions.Spawned);
        Assert.Contains(h.Say.Said, l => l.Contains("--max-spend 6 reached at $6.50", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_hour_a_night_was_given_is_the_hour_it_was_given_and_not_one_re_read_after_midnight()
    {
        using var h = new Harness();
        OneTicket(h);

        // `--until 23:59` typed at 23:58 and restarted at 00:01 re-reads as
        // 23:59 tomorrow, which adds a day to the night. The instant is carried,
        // so it is the one it always was - and it has now gone by.
        Carrying(h, new NightState
        {
            Runs = 1,
            UntilAt = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
        });

        Assert.Equal(0, await new GoToWorkCommand(h.Supervised).RunAsync(["--until", "23:59"], default));

        Assert.Empty(h.Sessions.Spawned);
        Assert.Contains(h.Say.Said, l => l.Contains("--until 23:59 has come", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_tally_at_the_end_is_the_whole_night_and_says_how_many_times_it_came_back()
    {
        using var h = new Harness();
        OneTicket(h);
        Carrying(h, new NightState
        {
            Runs = 2,
            Spent = 5m,
            Restarts = 1,
            Started = DateTimeOffset.UtcNow.AddHours(-2),
            Moved = ["hatch:   moved    AER-9  landed earlier"],
        });

        await new GoToWorkCommand(h.Supervised).RunAsync(["--max-runs", "3"], default);

        // Every increment, the whole spend, and the elapsed time measured from
        // the first incarnation's start rather than this one's.
        Assert.Contains(h.Say.Said, l => l.Contains("3 increment(s) in 2h", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains("$6.50, 1 restart(s)", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains("moved    AER-9", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains("stalled  AER-1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_night_with_no_restarts_in_it_reads_exactly_as_it_always_did()
    {
        using var h = new Harness();
        OneTicket(h);

        await new GoToWorkCommand(h.Supervised).RunAsync(["--max-runs", "1"], default);

        Assert.Contains(h.Say.Said, l => l.Contains("1 increment(s) in", StringComparison.Ordinal));
        Assert.DoesNotContain(h.Say.Said, l => l.Contains("restart(s)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Three_ways_of_not_restarting_at_all()
    {
        // The source changes under every one of these, and none of them comes
        // back as the new version.
        foreach (var (what, args, supervised) in (ValueTuple<string, string[], bool>[])
                 [
                     ("--no-restart", ["--no-restart", "--max-runs", "1"], true),
                     ("--once", ["--once"], true),
                     ("no supervisor", ["--max-runs", "1"], false),
                 ])
        {
            using var h = new Harness();
            OneTicket(h);
            h.Workspace.Watching = () => h.Self.Print = FakeSelf.Of(("src/Hatch.Cli/GoToWork.cs", "after"));

            var runtime = supervised ? h.Supervised : h.Runtime;

            Assert.Equal(0, await new GoToWorkCommand(runtime).RunAsync(args, default));
            Assert.Single(h.Sessions.Spawned);
            Assert.DoesNotContain(h.Say.Said, l => l.Contains("restarting", StringComparison.Ordinal));
            Assert.False(File.Exists(h.NightState));

            // A runner started by hand never even reads its own source: there is
            // nobody standing over it to build the new one.
            if (!supervised) Assert.Equal(0, h.Self.Taken);
        }
    }

    // ---- --workspace: cloning what the board binds (HA-19) ----

    private static Runtime WithWorkspace(Harness h, string workspace) =>
        h.Runtime with { Settings = h.Runtime.Settings with { Workspace = workspace } };

    [Fact]
    public async Task A_workspace_holding_two_prior_clones_is_served_and_their_remotes_declared_with_clones_true()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());

        var workspace = Path.Combine(h.Temp, "clones");
        var a = Path.Combine(workspace, "example.test", "owner", "a");
        var b = Path.Combine(workspace, "example.test", "owner", "b");
        Directory.CreateDirectory(a);
        Git(a, "init", "--quiet");
        Git(a, "remote", "add", "origin", "https://example.test/owner/a.git");
        Directory.CreateDirectory(b);
        Git(b, "init", "--quiet");
        Git(b, "remote", "add", "origin", "https://example.test/owner/b.git");

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once", "--workspace", workspace], default));

        var call = Assert.Single(h.Wire.To("GET", Queue));
        Assert.Contains($"remote={Uri.EscapeDataString("https://example.test/owner/a.git")}", call.Query);
        Assert.Contains($"remote={Uri.EscapeDataString("https://example.test/owner/b.git")}", call.Query);
        Assert.Contains("clones=true", call.Query);

        // Nothing was cloned: both were already there.
        Assert.Empty(h.Clone.Requested);
    }

    [Fact]
    public async Task Clones_is_absent_from_the_queue_read_without_a_workspace()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        var call = Assert.Single(h.Wire.To("GET", Queue));
        Assert.DoesNotContain("clones=true", call.Query);
    }

    [Fact]
    public async Task A_primary_with_no_local_checkout_is_cloned_after_the_claim_and_before_the_reset_then_spawned_there()
    {
        using var h = new Harness();
        var workspace = Path.Combine(h.Temp, "clones");
        var runtime = WithWorkspace(h, workspace);

        var repo = Fixtures.Repository(
            "https://example.test/owner/repo.git", canonical: "example.test/owner/repo", primary: true, matchedRemote: null);

        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1") });
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", repositories: [repo]));
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-1/claim", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        var claimedByThen = false;
        h.Workspace.Watching = () => claimedByThen = h.Wire.Count("POST", "/api/hatch/issues/AER-1/claim") == 1;

        Assert.Equal(0, await new GoToWorkCommand(runtime).RunAsync(["--once"], default));

        var expected = Checkouts.PathFor(workspace, "example.test/owner/repo");
        var cloned = Assert.Single(h.Clone.Requested);
        Assert.Equal("https://example.test/owner/repo.git", cloned.Remote);
        Assert.Equal(expected, cloned.Path);

        Assert.True(claimedByThen, "the clone happened before the workspace was reset, but after the claim");
        Assert.Contains(expected, h.Workspace.Prepared);

        var spawned = Assert.Single(h.Sessions.Spawned);
        Assert.Equal(expected, spawned.Root);
    }

    [Fact]
    public async Task A_remote_bound_by_two_projects_across_a_whole_night_is_cloned_once()
    {
        using var h = new Harness();
        var workspace = Path.Combine(h.Temp, "clones");
        var runtime = WithWorkspace(h, workspace);

        var repo = Fixtures.Repository(
            "https://example.test/owner/shared.git", canonical: "example.test/owner/shared", primary: true, matchedRemote: null);

        h.Wire.Once(
            "GET", Queue, HttpStatusCode.OK,
            System.Text.Json.JsonSerializer.Serialize(new[] { Fixtures.Row("AER-1"), Fixtures.Row("AER-2") }, Fixtures.Json));
        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-2") });

        foreach (var key in new[] { "AER-1", "AER-2" })
        {
            h.Wire.Reply("POST", $"/api/hatch/issues/{key}/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
            h.Wire.Reply("POST", $"/api/hatch/issues/{key}/claim/heartbeat", HttpStatusCode.NoContent);
            h.Wire.Json("GET", $"/api/hatch/work/{key}", Fixtures.Work(key, repositories: [repo]));
            h.Wire.Reply("DELETE", $"/api/hatch/issues/{key}/claim", HttpStatusCode.NoContent);
            h.Wire.Json("POST", $"/api/hatch/issues/{key}/work-log", Fixtures.WorkLogRow());
            h.Wire.Json("GET", $"/api/hatch/issues/{key}/questions", Array.Empty<QuestionDto>());
        }

        Assert.Equal(0, await new GoToWorkCommand(runtime).RunAsync(["--max-runs", "2"], default));

        Assert.Single(h.Clone.Requested);
        Assert.Equal(2, h.Sessions.Spawned.Count);
    }

    [Fact]
    public async Task A_failed_clone_releases_the_lease_comments_the_ticket_walks_on_and_three_end_the_night()
    {
        using var h = new Harness();
        var workspace = Path.Combine(h.Temp, "clones");
        var runtime = WithWorkspace(h, workspace);

        h.Clone.Error = "fatal: could not read from remote repository.";

        // All three clear at once, so the walk inside a single PickAsync call
        // (Attempts = 5) tries all of them - no interval wait needed between
        // passes to reach three failures.
        var keys = new[] { "AER-1", "AER-2", "AER-3" };
        h.Wire.Json("GET", Queue, keys.Select(k => Fixtures.Row(k)).ToArray());

        foreach (var key in keys)
        {
            var repo = Fixtures.Repository(
                $"https://example.test/owner/{key}.git", canonical: $"example.test/owner/{key}",
                primary: true, matchedRemote: null);

            h.Wire.Reply("POST", $"/api/hatch/issues/{key}/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
            h.Wire.Reply("POST", $"/api/hatch/issues/{key}/claim/heartbeat", HttpStatusCode.NoContent);
            h.Wire.Json("GET", $"/api/hatch/work/{key}", Fixtures.Work(key, repositories: [repo]));
            h.Wire.Reply("DELETE", $"/api/hatch/issues/{key}/claim", HttpStatusCode.NoContent);
            h.Wire.Json($"POST", $"/api/hatch/issues/{key}/comments",
                new CommentDto(1, "hatch", "noted", "comment", null, null, DateTimeOffset.UnixEpoch));
        }

        // A short interval: the walk records all three failures inside its one
        // pass, so only the wait before the loop notices `ShouldStop` is real.
        await new GoToWorkCommand(runtime).RunAsync(["--interval", "1"], default);

        Assert.Empty(h.Sessions.Spawned);
        foreach (var key in keys)
        {
            Assert.Single(h.Wire.To("POST", $"/api/hatch/issues/{key}/claim"));
            Assert.Single(h.Wire.To("DELETE", $"/api/hatch/issues/{key}/claim"));

            var comment = Assert.Single(h.Wire.To("POST", $"/api/hatch/issues/{key}/comments"));
            var body = comment.Read<CommentCreateRequest>().Body;
            Assert.Contains("could not clone", body, StringComparison.Ordinal);
            Assert.Contains("fatal: could not read from remote repository.", body, StringComparison.Ordinal);
        }

        Assert.Equal(3, h.Clone.Requested.Count);
        Assert.Contains(h.Say.Said, l => l.Contains("three increments in a row failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_lock_held_on_the_workspace_itself_refuses_a_second_loop_naming_it()
    {
        using var h = new Harness();
        var workspace = Path.Combine(h.Temp, "clones");
        Directory.CreateDirectory(workspace);
        var runtime = WithWorkspace(h, workspace);

        using var held = LoopLock.Take(workspace, h.Temp, out _);
        Assert.NotNull(held);

        Assert.Equal(1, await new GoToWorkCommand(runtime).RunAsync(["--once"], default));

        Assert.Contains(h.Say.Complained, l => l.Contains("already running in", StringComparison.Ordinal));
        Assert.Contains(h.Say.Complained, l => l.Contains(workspace, StringComparison.Ordinal));
        Assert.Empty(h.Wire.Calls);

        // The standing checkout's own lock was released on the way out, same as
        // any other refusal before a claim: this loop never got past the door.
        using var retaken = LoopLock.Take(h.Root, h.Temp, out var refusal);
        Assert.NotNull(retaken);
        Assert.Equal("", refusal);
    }

    [Fact]
    public async Task Without_a_workspace_a_primary_matching_nothing_ends_changed_under_us_with_no_clone_attempted()
    {
        using var h = new Harness();
        var repo = Fixtures.Repository("https://example.test/elsewhere.git", primary: true, matchedRemote: null);

        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1") });
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", repositories: [repo]));
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-1/claim", HttpStatusCode.NoContent);

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        Assert.Empty(h.Sessions.Spawned);
        Assert.Empty(h.Clone.Requested);
        Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
    }
}

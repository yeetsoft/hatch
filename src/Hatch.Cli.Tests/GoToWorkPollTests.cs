using System.Net;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Cli.Tests;

/// <summary>
/// Where the poll sits in the loop: after the heartbeat, before the pass, on
/// every kind of pass, never while paused - and never able to end a night.
/// What the poll does once it runs is <see cref="PollTests"/>'s.
/// </summary>
public sealed class GoToWorkPollTests
{
    private const string Queue = "/api/hatch/work/queue";
    private const string Review = "/api/hatch/work/review";
    private const string Beat = "/api/hatch/runners/test%3A%2Fcheckout";
    private const string PrUrl = "https://github.com/o/r/pull/1";

    private static readonly string Trunk = new('a', 40);
    private static readonly string Tip = new('b', 40);

    private static void OneTicket(Harness h, string key = "AER-1")
    {
        h.Wire.Json("GET", Queue, new[] { Fixtures.Row(key) });
        h.Wire.Reply("POST", $"/api/hatch/issues/{key}/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        h.Wire.Reply("POST", $"/api/hatch/issues/{key}/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Reply("DELETE", $"/api/hatch/issues/{key}/claim", HttpStatusCode.NoContent);
        h.Wire.Json("GET", $"/api/hatch/work/{key}", Fixtures.Work(key, from: "In Review"));
        h.Wire.Json("POST", $"/api/hatch/issues/{key}/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", $"/api/hatch/issues/{key}/questions", Array.Empty<QuestionDto>());
    }

    /// <summary>One issue in review whose branch has stopped merging, and a board that takes the verdict.</summary>
    private static void InReview(
        Harness h, string key = "AER-9", HttpStatusCode put = HttpStatusCode.OK, string? pullRequestUrl = null)
    {
        h.Wire.Json("GET", Review, new[] { Fixtures.Review(key, pullRequestUrl: pullRequestUrl) });
        h.Wire.Reply(
            "PUT", $"/api/hatch/issues/{key}/merge-check", put,
            put == HttpStatusCode.OK
                ? System.Text.Json.JsonSerializer.Serialize(Fixtures.MergeCheck(), Fixtures.Json)
                : "\"no\"");

        h.Workspace.HeadsFor[h.Root] = new RemoteHeads(
            "main", new Dictionary<string, string> { ["main"] = Trunk, [$"{key.ToLowerInvariant()}-thing"] = Tip });
        h.Workspace.Verdicts[(h.Root, key)] = new Verdict(
            MergeVerdicts.Conflicted, "main", Trunk, $"{key.ToLowerInvariant()}-thing", Tip, ["a.txt"]);
    }

    [Fact]
    public async Task The_poll_runs_after_the_heartbeat_and_before_the_pass_reads_the_queue()
    {
        using var h = new Harness();
        OneTicket(h);
        InReview(h);

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        var routes = h.Wire.Calls.Select(c => c.Route).ToList();
        var beat = routes.IndexOf($"POST {Beat}");
        var put = routes.IndexOf("PUT /api/hatch/issues/AER-9/merge-check");
        var queue = routes.IndexOf($"GET {Queue}");

        // The merge train now reads review independently, on its own clock, so
        // more than one "GET Review" may appear - the one this test cares about
        // is the poll's own, which is always the one immediately before its own
        // PUT, in the main loop's own sequential order, whatever the train's
        // independent ticks did around it.
        var review = routes.Take(put).ToList().FindLastIndex(r => r == $"GET {Review}");

        Assert.True(beat >= 0 && beat < review, "the board hears the runner is alive first");
        Assert.True(review < put && put < queue, "the verdict is put before the pass reads the queue");
    }

    [Fact]
    public async Task Once_polls_once_and_then_the_pass_runs()
    {
        using var h = new Harness();
        OneTicket(h);
        InReview(h);

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        // Two, now: the poll's own, plus the merge train's own independent
        // first tick - unconditional and immediate, on its own clock, the
        // moment the loop starts regardless of --once.
        Assert.Equal(2, h.Wire.To("GET", Review).Count);
        Assert.Single(h.Wire.To("PUT", "/api/hatch/issues/AER-9/merge-check"));
        Assert.Single(h.Sessions.Spawned);
    }

    /// <summary>
    /// The poll belongs to the loop and not to the pass, so a board with nothing
    /// an agent may move is polled all the same.
    /// </summary>
    [Fact]
    public async Task An_idle_board_is_polled_too()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());
        InReview(h);

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        Assert.Single(h.Wire.To("PUT", "/api/hatch/issues/AER-9/merge-check"));
        Assert.Empty(h.Sessions.Spawned);
    }

    [Fact]
    public async Task A_busy_board_is_polled_too()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1") });
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.Conflict, "\"hatch is working this from other:/tree, last heard from 8 seconds ago\"");
        InReview(h);

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        Assert.Single(h.Wire.To("PUT", "/api/hatch/issues/AER-9/merge-check"));
    }

    [Fact]
    public async Task A_paused_runner_polls_nothing_but_the_merge_train_keeps_ticking()
    {
        using var h = new Harness();
        OneTicket(h);
        InReview(h, pullRequestUrl: PrUrl);
        h.Wire.Json("POST", Beat, new RunnerInstructionDto("paused", null, null, null, null));

        using var interrupting = new CancellationTokenSource();
        var running = new GoToWorkCommand(h.Runtime)
            .RunAsync(["--interval", "1", "--merge-interval", "1"], interrupting.Token);
        await Harness.Eventually(() => h.Wire.To("POST", Beat).Count >= 2, "a paused runner to heartbeat twice");

        // The train's own call to the forge, proof it ticked while the loop
        // reads as paused - the old rule, that nothing here calls Review at
        // all, no longer holds once the train is ticking on its own clock.
        await Harness.Eventually(() => h.Forge.PullRequestReads.Count >= 1, "the train to read the pull request while paused");

        await interrupting.CancelAsync();
        await running;

        Assert.Empty(h.Wire.To("PUT", "/api/hatch/issues/AER-9/merge-check"));
        Assert.Empty(h.Workspace.Calls);
    }

    [Fact]
    public async Task An_exhausted_runner_polls_nothing_but_the_merge_train_keeps_ticking()
    {
        using var h = new Harness();
        OneTicket(h);
        InReview(h, pullRequestUrl: PrUrl);
        h.Wire.Json("POST", Beat, new RunnerInstructionDto("running", null, null, null, null));

        Assert.True(new NightState { ExhaustedUntil = DateTimeOffset.UtcNow.AddHours(1) }
            .Write(h.Supervised.NightStatePath));

        using var interrupting = new CancellationTokenSource();
        var running = new GoToWorkCommand(h.Supervised)
            .RunAsync(["--interval", "1", "--restart-after", "0", "--merge-interval", "1"], interrupting.Token);
        await Harness.Eventually(() => h.Wire.To("POST", Beat).Count >= 2, "an exhausted runner to heartbeat twice");

        // Same proof as the paused case: the train ticks through exhaustion too.
        await Harness.Eventually(() => h.Forge.PullRequestReads.Count >= 1, "the train to read the pull request while exhausted");

        await interrupting.CancelAsync();
        await running;

        Assert.Empty(h.Wire.To("PUT", "/api/hatch/issues/AER-9/merge-check"));

        // The one exception: the wait's own self-check keeps this checkout
        // current, so it may hold a "prepare" call - never an "enter".
        Assert.DoesNotContain(h.Workspace.Calls, c => c.StartsWith("enter ", StringComparison.Ordinal));
    }

    /// <summary>
    /// The clock runs on its own interval, independent of --interval - proven
    /// by a merge-row fixture and a merge-interval well inside one
    /// --interval-paced iteration.
    /// </summary>
    [Fact]
    public async Task The_merge_train_ticks_at_least_twice_within_one_interval()
    {
        using var h = new Harness();
        OneTicket(h);
        InReview(h, pullRequestUrl: PrUrl);
        h.Wire.Json("POST", Beat, new RunnerInstructionDto("paused", null, null, null, null));

        using var interrupting = new CancellationTokenSource();
        var running = new GoToWorkCommand(h.Runtime)
            .RunAsync(["--interval", "3", "--merge-interval", "1"], interrupting.Token);
        await Harness.Eventually(() => h.Forge.PullRequestReads.Count >= 2, "the train to tick at least twice");
        await interrupting.CancelAsync();
        await running;
    }

    /// <summary>Stopping the loop - here, by --once finishing - stops the train's own clock with it.</summary>
    [Fact]
    public async Task Stopping_the_loop_leaves_no_further_train_call_observable()
    {
        using var h = new Harness();
        OneTicket(h);
        InReview(h, pullRequestUrl: PrUrl);

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once", "--merge-interval", "1"], default);

        var countAfter = h.Forge.PullRequestReads.Count;
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        Assert.Equal(countAfter, h.Forge.PullRequestReads.Count);
    }

    /// <summary>
    /// Nothing the train does moves the run tally, the spend, or what was
    /// spawned - holding regardless of how many ticks fired in the one fast
    /// pass --once makes, which is why this does not wait for any.
    /// </summary>
    [Fact]
    public async Task The_merge_trains_ticks_never_touch_the_spawn_count_or_the_tally()
    {
        using var h = new Harness();
        OneTicket(h);
        InReview(h, pullRequestUrl: PrUrl);

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once", "--merge-interval", "1"], default);

        Assert.Single(h.Sessions.Spawned);
        Assert.Contains(h.Say.Said, l => l.Contains("1 increment(s) in", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains("$1.50", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_verdict_the_board_refuses_does_not_end_the_night_or_count_as_a_failed_increment()
    {
        using var h = new Harness();
        OneTicket(h);
        InReview(h, put: HttpStatusCode.BadRequest);

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        // The pass that follows is untouched: the ticket is worked as it would
        // have been.
        Assert.Single(h.Sessions.Spawned);
        Assert.Contains(h.Say.Complained, l => l.Contains("would not take the verdict", StringComparison.Ordinal));
        Assert.DoesNotContain(h.Say.Said, l => l.Contains("three increments in a row failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_board_without_the_route_costs_one_line_and_the_night_goes_on()
    {
        using var h = new Harness();
        OneTicket(h);

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        Assert.Single(h.Sessions.Spawned);
        Assert.Contains(h.Say.Complained, l => l.Contains("could not read what is in review", StringComparison.Ordinal));
    }

    /// <summary>A runner that cannot read builds says so once and goes on: the pass that follows works whatever the forge said.</summary>
    [Fact]
    public async Task A_forge_that_cannot_answer_costs_one_line_and_the_night_goes_on()
    {
        using var h = new Harness();
        OneTicket(h);
        InReview(h);
        h.Wire.Json("PUT", "/api/hatch/issues/AER-9/build-check", Fixtures.Build());
        h.Forge.Answer = new ForgeAnswer(null, "gh is not installed");

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        Assert.Single(h.Sessions.Spawned);
        Assert.Empty(h.Wire.To("PUT", "/api/hatch/issues/AER-9/build-check"));
        Assert.Single(h.Say.Complained, l => l.Contains("could not read builds", StringComparison.Ordinal));
    }

    /// <summary>
    /// Timed by the interval and not by the iteration: a loop that finishes an
    /// increment and goes straight on asks git nothing more than one that waited.
    /// </summary>
    [Fact]
    public async Task Increments_back_to_back_within_one_interval_poll_once()
    {
        using var h = new Harness();
        OneTicket(h);
        InReview(h);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 27, 2, 0, 0, TimeSpan.Zero));

        await new GoToWorkCommand(h.Runtime with { Clock = clock }).RunAsync(["--max-runs", "2", "--interval", "60"], default);

        Assert.Equal(2, h.Sessions.Spawned.Count);

        // One from the poll, gated by --interval on the fake clock as this test
        // is really about, plus one from the merge train's own independent
        // first tick - unaffected by the fake clock, since its own delay is
        // real time and the two increments run well inside it.
        Assert.Equal(2, h.Wire.To("GET", Review).Count);
    }

    [Fact]
    public async Task A_poll_after_the_interval_asks_again()
    {
        using var h = new Harness();
        OneTicket(h);
        InReview(h);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 27, 2, 0, 0, TimeSpan.Zero))
        {
            AutoAdvanceAmount = TimeSpan.FromMinutes(5),
        };

        await new GoToWorkCommand(h.Runtime with { Clock = clock }).RunAsync(["--max-runs", "2", "--interval", "60"], default);

        Assert.Equal(2, h.Sessions.Spawned.Count);

        // Two from the poll, one per increment as the fake clock's own advance
        // re-opens its gate each time, plus one from the merge train's own
        // independent first tick.
        Assert.Equal(3, h.Wire.To("GET", Review).Count);
    }
}

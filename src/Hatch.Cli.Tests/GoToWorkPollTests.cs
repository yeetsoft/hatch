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
    private static void InReview(Harness h, string key = "AER-9", HttpStatusCode put = HttpStatusCode.OK)
    {
        h.Wire.Json("GET", Review, new[] { Fixtures.Review(key) });
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
        var review = routes.IndexOf($"GET {Review}");
        var put = routes.IndexOf("PUT /api/hatch/issues/AER-9/merge-check");
        var queue = routes.IndexOf($"GET {Queue}");

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

        Assert.Single(h.Wire.To("GET", Review));
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
    public async Task A_paused_runner_polls_nothing()
    {
        using var h = new Harness();
        OneTicket(h);
        InReview(h);
        h.Wire.Json("POST", Beat, new RunnerInstructionDto("paused", null, null, null, null));

        using var interrupting = new CancellationTokenSource();
        var running = new GoToWorkCommand(h.Runtime).RunAsync(["--interval", "1"], interrupting.Token);
        await Harness.Eventually(() => h.Wire.To("POST", Beat).Count >= 2, "a paused runner to heartbeat twice");
        await interrupting.CancelAsync();
        await running;

        Assert.Empty(h.Wire.To("GET", Review));
        Assert.Empty(h.Wire.To("PUT", "/api/hatch/issues/AER-9/merge-check"));
        Assert.Empty(h.Workspace.Calls);
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
        Assert.Single(h.Wire.To("GET", Review));
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
        Assert.Equal(2, h.Wire.To("GET", Review).Count);
    }
}

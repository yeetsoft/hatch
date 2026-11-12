using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Cli.Tests;

/// <summary>
/// A loop ends its own session on the way out, and a fresh loop that finds the
/// checkout's session still live waits to see whether it is a dead loop's
/// leftovers or a real second loop.
/// </summary>
public sealed class GoToWorkSessionTests
{
    private const string Beat = "/api/runners/sessions";
    private const string End = "/api/runners/sessions/7/end";
    private const string Queue = "/api/work/queue";
    private const string Claim = "/api/issues/AER-1/claim";

    private static readonly DateTimeOffset Start = new(2026, 9, 8, 2, 0, 0, TimeSpan.Zero);

    private static void OneTicket(Harness h)
    {
        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1") });
        h.Wire.Reply("POST", Claim, HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        h.Wire.Reply("POST", $"{Claim}/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Reply("DELETE", Claim, HttpStatusCode.NoContent);
        h.Wire.Json("GET", "/api/work/AER-1", Fixtures.Work("AER-1", from: "In Review"));
        h.Wire.Json("POST", "/api/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/issues/AER-1/questions", Array.Empty<QuestionDto>());
    }

    private static RunnerInstructionDto Told(string state = "running", int? maxRuns = null) =>
        new(state, null, maxRuns, null, null, SessionId: 7);

    private static string Refusal(DateTimeOffset lastSeenAt, int goneIn) =>
        JsonSerializer.Serialize(
            new RunnerSessionRefusalDto("test:/checkout is already the runner on elsewhere:/tree", lastSeenAt, goneIn),
            Fixtures.Json);

    private static FakeTimeProvider Clock() => new(Start);

    /// <summary>
    /// Steps a fake clock forward until <paramref name="run"/> finishes: the
    /// wait is a <c>Task.Delay</c> on the runtime's clock, which nothing else
    /// reads, so it only elapses when something advances the clock from outside.
    /// </summary>
    private static async Task<int> DriveAsync(FakeTimeProvider clock, Task<int> run)
    {
        for (var i = 0; !run.IsCompleted; i++)
        {
            Assert.True(i < 1_000, "the clock-driven run never finished");
            clock.Advance(TimeSpan.FromSeconds(5));
            await Task.Delay(5);
        }

        return await run;
    }

    [Fact]
    public async Task A_loop_that_reaches_max_runs_ends_its_session_once()
    {
        using var h = new Harness();
        OneTicket(h);
        h.Wire.Json("POST", Beat, Told(maxRuns: 1));
        h.Wire.Reply("POST", End, HttpStatusCode.NoContent);

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--max-runs", "1"], default));

        Assert.Single(h.Wire.To("POST", End));
    }

    [Fact]
    public async Task A_loop_the_board_stops_ends_its_session_once()
    {
        using var h = new Harness();
        h.Wire.Json("POST", Beat, Told("stopping"));
        h.Wire.Reply("POST", End, HttpStatusCode.NoContent);

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync([], default));

        Assert.Single(h.Wire.To("POST", End));
    }

    [Fact]
    public async Task A_loop_that_hits_its_stop_file_ends_its_session_once()
    {
        using var h = new Harness();
        var stop = Path.Combine(h.Temp, "stop");
        h.Wire.Json("POST", Beat, Told());
        h.Wire.Reply("POST", End, HttpStatusCode.NoContent);
        h.Wire.Json("GET", Queue, Array.Empty<object>());

        using var cts = new CancellationTokenSource();
        var running = new GoToWorkCommand(h.Runtime).RunAsync(["--stop-file", stop, "--interval", "1"], cts.Token);
        await Harness.Eventually(() => h.Wire.Count("POST", Beat) > 0, "the first heartbeat");
        File.WriteAllText(stop, "");

        Assert.Equal(0, await running);
        Assert.Single(h.Wire.To("POST", End));
    }

    [Fact]
    public async Task A_loop_stopped_by_Ctrl_C_ends_its_session_though_its_own_token_is_cancelled()
    {
        using var h = new Harness();
        h.Wire.Json("POST", Beat, Told("paused"));
        h.Wire.Reply("POST", End, HttpStatusCode.NoContent);

        using var cts = new CancellationTokenSource();
        var running = new GoToWorkCommand(h.Runtime).RunAsync(["--standby", "--interval", "3600"], cts.Token);
        await Harness.Eventually(() => h.Wire.Count("POST", Beat) > 0, "the first heartbeat");
        await Harness.Eventually(
            () => h.Say.Said.Any(l => l.Contains("the board has this runner paused", StringComparison.Ordinal)),
            "the board's paused line");

        cts.Cancel();
        await running;

        Assert.Single(h.Wire.To("POST", End));
    }

    [Fact]
    public async Task A_once_pass_ends_no_session_since_it_may_have_joined_a_loops()
    {
        using var h = new Harness();
        OneTicket(h);
        h.Wire.Json("POST", Beat, Told());
        h.Wire.Reply("POST", End, HttpStatusCode.NoContent);

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        Assert.Empty(h.Wire.To("POST", End));
    }

    [Fact]
    public async Task A_loop_handing_over_to_a_restarted_build_leaves_the_session_for_its_successor()
    {
        using var h = new Harness();
        OneTicket(h);
        h.Wire.Json("POST", Beat, Told());
        h.Wire.Reply("POST", End, HttpStatusCode.NoContent);

        var pointer = Path.Combine(h.Temp, "profile");
        var profiles = Path.Combine(h.Temp, "profiles");
        Directory.CreateDirectory(profiles);
        File.WriteAllText(pointer, "day");
        File.WriteAllText(Path.Combine(profiles, "day"), "");
        File.WriteAllText(Path.Combine(profiles, "night"), "");
        var runtime = h.Supervised with { ProfileName = "day", ActiveProfilePath = pointer, ProfilesDirectory = profiles };

        var finished = h.Sessions.Behaviour;
        h.Sessions.Behaviour = async (request, onLine, ct) =>
        {
            var result = await finished(request, onLine, ct);
            File.WriteAllText(pointer, "night");
            return result;
        };

        Assert.Equal(GoToWorkCommand.RestartExitCode, await new GoToWorkCommand(runtime).RunAsync([], default));

        Assert.Empty(h.Wire.To("POST", End));
    }

    [Fact]
    public async Task A_fresh_loop_that_finds_a_crashed_loops_session_waits_says_so_once_and_starts()
    {
        var clock = Clock();
        using var h = new Harness();
        OneTicket(h);
        h.Wire.Once("POST", Beat, HttpStatusCode.Conflict, Refusal(Start.AddSeconds(-70), 20));
        h.Wire.Once("POST", Beat, HttpStatusCode.Conflict, Refusal(Start.AddSeconds(-70), 15));
        h.Wire.Json("POST", Beat, Told(maxRuns: 1));
        h.Wire.Reply("POST", End, HttpStatusCode.NoContent);

        var runtime = h.Runtime with { Clock = clock };
        Assert.Equal(0, await DriveAsync(clock, new GoToWorkCommand(runtime).RunAsync(["--max-runs", "1"], default)));

        var waiting = h.Say.Said.Where(l => l.Contains("has not ended its session", StringComparison.Ordinal)).ToList();
        var line = Assert.Single(waiting);
        Assert.Contains("waiting up to 20s", line, StringComparison.Ordinal);
        Assert.True(h.Wire.Count("POST", Beat) >= 3, "two refused beats, then the one that was answered");
        Assert.Single(h.Wire.To("POST", Claim));
        Assert.Single(h.Wire.To("POST", End));
    }

    [Fact]
    public async Task A_fresh_loop_whose_held_session_beats_again_while_it_waits_is_refused_with_the_sentence()
    {
        var clock = Clock();
        using var h = new Harness();
        OneTicket(h);
        h.Wire.Once("POST", Beat, HttpStatusCode.Conflict, Refusal(Start.AddSeconds(-30), 60));
        h.Wire.Reply("POST", Beat, HttpStatusCode.Conflict, Refusal(Start.AddSeconds(-2), 88));
        h.Wire.Reply("POST", End, HttpStatusCode.NoContent);

        var runtime = h.Runtime with { Clock = clock };
        Assert.Equal(1, await DriveAsync(clock, new GoToWorkCommand(runtime).RunAsync([], default)));

        Assert.True(h.Say.Mentions("already the runner on elsewhere:/tree"));
        Assert.Empty(h.Sessions.Spawned);
        Assert.Empty(h.Wire.To("POST", Claim));
        Assert.Empty(h.Wire.To("POST", End));
        Assert.Equal(2, h.Wire.Count("POST", Beat));
    }

    [Fact]
    public async Task A_fresh_loop_gives_up_when_the_held_session_never_goes_and_never_beats()
    {
        var clock = Clock();
        using var h = new Harness();
        h.Wire.Reply("POST", Beat, HttpStatusCode.Conflict, Refusal(Start.AddSeconds(-30), 20));

        var runtime = h.Runtime with { Clock = clock };
        Assert.Equal(1, await DriveAsync(clock, new GoToWorkCommand(runtime).RunAsync([], default)));

        Assert.True(h.Say.Mentions("already the runner on elsewhere:/tree"));
        Assert.InRange(h.Wire.Count("POST", Beat), 2, 12);
    }

    [Fact]
    public async Task An_older_hatch_that_answers_a_bare_sentence_refuses_at_once_with_no_wait()
    {
        using var h = new Harness();
        h.Wire.Reply("POST", Beat, HttpStatusCode.Conflict, "\"test:/checkout is already the runner on elsewhere:/tree\"");

        Assert.Equal(1, await new GoToWorkCommand(h.Runtime).RunAsync([], default));

        Assert.Single(h.Wire.To("POST", Beat));
        Assert.DoesNotContain(h.Say.Said, l => l.Contains("has not ended its session", StringComparison.Ordinal));
    }
}

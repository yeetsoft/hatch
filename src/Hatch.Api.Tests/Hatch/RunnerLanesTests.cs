using Hatch.Api.Modules.Hatch;
using Hatch.Contracts;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The pure fold behind the runners' lanes on the board health read: one cell
/// for each bar, in the order the story gives, pinned against histories built
/// in memory. No route, no database.
/// </summary>
public class RunnerLanesTests
{
    // An 8-hour window is 20-minute bars: the last starts 12:00, so From is 04:20 and To 12:20.
    private static readonly DateTimeOffset Now = new(2026, 1, 31, 12, 7, 30, TimeSpan.Zero);

    private static DateTimeOffset At(int hour, int minute, int second = 0) =>
        new(2026, 1, 31, hour, minute, second, TimeSpan.Zero);

    private static readonly DateTimeOffset From = At(4, 20);

    private static readonly BoardHealthReport.Axis Axis = BoardHealthReport.AxisOf(8, Now);

    private const string Running = RunnerStateRule.Running;
    private const string Gone = RunnerStateRule.Gone;

    private static RunnerStateRule.Change State(DateTimeOffset at, string state, string? kind = null) => new(at, state, kind);

    /// <summary>A runner heard from at noon, live now, running since before the window opened.</summary>
    private static RunnerLanes.RunnerInput Runner(string name, params RunnerStateRule.Change[] timeline) =>
        new(name, At(12, 0), true, timeline);

    private static RunnerLanes.RunnerInput Up(string name) => Runner(name, State(At(4, 0), Running));

    private static BoardHealthReport.ClaimEvent Claim(long issue, long id, string kind, DateTimeOffset at, string actor, string? payload = null) =>
        new(issue, id, kind, at, actor, payload);

    private static BoardHealthReport.ClaimEvent Take(long issue, long id, DateTimeOffset at, string actor) =>
        Claim(issue, id, EfHatchIssueEvent.ClaimTaken, at, actor);

    private static BoardHealthReport.ClaimEvent Release(long issue, long id, DateTimeOffset at, string actor, string? outcome = null) =>
        Claim(issue, id, EfHatchIssueEvent.ClaimReleased, at, actor, outcome is null ? null : $"{{\"outcome\":\"{outcome}\"}}");

    private static RunnerLanes.Result Fold(
        RunnerLanes.RunnerInput[] runners, BoardHealthReport.ClaimEvent[]? claims = null,
        BoardHealthReport.ForwardMove[]? moves = null, RunnerLanes.Stall[]? stalls = null,
        RunnerLanes.FailedSession[]? failed = null) =>
        RunnerLanes.Fold(runners, claims ?? [], moves ?? [], stalls ?? [], failed ?? [], Axis, Now);

    /// <summary>The cell of the bar holding <paramref name="at"/>.</summary>
    private static RunnerLanes.Cell Cell(RunnerLanes.Lane lane, DateTimeOffset at) =>
        lane.Cells[(int)((at - From).TotalMinutes / Axis.BarMinutes)];

    private static void Is(RunnerLanes.Lane lane, DateTimeOffset at, string state, string? why = null)
    {
        var cell = Cell(lane, at);
        Assert.Equal((state, why), (cell.State, cell.Why));
    }

    private static RunnerLanes.Lane Only(RunnerLanes.Result result) => Assert.Single(result.Lanes);

    private static BoardHealthReport.ForwardMove RunnerMove(long issue, DateTimeOffset at, string actor) =>
        new(issue, issue * 1000 + at.Minute, at, BoardHealthReport.MoveKind.Runner, actor);

    // ---- The eight cells ----

    [Fact]
    public void ALane_HasTwentyFourCells_OldestFirst()
    {
        var lane = Only(Fold([Up("r1")]));

        Assert.Equal(24, lane.Cells.Count);
        Assert.All(lane.Cells, c => Assert.Equal(RunnerLanes.Idle, c.State));
    }

    [Fact]
    public void LetGo_ADroppedRelease_IsTheActorsAndNamesWhy()
    {
        var lane = Only(Fold([Up("r1")],
            claims: [Take(1, 1, At(5, 0), "r1"), Release(1, 2, At(5, 30), "r1", ClaimOutcomes.Dropped)]));

        Is(lane, At(5, 30), RunnerLanes.LetGo, RunnerLanes.Dropped);
        Is(lane, At(5, 0), RunnerLanes.Working);
    }

    [Fact]
    public void LetGo_ADroppedReleaseWhoseTakeIsBeforeTheWindow_StillCounts()
    {
        var lane = Only(Fold([Up("r1")], claims: [Release(1, 2, At(5, 30), "r1", ClaimOutcomes.Dropped)]));

        Is(lane, At(5, 30), RunnerLanes.LetGo, RunnerLanes.Dropped);
    }

    [Fact]
    public void LetGo_ALapse_BelongsToTheRunnerThatHeld_NotTheOneThatTookOver()
    {
        var claims = new[]
        {
            Take(1, 1, At(5, 0), "r1"),
            Claim(1, 2, EfHatchIssueEvent.ClaimLapsed, At(6, 10), "r2"),
            Take(1, 3, At(6, 10), "r2"),
        };

        var result = Fold([Up("r1"), Up("r2")], claims);

        Is(result.Lanes.Single(l => l.Name == "r1"), At(6, 10), RunnerLanes.LetGo, RunnerLanes.Lapsed);
        Is(result.Lanes.Single(l => l.Name == "r2"), At(6, 10), RunnerLanes.Working);
    }

    [Fact]
    public void LetGo_AStall_IsTheActors()
    {
        var lane = Only(Fold([Up("r1")], stalls: [new RunnerLanes.Stall(1, At(7, 0), "r1")]));

        Is(lane, At(7, 0), RunnerLanes.LetGo, RunnerLanes.Stalled);
    }

    [Fact]
    public void LetGo_AFailedSession_BelongsToTheHolderOpenWhenItReachedTheBoard()
    {
        var claims = new[] { Take(1, 1, At(5, 0), "r1"), Release(1, 2, At(5, 45), "r1") };

        var result = Fold([Up("r1"), Up("r2")], claims, failed: [new RunnerLanes.FailedSession(1, At(5, 30))]);

        Is(result.Lanes.Single(l => l.Name == "r1"), At(5, 30), RunnerLanes.LetGo, RunnerLanes.Errored);
        Assert.DoesNotContain(result.Lanes.Single(l => l.Name == "r2").Cells, c => c.State == RunnerLanes.LetGo);
    }

    [Fact]
    public void LetGo_AFailedSessionWithNoHolder_BelongsToNobody()
    {
        var lane = Only(Fold([Up("r1")], failed: [new RunnerLanes.FailedSession(1, At(5, 30))]));

        Assert.DoesNotContain(lane.Cells, c => c.State == RunnerLanes.LetGo);
    }

    [Fact]
    public void LetGo_SeveralInOneBar_SayTheFirstOfDroppedLapsedStalledErrored()
    {
        var claims = new[]
        {
            Take(1, 1, At(5, 0), "r1"), Release(1, 2, At(5, 5), "r1", ClaimOutcomes.Dropped),
            Take(2, 3, At(5, 0), "r1"), Claim(2, 4, EfHatchIssueEvent.ClaimLapsed, At(5, 8), "r2"),
        };
        var stalls = new[] { new RunnerLanes.Stall(3, At(5, 6), "r1") };

        var lane = Only(Fold([Up("r1")], claims, stalls: stalls));
        Is(lane, At(5, 0), RunnerLanes.LetGo, RunnerLanes.Dropped);

        var noDrop = Only(Fold([Up("r1")], claims.Skip(2).ToArray(), stalls: stalls));
        Is(noDrop, At(5, 0), RunnerLanes.LetGo, RunnerLanes.Lapsed);

        var onlyStall = Only(Fold([Up("r1")], stalls: stalls, failed: [new RunnerLanes.FailedSession(9, At(5, 7))]));
        Is(onlyStall, At(5, 0), RunnerLanes.LetGo, RunnerLanes.Stalled);
    }

    [Theory]
    [InlineData(ClaimOutcomes.Preempted)]
    [InlineData(ClaimOutcomes.Recused)]
    [InlineData(ClaimOutcomes.Worked)]
    [InlineData(ClaimOutcomes.Unservable)]
    public void AReleaseThatIsNotADrop_ReadsAsWorking(string outcome)
    {
        var lane = Only(Fold([Up("r1")],
            claims: [Take(1, 1, At(5, 0), "r1"), Release(1, 2, At(5, 30), "r1", outcome)]));

        Is(lane, At(5, 30), RunnerLanes.Working);
        Assert.DoesNotContain(lane.Cells, c => c.State == RunnerLanes.LetGo);
    }

    [Fact]
    public void Down_Exhausted_IsOutOfUsage()
    {
        var lane = Only(Fold([Runner("r1", State(At(4, 0), Running), State(At(6, 0), RunnerStateRule.Exhausted), State(At(7, 0), Running))]));

        Is(lane, At(6, 20), RunnerLanes.Down, RunnerLanes.OutOfUsage);
        Is(lane, At(7, 0), RunnerLanes.Idle);
    }

    [Theory]
    [InlineData(RunnerAgentDownKinds.NotStarting, "not-starting")]
    [InlineData(RunnerAgentDownKinds.SignedOut, "signed-out")]
    [InlineData(null, "agent-down")]
    [InlineData("something-new", "agent-down")]
    public void Down_AgentDown_NamesItsKind_OrJustSaysAgentDown(string? kind, string why)
    {
        var lane = Only(Fold([Runner("r1", State(At(4, 0), Running), State(At(6, 0), RunnerStateRule.AgentDown, kind), State(At(7, 0), Running))]));

        Is(lane, At(6, 20), RunnerLanes.Down, why);
    }

    [Fact]
    public void Down_AgentDownBeatsExhausted_WhenBothAreInOneBar()
    {
        var lane = Only(Fold([Runner("r1",
            State(At(4, 0), Running), State(At(6, 0), RunnerStateRule.Exhausted),
            State(At(6, 5), RunnerStateRule.AgentDown, RunnerAgentDownKinds.SignedOut), State(At(7, 0), Running))]));

        Is(lane, At(6, 0), RunnerLanes.Down, "signed-out");
    }

    [Fact]
    public void Moved_ARunnersForwardMove_IsDrawnInItsBar()
    {
        var lane = Only(Fold([Up("r1")],
            claims: [Take(1, 1, At(5, 0), "r1")], moves: [RunnerMove(1, At(5, 10), "r1")]));

        Is(lane, At(5, 10), RunnerLanes.Moved);
        Is(lane, At(5, 30), RunnerLanes.Working);
    }

    [Fact]
    public void Moved_AMoveByAnotherActorOrKind_IsNotThisRunners()
    {
        var moves = new[]
        {
            RunnerMove(1, At(5, 10), "r2"),
            new BoardHealthReport.ForwardMove(1, 2, At(5, 30), BoardHealthReport.MoveKind.Person, "r1"),
            new BoardHealthReport.ForwardMove(1, 3, At(5, 50), BoardHealthReport.MoveKind.Board, "r1"),
        };

        var lane = Only(Fold([Up("r1")], moves: moves));

        Assert.DoesNotContain(lane.Cells, c => c.State == RunnerLanes.Moved);
    }

    [Fact]
    public void Working_AClaimHeldAtAnyTimeInTheBar_Counts()
    {
        var lane = Only(Fold([Up("r1")],
            claims: [Take(1, 1, At(5, 18), "r1"), Release(1, 2, At(5, 22), "r1")]));

        Is(lane, At(5, 0), RunnerLanes.Working);
        Is(lane, At(5, 20), RunnerLanes.Working);
        Is(lane, At(5, 40), RunnerLanes.Idle);
        Is(lane, At(4, 40), RunnerLanes.Idle);
    }

    [Fact]
    public void Working_ATakeOverAnOpenClaim_ClosesTheFirst()
    {
        var claims = new[] { Take(1, 1, At(5, 0), "r1"), Take(1, 2, At(6, 0), "r2") };

        var result = Fold([Up("r1"), Up("r2")], claims);

        var r1 = result.Lanes.Single(l => l.Name == "r1");
        Is(r1, At(5, 30), RunnerLanes.Working);
        Is(r1, At(6, 30), RunnerLanes.Idle);
        Is(result.Lanes.Single(l => l.Name == "r2"), At(6, 30), RunnerLanes.Working);
    }

    [Fact]
    public void Working_AClearedClaim_StopsAtTheClear()
    {
        var lane = Only(Fold([Up("r1")],
            claims: [Take(1, 1, At(5, 0), "r1"), Claim(1, 2, EfHatchIssueEvent.ClaimCleared, At(5, 30), "ann")]));

        Is(lane, At(5, 30), RunnerLanes.Working);
        Is(lane, At(5, 40), RunnerLanes.Idle);
    }

    [Fact]
    public void Working_AClaimOpenAcrossAGoneStretch_IsNotWorkingWhileGone()
    {
        var lane = Only(Fold([Runner("r1",
            State(At(4, 0), Running), State(At(6, 0), Gone), State(At(9, 0), Running))],
            claims: [Take(1, 1, At(5, 0), "r1")]));

        Is(lane, At(5, 40), RunnerLanes.Working);
        Is(lane, At(6, 20), RunnerLanes.Off);
        Is(lane, At(8, 40), RunnerLanes.Off);
        Is(lane, At(9, 20), RunnerLanes.Idle);
    }

    [Fact]
    public void Working_AClaimEndedByALapse_IsHeldOnlyUntilTheRunnerWentGone()
    {
        var claims = new[]
        {
            Take(1, 1, At(5, 0), "r1"),
            Claim(1, 2, EfHatchIssueEvent.ClaimLapsed, At(9, 0), "r2"),
            Take(1, 3, At(9, 0), "r2"),
        };

        var lane = Only(Fold([Runner("r1", State(At(4, 0), Running), State(At(6, 0), Gone))], claims));

        Is(lane, At(5, 40), RunnerLanes.Working);
        Is(lane, At(7, 0), RunnerLanes.Off);
        Is(lane, At(9, 0), RunnerLanes.LetGo, RunnerLanes.Lapsed);
    }

    [Fact]
    public void Idle_RunningOrStopping_WithNothingHeld()
    {
        var lane = Only(Fold([Runner("r1",
            State(At(4, 0), Running), State(At(6, 0), RunnerStates.Stopping), State(At(7, 0), Gone))]));

        Is(lane, At(5, 0), RunnerLanes.Idle);
        Is(lane, At(6, 20), RunnerLanes.Idle);
        Is(lane, At(7, 20), RunnerLanes.Off);
    }

    [Fact]
    public void Paused_APersonsPause_HasNoWhy()
    {
        var lane = Only(Fold([Runner("r1", State(At(4, 0), Running), State(At(6, 0), RunnerStateRule.Paused))]));

        Is(lane, At(6, 20), RunnerLanes.Paused);
    }

    [Fact]
    public void Paused_ABoardPause_IsStoredAsAgentDown_ButReadsAsPausedByTheBoard()
    {
        var lane = Only(Fold([Runner("r1",
            State(At(4, 0), Running), State(At(6, 0), RunnerStateRule.AgentDown, RunnerAgentDownKinds.BoardPaused))]));

        Is(lane, At(6, 20), RunnerLanes.Paused, RunnerLanes.Board);
        Assert.DoesNotContain(lane.Cells, c => c.State == RunnerLanes.Down);
    }

    [Fact]
    public void Off_TheTimelineCoversTheBarAndNoneOfItIsUp()
    {
        var lane = Only(Fold([Runner("r1", State(At(3, 0), Running), State(At(5, 0), Gone))]));

        Is(lane, At(4, 40), RunnerLanes.Idle);
        Is(lane, At(5, 20), RunnerLanes.Off);
        Is(lane, At(12, 0), RunnerLanes.Off);
    }

    [Fact]
    public void Blank_BeforeTheFirstRow_IsNotIdleOrOff()
    {
        var lane = Only(Fold([Runner("r1", State(At(8, 0), Running))]));

        Is(lane, At(4, 20), RunnerLanes.Blank);
        Is(lane, At(7, 40), RunnerLanes.Blank);
        Is(lane, At(8, 0), RunnerLanes.Idle);
    }

    [Fact]
    public void Blank_BeforeTheFirstRow_StillShowsAClaim_AndAMove_AndALetGo()
    {
        var claims = new[] { Take(1, 1, At(5, 0), "r1"), Release(1, 2, At(5, 30), "r1"), Take(2, 3, At(6, 0), "r1"), Release(2, 4, At(6, 10), "r1", ClaimOutcomes.Dropped) };

        var lane = Only(Fold([Runner("r1", State(At(8, 0), Running))], claims, moves: [RunnerMove(1, At(5, 10), "r1")]));

        Is(lane, At(5, 10), RunnerLanes.Moved);
        Is(lane, At(5, 30), RunnerLanes.Working);
        Is(lane, At(6, 0), RunnerLanes.LetGo, RunnerLanes.Dropped);
        Is(lane, At(4, 20), RunnerLanes.Blank);
        Is(lane, At(7, 0), RunnerLanes.Blank);
    }

    [Fact]
    public void Blank_ARunnerWithNoRowsAtAll_ShowsItsClaimsAlone_AndHasALaneForThem()
    {
        var lane = Only(Fold([Runner("r1")], claims: [Take(1, 1, At(5, 0), "r1"), Release(1, 2, At(5, 30), "r1")]));

        Is(lane, At(5, 20), RunnerLanes.Working);
        Is(lane, At(8, 0), RunnerLanes.Blank);
        Assert.Equal(22, lane.Cells.Count(c => c.State == RunnerLanes.Blank));
    }

    [Fact]
    public void TheBarTheFirstRowFallsIn_IsCovered()
    {
        var lane = Only(Fold([Runner("r1", State(At(8, 10), Running))]));

        Is(lane, At(7, 40), RunnerLanes.Blank);
        Is(lane, At(8, 0), RunnerLanes.Idle);
    }

    [Fact]
    public void TheBarTheFirstRowFallsIn_IsOff_WhenTheFirstRowIsGone()
    {
        var lane = Only(Fold([Runner("r1", State(At(8, 10), Gone), State(At(10, 0), Running))]));

        Is(lane, At(8, 0), RunnerLanes.Off);
        Is(lane, At(7, 40), RunnerLanes.Blank);
    }

    [Fact]
    public void AFirstRowAtTheBarsEnd_BelongsToTheNextBar()
    {
        var lane = Only(Fold([Runner("r1", State(At(8, 20), Running))]));

        Is(lane, At(8, 0), RunnerLanes.Blank);
        Is(lane, At(8, 20), RunnerLanes.Idle);
    }

    [Fact]
    public void TheLastBar_EndsAtNow()
    {
        var lane = Only(Fold([Up("r1")], claims: [Take(1, 1, At(12, 7, 30), "r1")]));

        Is(lane, At(12, 0), RunnerLanes.Working);

        var later = Only(Fold([Up("r1")], claims: [Take(1, 1, At(12, 8), "r1")]));
        Is(later, At(12, 0), RunnerLanes.Idle);
    }

    // ---- Order between neighbours ----

    [Fact]
    public void Order_LetGoBeatsDown_BeatsMoved_BeatsWorking_BeatsIdle_BeatsPaused()
    {
        // One bar, 05:00 to 05:20, gets each fact in turn.
        var claims = new[] { Take(1, 1, At(4, 50), "r1") };
        var moves = new[] { RunnerMove(1, At(5, 5), "r1") };
        var down = new[] { State(At(4, 0), Running), State(At(5, 10), RunnerStateRule.Exhausted), State(At(5, 15), RunnerStateRule.Paused) };
        var letGo = new[] { new RunnerLanes.Stall(1, At(5, 12), "r1") };

        Is(Only(Fold([Runner("r1", down)], claims, moves, letGo)), At(5, 0), RunnerLanes.LetGo, RunnerLanes.Stalled);
        Is(Only(Fold([Runner("r1", down)], claims, moves)), At(5, 0), RunnerLanes.Down, RunnerLanes.OutOfUsage);

        var upAndPaused = new[] { State(At(4, 0), Running), State(At(5, 10), RunnerStateRule.Paused) };
        Is(Only(Fold([Runner("r1", upAndPaused)], claims, moves)), At(5, 0), RunnerLanes.Moved);
        Is(Only(Fold([Runner("r1", upAndPaused)], claims)), At(5, 0), RunnerLanes.Working);
        Is(Only(Fold([Runner("r1", upAndPaused)])), At(5, 0), RunnerLanes.Idle);
        Is(Only(Fold([Runner("r1", State(At(4, 0), RunnerStateRule.Paused), State(At(5, 10), Gone))])), At(5, 0), RunnerLanes.Paused);
    }

    [Fact]
    public void Order_ALetGoShowsEvenWhereTheRunnerWasGone()
    {
        var lane = Only(Fold([Runner("r1", State(At(4, 0), Running), State(At(5, 0), Gone))],
            stalls: [new RunnerLanes.Stall(1, At(6, 0), "r1")]));

        Is(lane, At(6, 0), RunnerLanes.LetGo, RunnerLanes.Stalled);
    }

    // ---- A lane, or none ----

    [Fact]
    public void ARunnerThatWasNeverUp_HasNoLane()
    {
        var result = Fold([
            Runner("gone", State(At(3, 0), Running), State(At(4, 0), Gone)),
            Runner("never"),
            Runner("late", State(At(13, 0), Running)),
        ]);

        Assert.Empty(result.Lanes);
        Assert.Equal((0, 0), (result.Up, result.Paused));
    }

    [Fact]
    public void ARunnerThatWasUpForOneBar_HasALane()
    {
        var result = Fold([Runner("r1", State(At(11, 0), Running), State(At(11, 10), Gone))]);

        var lane = Only(result);
        Assert.False(lane.Up);
        Is(lane, At(11, 0), RunnerLanes.Idle);
        Is(lane, At(10, 40), RunnerLanes.Blank);
        Is(lane, At(11, 20), RunnerLanes.Off);
    }

    // ---- Order and counts ----

    [Fact]
    public void Lanes_AreUpNowFirst_ThenMostRecentlyHeardFrom_ThenByNameOrdinal()
    {
        RunnerLanes.RunnerInput Input(string name, DateTimeOffset seen, params RunnerStateRule.Change[] t) => new(name, seen, true, t);

        var running = new[] { State(At(4, 0), Running) };
        var wasUp = new[] { State(At(4, 0), Running), State(At(10, 0), Gone) };

        var result = Fold([
            Input("b-older", At(9, 0), running),
            Input("gone-newest", At(11, 50), wasUp),
            Input("b-same", At(11, 0), running),
            Input("a-same", At(11, 0), running),
            Input("B-same", At(11, 0), running),
            Input("newest", At(12, 0), running),
        ]);

        Assert.Equal(["newest", "B-same", "a-same", "b-same", "b-older", "gone-newest"], result.Lanes.Select(l => l.Name));
    }

    [Fact]
    public void Counts_UpAreLanesNotGoneNow_PausedAreThoseOfThemPaused_BoardPauseIncluded()
    {
        var result = Fold([
            Up("running"),
            Runner("paused", State(At(4, 0), RunnerStateRule.Paused)),
            Runner("board", State(At(4, 0), Running), State(At(10, 0), RunnerStateRule.AgentDown, RunnerAgentDownKinds.BoardPaused)),
            Runner("down", State(At(4, 0), Running), State(At(10, 0), RunnerStateRule.AgentDown, RunnerAgentDownKinds.SignedOut)),
            Runner("gone", State(At(4, 0), Running), State(At(10, 0), Gone)),
            Runner("nobody"),
        ]);

        Assert.Equal(5, result.Lanes.Count);
        Assert.Equal(4, result.Up);
        Assert.Equal(2, result.Paused);
        Assert.Equal(new[] { "board", "down", "gone", "paused", "running" }, result.Lanes.Select(l => l.Name).Order());
        Assert.True(result.Lanes.Single(l => l.Name == "board").Paused);
        Assert.False(result.Lanes.Single(l => l.Name == "down").Paused);
        Assert.False(result.Lanes.Single(l => l.Name == "gone").Up);
    }

    [Fact]
    public void Now_WithNoRows_IsWhetherTheRunnerIsLive()
    {
        var live = Only(Fold([new RunnerLanes.RunnerInput("r1", At(12, 0), true, [])], claims: [Take(1, 1, At(5, 0), "r1")]));
        var dead = Only(Fold([new RunnerLanes.RunnerInput("r1", At(12, 0), false, [])], claims: [Take(1, 1, At(5, 0), "r1")]));

        Assert.True(live.Up);
        Assert.False(dead.Up);
        Assert.False(live.Paused);
    }

    [Fact]
    public void ALane_CarriesTheRunnersLastSeenAt()
    {
        var lane = Only(Fold([new RunnerLanes.RunnerInput("r1", At(11, 11), true, [State(At(4, 0), Running)])]));

        Assert.Equal(At(11, 11), lane.LastSeenAt);
    }

    // ---- The two folds read one rule ----

    [Fact]
    public void AMovedCell_IsABarWhereTheMovedFoldCountsARunnerMove()
    {
        var board = new List<EfHatchStatus>
        {
            new() { Id = 1, Name = "backlog", SortOrder = 10 },
            new() { Id = 2, Name = "todo", SortOrder = 20 },
            new() { Id = 3, Name = "doing", SortOrder = 30 },
            new() { Id = 4, Name = "review", SortOrder = 40 },
            new() { Id = 5, Name = "done", SortOrder = 50, IsTerminal = true },
        };
        string Name(int id) => board.Single(s => s.Id == id).Name;
        BoardHealthReport.Move Move(long id, DateTimeOffset at, int from, int to, string actor) =>
            new(1, id, at, actor, $"{{\"from\":\"{Name(from)}\",\"fromId\":{from},\"to\":\"{Name(to)}\",\"toId\":{to}}}");

        var issue = new BoardHealthReport.Issue(new TimelineReport.Row(1, null, 1, "HA", 1, "task", 4, 1, At(4, 0), "ann"), 0, false);
        var moves = new[]
        {
            Move(10, At(5, 10), 1, 2, "r1"),   // under r1's claim: a runner's
            Move(11, At(6, 10), 2, 3, "ann"),  // by a person
            Move(12, At(7, 10), 3, 4, "r1"),   // r1 let go at 07:00: a person's, whoever made it
            Move(13, At(8, 10), 4, 3, "r1"),   // backwards: nobody's
        };
        var claims = new[]
        {
            Take(1, 1, At(5, 0), "r1"), Release(1, 2, At(5, 30), "r1"),
            Take(1, 3, At(6, 0), "r1"), Release(1, 4, At(7, 0), "r1"),
        };

        var fold = BoardHealthReport.Fold(
            [issue], moves, claims, [], [], [], board, 8, Now, null, new HashSet<long>(), new Dictionary<int, long>(), null);
        var forward = BoardHealthReport.ForwardMoves(moves, claims, board, Axis);
        var lane = Only(RunnerLanes.Fold([Up("r1")], claims, forward, [], [], Axis, Now));

        var counted = fold.Bars.Select((b, k) => b.Runner > 0 ? k : -1).Where(k => k >= 0).ToList();
        var drawn = lane.Cells.Select((c, k) => c.State == RunnerLanes.Moved ? k : -1).Where(k => k >= 0).ToList();

        Assert.Equal([2], counted);
        Assert.Equal(counted, drawn);
    }
}

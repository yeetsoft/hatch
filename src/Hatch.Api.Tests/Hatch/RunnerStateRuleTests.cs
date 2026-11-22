using Hatch.Api.Modules.Hatch;
using Hatch.Contracts;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The rule that reads a runner's state off the rows it speaks through: how the
/// sources rank, what ends one by the clock, and the instants it does. Facts are
/// built in memory - no database, no clock.
/// </summary>
public class RunnerStateRuleTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    private static RunnerStateRule.Source Live(
        string state = RunnerStates.Running, DateTimeOffset? seen = null, DateTimeOffset? exhausted = null,
        DateTimeOffset? down = null, string? kind = null, DateTimeOffset? ended = null, bool retired = false) =>
        new(seen ?? T0, ended, retired, state, exhausted, down, kind);

    private static RunnerStateRule.Answer At(DateTimeOffset t, params RunnerStateRule.Source[] sources) =>
        RunnerStateRule.At(sources, Limit, t);

    // ---- Ranking ----

    [Fact]
    public void ALiveSourceWithNothingWrong_IsRunning() =>
        Assert.Equal(RunnerStateRule.Running, At(T0, Live()).State);

    [Theory]
    [InlineData(RunnerStates.Paused)]
    [InlineData(RunnerStates.Stopping)]
    public void AskedToPauseOrStop_IsThatState(string asked) =>
        Assert.Equal(asked, At(T0, Live(asked)).State);

    [Fact]
    public void Exhausted_BeatsPaused() =>
        Assert.Equal("exhausted", At(T0, Live(RunnerStates.Paused, exhausted: T0.AddMinutes(10))).State);

    [Fact]
    public void AgentDown_BeatsExhausted_AndCarriesItsKind()
    {
        var answer = At(T0, Live(exhausted: T0.AddMinutes(10), down: T0.AddMinutes(5), kind: RunnerAgentDownKinds.SignedOut));

        Assert.Equal("agent-down", answer.State);
        Assert.Equal(RunnerAgentDownKinds.SignedOut, answer.Kind);
    }

    [Fact]
    public void OnlyAgentDown_CarriesAKind()
    {
        Assert.Null(At(T0, Live(exhausted: T0.AddMinutes(10))).Kind);
        Assert.Null(At(T0, Live(RunnerStates.Paused, kind: RunnerAgentDownKinds.NotStarting)).Kind);
    }

    [Fact]
    public void TheRunnerTakesItsHighestRankedSource()
    {
        var answer = At(
            T0,
            Live(),
            Live(RunnerStates.Paused),
            Live(exhausted: T0.AddMinutes(1)));

        Assert.Equal("exhausted", answer.State);
    }

    [Fact]
    public void AgentDownKind_IsTheWinningSourcesOwn()
    {
        var answer = At(
            T0,
            Live(down: T0.AddMinutes(5), kind: RunnerAgentDownKinds.NotStarting),
            Live());

        Assert.Equal(RunnerAgentDownKinds.NotStarting, answer.Kind);
    }

    [Fact]
    public void PausedAndStopping_Tie_AndTheOneHeardFromMoreRecentlyWins()
    {
        var paused = Live(RunnerStates.Paused, seen: T0.AddSeconds(10));
        var stopping = Live(RunnerStates.Stopping, seen: T0.AddSeconds(20));

        Assert.Equal(RunnerStates.Stopping, At(T0.AddSeconds(30), paused, stopping).State);
        Assert.Equal(RunnerStates.Stopping, At(T0.AddSeconds(30), stopping, paused).State);

        var later = Live(RunnerStates.Paused, seen: T0.AddSeconds(25));
        Assert.Equal(RunnerStates.Paused, At(T0.AddSeconds(30), later, stopping).State);
        Assert.Equal(RunnerStates.Paused, At(T0.AddSeconds(30), stopping, later).State);
    }

    [Fact]
    public void PausedAndStopping_BothBeatRunning()
    {
        Assert.Equal(RunnerStates.Paused, At(T0, Live(), Live(RunnerStates.Paused)).State);
        Assert.Equal(RunnerStates.Stopping, At(T0, Live(), Live(RunnerStates.Stopping)).State);
    }

    [Fact]
    public void AnHourlyTokenThrottle_ReadsAsRunning()
    {
        // The row has no throttle field at all: a throttled session is a running one.
        Assert.Equal(RunnerStateRule.Running, At(T0, Live()).State);
    }

    // ---- Gone ----

    [Fact]
    public void NoSources_ReadsGone() =>
        Assert.Equal(RunnerStateRule.Gone, At(T0).State);

    [Fact]
    public void ASourceIsGone_AtOrAfterItsLastBeatPlusTheLimit()
    {
        var source = Live();

        Assert.Equal(RunnerStateRule.Running, At(T0 + Limit - TimeSpan.FromTicks(1), source).State);
        Assert.Equal(RunnerStateRule.Gone, At(T0 + Limit, source).State);
        Assert.Equal(RunnerStateRule.Gone, At(T0 + Limit + TimeSpan.FromSeconds(1), source).State);
    }

    [Fact]
    public void AGoneSource_IsGone_WhateverItWasToldOrSaid()
    {
        var source = Live(RunnerStates.Paused, exhausted: T0.AddHours(1), down: T0.AddHours(1), kind: RunnerAgentDownKinds.SignedOut);

        Assert.Equal(RunnerStateRule.Gone, At(T0 + Limit, source).State);
    }

    [Fact]
    public void AGoneSource_CountsOnlyWhenEverySourceIsGone()
    {
        var old = Live(seen: T0.AddMinutes(-10));
        var fresh = Live(seen: T0);

        Assert.Equal(RunnerStateRule.Running, At(T0, old, fresh).State);
        Assert.Equal(RunnerStateRule.Gone, At(T0.AddMinutes(5), old, fresh).State);
    }

    [Fact]
    public void ASessionThatEndedItself_IsGoneAtOnce_ASweptOneAtTheLimit()
    {
        var ended = T0.AddSeconds(5);
        var itself = Live(seen: T0, ended: ended);
        var swept = Live(seen: T0, ended: T0 + Limit + TimeSpan.FromMinutes(3));

        Assert.Equal(RunnerStateRule.Running, At(T0.AddSeconds(4), itself).State);
        Assert.Equal(RunnerStateRule.Gone, At(ended, itself).State);

        Assert.Equal(RunnerStateRule.Running, At(T0 + Limit - TimeSpan.FromTicks(1), swept).State);
        Assert.Equal(RunnerStateRule.Gone, At(T0 + Limit, swept).State);
    }

    [Fact]
    public void ARetiredHeartbeat_IsGoneFromItsLastBeat()
    {
        var retired = Live(seen: T0, retired: true);

        Assert.Equal(RunnerStateRule.Running, At(T0.AddTicks(-1), retired).State);
        Assert.Equal(RunnerStateRule.Gone, At(T0, retired).State);
        Assert.Equal(RunnerStateRule.Gone, At(T0.AddSeconds(1), retired).State);
    }

    [Fact]
    public void ARetiredHeartbeat_NeverOutranksALiveSession()
    {
        var retired = Live(RunnerStates.Paused, seen: T0, retired: true);
        var session = Live(seen: T0);

        Assert.Equal(RunnerStateRule.Running, At(T0.AddSeconds(1), retired, session).State);
    }

    // ---- A stretch that ends ----

    [Fact]
    public void AnUntilHasRunOut_AtItsInstant()
    {
        var until = T0.AddSeconds(30);
        var source = Live(exhausted: until, down: until, kind: RunnerAgentDownKinds.NotStarting);

        Assert.Equal("agent-down", At(until - TimeSpan.FromTicks(1), source).State);
        Assert.Equal(RunnerStateRule.Running, At(until, source).State);
    }

    [Fact]
    public void WhenAnAgentDownRunsOut_TheExhaustedBeneathItShowsThrough()
    {
        var source = Live(exhausted: T0.AddSeconds(50), down: T0.AddSeconds(20), kind: RunnerAgentDownKinds.NotStarting);

        Assert.Equal("agent-down", At(T0.AddSeconds(19), source).State);
        Assert.Equal("exhausted", At(T0.AddSeconds(20), source).State);
    }

    // ---- The clock alone ----

    [Fact]
    public void ClockChanges_NamesTheInstantAnExhaustionRunsOut()
    {
        var until = T0.AddSeconds(30);
        var changes = RunnerStateRule.ClockChanges([Live(exhausted: until)], Limit, T0, T0.AddSeconds(45));

        var change = Assert.Single(changes);
        Assert.Equal(until, change.At);
        Assert.Equal(RunnerStateRule.Running, change.State);
    }

    [Fact]
    public void ClockChanges_OutOfAnAgentDown_NamesWhatIsLeft()
    {
        var source = Live(RunnerStates.Paused, down: T0.AddSeconds(20), kind: RunnerAgentDownKinds.BoardPaused);

        var change = Assert.Single(RunnerStateRule.ClockChanges([source], Limit, T0, T0.AddSeconds(40)));

        Assert.Equal(T0.AddSeconds(20), change.At);
        Assert.Equal(RunnerStateRule.Paused, change.State);
        Assert.Null(change.Kind);
    }

    [Fact]
    public void ClockChanges_ReadsTheRangeAsOpenAtTheStartAndClosedAtTheEnd()
    {
        var until = T0.AddSeconds(30);
        var source = Live(exhausted: until);

        Assert.Empty(RunnerStateRule.ClockChanges([source], Limit, until, T0.AddSeconds(45)));
        Assert.Single(RunnerStateRule.ClockChanges([source], Limit, T0, until));
        Assert.Empty(RunnerStateRule.ClockChanges([source], Limit, T0, until - TimeSpan.FromTicks(1)));
    }

    [Fact]
    public void ClockChanges_ChainsAnExhaustionIntoGone()
    {
        var source = Live(exhausted: T0.AddSeconds(30));

        var changes = RunnerStateRule.ClockChanges([source], Limit, T0, T0.AddMinutes(10));

        Assert.Collection(
            changes,
            c => { Assert.Equal(T0.AddSeconds(30), c.At); Assert.Equal(RunnerStateRule.Running, c.State); },
            c => { Assert.Equal(T0 + Limit, c.At); Assert.Equal(RunnerStateRule.Gone, c.State); });
    }

    [Fact]
    public void ClockChanges_SaysNothingForAnInstantThatChangesNothing()
    {
        // A second source's gone instant, while the first is still live and running.
        var a = Live(seen: T0.AddSeconds(30));
        var b = Live(seen: T0);

        Assert.Empty(RunnerStateRule.ClockChanges([a, b], Limit, T0, T0.AddSeconds(70)));
    }

    [Fact]
    public void ClockChanges_ForAnEndedSession_UsesTheEarlierOfItsEndAndTheLimit()
    {
        var ended = T0.AddSeconds(5);

        var changes = RunnerStateRule.ClockChanges([Live(ended: ended)], Limit, T0, T0.AddMinutes(5));

        var change = Assert.Single(changes);
        Assert.Equal(ended, change.At);
        Assert.Equal(RunnerStateRule.Gone, change.State);
    }

    [Fact]
    public void ClockChanges_ForNoSources_IsEmpty() =>
        Assert.Empty(RunnerStateRule.ClockChanges([], Limit, T0, T0.AddHours(1)));
}

namespace Hatch.Cli.Tests;

/// <summary>
/// The one object a live increment and the idle loop between them both write
/// to - what it remembers, and what it forgets.
/// </summary>
public sealed class ReadoutStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BeginningAnIncrement_ClearsWhateverIdleLineWasShowing()
    {
        var state = new ReadoutState();
        state.SetIdle("nothing on the board is an agent's to move", Now);

        state.BeginIncrement("HA-1", "A ticket", "In Progress -> In Review", null, Now);

        var snapshot = state.Snapshot();
        Assert.NotNull(snapshot.Increment);
        Assert.Null(snapshot.Idle);
    }

    [Fact]
    public void Activity_UpdatesTheRunningIncrementAndKeepsTheLastToolWhenNoneIsGiven()
    {
        var state = new ReadoutState();
        state.BeginIncrement("HA-1", "A ticket", "W", null, Now);

        state.Activity(100, Now.AddSeconds(1), "Bash  make build");
        state.Activity(250, Now.AddSeconds(2), lastTool: null);

        var inc = state.Snapshot().Increment;
        Assert.Equal(250, inc!.TokensSoFar);
        Assert.Equal(Now.AddSeconds(2), inc.LastActivityAt);
        Assert.Equal("Bash  make build", inc.LastTool);
    }

    [Fact]
    public void ActivityAfterTheIncrementEnded_IsDropped()
    {
        var state = new ReadoutState();
        state.BeginIncrement("HA-1", "A ticket", "W", null, Now);
        state.EndIncrement();

        state.Activity(999, Now, "Bash  rm -rf /");

        Assert.Null(state.Snapshot().Increment);
    }

    [Fact]
    public void EndingAnIncrement_LeavesNothingShowingUntilTheNextOneBegins()
    {
        var state = new ReadoutState();
        state.BeginIncrement("HA-1", "A ticket", "W", null, Now);

        state.EndIncrement();

        var snapshot = state.Snapshot();
        Assert.Null(snapshot.Increment);
        Assert.Null(snapshot.Idle);
    }

    [Fact]
    public void RunnerAndUsage_AreRememberedAcrossIncrements()
    {
        var state = new ReadoutState();
        var runner = new RunnerSnapshot("Chrissy", "Nathan", 3, TimeSpan.FromMinutes(5), 1.5m, null);
        state.SetRunner(runner);
        state.SetUsage([new UsageWindow("session", "Session", 0.5, null)], Now);

        state.BeginIncrement("HA-1", "A ticket", "W", null, Now);
        state.EndIncrement();

        var snapshot = state.Snapshot();
        Assert.Equal(runner, snapshot.Runner);
        Assert.Single(snapshot.UsageWindows);
    }

    [Fact]
    public void SetUsage_StampsTheInstantItWasRead_AndALaterCallMovesIt()
    {
        var state = new ReadoutState();

        state.SetUsage([new UsageWindow("session", "Session", 0.5, null)], Now);
        Assert.Equal(Now, state.Snapshot().UsageReadAt);

        var later = Now.AddMinutes(10);
        state.SetUsage([new UsageWindow("session", "Session", 0.6, null)], later);
        Assert.Equal(later, state.Snapshot().UsageReadAt);
    }
}

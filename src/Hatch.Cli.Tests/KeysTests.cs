namespace Hatch.Cli.Tests;

/// <summary>
/// <see cref="Keys.Decide"/>, the pure table - driven directly, with no <see
/// cref="Controls"/> and no console involved - and a couple of
/// <see cref="Controls"/>-level tests to check the table actually lands.
/// </summary>
public sealed class KeysTests
{
    [Fact]
    public void S_ArmsTheStopAndLogsIt()
    {
        var (stopArmed, paused, longLegend, confirming, cancelNow, skipNow, _, log) =
            Keys.Decide('s', stopArmed: false, paused: false, longLegend: false, Confirming.None, incrementRunning: false, napping: false);

        Assert.True(stopArmed);
        Assert.False(paused);
        Assert.False(longLegend);
        Assert.Equal(Confirming.None, confirming);
        Assert.False(cancelNow);
        Assert.False(skipNow);
        Assert.True(log is { Length: > 0 });
    }

    [Fact]
    public void S_AgainUnarmsTheStopAndLogsADifferentLine()
    {
        var (armed, _, _, _, _, _, _, armedLog) =
            Keys.Decide('s', stopArmed: false, paused: false, longLegend: false, Confirming.None, incrementRunning: false, napping: false);
        Assert.True(armed);

        var (unarmed, _, _, confirming, cancelNow, skipNow, _, unarmedLog) =
            Keys.Decide('s', stopArmed: armed, paused: false, longLegend: false, Confirming.None, incrementRunning: false, napping: false);

        Assert.False(unarmed);
        Assert.Equal(Confirming.None, confirming);
        Assert.False(cancelNow);
        Assert.False(skipNow);
        Assert.True(unarmedLog is { Length: > 0 });
        Assert.NotEqual(armedLog, unarmedLog);
    }

    [Fact]
    public void C_ArmsTheCancelConfirmationAndLogsNothing()
    {
        var (stopArmed, paused, longLegend, confirming, cancelNow, skipNow, _, log) =
            Keys.Decide('c', stopArmed: false, paused: false, longLegend: false, Confirming.None, incrementRunning: false, napping: false);

        Assert.False(stopArmed);
        Assert.False(paused);
        Assert.False(longLegend);
        Assert.Equal(Confirming.Cancel, confirming);
        Assert.False(cancelNow);
        Assert.False(skipNow);
        Assert.Null(log);
    }

    [Fact]
    public void C_thenY_ConfirmsTheCancel()
    {
        var (stopArmed, _, _, confirming, cancelNow, skipNow, _, log) =
            Keys.Decide('y', stopArmed: false, paused: false, longLegend: false, Confirming.Cancel, incrementRunning: false, napping: false);

        Assert.False(stopArmed);
        Assert.Equal(Confirming.None, confirming);
        Assert.True(cancelNow);
        Assert.False(skipNow);
        Assert.True(log is { Length: > 0 });
    }

    [Theory]
    [InlineData('x')]
    [InlineData('\r')] // Enter's char
    public void C_thenAnythingElse_TakesItBack(char key)
    {
        var (stopArmed, _, _, confirming, cancelNow, skipNow, _, log) =
            Keys.Decide(key, stopArmed: true, paused: false, longLegend: false, Confirming.Cancel, incrementRunning: false, napping: false);

        Assert.True(stopArmed);
        Assert.Equal(Confirming.None, confirming);
        Assert.False(cancelNow);
        Assert.False(skipNow);
        Assert.Null(log);
    }

    [Fact]
    public void AnUnknownKeyWithNothingArmed_ChangesNothingAndLogsNothing()
    {
        var (stopArmed, paused, longLegend, confirming, cancelNow, skipNow, _, log) =
            Keys.Decide('q', stopArmed: false, paused: false, longLegend: false, Confirming.None, incrementRunning: false, napping: false);

        Assert.False(stopArmed);
        Assert.False(paused);
        Assert.False(longLegend);
        Assert.Equal(Confirming.None, confirming);
        Assert.False(cancelNow);
        Assert.False(skipNow);
        Assert.Null(log);
    }

    // ---- p: pause and resume (HA-133) ----

    [Fact]
    public void P_PausesAndLogsIt()
    {
        var (_, paused, _, confirming, _, _, _, log) =
            Keys.Decide('p', stopArmed: false, paused: false, longLegend: false, Confirming.None, incrementRunning: false, napping: false);

        Assert.True(paused);
        Assert.Equal(Confirming.None, confirming);
        Assert.True(log is { Length: > 0 });
    }

    [Fact]
    public void P_AgainResumesAndLogsADifferentLine()
    {
        var (_, armed, _, _, _, _, _, armedLog) =
            Keys.Decide('p', stopArmed: false, paused: false, longLegend: false, Confirming.None, incrementRunning: false, napping: false);
        Assert.True(armed);

        var (_, resumed, _, _, _, _, _, resumedLog) =
            Keys.Decide('p', stopArmed: false, paused: armed, longLegend: false, Confirming.None, incrementRunning: false, napping: false);

        Assert.False(resumed);
        Assert.True(resumedLog is { Length: > 0 });
        Assert.NotEqual(armedLog, resumedLog);
    }

    // ---- k: skip this increment (HA-133) ----

    [Fact]
    public void K_WhileAnIncrementIsRunning_ArmsTheSkipConfirmationAndLogsNothing()
    {
        var (stopArmed, paused, longLegend, confirming, cancelNow, skipNow, _, log) =
            Keys.Decide('k', stopArmed: false, paused: false, longLegend: false, Confirming.None, incrementRunning: true, napping: false);

        Assert.False(stopArmed);
        Assert.False(paused);
        Assert.False(longLegend);
        Assert.Equal(Confirming.Skip, confirming);
        Assert.False(cancelNow);
        Assert.False(skipNow);
        Assert.Null(log);
    }

    [Fact]
    public void K_WithNoIncrementRunning_ChangesNothingAndLogsNothing()
    {
        var (stopArmed, paused, longLegend, confirming, cancelNow, skipNow, _, log) =
            Keys.Decide('k', stopArmed: false, paused: false, longLegend: false, Confirming.None, incrementRunning: false, napping: false);

        Assert.False(stopArmed);
        Assert.False(paused);
        Assert.False(longLegend);
        Assert.Equal(Confirming.None, confirming);
        Assert.False(cancelNow);
        Assert.False(skipNow);
        Assert.Null(log);
    }

    [Fact]
    public void K_thenY_ConfirmsTheSkip()
    {
        var (_, _, _, confirming, cancelNow, skipNow, _, log) =
            Keys.Decide('y', stopArmed: false, paused: false, longLegend: false, Confirming.Skip, incrementRunning: true, napping: false);

        Assert.Equal(Confirming.None, confirming);
        Assert.False(cancelNow);
        Assert.True(skipNow);
        Assert.True(log is { Length: > 0 });
    }

    [Theory]
    [InlineData('x')]
    [InlineData('\r')]
    public void K_thenAnythingElse_TakesItBack(char key)
    {
        var (_, _, _, confirming, cancelNow, skipNow, _, log) =
            Keys.Decide(key, stopArmed: false, paused: false, longLegend: false, Confirming.Skip, incrementRunning: true, napping: false);

        Assert.Equal(Confirming.None, confirming);
        Assert.False(cancelNow);
        Assert.False(skipNow);
        Assert.Null(log);
    }

    // ---- ?: the long legend (HA-133) ----

    [Fact]
    public void Question_TogglesLongLegendAndLogsNothingEitherWay()
    {
        var (_, _, on, _, _, _, _, onLog) =
            Keys.Decide('?', stopArmed: false, paused: false, longLegend: false, Confirming.None, incrementRunning: false, napping: false);

        Assert.True(on);
        Assert.Null(onLog);

        var (_, _, off, _, _, _, _, offLog) =
            Keys.Decide('?', stopArmed: false, paused: false, longLegend: on, Confirming.None, incrementRunning: false, napping: false);

        Assert.False(off);
        Assert.Null(offLog);
    }

    // ---- n: check the board now, during the idle nap ----

    [Fact]
    public void N_WhileNapping_ChecksNowAndLogs()
    {
        var (stopArmed, paused, longLegend, confirming, cancelNow, skipNow, checkNow, log) =
            Keys.Decide('n', stopArmed: false, paused: false, longLegend: false, Confirming.None, incrementRunning: false, napping: true);

        Assert.False(stopArmed);
        Assert.False(paused);
        Assert.False(longLegend);
        Assert.Equal(Confirming.None, confirming);
        Assert.False(cancelNow);
        Assert.False(skipNow);
        Assert.True(checkNow);
        Assert.True(log is { Length: > 0 });
    }

    [Fact]
    public void N_Capital_ChecksNowToo()
    {
        var (_, _, _, _, _, _, checkNow, _) =
            Keys.Decide('N', stopArmed: false, paused: false, longLegend: false, Confirming.None, incrementRunning: false, napping: true);

        Assert.True(checkNow);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void N_WhenNotNapping_DoesNothingAndLogsNothing(bool incrementRunning)
    {
        var (stopArmed, paused, longLegend, confirming, cancelNow, skipNow, checkNow, log) =
            Keys.Decide('n', stopArmed: false, paused: false, longLegend: false, Confirming.None, incrementRunning, napping: false);

        Assert.False(stopArmed);
        Assert.False(paused);
        Assert.False(longLegend);
        Assert.Equal(Confirming.None, confirming);
        Assert.False(cancelNow);
        Assert.False(skipNow);
        Assert.False(checkNow);
        Assert.Null(log);
    }

    [Fact]
    public void N_WhilePaused_DoesNothing()
    {
        var (_, paused, _, _, _, _, checkNow, log) =
            Keys.Decide('n', stopArmed: false, paused: true, longLegend: false, Confirming.None, incrementRunning: false, napping: true);

        Assert.True(paused);
        Assert.False(checkNow);
        Assert.Null(log);
    }

    [Theory]
    [InlineData(Confirming.Cancel)]
    [InlineData(Confirming.Skip)]
    public void N_DuringACancelOrSkipConfirmation_GoesBackAndDoesNotCheck(Confirming asked)
    {
        var (_, _, _, confirming, cancelNow, skipNow, checkNow, log) =
            Keys.Decide('n', stopArmed: false, paused: false, longLegend: false, asked, incrementRunning: true, napping: true);

        Assert.Equal(Confirming.None, confirming);
        Assert.False(cancelNow);
        Assert.False(skipNow);
        Assert.False(checkNow);
        Assert.Null(log);
    }

    [Fact]
    public void N_LeavesAnArmedStopArmed()
    {
        var (stopArmed, _, _, _, _, _, checkNow, _) =
            Keys.Decide('n', stopArmed: true, paused: false, longLegend: false, Confirming.None, incrementRunning: false, napping: true);

        Assert.True(stopArmed);
        Assert.True(checkNow);
    }

    // ---- Controls, not only the static table ----

    [Fact]
    public void ControlsKeysOn_SurvivesAPress()
    {
        var controls = new Controls(keysOn: true);

        Assert.True(controls.Snapshot().KeysOn);
        controls.Press('s');
        Assert.True(controls.Snapshot().KeysOn);
    }

    [Fact]
    public void Press_AppliesTheTableUnderTheLock()
    {
        var controls = new Controls(keysOn: true);

        var (log, cancelNow, skipNow) = controls.Press('s');
        var expected = Keys.Decide('s', stopArmed: false, paused: false, longLegend: false, Confirming.None, incrementRunning: false, napping: false);

        Assert.Equal(expected.Log, log);
        Assert.Equal(expected.CancelNow, cancelNow);
        Assert.Equal(expected.SkipNow, skipNow);
        Assert.Equal(expected.StopArmed, controls.Snapshot().StopArmed);
        Assert.Equal(expected.Confirming, controls.Snapshot().Confirming);
    }

    [Fact]
    public void Controls_NeverArmsTheSkipConfirmationWhileNoIncrementIsRunning()
    {
        var controls = new Controls(keysOn: true);
        controls.SetIncrementRunning(false);

        controls.Press('k');
        Assert.Equal(Confirming.None, controls.Snapshot().Confirming);

        controls.SetIncrementRunning(true);
        controls.Press('k');
        Assert.Equal(Confirming.Skip, controls.Snapshot().Confirming);
    }

    [Fact]
    public void Controls_RaisesSkippedExactlyOnceWhenKThenYConfirmIt()
    {
        var controls = new Controls(keysOn: true);
        controls.SetIncrementRunning(true);

        var raised = 0;
        controls.Skipped += () => raised++;

        controls.Press('k');
        controls.Press('y');

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Controls_RaisesCheckNowOnlyWhileNapping()
    {
        var controls = new Controls(keysOn: true);

        var raised = 0;
        controls.CheckNow += () => raised++;

        controls.SetNapping(false);
        controls.Press('n');
        Assert.Equal(0, raised);

        controls.SetNapping(true);
        controls.Press('n');
        Assert.Equal(1, raised);
    }
}

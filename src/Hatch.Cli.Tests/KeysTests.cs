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
        var (stopArmed, confirming, cancelNow, log) = Keys.Decide('s', stopArmed: false, Confirming.None);

        Assert.True(stopArmed);
        Assert.Equal(Confirming.None, confirming);
        Assert.False(cancelNow);
        Assert.True(log is { Length: > 0 });
    }

    [Fact]
    public void S_AgainUnarmsTheStopAndLogsADifferentLine()
    {
        var (armed, _, _, armedLog) = Keys.Decide('s', stopArmed: false, Confirming.None);
        Assert.True(armed);

        var (unarmed, confirming, cancelNow, unarmedLog) = Keys.Decide('s', stopArmed: armed, Confirming.None);

        Assert.False(unarmed);
        Assert.Equal(Confirming.None, confirming);
        Assert.False(cancelNow);
        Assert.True(unarmedLog is { Length: > 0 });
        Assert.NotEqual(armedLog, unarmedLog);
    }

    [Fact]
    public void C_ArmsTheCancelConfirmationAndLogsNothing()
    {
        var (stopArmed, confirming, cancelNow, log) = Keys.Decide('c', stopArmed: false, Confirming.None);

        Assert.False(stopArmed);
        Assert.Equal(Confirming.Cancel, confirming);
        Assert.False(cancelNow);
        Assert.Null(log);
    }

    [Fact]
    public void C_thenY_ConfirmsTheCancel()
    {
        var (stopArmed, confirming, cancelNow, log) = Keys.Decide('y', stopArmed: false, Confirming.Cancel);

        Assert.False(stopArmed);
        Assert.Equal(Confirming.None, confirming);
        Assert.True(cancelNow);
        Assert.True(log is { Length: > 0 });
    }

    [Theory]
    [InlineData('x')]
    [InlineData('\r')] // Enter's char
    public void C_thenAnythingElse_TakesItBack(char key)
    {
        var (stopArmed, confirming, cancelNow, log) = Keys.Decide(key, stopArmed: true, Confirming.Cancel);

        Assert.True(stopArmed);
        Assert.Equal(Confirming.None, confirming);
        Assert.False(cancelNow);
        Assert.Null(log);
    }

    [Fact]
    public void AnUnknownKeyWithNothingArmed_ChangesNothingAndLogsNothing()
    {
        var (stopArmed, confirming, cancelNow, log) = Keys.Decide('q', stopArmed: false, Confirming.None);

        Assert.False(stopArmed);
        Assert.Equal(Confirming.None, confirming);
        Assert.False(cancelNow);
        Assert.Null(log);
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

        var (log, cancelNow) = controls.Press('s');
        var expected = Keys.Decide('s', stopArmed: false, Confirming.None);

        Assert.Equal(expected.Log, log);
        Assert.Equal(expected.CancelNow, cancelNow);
        Assert.Equal(expected.StopArmed, controls.Snapshot().StopArmed);
        Assert.Equal(expected.Confirming, controls.Snapshot().Confirming);
    }
}

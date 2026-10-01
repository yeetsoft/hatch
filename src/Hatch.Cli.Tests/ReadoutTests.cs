namespace Hatch.Cli.Tests;

/// <summary>
/// What the readout says, as a pure function of a snapshot and the clock - no
/// terminal involved, per the split HA-120 draws between this and the drawing
/// class.
/// </summary>
public sealed class ReadoutTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly ControlsSnapshot NoControls = ReadoutState.NoControls;

    // ---- The increment's two rows ----

    [Fact]
    public void TheIssueRow_NamesTheKeyTheTitleTheMoveAndTheAddress()
    {
        var inc = new IncrementSnapshot(
            "HA-120", "Console: a pinned readout", "In Progress -> In Review",
            "https://hatch.example.test/apps/hatch/issues/HA-120", Now, 0, Now, null);
        var snapshot = new ReadoutSnapshot(inc, null, RunnerSnapshot.Empty, [], null, NoControls);

        var rows = Readout.Draw(snapshot, Now, 200, color: false);

        Assert.Contains("HA-120", rows[0], StringComparison.Ordinal);
        Assert.Contains("Console: a pinned readout", rows[0], StringComparison.Ordinal);
        Assert.Contains("In Progress -> In Review", rows[0], StringComparison.Ordinal);
        Assert.Contains("https://hatch.example.test/apps/hatch/issues/HA-120", rows[0], StringComparison.Ordinal);
    }

    [Fact]
    public void TheAliveRow_NamesElapsedTimeTokensAndHowLongItHasBeenQuiet()
    {
        var started = Now.AddMinutes(-12);
        var lastActivity = Now.AddSeconds(-14);
        var inc = new IncrementSnapshot("HA-1", "T", "W", null, started, 84_000, lastActivity, "Bash  make test-api");
        var snapshot = new ReadoutSnapshot(inc, null, RunnerSnapshot.Empty, [], null, NoControls);

        var rows = Readout.Draw(snapshot, Now, 200, color: false);

        Assert.Contains("12m00s elapsed", rows[1], StringComparison.Ordinal);
        Assert.Contains("84k tokens", rows[1], StringComparison.Ordinal);
        Assert.Contains("quiet 0m14s — Bash  make test-api", rows[1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(60, false)] // under 2 minutes: no colour
    [InlineData(150, true)] // past 2 minutes: warn
    public void TheQuietTimer_TurnsColourPastTwoMinutes(int quietSeconds, bool expectColour)
    {
        var inc = new IncrementSnapshot("HA-1", "T", "W", null, Now, 0, Now.AddSeconds(-quietSeconds), null);
        var snapshot = new ReadoutSnapshot(inc, null, RunnerSnapshot.Empty, [], null, NoControls);

        var row = Readout.Draw(snapshot, Now, 200, color: true)[1];

        Assert.Equal(expectColour, row.Contains("\x1b[33m", StringComparison.Ordinal));
    }

    [Fact]
    public void TheQuietTimer_TurnsTheDangerColourPastTenMinutes()
    {
        var inc = new IncrementSnapshot("HA-1", "T", "W", null, Now, 0, Now.AddMinutes(-11), null);
        var snapshot = new ReadoutSnapshot(inc, null, RunnerSnapshot.Empty, [], null, NoControls);

        var row = Readout.Draw(snapshot, Now, 200, color: true)[1];

        Assert.Contains("\x1b[31m", row, StringComparison.Ordinal);
    }

    [Fact]
    public void NoColour_MeansNoEscapeCodesAnywhere()
    {
        var inc = new IncrementSnapshot("HA-1", "T", "W", null, Now, 0, Now.AddMinutes(-11), null);
        var snapshot = new ReadoutSnapshot(inc, null, RunnerSnapshot.Empty, [], null, NoControls);

        var rows = Readout.Draw(snapshot, Now, 200, color: false);

        Assert.All(rows, r => Assert.DoesNotContain("\x1b[", r, StringComparison.Ordinal));
    }

    // ---- No increment: no issue or alive row ----

    [Fact]
    public void BetweenIncrements_ThereIsNoIssueOrAliveRow()
    {
        var idle = new IdleSnapshot("nothing on the board is an agent's to move", Now.AddSeconds(47));
        var snapshot = new ReadoutSnapshot(null, idle, RunnerSnapshot.Empty, [], null, NoControls);

        var rows = Readout.Draw(snapshot, Now, 200, color: false);

        Assert.DoesNotContain(rows, r => r.Contains("elapsed", StringComparison.Ordinal));
        Assert.Contains(rows, r => r.Contains("nothing on the board is an agent's to move", StringComparison.Ordinal));
        Assert.Contains(rows, r => r.Contains("looking again in 0m47s", StringComparison.Ordinal));
    }

    // ---- Usage windows ----

    [Fact]
    public void NoUsageReading_MeansNoUsageRowsAndNothingComplains()
    {
        var snapshot = new ReadoutSnapshot(null, null, RunnerSnapshot.Empty, [], null, NoControls);

        var rows = Readout.Draw(snapshot, Now, 200, color: false);

        Assert.DoesNotContain(rows, r => r.Contains('[') && r.Contains('%'));
    }

    [Fact]
    public void AUsageWindow_DrawsATwentyCellBarAndAPercentageAndAReset()
    {
        var window = new UsageWindow("session", "Session", 0.63, Now.AddHours(2));
        var snapshot = new ReadoutSnapshot(null, null, RunnerSnapshot.Empty, [window], null, NoControls);

        var row = Readout.Draw(snapshot, Now, 200, color: false)[0];

        Assert.Contains("Session", row, StringComparison.Ordinal);
        Assert.Contains("63%", row, StringComparison.Ordinal);
        Assert.Contains("resets in 2h00m00s", row, StringComparison.Ordinal);

        var opened = row.IndexOf('[');
        var closed = row.IndexOf(']');
        Assert.Equal(20, closed - opened - 1);
    }

    // ---- The closing banner's usage line ----

    [Fact]
    public void OneLine_IsNullWhereThereIsNoReading()
    {
        Assert.Null(Readout.OneLine([]));
    }

    [Fact]
    public void OneLine_NamesEveryWindowCompactly()
    {
        var line = Readout.OneLine([new UsageWindow("session", "Session", 0.63, null), new UsageWindow("weekly", "Weekly", 0.39, null)]);

        Assert.Equal("Session 63%, Weekly 39%", line);
    }

    // ---- The runner row ----

    [Fact]
    public void TheRunnerRow_NamesTheCharacterThePersonTheNightAndTheBound()
    {
        var runner = new RunnerSnapshot("Chrissy", "Nathan", 7, TimeSpan.FromHours(3) + TimeSpan.FromMinutes(12), 4.82m, "--max-spend 20");
        var snapshot = new ReadoutSnapshot(null, null, runner, [], null, NoControls);

        var row = Readout.Draw(snapshot, Now, 200, color: false)[0];

        Assert.Contains("Chrissy for Nathan", row, StringComparison.Ordinal);
        Assert.Contains("7 increment(s) in 3h12m00s", row, StringComparison.Ordinal);
        Assert.Contains("$4.82 spent", row, StringComparison.Ordinal);
        Assert.Contains("stops at --max-spend 20", row, StringComparison.Ordinal);
    }

    [Fact]
    public void ARunnerWithNoBound_SaysNothingAboutOne()
    {
        var runner = new RunnerSnapshot("Chrissy", null, 1, TimeSpan.Zero, 0m, null);
        var snapshot = new ReadoutSnapshot(null, null, runner, [], null, NoControls);

        var row = Readout.Draw(snapshot, Now, 200, color: false)[0];

        Assert.DoesNotContain("stops at", row, StringComparison.Ordinal);
        Assert.DoesNotContain("for", row, StringComparison.Ordinal);
    }

    // ---- Clipping ----

    [Fact]
    public void EveryRow_IsClippedToTheGivenWidth()
    {
        var runner = new RunnerSnapshot("Chrissy", "Nathan", 7, TimeSpan.FromHours(3), 4.82m, "--max-spend 20");
        var snapshot = new ReadoutSnapshot(null, null, runner, [], null, NoControls);

        var rows = Readout.Draw(snapshot, Now, 10, color: false);

        Assert.All(rows, r => Assert.True(r.Length <= 10));
    }

    // ---- The keyboard's legend row (HA-132) ----

    [Fact]
    public void KeysOn_DrawsALegendNamingBothKeys()
    {
        var controls = new ControlsSnapshot(KeysOn: true, StopArmed: false, Paused: false, LongLegend: false, Confirming.None);
        var snapshot = new ReadoutSnapshot(null, null, RunnerSnapshot.Empty, [], null, controls);

        var row = Readout.Draw(snapshot, Now, 200, color: false)[^1];

        Assert.Contains("s stop after this increment", row, StringComparison.Ordinal);
        Assert.Contains("c cancel now", row, StringComparison.Ordinal);
    }

    [Fact]
    public void KeysOff_DrawsNoLegendRowAtAll()
    {
        var controls = new ControlsSnapshot(KeysOn: false, StopArmed: false, Paused: false, LongLegend: false, Confirming.None);
        var snapshot = new ReadoutSnapshot(null, null, RunnerSnapshot.Empty, [], null, controls);

        var rows = Readout.Draw(snapshot, Now, 200, color: false);

        Assert.DoesNotContain(rows, r => r.Contains("stop after this increment", StringComparison.Ordinal));
        Assert.DoesNotContain(rows, r => r.Contains("cancel now", StringComparison.Ordinal));
    }

    [Fact]
    public void StopArmed_SaysHowToUndoIt()
    {
        var controls = new ControlsSnapshot(KeysOn: true, StopArmed: true, Paused: false, LongLegend: false, Confirming.None);
        var snapshot = new ReadoutSnapshot(null, null, RunnerSnapshot.Empty, [], null, controls);

        var row = Readout.Draw(snapshot, Now, 200, color: false)[^1];

        Assert.Contains("s to undo", row, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfirmingCancel_AsksTheQuestion()
    {
        var controls = new ControlsSnapshot(KeysOn: true, StopArmed: false, Paused: false, LongLegend: false, Confirming.Cancel);
        var snapshot = new ReadoutSnapshot(null, null, RunnerSnapshot.Empty, [], null, controls);

        var row = Readout.Draw(snapshot, Now, 200, color: false)[^1];

        Assert.Contains("cancel now?", row, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLegendRow_IsClippedAtANarrowWidthToo()
    {
        var controls = new ControlsSnapshot(KeysOn: true, StopArmed: false, Paused: false, LongLegend: false, Confirming.None);
        var snapshot = new ReadoutSnapshot(null, null, RunnerSnapshot.Empty, [], null, controls);

        var rows = Readout.Draw(snapshot, Now, 10, color: false);

        Assert.All(rows, r => Assert.True(r.Length <= 10));
    }

    // ---- The keyboard's p, k and ? (HA-133) ----

    [Fact]
    public void Paused_DrawsThePausedRowNamingTheKeyboard()
    {
        var controls = new ControlsSnapshot(KeysOn: true, StopArmed: false, Paused: true, LongLegend: false, Confirming.None);
        var snapshot = new ReadoutSnapshot(null, null, RunnerSnapshot.Empty, [], null, controls);

        var row = Readout.Draw(snapshot, Now, 200, color: false)[^1];

        Assert.Contains("paused from the keyboard", row, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfirmingSkip_AsksTheQuestion()
    {
        var controls = new ControlsSnapshot(KeysOn: true, StopArmed: false, Paused: false, LongLegend: false, Confirming.Skip);
        var snapshot = new ReadoutSnapshot(null, null, RunnerSnapshot.Empty, [], null, controls);

        var row = Readout.Draw(snapshot, Now, 200, color: false)[^1];

        Assert.Contains("skip this increment?", row, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfirmingSkip_OutranksStopArmed()
    {
        var controls = new ControlsSnapshot(KeysOn: true, StopArmed: true, Paused: false, LongLegend: false, Confirming.Skip);
        var snapshot = new ReadoutSnapshot(null, null, RunnerSnapshot.Empty, [], null, controls);

        var row = Readout.Draw(snapshot, Now, 200, color: false)[^1];

        Assert.Contains("skip this increment?", row, StringComparison.Ordinal);
    }

    [Fact]
    public void LongLegend_DrawsOneRowPerKeyEachAFullSentence()
    {
        var controls = new ControlsSnapshot(KeysOn: true, StopArmed: false, Paused: false, LongLegend: true, Confirming.None);
        var snapshot = new ReadoutSnapshot(null, null, RunnerSnapshot.Empty, [], null, controls);

        var rows = Readout.Draw(snapshot, Now, 200, color: false);
        var legend = rows.TakeLast(5).ToList();

        Assert.Equal(5, legend.Count);
        foreach (var key in new[] { "s", "c", "p", "k", "?" })
            Assert.Contains(legend, r => r.TrimStart().StartsWith(key, StringComparison.Ordinal));
    }

    [Fact]
    public void LongLegend_ToggledOff_DrawsTheShortLegendAgain()
    {
        var controls = new ControlsSnapshot(KeysOn: true, StopArmed: false, Paused: false, LongLegend: false, Confirming.None);
        var snapshot = new ReadoutSnapshot(null, null, RunnerSnapshot.Empty, [], null, controls);

        var row = Readout.Draw(snapshot, Now, 200, color: false)[^1];

        Assert.Contains("s stop after this increment", row, StringComparison.Ordinal);
        Assert.Contains("k skip this increment", row, StringComparison.Ordinal);
        Assert.Contains("? more", row, StringComparison.Ordinal);
    }
}

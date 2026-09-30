namespace Hatch.Cli.Tests;

/// <summary>
/// What the readout says, as a pure function of a snapshot and the clock - no
/// terminal involved, per the split HA-120 draws between this and the drawing
/// class.
/// </summary>
public sealed class ReadoutTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    // ---- The increment's two rows ----

    [Fact]
    public void TheIssueRow_NamesTheKeyTheTitleTheMoveAndTheAddress()
    {
        var inc = new IncrementSnapshot(
            "HA-120", "Console: a pinned readout", "In Progress -> In Review",
            "https://hatch.example.test/apps/hatch/issues/HA-120", Now, 0, Now, null);
        var snapshot = new ReadoutSnapshot(inc, null, RunnerSnapshot.Empty, [], null);

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
        var snapshot = new ReadoutSnapshot(inc, null, RunnerSnapshot.Empty, [], null);

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
        var snapshot = new ReadoutSnapshot(inc, null, RunnerSnapshot.Empty, [], null);

        var row = Readout.Draw(snapshot, Now, 200, color: true)[1];

        Assert.Equal(expectColour, row.Contains("\x1b[33m", StringComparison.Ordinal));
    }

    [Fact]
    public void TheQuietTimer_TurnsTheDangerColourPastTenMinutes()
    {
        var inc = new IncrementSnapshot("HA-1", "T", "W", null, Now, 0, Now.AddMinutes(-11), null);
        var snapshot = new ReadoutSnapshot(inc, null, RunnerSnapshot.Empty, [], null);

        var row = Readout.Draw(snapshot, Now, 200, color: true)[1];

        Assert.Contains("\x1b[31m", row, StringComparison.Ordinal);
    }

    [Fact]
    public void NoColour_MeansNoEscapeCodesAnywhere()
    {
        var inc = new IncrementSnapshot("HA-1", "T", "W", null, Now, 0, Now.AddMinutes(-11), null);
        var snapshot = new ReadoutSnapshot(inc, null, RunnerSnapshot.Empty, [], null);

        var rows = Readout.Draw(snapshot, Now, 200, color: false);

        Assert.All(rows, r => Assert.DoesNotContain("\x1b[", r, StringComparison.Ordinal));
    }

    // ---- No increment: no issue or alive row ----

    [Fact]
    public void BetweenIncrements_ThereIsNoIssueOrAliveRow()
    {
        var idle = new IdleSnapshot("nothing on the board is an agent's to move", Now.AddSeconds(47));
        var snapshot = new ReadoutSnapshot(null, idle, RunnerSnapshot.Empty, [], null);

        var rows = Readout.Draw(snapshot, Now, 200, color: false);

        Assert.DoesNotContain(rows, r => r.Contains("elapsed", StringComparison.Ordinal));
        Assert.Contains(rows, r => r.Contains("nothing on the board is an agent's to move", StringComparison.Ordinal));
        Assert.Contains(rows, r => r.Contains("looking again in 0m47s", StringComparison.Ordinal));
    }

    // ---- Usage windows ----

    [Fact]
    public void NoUsageReading_MeansNoUsageRowsAndNothingComplains()
    {
        var snapshot = new ReadoutSnapshot(null, null, RunnerSnapshot.Empty, [], null);

        var rows = Readout.Draw(snapshot, Now, 200, color: false);

        Assert.DoesNotContain(rows, r => r.Contains('[') && r.Contains('%'));
    }

    [Fact]
    public void AUsageWindow_DrawsATwentyCellBarAndAPercentageAndAReset()
    {
        var window = new UsageWindow("session", "Session", 0.63, Now.AddHours(2));
        var snapshot = new ReadoutSnapshot(null, null, RunnerSnapshot.Empty, [window], null);

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
        var snapshot = new ReadoutSnapshot(null, null, runner, [], null);

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
        var snapshot = new ReadoutSnapshot(null, null, runner, [], null);

        var row = Readout.Draw(snapshot, Now, 200, color: false)[0];

        Assert.DoesNotContain("stops at", row, StringComparison.Ordinal);
        Assert.DoesNotContain("for", row, StringComparison.Ordinal);
    }

    // ---- Clipping ----

    [Fact]
    public void EveryRow_IsClippedToTheGivenWidth()
    {
        var runner = new RunnerSnapshot("Chrissy", "Nathan", 7, TimeSpan.FromHours(3), 4.82m, "--max-spend 20");
        var snapshot = new ReadoutSnapshot(null, null, runner, [], null);

        var rows = Readout.Draw(snapshot, Now, 10, color: false);

        Assert.All(rows, r => Assert.True(r.Length <= 10));
    }
}

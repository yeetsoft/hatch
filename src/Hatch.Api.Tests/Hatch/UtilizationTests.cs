using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="Utilization.Pace"/>'s own arithmetic - no database, no
/// controller, just a reading built by hand against a fixed instant. See
/// HA-209.
/// </summary>
public class UtilizationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    private static UtilizationLimit Session(int percent, TimeSpan untilReset) =>
        new(UtilizationWindows.Session, "Session", percent, Now + untilReset);

    private static UtilizationLimit Weekly(int percent, TimeSpan untilReset) =>
        new(UtilizationWindows.Weekly, "Weekly", percent, Now + untilReset);

    private static UtilizationReading Reading(params UtilizationLimit[] limits) =>
        new(UtilizationStates.Ok, Now, limits);

    [Fact]
    public void ExactlyAtTheReserveBoundary_ReadsAhead()
    {
        // 5-hour session window, 2 hours left -> 60% elapsed. 60 - Reserve(10) == 50.
        var reading = Reading(Session(50, TimeSpan.FromHours(2)));

        var pace = Utilization.Pace(reading, Now);

        Assert.Null(pace.Fold);
        Assert.NotNull(pace.ClearNote);
    }

    [Fact]
    public void OnePointShortOfTheBoundary_ReadsBehindWithTheStatedNumbers()
    {
        // Same window, one point further spent: 51% spent, 60% elapsed.
        var reading = Reading(Session(51, TimeSpan.FromHours(2)));

        var pace = Utilization.Pace(reading, Now);

        Assert.NotNull(pace.Fold);
        Assert.Null(pace.ClearNote);
        Assert.Contains("51%", pace.Fold);
        Assert.Contains("60%", pace.Fold);
    }

    [Fact]
    public void AWindowAlreadyReset_ReadsAheadRegardlessOfPercent()
    {
        var reading = Reading(Session(99, TimeSpan.FromMinutes(-1)));

        var pace = Utilization.Pace(reading, Now);

        Assert.Null(pace.Fold);
    }

    [Fact]
    public void NoWindowHasAResetInstant_IsVacuouslyClear()
    {
        var reading = Reading(new UtilizationLimit(UtilizationWindows.Extra, "Extra", 99, null));

        var pace = Utilization.Pace(reading, Now);

        Assert.Null(pace.Fold);
        Assert.Null(pace.ClearNote);
    }

    [Fact]
    public void TwoWindowsBothAhead_ClearNoteNamesTheOneAheadByLess()
    {
        // Session: 60% elapsed, 10% spent -> 50 ahead.
        // Weekly: 7-day window, 1 day left -> ~85.7% elapsed, 70% spent -> ~15.7 ahead - still ahead, but the lesser margin.
        var reading = Reading(
            Session(10, TimeSpan.FromHours(2)),
            Weekly(70, TimeSpan.FromDays(1)));

        var pace = Utilization.Pace(reading, Now);

        Assert.Null(pace.Fold);
        Assert.NotNull(pace.ClearNote);
        Assert.Contains("weekly", pace.ClearNote);
    }

    [Fact]
    public void OneWindowBehindAndOneAhead_FoldNamesOnlyTheBehindOne()
    {
        // Session: 60% elapsed, 10% spent -> well ahead.
        // Weekly: 7-day window, 1 day left -> ~85.7% elapsed, 80% spent -> behind (margin < 10).
        var reading = Reading(
            Session(10, TimeSpan.FromHours(2)),
            Weekly(80, TimeSpan.FromDays(1)));

        var pace = Utilization.Pace(reading, Now);

        Assert.NotNull(pace.Fold);
        Assert.Contains("weekly", pace.Fold);
        Assert.DoesNotContain("session", pace.Fold);
    }

    // ---- SessionPace, low's own arithmetic (HA-226) ----

    [Fact]
    public void SessionPace_AheadOnTheSessionWindow_ReadsClear()
    {
        // 60% elapsed, 10% spent -> 50 ahead.
        var reading = Reading(Session(10, TimeSpan.FromHours(2)));

        var pace = Utilization.SessionPace(reading, Now);

        Assert.Null(pace.Fold);
        Assert.NotNull(pace.ClearNote);
        Assert.Contains("low", pace.ClearNote);
    }

    [Fact]
    public void SessionPace_OnePointBehind_ReadsBehindWithBothPercentages()
    {
        // Low holds nothing in reserve, so only a negative margin reads behind:
        // 61% spent, 60% elapsed -> margin -1.
        var reading = Reading(Session(61, TimeSpan.FromHours(2)));

        var pace = Utilization.SessionPace(reading, Now);

        Assert.NotNull(pace.Fold);
        Assert.Null(pace.ClearNote);
        Assert.Contains("61%", pace.Fold);
        Assert.Contains("60%", pace.Fold);
        Assert.Contains("low", pace.Fold);
    }

    [Fact]
    public void SessionPace_AWindowAlreadyReset_ReadsAheadRegardlessOfPercent()
    {
        var reading = Reading(Session(99, TimeSpan.FromMinutes(-1)));

        var pace = Utilization.SessionPace(reading, Now);

        Assert.Null(pace.Fold);
    }

    [Fact]
    public void SessionPace_AReadingWithNoSessionWindow_IsVacuouslyAhead()
    {
        // A weekly window alone, deep behind pace for economy, is not even read
        // by SessionPace - it only ever looks at the session window.
        var reading = Reading(Weekly(80, TimeSpan.FromDays(1)));

        var pace = Utilization.SessionPace(reading, Now);

        Assert.Null(pace.Fold);
        Assert.Null(pace.ClearNote);
    }

    [Fact]
    public void TheAsymmetryBetweenEconomyAndLow_OnTheSameReading()
    {
        // 51% spent, 60% elapsed on the session window alone - margin 9.
        // Economy holds 10 points in reserve, so margin 9 is behind.
        // Low holds nothing in reserve, so the same margin 9 is clear.
        var reading = Reading(Session(51, TimeSpan.FromHours(2)));

        var economy = Utilization.Pace(reading, Now);
        var low = Utilization.SessionPace(reading, Now);

        Assert.NotNull(economy.Fold);
        Assert.Null(low.Fold);
        Assert.NotNull(low.ClearNote);
    }
}

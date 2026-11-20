using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.Extensions.Options;

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
        new(UtilizationStates.Ok, Now, Now, limits, []);

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

    // ---- Of, freshness judged on ReceivedAt rather than ReadAt (HA-2536) ----

    private static readonly string Usage =
        System.Text.Json.JsonSerializer.Serialize(new[] { new RunnerUsageWindowDto(UtilizationWindows.Session, "Session", 10, null) });

    [Fact]
    public void AReadingReportedAhead_ButReceivedNow_ReadsOk()
    {
        // The runner's own clock says ten minutes in the future; Hatch received it now.
        var readings = new[] { (Usage, (DateTimeOffset?)Now.AddMinutes(10), (DateTimeOffset?)Now, (string?)null, (string?)null) };

        var reading = Utilization.Of(readings, Now);

        Assert.Equal(UtilizationStates.Ok, reading!.State);
        Assert.Equal(Now, reading.ReceivedAt);
    }

    [Fact]
    public void AnEarlierReadAtDoesNotBeatALaterReceivedAt()
    {
        // The first reading's own ReadAt is later, but it was received first;
        // the second reading was received a minute after it, and wins.
        var earlierReceived = (Usage, (DateTimeOffset?)Now.AddMinutes(5), (DateTimeOffset?)Now, (string?)null, (string?)null);
        var laterReceived = (Usage, (DateTimeOffset?)Now.AddMinutes(1), (DateTimeOffset?)Now.AddMinutes(1), (string?)null, (string?)null);

        var reading = Utilization.Of([earlierReceived, laterReceived], Now);

        Assert.Equal(Now.AddMinutes(1), reading!.ReceivedAt);
        Assert.Equal(Now.AddMinutes(1), reading.ReadAt);
    }

    [Fact]
    public void ARowWithNoReceivedAt_FallsBackToItsOwnReadAt()
    {
        var readings = new[] { (Usage, (DateTimeOffset?)Now, (DateTimeOffset?)null, (string?)null, (string?)null) };

        var reading = Utilization.Of(readings, Now);

        Assert.Equal(UtilizationStates.Ok, reading!.State);
        Assert.Equal(Now, reading.ReceivedAt);
    }

    [Fact]
    public void TheFreshestReadingsSourceAndProbeOff_RideOntoTheResult()
    {
        var older = (Usage, (DateTimeOffset?)Now.AddMinutes(-3), (DateTimeOffset?)Now.AddMinutes(-3), (string?)"stream", (string?)"old reason");
        var fresher = (Usage, (DateTimeOffset?)Now, (DateTimeOffset?)Now, (string?)"probe", (string?)null);

        var reading = Utilization.Of([fresher, older], Now);

        Assert.Equal("probe", reading!.Source);
        Assert.Null(reading.ProbeOff);
    }

    // ---- RunnerEntries, the panel behind the headline (HA-2535) ----

    private static string UsageJson(int percent) =>
        System.Text.Json.JsonSerializer.Serialize(new[] { new RunnerUsageWindowDto(UtilizationWindows.Session, "Session", percent, null) });

    private static Runners RunnerRules(int goneAfterSeconds = 90) =>
        new(Options.Create(new HatchOptions { RunnerGoneAfterSeconds = goneAfterSeconds }), Options.Create(new AuthOptions()), new StubHatchRevision());

    private static RunnerSessions SessionRules(int goneAfterSeconds = 90) => new(RunnerRules(goneAfterSeconds));

    private static EfHatchRunner Beat(string name, DateTimeOffset lastSeenAt, int? percent = null, DateTimeOffset? readAt = null) => new()
    {
        Name = name,
        Kind = EfHatchRunner.LoopKind,
        State = EfHatchRunner.Running,
        FirstSeenAt = lastSeenAt,
        LastSeenAt = lastSeenAt,
        Usage = percent is { } p ? UsageJson(p) : null,
        UsageReadAt = percent is not null ? readAt ?? lastSeenAt : null,
        UsageReceivedAt = percent is not null ? readAt ?? lastSeenAt : null,
    };

    private static EfHatchRunnerSession SessionOf(
        int percent, DateTimeOffset readAt, string? source = null, string? probeOff = null) => new()
    {
        ApiKeyId = Guid.NewGuid(),
        Kind = EfHatchRunner.LoopKind,
        State = EfHatchRunner.Running,
        StartedAt = readAt,
        LastSeenAt = readAt,
        Usage = UsageJson(percent),
        UsageReadAt = readAt,
        UsageReceivedAt = readAt,
        UsageSource = source,
        UsageProbeOff = probeOff,
    };

    [Fact]
    public void TwoRunners_AreBothListed()
    {
        var one = Beat("runner-one", Now, 10);
        var two = Beat("runner-two", Now, 20);
        var headline = Reading(new UtilizationLimit(UtilizationWindows.Session, "Session", 10, null)) with { ReadAt = Now, ReceivedAt = Now };

        var entries = Utilization.RunnerEntries(
            [new Runner(null, null, one), new Runner(null, null, two)], RunnerRules(), SessionRules(), headline, Now);

        Assert.Equal(["runner-one", "runner-two"], entries.Select(e => e.Name));
        Assert.All(entries, e => Assert.True(e.Live));
    }

    [Fact]
    public void AGoneRunner_StillHoldsTheHeadline()
    {
        var goneAt = Now.AddHours(-3);
        var gone = Beat("runner-gone", goneAt, 42, goneAt);
        var headline = Reading(new UtilizationLimit(UtilizationWindows.Session, "Session", 42, null)) with { ReadAt = goneAt, ReceivedAt = goneAt };

        var entries = Utilization.RunnerEntries([new Runner(null, null, gone)], RunnerRules(), SessionRules(), headline, Now);

        var entry = Assert.Single(entries);
        Assert.Equal("runner-gone", entry.Name);
        Assert.False(entry.Live);
        Assert.NotNull(entry.Limits);
    }

    [Fact]
    public void ALiveRunnerWithNoReading_HasNoLimits()
    {
        var silent = Beat("runner-silent", Now);
        var headline = Reading(new UtilizationLimit(UtilizationWindows.Session, "Session", 10, null)) with { ReadAt = Now.AddMinutes(-5), ReceivedAt = Now.AddMinutes(-5) };

        var entries = Utilization.RunnerEntries([new Runner(null, null, silent)], RunnerRules(), SessionRules(), headline, Now);

        var entry = Assert.Single(entries);
        Assert.True(entry.Live);
        Assert.Null(entry.Limits);
        Assert.Null(entry.State);
        Assert.Null(entry.ReadAt);
        Assert.Null(entry.ReceivedAt);
    }

    [Fact]
    public void ARunnersEntry_CarriesTheSourceAndProbeOffOfItsFreshestReading_NotAnOlderOne()
    {
        var older = Beat("runner-one", Now, 10, Now.AddMinutes(-4));
        older.UsageSource = "stream";
        older.UsageProbeOff = "old reason";
        var sessions = new[] { SessionOf(20, Now, source: "probe", probeOff: null) };
        var headline = Reading(new UtilizationLimit(UtilizationWindows.Session, "Session", 20, null)) with { ReadAt = Now, ReceivedAt = Now };

        var entries = Utilization.RunnerEntries(
            [new Runner(null, null, older, sessions)], RunnerRules(), SessionRules(), headline, Now);

        var entry = Assert.Single(entries);
        Assert.Equal("probe", entry.Source);
        Assert.Null(entry.ProbeOff);
    }

    [Fact]
    public void ARunnerWithNoReading_HasNoSourceOrProbeOff()
    {
        var silent = Beat("runner-silent", Now);
        var headline = Reading(new UtilizationLimit(UtilizationWindows.Session, "Session", 10, null)) with { ReadAt = Now.AddMinutes(-5), ReceivedAt = Now.AddMinutes(-5) };

        var entry = Assert.Single(Utilization.RunnerEntries([new Runner(null, null, silent)], RunnerRules(), SessionRules(), headline, Now));

        Assert.Null(entry.Source);
        Assert.Null(entry.ProbeOff);
    }

    [Fact]
    public void ARunnerNeitherLiveNorTheHeadlinesHolder_IsDropped()
    {
        var goneAt = Now.AddHours(-3);
        var dropped = Beat("runner-dropped", goneAt, 99, goneAt);
        var headline = Reading(new UtilizationLimit(UtilizationWindows.Session, "Session", 10, null)) with { ReadAt = Now, ReceivedAt = Now };

        var entries = Utilization.RunnerEntries([new Runner(null, null, dropped)], RunnerRules(), SessionRules(), headline, Now);

        Assert.Empty(entries);
    }
}

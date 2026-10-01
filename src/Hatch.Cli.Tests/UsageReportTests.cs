namespace Hatch.Cli.Tests;

/// <summary>
/// The probe's own reading, parsed out of a <c>/usage</c> stream rather than a
/// session's - see the mapping table and the decisions on HA-173.
/// </summary>
public sealed class UsageReportTests
{
    [Fact]
    public void A_session_window_maps_to_session_Session()
    {
        var windows = UsageReport.Parse([Fixtures.UsageReport([("session", 17, null)])]);

        var window = Assert.Single(windows!);
        Assert.Equal("session", window.Window);
        Assert.Equal("Session", window.Label);
        Assert.Equal(0.17, window.Utilization, 3);
    }

    [Fact]
    public void A_weekly_all_window_maps_to_weekly_Weekly()
    {
        var windows = UsageReport.Parse([Fixtures.UsageReport([("weekly_all", 40, null)])]);

        var window = Assert.Single(windows!);
        Assert.Equal("weekly", window.Window);
        Assert.Equal("Weekly", window.Label);
    }

    [Fact]
    public void A_scoped_weekly_window_is_named_by_the_account()
    {
        var windows = UsageReport.Parse(
            [Fixtures.UsageReport([("weekly_scoped", 60, null)], scopedName: "Opus 5")]);

        var window = Assert.Single(windows!);
        Assert.Equal("weeklyModel", window.Window);
        Assert.Equal("Weekly (Opus 5)", window.Label);
    }

    [Fact]
    public void A_scoped_weekly_window_with_no_name_reads_as_model()
    {
        var windows = UsageReport.Parse([Fixtures.UsageReport([("weekly_scoped", 60, null)])]);

        var window = Assert.Single(windows!);
        Assert.Equal("weeklyModel", window.Window);
        Assert.Equal("Weekly (model)", window.Label);
    }

    [Fact]
    public void ExtraUsage_switchedOn_isARow()
    {
        var windows = UsageReport.Parse(
            [Fixtures.UsageReport(extra: (IsEnabled: true, Utilization: 33))]);

        var window = Assert.Single(windows!);
        Assert.Equal("extra", window.Window);
        Assert.Equal("Extra usage", window.Label);
        Assert.Equal(0.33, window.Utilization, 3);
        Assert.Null(window.ResetsAt);
    }

    [Fact]
    public void ExtraUsage_switchedOff_isNoRow()
    {
        var windows = UsageReport.Parse(
            [Fixtures.UsageReport(extra: (IsEnabled: false, Utilization: 33))]);

        Assert.Empty(windows!);
    }

    [Fact]
    public void AResetInstant_isCarriedAsGiven()
    {
        var windows = UsageReport.Parse(
            [Fixtures.UsageReport([("session", 17, "2026-09-08T12:00:00Z")])]);

        Assert.Equal(
            DateTimeOffset.Parse("2026-09-08T12:00:00Z"), Assert.Single(windows!).ResetsAt);
    }

    [Fact]
    public void AKindNobodyKnowsIsSkippedRatherThanGuessedAt()
    {
        var windows = UsageReport.Parse(
            [Fixtures.UsageReport([("session", 17, null), ("something_new", 90, null)])]);

        Assert.Single(windows!);
    }

    [Fact]
    public void NullRateLimits_isNoReading()
    {
        var line = """{"type":"assistant","message":{"usage_report":{"rate_limits":null}}}""";
        Assert.Null(UsageReport.Parse([line]));
    }

    [Fact]
    public void NoUsageReportAtAll_isNoReading()
    {
        var line = """{"type":"assistant","message":{"content":[]}}""";
        Assert.Null(UsageReport.Parse([line]));
    }

    [Fact]
    public void GarbageIsNoReading()
    {
        Assert.Null(UsageReport.Parse(["not json at all", "{", ""]));
    }

    [Fact]
    public void ANonAssistantLineIsSkipped()
    {
        Assert.Null(UsageReport.Parse([Fixtures.Init(), Fixtures.Result()]));
    }

    [Fact]
    public void TurnsIsZeroForAProbeThatCostNothing()
    {
        Assert.Equal(0, UsageReport.Turns([Fixtures.Result(turns: 0)]));
    }

    [Fact]
    public void TurnsReportsAProbeThatCostSomething()
    {
        Assert.Equal(1, UsageReport.Turns([Fixtures.Result(turns: 1)]));
    }

    [Fact]
    public void TurnsIsZeroWithNoResultLineAtAll()
    {
        Assert.Equal(0, UsageReport.Turns([Fixtures.Init()]));
    }
}

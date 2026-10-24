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

    private const string UsageText =
        "Current session: 1% used · resets Oct 24 at 7:10pm (America/New_York)\n" +
        "Current week (all models): 28% used · resets Oct 27 at 2pm (America/New_York)\n" +
        "Current week (Opus 5): 12.5% used · resets Oct 27 at 2pm (America/New_York)";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-24T14:00:00-04:00");

    [Fact]
    public void WithNoUsageReport_theWindowsAreReadFromTheAssistantText()
    {
        var windows = UsageReport.Parse([Fixtures.Init(), Fixtures.AssistantText(UsageText), Fixtures.Result()], Now);

        Assert.Equal(["session", "weekly", "weeklyModel"], windows!.Select(w => w.Window));
        Assert.Equal(["Session", "Weekly", "Weekly (Opus 5)"], windows!.Select(w => w.Label));
        Assert.Equal(0.01, windows![0].Utilization, 3);
        Assert.Equal(0.28, windows![1].Utilization, 3);
        Assert.Equal(0.125, windows![2].Utilization, 3);
        Assert.Equal(DateTimeOffset.Parse("2026-10-24T19:10:00-04:00"), windows![0].ResetsAt);
        Assert.Equal(DateTimeOffset.Parse("2026-10-27T14:00:00-04:00"), windows![1].ResetsAt);
    }

    [Fact]
    public void TheTextReset_isTheOneUsageLimitReads()
    {
        const string said = "resets Oct 27 at 2pm (America/New_York)";
        var windows = UsageReport.Parse([Fixtures.AssistantText($"Current week (all models): 28% used · {said}")], Now);

        Assert.Equal(UsageLimit.ResetNamed(said, Now), Assert.Single(windows!).ResetsAt);
    }

    [Fact]
    public void ATextWindowWithNoReset_hasNoResetInstant()
    {
        var windows = UsageReport.Parse([Fixtures.AssistantText("Current session: 4% used")], Now);

        Assert.Null(Assert.Single(windows!).ResetsAt);
    }

    [Fact]
    public void AStructuredReport_winsOverTheText()
    {
        var windows = UsageReport.Parse(
            [Fixtures.UsageReport([("session", 17, null)]), Fixtures.AssistantText(UsageText)], Now);

        var window = Assert.Single(windows!);
        Assert.Equal(0.17, window.Utilization, 3);
    }

    [Fact]
    public void AssistantTextWithNoCurrentLine_isNoReading()
    {
        Assert.Null(UsageReport.Parse([Fixtures.AssistantText("Nothing to see here.")], Now));
    }

    [Fact]
    public void SaidPreviewsTheAssistantText_notTheInitLine()
    {
        var said = UsageReport.Said([Fixtures.Init(), Fixtures.AssistantText("hello there"), Fixtures.Result()]);

        Assert.Equal("hello there\n", said);
    }

    [Fact]
    public void SaidFallsBackToTheResultText_andNeverTheInitLine()
    {
        var said = UsageReport.Said([Fixtures.Init(), Fixtures.Result(said: "it failed")]);

        Assert.Equal("it failed", said);
        Assert.DoesNotContain("init", UsageReport.Said([Fixtures.Init()]));
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

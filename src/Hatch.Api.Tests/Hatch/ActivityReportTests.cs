using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The pure fold alone: rows in, counts and rows out, with no database and no
/// clock - pinned the way <see cref="ChangelogReportTests"/> pins <see cref="ChangelogReport"/>.
/// </summary>
public class ActivityReportTests
{
    private static readonly DateTimeOffset Jan1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Wide enough to hold every row these tests write, hourly, so the series and sessions never spill off the grid.</summary>
    private static readonly WorkLogWindow DefaultWindow = new(Jan1, 24, TimeSpan.FromHours(1));

    private static readonly TimeSpan DefaultGap = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan DefaultLead = TimeSpan.FromMinutes(15);

    private static ActivityReport.Row Row(
        long id, long issueId, string kind, int hours, int projectId = 1) =>
        new(id, issueId, projectId, kind, Jan1.AddHours(hours));

    private static ActivityReport.Result Fold(
        IReadOnlyList<ActivityReport.Row> rows, IReadOnlyCollection<int> projectIds, int maxEvents,
        WorkLogWindow? window = null, TimeSpan? gap = null, TimeSpan? lead = null, int windowDays = 3) =>
        ActivityReport.Fold(
            rows, projectIds, maxEvents, window ?? DefaultWindow, gap ?? DefaultGap, lead ?? DefaultLead, windowDays);

    [Fact]
    public void NoRows_AnswerEmpty()
    {
        var result = Fold([], [], 500);

        Assert.Equal(0, result.Total);
        Assert.Empty(result.Kinds);
        Assert.Empty(result.Issues);
        Assert.Empty(result.Events);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void KindsAreCounted_MostFirst_ThenByName()
    {
        ActivityReport.Row[] rows =
        [
            Row(1, 1, "retitled", 1),
            Row(2, 1, "status_changed", 2),
            Row(3, 2, "status_changed", 3),
            Row(4, 2, "created", 4),
            Row(5, 3, "status_changed", 5),
        ];

        var result = Fold(rows, [], 500);

        Assert.Equal(5, result.Total);
        Assert.Equal(
            [("status_changed", 3), ("created", 1), ("retitled", 1)],
            result.Kinds.Select(k => (k.Kind, k.Count)).ToArray());
    }

    [Fact]
    public void ProjectIds_NarrowTheTotalTheKindsAndTheIssues()
    {
        ActivityReport.Row[] rows =
        [
            Row(1, 1, "created", 1, projectId: 1),
            Row(2, 2, "created", 2, projectId: 2),
            Row(3, 3, "retitled", 3, projectId: 3),
        ];

        var result = Fold(rows, [1, 3], 500);

        Assert.Equal(2, result.Total);
        Assert.Equal([3L, 1L], result.Issues.Select(i => i.IssueId).ToArray());
        Assert.Equal([3L, 1L], result.Events.Select(e => e.IssueId).ToArray());
        Assert.Equal(2, result.Kinds.Count);
    }

    [Fact]
    public void ThePageCap_CutsOnlyTheRows_NotTheCountsOrTheIssues()
    {
        ActivityReport.Row[] rows =
        [
            Row(1, 1, "created", 1),
            Row(2, 2, "retitled", 2),
            Row(3, 3, "created", 3),
        ];

        var result = Fold(rows, [], 2);

        Assert.Equal([3L, 2L], result.Events.Select(e => e.Id).ToArray());
        Assert.True(result.Truncated);
        Assert.Equal(3, result.Total);
        Assert.Equal(3, result.Kinds.Sum(k => k.Count));
        Assert.Equal(3, result.Issues.Count);
    }

    [Fact]
    public void AsManyRowsAsTheCap_IsNotTruncated()
    {
        var result = Fold([Row(1, 1, "created", 1), Row(2, 1, "created", 2)], [], 2);

        Assert.Equal(2, result.Events.Count);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void Issues_AreOrderedByLastDescending_WithTheIdBreakingATie_AndCarryTheirOwnRundown()
    {
        ActivityReport.Row[] rows =
        [
            Row(1, 10, "created", 1),
            Row(2, 10, "retitled", 9),
            Row(3, 10, "retitled", 5),
            Row(4, 20, "created", 9),
            Row(5, 30, "created", 12),
        ];

        var result = Fold(rows, [], 500);

        Assert.Equal([30L, 20L, 10L], result.Issues.Select(i => i.IssueId).ToArray());

        var ten = result.Issues[2];
        Assert.Equal(Jan1.AddHours(1), ten.First);
        Assert.Equal(Jan1.AddHours(9), ten.Last);
        Assert.Equal(3, ten.Count);
        Assert.Equal([("retitled", 2), ("created", 1)], ten.Kinds.Select(k => (k.Kind, k.Count)).ToArray());
    }

    [Fact]
    public void EventsAtTheSameInstant_ComeNewestIdFirst()
    {
        ActivityReport.Row[] rows =
        [
            Row(1, 1, "created", 1),
            Row(3, 1, "retitled", 1),
            Row(2, 1, "redescribed", 1),
        ];

        var result = Fold(rows, [], 500);

        Assert.Equal([3L, 2L, 1L], result.Events.Select(e => e.Id).ToArray());
    }

    // ---- Sessions ----

    [Fact]
    public void TwoEventsExactlyTheGapApart_FoldToOneSession()
    {
        ActivityReport.Row[] rows =
        [
            new(1, 1, 1, "created", Jan1),
            new(2, 1, 1, "retitled", Jan1.AddMinutes(30)),
        ];

        var result = Fold(rows, [], 500, gap: TimeSpan.FromMinutes(30));

        var session = Assert.Single(result.Sessions);
        Assert.Equal(Jan1, session.From);
        Assert.Equal(Jan1.AddMinutes(30), session.To);
        Assert.Equal(2, session.Events);
    }

    [Fact]
    public void TwoEventsAGapAndASecondApart_FoldToTwoSessions()
    {
        ActivityReport.Row[] rows =
        [
            new(1, 1, 1, "created", Jan1),
            new(2, 1, 1, "retitled", Jan1.AddMinutes(30).AddSeconds(1)),
        ];

        var result = Fold(rows, [], 500, gap: TimeSpan.FromMinutes(30));

        Assert.Equal(2, result.Sessions.Count);
    }

    [Fact]
    public void ALoneEvent_IsWorthTheLeadAlone()
    {
        var result = Fold([Row(1, 1, "created", 0)], [], 500, lead: TimeSpan.FromMinutes(15));

        var session = Assert.Single(result.Sessions);
        Assert.Equal(session.From, session.To);
        Assert.Equal(15, session.EstimatedMinutes);
    }

    [Fact]
    public void Sessions_ComeBackNewestFromFirst()
    {
        ActivityReport.Row[] rows =
        [
            new(1, 1, 1, "created", Jan1),
            new(2, 1, 1, "created", Jan1.AddHours(5)),
        ];

        var result = Fold(rows, [], 500, gap: TimeSpan.FromMinutes(30));

        Assert.Equal([Jan1.AddHours(5), Jan1], result.Sessions.Select(s => s.From).ToArray());
    }

    [Fact]
    public void EstimateTotals_TheSessionsAndCarriesTheParameters()
    {
        ActivityReport.Row[] rows = [Row(1, 1, "created", 0), Row(2, 1, "created", 10)];

        var result = Fold(
            rows, [], 500, gap: TimeSpan.FromMinutes(30), lead: TimeSpan.FromMinutes(20), windowDays: 7);

        Assert.Equal(2, result.Estimate.Sessions);
        Assert.Equal(result.Sessions.Sum(s => s.EstimatedMinutes), result.Estimate.Minutes);
        Assert.Equal(30, result.Estimate.GapMinutes);
        Assert.Equal(20, result.Estimate.LeadMinutes);
        Assert.Equal(7, result.Estimate.WindowDays);
    }

    // ---- The series ----

    [Fact]
    public void AnEmptyBucket_IsAZero_NeverAbsent()
    {
        ActivityReport.Row[] rows = [Row(1, 1, "created", 0)];

        var result = Fold(rows, [], 500, window: new WorkLogWindow(Jan1, 3, TimeSpan.FromHours(1)));

        Assert.Equal(3, result.Series.Count);
        Assert.Equal(0, result.Series[1].Events);
        Assert.Equal(0, result.Series[1].Hours);
    }

    [Fact]
    public void ASessionSpanningABucketEdge_CountsItsHoursInItsStartBucket_NotSplit()
    {
        ActivityReport.Row[] rows =
        [
            new(1, 1, 1, "created", Jan1.AddMinutes(50)),
            new(2, 1, 1, "retitled", Jan1.AddHours(1).AddMinutes(10)),
        ];
        var window = new WorkLogWindow(Jan1, 3, TimeSpan.FromHours(1));

        var result = Fold(rows, [], 500, window: window, gap: TimeSpan.FromMinutes(30), lead: TimeSpan.Zero);

        var session = Assert.Single(result.Sessions);
        Assert.Equal(20, session.EstimatedMinutes);
        Assert.Equal(20.0 / 60, result.Series[0].Hours);
        Assert.Equal(0, result.Series[1].Hours);
    }

    [Fact]
    public void TheRollingMean_AtTheLeftEdge_AveragesOnlyWhatExists()
    {
        // All six land in the first of three daily buckets; the other two are
        // empty. A window that divided by three buckets regardless would say
        // 2 here instead of 6.
        var rows = Enumerable.Range(0, 6).Select(h => Row(h, 1, "created", h)).ToArray();
        var window = new WorkLogWindow(Jan1, 3, TimeSpan.FromDays(1));

        var result = Fold(rows, [], 500, window: window, gap: TimeSpan.FromMinutes(30), windowDays: 3);

        Assert.Equal(6, result.Series[0].RollingEvents);
        Assert.Equal(3, result.Series[1].RollingEvents);
        Assert.Equal(2, result.Series[2].RollingEvents);
    }

    // ---- HandTyped: a person's own keys ----

    private const string KeyName = "ann-laptop";

    private static ActivityReport.Row KeyRow(long id, long issueId, string kind, int minutes) =>
        new(id, issueId, 1, kind, Jan1.AddMinutes(minutes), KeyName);

    private static ActivityReport.ClaimEvent Claim(long id, long issueId, string kind, int minutes, string actor = KeyName) =>
        new(id, issueId, actor, kind, Jan1.AddMinutes(minutes));

    [Fact]
    public void AKeysEvent_InsideAWorkLogSession_IsTheLoops()
    {
        ActivityReport.WorkSpan[] spans = [new(1, Jan1.AddMinutes(10), Jan1.AddMinutes(40))];

        var typed = ActivityReport.HandTyped([KeyRow(1, 1, "commented", 25)], [], spans);

        Assert.Empty(typed);
    }

    [Fact]
    public void AKeysEvent_InsideAClaimWithNoWorkLogRowYet_IsTheLoops()
    {
        ActivityReport.ClaimEvent[] claims = [Claim(1, 1, "claim_taken", 10)];

        var typed = ActivityReport.HandTyped([KeyRow(2, 1, "status_changed", 25)], claims, []);

        Assert.Empty(typed);
    }

    [Fact]
    public void AKeysEvent_OutsideBothASessionAndAClaim_IsTheHands()
    {
        // The work-log row is another stretch of the same issue; the claim was
        // released before the event.
        ActivityReport.WorkSpan[] spans = [new(1, Jan1.AddMinutes(10), Jan1.AddMinutes(20))];
        ActivityReport.ClaimEvent[] claims =
            [Claim(1, 1, "claim_taken", 10), Claim(3, 1, "claim_released", 20)];

        var typed = ActivityReport.HandTyped([KeyRow(4, 1, "commented", 30)], claims, spans);

        var row = Assert.Single(typed);
        Assert.Equal(4, row.Id);
        Assert.Equal(KeyName, row.ViaKey);
    }

    [Fact]
    public void AKeysEvent_OnAnIssueNoClaimOrSessionTouches_IsTheHands()
    {
        ActivityReport.ClaimEvent[] claims = [Claim(1, 2, "claim_taken", 10)];
        ActivityReport.WorkSpan[] spans = [new(2, Jan1, Jan1.AddHours(1))];

        Assert.Single(ActivityReport.HandTyped([KeyRow(2, 1, "commented", 25)], claims, spans));
    }

    [Fact]
    public void AClaimAnotherKeyTook_DoesNotCoverThisKeysEvent()
    {
        ActivityReport.ClaimEvent[] claims = [Claim(1, 1, "claim_taken", 10, actor: "bob-runner")];

        Assert.Single(ActivityReport.HandTyped([KeyRow(2, 1, "commented", 25)], claims, []));
    }

    [Fact]
    public void ALaterTakeByAnotherHolder_EndsAClaimThatWasNeverReleased()
    {
        // A runner that died without releasing: the next holder's take is the
        // end of its claim, so a hand-typed event after it is counted.
        ActivityReport.ClaimEvent[] claims =
            [Claim(1, 1, "claim_taken", 10), Claim(2, 1, "claim_taken", 20, actor: "bob-runner")];

        Assert.Single(ActivityReport.HandTyped([KeyRow(3, 1, "commented", 25)], claims, []));
    }

    [Fact]
    public void TheClaimEventsThemselves_AreTheLoops()
    {
        var typed = ActivityReport.HandTyped(
            [KeyRow(1, 1, "claim_taken", 10), KeyRow(2, 1, "claim_released", 20), KeyRow(3, 1, "claim_lapsed", 30)], [], []);

        Assert.Empty(typed);
    }

    [Fact]
    public void ARowsViaKey_SurvivesTheFold()
    {
        var rows = new[] { Row(1, 1, "created", 1), KeyRow(2, 1, "commented", 90) };

        var result = Fold(rows, [], 500);

        Assert.Equal(2, result.Total);
        Assert.Equal(KeyName, result.Events[0].ViaKey);
        Assert.Null(result.Events[1].ViaKey);
    }
}

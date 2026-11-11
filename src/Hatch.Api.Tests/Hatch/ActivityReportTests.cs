using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The pure fold alone: rows in, counts and rows out, with no database and no
/// clock - pinned the way <see cref="ChangelogReportTests"/> pins <see cref="ChangelogReport"/>.
/// </summary>
public class ActivityReportTests
{
    private static readonly DateTimeOffset Jan1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static ActivityReport.Row Row(
        long id, long issueId, string kind, int hours, int projectId = 1) =>
        new(id, issueId, projectId, kind, Jan1.AddHours(hours));

    [Fact]
    public void NoRows_AnswerEmpty()
    {
        var result = ActivityReport.Fold([], [], 500);

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

        var result = ActivityReport.Fold(rows, [], 500);

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

        var result = ActivityReport.Fold(rows, [1, 3], 500);

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

        var result = ActivityReport.Fold(rows, [], 2);

        Assert.Equal([3L, 2L], result.Events.Select(e => e.Id).ToArray());
        Assert.True(result.Truncated);
        Assert.Equal(3, result.Total);
        Assert.Equal(3, result.Kinds.Sum(k => k.Count));
        Assert.Equal(3, result.Issues.Count);
    }

    [Fact]
    public void AsManyRowsAsTheCap_IsNotTruncated()
    {
        var result = ActivityReport.Fold([Row(1, 1, "created", 1), Row(2, 1, "created", 2)], [], 2);

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

        var result = ActivityReport.Fold(rows, [], 500);

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

        var result = ActivityReport.Fold(rows, [], 500);

        Assert.Equal([3L, 2L, 1L], result.Events.Select(e => e.Id).ToArray());
    }
}

using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The pure fold alone: rows in, buckets out, with no database and no clock -
/// pinned the way <see cref="TimelineReportTests"/> pins <see cref="TimelineReport"/>.
/// </summary>
public class ChangelogReportTests
{
    private static readonly DateTimeOffset Jan1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static ChangelogReport.Row Epic(
        long id, DateTimeOffset closedAt, string? parentType = null) =>
        new(id, parentType, "epic", closedAt);

    [Theory]
    [InlineData(ChangelogReport.ChangelogBucketSize.Hour)]
    [InlineData(ChangelogReport.ChangelogBucketSize.Day)]
    [InlineData(ChangelogReport.ChangelogBucketSize.Week)]
    [InlineData(ChangelogReport.ChangelogBucketSize.Month)]
    public void AnEpicClosedInsideTheWindow_LandsInTheBucketMatchingItsClosedAt(
        ChangelogReport.ChangelogBucketSize bucket)
    {
        var closedAt = new DateTimeOffset(2026, 3, 10, 14, 0, 0, TimeSpan.Zero);
        var row = Epic(1, closedAt);

        var buckets = ChangelogReport.Fold([row], Jan1, Jan1.AddYears(1), bucket, offsetMinutes: 0);

        var expected = ChangelogReport.BoundsOf(closedAt, bucket, 0);
        var found = Assert.Single(buckets);
        Assert.Equal(expected.Start, found.Start);
        Assert.Equal(expected.End, found.End);
        Assert.Equal([1L], found.EpicIds);
    }

    [Fact]
    public void AnOffsetReaderBehindUtc_SeesTheDayBucketOnTheirOwnClock()
    {
        var row = Epic(1, new DateTimeOffset(2026, 1, 15, 1, 0, 0, TimeSpan.Zero));

        var buckets = ChangelogReport.Fold([row], Jan1, Jan1.AddYears(1), ChangelogReport.ChangelogBucketSize.Day, offsetMinutes: -300);

        var found = Assert.Single(buckets);
        Assert.Equal(new DateTime(2026, 1, 14), found.Start.AddMinutes(-300).DateTime);
    }

    [Fact]
    public void TwoRowsAnHourApartInTheSameWeek_LandInOneWeekBucketStartingMonday()
    {
        // 2026-01-12 is a Monday.
        var monday = new DateTimeOffset(2026, 1, 12, 0, 0, 0, TimeSpan.Zero);
        var rows = new[]
        {
            Epic(1, monday.AddDays(2).AddHours(10)),
            Epic(2, monday.AddDays(2).AddHours(11)),
        };

        var buckets = ChangelogReport.Fold(rows, Jan1, Jan1.AddYears(1), ChangelogReport.ChangelogBucketSize.Week, offsetMinutes: 0);

        var found = Assert.Single(buckets);
        Assert.Equal(monday, found.Start);
        Assert.Equal(2, found.EpicIds.Count);
    }

    [Fact]
    public void ASubEpicWhoseParentIsAlsoAnEpic_NeverAppears()
    {
        var row = Epic(1, new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero), parentType: "epic");

        var buckets = ChangelogReport.Fold([row], Jan1, Jan1.AddYears(1), ChangelogReport.ChangelogBucketSize.Day, offsetMinutes: 0);

        Assert.Empty(buckets);
    }

    [Theory]
    [InlineData("story")]
    [InlineData("bug")]
    [InlineData("task")]
    [InlineData("reflection")]
    public void ARowThatIsNotAnEpic_NeverAppears(string type)
    {
        var row = new ChangelogReport.Row(1, null, type, new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero));

        var buckets = ChangelogReport.Fold([row], Jan1, Jan1.AddYears(1), ChangelogReport.ChangelogBucketSize.Day, offsetMinutes: 0);

        Assert.Empty(buckets);
    }

    [Fact]
    public void TheWindowIsHalfOpen_FromIsInclusiveAndToIsExclusive()
    {
        var from = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero);
        var atFrom = Epic(1, from);
        var atTo = Epic(2, to);

        var buckets = ChangelogReport.Fold([atFrom, atTo], from, to, ChangelogReport.ChangelogBucketSize.Hour, offsetMinutes: 0);

        var ids = buckets.SelectMany(b => b.EpicIds).ToList();
        Assert.Contains(1L, ids);
        Assert.DoesNotContain(2L, ids);
    }

    [Fact]
    public void FromEqualToTo_AnswersNoBucketsAtAll()
    {
        var at = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var row = Epic(1, at);

        var buckets = ChangelogReport.Fold([row], at, at, ChangelogReport.ChangelogBucketSize.Hour, offsetMinutes: 0);

        Assert.Empty(buckets);
    }

    [Fact]
    public void WithinABucket_EpicIdsAreOrderedByClosedAtThenIdDescending()
    {
        var day = new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero);
        var rows = new[]
        {
            Epic(1, day.AddHours(1)),
            Epic(2, day.AddHours(3)),
            Epic(3, day.AddHours(3)),
        };

        var buckets = ChangelogReport.Fold(rows, Jan1, Jan1.AddYears(1), ChangelogReport.ChangelogBucketSize.Day, offsetMinutes: 0);

        var found = Assert.Single(buckets);
        Assert.Equal([3L, 2L, 1L], found.EpicIds);
    }

    [Fact]
    public void AcrossBuckets_BucketsComeBackNewestStartFirst()
    {
        var rows = new[]
        {
            Epic(1, new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)),
            Epic(2, new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero)),
        };

        var buckets = ChangelogReport.Fold(rows, Jan1, Jan1.AddYears(1), ChangelogReport.ChangelogBucketSize.Day, offsetMinutes: 0);

        Assert.Equal(2, buckets.Count);
        Assert.True(buckets[0].Start > buckets[1].Start);
        Assert.Equal([2L], buckets[0].EpicIds);
        Assert.Equal([1L], buckets[1].EpicIds);
    }

    [Fact]
    public void ARowWithNoParentAtAll_IsTopLevelAndAppears()
    {
        var row = Epic(1, new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero), parentType: null);

        var buckets = ChangelogReport.Fold([row], Jan1, Jan1.AddYears(1), ChangelogReport.ChangelogBucketSize.Day, offsetMinutes: 0);

        var found = Assert.Single(buckets);
        Assert.Equal([1L], found.EpicIds);
    }
}

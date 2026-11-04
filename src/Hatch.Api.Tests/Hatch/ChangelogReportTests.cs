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

    private static ChangelogReport.Row Issue(
        long id, string type, DateTimeOffset closedAt, string? parentType = null) =>
        new(id, parentType, type, closedAt);

    [Fact]
    public void ABugWithAnEpicParent_LandsInLoose()
    {
        var row = Issue(1, "bug", new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero), parentType: "epic");

        var buckets = ChangelogReport.FoldLoose([row], Jan1, Jan1.AddYears(1), ChangelogReport.ChangelogBucketSize.Day, offsetMinutes: 0);

        var found = Assert.Single(buckets);
        Assert.Equal([1L], found.IssueIds);
    }

    [Fact]
    public void ABugWithNoParentAtAll_LandsInLoose()
    {
        var row = Issue(1, "bug", new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero), parentType: null);

        var buckets = ChangelogReport.FoldLoose([row], Jan1, Jan1.AddYears(1), ChangelogReport.ChangelogBucketSize.Day, offsetMinutes: 0);

        var found = Assert.Single(buckets);
        Assert.Equal([1L], found.IssueIds);
    }

    [Fact]
    public void AStoryWithNoParentAtAll_LandsInLoose()
    {
        var row = Issue(1, "story", new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero), parentType: null);

        var buckets = ChangelogReport.FoldLoose([row], Jan1, Jan1.AddYears(1), ChangelogReport.ChangelogBucketSize.Day, offsetMinutes: 0);

        var found = Assert.Single(buckets);
        Assert.Equal([1L], found.IssueIds);
    }

    [Fact]
    public void AStoryWithAnEpicParent_NeverAppearsInLoose()
    {
        var row = Issue(1, "story", new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero), parentType: "epic");

        var buckets = ChangelogReport.FoldLoose([row], Jan1, Jan1.AddYears(1), ChangelogReport.ChangelogBucketSize.Day, offsetMinutes: 0);

        Assert.Empty(buckets);
    }

    [Fact]
    public void ATaskWithNoParent_NeverAppearsInLoose()
    {
        var row = Issue(1, "task", new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero), parentType: null);

        var buckets = ChangelogReport.FoldLoose([row], Jan1, Jan1.AddYears(1), ChangelogReport.ChangelogBucketSize.Day, offsetMinutes: 0);

        Assert.Empty(buckets);
    }

    [Fact]
    public void AReflectionWithNoParent_NeverAppearsInLoose()
    {
        var row = Issue(1, "reflection", new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero), parentType: null);

        var buckets = ChangelogReport.FoldLoose([row], Jan1, Jan1.AddYears(1), ChangelogReport.ChangelogBucketSize.Day, offsetMinutes: 0);

        Assert.Empty(buckets);
    }

    [Fact]
    public void WithinABucket_LooseIssueIdsAreOrderedByClosedAtThenIdDescending()
    {
        var day = new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero);
        var rows = new[]
        {
            Issue(1, "bug", day.AddHours(1)),
            Issue(2, "bug", day.AddHours(3)),
            Issue(3, "bug", day.AddHours(3)),
        };

        var buckets = ChangelogReport.FoldLoose(rows, Jan1, Jan1.AddYears(1), ChangelogReport.ChangelogBucketSize.Day, offsetMinutes: 0);

        var found = Assert.Single(buckets);
        Assert.Equal([3L, 2L, 1L], found.IssueIds);
    }

    [Fact]
    public void AnOffsetReaderBehindUtc_SeesTheLooseDayBucketOnTheirOwnClock()
    {
        var row = Issue(1, "bug", new DateTimeOffset(2026, 1, 15, 1, 0, 0, TimeSpan.Zero));

        var buckets = ChangelogReport.FoldLoose([row], Jan1, Jan1.AddYears(1), ChangelogReport.ChangelogBucketSize.Day, offsetMinutes: -300);

        var found = Assert.Single(buckets);
        Assert.Equal(new DateTime(2026, 1, 14), found.Start.AddMinutes(-300).DateTime);
    }

    [Fact]
    public void TheLooseWindowIsHalfOpen_FromIsInclusiveAndToIsExclusive()
    {
        var from = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero);
        var atFrom = Issue(1, "bug", from);
        var atTo = Issue(2, "bug", to);

        var buckets = ChangelogReport.FoldLoose([atFrom, atTo], from, to, ChangelogReport.ChangelogBucketSize.Hour, offsetMinutes: 0);

        var ids = buckets.SelectMany(b => b.IssueIds).ToList();
        Assert.Contains(1L, ids);
        Assert.DoesNotContain(2L, ids);
    }

    [Fact]
    public void AcrossBuckets_LooseBucketsComeBackNewestStartFirst()
    {
        var rows = new[]
        {
            Issue(1, "bug", new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)),
            Issue(2, "bug", new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero)),
        };

        var buckets = ChangelogReport.FoldLoose(rows, Jan1, Jan1.AddYears(1), ChangelogReport.ChangelogBucketSize.Day, offsetMinutes: 0);

        Assert.Equal(2, buckets.Count);
        Assert.True(buckets[0].Start > buckets[1].Start);
        Assert.Equal([2L], buckets[0].IssueIds);
        Assert.Equal([1L], buckets[1].IssueIds);
    }

    private static ChangelogReport.StoryRow Story(long id, long? epicId, DateTimeOffset closedAt) =>
        new(id, epicId, closedAt);

    private static ChangelogReport.EpicCandidate Candidate(long id, DateTimeOffset? closedAt) =>
        new(id, closedAt);

    [Fact]
    public void TwoStoriesUnderOneEpicInTheSameBucket_MakeOneGroupOrderedByClosedAt()
    {
        var day = new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero);
        var stories = new[]
        {
            Story(1, epicId: 100, day.AddHours(1)),
            Story(2, epicId: 100, day.AddHours(3)),
        };

        var buckets = ChangelogReport.FoldStories([], stories, Jan1, Jan1.AddYears(1), ChangelogReport.ChangelogBucketSize.Day, offsetMinutes: 0);

        var bucket = Assert.Single(buckets);
        var group = Assert.Single(bucket.Groups);
        Assert.Equal(100, group.EpicId);
        Assert.False(group.ClosedHere);
        Assert.Equal([2L, 1L], group.StoryIds);
    }

    [Fact]
    public void AStoryUnderASubEpic_GroupsUnderTheSubEpicNotTheRoot()
    {
        var story = Story(1, epicId: 200, new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero));

        var buckets = ChangelogReport.FoldStories([], [story], Jan1, Jan1.AddYears(1), ChangelogReport.ChangelogBucketSize.Day, offsetMinutes: 0);

        var bucket = Assert.Single(buckets);
        var group = Assert.Single(bucket.Groups);
        Assert.Equal(200, group.EpicId);
        Assert.Equal([1L], group.StoryIds);
    }

    [Fact]
    public void AnEpicClosedWithNoStoryClosingInThatBucket_MakesABareMilestoneGroup()
    {
        var closedAt = new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero);
        var epic = Candidate(100, closedAt);

        var buckets = ChangelogReport.FoldStories([epic], [], Jan1, Jan1.AddYears(1), ChangelogReport.ChangelogBucketSize.Day, offsetMinutes: 0);

        var bucket = Assert.Single(buckets);
        var group = Assert.Single(bucket.Groups);
        Assert.Equal(100, group.EpicId);
        Assert.True(group.ClosedHere);
        Assert.Empty(group.StoryIds);
    }

    [Fact]
    public void AnOpenEpicWithAStoryClosingUnderIt_MakesAGroupThatIsNotClosedHere()
    {
        var day = new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero);
        var epic = Candidate(100, closedAt: null);
        var story = Story(1, epicId: 100, day.AddHours(1));

        var buckets = ChangelogReport.FoldStories([epic], [story], Jan1, Jan1.AddYears(1), ChangelogReport.ChangelogBucketSize.Day, offsetMinutes: 0);

        var bucket = Assert.Single(buckets);
        var group = Assert.Single(bucket.Groups);
        Assert.Equal(100, group.EpicId);
        Assert.False(group.ClosedHere);
        Assert.Equal([1L], group.StoryIds);
    }

    [Fact]
    public void AStoryWithNoEpicAtAll_MakesNoGroupAnywhere()
    {
        var story = Story(1, epicId: null, new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero));

        var buckets = ChangelogReport.FoldStories([], [story], Jan1, Jan1.AddYears(1), ChangelogReport.ChangelogBucketSize.Day, offsetMinutes: 0);

        Assert.Empty(buckets);
    }

    [Fact]
    public void TwoEpicsClosingInTheSameBucket_OneAlsoCarryingAStoryElsewhere_AreBothPresentAndOrdered()
    {
        var day = new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero);
        var epics = new[]
        {
            Candidate(100, day.AddHours(1)),
            Candidate(200, day.AddHours(5)),
        };
        var stories = new[]
        {
            // Epic 100's story closes in a different bucket the next day, so it
            // groups separately from epic 100's own bare close above.
            Story(1, epicId: 100, day.AddDays(1).AddHours(2)),
        };

        var buckets = ChangelogReport.FoldStories(epics, stories, Jan1, Jan1.AddYears(1), ChangelogReport.ChangelogBucketSize.Day, offsetMinutes: 0);

        Assert.Equal(2, buckets.Count);

        var dayBucket = buckets.Single(b => b.Start == ChangelogReport.BoundsOf(day, ChangelogReport.ChangelogBucketSize.Day, 0).Start);
        Assert.Equal(2, dayBucket.Groups.Count);
        // Newest close first: epic 200 (hour 5) before epic 100 (hour 1).
        Assert.Equal([200L, 100L], dayBucket.Groups.Select(g => g.EpicId).ToList());
        Assert.All(dayBucket.Groups, g => Assert.True(g.ClosedHere));
        Assert.Empty(dayBucket.Groups.Single(g => g.EpicId == 100).StoryIds);
        Assert.Empty(dayBucket.Groups.Single(g => g.EpicId == 200).StoryIds);

        var nextDayBucket = buckets.Single(b => b != dayBucket);
        var storyGroup = Assert.Single(nextDayBucket.Groups);
        Assert.Equal(100, storyGroup.EpicId);
        Assert.False(storyGroup.ClosedHere);
        Assert.Equal([1L], storyGroup.StoryIds);
    }

    [Fact]
    public void AcrossTwoBuckets_StoryBucketsComeBackNewestStartFirst()
    {
        var stories = new[]
        {
            Story(1, epicId: 100, new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)),
            Story(2, epicId: 200, new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero)),
        };

        var buckets = ChangelogReport.FoldStories([], stories, Jan1, Jan1.AddYears(1), ChangelogReport.ChangelogBucketSize.Day, offsetMinutes: 0);

        Assert.Equal(2, buckets.Count);
        Assert.True(buckets[0].Start > buckets[1].Start);
        Assert.Equal(200, Assert.Single(buckets[0].Groups).EpicId);
        Assert.Equal(100, Assert.Single(buckets[1].Groups).EpicId);
    }
}

using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The fold from "every close" to "the newest window, grouped and ordered" -
/// pure and synchronous, so every case here is hand-built records and no
/// database.
/// </summary>
public class ChangelogTests
{
    private static readonly DateTimeOffset Base = new(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);

    private static Changelog.Entry Entry(
        long id, DateTimeOffset closedAt, string type = "task", int descendantCount = 0, string? key = null) =>
        new(id, key ?? $"AER-{id}", $"Issue {id}", type, "AER", null, closedAt, descendantCount);

    // ---- ResolveWindow ----

    [Fact]
    public void NoCloses_AnswersNoWindowAtAll()
    {
        var window = Changelog.ResolveWindow([], Base, days: 14);

        Assert.Equal(new Changelog.Window(null, null, false), window);
    }

    [Fact]
    public void ACloseAt2359AndOneAt0001TheNextDay_AreTwoDays()
    {
        var late = Base + TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59);
        var early = Base.AddDays(1) + TimeSpan.FromMinutes(1);

        var window = Changelog.ResolveWindow([late, early], Base.AddDays(2), days: 14);

        Assert.Equal(Base.ToString("yyyy-MM-dd"), window.Oldest);
    }

    [Fact]
    public void AQuietStretchBetweenTwoDays_IsSkippedWhole_WithNoEmptyDaysCounted()
    {
        var recent = Base;
        var old = Base.AddDays(-10);

        // Asking for the newest 2 days that shipped something reaches straight
        // across the ten-day gap - the quiet days never appear and never count.
        var window = Changelog.ResolveWindow([recent, old], Base.AddDays(1), days: 2);

        Assert.Equal(old.ToString("yyyy-MM-dd"), window.Oldest);
    }

    [Fact]
    public void BeforeIsExclusive_ACloseExactlyAtBeforeIsExcluded_OneTickEarlierIsIncluded()
    {
        var atBefore = Base;
        var before = Base;

        var excluded = Changelog.ResolveWindow([atBefore], before, days: 14);
        Assert.Equal(new Changelog.Window(null, null, false), excluded);

        var included = Changelog.ResolveWindow([atBefore - TimeSpan.FromTicks(1)], before, days: 14);
        Assert.NotNull(included.Oldest);
    }

    [Fact]
    public void HasOlder_IsTrueMidHistory_AndFalseAtTheEndOfHistory()
    {
        var midHistory = Changelog.ResolveWindow([Base, Base.AddDays(-1)], Base.AddDays(1), days: 1);
        Assert.True(midHistory.HasOlder);

        var endOfHistory = Changelog.ResolveWindow([Base], Base.AddDays(1), days: 1);
        Assert.False(endOfHistory.HasOlder);
    }

    [Fact]
    public void BeforeSetToTheFirstWindowsOldestDayStart_AnswersTheNextWindow_WithNoOverlapAndNoGap()
    {
        var closes = new[] { Base, Base.AddDays(-1), Base.AddDays(-2) };

        var first = Changelog.ResolveWindow(closes, Base.AddDays(1), days: 1);
        Assert.Equal(Base.ToString("yyyy-MM-dd"), first.Oldest);

        var second = Changelog.ResolveWindow(closes, first.OldestDayStart!.Value, days: 1);
        Assert.Equal(Base.AddDays(-1).ToString("yyyy-MM-dd"), second.Oldest);
    }

    // ---- Months ----

    [Fact]
    public void Months_CountsEveryClosedMonth_RegardlessOfAnyWindow()
    {
        var closes = new[]
        {
            Base,
            Base.AddMonths(-1),
            Base.AddMonths(-13),
        };

        var months = Changelog.Months(closes);

        Assert.Equal(3, months.Count);
        Assert.Equal(Base.ToString("yyyy-MM"), months[0].Month);
        Assert.Equal(Base.AddMonths(-13).ToString("yyyy-MM"), months[^1].Month);
    }

    [Fact]
    public void Months_CountsOneEntryPerClose_NotPerDay()
    {
        var months = Changelog.Months([Base, Base.AddHours(1), Base.AddDays(1)]);

        Assert.Equal(3, Assert.Single(months).Count);
    }

    // ---- Days ----

    [Fact]
    public void FeaturesComeBeforeFixes_RegardlessOfCloseOrder()
    {
        var fix = Entry(1, Base + TimeSpan.FromHours(2), "task");
        var feature = Entry(2, Base + TimeSpan.FromHours(1), "epic");

        var days = Changelog.Days([fix, feature]);

        var day = Assert.Single(days);
        Assert.Equal(["AER-2"], day.Features.Select(e => e.Key));
        Assert.Equal(["AER-1"], day.Fixes.Select(e => e.Key));
    }

    [Fact]
    public void FixesOrderByStoryThenBugThenTask_RegardlessOfCloseOrder()
    {
        var task = Entry(1, Base + TimeSpan.FromHours(3), "task");
        var bug = Entry(2, Base + TimeSpan.FromHours(2), "bug");
        var story = Entry(3, Base + TimeSpan.FromHours(1), "story");

        var days = Changelog.Days([task, bug, story]);

        Assert.Equal(["AER-3", "AER-2", "AER-1"], Assert.Single(days).Fixes.Select(e => e.Key));
    }

    [Fact]
    public void WithinOneType_LaterCloseComesFirst()
    {
        var older = Entry(1, Base + TimeSpan.FromHours(1), "task");
        var newer = Entry(2, Base + TimeSpan.FromHours(2), "task");

        var days = Changelog.Days([older, newer]);

        Assert.Equal(["AER-2", "AER-1"], Assert.Single(days).Fixes.Select(e => e.Key));
    }

    [Fact]
    public void AParentlessTaskWithNoChildren_StillReadsAsAFix_WithDescendantCountZero()
    {
        var task = Entry(1, Base, "task", descendantCount: 0);

        var day = Assert.Single(Changelog.Days([task]));

        var row = Assert.Single(day.Fixes);
        Assert.Equal(0, row.DescendantCount);
    }

    [Fact]
    public void DaysComeBackNewestFirst()
    {
        var newer = Entry(1, Base, "task");
        var older = Entry(2, Base.AddDays(-1), "task");

        var days = Changelog.Days([older, newer]);

        Assert.Equal([Base.ToString("yyyy-MM-dd"), Base.AddDays(-1).ToString("yyyy-MM-dd")], days.Select(d => d.Date));
    }
}

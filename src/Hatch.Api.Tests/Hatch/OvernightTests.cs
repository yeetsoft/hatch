using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The fold from "every landing" to "the newest night, ordered" - pure and
/// synchronous, so every case here is hand-built records and no database.
/// </summary>
public class OvernightTests
{
    private static readonly DateTimeOffset Base = new(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);

    private static Overnight.Landing Landing(
        long id, DateTimeOffset at, string type = "task", int descendantCount = 0, string? key = null) =>
        new(id, key ?? $"AER-{id}", type, $"Issue {id}", "AER", at, descendantCount);

    [Fact]
    public void EmptyInput_AnswersAnEmptyNight()
    {
        var result = Overnight.NewestNight([]);

        Assert.Equal(new OvernightDto(null, null, 0, []), result);
    }

    [Fact]
    public void AGapOfExactlyQuietGap_IsOneNight()
    {
        var older = Landing(1, Base);
        var newer = Landing(2, Base + Overnight.QuietGap);

        var result = Overnight.NewestNight([older, newer]);

        Assert.Equal(2, result.Count);
        Assert.Equal(older.At, result.From);
        Assert.Equal(newer.At, result.To);
    }

    [Fact]
    public void OneTickOverQuietGap_SplitsTwoNights()
    {
        var older = Landing(1, Base);
        var newer = Landing(2, Base + Overnight.QuietGap + TimeSpan.FromTicks(1));

        var result = Overnight.NewestNight([older, newer]);

        Assert.Equal(1, result.Count);
        Assert.Equal(newer.At, result.From);
        Assert.Equal(newer.At, result.To);
    }

    [Fact]
    public void LandingsOnBothSidesOfAFourHourPlusGap_ComeBackAsOnlyTheNewerRun()
    {
        var oldNight = new[]
        {
            Landing(1, Base),
            Landing(2, Base + TimeSpan.FromMinutes(10)),
        };
        var newNight = new[]
        {
            Landing(3, Base + TimeSpan.FromHours(10)),
            Landing(4, Base + TimeSpan.FromHours(10) + TimeSpan.FromMinutes(5)),
        };

        var result = Overnight.NewestNight([.. oldNight, .. newNight]);

        Assert.Equal(2, result.Count);
        Assert.All(result.Rows, row => Assert.Contains(row.Key, newNight.Select(l => l.Key)));
    }

    [Fact]
    public void OneOfEachType_ComesBackEpicBugStoryTask_RegardlessOfLandingOrIdOrder()
    {
        var task = Landing(1, Base + TimeSpan.FromMinutes(30), "task");
        var story = Landing(2, Base + TimeSpan.FromMinutes(20), "story");
        var bug = Landing(3, Base + TimeSpan.FromMinutes(10), "bug");
        var epic = Landing(4, Base, "epic");

        var result = Overnight.NewestNight([task, story, bug, epic]);

        Assert.Equal(["epic", "bug", "story", "task"], result.Rows.Select(r => r.Type));
    }

    [Fact]
    public void OneOfEachTypeIncludingAReflection_ComesBackEpicReflectionBugStoryTask()
    {
        var task = Landing(1, Base + TimeSpan.FromMinutes(40), "task");
        var story = Landing(2, Base + TimeSpan.FromMinutes(30), "story");
        var bug = Landing(3, Base + TimeSpan.FromMinutes(20), "bug");
        var reflection = Landing(4, Base + TimeSpan.FromMinutes(10), "reflection");
        var epic = Landing(5, Base, "epic");

        var result = Overnight.NewestNight([task, story, bug, reflection, epic]);

        Assert.Equal(["epic", "reflection", "bug", "story", "task"], result.Rows.Select(r => r.Type));
    }

    [Fact]
    public void WithinOneType_OrdersByDescendantCountThenAtThenIssueId_AllDescending()
    {
        var landings = new[]
        {
            Landing(10, Base, descendantCount: 5, key: "AER-10"),
            Landing(11, Base, descendantCount: 5, key: "AER-11"),
            Landing(12, Base + TimeSpan.FromMinutes(1), descendantCount: 5, key: "AER-12"),
            Landing(13, Base, descendantCount: 9, key: "AER-13"),
        };

        var result = Overnight.NewestNight(landings);

        // 13 wins on DescendantCount alone; 12 beats 10/11 on a later At despite
        // a tied count; 11 beats 10 on nothing but a higher IssueId - three ties
        // broken by three different fields, each one descending.
        Assert.Equal(["AER-13", "AER-12", "AER-11", "AER-10"], result.Rows.Select(r => r.Key));
    }

    [Fact]
    public void Count_MatchesTheRowList()
    {
        var result = Overnight.NewestNight([Landing(1, Base), Landing(2, Base)]);

        Assert.Equal(result.Rows.Count, result.Count);
    }

    [Fact]
    public void NoSeenAtAtAll_ReportsEveryRowUnseen()
    {
        var result = Overnight.NewestNight([Landing(1, Base), Landing(2, Base + TimeSpan.FromMinutes(1))]);

        Assert.Equal(2, result.Unseen);
    }

    [Fact]
    public void ASeenAtBetweenTwoLandings_CountsOnlyTheLaterOne()
    {
        var older = Landing(1, Base);
        var newer = Landing(2, Base + TimeSpan.FromMinutes(1));

        var result = Overnight.NewestNight([older, newer], seenAt: Base + TimeSpan.FromSeconds(30));

        Assert.Equal(1, result.Unseen);
    }

    [Fact]
    public void ASeenAtAfterEveryLanding_ReportsNothingUnseen()
    {
        var result = Overnight.NewestNight(
            [Landing(1, Base), Landing(2, Base + TimeSpan.FromMinutes(1))],
            seenAt: Base + TimeSpan.FromHours(1));

        Assert.Equal(0, result.Unseen);
    }

    [Fact]
    public void ASeenAtExactlyEqualToALanding_DoesNotCountThatLanding()
    {
        var landing = Landing(1, Base);

        var result = Overnight.NewestNight([landing], seenAt: Base);

        Assert.Equal(0, result.Unseen);
    }
}

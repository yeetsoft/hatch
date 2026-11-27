using Hatch.Api.Modules.Hatch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// Where an ambition stands, read as four counts and a list of open epics.
///
/// Each case seeds one capability per fact it pins, so a count of one says
/// which rule put it there. <c>Why</c> is pinned as present or absent, not by
/// its words: the sentence is the page's to reword.
/// </summary>
public class AmbitionsControllerTests
{
    [Fact]
    public async Task OneCapabilityInEachCount_IsCountedOnceEach_WithTheOpenEpicsAndTheLimit()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "the ambition", wipLimit: 3);
        var held = await h.CapabilityAsync(ambition, 1);
        var waiting = await h.CapabilityAsync(ambition, 2);
        var flying = await h.CapabilityAsync(ambition, 3);
        await h.CapabilityAsync(ambition, 4);
        await h.VerdictAsync(held, EfHatchCapabilityVerdict.Holds);

        var waits = await h.FileAsync("epic", "waits", parentId: ambition.Id, rank: 1);
        var free = await h.FileAsync("epic", "free", parentId: ambition.Id, rank: 2);
        await h.AskAsync(waits, "which?", level: QuestionLevels.HoldName);
        await h.ServeAsync(waits, waiting);
        await h.ServeAsync(free, flying);

        var row = Assert.Single(await h.ReadAsync());

        Assert.Equal(h.Key(ambition), row.Key);
        Assert.Equal("AER", row.ProjectKey);
        Assert.Equal(3, row.Limit);
        Assert.Equal((4, 1, 1, 1, 1), (row.Capabilities, row.Holds, row.WaitsOnPerson, row.InFlight, row.NotStarted));

        Assert.Equal([h.Key(waits), h.Key(free)], row.Open.Select(e => e.Key));
        Assert.Equal([2], row.Open[0].Serves);
        Assert.True(row.Open[0].WaitsOnPerson);
        Assert.NotNull(row.Open[0].Why);
        Assert.Equal([3], row.Open[1].Serves);
        Assert.False(row.Open[1].WaitsOnPerson);
        Assert.Null(row.Open[1].Why);
    }

    [Fact]
    public async Task ALimitThatIsUnset_ReadsAsOne()
    {
        var h = await NewAsync();
        await h.FileAsync("ambition", "a");

        Assert.Equal(1, Assert.Single(await h.ReadAsync()).Limit);
    }

    [Fact]
    public async Task ADoesNotHoldVerdictWithAnOpenEpicServingIt_IsInFlight()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a");
        var capability = await h.CapabilityAsync(ambition, 1);
        await h.VerdictAsync(capability, EfHatchCapabilityVerdict.DoesNotHold);
        await h.ServeAsync(await h.FileAsync("epic", "e", parentId: ambition.Id), capability);

        var row = Assert.Single(await h.ReadAsync());

        Assert.Equal((0, 1, 0, 0), (row.Holds, row.InFlight, row.WaitsOnPerson, row.NotStarted));
    }

    [Fact]
    public async Task ACannotTellVerdictWithNoEpic_IsNotStarted()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a");
        await h.VerdictAsync(await h.CapabilityAsync(ambition, 1), EfHatchCapabilityVerdict.CannotTell);

        var row = Assert.Single(await h.ReadAsync());

        Assert.Equal((0, 0, 0, 1), (row.Holds, row.InFlight, row.WaitsOnPerson, row.NotStarted));
    }

    [Fact]
    public async Task ANewerVerdict_ReplacesTheOlderOne()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a");
        var capability = await h.CapabilityAsync(ambition, 1);
        await h.VerdictAsync(capability, EfHatchCapabilityVerdict.Holds);
        await h.VerdictAsync(capability, EfHatchCapabilityVerdict.DoesNotHold);

        var row = Assert.Single(await h.ReadAsync());

        Assert.Equal((0, 1), (row.Holds, row.NotStarted));
    }

    [Fact]
    public async Task ARunOfHoldsThatBeganExactlySevenDaysAgo_IsNewlyHolding()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a");
        await h.VerdictAsync(await h.CapabilityAsync(ambition, 1), EfHatchCapabilityVerdict.Holds, Now.AddDays(-7));

        var row = Assert.Single(await h.ReadAsync());

        Assert.Equal((1, 1), (row.Holds, row.NewlyHolding));
    }

    [Fact]
    public async Task ARunOfHoldsThatBeganSevenDaysAndASecondAgo_IsNotNewlyHolding()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a");
        await h.VerdictAsync(await h.CapabilityAsync(ambition, 1), EfHatchCapabilityVerdict.Holds, Now.AddDays(-7).AddSeconds(-1));

        var row = Assert.Single(await h.ReadAsync());

        Assert.Equal((1, 0), (row.Holds, row.NewlyHolding));
    }

    [Fact]
    public async Task AnOldRunOfHolds_StaysOldWhenANewHoldsJoinsIt()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a");
        var capability = await h.CapabilityAsync(ambition, 1);
        await h.VerdictAsync(capability, EfHatchCapabilityVerdict.Holds, Now.AddDays(-30));
        await h.VerdictAsync(capability, EfHatchCapabilityVerdict.Holds, Now.AddDays(-1));

        var row = Assert.Single(await h.ReadAsync());

        Assert.Equal((1, 0), (row.Holds, row.NewlyHolding));
    }

    [Theory]
    [InlineData(EfHatchCapabilityVerdict.DoesNotHold)]
    [InlineData(EfHatchCapabilityVerdict.CannotTell)]
    public async Task AVerdictThatIsNotHolds_BetweenTwoHolds_StartsTheRunAgain(string between)
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a");
        var capability = await h.CapabilityAsync(ambition, 1);
        await h.VerdictAsync(capability, EfHatchCapabilityVerdict.Holds, Now.AddDays(-30));
        await h.VerdictAsync(capability, between, Now.AddDays(-20));
        await h.VerdictAsync(capability, EfHatchCapabilityVerdict.Holds, Now.AddDays(-1));

        var row = Assert.Single(await h.ReadAsync());

        Assert.Equal((1, 1), (row.Holds, row.NewlyHolding));
    }

    [Fact]
    public async Task ACapabilityWhoseNewestVerdictIsNotHolds_OrThatWasNeverJudged_IsNotNewlyHolding()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a");
        var lost = await h.CapabilityAsync(ambition, 1);
        await h.VerdictAsync(lost, EfHatchCapabilityVerdict.Holds, Now.AddDays(-2));
        await h.VerdictAsync(lost, EfHatchCapabilityVerdict.DoesNotHold, Now.AddDays(-1));
        await h.CapabilityAsync(ambition, 2);

        var row = Assert.Single(await h.ReadAsync());

        Assert.Equal((0, 0), (row.Holds, row.NewlyHolding));
    }

    [Fact]
    public async Task EachAmbition_CountsItsOwnNewlyHolding()
    {
        var h = await NewAsync();
        var first = await h.FileAsync("ambition", "first", rank: 1);
        var second = await h.FileAsync("ambition", "second", rank: 2);
        await h.VerdictAsync(await h.CapabilityAsync(first, 1), EfHatchCapabilityVerdict.Holds, Now.AddDays(-1));
        await h.VerdictAsync(await h.CapabilityAsync(first, 2), EfHatchCapabilityVerdict.Holds, Now.AddDays(-2));
        await h.VerdictAsync(await h.CapabilityAsync(second, 1), EfHatchCapabilityVerdict.Holds, Now.AddDays(-1));
        await h.VerdictAsync(await h.CapabilityAsync(second, 2), EfHatchCapabilityVerdict.Holds, Now.AddDays(-40));

        var rows = await h.ReadAsync();

        Assert.Equal([(2, 2), (2, 1)], rows.Select(r => (r.Holds, r.NewlyHolding)));
    }

    [Fact]
    public async Task AHoldQuestionOnTheAmbition_FoldsOnlyItsOwnPass_AndLeavesEveryOpenEpicBeneathItFlying()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a");
        var one = await h.CapabilityAsync(ambition, 1);
        var two = await h.CapabilityAsync(ambition, 2);
        await h.ServeAsync(await h.FileAsync("epic", "one", parentId: ambition.Id), one);
        await h.ServeAsync(await h.FileAsync("epic", "two", parentId: ambition.Id), two);
        await h.AskAsync(ambition, "which way?", level: QuestionLevels.HoldName);

        var row = Assert.Single(await h.ReadAsync());

        Assert.Equal((0, 2), (row.WaitsOnPerson, row.InFlight));
        Assert.All(row.Open, e => Assert.False(e.WaitsOnPerson));
    }

    [Fact]
    public async Task AQuestionAtBuild_FoldsNothing()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a");
        var capability = await h.CapabilityAsync(ambition, 1);
        var epic = await h.FileAsync("epic", "e", parentId: ambition.Id);
        await h.ServeAsync(epic, capability);
        await h.AskAsync(ambition, "meanwhile?", level: QuestionLevels.BuildName);
        await h.AskAsync(epic, "and this?", level: QuestionLevels.BuildName);

        var row = Assert.Single(await h.ReadAsync());

        Assert.Equal((1, 0), (row.InFlight, row.WaitsOnPerson));
        Assert.False(Assert.Single(row.Open).WaitsOnPerson);
    }

    [Fact]
    public async Task AHoldQuestionOnAStoryBeneathTheEpic_DoesNotFoldTheEpic()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a");
        var capability = await h.CapabilityAsync(ambition, 1);
        var epic = await h.FileAsync("epic", "e", parentId: ambition.Id);
        await h.ServeAsync(epic, capability);
        await h.AskAsync(await h.FileAsync("story", "s", parentId: epic.Id), "which?", level: QuestionLevels.HoldName);

        var row = Assert.Single(await h.ReadAsync());

        Assert.Equal((1, 0), (row.InFlight, row.WaitsOnPerson));
    }

    [Fact]
    public async Task AnEpicWithHeldSet_IsWaitingOnAPerson()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a");
        var capability = await h.CapabilityAsync(ambition, 1);
        var epic = await h.FileAsync("epic", "e", parentId: ambition.Id, held: true);
        await h.ServeAsync(epic, capability);

        var row = Assert.Single(await h.ReadAsync());

        Assert.Equal((1, 0), (row.WaitsOnPerson, row.InFlight));
        Assert.NotNull(Assert.Single(row.Open).Why);
    }

    [Fact]
    public async Task ADangerousEpicWithAQuestionAtBuild_IsWaitingOnAPerson()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a");
        var capability = await h.CapabilityAsync(ambition, 1);
        var epic = await h.FileAsync("epic", "e", parentId: ambition.Id, dangerous: "load test");
        await h.ServeAsync(epic, capability);
        await h.AskAsync(epic, "ready?", level: QuestionLevels.BuildName);

        var row = Assert.Single(await h.ReadAsync());

        Assert.Equal((1, 0), (row.WaitsOnPerson, row.InFlight));
    }

    [Fact]
    public async Task ADangerousEpicWithNoQuestion_IsNotWaiting()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a");
        var capability = await h.CapabilityAsync(ambition, 1);
        var epic = await h.FileAsync("epic", "e", parentId: ambition.Id, dangerous: "load test");
        await h.ServeAsync(epic, capability);

        Assert.Equal(1, Assert.Single(await h.ReadAsync()).InFlight);
    }

    [Fact]
    public async Task ADangerousEpicUnderAnAmbitionWithAQuestionOnTheAmbition_IsWaitingOnAPerson()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a");
        var capability = await h.CapabilityAsync(ambition, 1);
        var epic = await h.FileAsync("epic", "e", parentId: ambition.Id, dangerous: "load test");
        await h.ServeAsync(epic, capability);
        await h.AskAsync(ambition, "ready?", level: QuestionLevels.HoldName);

        var row = Assert.Single(await h.ReadAsync());

        Assert.Equal((1, 0), (row.WaitsOnPerson, row.InFlight));
    }

    [Fact]
    public async Task AClosedEpicThatServesACapability_CountsForNothing()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a");
        var capability = await h.CapabilityAsync(ambition, 1);
        await h.ServeAsync(await h.FileAsync("epic", "finished", parentId: ambition.Id, statusId: h.Done), capability);

        var row = Assert.Single(await h.ReadAsync());

        Assert.Equal((0, 0, 1), (row.InFlight, row.WaitsOnPerson, row.NotStarted));
        Assert.Empty(row.Open);
    }

    [Fact]
    public async Task ADeferredEpic_CountsAsClosed()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a");
        var capability = await h.CapabilityAsync(ambition, 1);
        await h.ServeAsync(await h.FileAsync("epic", "shelved", parentId: ambition.Id, statusId: h.Shelved), capability);

        var row = Assert.Single(await h.ReadAsync());

        Assert.Equal((0, 1), (row.InFlight, row.NotStarted));
        Assert.Empty(row.Open);
    }

    [Fact]
    public async Task ACapabilityServedByOneWaitingEpicAndOneFreeEpic_IsInFlight()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a");
        var capability = await h.CapabilityAsync(ambition, 1);
        var waits = await h.FileAsync("epic", "waits", parentId: ambition.Id, held: true);
        var free = await h.FileAsync("epic", "free", parentId: ambition.Id);
        await h.ServeAsync(waits, capability);
        await h.ServeAsync(free, capability);

        var row = Assert.Single(await h.ReadAsync());

        Assert.Equal((1, 0), (row.InFlight, row.WaitsOnPerson));
    }

    [Fact]
    public async Task AnEpicServingTwoCapabilities_ListsBothPositionsAscending()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a");
        var first = await h.CapabilityAsync(ambition, 1);
        await h.CapabilityAsync(ambition, 2);
        var third = await h.CapabilityAsync(ambition, 3);
        var epic = await h.FileAsync("epic", "e", parentId: ambition.Id);
        await h.ServeAsync(epic, third);
        await h.ServeAsync(epic, first);

        Assert.Equal([1, 3], Assert.Single(Assert.Single(await h.ReadAsync()).Open).Serves);
    }

    [Fact]
    public async Task TwoAmbitionsInTwoProjects_ComeBackTogether_AndProjectIdNarrowsToOne()
    {
        var h = await NewAsync();
        var here = await h.FileAsync("ambition", "here");
        var there = await h.FileAsync("ambition", "there", project: h.Other);
        await h.CapabilityAsync(here, 1);
        await h.CapabilityAsync(there, 1);

        var both = await h.ReadAsync();
        Assert.Equal(["AER", "OPS"], both.Select(r => r.ProjectKey).Order());
        Assert.Equal([h.Key(here), h.Key(there)], both.Select(r => r.Key).Order());

        var one = Assert.Single(await h.ReadAsync(h.Other));
        Assert.Equal(("OPS", h.Key(there)), (one.ProjectKey, one.Key));

        Assert.Equal(2, (await h.ReadAsync(h.ProjectId, h.Other)).Length);
    }

    [Fact]
    public async Task AnAmbitionWithNoCapabilities_HasEveryCountAtZero()
    {
        var h = await NewAsync();
        await h.FileAsync("ambition", "bare");

        var row = Assert.Single(await h.ReadAsync());

        Assert.Equal((0, 0, 0, 0, 0), (row.Capabilities, row.Holds, row.InFlight, row.WaitsOnPerson, row.NotStarted));
    }

    [Fact]
    public async Task AnAmbitionWithNoOpenEpic_HasAnEmptyOpenList()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a");
        await h.CapabilityAsync(ambition, 1);

        Assert.Empty(Assert.Single(await h.ReadAsync()).Open);
    }

    [Fact]
    public async Task NoAmbitions_IsAnEmptyArray()
    {
        var h = await NewAsync();
        await h.FileAsync("epic", "not an ambition");

        Assert.Empty(await h.ReadAsync());
    }

    [Fact]
    public async Task AmbitionsComeBackInBoardOrder_ColumnThenRankThenId()
    {
        var h = await NewAsync();
        var later = await h.FileAsync("ambition", "later column", statusId: h.Doing);
        var lowRank = await h.FileAsync("ambition", "low rank", rank: 10);
        var highRank = await h.FileAsync("ambition", "high rank", rank: 20);
        var tieFirst = await h.FileAsync("ambition", "tie first", rank: 30);
        var tieSecond = await h.FileAsync("ambition", "tie second", rank: 30);

        var keys = (await h.ReadAsync()).Select(r => r.Key);

        Assert.Equal(new[] { lowRank, highRank, tieFirst, tieSecond, later }.Select(h.Key), keys);
    }

    [Fact]
    public async Task OpenEpics_ComeBackInRankOrder_AndEachCarriesItsColumn()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a");
        var second = await h.FileAsync("epic", "second", parentId: ambition.Id, rank: 20, statusId: h.Doing);
        var first = await h.FileAsync("epic", "first", parentId: ambition.Id, rank: 10);

        var open = Assert.Single(await h.ReadAsync()).Open;

        Assert.Equal([h.Key(first), h.Key(second)], open.Select(e => e.Key));
        Assert.Equal(["todo", "doing"], open.Select(e => e.StatusName));
        Assert.Equal([h.Todo, h.Doing], open.Select(e => e.StatusId));
    }

    [Fact]
    public async Task AnEpicOfAnotherAmbition_IsNotListedAndServesNothingHere()
    {
        var h = await NewAsync();
        var mine = await h.FileAsync("ambition", "mine");
        var theirs = await h.FileAsync("ambition", "theirs");
        var capability = await h.CapabilityAsync(mine, 1);
        await h.ServeAsync(await h.FileAsync("epic", "elsewhere", parentId: theirs.Id), capability);

        var rows = await h.ReadAsync();
        var mineRow = rows.Single(r => r.Key == h.Key(mine));

        Assert.Equal((0, 1), (mineRow.InFlight, mineRow.NotStarted));
        Assert.Empty(mineRow.Open);
        Assert.Empty(Assert.Single(rows.Single(r => r.Key == h.Key(theirs)).Open).Serves);
    }

    // ---- The harness ----

    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private class Harness
    {
        public required HatchContext Db { get; init; }
        public required AmbitionsController Ambitions { get; init; }
        public required int ProjectId { get; init; }
        public required int Other { get; init; }
        public required int Todo { get; init; }
        public required int Doing { get; init; }
        public required int Done { get; init; }
        public required int Shelved { get; init; }

        private int next = 1;
        private int rank = 1024;

        public async Task<EfHatchIssue> FileAsync(
            string type, string title, int? statusId = null, long? parentId = null, int? project = null,
            int? rank = null, bool held = false, string? dangerous = null, int? wipLimit = null)
        {
            var issue = new EfHatchIssue
            {
                ProjectId = project ?? ProjectId,
                Number = next++,
                Type = type,
                Title = title,
                Description = "",
                StatusId = statusId ?? Todo,
                Rank = rank ?? this.rank,
                ParentId = parentId,
                Held = held,
                Dangerous = dangerous,
                WipLimit = wipLimit,
                CreatedBy = "operator",
                CreatedAt = Now,
                UpdatedAt = Now,
            };

            Db.Issues.Add(issue);
            await Db.SaveChangesAsync();
            return issue;
        }

        public async Task<EfHatchCapability> CapabilityAsync(EfHatchIssue ambition, int position)
        {
            var capability = new EfHatchCapability
            {
                IssueId = ambition.Id,
                Position = position,
                Statement = $"capability {position}",
                HowToTell = "look",
                CreatedBy = "operator",
                CreatedAt = Now,
                UpdatedAt = Now,
            };

            Db.Capabilities.Add(capability);
            await Db.SaveChangesAsync();
            return capability;
        }

        public async Task VerdictAsync(EfHatchCapability capability, string verdict, DateTimeOffset? at = null)
        {
            Db.CapabilityVerdicts.Add(new EfHatchCapabilityVerdict
            {
                CapabilityId = capability.Id,
                Verdict = verdict,
                LookedAt = "the page",
                Sha = "abc1234",
                Author = "hatch-agent",
                CreatedAt = at ?? Now,
            });

            await Db.SaveChangesAsync();
        }

        public async Task ServeAsync(EfHatchIssue epic, EfHatchCapability capability)
        {
            Db.CapabilityLinks.Add(new EfHatchCapabilityLink
            {
                CapabilityId = capability.Id,
                IssueId = epic.Id,
                Actor = "operator",
                At = Now,
            });

            await Db.SaveChangesAsync();
        }

        public async Task AskAsync(EfHatchIssue issue, string body, string? level = null)
        {
            Db.Comments.Add(new EfHatchComment
            {
                IssueId = issue.Id,
                Author = "hatch-agent",
                Body = body,
                Kind = EfHatchComment.Question,
                Level = level,
                CreatedAt = Now,
            });

            await Db.SaveChangesAsync();
        }

        public async Task<AmbitionRowDto[]> ReadAsync(params int[] projectIds) =>
            Value(await Ambitions.GetAmbitions(projectIds.Length == 0 ? null : projectIds.ToList(), default));

        /// <summary>The key of an issue filed here, in whichever of the two projects it went to.</summary>
        public string Key(EfHatchIssue issue) =>
            IssueKey.Format(issue.ProjectId == ProjectId ? "AER" : "OPS", issue.Number);
    }

    /// <summary>Two projects, and a board with a column to wait in, one to work in, one to finish in and one to shelve in.</summary>
    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var other = new EfHatchProject { Key = "OPS", Name = "Operations", CreatedAt = Now };
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        var doing = new EfHatchStatus { Name = "doing", SortOrder = 30 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 40, IsTerminal = true };
        var shelved = new EfHatchStatus { Name = "shelved", SortOrder = 50, IsDeferred = true };
        db.AddRange(project, other, todo, doing, done, shelved);
        await db.SaveChangesAsync();

        return new Harness
        {
            Db = db,
            Ambitions = new AmbitionsController(db, new StubActorDirectory(), new FakeTimeProvider(Now)),
            ProjectId = project.Id,
            Other = other.Id,
            Todo = todo.Id,
            Doing = doing.Id,
            Done = done.Id,
            Shelved = shelved.Id,
        };
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result?.GetType().Name ?? "nothing"}");
}

using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <c>GET /api/hatch/reports/changelog</c>: closed top-level epics, bucketed by
/// <see cref="ChangelogReport.Fold"/> and decorated with cards, wired against
/// the in-memory database.
/// </summary>
/// <remarks>
/// A read, never a write to the event trail - the harness sets
/// <c>ClosedAt</c>/<c>StatusId</c>/<c>ParentId</c> directly, as
/// <see cref="ChangelogControllerTests"/> does.
/// </remarks>
public class ReportsControllerChangelogTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 3, 0, 0, TimeSpan.Zero);

    // ---- Refusals ----

    [Fact]
    public async Task AnUnparseableBound_IsRefused()
    {
        var h = await NewAsync();

        var result = await h.Controller.GetChangelogReport(
            "not a date", null, null, null, ct: default);

        Assert.Equal(RangeBounds.NotAnInstant("not a date"), Refusal(result));
    }

    [Fact]
    public async Task AnInvertedRange_IsRefused()
    {
        var h = await NewAsync();

        var result = await h.Controller.GetChangelogReport(
            Now.ToString("o"), Now.AddDays(-1).ToString("o"), null, null, ct: default);

        Assert.Equal("a range ends before it begins", Refusal(result));
    }

    [Fact]
    public async Task ABadBucketName_IsRefused()
    {
        var h = await NewAsync();

        var result = await h.Controller.GetChangelogReport(
            null, null, null, "fortnight", ct: default);

        Assert.Equal("a bucket is hour, day, week or month - not \"fortnight\"", Refusal(result));
    }

    [Fact]
    public async Task AnOffsetPastTheLimit_IsRefused()
    {
        var h = await NewAsync();

        var result = await h.Controller.GetChangelogReport(
            null, null, null, null, offsetMinutes: 1441, ct: default);

        Assert.Equal("an offset from UTC is minutes between -1440 and 1440 - not 1441", Refusal(result));
    }

    [Fact]
    public async Task AnUnknownProjectId_IsRefused()
    {
        var h = await NewAsync();

        var result = await h.Controller.GetChangelogReport(
            null, null, [999_999], null, ct: default);

        Assert.Equal("there is no project 999999", Refusal(result));
    }

    [Fact]
    public async Task ALevelOtherThanEpicOrStory_IsRefused()
    {
        var h = await NewAsync();

        var result = await h.Controller.GetChangelogReport(
            null, null, null, null, level: "task", ct: default);

        Assert.Equal("a level is epic or story - not \"task\"", Refusal(result));
    }

    // ---- The answer ----

    [Fact]
    public async Task TwoProjectIds_NarrowTheAnswerToTheirUnion()
    {
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        var third = await h.AddProjectAsync("THD");

        await h.CloseEpicAsync("in AER", Now - TimeSpan.FromMinutes(1));
        await h.CloseEpicAsync("in OTH", Now - TimeSpan.FromMinutes(1), projectId: other);
        await h.CloseEpicAsync("in THD", Now - TimeSpan.FromMinutes(1), projectId: third);

        var result = Value(await h.Controller.GetChangelogReport(
            null, null, [h.ProjectId, other], null, ct: default));

        var keys = result.Buckets.SelectMany(b => b.Groups).Select(g => g.Epic.Key).ToList();
        Assert.Equal(["AER-1", "OTH-2"], keys.Order());
    }

    [Fact]
    public async Task NoProjectId_AnswersEveryProjectsClosedEpics()
    {
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        await h.CloseEpicAsync("in AER", Now - TimeSpan.FromMinutes(1));
        await h.CloseEpicAsync("in OTH", Now - TimeSpan.FromMinutes(1), projectId: other);

        var result = Value(await h.Controller.GetChangelogReport(null, null, null, null, ct: default));

        var keys = result.Buckets.SelectMany(b => b.Groups).Select(g => g.Epic.Key).ToList();
        Assert.Equal(["AER-1", "OTH-2"], keys.Order());
    }

    [Fact]
    public async Task LooseTrue_EchoesTrueAndLooseMaybe_Is400()
    {
        var h = await NewAsync();
        await h.CloseEpicAsync("an epic", Now - TimeSpan.FromMinutes(1));

        var trueResult = Value(await h.Controller.GetChangelogReport(null, null, null, null, loose: "true", ct: default));
        Assert.True(trueResult.Loose);

        var maybeResult = await h.Controller.GetChangelogReport(null, null, null, null, loose: "maybe", ct: default);
        Assert.Equal("loose is true or false - not \"maybe\"", Refusal(maybeResult));
    }

    [Fact]
    public async Task ABugUnderAnEpic_LandsInLoose_AndDescendantCountStillCountsIt()
    {
        var h = await NewAsync();
        var epic = await h.CloseEpicAsync("the epic", Now - TimeSpan.FromMinutes(1));
        var bug = await h.CloseAsync("bug", "a child bug", Now - TimeSpan.FromMinutes(1), parentId: epic.Id);

        var result = Value(await h.Controller.GetChangelogReport(null, null, null, null, loose: "true", ct: default));

        var bucket = Assert.Single(result.Buckets);
        var looseEntry = Assert.Single(bucket.Loose);
        Assert.Equal(IssueKey.Format("AER", bug.Number), looseEntry.Key);
        var group = Assert.Single(bucket.Groups);
        Assert.Equal(1, group.DescendantCount);
    }

    [Fact]
    public async Task AParentlessStoryClosedInTheWindow_LandsInLoose()
    {
        var h = await NewAsync();
        var story = await h.CloseAsync("story", "a parentless story", Now - TimeSpan.FromMinutes(1));

        var result = Value(await h.Controller.GetChangelogReport(null, null, null, null, loose: "true", ct: default));

        var bucket = Assert.Single(result.Buckets);
        var looseEntry = Assert.Single(bucket.Loose);
        Assert.Equal(IssueKey.Format("AER", story.Number), looseEntry.Key);
    }

    [Fact]
    public async Task AStoryUnderAnEpic_DoesNotLandInLoose()
    {
        var h = await NewAsync();
        var epic = await h.CloseEpicAsync("the epic", Now - TimeSpan.FromMinutes(1));
        await h.CloseAsync("story", "a story under the epic", Now - TimeSpan.FromMinutes(1), parentId: epic.Id);

        var result = Value(await h.Controller.GetChangelogReport(null, null, null, null, loose: "true", ct: default));

        var bucket = Assert.Single(result.Buckets);
        Assert.Empty(bucket.Loose);
    }

    [Fact]
    public async Task AParentlessTask_DoesNotLandInLoose()
    {
        var h = await NewAsync();
        await h.CloseAsync("task", "a parentless task", Now - TimeSpan.FromMinutes(1));

        var result = Value(await h.Controller.GetChangelogReport(null, null, null, null, loose: "true", ct: default));

        Assert.Empty(result.Buckets);
    }

    [Fact]
    public async Task AClosedReflection_DoesNotLandInLoose()
    {
        var h = await NewAsync();
        await h.CloseAsync("reflection", "a closed reflection", Now - TimeSpan.FromMinutes(1));

        var result = Value(await h.Controller.GetChangelogReport(null, null, null, null, loose: "true", ct: default));

        Assert.Empty(result.Buckets);
    }

    [Fact]
    public async Task PriorityRidesTheLooseEntry()
    {
        var h = await NewAsync();
        var emergency = await h.CloseAsync("bug", "an emergency bug", Now - TimeSpan.FromMinutes(5));
        emergency.Priority = PriorityLevels.Emergency;
        var normal = await h.CloseAsync("bug", "a normal bug", Now - TimeSpan.FromMinutes(1));
        await h.Db.SaveChangesAsync();

        var result = Value(await h.Controller.GetChangelogReport(null, null, null, null, loose: "true", ct: default));

        var bucket = Assert.Single(result.Buckets);
        var entries = bucket.Loose.ToDictionary(e => e.Key);
        Assert.Equal("emergency", entries[IssueKey.Format("AER", emergency.Number)].Priority);
        Assert.Equal("normal", entries[IssueKey.Format("AER", normal.Number)].Priority);
    }

    [Fact]
    public async Task WithLooseFalseOrAbsent_ABucketHoldingOnlyABug_IsAbsent()
    {
        var h = await NewAsync();
        await h.CloseAsync("bug", "a parentless bug", Now - TimeSpan.FromMinutes(1));

        var absent = Value(await h.Controller.GetChangelogReport(null, null, null, null, ct: default));
        Assert.Empty(absent.Buckets);

        var falseResult = Value(await h.Controller.GetChangelogReport(null, null, null, null, loose: "false", ct: default));
        Assert.Empty(falseResult.Buckets);
    }

    [Fact]
    public async Task WithLooseTrue_ThatBucket_HasEmptyGroupsAndNonemptyLoose()
    {
        var h = await NewAsync();
        await h.CloseAsync("bug", "a parentless bug", Now - TimeSpan.FromMinutes(1));

        var result = Value(await h.Controller.GetChangelogReport(null, null, null, null, loose: "true", ct: default));

        var bucket = Assert.Single(result.Buckets);
        Assert.Empty(bucket.Groups);
        Assert.Single(bucket.Loose);
    }

    [Fact]
    public async Task AClosedEpicWithOneChildTask_AppearsClosedHereWithNoStoriesAndADescendantCountOfOne()
    {
        var h = await NewAsync();
        var epic = await h.CloseEpicAsync("the epic", Now - TimeSpan.FromMinutes(1));
        await h.CloseAsync("task", "a child task", Now - TimeSpan.FromMinutes(1), parentId: epic.Id);

        var result = Value(await h.Controller.GetChangelogReport(null, null, null, null, ct: default));

        var bucket = Assert.Single(result.Buckets);
        var group = Assert.Single(bucket.Groups);
        Assert.Equal("AER-1", group.Epic.Key);
        Assert.True(group.ClosedHere);
        Assert.Empty(group.Stories);
        Assert.Equal(1, group.DescendantCount);
    }

    [Fact]
    public async Task AChildIsNeverItsOwnGroup_WhateverItsType_IncludingAChildEpic()
    {
        var h = await NewAsync();
        var epic = await h.CloseEpicAsync("the epic", Now - TimeSpan.FromMinutes(1));
        await h.CloseEpicAsync("a child epic", Now - TimeSpan.FromMinutes(1), parentId: epic.Id);
        await h.CloseAsync("story", "a child story", Now - TimeSpan.FromMinutes(1), parentId: epic.Id);
        await h.CloseAsync("bug", "a child bug", Now - TimeSpan.FromMinutes(1), parentId: epic.Id);
        await h.CloseAsync("task", "a child task", Now - TimeSpan.FromMinutes(1), parentId: epic.Id);

        var result = Value(await h.Controller.GetChangelogReport(null, null, null, null, ct: default));

        var bucket = Assert.Single(result.Buckets);
        var group = Assert.Single(bucket.Groups);
        Assert.Equal("AER-1", group.Epic.Key);
    }

    [Fact]
    public async Task TwoEpicsClosedInTheSameBucket_ComeBackOrderedByClosedAtDescending()
    {
        var h = await NewAsync();
        var earlier = await h.CloseEpicAsync("earlier", Now - TimeSpan.FromMinutes(5));
        var later = await h.CloseEpicAsync("later", Now - TimeSpan.FromMinutes(1));

        var result = Value(await h.Controller.GetChangelogReport(null, null, null, null, ct: default));

        var bucket = Assert.Single(result.Buckets);
        Assert.Equal(
            [IssueKey.Format("AER", later.Number), IssueKey.Format("AER", earlier.Number)],
            bucket.Groups.Select(g => g.Epic.Key));
    }

    // ---- level=story ----

    [Fact]
    public async Task TwoStoriesUnderOneEpic_ComeBackAsOneGroupWithClosedHereFalse()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic", h.InProgress);
        await h.CloseAsync("story", "first story", Now - TimeSpan.FromMinutes(5), parentId: epic.Id);
        await h.CloseAsync("story", "second story", Now - TimeSpan.FromMinutes(1), parentId: epic.Id);

        var result = Value(await h.Controller.GetChangelogReport(
            null, null, null, null, level: "story", ct: default));

        var bucket = Assert.Single(result.Buckets);
        var group = Assert.Single(bucket.Groups);
        Assert.Equal("AER-1", group.Epic.Key);
        Assert.False(group.ClosedHere);
        Assert.Empty(group.Stories);
    }

    [Fact]
    public async Task AStoryUnderASubEpic_GroupsUnderTheSubEpicNotTheRoot()
    {
        var h = await NewAsync();
        var root = await h.FileAsync("epic", "the root epic", h.InProgress);
        var sub = await h.FileAsync("epic", "the sub epic", h.InProgress, parentId: root.Id);
        await h.CloseAsync("story", "a story", Now - TimeSpan.FromMinutes(1), parentId: sub.Id);

        var result = Value(await h.Controller.GetChangelogReport(
            null, null, null, null, level: "story", ct: default));

        var bucket = Assert.Single(result.Buckets);
        var group = Assert.Single(bucket.Groups);
        Assert.Equal(IssueKey.Format("AER", sub.Number), group.Epic.Key);
    }

    [Fact]
    public async Task AnEpicClosingWithNoStoryClosingUnderIt_StillMakesAGroup()
    {
        var h = await NewAsync();
        var epic = await h.CloseEpicAsync("the epic", Now - TimeSpan.FromMinutes(1));

        var result = Value(await h.Controller.GetChangelogReport(
            null, null, null, null, level: "story", ct: default));

        var bucket = Assert.Single(result.Buckets);
        var group = Assert.Single(bucket.Groups);
        Assert.Equal("AER-1", group.Epic.Key);
        Assert.True(group.ClosedHere);
        Assert.Empty(group.Stories);
    }

    [Fact]
    public async Task AClosedBugUnderTheEpic_NeverMakesItsOwnGroup()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic", h.InProgress);
        await h.CloseAsync("bug", "a closed bug", Now - TimeSpan.FromMinutes(1), parentId: epic.Id);

        var result = Value(await h.Controller.GetChangelogReport(
            null, null, null, null, level: "story", ct: default));

        Assert.Empty(result.Buckets);
    }

    [Fact]
    public async Task AClosedStoryUnderAReflection_MakesNoGroupAnywhere()
    {
        var h = await NewAsync();
        var reflection = await h.FileAsync("reflection", "the reflection", h.InProgress);
        await h.CloseAsync("story", "a closed story", Now - TimeSpan.FromMinutes(1), parentId: reflection.Id);

        var result = Value(await h.Controller.GetChangelogReport(
            null, null, null, null, level: "story", ct: default));

        Assert.Empty(result.Buckets);
    }

    [Fact]
    public async Task LevelStory_EchoesStoryAndAnswers200WithNothingClosed()
    {
        var h = await NewAsync();

        var result = Value(await h.Controller.GetChangelogReport(
            null, null, null, null, level: "story", ct: default));

        Assert.Equal("story", result.Level);
        Assert.Empty(result.Buckets);
    }

    // ---- Who may read it ----

    [Fact]
    public void TheRoute_AcceptsAKey()
    {
        var guard = typeof(ReportsController)
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .Single();

        Assert.True(guard.AcceptKey);
    }

    private static T Value<T>(Microsoft.AspNetCore.Mvc.ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result?.GetType().Name ?? "nothing"}");

    private static string Refusal<T>(Microsoft.AspNetCore.Mvc.ActionResult<T> result) =>
        Assert.IsType<string>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 10 };
        var inProgress = new EfHatchStatus { Name = "in progress", SortOrder = 20 };
        var deferred = new EfHatchStatus { Name = "parked", SortOrder = 25, IsDeferred = true };
        var shipped = new EfHatchStatus { Name = "shipped", SortOrder = 30, IsTerminal = true };
        db.AddRange(project, todo, inProgress, deferred, shipped);
        await db.SaveChangesAsync();

        return new Harness
        {
            Db = db,
            Controller = new ReportsController(
                db, new StubActorDirectory(), TestClaims.With(), new FakeTimeProvider(Now)),
            ProjectId = project.Id,
            InProgress = inProgress.Id,
            Deferred = deferred.Id,
            Shipped = shipped.Id,
        };
    }

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required ReportsController Controller { get; init; }
        public required int ProjectId { get; init; }
        public required int InProgress { get; init; }
        public required int Deferred { get; init; }
        public required int Shipped { get; init; }

        private int next = 1;

        public async Task<int> AddProjectAsync(string key)
        {
            var project = new EfHatchProject { Key = key, Name = key, CreatedAt = Now };
            Db.Projects.Add(project);
            await Db.SaveChangesAsync();
            return project.Id;
        }

        public async Task<EfHatchIssue> FileAsync(
            string type, string title, int statusId, long? parentId = null, int? projectId = null)
        {
            var issue = new EfHatchIssue
            {
                ProjectId = projectId ?? ProjectId,
                Number = next++,
                Type = type,
                Title = title,
                Description = "",
                StatusId = statusId,
                Rank = 1024,
                ParentId = parentId,
                CreatedBy = "operator",
                // Well before Now, so the default fourteen-day window is never
                // clamped forward onto Now by RangeBounds.ClampStart - closes
                // a minute or two before Now must stay inside the range.
                CreatedAt = Now - TimeSpan.FromDays(30),
                UpdatedAt = Now,
            };

            Db.Issues.Add(issue);
            await Db.SaveChangesAsync();
            return issue;
        }

        public async Task<EfHatchIssue> CloseAsync(
            string type, string title, DateTimeOffset closedAt, long? parentId = null, int? projectId = null)
        {
            var issue = await FileAsync(type, title, Shipped, parentId, projectId);
            issue.ClosedAt = closedAt;
            await Db.SaveChangesAsync();
            return issue;
        }

        public Task<EfHatchIssue> CloseEpicAsync(
            string title, DateTimeOffset closedAt, long? parentId = null, int? projectId = null) =>
            CloseAsync("epic", title, closedAt, parentId, projectId);
    }
}

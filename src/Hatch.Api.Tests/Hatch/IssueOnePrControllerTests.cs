using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The story's own say over whether its tasks share one branch and one pull
/// request, and the value every reader derives from it: a story with no
/// override follows its project, an override wins, a task follows its story,
/// and flipping any of it takes effect on the next read with no write to the
/// issues beneath.
/// </summary>
public class IssueOnePrControllerTests
{
    // ---- Setting it ----

    [Fact]
    public async Task AStory_IsOffUntilSomebodySaysSo()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "the story");

        Assert.False(story.OnePr);
        Assert.Null(story.OnePrOwn);
        Assert.Null(story.OnePrFrom);
    }

    [Fact]
    public async Task AStory_CanBeSetOn()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "the story");

        var set = await h.SetAsync(story.Key, "on");

        Assert.True(set.OnePr);
        Assert.True(set.OnePrOwn);
        Assert.True((await h.RowAsync(story.Key)).OnePullRequest);
    }

    [Fact]
    public async Task AStory_CanBeSetOff_AgainstAProjectThatIsOn()
    {
        var h = await NewAsync();
        await h.ProjectDefaultAsync(true);
        var story = await h.FileAsync("story", "the story");

        var set = await h.SetAsync(story.Key, "off");

        Assert.False(set.OnePr);
        Assert.False(set.OnePrOwn);
    }

    [Fact]
    public async Task Inherit_ClearsTheOverride()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "the story");
        await h.SetAsync(story.Key, "on");

        var cleared = await h.SetAsync(story.Key, "inherit");

        Assert.False(cleared.OnePr);
        Assert.Null(cleared.OnePrOwn);
        Assert.Null((await h.RowAsync(story.Key)).OnePullRequest);
    }

    [Fact]
    public async Task TheValueIsReadCaseInsensitively()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "the story");

        Assert.True((await h.SetAsync(story.Key, " ON ")).OnePr);
    }

    [Theory]
    [InlineData("")]
    [InlineData("maybe")]
    [InlineData("true")]
    public async Task AnythingElse_IsRefusedAndWritesNothing(string value)
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "the story");

        var refused = await h.OnePr.PutIssueOnePr(story.Key, new OnePrRequest(value), default);

        Assert.IsType<BadRequestObjectResult>(refused.Result);
        Assert.Null((await h.RowAsync(story.Key)).OnePullRequest);
        Assert.Empty(await h.EventsAsync(story.Key));
    }

    [Theory]
    [InlineData("epic")]
    [InlineData("task")]
    public async Task OnlyAStoryOrABugTakesIt(string type)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync(type, "not a story or a bug");

        var refused = await h.OnePr.PutIssueOnePr(issue.Key, new OnePrRequest("on"), default);

        var bad = Assert.IsType<BadRequestObjectResult>(refused.Result);
        Assert.Contains(type, bad.Value!.ToString());
        Assert.Contains("only a story or a bug carries an override", bad.Value!.ToString());
        Assert.Null((await h.RowAsync(issue.Key)).OnePullRequest);
    }

    [Fact]
    public async Task ABug_CanBeSetOn_AndTheTrailSaysSo()
    {
        var h = await NewAsync();
        var bug = await h.FileAsync("bug", "the bug");

        var set = await h.SetAsync(bug.Key, "on");

        Assert.True(set.OnePr);
        Assert.True(set.OnePrOwn);
        Assert.True((await h.RowAsync(bug.Key)).OnePullRequest);
        var written = Assert.Single(await h.EventsAsync(bug.Key));
        Assert.Equal(("inherit", "on"), Sides(written));
    }

    [Fact]
    public async Task ABug_IsOffUntilSomebodySaysSo_AndFollowsTheProjectLikeAStory()
    {
        var h = await NewAsync();
        var bug = await h.FileAsync("bug", "the bug");

        Assert.False(bug.OnePr);
        Assert.Null(bug.OnePrOwn);

        await h.ProjectDefaultAsync(true);

        var read = await h.ReadAsync(bug.Key);
        Assert.True(read.OnePr);
        Assert.Null(read.OnePrOwn);
        Assert.Null(read.OnePrFrom);
    }

    [Fact]
    public async Task ABug_CanBeBornWithIt()
    {
        var h = await NewAsync();

        var bug = await h.FileAsync("bug", "the bug", onePr: "on");

        Assert.True((await h.RowAsync(bug.Key)).OnePullRequest);
        Assert.True((await h.ReadAsync(bug.Key)).OnePrOwn);
    }

    [Fact]
    public async Task AnIssueThatIsNotThere_IsNotFound()
    {
        var h = await NewAsync();

        Assert.IsType<NotFoundResult>(
            (await h.OnePr.PutIssueOnePr("AER-404", new OnePrRequest("on"), default)).Result);
    }

    // ---- Born with it ----

    [Theory]
    [InlineData("on", true)]
    [InlineData("off", false)]
    [InlineData("inherit", null)]
    public async Task AStory_CanBeBornWithIt(string value, bool? expected)
    {
        var h = await NewAsync();

        var story = await h.FileAsync("story", "the story", onePr: value);

        Assert.Equal(expected, (await h.RowAsync(story.Key)).OnePullRequest);
        Assert.Equal(expected, (await h.ReadAsync(story.Key)).OnePrOwn);
        Assert.Empty(await h.EventsAsync(story.Key));
    }

    [Fact]
    public async Task BornWithIt_IsReadCaseInsensitivelyAndTrimmed()
    {
        var h = await NewAsync();

        var story = await h.FileAsync("story", "the story", onePr: " ON ");

        Assert.True((await h.RowAsync(story.Key)).OnePullRequest);
    }

    [Fact]
    public async Task BornWithoutIt_FollowsTheProject()
    {
        var h = await NewAsync();

        var story = await h.FileAsync("story", "the story");

        Assert.Null((await h.RowAsync(story.Key)).OnePullRequest);
        Assert.Null((await h.ReadAsync(story.Key)).OnePrOwn);
    }

    [Theory]
    [InlineData("")]
    [InlineData("maybe")]
    [InlineData("true")]
    public async Task BornWithAnythingElse_IsRefusedWithTheSentencePutUses(string value)
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "the story");
        var before = await h.Db.Issues.CountAsync();

        var refused = await h.Issues.CreateIssue(
            new IssueCreateRequest(h.ProjectId, "story", "another", null, null, null, null, value), default);
        var put = await h.OnePr.PutIssueOnePr(story.Key, new OnePrRequest(value), default);

        var bad = Assert.IsType<BadRequestObjectResult>(refused.Result);
        var putBad = Assert.IsType<BadRequestObjectResult>(put.Result);
        Assert.Equal(putBad.Value, bad.Value);
        Assert.Equal(before, await h.Db.Issues.CountAsync());
    }

    [Theory]
    [InlineData("epic", "on")]
    [InlineData("task", "on")]
    [InlineData("task", "inherit")]
    public async Task BornWithIt_OnlyAStoryOrABugTakesIt(string type, string value)
    {
        var h = await NewAsync();
        var before = await h.Db.Issues.CountAsync();

        var refused = await h.Issues.CreateIssue(
            new IssueCreateRequest(h.ProjectId, type, "not a story", null, null, null, null, value), default);

        var bad = Assert.IsType<BadRequestObjectResult>(refused.Result);
        Assert.Contains($"this is a {type}", bad.Value!.ToString());
        Assert.Equal(before, await h.Db.Issues.CountAsync());
    }

    [Fact]
    public async Task ABadValueOnATask_GetsTheValueSentence()
    {
        var h = await NewAsync();

        var refused = await h.Issues.CreateIssue(
            new IssueCreateRequest(h.ProjectId, "task", "not a story", null, null, null, null, "maybe"), default);

        var bad = Assert.IsType<BadRequestObjectResult>(refused.Result);
        Assert.Equal("\"maybe\" is not a one-PR setting - on, off or inherit", bad.Value);
    }

    // ---- Who may ----

    [Fact]
    public async Task AKey_MaySetIt_WhateverTheProjectDefaultIs()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "the story");
        h.Caller.Person = null;
        h.Caller.Key = new EfApiKey { Name = "a key", Hash = [1], Prefix = "hatch_ak_x", CreatedAt = Now };

        var on = await h.SetAsync(story.Key, "on");
        await h.ProjectDefaultAsync(true);
        var off = await h.SetAsync(story.Key, "off");

        Assert.True(on.OnePr);
        Assert.False(off.OnePr);
        var events = await h.EventsAsync(story.Key);
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal("a key", e.Actor));
    }

    // ---- Locked once work has started ----

    private static async Task<(IssueDto Story, IssueDto Task)> StoryWithTaskAsync(Harness h)
    {
        var story = await h.FileAsync("story", "the story");
        var task = await h.FileAsync("task", "the task", story.Key);
        return (story, task);
    }

    [Fact]
    public async Task WhileNothingHasStarted_TheSettingCanBeChangedFreely()
    {
        var h = await NewAsync(board: true);
        var (story, _) = await StoryWithTaskAsync(h);

        await h.SetAsync(story.Key, "on");
        await h.SetAsync(story.Key, "off");
        var last = await h.SetAsync(story.Key, "inherit");

        Assert.Null(last.OnePrOwn);
    }

    [Fact]
    public async Task OnceATaskIsInTheImplementationColumn_TheSettingIsLocked()
    {
        var h = await NewAsync(board: true);
        var (story, task) = await StoryWithTaskAsync(h);
        await h.SetAsync(story.Key, "on");
        await h.MoveAsync(task.Key, h.Doing);

        var refused = await h.OnePr.PutIssueOnePr(story.Key, new OnePrRequest("off"), default);

        var conflict = Assert.IsType<ConflictObjectResult>(refused.Result);
        Assert.Contains($"{task.Key} is in \"in progress\"", conflict.Value!.ToString());
        Assert.True((await h.RowAsync(story.Key)).OnePullRequest);
        Assert.Single(await h.EventsAsync(story.Key));
    }

    [Fact]
    public async Task OnceATaskUnderABugIsInTheImplementationColumn_TheBugsSettingIsLocked()
    {
        var h = await NewAsync(board: true);
        var bug = await h.FileAsync("bug", "the bug");
        var task = await h.FileAsync("task", "the task", bug.Key);
        await h.SetAsync(bug.Key, "on");
        await h.MoveAsync(task.Key, h.Doing);

        var refused = await h.OnePr.PutIssueOnePr(bug.Key, new OnePrRequest("off"), default);

        var conflict = Assert.IsType<ConflictObjectResult>(refused.Result);
        Assert.Contains($"{task.Key} is in \"in progress\"", conflict.Value!.ToString());
        Assert.True((await h.RowAsync(bug.Key)).OnePullRequest);
    }

    [Fact]
    public async Task ATaskThatHasMovedPastTheImplementationColumn_LocksItToo()
    {
        var h = await NewAsync(board: true);
        var (story, task) = await StoryWithTaskAsync(h);
        await h.MoveAsync(task.Key, (await h.Db.Statuses.FirstAsync(s => s.Name == "review")).Id);

        var refused = await h.OnePr.PutIssueOnePr(story.Key, new OnePrRequest("on"), default);

        Assert.IsType<ConflictObjectResult>(refused.Result);
    }

    [Fact]
    public async Task ADoneTaskCountsAsStarted()
    {
        var h = await NewAsync(board: true);
        var (story, task) = await StoryWithTaskAsync(h);
        await h.MoveAsync(task.Key, (await h.Db.Statuses.FirstAsync(s => s.IsTerminal)).Id);

        Assert.IsType<ConflictObjectResult>((await h.OnePr.PutIssueOnePr(story.Key, new OnePrRequest("on"), default)).Result);
    }

    [Fact]
    public async Task ATaskStillToDoOrShelved_DoesNotLock()
    {
        var h = await NewAsync(board: true);
        var (story, task) = await StoryWithTaskAsync(h);
        await h.MoveAsync(task.Key, (await h.Db.Statuses.FirstAsync(s => s.Name == "todo")).Id);
        var shelved = await h.FileAsync("task", "set aside", story.Key);
        await h.MoveAsync(shelved.Key, h.Shelved);

        Assert.True((await h.SetAsync(story.Key, "on")).OnePr);
    }

    [Fact]
    public async Task ADescendantBelowATaskLocksItsStory()
    {
        var h = await NewAsync(board: true);
        var story = await h.FileAsync("story", "the story");
        var bug = await h.FileAsync("bug", "under the story", story.Key);
        var deep = await h.FileAsync("task", "under the bug", bug.Key);
        await h.MoveAsync(deep.Key, h.Doing);

        Assert.IsType<ConflictObjectResult>((await h.OnePr.PutIssueOnePr(story.Key, new OnePrRequest("on"), default)).Result);
    }

    [Fact]
    public async Task SettingTheValueItAlreadyHolds_IsNeverRefused_EvenOnceWorkHasStarted()
    {
        var h = await NewAsync(board: true);
        var (story, task) = await StoryWithTaskAsync(h);
        await h.SetAsync(story.Key, "on");
        await h.MoveAsync(task.Key, h.Doing);

        var again = await h.SetAsync(story.Key, "on");

        Assert.True(again.OnePrOwn);
    }

    [Fact]
    public async Task OnABoardWithNoImplementationColumn_NothingHasStarted()
    {
        var h = await NewAsync();
        var (story, task) = await StoryWithTaskAsync(h);
        await h.MoveAsync(task.Key, (await h.Db.Statuses.FirstAsync(s => s.IsTerminal)).Id);

        Assert.True((await h.SetAsync(story.Key, "on")).OnePr);
    }

    // ---- Enforced ----

    private static string Sentence(IActionResult? result) =>
        Assert.IsType<ConflictObjectResult>(result).Value!.ToString()!;

    [Theory]
    [InlineData("story", "on")]
    [InlineData("story", "off")]
    [InlineData("bug", "on")]
    [InlineData("bug", "off")]
    public async Task UnderAnEnforcedProject_OnAndOffAreRefused_AndNothingIsWritten(string type, string value)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync(type, "the issue");
        await h.EnforceAsync();
        var was = (await h.RowAsync(issue.Key)).UpdatedAt;
        h.Time.Advance(TimeSpan.FromHours(1));

        var refused = await h.OnePr.PutIssueOnePr(issue.Key, new OnePrRequest(value), default);

        var message = Sentence(refused.Result);
        Assert.Contains("AER enforces its one-PR default", message);
        Assert.Contains(issue.Key, message);
        var row = await h.RowAsync(issue.Key);
        Assert.Null(row.OnePullRequest);
        Assert.Equal(was, row.UpdatedAt);
        Assert.Empty(await h.EventsAsync(issue.Key));
    }

    [Fact]
    public async Task UnderAnEnforcedProject_InheritIsAccepted_AndClearsAnUnstartedOverride()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "the story");
        await h.SetAsync(story.Key, "on");
        await h.EnforceAsync();

        var cleared = await h.SetAsync(story.Key, "inherit");

        Assert.Null(cleared.OnePrOwn);
        Assert.Null((await h.RowAsync(story.Key)).OnePullRequest);
    }

    [Fact]
    public async Task UnderAnEnforcedProject_AStartedStoryHearsThePolicy_NotTheLock_ForOnAndOff()
    {
        var h = await NewAsync(board: true);
        var (story, task) = await StoryWithTaskAsync(h);
        await h.SetAsync(story.Key, "on");
        await h.MoveAsync(task.Key, h.Doing);
        await h.EnforceAsync();

        var refused = await h.OnePr.PutIssueOnePr(story.Key, new OnePrRequest("off"), default);

        var message = Sentence(refused.Result);
        Assert.Contains("AER enforces its one-PR default", message);
        Assert.DoesNotContain("already started", message);
    }

    [Fact]
    public async Task UnderAnEnforcedProject_InheritOnAStartedOverriddenStory_GetsTheLock()
    {
        var h = await NewAsync(board: true);
        var (story, task) = await StoryWithTaskAsync(h);
        await h.SetAsync(story.Key, "on");
        await h.MoveAsync(task.Key, h.Doing);
        await h.EnforceAsync();

        var refused = await h.OnePr.PutIssueOnePr(story.Key, new OnePrRequest("inherit"), default);

        Assert.Contains("has work under it that has already started", Sentence(refused.Result));
    }

    [Fact]
    public async Task UnderAnEnforcedProject_TheValueAlreadyHeld_IsNotRefused_AndWritesNothing()
    {
        var h = await NewAsync(board: true);
        var (story, task) = await StoryWithTaskAsync(h);
        await h.MoveAsync(task.Key, h.Doing);
        IssueKey.TryParse(story.Key, out var projectKey, out var number);
        var row = await h.Db.Issues.WithKey(projectKey, number).FirstAsync();
        row.OnePullRequest = true;
        await h.Db.SaveChangesAsync();
        await h.EnforceAsync();
        var was = (await h.RowAsync(story.Key)).UpdatedAt;
        h.Time.Advance(TimeSpan.FromHours(1));

        var again = await h.SetAsync(story.Key, "on");

        Assert.True(again.OnePrOwn);
        Assert.Equal(was, (await h.RowAsync(story.Key)).UpdatedAt);
        Assert.Empty(await h.EventsAsync(story.Key));
    }

    [Theory]
    [InlineData("story")]
    [InlineData("bug")]
    public async Task UnderAnEnforcedProject_BornWithOn_IsRefused_AndNothingIsWritten(string type)
    {
        var h = await NewAsync();
        await h.EnforceAsync();
        var before = await h.Db.Issues.CountAsync();
        var next = (await h.Db.Projects.AsNoTracking().FirstAsync()).NextIssueNumber;

        var refused = await h.Issues.CreateIssue(
            new IssueCreateRequest(h.ProjectId, type, "the issue", null, null, null, null, "on"), default);

        var message = Sentence(refused.Result);
        Assert.Contains("AER enforces its one-PR default", message);
        Assert.Contains($"this {type}", message);
        Assert.Equal(before, await h.Db.Issues.CountAsync());
        Assert.Equal(next, (await h.Db.Projects.AsNoTracking().FirstAsync()).NextIssueNumber);
    }

    [Theory]
    [InlineData("inherit")]
    [InlineData(null)]
    public async Task UnderAnEnforcedProject_BornWithInheritOrNothing_IsAccepted(string? value)
    {
        var h = await NewAsync();
        await h.EnforceAsync();

        var story = await h.FileAsync("story", "the story", onePr: value);

        Assert.Null((await h.RowAsync(story.Key)).OnePullRequest);
    }

    [Fact]
    public async Task UnderAnEnforcedProject_BornWithOnAsATask_StillGetsTheBadRequest()
    {
        var h = await NewAsync();
        await h.EnforceAsync();

        var refused = await h.Issues.CreateIssue(
            new IssueCreateRequest(h.ProjectId, "task", "not a story", null, null, null, null, "on"), default);

        Assert.IsType<BadRequestObjectResult>(refused.Result);
    }

    [Fact]
    public async Task UnderAnEnforcedProject_AKeyGetsTheSameRefusal()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "the story");
        await h.EnforceAsync();
        h.Caller.Person = null;
        h.Caller.Key = new EfApiKey { Name = "a key", Hash = [1], Prefix = "hatch_ak_x", CreatedAt = Now };

        var refused = await h.OnePr.PutIssueOnePr(story.Key, new OnePrRequest("on"), default);

        Assert.Contains("AER enforces its one-PR default", Sentence(refused.Result));
    }

    // ---- Moving into an enforced project ----

    private static IssuePatchRequest MoveTo(int projectId, bool? moveDescendants = null) =>
        new(null, null, null, null, null, null, null, null, projectId, moveDescendants);

    [Theory]
    [InlineData("story")]
    [InlineData("bug")]
    public async Task MovingIntoAnEnforcedProject_ReturnsAnUnstartedOverrideToInherit_AndSaysWhy(string type)
    {
        var h = await NewAsync(board: true);
        var issue = await h.FileAsync(type, "the issue", onePr: "on");
        var id = (await h.RowAsync(issue.Key)).Id;
        var ops = await h.ProjectAsync(OnePullRequestPolicies.Enforced);

        Value(await h.Issues.PatchIssue(issue.Key, MoveTo(ops), default));

        Assert.Null((await h.RowByIdAsync(id)).OnePullRequest);
        var events = await h.OnePrEventsByIdAsync(id);
        var e = Assert.Single(events);
        Assert.Equal("on", e.GetProperty("from").GetString());
        Assert.Equal("inherit", e.GetProperty("to").GetString());
        Assert.Contains("moved into a project", e.GetProperty("fixedBecause").GetString());
    }

    [Fact]
    public async Task MovingIntoAnEnforcedProject_AStartedStoryKeepsItsOverride_AndWritesNothing()
    {
        var h = await NewAsync(board: true);
        var (story, task) = await StoryWithTaskAsync(h);
        await h.SetAsync(story.Key, "on");
        await h.MoveAsync(task.Key, h.Doing);
        var id = (await h.RowAsync(story.Key)).Id;
        var ops = await h.ProjectAsync(OnePullRequestPolicies.Enforced);

        Value(await h.Issues.PatchIssue(story.Key, MoveTo(ops), default));

        Assert.True((await h.RowByIdAsync(id)).OnePullRequest);
        Assert.Single(await h.OnePrEventsByIdAsync(id)); // the "on" it was given, and nothing from the move
    }

    [Fact]
    public async Task MovingIntoAnOptionalProject_LeavesTheOverrideAlone_AndWritesNothing()
    {
        var h = await NewAsync(board: true);
        var story = await h.FileAsync("story", "the story", onePr: "on");
        var id = (await h.RowAsync(story.Key)).Id;
        var ops = await h.ProjectAsync(OnePullRequestPolicies.Optional);

        Value(await h.Issues.PatchIssue(story.Key, MoveTo(ops), default));

        Assert.True((await h.RowByIdAsync(id)).OnePullRequest);
        Assert.Empty(await h.OnePrEventsByIdAsync(id));
    }

    [Fact]
    public async Task MovingOutOfAnEnforcedProject_LeavesTheOverrideAlone_AndWritesNothing()
    {
        var h = await NewAsync(board: true);
        var story = await h.FileAsync("story", "the story", onePr: "on");
        var id = (await h.RowAsync(story.Key)).Id;
        await h.EnforceAsync();
        var ops = await h.ProjectAsync(OnePullRequestPolicies.Optional);

        Value(await h.Issues.PatchIssue(story.Key, MoveTo(ops), default));

        Assert.True((await h.RowByIdAsync(id)).OnePullRequest);
        Assert.Empty(await h.OnePrEventsByIdAsync(id));
    }

    [Fact]
    public async Task MovingAStoryWithItsUnstartedTask_ReconcilesTheStoryOnce()
    {
        var h = await NewAsync(board: true);
        var (story, task) = await StoryWithTaskAsync(h);
        await h.SetAsync(story.Key, "on");
        var id = (await h.RowAsync(story.Key)).Id;
        var ops = await h.ProjectAsync(OnePullRequestPolicies.Enforced);

        Value(await h.Issues.PatchIssue(story.Key, MoveTo(ops), default));

        Assert.Null((await h.RowByIdAsync(id)).OnePullRequest);
        Assert.Equal(2, (await h.OnePrEventsByIdAsync(id)).Count); // the "on", then the return to inherit
    }

    [Fact]
    public async Task MovingAnOverriddenStoryAndItsOverriddenChildStory_ReconcilesBoth()
    {
        var h = await NewAsync(board: true);
        var epic = await h.FileAsync("epic", "the epic");
        var story = await h.FileAsync("story", "the story", epic.Key, onePr: "on");
        var bug = await h.FileAsync("bug", "the bug", story.Key, onePr: "off");
        var ids = new[] { (await h.RowAsync(story.Key)).Id, (await h.RowAsync(bug.Key)).Id };
        var ops = await h.ProjectAsync(OnePullRequestPolicies.Enforced);

        Value(await h.Issues.PatchIssue(story.Key, MoveTo(ops), default));

        foreach (var id in ids)
        {
            Assert.Null((await h.RowByIdAsync(id)).OnePullRequest);
            Assert.Single(await h.OnePrEventsByIdAsync(id));
        }
    }

    [Fact]
    public async Task BulkMovingIntoAnEnforcedProject_GivesTheSameResultAsThePatch()
    {
        var h = await NewAsync(board: true);
        var story = await h.FileAsync("story", "the story", onePr: "on");
        var started = await h.FileAsync("story", "the started story", onePr: "off");
        var task = await h.FileAsync("task", "the task", started.Key);
        await h.MoveAsync(task.Key, h.Doing);
        var ids = (story: (await h.RowAsync(story.Key)).Id, started: (await h.RowAsync(started.Key)).Id);
        var ops = await h.ProjectAsync(OnePullRequestPolicies.Enforced);

        var result = Value(await h.Issues.BulkEdit(new IssueBulkEditRequest([story.Key, started.Key], ProjectId: ops), default));

        Assert.Empty(result.Failures);
        Assert.Null((await h.RowByIdAsync(ids.story)).OnePullRequest);
        var e = Assert.Single(await h.OnePrEventsByIdAsync(ids.story));
        Assert.Equal("on", e.GetProperty("from").GetString());
        Assert.Equal("inherit", e.GetProperty("to").GetString());
        Assert.False((await h.RowByIdAsync(ids.started)).OnePullRequest);
        Assert.Empty(await h.OnePrEventsByIdAsync(ids.started));
    }

    [Fact]
    public async Task MovingWithoutDescendants_TheStartedTaskStaysBehind_SoTheStoryCountsAsUnstartedAndIsReconciled()
    {
        var h = await NewAsync(board: true);
        var (story, task) = await StoryWithTaskAsync(h);
        await h.SetAsync(story.Key, "on");
        await h.MoveAsync(task.Key, h.Doing);
        var id = (await h.RowAsync(story.Key)).Id;
        var ops = await h.ProjectAsync(OnePullRequestPolicies.Enforced);

        Value(await h.Issues.PatchIssue(story.Key, MoveTo(ops, moveDescendants: false), default));

        Assert.Null((await h.RowByIdAsync(id)).OnePullRequest);
        Assert.Equal(2, (await h.OnePrEventsByIdAsync(id)).Count);
    }

    // ---- The trail ----

    [Fact]
    public async Task Setting_WritesWhoDidItAndWhichWay()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "the story");

        await h.SetAsync(story.Key, "on");
        await h.SetAsync(story.Key, "off");
        await h.SetAsync(story.Key, "inherit");

        var events = await h.EventsAsync(story.Key);
        Assert.Equal(3, events.Count);
        Assert.All(events, e => Assert.Equal("a person", e.Actor));
        Assert.Equal(("inherit", "on"), Sides(events[0]));
        Assert.Equal(("on", "off"), Sides(events[1]));
        Assert.Equal(("off", "inherit"), Sides(events[2]));
    }

    [Fact]
    public async Task SettingTheValueItAlreadyHolds_WritesNothingAndDoesNotMoveTheUpdatedTime()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "the story");
        await h.SetAsync(story.Key, "on");
        var was = (await h.ReadAsync(story.Key)).UpdatedAt;

        h.Time.Advance(TimeSpan.FromHours(1));
        await h.SetAsync(story.Key, "on");

        Assert.Single(await h.EventsAsync(story.Key));
        Assert.Equal(was, (await h.ReadAsync(story.Key)).UpdatedAt);
    }

    [Fact]
    public async Task InheritingWhatIsAlreadyInherited_WritesNothing()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "the story");

        await h.SetAsync(story.Key, "inherit");

        Assert.Empty(await h.EventsAsync(story.Key));
    }

    // ---- The effective value, live at read ----

    [Fact]
    public async Task AStoryWithNoOverride_FollowsItsProject_Live()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "the story");
        Assert.False((await h.ReadAsync(story.Key)).OnePr);

        await h.ProjectDefaultAsync(true);
        var on = await h.ReadAsync(story.Key);
        await h.ProjectDefaultAsync(false);
        var off = await h.ReadAsync(story.Key);

        Assert.True(on.OnePr);
        Assert.Null(on.OnePrOwn);
        Assert.Null(on.OnePrFrom);
        Assert.False(off.OnePr);
    }

    [Fact]
    public async Task AnOverride_WinsOverTheProject_BothWays()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "the story");

        await h.ProjectDefaultAsync(true);
        await h.SetAsync(story.Key, "off");
        Assert.False((await h.ReadAsync(story.Key)).OnePr);

        await h.ProjectDefaultAsync(false);
        await h.SetAsync(story.Key, "on");
        Assert.True((await h.ReadAsync(story.Key)).OnePr);
    }

    [Fact]
    public async Task ATask_FollowsItsStory_AndNamesIt()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "the story");
        var task = await h.FileAsync("task", "the task", story.Key);

        Assert.False((await h.ReadAsync(task.Key)).OnePr);

        await h.SetAsync(story.Key, "on");
        var on = await h.ReadAsync(task.Key);

        Assert.True(on.OnePr);
        Assert.Null(on.OnePrOwn);
        Assert.Equal(story.Key, on.OnePrFrom);

        await h.SetAsync(story.Key, "off");
        Assert.False((await h.ReadAsync(task.Key)).OnePr);
    }

    [Fact]
    public async Task ATask_FollowsTheProjectDefault_ThroughItsStory()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "the story");
        var task = await h.FileAsync("task", "the task", story.Key);

        await h.ProjectDefaultAsync(true);

        var read = await h.ReadAsync(task.Key);
        Assert.True(read.OnePr);
        Assert.Equal(story.Key, read.OnePrFrom);
    }

    [Fact]
    public async Task ATaskUnderABug_FollowsTheBug_AndNamesIt()
    {
        var h = await NewAsync();
        var bug = await h.FileAsync("bug", "the bug");
        var task = await h.FileAsync("task", "the task", bug.Key);

        Assert.False((await h.ReadAsync(task.Key)).OnePr);

        await h.SetAsync(bug.Key, "on");
        var on = await h.ReadAsync(task.Key);

        Assert.True(on.OnePr);
        Assert.Null(on.OnePrOwn);
        Assert.Equal(bug.Key, on.OnePrFrom);

        await h.SetAsync(bug.Key, "off");
        Assert.False((await h.ReadAsync(task.Key)).OnePr);
    }

    [Fact]
    public async Task ATaskUnderABugUnderAStory_FollowsTheBug_NotTheStory()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "the story");
        var bug = await h.FileAsync("bug", "the bug", story.Key);
        var task = await h.FileAsync("task", "the task", bug.Key);
        await h.SetAsync(story.Key, "on");

        var underBug = await h.ReadAsync(task.Key);
        Assert.False(underBug.OnePr);
        Assert.Equal(bug.Key, underBug.OnePrFrom);

        await h.SetAsync(bug.Key, "on");
        Assert.True((await h.ReadAsync(task.Key)).OnePr);
    }

    [Fact]
    public async Task ATaskWithNoStoryAbove_IsOff_EvenWhereTheProjectIsOn()
    {
        var h = await NewAsync();
        await h.ProjectDefaultAsync(true);
        var epic = await h.FileAsync("epic", "the epic");
        var underEpic = await h.FileAsync("task", "straight under the epic", epic.Key);
        var orphan = await h.FileAsync("task", "no parent");

        foreach (var key in new[] { epic.Key, underEpic.Key, orphan.Key })
        {
            var read = await h.ReadAsync(key);
            Assert.False(read.OnePr);
            Assert.Null(read.OnePrFrom);
        }
    }

    [Fact]
    public async Task Reparenting_ChangesWhatATaskReadsAtOnce()
    {
        var h = await NewAsync();
        var on = await h.FileAsync("story", "on");
        var off = await h.FileAsync("story", "off");
        await h.SetAsync(on.Key, "on");
        var task = await h.FileAsync("task", "the task", on.Key);
        Assert.True((await h.ReadAsync(task.Key)).OnePr);

        await h.Issues.PatchIssue(task.Key, new IssuePatchRequest(null, null, null, null, off.Key, null, null, null, null, null), default);

        var read = await h.ReadAsync(task.Key);
        Assert.False(read.OnePr);
        Assert.Equal(off.Key, read.OnePrFrom);
    }

    [Fact]
    public async Task Flipping_WritesNothingOntoTheTasksBeneath()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "the story");
        var task = await h.FileAsync("task", "the task", story.Key);

        await h.SetAsync(story.Key, "on");

        Assert.Null((await h.RowAsync(task.Key)).OnePullRequest);
        Assert.Empty(await h.EventsAsync(task.Key));
    }

    // ---- Every read that draws a card ----

    [Fact]
    public async Task TheBoardAndSearch_CarryTheSameValueAsTheIssue()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "the story");
        var task = await h.FileAsync("task", "the task", story.Key);
        await h.SetAsync(story.Key, "on");

        var board = Value(await h.Board.GetBoard(default)).Issues.ToDictionary(c => c.Key);
        var search = Value(await h.Issues.SearchIssues(null, null, null, null, null, null, default)).ToDictionary(c => c.Key);

        foreach (var cards in new[] { board, search })
        {
            Assert.True(cards[story.Key].OnePr);
            Assert.True(cards[story.Key].OnePrOwn);
            Assert.Null(cards[story.Key].OnePrFrom);
            Assert.True(cards[task.Key].OnePr);
            Assert.Null(cards[task.Key].OnePrOwn);
            Assert.Equal(story.Key, cards[task.Key].OnePrFrom);
        }
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required StubCallerIdentity Caller { get; init; }
        public required IssueOnePrController OnePr { get; init; }
        public required IssuesController Issues { get; init; }
        public required BoardController Board { get; init; }
        public required IssueThreadController Thread { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }
        public int Doing { get; init; }
        public int Shelved { get; init; }

        /// <summary>Straight onto the row - these tests are about what a story's setting does once work has begun, not about how it got there.</summary>
        public async Task MoveAsync(string key, int statusId)
        {
            IssueKey.TryParse(key, out var projectKey, out var number);
            var issue = await Db.Issues.WithKey(projectKey, number).FirstAsync();
            issue.StatusId = statusId;
            await Db.SaveChangesAsync();
        }

        public async Task<IssueDto> FileAsync(string type, string title, string? parentKey = null, string? onePr = null) =>
            Created(await Issues.CreateIssue(new IssueCreateRequest(ProjectId, type, title, null, parentKey, null, null, onePr), default));

        public async Task<IssueDto> SetAsync(string key, string value) =>
            Value(await OnePr.PutIssueOnePr(key, new OnePrRequest(value), default));

        public async Task<IssueDto> ReadAsync(string key) => Value(await Issues.GetIssue(key, default));

        public async Task<EfHatchIssue> RowAsync(string key)
        {
            IssueKey.TryParse(key, out var projectKey, out var number);
            return await Db.Issues.AsNoTracking().WithKey(projectKey, number).FirstAsync();
        }

        /// <summary>The flag's own events, oldest first.</summary>
        public async Task<IReadOnlyList<IssueEventDto>> EventsAsync(string key) =>
            Value(await Thread.GetEvents(key, default))
                .Where(e => e.Kind == EfHatchIssueEvent.OnePrChanged)
                .Reverse()
                .ToList();

        /// <summary>The project's policy set to enforced, written straight to the row - who may do that is ProjectsControllerTests' business.</summary>
        public async Task EnforceAsync()
        {
            var project = await Db.Projects.FirstAsync(p => p.Id == ProjectId);
            project.OnePullRequestPolicy = OnePullRequestPolicies.Enforced;
            await Db.SaveChangesAsync();
        }

        /// <summary>A second project, OPS, with the policy asked for, written straight to the row. Returns its id.</summary>
        public async Task<int> ProjectAsync(string policy)
        {
            var project = new EfHatchProject { Key = "OPS", Name = "Ops", CreatedAt = Now, OnePullRequestPolicy = policy };
            Db.Add(project);
            await Db.SaveChangesAsync();
            return project.Id;
        }

        /// <summary>By id, since a move changes the key a test would otherwise look the issue up by.</summary>
        public async Task<EfHatchIssue> RowByIdAsync(long id) =>
            await Db.Issues.AsNoTracking().FirstAsync(i => i.Id == id);

        /// <summary>The payloads of the issue's one_pr_changed events, oldest first.</summary>
        public async Task<List<JsonElement>> OnePrEventsByIdAsync(long id)
        {
            var issue = await Db.Issues.AsNoTracking().Include(i => i.Events).FirstAsync(i => i.Id == id);
            return issue.Events
                .Where(e => e.Kind == EfHatchIssueEvent.OnePrChanged)
                .OrderBy(e => e.Id)
                .Select(e => JsonDocument.Parse(e.Payload!).RootElement.Clone())
                .ToList();
        }

        /// <summary>The project's default, written straight to the row - who may write it is ProjectsControllerTests' business.</summary>
        public async Task ProjectDefaultAsync(bool on)
        {
            var project = await Db.Projects.FirstAsync(p => p.Id == ProjectId);
            project.OnePullRequest = on;
            await Db.SaveChangesAsync();
        }
    }

    private static async Task<Harness> NewAsync(bool board = false)
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Add(project);
        db.AddRange(
            new EfHatchStatus { Name = "inbox", SortOrder = 10 },
            new EfHatchStatus { Name = "done", SortOrder = 50, IsTerminal = true });

        // A board with somewhere for work to start: the lock below is measured
        // from the implementation column, and the two-column board has none.
        EfHatchStatus? doing = null, shelved = null;
        if (board)
        {
            doing = new EfHatchStatus { Name = "in progress", SortOrder = 30, IsImplementation = true };
            shelved = new EfHatchStatus { Name = "shelved", SortOrder = 35, IsDeferred = true };
            db.AddRange(new EfHatchStatus { Name = "todo", SortOrder = 20 }, doing, shelved, new EfHatchStatus { Name = "review", SortOrder = 40 });
        }

        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var caller = new StubCallerIdentity { Person = new EfPerson { Name = "a person", CreatedAt = Now, UpdatedAt = Now } };
        var actors = new StubActorDirectory();

        return new Harness
        {
            Db = db,
            Caller = caller,
            OnePr = new IssueOnePrController(db, actors, TestClaims.With(), caller, time),
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time, new ProjectAccess(db, actors)),
            Board = new BoardController(db, actors, TestClaims.With(), time, TestClaims.DoneHorizon()),
            Thread = new IssueThreadController(db, caller, time),
            Time = time,
            ProjectId = project.Id,
            Doing = doing?.Id ?? 0,
            Shelved = shelved?.Id ?? 0,
        };
    }

    /// <summary>Whoever the test says is holding the phone. Their name is the audit actor.</summary>
    private sealed class StubCallerIdentity : ICallerIdentity
    {
        public EfPerson? Person { get; set; }

        public EfApiKey? Key { get; set; }

        public Task<EfAuthGrant?> GrantAsync(CancellationToken ct) => Task.FromResult<EfAuthGrant?>(null);

        public Task<Guid?> PersonIdAsync(CancellationToken ct) => Task.FromResult(Person?.Id);

        public Task<EfPerson?> PersonAsync(CancellationToken ct) => Task.FromResult(Person);

        public Task<EfApiKey?> ApiKeyAsync(CancellationToken ct) => Task.FromResult(Key);

        public Actor? Local { get; set; }

        public Task<Actor?> LocalAsync(CancellationToken ct) => Task.FromResult(Local);

        public Task<bool> IsProgramAsync(CancellationToken ct) =>
            Task.FromResult(Key is not null || Local is { Kind: ActorKind.Key });

        public Task<string> ActorNameAsync(CancellationToken ct) =>
            Task.FromResult(Person?.Name ?? Key?.Name ?? Local?.Name ?? CallerIdentity.Unattributed);
    }

    private static (string From, string To) Sides(IssueEventDto e)
    {
        var payload = e.Payload!.Value;
        return (payload.GetProperty("from").GetString()!, payload.GetProperty("to").GetString()!);
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result}");

    private static T Created<T>(ActionResult<T> result) =>
        result.Result is CreatedAtActionResult created
            ? (T)created.Value!
            : result.Value ?? throw new InvalidOperationException($"expected a created value, got {result.Result}");
}

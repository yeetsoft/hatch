using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The gate itself, through the two doors that ask it: a move whose <c>from</c>
/// is outside the section and whose <c>to</c> is inside is refused while the
/// section is at or over its limit, unless the issue is already counted or a
/// person overrides it - see HA-89.
/// </summary>
public class WipMoveTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    // ---- The move ----

    [Fact]
    public async Task AMoveIntoAFullSection_Is409AndWritesNothing()
    {
        var h = await NewAsync();
        await h.StoryAsync(h.InProgress);
        await h.StoryAsync(h.InProgress);
        var moving = await h.StoryAsync(h.ToDo);
        var before = Value(await h.Issues.GetIssue(moving, default));
        var eventsBefore = (await h.EventsAsync(moving)).Count;

        var result = await h.Issues.MoveIssue(moving, new IssueMoveRequest(h.InProgress, null, null), default);

        var refusal = Assert.IsType<ConflictObjectResult>(result.Result);
        var dto = Assert.IsType<WipRefusalDto>(refusal.Value);
        Assert.Equal("the WIP section is full - 2 of 2 stories and bugs are in it", dto.Error);
        Assert.Equal(2, dto.Load);
        Assert.Equal(2, dto.Limit);

        var after = Value(await h.Issues.GetIssue(moving, default));
        Assert.Equal(h.ToDo, after.StatusId);
        Assert.Equal(before.Rank, after.Rank);
        Assert.Equal(eventsBefore, (await h.EventsAsync(moving)).Count);
    }

    // ---- The patch ----

    [Fact]
    public async Task APatchIntoAFullSection_IsTheSame409AndWritesNothing()
    {
        var h = await NewAsync();
        await h.StoryAsync(h.InProgress);
        await h.StoryAsync(h.InProgress);
        var moving = await h.StoryAsync(h.ToDo);
        var eventsBefore = (await h.EventsAsync(moving)).Count;

        var result = await h.Issues.PatchIssue(moving, Patch(statusId: h.InProgress), default);

        var refusal = Assert.IsType<ConflictObjectResult>(result.Result);
        var dto = Assert.IsType<WipRefusalDto>(refusal.Value);
        Assert.Equal("the WIP section is full - 2 of 2 stories and bugs are in it", dto.Error);
        Assert.Equal(2, dto.Load);
        Assert.Equal(2, dto.Limit);

        var after = Value(await h.Issues.GetIssue(moving, default));
        Assert.Equal(h.ToDo, after.StatusId);
        Assert.Equal(eventsBefore, (await h.EventsAsync(moving)).Count);
    }

    // ---- Bulk ----

    [Fact]
    public async Task ABulkMoveNamingTwoStoriesAndATask_MovesTheTaskAndFailsEachStory()
    {
        var h = await NewAsync();
        await h.StoryAsync(h.InProgress);
        await h.StoryAsync(h.InProgress);
        var storyOne = await h.StoryAsync(h.ToDo);
        var storyTwo = await h.StoryAsync(h.ToDo);
        var task = await h.IssueAsync("task", h.ToDo);

        var result = Value(await h.Issues.BulkEdit(Bulk([storyOne, storyTwo, task], statusId: h.InProgress), default));

        Assert.Equal([task], result.Changed);
        Assert.Equal(2, result.Failures.Count);
        foreach (var failure in result.Failures)
        {
            Assert.Contains(failure.Key, new[] { storyOne, storyTwo });
            Assert.Equal("the WIP section is full - 2 of 2 stories and bugs are in it", failure.Reason);
        }
    }

    [Fact]
    public async Task ABulkMoveWithOneSlotFree_MovesTheFirstStoryAndRefusesTheSecond()
    {
        var h = await NewAsync();
        await h.StoryAsync(h.InProgress);
        var storyOne = await h.StoryAsync(h.ToDo);
        var storyTwo = await h.StoryAsync(h.ToDo);

        var result = Value(await h.Issues.BulkEdit(Bulk([storyOne, storyTwo], statusId: h.InProgress), default));

        Assert.Equal([storyOne], result.Changed);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(storyTwo, failure.Key);
        Assert.Equal("the WIP section is full - 2 of 2 stories and bugs are in it", failure.Reason);
    }

    // ---- The override ----

    [Fact]
    public async Task APersonsOverride_LandsTheMoveAndWritesBothEvents()
    {
        var h = await NewAsync();
        await h.StoryAsync(h.InProgress);
        await h.StoryAsync(h.InProgress);
        var moving = await h.StoryAsync(h.ToDo);

        var result = await h.Issues.MoveIssue(
            moving, new IssueMoveRequest(h.InProgress, null, null, WipOverride: true), default);

        Assert.Equal(h.InProgress, Value(result).StatusId);

        var events = await h.EventsAsync(moving);
        var statusChanged = Assert.Single(events, e => e.Kind == EfHatchIssueEvent.StatusChanged);
        var overridden = Assert.Single(events, e => e.Kind == EfHatchIssueEvent.WipOverridden);
        Assert.Equal("Nathan", statusChanged.Actor);
        Assert.Equal("Nathan", overridden.Actor);

        var payload = overridden.Payload!.Value;
        Assert.Equal(2, payload.GetProperty("limit").GetInt32());
        Assert.Equal(3, payload.GetProperty("load").GetInt32());
        Assert.Equal("In Progress", payload.GetProperty("to").GetString());
    }

    [Fact]
    public async Task AKeysOverride_Is403AndWritesNothingWhateverTheLoad()
    {
        var h = await NewAsync();
        await h.StoryAsync(h.InProgress);
        await h.StoryAsync(h.InProgress);
        var full = await h.StoryAsync(h.ToDo);
        h.Caller.Key = AKey();

        var refusedFull = await h.Issues.MoveIssue(
            full, new IssueMoveRequest(h.InProgress, null, null, WipOverride: true), default);
        var objFull = Assert.IsType<ObjectResult>(refusedFull.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, objFull.StatusCode);
        Assert.Equal("overriding the WIP limit is a person's call, not an agent's", objFull.Value);
        Assert.Equal(h.ToDo, Value(await h.Issues.GetIssue(full, default)).StatusId);

        // And the same where the section has room.
        var roomy = await h.StoryAsync(h.ToDo);
        var refusedRoomy = await h.Issues.PatchIssue(roomy, Patch(statusId: h.InReview) with { WipOverride = true }, default);
        var objRoomy = Assert.IsType<ObjectResult>(refusedRoomy.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, objRoomy.StatusCode);
        Assert.Equal(h.ToDo, Value(await h.Issues.GetIssue(roomy, default)).StatusId);
    }

    [Fact]
    public async Task AKeylessRunnerWhereTheWallIsOff_IsTheSame403()
    {
        var h = await NewAsync();
        var moving = await h.StoryAsync(h.ToDo);
        h.Caller.Local = new Actor(ActorKind.Key, Guid.NewGuid(), "a-runner");

        var refused = await h.Issues.MoveIssue(
            moving, new IssueMoveRequest(h.InProgress, null, null, WipOverride: true), default);

        var obj = Assert.IsType<ObjectResult>(refused.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
    }

    [Fact]
    public async Task APersonsOverrideWithRoom_WritesOnlyStatusChanged()
    {
        var h = await NewAsync();
        var moving = await h.StoryAsync(h.ToDo);

        await h.Issues.MoveIssue(moving, new IssueMoveRequest(h.InProgress, null, null, WipOverride: true), default);

        var kinds = (await h.EventsAsync(moving)).Select(e => e.Kind).ToList();
        Assert.Contains(EfHatchIssueEvent.StatusChanged, kinds);
        Assert.DoesNotContain(EfHatchIssueEvent.WipOverridden, kinds);
    }

    // ---- Never gated ----

    [Fact]
    public async Task AMoveWithinTheSection_IsNeverGated()
    {
        var h = await NewAsync();
        await h.StoryAsync(h.InProgress);
        var moving = await h.StoryAsync(h.InProgress);

        var result = await h.Issues.MoveIssue(moving, new IssueMoveRequest(h.InReview, null, null), default);

        Assert.Equal(h.InReview, Value(result).StatusId);
    }

    [Fact]
    public async Task AMoveOutOfTheSection_IsNeverGated()
    {
        var h = await NewAsync();
        await h.StoryAsync(h.InProgress);
        var moving = await h.StoryAsync(h.InReview);

        var result = await h.Issues.MoveIssue(moving, new IssueMoveRequest(h.ToDo, null, null), default);

        Assert.Equal(h.ToDo, Value(result).StatusId);
    }

    [Fact]
    public async Task ATaskOrAnEpicMovingIn_IsNeverGated()
    {
        var h = await NewAsync();
        await h.StoryAsync(h.InProgress);
        await h.StoryAsync(h.InProgress);
        var task = await h.IssueAsync("task", h.ToDo);
        var epic = await h.IssueAsync("epic", h.ToDo);

        Assert.Equal(h.InProgress, Value(await h.Issues.MoveIssue(task, new IssueMoveRequest(h.InProgress, null, null), default)).StatusId);
        Assert.Equal(h.InProgress, Value(await h.Issues.MoveIssue(epic, new IssueMoveRequest(h.InProgress, null, null), default)).StatusId);
    }

    [Fact]
    public async Task AStoryWithALiveClaimMovingIn_IsNeverGated()
    {
        var h = await NewAsync();
        await h.StoryAsync(h.InProgress);
        await h.StoryAsync(h.InProgress);
        var claimed = await h.StoryAsync(h.ToDo);
        await h.ClaimAsync(claimed);

        var result = await h.Issues.MoveIssue(claimed, new IssueMoveRequest(h.InProgress, null, null), default);

        Assert.Equal(h.InProgress, Value(result).StatusId);
    }

    [Fact]
    public async Task AReorderWithinAColumn_IsNeverGated()
    {
        var h = await NewAsync();
        await h.StoryAsync(h.InProgress);
        var moving = await h.StoryAsync(h.InProgress);

        var result = await h.Issues.MoveIssue(moving, new IssueMoveRequest(h.InProgress, null, null, h.InProgress), default);

        Assert.Equal(h.InProgress, Value(result).StatusId);
        Assert.DoesNotContain(EfHatchIssueEvent.WipOverridden, (await h.EventsAsync(moving)).Select(e => e.Kind));
    }

    // ---- Room, and its edges ----

    [Fact]
    public async Task ARefusedMove_SucceedsOnceASlotFreesUp()
    {
        var h = await NewAsync();
        await h.StoryAsync(h.InProgress);
        var toFree = await h.StoryAsync(h.InProgress);
        var moving = await h.StoryAsync(h.ToDo);

        var refused = await h.Issues.MoveIssue(moving, new IssueMoveRequest(h.InProgress, null, null), default);
        Assert.IsType<ConflictObjectResult>(refused.Result);

        // Out of the section entirely - In Review counts too, so a move there
        // would not have freed anything.
        await h.Issues.MoveIssue(toFree, new IssueMoveRequest(h.Done, null, null), default);

        var result = await h.Issues.MoveIssue(moving, new IssueMoveRequest(h.InProgress, null, null), default);
        Assert.Equal(h.InProgress, Value(result).StatusId);
    }

    [Fact]
    public async Task WithNoLimitSet_EveryMoveBehavesAsBefore_FlagOrNot()
    {
        var h = await NewAsync(withLimit: false);
        var a = await h.StoryAsync(h.ToDo);
        var b = await h.StoryAsync(h.ToDo);

        var moved = await h.Issues.MoveIssue(a, new IssueMoveRequest(h.InProgress, null, null), default);
        Assert.Equal(h.InProgress, Value(moved).StatusId);

        var overridden = await h.Issues.MoveIssue(b, new IssueMoveRequest(h.InProgress, null, null, WipOverride: true), default);
        Assert.Equal(h.InProgress, Value(overridden).StatusId);

        foreach (var key in new[] { a, b })
            Assert.DoesNotContain(EfHatchIssueEvent.WipOverridden, (await h.EventsAsync(key)).Select(e => e.Kind));
    }

    [Fact]
    public async Task MovingAnOverriddenStoryBackOut_IsAnOrdinaryMove()
    {
        var h = await NewAsync();
        await h.StoryAsync(h.InProgress);
        await h.StoryAsync(h.InProgress);
        var overridden = await h.StoryAsync(h.ToDo);
        await h.Issues.MoveIssue(overridden, new IssueMoveRequest(h.InProgress, null, null, WipOverride: true), default);

        var result = await h.Issues.MoveIssue(overridden, new IssueMoveRequest(h.ToDo, null, null), default);

        Assert.Equal(h.ToDo, Value(result).StatusId);
        var kinds = (await h.EventsAsync(overridden)).Select(e => e.Kind).ToList();
        Assert.Equal(1, kinds.Count(k => k == EfHatchIssueEvent.WipOverridden));
        Assert.Equal(2, kinds.Count(k => k == EfHatchIssueEvent.StatusChanged));
    }

    // ---- The epic slice ----

    [Fact]
    public async Task AnEpicMoveIntoAFullEpicSlice_Is409AndWritesNothing()
    {
        var h = await NewAsync(withEpicLimit: 2);
        await h.EpicAsync(h.InProgress);
        await h.EpicAsync(h.InProgress);
        var moving = await h.EpicAsync(h.ToDo);
        var eventsBefore = (await h.EventsAsync(moving)).Count;

        var result = await h.Issues.MoveIssue(moving, new IssueMoveRequest(h.InProgress, null, null), default);

        var refusal = Assert.IsType<ConflictObjectResult>(result.Result);
        var dto = Assert.IsType<WipRefusalDto>(refusal.Value);
        Assert.Equal("the WIP section is full - 2 of 2 epics are in it", dto.Error);
        Assert.Equal(2, dto.Load);
        Assert.Equal(2, dto.Limit);

        var after = Value(await h.Issues.GetIssue(moving, default));
        Assert.Equal(h.ToDo, after.StatusId);
        Assert.Equal(eventsBefore, (await h.EventsAsync(moving)).Count);
    }

    [Fact]
    public async Task AStoryMoveIsNeverGatedByAFullEpicSlice()
    {
        var h = await NewAsync(withEpicLimit: 2);
        await h.EpicAsync(h.InProgress);
        await h.EpicAsync(h.InProgress);
        var moving = await h.StoryAsync(h.ToDo);

        var result = await h.Issues.MoveIssue(moving, new IssueMoveRequest(h.InProgress, null, null), default);

        Assert.Equal(h.InProgress, Value(result).StatusId);
    }

    [Fact]
    public async Task AnEpicMoveIsNeverGatedByAFullStoryAndBugSlice()
    {
        var h = await NewAsync(withEpicLimit: 2);
        await h.StoryAsync(h.InProgress);
        await h.StoryAsync(h.InProgress);
        var moving = await h.EpicAsync(h.ToDo);

        var result = await h.Issues.MoveIssue(moving, new IssueMoveRequest(h.InProgress, null, null), default);

        Assert.Equal(h.InProgress, Value(result).StatusId);
    }

    [Fact]
    public async Task APersonsOverrideOfAnEpic_LandsTheMoveAndWritesTheEpicSlicesNumbers()
    {
        var h = await NewAsync(withEpicLimit: 2);
        await h.EpicAsync(h.InProgress);
        await h.EpicAsync(h.InProgress);
        var moving = await h.EpicAsync(h.ToDo);

        var result = await h.Issues.MoveIssue(
            moving, new IssueMoveRequest(h.InProgress, null, null, WipOverride: true), default);

        Assert.Equal(h.InProgress, Value(result).StatusId);

        var overridden = Assert.Single(await h.EventsAsync(moving), e => e.Kind == EfHatchIssueEvent.WipOverridden);
        var payload = overridden.Payload!.Value;
        Assert.Equal(2, payload.GetProperty("limit").GetInt32());
        Assert.Equal(3, payload.GetProperty("load").GetInt32());
        Assert.Equal("In Progress", payload.GetProperty("to").GetString());
    }

    [Fact]
    public async Task AKeysOverrideOfAnEpic_Is403()
    {
        var h = await NewAsync(withEpicLimit: 2);
        await h.EpicAsync(h.InProgress);
        await h.EpicAsync(h.InProgress);
        var full = await h.EpicAsync(h.ToDo);
        h.Caller.Key = AKey();

        var refused = await h.Issues.MoveIssue(
            full, new IssueMoveRequest(h.InProgress, null, null, WipOverride: true), default);

        var obj = Assert.IsType<ObjectResult>(refused.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
        Assert.Equal(h.ToDo, Value(await h.Issues.GetIssue(full, default)).StatusId);
    }

    [Fact]
    public async Task ABulkMoveOfTwoEpicsWithOneSlotFree_MovesTheFirstAndRefusesTheSecond()
    {
        var h = await NewAsync(withEpicLimit: 2);
        await h.EpicAsync(h.InProgress);
        var epicOne = await h.EpicAsync(h.ToDo);
        var epicTwo = await h.EpicAsync(h.ToDo);

        var result = Value(await h.Issues.BulkEdit(Bulk([epicOne, epicTwo], statusId: h.InProgress), default));

        Assert.Equal([epicOne], result.Changed);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(epicTwo, failure.Key);
        Assert.Equal("the WIP section is full - 2 of 2 epics are in it", failure.Reason);
    }

    [Fact]
    public async Task ABatchMovingOneStoryAndOneEpic_CountsEachAgainstItsOwnSliceOnly()
    {
        var h = await NewAsync(withEpicLimit: 2);
        await h.StoryAsync(h.InProgress);
        await h.EpicAsync(h.InProgress);
        var story = await h.StoryAsync(h.ToDo);
        var epic = await h.EpicAsync(h.ToDo);

        var result = Value(await h.Issues.BulkEdit(Bulk([story, epic], statusId: h.InProgress), default));

        Assert.Equal([story, epic], result.Changed);
        Assert.Empty(result.Failures);
    }

    // ---- Harness ----

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required IssuesController Issues { get; init; }
        public required IssueThreadController Thread { get; init; }
        public required StubCallerIdentity Caller { get; init; }
        public required int ProjectId { get; init; }
        public required int ToDo { get; init; }
        public required int InProgress { get; init; }
        public required int InReview { get; init; }
        public required int Done { get; init; }

        public Task<string> StoryAsync(int statusId) => IssueAsync("story", statusId);

        public Task<string> EpicAsync(int statusId) => IssueAsync("epic", statusId);

        public async Task<string> IssueAsync(string type, int statusId)
        {
            var now = Time.GetUtcNow();
            var project = await Db.Projects.SingleAsync(p => p.Id == ProjectId);
            var number = project.NextIssueNumber++;
            var issue = new EfHatchIssue
            {
                ProjectId = ProjectId,
                Number = number,
                Type = type,
                Title = type,
                StatusId = statusId,
                Rank = 100,
                CreatedBy = "hatch",
                CreatedAt = now,
                UpdatedAt = now,
            };
            Db.Issues.Add(issue);
            await Db.SaveChangesAsync();
            return $"AER-{number}";
        }

        public async Task ClaimAsync(string key)
        {
            var now = Time.GetUtcNow();
            IssueKey.TryParse(key, out var projectKey, out var number);
            var issue = await Db.Issues.WithKey(projectKey, number).FirstAsync();
            issue.ClaimToken = Guid.NewGuid();
            issue.ClaimedBy = "hatch";
            issue.ClaimRunner = "somewhere:/checkouts/one";
            issue.ClaimedAt = now;
            issue.ClaimHeartbeatAt = now;
            await Db.SaveChangesAsync();
        }

        public async Task<IReadOnlyList<IssueEventDto>> EventsAsync(string key) =>
            Value(await Thread.GetEvents(key, default));
    }

    private static async Task<Harness> NewAsync(bool withLimit = true, int? withEpicLimit = null)
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var todo = new EfHatchStatus { Name = "To Do", SortOrder = 10 };
        var inProgress = new EfHatchStatus { Name = "In Progress", SortOrder = 20, IsWip = true };
        var inReview = new EfHatchStatus { Name = "In Review", SortOrder = 30, IsWip = true };
        var done = new EfHatchStatus { Name = "Done", SortOrder = 40, IsTerminal = true };
        db.AddRange(project, todo, inProgress, inReview, done);
        await db.SaveChangesAsync();

        if (withLimit)
        {
            db.WipLimits.Add(new EfHatchWipLimit { Types = EfHatchWipLimit.StoriesAndBugs, Limit = 2 });
            await db.SaveChangesAsync();
        }

        if (withEpicLimit is { } epicLimit)
        {
            db.WipLimits.Add(new EfHatchWipLimit { Types = EfHatchWipLimit.Epics, Limit = epicLimit });
            await db.SaveChangesAsync();
        }

        var time = new FakeTimeProvider(Now);
        var caller = new StubCallerIdentity { Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now } };
        var ranks = new RankService(db);
        var actors = new StubActorDirectory();
        var claims = TestClaims.With();

        return new Harness
        {
            Db = db,
            Time = time,
            Issues = new IssuesController(db, ranks, actors, claims, caller, time),
            Thread = new IssueThreadController(db, caller, time),
            Caller = caller,
            ProjectId = project.Id,
            ToDo = todo.Id,
            InProgress = inProgress.Id,
            InReview = inReview.Id,
            Done = done.Id,
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

    private static EfApiKey AKey() => new()
    {
        Name = "hatch",
        Prefix = "hatch_ak_x",
        Hash = [1],
        Scopes = [ApiKeyScopes.Hatch],
        CreatedAt = Now,
    };

    private static IssuePatchRequest Patch(int? statusId = null) => new(null, null, null, statusId, null, null, null, null);

    private static IssueBulkEditRequest Bulk(IReadOnlyList<string> keys, int? statusId = null) =>
        new(keys, null, statusId, null, null, null);

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {Reason(result.Result)}");

    private static string Reason(IActionResult? result) => result switch
    {
        ObjectResult o => $"{o.StatusCode}: {o.Value}",
        StatusCodeResult s => s.StatusCode.ToString(),
        null => "no result",
        _ => result.GetType().Name,
    };
}

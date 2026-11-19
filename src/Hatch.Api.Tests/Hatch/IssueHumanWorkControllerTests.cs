using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What the route writes, what it refuses to write twice, how the override is
/// read down a tree - and the property the design rests on, which is that an
/// API key cannot set one.
/// </summary>
public class IssueHumanWorkControllerTests
{
    // ---- Setting ----

    [Fact]
    public async Task AnIssue_ReadsAgentInheritUntilSomebodySaysSo()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        Assert.Equal(HumanWorkValues.Agent, issue.HumanWork);
        Assert.Equal(HumanWorkValues.Inherit, issue.HumanWorkOwn);
        Assert.Null(issue.HumanWorkFrom);
    }

    [Theory]
    [InlineData(HumanWorkValues.Human, true)]
    [InlineData(HumanWorkValues.Agent, false)]
    public async Task AValue_IsSetByName(string name, bool value)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var set = await h.SetAsync(issue.Key, name);

        Assert.Equal(name, set.HumanWork);
        Assert.Equal(name, set.HumanWorkOwn);
        Assert.Null(set.HumanWorkFrom);
        Assert.Equal(value, (await h.RowAsync(issue.Key)).HumanWork);
    }

    [Fact]
    public async Task Inherit_ClearsAnOverride()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.SetAsync(issue.Key, HumanWorkValues.Human);

        var cleared = await h.SetAsync(issue.Key, HumanWorkValues.Inherit);

        Assert.Equal(HumanWorkValues.Agent, cleared.HumanWork);
        Assert.Equal(HumanWorkValues.Inherit, cleared.HumanWorkOwn);
        Assert.Null((await h.RowAsync(issue.Key)).HumanWork);
    }

    [Fact]
    public async Task AnUnknownName_IsABadRequestAndWritesNothing()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var result = await h.HumanWork.PutIssueHumanWork(issue.Key, new HumanWorkRequest("sometimes"), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("\"sometimes\" is not a human-work setting - human, agent or inherit", bad.Value);
        Assert.Empty(await h.EventsAsync(issue.Key));
    }

    [Fact]
    public async Task AnIssueThatIsNotThere_IsNotFound()
    {
        var h = await NewAsync();

        Assert.IsType<NotFoundResult>(
            (await h.HumanWork.PutIssueHumanWork("AER-404", new HumanWorkRequest("human"), default)).Result);
    }

    // ---- The trail ----

    [Fact]
    public async Task Setting_WritesWhoDidItAndWhichWay()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.SetAsync(issue.Key, HumanWorkValues.Human);
        await h.SetAsync(issue.Key, HumanWorkValues.Agent);

        var events = await h.EventsAsync(issue.Key);
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal("Nathan", e.Actor));
        Assert.Equal(("inherit", "human"), Sides(events[0]));
        Assert.Equal(("human", "agent"), Sides(events[1]));
    }

    [Fact]
    public async Task SettingTheValueItAlreadyHolds_WritesNothing()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.SetAsync(issue.Key, HumanWorkValues.Human);

        var was = (await h.RowAsync(issue.Key)).UpdatedAt;
        h.Time.Advance(TimeSpan.FromHours(1));
        var again = await h.SetAsync(issue.Key, HumanWorkValues.Human);

        Assert.Equal(HumanWorkValues.Human, again.HumanWork);
        Assert.Single(await h.EventsAsync(issue.Key));
        Assert.Equal(was, (await h.RowAsync(issue.Key)).UpdatedAt);
    }

    [Fact]
    public async Task SettingInheritOnSomethingNeverSet_WritesNothing()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.SetAsync(issue.Key, HumanWorkValues.Inherit);

        Assert.Empty(await h.EventsAsync(issue.Key));
    }

    // ---- Inheritance ----

    [Fact]
    public async Task AChild_ReadsItsParentsOverrideAndSaysWhoseItIs()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic");
        var story = await h.FileAsync("story", "the story", epic.Key);
        await h.SetAsync(epic.Key, HumanWorkValues.Human);

        var read = await h.ReadAsync(story.Key);

        Assert.Equal(HumanWorkValues.Human, read.HumanWork);
        Assert.Equal(HumanWorkValues.Inherit, read.HumanWorkOwn);
        Assert.Equal(epic.Key, read.HumanWorkFrom);
        Assert.Null((await h.RowAsync(story.Key)).HumanWork);
    }

    [Fact]
    public async Task AnInheritInTheMiddle_DoesNotStopTheWalk()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic");
        var story = await h.FileAsync("story", "the story", epic.Key);
        var task = await h.FileAsync("task", "the task", story.Key);
        await h.SetAsync(epic.Key, HumanWorkValues.Human);

        var read = await h.ReadAsync(task.Key);

        Assert.Equal(HumanWorkValues.Human, read.HumanWork);
        Assert.Equal(epic.Key, read.HumanWorkFrom);
    }

    [Fact]
    public async Task AChildsOwnAgent_StopsTheWalkUnderAHumanAncestor()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic");
        var story = await h.FileAsync("story", "the story", epic.Key);
        var task = await h.FileAsync("task", "the task", story.Key);
        await h.SetAsync(epic.Key, HumanWorkValues.Human);
        await h.SetAsync(task.Key, HumanWorkValues.Agent);

        var read = await h.ReadAsync(task.Key);

        Assert.Equal(HumanWorkValues.Agent, read.HumanWork);
        Assert.Equal(HumanWorkValues.Agent, read.HumanWorkOwn);
        Assert.Null(read.HumanWorkFrom);

        // The story between them still reads the epic's, since nothing of its
        // own stops the walk there.
        Assert.Equal(HumanWorkValues.Human, (await h.ReadAsync(story.Key)).HumanWork);
    }

    [Fact]
    public async Task ClearingTheChildsAgent_ReturnsToTheAncestorsHuman()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic");
        var story = await h.FileAsync("story", "the story", epic.Key);
        await h.SetAsync(epic.Key, HumanWorkValues.Human);
        await h.SetAsync(story.Key, HumanWorkValues.Agent);

        await h.SetAsync(story.Key, HumanWorkValues.Inherit);

        var read = await h.ReadAsync(story.Key);
        Assert.Equal(HumanWorkValues.Human, read.HumanWork);
        Assert.Equal(epic.Key, read.HumanWorkFrom);
    }

    [Fact]
    public async Task Reparenting_ChangesItAtOnceWithNoWrite()
    {
        var h = await NewAsync();
        var human = await h.FileAsync("epic", "a person's epic");
        await h.SetAsync(human.Key, HumanWorkValues.Human);
        var plain = await h.FileAsync("epic", "a plain epic");
        var story = await h.FileAsync("story", "moves between them", human.Key);

        Assert.Equal(HumanWorkValues.Human, (await h.ReadAsync(story.Key)).HumanWork);

        await h.ReparentAsync(story.Key, plain.Key);

        Assert.Equal(HumanWorkValues.Agent, (await h.ReadAsync(story.Key)).HumanWork);
        Assert.Empty(await h.EventsAsync(story.Key));
    }

    [Fact]
    public async Task AnEpicSetAgent_WinsOverAHumanGrandparent()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "the ambition");
        await h.SetAsync(ambition.Key, HumanWorkValues.Human);
        var epic = await h.FileAsync("epic", "the epic", ambition.Key);
        await h.SetAsync(epic.Key, HumanWorkValues.Agent);

        var read = await h.ReadAsync(epic.Key);

        Assert.Equal(HumanWorkValues.Agent, read.HumanWork);
        Assert.Null(read.HumanWorkFrom);
    }

    // ---- The edge that is cut ----

    [Fact]
    public void SettingIt_IsClosedToAnApiKey()
    {
        var guard = typeof(IssueHumanWorkController)
            .GetMethod(nameof(IssueHumanWorkController.PutIssueHumanWork))!
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .SingleOrDefault();

        // No key accepted, and no class-level attribute to inherit one from:
        // marking an issue for a person is a person's own call.
        Assert.NotNull(guard);
        Assert.False(guard.AcceptKey);

        Assert.Empty(typeof(IssueHumanWorkController)
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false));
    }

    [Fact]
    public async Task AProgramCaller_GetsA403AndNothingIsWritten()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        h.Caller.Local = new Actor(ActorKind.Key, Guid.NewGuid(), "a-runner");

        var refusal = await h.HumanWork.PutIssueHumanWork(issue.Key, new HumanWorkRequest("human"), default);

        var obj = Assert.IsType<ObjectResult>(refusal.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
        Assert.Equal("marking an issue as human work is a person's call, not an agent's", obj.Value);
        Assert.Null((await h.RowAsync(issue.Key)).HumanWork);
        Assert.Empty(await h.EventsAsync(issue.Key));
    }

    [Fact]
    public async Task AProgramCaller_IsRefusedBeforeTheIssueIsLookedUp()
    {
        var h = await NewAsync();
        h.Caller.Local = new Actor(ActorKind.Key, Guid.NewGuid(), "a-runner");

        var refusal = await h.HumanWork.PutIssueHumanWork("AER-404", new HumanWorkRequest("human"), default);

        var obj = Assert.IsType<ObjectResult>(refusal.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
    }

    // ---- Reading ----

    [Fact]
    public async Task ACard_CarriesTheOverride()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic");
        var story = await h.FileAsync("story", "the story", epic.Key);
        await h.SetAsync(epic.Key, HumanWorkValues.Human);

        var cards = Value(await h.Issues.SearchIssues(null, null, null, null, null, null, null, default));

        var card = cards.Single(c => c.Key == story.Key);
        Assert.Equal(HumanWorkValues.Human, card.HumanWork);
        Assert.Equal(HumanWorkValues.Inherit, card.HumanWorkOwn);
        Assert.Equal(epic.Key, card.HumanWorkFrom);
    }

    [Fact]
    public async Task TheBoardCard_CarriesTheOverride()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.SetAsync(issue.Key, HumanWorkValues.Human);

        var board = Value(await h.Board.GetBoard(default));

        Assert.Equal(HumanWorkValues.Human, board.Issues.Single(c => c.Key == issue.Key).HumanWork);
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required StubCallerIdentity Caller { get; init; }
        public required IssueHumanWorkController HumanWork { get; init; }
        public required IssuesController Issues { get; init; }
        public required BoardController Board { get; init; }
        public required IssueThreadController Thread { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }

        public async Task<IssueDto> FileAsync(string type = "task", string title = "a thing", string? parentKey = null) =>
            Created(await Issues.CreateIssue(new IssueCreateRequest(ProjectId, type, title, null, parentKey, null, null), default));

        public async Task<IssueDto> SetAsync(string key, string value) =>
            Value(await HumanWork.PutIssueHumanWork(key, new HumanWorkRequest(value), default));

        public async Task<IssueDto> ReadAsync(string key) => Value(await Issues.GetIssue(key, default));

        public async Task ReparentAsync(string key, string newParentKey)
        {
            Value(await Issues.PatchIssue(
                key, new IssuePatchRequest(null, null, null, null, newParentKey, null, null, null, null, null), default));
        }

        /// <summary>The row itself, which is what the projection is read against.</summary>
        public async Task<EfHatchIssue> RowAsync(string key)
        {
            IssueKey.TryParse(key, out var projectKey, out var number);
            return await Db.Issues.AsNoTracking().WithKey(projectKey, number).FirstAsync();
        }

        /// <summary>The override's own events, oldest first.</summary>
        public async Task<IReadOnlyList<IssueEventDto>> EventsAsync(string key) =>
            Value(await Thread.GetEvents(key, default))
                .Where(e => e.Kind == EfHatchIssueEvent.HumanWorkChanged)
                .Reverse()
                .ToList();
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Add(project);
        db.Add(new EfHatchStatus { Name = "inbox", SortOrder = 10 });
        db.Add(new EfHatchStatus { Name = "done", SortOrder = 20, IsTerminal = true });
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var caller = new StubCallerIdentity { Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now } };
        var actors = new StubActorDirectory();

        return new Harness
        {
            Db = db,
            Caller = caller,
            HumanWork = new IssueHumanWorkController(db, actors, TestClaims.With(), caller, time),
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time, new ProjectAccess(db, actors)),
            Board = new BoardController(db, actors, TestClaims.With(), time, TestClaims.DoneHorizon()),
            Thread = new IssueThreadController(db, caller, time),
            Time = time,
            ProjectId = project.Id,
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

        /// <summary>Local mode's third lane: the person at the machine, or a runner that named itself. Null is every install with a wall.</summary>
        public Actor? Local { get; set; }

        public Task<Actor?> LocalAsync(CancellationToken ct) => Task.FromResult(Local);

        public Task<bool> IsProgramAsync(CancellationToken ct) =>
            Task.FromResult(Key is not null || Local is { Kind: ActorKind.Key });

        public Task<string> ActorNameAsync(CancellationToken ct) =>
            Task.FromResult(Person?.Name ?? Key?.Name ?? Local?.Name ?? CallerIdentity.Unattributed);
    }

    /// <summary>The two sides a <c>human_work_changed</c> event carries, by name.</summary>
    private static (string From, string To) Sides(IssueEventDto e)
    {
        var payload = e.Payload!.Value;
        return (payload.GetProperty("from").GetString()!, payload.GetProperty("to").GetString()!);
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {Reason(result.Result)}");

    private static T Created<T>(ActionResult<T> result) =>
        result.Result is CreatedAtActionResult created
            ? (T)created.Value!
            : result.Value ?? throw new InvalidOperationException($"expected a created value, got {Reason(result.Result)}");

    /// <summary>The plain-text reason on a refusal - what the UI puts on screen.</summary>
    private static string Reason(IActionResult? result) => result switch
    {
        ObjectResult o => o.Value?.ToString() ?? $"{o.StatusCode}",
        StatusCodeResult s => s.StatusCode.ToString(),
        null => "no result",
        _ => result.GetType().Name,
    };
}

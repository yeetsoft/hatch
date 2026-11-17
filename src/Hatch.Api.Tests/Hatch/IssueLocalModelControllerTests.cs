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
/// What the route writes, what it refuses to write twice, how the level is
/// read down a tree - and the property the design rests on, which is that an
/// API key cannot set one.
/// </summary>
public class IssueLocalModelControllerTests
{
    // ---- Setting ----

    [Fact]
    public async Task AnIssue_ReadsNoneUntilSomebodySaysSo()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        Assert.Equal(LocalModelLevels.NoneName, issue.LocalModel);
        Assert.Equal(LocalModelLevels.NoneName, issue.LocalModelOwn);
        Assert.Null(issue.LocalModelFrom);
    }

    [Theory]
    [InlineData(LocalModelLevels.PreferName, LocalModelLevels.Prefer)]
    [InlineData(LocalModelLevels.RequireName, LocalModelLevels.Require)]
    public async Task ALevel_IsSetByName(string name, int level)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var set = await h.SetAsync(issue.Key, name);

        Assert.Equal(name, set.LocalModel);
        Assert.Equal(name, set.LocalModelOwn);
        Assert.Null(set.LocalModelFrom);
        Assert.Equal(level, (await h.RowAsync(issue.Key)).LocalModel);
    }

    [Fact]
    public async Task None_ClearsALevel()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.SetAsync(issue.Key, LocalModelLevels.RequireName);

        var cleared = await h.SetAsync(issue.Key, LocalModelLevels.NoneName);

        Assert.Equal(LocalModelLevels.NoneName, cleared.LocalModel);
        Assert.Equal(LocalModelLevels.None, (await h.RowAsync(issue.Key)).LocalModel);
    }

    [Fact]
    public async Task AnUnknownName_IsABadRequestAndWritesNothing()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var result = await h.LocalModel.PutIssueLocalModel(issue.Key, new LocalModelRequest("always"), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("\"always\" is not a local-model level - none, prefer or require", bad.Value);
        Assert.Empty(await h.EventsAsync(issue.Key));
    }

    [Fact]
    public async Task AnIssueThatIsNotThere_IsNotFound()
    {
        var h = await NewAsync();

        Assert.IsType<NotFoundResult>(
            (await h.LocalModel.PutIssueLocalModel("AER-404", new LocalModelRequest("prefer"), default)).Result);
    }

    // ---- The trail ----

    [Fact]
    public async Task Setting_WritesWhoDidItAndWhichWay()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.SetAsync(issue.Key, LocalModelLevels.PreferName);
        await h.SetAsync(issue.Key, LocalModelLevels.RequireName);

        var events = await h.EventsAsync(issue.Key);
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal("Nathan", e.Actor));
        Assert.Equal(("none", "prefer"), Sides(events[0]));
        Assert.Equal(("prefer", "require"), Sides(events[1]));
    }

    [Fact]
    public async Task SettingTheValueItAlreadyHolds_WritesNothing()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.SetAsync(issue.Key, LocalModelLevels.PreferName);

        var was = (await h.RowAsync(issue.Key)).UpdatedAt;
        h.Time.Advance(TimeSpan.FromHours(1));
        var again = await h.SetAsync(issue.Key, LocalModelLevels.PreferName);

        Assert.Equal(LocalModelLevels.PreferName, again.LocalModel);
        Assert.Single(await h.EventsAsync(issue.Key));
        Assert.Equal(was, (await h.RowAsync(issue.Key)).UpdatedAt);
    }

    [Fact]
    public async Task SettingNoneOnSomethingNeverSet_WritesNothing()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.SetAsync(issue.Key, LocalModelLevels.NoneName);

        Assert.Empty(await h.EventsAsync(issue.Key));
    }

    // ---- Inheritance ----

    [Fact]
    public async Task AChild_ReadsItsParentsLevelAndSaysWhoseItIs()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic");
        var story = await h.FileAsync("story", "the story", epic.Key);
        await h.SetAsync(epic.Key, LocalModelLevels.PreferName);

        var read = await h.ReadAsync(story.Key);

        Assert.Equal(LocalModelLevels.PreferName, read.LocalModel);
        Assert.Equal(LocalModelLevels.NoneName, read.LocalModelOwn);
        Assert.Equal(epic.Key, read.LocalModelFrom);
        Assert.Equal(LocalModelLevels.None, (await h.RowAsync(story.Key)).LocalModel);
    }

    [Fact]
    public async Task ANoneInTheMiddle_DoesNotStopTheWalk()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic");
        var story = await h.FileAsync("story", "the story", epic.Key);
        var task = await h.FileAsync("task", "the task", story.Key);
        await h.SetAsync(epic.Key, LocalModelLevels.RequireName);

        var read = await h.ReadAsync(task.Key);

        Assert.Equal(LocalModelLevels.RequireName, read.LocalModel);
        Assert.Equal(epic.Key, read.LocalModelFrom);
    }

    [Fact]
    public async Task AChildsOwnLevel_Wins()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic");
        var story = await h.FileAsync("story", "the story", epic.Key);
        await h.SetAsync(epic.Key, LocalModelLevels.RequireName);
        await h.SetAsync(story.Key, LocalModelLevels.PreferName);

        var read = await h.ReadAsync(story.Key);

        Assert.Equal(LocalModelLevels.PreferName, read.LocalModel);
        Assert.Equal(LocalModelLevels.PreferName, read.LocalModelOwn);
        Assert.Null(read.LocalModelFrom);
    }

    // ---- The edge that is cut ----

    [Fact]
    public void SettingIt_IsClosedToAnApiKey()
    {
        var guard = typeof(IssueLocalModelController)
            .GetMethod(nameof(IssueLocalModelController.PutIssueLocalModel))!
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .SingleOrDefault();

        // No key accepted, and no class-level attribute to inherit one from:
        // choosing what runs a ticket's work is a person's call.
        Assert.NotNull(guard);
        Assert.False(guard.AcceptKey);

        Assert.Empty(typeof(IssueLocalModelController)
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false));
    }

    [Fact]
    public async Task AProgramCaller_GetsA403AndNothingIsWritten()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        h.Caller.Local = new Actor(ActorKind.Key, Guid.NewGuid(), "a-runner");

        var refusal = await h.LocalModel.PutIssueLocalModel(issue.Key, new LocalModelRequest("require"), default);

        var obj = Assert.IsType<ObjectResult>(refusal.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
        Assert.Equal("setting an issue's local-model level is a person's call, not an agent's", obj.Value);
        Assert.Equal(LocalModelLevels.None, (await h.RowAsync(issue.Key)).LocalModel);
        Assert.Empty(await h.EventsAsync(issue.Key));
    }

    // ---- Reading ----

    [Fact]
    public async Task ACard_CarriesTheLevel()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic");
        var story = await h.FileAsync("story", "the story", epic.Key);
        await h.SetAsync(epic.Key, LocalModelLevels.PreferName);

        var cards = Value(await h.Issues.SearchIssues(null, null, null, null, null, null, null, default));

        var card = cards.Single(c => c.Key == story.Key);
        Assert.Equal(LocalModelLevels.PreferName, card.LocalModel);
        Assert.Equal(LocalModelLevels.NoneName, card.LocalModelOwn);
        Assert.Equal(epic.Key, card.LocalModelFrom);
    }

    [Fact]
    public async Task TheBoardCard_CarriesTheLevel()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.SetAsync(issue.Key, LocalModelLevels.RequireName);

        var board = Value(await h.Board.GetBoard(default));

        Assert.Equal(LocalModelLevels.RequireName, board.Issues.Single(c => c.Key == issue.Key).LocalModel);
    }

    // ---- Filing ----

    [Fact]
    public async Task FilingAtALevel_BirthsItThereWithNoEvent()
    {
        var h = await NewAsync();

        var filed = Created(await h.Issues.CreateIssue(
            new IssueCreateRequest(h.ProjectId, "task", "a thing", null, null, null, null, LocalModel: "require"), default));

        Assert.Equal(LocalModelLevels.RequireName, filed.LocalModel);
        Assert.Equal(LocalModelLevels.Require, (await h.RowAsync(filed.Key)).LocalModel);
        Assert.Empty(await h.EventsAsync(filed.Key));
    }

    [Fact]
    public async Task AProgramNamingALevelAtFiling_Gets403()
    {
        var h = await NewAsync();
        h.Caller.Local = new Actor(ActorKind.Key, Guid.NewGuid(), "a-runner");

        var refusal = await h.Issues.CreateIssue(
            new IssueCreateRequest(h.ProjectId, "task", "a thing", null, null, null, null, LocalModel: "none"), default);

        var obj = Assert.IsType<ObjectResult>(refusal.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
        Assert.Equal("choosing an issue's priority, pre-approval or local-model level is a person's call, not an agent's", obj.Value);
        Assert.Empty(h.Db.Issues);
    }

    [Fact]
    public async Task ABadNameAtFiling_Is400()
    {
        var h = await NewAsync();

        var result = await h.Issues.CreateIssue(
            new IssueCreateRequest(h.ProjectId, "task", "a thing", null, null, null, null, LocalModel: "always"), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("\"always\" is not a local-model level - none, prefer or require", bad.Value);
        Assert.Empty(h.Db.Issues);
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required StubCallerIdentity Caller { get; init; }
        public required IssueLocalModelController LocalModel { get; init; }
        public required IssuesController Issues { get; init; }
        public required BoardController Board { get; init; }
        public required IssueThreadController Thread { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }

        public async Task<IssueDto> FileAsync(string type = "task", string title = "a thing", string? parentKey = null) =>
            Created(await Issues.CreateIssue(new IssueCreateRequest(ProjectId, type, title, null, parentKey, null, null), default));

        public async Task<IssueDto> SetAsync(string key, string level) =>
            Value(await LocalModel.PutIssueLocalModel(key, new LocalModelRequest(level), default));

        public async Task<IssueDto> ReadAsync(string key) => Value(await Issues.GetIssue(key, default));

        /// <summary>The row itself, which is what the projection is read against.</summary>
        public async Task<EfHatchIssue> RowAsync(string key)
        {
            IssueKey.TryParse(key, out var projectKey, out var number);
            return await Db.Issues.AsNoTracking().WithKey(projectKey, number).FirstAsync();
        }

        /// <summary>The level's own events, oldest first.</summary>
        public async Task<IReadOnlyList<IssueEventDto>> EventsAsync(string key) =>
            Value(await Thread.GetEvents(key, default))
                .Where(e => e.Kind == EfHatchIssueEvent.LocalModelChanged)
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
            LocalModel = new IssueLocalModelController(db, actors, TestClaims.With(), caller, time),
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

    /// <summary>The two sides a <c>local_model_changed</c> event carries, by name.</summary>
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

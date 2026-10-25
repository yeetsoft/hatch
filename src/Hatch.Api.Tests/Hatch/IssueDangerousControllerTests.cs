using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The dangerous flag an issue carries: set through its own route, trimmed and
/// capped, written only on a change, and read through every card and the issue
/// itself - its own and the one it inherits from an ancestor.
/// </summary>
public class IssueDangerousControllerTests
{
    // ---- Reading it ----

    [Fact]
    public async Task AFiledIssue_ReadsNoReason_ThroughEveryRead()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");

        var none = ((string?)null, (string?)null, (string?)null);
        Assert.Equal(none, (issue.DangerousOwn, issue.Dangerous, issue.DangerousFrom));
        var dto = await h.ReadAsync(issue.Key);
        Assert.Equal(none, (dto.DangerousOwn, dto.Dangerous, dto.DangerousFrom));
        Assert.Equal(none, Fields(await h.CardAsync(issue.Key)));
        Assert.Equal(none, Fields(await h.HitAsync(issue.Key)));
        Assert.Equal(none, Fields(await h.BoardAsync(issue.Key)));
    }

    [Fact]
    public async Task ARowWithAReasonSetOnTheEntity_ReadsItThroughEveryRead()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");
        await h.SetColumnAsync(issue.Key, "drops a table");

        var own = ((string?)"drops a table", (string?)"drops a table", (string?)null);
        var dto = await h.ReadAsync(issue.Key);
        Assert.Equal(own, (dto.DangerousOwn, dto.Dangerous, dto.DangerousFrom));
        Assert.Equal(own, Fields(await h.CardAsync(issue.Key)));
        Assert.Equal(own, Fields(await h.HitAsync(issue.Key)));
        Assert.Equal(own, Fields(await h.BoardAsync(issue.Key)));
    }

    [Fact]
    public async Task AChildOfAFlaggedParent_ReadsOwnNull_AndTheParentsReasonAndKey()
    {
        var h = await NewAsync();
        var parent = await h.FileAsync("story", "a story");
        var child = Created(await h.Issues.CreateIssue(
            new IssueCreateRequest(h.ProjectId, "task", "a task", null, parent.Key, null, null), default));
        await h.SetAsync(parent.Key, "load-tests the API");

        var expected = ((string?)null, (string?)"load-tests the API", (string?)parent.Key);
        var dto = await h.ReadAsync(child.Key);
        Assert.Equal(expected, (dto.DangerousOwn, dto.Dangerous, dto.DangerousFrom));
        Assert.Equal(expected, Fields(await h.CardAsync(child.Key)));
        Assert.Equal(expected, Fields(await h.HitAsync(child.Key)));
        Assert.Equal(expected, Fields(await h.BoardAsync(child.Key)));
    }

    // ---- Who may set it ----

    [Fact]
    public async Task AKeyWithNoPerson_MaySetIt()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");
        h.Caller.Person = null;
        h.Caller.Key = new EfApiKey { Name = "a key", Hash = [1], Prefix = "hatch_ak_x", CreatedAt = Now };

        var set = await h.SetAsync(issue.Key, "load-tests the API");

        Assert.Equal("load-tests the API", set.DangerousOwn);
        Assert.Equal("load-tests the API", set.Dangerous);
        Assert.Null(set.DangerousFrom);
    }

    [Fact]
    public async Task APersonWhoOwnsNothing_MaySetIt()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");
        h.Caller.Person = new EfPerson { Name = "nobody in particular", CreatedAt = Now, UpdatedAt = Now };

        var set = await h.SetAsync(issue.Key, "load-tests the API");

        Assert.Equal("load-tests the API", set.DangerousOwn);
    }

    // ---- Normalising ----

    [Fact]
    public async Task TheReasonIsTrimmed()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");

        var set = await h.SetAsync(issue.Key, "  load-tests the API \n");

        Assert.Equal("load-tests the API", set.DangerousOwn);
        Assert.Equal("load-tests the API", (await h.ReadAsync(issue.Key)).DangerousOwn);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnEmptyOrWhitespaceReason_ClearsItAndWritesToAsNull(string clear)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");
        await h.SetAsync(issue.Key, "load-tests the API");

        var set = await h.SetAsync(issue.Key, clear);

        Assert.Null(set.DangerousOwn);
        Assert.Null(set.Dangerous);
        Assert.Null(set.DangerousFrom);
        var last = (await h.EventsAsync(issue.Key))[1];
        Assert.Equal("load-tests the API", last.Payload!.Value.GetProperty("from").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, last.Payload!.Value.GetProperty("to").ValueKind);
    }

    // ---- The no-op rule ----

    [Fact]
    public async Task TheSameReasonSentAgain_WritesNoEventAndLeavesUpdatedAtWhereItWas()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");
        await h.SetAsync(issue.Key, "load-tests the API");
        var was = (await h.ReadAsync(issue.Key)).UpdatedAt;

        h.Time.Advance(TimeSpan.FromHours(1));
        await h.SetAsync(issue.Key, " load-tests the API ");

        Assert.Single(await h.EventsAsync(issue.Key));
        Assert.Equal(was, (await h.ReadAsync(issue.Key)).UpdatedAt);
    }

    [Fact]
    public async Task ClearingAnUnflaggedIssue_WritesNothing()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");

        await h.SetAsync(issue.Key, "");

        Assert.Empty(await h.EventsAsync(issue.Key));
    }

    [Fact]
    public async Task AChangedReason_WritesExactlyOneEvent_WithFromAndTo_AndMovesUpdatedAt()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");
        await h.SetAsync(issue.Key, "first");
        var was = (await h.ReadAsync(issue.Key)).UpdatedAt;

        h.Time.Advance(TimeSpan.FromHours(1));
        await h.SetAsync(issue.Key, "second");

        var events = await h.EventsAsync(issue.Key);
        Assert.Equal(2, events.Count);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, events[0].Payload!.Value.GetProperty("from").ValueKind);
        Assert.Equal("first", events[0].Payload!.Value.GetProperty("to").GetString());
        Assert.Equal("first", events[1].Payload!.Value.GetProperty("from").GetString());
        Assert.Equal("second", events[1].Payload!.Value.GetProperty("to").GetString());
        Assert.True((await h.ReadAsync(issue.Key)).UpdatedAt > was);
    }

    // ---- Refusals ----

    [Fact]
    public async Task ANullReason_Is400_AndWritesNothing()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");
        await h.SetAsync(issue.Key, "load-tests the API");

        var refused = await h.Controller.PutIssueDangerous(issue.Key, new DangerousRequest(null), default);

        var bad = Assert.IsType<BadRequestObjectResult>(refused.Result);
        Assert.Equal("reason is required - send \"\" to clear", bad.Value);
        Assert.Equal("load-tests the API", (await h.ReadAsync(issue.Key)).DangerousOwn);
    }

    [Fact]
    public async Task AReasonOf201Characters_Is400_AndOneOf200Passes()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");

        var refused = await h.Controller.PutIssueDangerous(issue.Key, new DangerousRequest(new string('x', 201)), default);

        var bad = Assert.IsType<BadRequestObjectResult>(refused.Result);
        Assert.Equal("a dangerous reason is at most 200 characters", bad.Value);
        Assert.Null((await h.ReadAsync(issue.Key)).DangerousOwn);

        var set = await h.SetAsync(issue.Key, new string('x', 200));
        Assert.Equal(200, set.DangerousOwn!.Length);
    }

    [Fact]
    public async Task TheCapIsMeasuredAfterTheTrim()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");

        var set = await h.SetAsync(issue.Key, "  " + new string('x', 200) + "  ");

        Assert.Equal(200, set.DangerousOwn!.Length);
    }

    [Fact]
    public async Task AnUnknownKey_IsNotFound()
    {
        var h = await NewAsync();

        var refused = await h.Controller.PutIssueDangerous("AER-404", new DangerousRequest("x"), default);

        Assert.IsType<NotFoundResult>(refused.Result);
    }

    // ---- What does not reach it ----

    [Fact]
    public async Task PatchingTheTitle_LeavesTheFlagAlone()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");
        await h.SetAsync(issue.Key, "load-tests the API");

        await h.Issues.PatchIssue(issue.Key, new IssuePatchRequest("a new title", null, null, null, null, null, null, null, null, null), default);

        Assert.Equal("load-tests the API", (await h.ReadAsync(issue.Key)).DangerousOwn);
    }

    private static (string? Own, string? Effective, string? From) Fields(IssueCardDto c) =>
        (c.DangerousOwn, c.Dangerous, c.DangerousFrom);

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required StubCallerIdentity Caller { get; init; }
        public required IssueDangerousController Controller { get; init; }
        public required IssuesController Issues { get; init; }
        public required BoardController Board { get; init; }
        public required SearchController Search { get; init; }
        public required IssueThreadController Thread { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }

        public async Task<IssueDto> FileAsync(string type, string title) =>
            Created(await Issues.CreateIssue(new IssueCreateRequest(ProjectId, type, title, null, null, null, null), default));

        public async Task<IssueDto> SetAsync(string key, string reason) =>
            Value(await Controller.PutIssueDangerous(key, new DangerousRequest(reason), default));

        public async Task<IssueDto> ReadAsync(string key) => Value(await Issues.GetIssue(key, default));

        /// <summary>The card the issue list draws for <paramref name="key"/>.</summary>
        public async Task<IssueCardDto> CardAsync(string key) =>
            Value(await Issues.SearchIssues(null, null, null, null, null, null, default)).Single(c => c.Key == key);

        /// <summary>The card the search draws for <paramref name="key"/>, with no query - the arm InMemory can run.</summary>
        public async Task<IssueCardDto> HitAsync(string key) =>
            Value(await Search.GetSearch(null, null, null, null, null, null, null, null, 50, 0, default))
                .Hits.Single(h => h.Card.Key == key).Card;

        /// <summary>The card the board draws for <paramref name="key"/>.</summary>
        public async Task<IssueCardDto> BoardAsync(string key) =>
            Value(await Board.GetBoard(default)).Issues.Single(c => c.Key == key);

        /// <summary>The flag's own events, oldest first.</summary>
        public async Task<IReadOnlyList<IssueEventDto>> EventsAsync(string key) =>
            Value(await Thread.GetEvents(key, default))
                .Where(e => e.Kind == EfHatchIssueEvent.DangerousChanged)
                .Reverse()
                .ToList();

        /// <summary>Straight onto the row - a seed for the read tests above, not a route under test.</summary>
        public async Task SetColumnAsync(string key, string reason)
        {
            IssueKey.TryParse(key, out var projectKey, out var number);
            var issue = await Db.Issues.WithKey(projectKey, number).FirstAsync();
            issue.Dangerous = reason;
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Add(project);
        db.Add(new EfHatchStatus { Name = "inbox", SortOrder = 10 });
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var caller = new StubCallerIdentity { Person = new EfPerson { Name = "a person", CreatedAt = Now, UpdatedAt = Now } };
        var actors = new StubActorDirectory();

        return new Harness
        {
            Db = db,
            Caller = caller,
            Controller = new IssueDangerousController(db, actors, TestClaims.With(), caller, time),
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time, new ProjectAccess(db, actors)),
            Board = new BoardController(db, actors, TestClaims.With(), time, TestClaims.DoneHorizon()),
            Search = new SearchController(db, actors, TestClaims.With(), time),
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

        public Actor? Local { get; set; }

        public Task<Actor?> LocalAsync(CancellationToken ct) => Task.FromResult(Local);

        public Task<bool> IsProgramAsync(CancellationToken ct) =>
            Task.FromResult(Key is not null || Local is { Kind: ActorKind.Key });

        public Task<string> ActorNameAsync(CancellationToken ct) =>
            Task.FromResult(Person?.Name ?? Key?.Name ?? Local?.Name ?? CallerIdentity.Unattributed);
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result}");

    private static T Created<T>(ActionResult<T> result) =>
        result.Result is CreatedAtActionResult created
            ? (T)created.Value!
            : result.Value ?? throw new InvalidOperationException($"expected a created value, got {result.Result}");
}

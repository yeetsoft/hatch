using System.Text.Json;
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
/// What an issue drew on: the dreams woven into it, read and replaced whole,
/// and the one event a change to that set leaves on the issue's thread.
/// </summary>
public class IssueDreamsControllerTests
{
    // ---- Reading it ----

    [Fact]
    public async Task AFreshIssue_ReadsAnEmptyDreamSet()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");

        Assert.Empty(await h.GetAsync(issue.Key));
    }

    // ---- Replacing it ----

    [Fact]
    public async Task APut_ThenAGet_RoundTripsTheSet()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");
        var a = await h.SeedDreamAsync("first");
        h.Time.Advance(TimeSpan.FromMinutes(1));
        var b = await h.SeedDreamAsync("second");

        var put = await h.PutAsync(issue.Key, a.Id, b.Id);

        Assert.Equal(new[] { b.Id, a.Id }, put.Select(d => d.Id));
        Assert.Equal(new[] { b.Id, a.Id }, (await h.GetAsync(issue.Key)).Select(d => d.Id));
    }

    [Fact]
    public async Task DuplicateIdsInTheRequest_CollapseToOne()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");
        var a = await h.SeedDreamAsync("first");

        var put = await h.PutAsync(issue.Key, a.Id, a.Id);

        Assert.Equal(new[] { a.Id }, put.Select(d => d.Id));
        Assert.Equal(new[] { a.Id }, (await h.GetAsync(issue.Key)).Select(d => d.Id));
        var events = await h.EventsAsync(issue.Key);
        Assert.Single(events);
        Assert.Equal(new[] { a.Id }, events[0].Payload!.Value.GetProperty("to").EnumerateArray().Select(e => e.GetInt64()));
    }

    // ---- The no-op rule ----

    [Fact]
    public async Task TheSameSetSentAgain_InADifferentOrder_WritesNoEvent_AndLeavesUpdatedAtWhereItWas()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");
        var a = await h.SeedDreamAsync("first");
        var b = await h.SeedDreamAsync("second");
        await h.PutAsync(issue.Key, a.Id, b.Id);
        var was = (await h.ReadAsync(issue.Key)).UpdatedAt;

        h.Time.Advance(TimeSpan.FromHours(1));
        await h.PutAsync(issue.Key, b.Id, a.Id);

        Assert.Single(await h.EventsAsync(issue.Key));
        Assert.Equal(was, (await h.ReadAsync(issue.Key)).UpdatedAt);
    }

    [Fact]
    public async Task AChangedSet_WritesExactlyOneEvent_WithFromAndToAsSortedIdArrays()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");
        var a = await h.SeedDreamAsync("first");
        var b = await h.SeedDreamAsync("second");
        await h.PutAsync(issue.Key, a.Id);

        h.Time.Advance(TimeSpan.FromHours(1));
        await h.PutAsync(issue.Key, b.Id, a.Id);

        var events = await h.EventsAsync(issue.Key);
        Assert.Equal(2, events.Count);
        var last = events[1];
        Assert.Equal(new[] { a.Id }, last.Payload!.Value.GetProperty("from").EnumerateArray().Select(e => e.GetInt64()));
        Assert.Equal(
            new[] { a.Id, b.Id }.Order(),
            last.Payload!.Value.GetProperty("to").EnumerateArray().Select(e => e.GetInt64()));
        Assert.Equal(Now.AddHours(1), (await h.ReadAsync(issue.Key)).UpdatedAt);
    }

    [Fact]
    public async Task AnEmptySet_ClearsIt_AndWritesToAsAnEmptyArray()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");
        var a = await h.SeedDreamAsync("first");
        await h.PutAsync(issue.Key, a.Id);

        var put = await h.PutAsync(issue.Key);

        Assert.Empty(put);
        Assert.Empty(await h.GetAsync(issue.Key));
        var last = (await h.EventsAsync(issue.Key))[1];
        Assert.Empty(last.Payload!.Value.GetProperty("to").EnumerateArray());
    }

    // ---- Refusals ----

    [Fact]
    public async Task AnIdThatIsNoDream_Is400NamingIt_AndWritesNothing()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");
        var a = await h.SeedDreamAsync("first");
        await h.PutAsync(issue.Key, a.Id);

        var refused = await h.Controller.PutIssueDreams(issue.Key, new IssueDreamsWriteRequest([a.Id, 999_999]), default);

        var bad = Assert.IsType<BadRequestObjectResult>(refused.Result);
        Assert.Equal("there is no dream 999999", bad.Value);
        Assert.Equal(new[] { a.Id }, (await h.GetAsync(issue.Key)).Select(d => d.Id));
        Assert.Single(await h.EventsAsync(issue.Key));
    }

    [Fact]
    public async Task AnUnknownIssueKey_IsBareNotFound()
    {
        var h = await NewAsync();

        foreach (var key in new[] { "AER-404", "nonsense" })
        {
            Assert.IsType<NotFoundResult>((await h.Controller.PutIssueDreams(key, new IssueDreamsWriteRequest([]), default)).Result);
            Assert.IsType<NotFoundResult>((await h.Controller.GetIssueDreams(key, default)).Result);
        }
    }

    // ---- What the dream reads back ----

    [Fact]
    public async Task WovenInto_ShowsUpOnTheDreamAfterAPut_AndIsGoneAfterOneThatDropsIt()
    {
        var h = await NewAsync();
        var first = await h.FileAsync("task", "a thing");
        var second = await h.FileAsync("task", "another thing");
        var a = await h.SeedDreamAsync("first");
        await h.PutAsync(first.Key, a.Id);
        await h.PutAsync(second.Key, a.Id);

        var both = new[] { first.Key, second.Key };
        Assert.Equal(both, (await h.ListedAsync(a.Id)).WovenInto);
        Assert.Equal(both, (await h.EditedAsync(a.Id)).WovenInto);

        await h.PutAsync(first.Key);

        var only = new[] { second.Key };
        Assert.Equal(only, (await h.ListedAsync(a.Id)).WovenInto);
        Assert.Equal(only, (await h.EditedAsync(a.Id)).WovenInto);
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required StubCallerIdentity Caller { get; init; }
        public required IssueDreamsController Controller { get; init; }
        public required DreamsController Dreams { get; init; }
        public required IssuesController Issues { get; init; }
        public required IssueThreadController Thread { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }

        public async Task<IssueDto> FileAsync(string type, string title) =>
            Created(await Issues.CreateIssue(new IssueCreateRequest(ProjectId, type, title, null, null, null, null), default));

        public async Task<DreamDto> SeedDreamAsync(string body) =>
            CreatedDream(await Dreams.AddDream(new DreamCreateRequest(body), default));

        public async Task<DreamDto[]> PutAsync(string key, params long[] ids) =>
            Value(await Controller.PutIssueDreams(key, new IssueDreamsWriteRequest(ids), default));

        public async Task<DreamDto[]> GetAsync(string key) => Value(await Controller.GetIssueDreams(key, default));

        public async Task<IssueDto> ReadAsync(string key) => Value(await Issues.GetIssue(key, default));

        /// <summary>The issue's dream events, oldest first.</summary>
        public async Task<IReadOnlyList<IssueEventDto>> EventsAsync(string key) =>
            Value(await Thread.GetEvents(key, default))
                .Where(e => e.Kind == EfHatchIssueEvent.DreamsWoven)
                .Reverse()
                .ToList();

        /// <summary>The dream as the dream list draws it.</summary>
        public async Task<DreamDto> ListedAsync(long id) =>
            Value(await Dreams.GetDreams(null, null, 50, 0, null, false, default)).Dreams.Single(d => d.Id == id);

        /// <summary>The dream as an edit that changes nothing answers it.</summary>
        public async Task<DreamDto> EditedAsync(long id) =>
            Value(await Dreams.EditDream(id, JsonDocument.Parse("{}").RootElement, default));
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
            Controller = new IssueDreamsController(db, caller, time),
            Dreams = new DreamsController(db, caller, time),
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time, new ProjectAccess(db, actors)),
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

    private static T CreatedDream<T>(ActionResult<T> result) =>
        result.Result is CreatedResult created
            ? (T)created.Value!
            : result.Value ?? throw new InvalidOperationException($"expected a created value, got {result.Result}");
}

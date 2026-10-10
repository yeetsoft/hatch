using System.Text.Json;
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
/// The two writes that set and clear the mark an increment leaves on an issue
/// it did not finish: who may write each, what a second mark does to the
/// first, and what resuming clears.
/// </summary>
public class IssueStallControllerTests
{
    // ---- Stalling: open to a key ----

    [Fact]
    public async Task AKeyMayStall()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        h.Caller.Key = AKey();

        var stalled = await h.StallAsync(issue.Key, "the session ended without moving it");

        Assert.NotNull(stalled.StalledAt);
        Assert.Equal("the session ended without moving it", stalled.StalledWhy);
    }

    [Fact]
    public async Task APersonMayStallHoldAndResume()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var stalled = await h.StallAsync(issue.Key, "weather");
        Assert.NotNull(stalled.StalledAt);

        var held = await h.HoldAsync(issue.Key, true);
        Assert.True(held.Held);

        var resumed = await h.HoldAsync(issue.Key, false);
        Assert.False(resumed.Held);
        Assert.Null(resumed.StalledAt);
        Assert.Null(resumed.StalledWhy);
    }

    // ---- Holding and resuming: a person's, never a key's ----

    [Fact]
    public async Task AKeyMayNotHoldOrResume()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        h.Caller.Key = AKey();

        var refusedHold = Assert.IsType<ObjectResult>(
            (await h.Hold.PutHold(issue.Key, new HoldRequest(true), default)).Result);
        Assert.Equal(StatusCodes.Status403Forbidden, refusedHold.StatusCode);
        Assert.Equal("holding a ticket is the operator's, not an agent's", refusedHold.Value);

        var refusedResume = Assert.IsType<ObjectResult>(
            (await h.Hold.PutHold(issue.Key, new HoldRequest(false), default)).Result);
        Assert.Equal(StatusCodes.Status403Forbidden, refusedResume.StatusCode);
        Assert.Equal("holding a ticket is the operator's, not an agent's", refusedResume.Value);
    }

    /// <summary>The same narrowing where the wall is off: a key with no credential is still a key.</summary>
    [Fact]
    public async Task AKeyMayNotHoldOrResume_AgainWithNoWall()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        h.Caller.Local = new Actor(ActorKind.Key, Guid.NewGuid(), "host:/src");

        var refused = Assert.IsType<ObjectResult>(
            (await h.Hold.PutHold(issue.Key, new HoldRequest(true), default)).Result);
        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Equal("holding a ticket is the operator's, not an agent's", refused.Value);

        // And the local person may.
        h.Caller.Local = new Actor(ActorKind.Person, Guid.NewGuid(), "Ada");
        var held = await h.HoldAsync(issue.Key, true);
        Assert.True(held.Held);
    }

    // ---- A second mark replaces the first ----

    [Fact]
    public async Task ASecondMark_ReplacesTheFirst()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.StallAsync(issue.Key, "first reason");
        var second = await h.StallAsync(issue.Key, "second reason");

        Assert.Equal("second reason", second.StalledWhy);
        Assert.Equal("second reason", (await h.RowAsync(issue.Key)).StalledWhy);

        var events = await h.EventsAsync(issue.Key, EfHatchIssueEvent.Stalled);
        Assert.Equal(2, events.Count);
    }

    // ---- Resuming clears the mark ----

    [Fact]
    public async Task Resuming_ClearsStalledAtAndWhy()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.StallAsync(issue.Key, "weather");

        await h.HoldAsync(issue.Key, false);

        var row = await h.RowAsync(issue.Key);
        Assert.Null(row.StalledAt);
        Assert.Null(row.StalledWhy);
    }

    // ---- Setting what it already holds ----

    [Fact]
    public async Task SettingHeldTrueTwice_WritesNothingTheSecondTime()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.HoldAsync(issue.Key, true);

        var was = (await h.ReadAsync(issue.Key)).UpdatedAt;
        h.Time.Advance(TimeSpan.FromHours(1));
        await h.HoldAsync(issue.Key, true);

        Assert.Equal(was, (await h.ReadAsync(issue.Key)).UpdatedAt);
        Assert.Single(await h.EventsAsync(issue.Key, EfHatchIssueEvent.Held));
    }

    [Fact]
    public async Task ResumingATicketThatWasNeverHeldOrStalled_WritesNothing()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var was = (await h.ReadAsync(issue.Key)).UpdatedAt;
        h.Time.Advance(TimeSpan.FromHours(1));
        await h.HoldAsync(issue.Key, false);

        Assert.Equal(was, (await h.ReadAsync(issue.Key)).UpdatedAt);
        Assert.Empty(await h.EventsAsync(issue.Key, EfHatchIssueEvent.Resumed));
    }

    // ---- Not found ----

    [Fact]
    public async Task AnIssueThatIsNotThere_IsNotFound()
    {
        var h = await NewAsync();

        Assert.IsType<NotFoundResult>(
            (await h.Stall.PutStall("AER-404", new StallRequest("weather"), default)).Result);
        Assert.IsType<NotFoundResult>(
            (await h.Hold.PutHold("AER-404", new HoldRequest(true), default)).Result);
    }

    // ---- The edge that is cut ----

    [Fact]
    public void HoldIsClosedToAnApiKey()
    {
        var guard = typeof(IssueStallController)
            .GetMethod(nameof(IssueStallController.PutHold))!
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .SingleOrDefault();

        Assert.NotNull(guard);
        Assert.Null(guard.AcceptScope);
    }

    [Fact]
    public void StallCarriesNoScopeAttribute()
    {
        Assert.Empty(typeof(IssueStallController)
            .GetMethod(nameof(IssueStallController.PutStall))!
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false));

        Assert.Empty(typeof(IssueStallController)
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false));
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required IssueStallController Stall { get; init; }
        public required IssueStallController Hold { get; init; }
        public required IssuesController Issues { get; init; }
        public required IssueThreadController Thread { get; init; }
        public required StubCallerIdentity Caller { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }

        public async Task<IssueDto> FileAsync(string type = "task", string title = "a thing") =>
            Created(await Issues.CreateIssue(new IssueCreateRequest(ProjectId, type, title, null, null, null, null), default));

        public async Task<IssueDto> StallAsync(string key, string why) =>
            Value(await Stall.PutStall(key, new StallRequest(why), default));

        public async Task<IssueDto> HoldAsync(string key, bool held) =>
            Value(await Hold.PutHold(key, new HoldRequest(held), default));

        public async Task<IssueDto> ReadAsync(string key) => Value(await Issues.GetIssue(key, default));

        public async Task<EfHatchIssue> RowAsync(string key)
        {
            IssueKey.TryParse(key, out var projectKey, out var number);
            return await Db.Issues.AsNoTracking().WithKey(projectKey, number).FirstAsync();
        }

        public async Task<IReadOnlyList<IssueEventDto>> EventsAsync(string key, string kind) =>
            Value(await Thread.GetEvents(key, default))
                .Where(e => e.Kind == kind)
                .Reverse()
                .ToList();
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var hatch = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Add(hatch);
        var inbox = new EfHatchStatus { Name = "inbox", SortOrder = 10 };
        db.Add(inbox);
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var caller = new StubCallerIdentity { Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now } };
        var actors = new StubActorDirectory();

        return new Harness
        {
            Db = db,
            Stall = new IssueStallController(db, actors, TestClaims.With(), caller, time),
            Hold = new IssueStallController(db, actors, TestClaims.With(), caller, time),
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time),
            Thread = new IssueThreadController(db, caller, time),
            Caller = caller,
            Time = time,
            ProjectId = hatch.Id,
        };
    }

    /// <summary>A key with the hatch scope, the same shape every other controller's tests refuse one with.</summary>
    private static EfApiKey AKey() => new()
    {
        Name = "hatch",
        Prefix = "hatch_ak_x",
        Hash = [1],
        Scopes = [ApiKeyScopes.Hatch],
        CreatedAt = Now,
    };

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
        result.Value ?? throw new InvalidOperationException($"expected a value, got {Reason(result.Result)}");

    private static T Created<T>(ActionResult<T> result) =>
        result.Result is CreatedAtActionResult created
            ? (T)created.Value!
            : result.Value ?? throw new InvalidOperationException($"expected a created value, got {Reason(result.Result)}");

    private static string Reason(IActionResult? result) => result switch
    {
        ObjectResult o => o.Value?.ToString() ?? $"{o.StatusCode}",
        StatusCodeResult s => s.StatusCode.ToString(),
        null => "no result",
        _ => result.GetType().Name,
    };
}

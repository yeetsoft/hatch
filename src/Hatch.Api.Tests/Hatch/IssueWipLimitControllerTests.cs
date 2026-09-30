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
/// How many of an epic's stories may be in progress at once: what it accepts,
/// what it refuses, what it writes in the trail - and the one property the
/// whole design rests on, which is that an API key cannot set one.
/// </summary>
public class IssueWipLimitControllerTests
{
    // ---- Setting, changing, clearing ----

    [Fact]
    public async Task ALimit_IsSetAndReadBack()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic");

        var patched = await h.SetAsync(epic.Key, "2");

        Assert.Equal(2, patched.WipLimit);
        Assert.Equal(2, (await h.ReadAsync(epic.Key)).WipLimit);
    }

    [Fact]
    public async Task AnEmptyString_ClearsIt()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic");
        await h.SetAsync(epic.Key, "2");

        var cleared = await h.SetAsync(epic.Key, "");

        Assert.Null(cleared.WipLimit);
    }

    [Fact]
    public async Task Whitespace_ClearsIt_TheSameAsEmpty()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic");
        await h.SetAsync(epic.Key, "2");

        var cleared = await h.SetAsync(epic.Key, "   ");

        Assert.Null(cleared.WipLimit);
    }

    [Fact]
    public async Task ANullLimit_WritesNothingAndReadsBackTheDto()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic");
        await h.SetAsync(epic.Key, "2");

        var read = Value(await h.Wip.PatchIssueWipLimit(epic.Key, new IssueWipLimitRequest(null), default));

        Assert.Equal(2, read.WipLimit);
        Assert.Single(await h.EventsAsync(epic.Key));
    }

    // ---- Refusals ----

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("three")]
    public async Task ABadValue_IsRefused_AndWritesNothing(string bad)
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic");

        var refusal = await h.Wip.PatchIssueWipLimit(epic.Key, new IssueWipLimitRequest(bad), default);

        Assert.Equal(
            $"stories at once is a whole number of one or more - not \"{bad}\"",
            Reason(refusal.Result));
        Assert.Null((await h.ReadAsync(epic.Key)).WipLimit);
        Assert.Empty(await h.EventsAsync(epic.Key));
    }

    [Theory]
    [InlineData("story")]
    [InlineData("task")]
    [InlineData("bug")]
    public async Task OnAnythingButAnEpic_ItIsRefused(string type)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync(type, "a thing");

        var refusal = await h.Wip.PatchIssueWipLimit(issue.Key, new IssueWipLimitRequest("2"), default);

        Assert.Equal(
            $"only an epic takes a limit on its stories at once - {issue.Key} is a {type}",
            Reason(refusal.Result));
        Assert.Empty(await h.EventsAsync(issue.Key));
    }

    [Fact]
    public async Task OnAnythingButAnEpic_EvenAnEmptyBodyIsRefused()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "a thing");

        var refusal = await h.Wip.PatchIssueWipLimit(issue.Key, new IssueWipLimitRequest(""), default);

        Assert.IsType<BadRequestObjectResult>(refusal.Result);
    }

    [Fact]
    public async Task AnUnknownKey_Is404()
    {
        var h = await NewAsync();

        Assert.IsType<NotFoundResult>(
            (await h.Wip.PatchIssueWipLimit("AER-404", new IssueWipLimitRequest("2"), default)).Result);
    }

    // ---- The trail ----

    [Fact]
    public async Task SettingAndClearing_EachNameWhatTheValueWentFromAndTo()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic");

        await h.SetAsync(epic.Key, "2");
        await h.SetAsync(epic.Key, "3");
        await h.SetAsync(epic.Key, "");

        var events = (await h.EventsAsync(epic.Key)).ToList();

        Assert.All(events, e => Assert.Equal("Nathan", e.Actor));
        Assert.Equal((null, 2), Sides(events[0]));
        Assert.Equal((2, 3), Sides(events[1]));
        Assert.Equal((3, null), Sides(events[2]));
    }

    [Fact]
    public async Task SettingTheValueItAlreadyHolds_WritesNothingAndDoesNotMoveTheUpdatedTime()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic");
        await h.SetAsync(epic.Key, "2");

        var was = (await h.ReadAsync(epic.Key)).UpdatedAt;
        h.Time.Advance(TimeSpan.FromHours(1));
        await h.SetAsync(epic.Key, "2");

        Assert.Single(await h.EventsAsync(epic.Key));
        Assert.Equal(was, (await h.ReadAsync(epic.Key)).UpdatedAt);
    }

    [Fact]
    public async Task ClearingItWhenItIsAlreadyClear_WritesNothing()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic");

        await h.SetAsync(epic.Key, "");

        Assert.Empty(await h.EventsAsync(epic.Key));
    }

    // ---- The one edge that is cut ----

    [Fact]
    public void SettingIt_IsClosedToAnApiKey()
    {
        var guard = typeof(IssueWipLimitController)
            .GetMethod(nameof(IssueWipLimitController.PatchIssueWipLimit))!
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .SingleOrDefault();

        Assert.NotNull(guard);
        Assert.Null(guard.AcceptScope);

        Assert.Empty(typeof(IssueWipLimitController)
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false));
    }

    [Fact]
    public async Task AKey_IsRefusedWithASentenceAndWritesNothing()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic");

        h.Caller.Person = null;
        h.Caller.Key = new EfApiKey { Name = "a key", Hash = [1], Prefix = "hatch_ak_x", CreatedAt = Now };

        var refused = Assert.IsType<ObjectResult>(
            (await h.Wip.PatchIssueWipLimit(epic.Key, new IssueWipLimitRequest("2"), default)).Result);
        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Null((await h.ReadAsync(epic.Key)).WipLimit);
        Assert.Empty(await h.EventsAsync(epic.Key));
    }

    /// <summary>A keyless runner in local mode is a program too - the check asks "is this a program", not "is there a key".</summary>
    [Fact]
    public async Task AKeylessRunnerWhereTheWallIsOff_IsRefusedTheSameWay()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic");

        h.Caller.Person = null;
        h.Caller.Local = new Actor(ActorKind.Key, LocalCaller.RunnerIdFor("host:/src"), "host:/src");

        var refused = Assert.IsType<ObjectResult>(
            (await h.Wip.PatchIssueWipLimit(epic.Key, new IssueWipLimitRequest("2"), default)).Result);
        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Empty(await h.EventsAsync(epic.Key));
    }

    [Fact]
    public async Task AKey_ReadsItThroughGetIssue()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic");
        await h.SetAsync(epic.Key, "2");

        // It rides IssueDto, which IssuesController hands out to the hatch
        // scope: an agent is entitled to know how many of its siblings it is
        // competing against.
        var read = Value(await h.Issues.GetIssue(epic.Key, default));

        Assert.Equal(2, read.WipLimit);
    }

    [Fact]
    public async Task PatchingAnIssue_WithWipLimitInTheBody_LeavesTheLimitAsItWasSet()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic");
        await h.SetAsync(epic.Key, "2");

        // The key-writable route ignores a field it does not define - ASP.NET
        // Core's default for unknown JSON members - so this is a test, not a
        // change.
        var json = """{"title":"renamed","wipLimit":5}""";
        var request = JsonSerializer.Deserialize<IssuePatchRequest>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        var patched = Value(await h.Issues.PatchIssue(epic.Key, request, default));

        Assert.Equal("renamed", patched.Title);
        Assert.Equal(2, patched.WipLimit);
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required IssueWipLimitController Wip { get; init; }
        public required IssuesController Issues { get; init; }
        public required IssueThreadController Thread { get; init; }
        public required StubCallerIdentity Caller { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }

        public async Task<IssueDto> FileAsync(string type = "task", string title = "a thing") =>
            Created(await Issues.CreateIssue(new IssueCreateRequest(ProjectId, type, title, null, null, null, null), default));

        public async Task<IssueDto> SetAsync(string key, string limit) =>
            Value(await Wip.PatchIssueWipLimit(key, new IssueWipLimitRequest(limit), default));

        public async Task<IssueDto> ReadAsync(string key) => Value(await Issues.GetIssue(key, default));

        /// <summary>The limit's own events, oldest first.</summary>
        public async Task<IReadOnlyList<IssueEventDto>> EventsAsync(string key) =>
            Value(await Thread.GetEvents(key, default))
                .Where(e => e.Kind == EfHatchIssueEvent.WipLimitChanged)
                .Reverse()
                .ToList();
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var hatch = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Add(hatch);
        db.Add(new EfHatchStatus { Name = "inbox", SortOrder = 10 });
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var caller = new StubCallerIdentity { Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now } };
        var actors = new StubActorDirectory();

        return new Harness
        {
            Wip = new IssueWipLimitController(db, actors, TestClaims.With(), caller, time),
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time),
            Thread = new IssueThreadController(db, caller, time),
            Caller = caller,
            Time = time,
            ProjectId = hatch.Id,
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

    /// <summary>The <c>from</c> and <c>to</c> a <c>wip_limit_changed</c> event carries.</summary>
    private static (int? From, int? To) Sides(IssueEventDto e)
    {
        var payload = e.Payload!.Value;
        return (
            payload.GetProperty("from").ValueKind == JsonValueKind.Null ? null : payload.GetProperty("from").GetInt32(),
            payload.GetProperty("to").ValueKind == JsonValueKind.Null ? null : payload.GetProperty("to").GetInt32());
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

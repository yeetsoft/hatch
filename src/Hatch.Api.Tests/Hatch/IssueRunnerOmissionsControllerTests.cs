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
/// The runners an issue omits: added, rewritten and lifted through their own
/// routes, trimmed and capped, written only on a change, and refused where the
/// issue only inherits the omission.
/// </summary>
public class IssueRunnerOmissionsControllerTests
{
    // ---- Adding ----

    [Fact]
    public async Task AnOmissionWithAReason_CreatesARow_WithTheCallerAndTheTime_AndTheReasonTrimmed()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");

        var dto = await h.OmitAsync(issue.Key, "alpha", "  too risky \n");

        var row = await h.Db.RunnerOmissions.SingleAsync();
        Assert.Equal(("alpha", "too risky", "a person", Now), (row.RunnerName, row.Reason, row.By, row.At));
        var own = Assert.Single(dto.OmittedRunnersOwn);
        Assert.Equal(("alpha", "too risky", (string?)null), (own.Runner, own.Reason, own.FromKey));
        Assert.Equal("alpha", Assert.Single(dto.OmittedRunners).Runner);
        Assert.Equal("alpha", Assert.Single((await h.ReadAsync(issue.Key)).OmittedRunnersOwn).Runner);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task AnEmptyWhitespaceOrNullReason_IsStoredAsNoReason(string? reason)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");

        var dto = await h.OmitAsync(issue.Key, "alpha", reason);

        Assert.Null((await h.Db.RunnerOmissions.SingleAsync()).Reason);
        Assert.Null(Assert.Single(dto.OmittedRunnersOwn).Reason);
    }

    [Fact]
    public async Task TheRunnerNameIsTrimmed()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");

        await h.OmitAsync(issue.Key, "  alpha ", "x");

        Assert.Equal("alpha", (await h.Db.RunnerOmissions.SingleAsync()).RunnerName);
    }

    // ---- The no-op rule ----

    [Fact]
    public async Task TheSameReasonSentTwice_KeepsOneRowAndOneEvent_AndLeavesUpdatedAtWhereItWas()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");
        await h.OmitAsync(issue.Key, "alpha", "too risky");
        var was = (await h.ReadAsync(issue.Key)).UpdatedAt;

        h.Time.Advance(TimeSpan.FromHours(1));
        await h.OmitAsync(issue.Key, "alpha", " too risky ");

        Assert.Single(await h.Db.RunnerOmissions.ToListAsync());
        Assert.Single(await h.EventsAsync(issue.Key));
        Assert.Equal(was, (await h.ReadAsync(issue.Key)).UpdatedAt);
        Assert.Equal(Now, (await h.Db.RunnerOmissions.SingleAsync()).At);
    }

    [Fact]
    public async Task ADifferentReason_RewritesByAndAt_AndWritesASecondEventWithFrom()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");
        await h.OmitAsync(issue.Key, "alpha", "first");

        h.Caller.Person = new EfPerson { Name = "someone else", CreatedAt = Now, UpdatedAt = Now };
        h.Time.Advance(TimeSpan.FromHours(1));
        await h.OmitAsync(issue.Key, "alpha", "second");

        var row = await h.Db.RunnerOmissions.SingleAsync();
        Assert.Equal(("second", "someone else", Now.AddHours(1)), (row.Reason, row.By, row.At));
        var events = await h.EventsAsync(issue.Key);
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal("alpha", e.Payload!.Value.GetProperty("runner").GetString()));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, events[0].Payload!.Value.GetProperty("from").ValueKind);
        Assert.Equal("first", events[0].Payload!.Value.GetProperty("reason").GetString());
        Assert.Equal("first", events[1].Payload!.Value.GetProperty("from").GetString());
        Assert.Equal("second", events[1].Payload!.Value.GetProperty("reason").GetString());
        Assert.Equal(Now.AddHours(1), (await h.ReadAsync(issue.Key)).UpdatedAt);
    }

    [Fact]
    public async Task AnEscapedName_IsStoredAsTheNameTheRunnerCallsItself()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");

        // Kestrel leaves %2F encoded in the path on purpose, so a runner whose
        // name is a path arrives here still carrying it.
        await h.OmitAsync(issue.Key, "host:%2Fcheckouts%2Fone", "x");

        Assert.Equal("host:/checkouts/one", (await h.Db.RunnerOmissions.SingleAsync()).RunnerName);
    }

    [Fact]
    public async Task TheRunnerNameIsMatchedCaseSensitively()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");

        await h.OmitAsync(issue.Key, "alpha", "x");
        await h.OmitAsync(issue.Key, "Alpha", "x");

        Assert.Equal(2, await h.Db.RunnerOmissions.CountAsync());
    }

    // ---- Refusals ----

    [Fact]
    public async Task AReasonOf201Characters_Is400_AndNothingIsWritten()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");

        var refused = await h.Controller.PutOmittedRunner(issue.Key, "alpha", new OmitRunnerRequest(new string('x', 201)), default);

        var bad = Assert.IsType<BadRequestObjectResult>(refused.Result);
        Assert.Equal("an omission reason is at most 200 characters", bad.Value);
        Assert.Empty(await h.Db.RunnerOmissions.ToListAsync());
        Assert.Empty(await h.EventsAsync(issue.Key));
    }

    [Fact]
    public async Task AReasonOf200Characters_Passes_AlsoWhenItIs200AfterTheTrim()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");

        var plain = await h.OmitAsync(issue.Key, "alpha", new string('x', 200));
        var padded = await h.OmitAsync(issue.Key, "beta", "  " + new string('x', 200) + "  ");

        Assert.All(padded.OmittedRunnersOwn, o => Assert.Equal(200, o.Reason!.Length));
        Assert.Equal(200, Assert.Single(plain.OmittedRunnersOwn).Reason!.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task AnEmptyOrWhitespaceRunner_Is400_AndNothingIsWritten(string runner)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");

        var refused = await h.Controller.PutOmittedRunner(issue.Key, runner, new OmitRunnerRequest("x"), default);

        var bad = Assert.IsType<BadRequestObjectResult>(refused.Result);
        Assert.Equal("a runner name is required", bad.Value);
        Assert.Empty(await h.Db.RunnerOmissions.ToListAsync());
        Assert.Empty(await h.EventsAsync(issue.Key));
    }

    [Fact]
    public async Task ARunnerNameOneOverTheLimit_Is400_AndNothingIsWritten()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");

        var refused = await h.Controller.PutOmittedRunner(
            issue.Key, new string('r', EfHatchRunner.MaxNameLength + 1), new OmitRunnerRequest("x"), default);

        var bad = Assert.IsType<BadRequestObjectResult>(refused.Result);
        Assert.Equal($"a runner name is at most {EfHatchRunner.MaxNameLength} characters", bad.Value);
        Assert.Empty(await h.Db.RunnerOmissions.ToListAsync());
        Assert.Empty(await h.EventsAsync(issue.Key));
    }

    [Fact]
    public async Task AnUnknownKey_IsNotFound_ForBothRoutes()
    {
        var h = await NewAsync();

        var put = await h.Controller.PutOmittedRunner("AER-404", "alpha", new OmitRunnerRequest("x"), default);
        var delete = await h.Controller.DeleteOmittedRunner("AER-404", "alpha", default);

        Assert.IsType<NotFoundResult>(put.Result);
        Assert.IsType<NotFoundResult>(delete.Result);
    }

    // ---- Lifting ----

    [Fact]
    public async Task LiftingAnOwnOmission_RemovesTheRow_WritesOneEvent_AndLeavesItOutOfBothLists()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");
        await h.OmitAsync(issue.Key, "alpha", "x");
        await h.OmitAsync(issue.Key, "beta", "y");

        h.Time.Advance(TimeSpan.FromHours(1));
        var dto = await h.LiftAsync(issue.Key, " alpha ");

        Assert.Equal("beta", Assert.Single(await h.Db.RunnerOmissions.ToListAsync()).RunnerName);
        Assert.Equal("beta", Assert.Single(dto.OmittedRunnersOwn).Runner);
        Assert.Equal("beta", Assert.Single(dto.OmittedRunners).Runner);
        var lifted = (await h.EventsAsync(issue.Key)).Where(e => e.Kind == EfHatchIssueEvent.RunnerOmissionLifted).ToList();
        Assert.Equal("alpha", Assert.Single(lifted).Payload!.Value.GetProperty("runner").GetString());
        Assert.Equal(Now.AddHours(1), dto.UpdatedAt);
    }

    [Fact]
    public async Task LiftingAnEscapedName_RemovesTheRowStoredUnderTheDecodedName()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");
        await h.OmitAsync(issue.Key, "host:%2Fcheckouts%2Fone", "x");

        await h.LiftAsync(issue.Key, "host:%2Fcheckouts%2Fone");

        Assert.Empty(await h.Db.RunnerOmissions.ToListAsync());
    }

    [Fact]
    public async Task LiftingAnOmissionOnlyAnAncestorCarries_Is404NamingTheAncestor_AndLeavesItsRow()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "a story");
        var child = Created(await h.Issues.CreateIssue(
            new IssueCreateRequest(h.ProjectId, "task", "a task", null, story.Key, null, null), default));
        await h.OmitAsync(story.Key, "alpha", "x");

        var refused = await h.Controller.DeleteOmittedRunner(child.Key, "alpha", default);

        var notFound = Assert.IsType<NotFoundObjectResult>(refused.Result);
        Assert.Contains(story.Key, (string)notFound.Value!);
        IssueKey.TryParse(story.Key, out var projectKey, out var number);
        var storyRow = await h.Db.Issues.WithKey(projectKey, number).FirstAsync();
        Assert.Equal(storyRow.Id, (await h.Db.RunnerOmissions.SingleAsync()).IssueId);
        Assert.Empty(await h.EventsAsync(child.Key));
    }

    [Fact]
    public async Task LiftingANameNobodyCarries_IsAPlain404()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");

        var refused = await h.Controller.DeleteOmittedRunner(issue.Key, "alpha", default);

        Assert.IsType<NotFoundResult>(refused.Result);
        Assert.Empty(await h.EventsAsync(issue.Key));
    }

    // ---- Projection ----

    [Fact]
    public async Task ATaskTwoLevelsDown_ReadsTheEpicsOmission_WithFromKey_AndTheEpicWinsWhenItOmitsTheSameName()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "an epic");
        var story = Created(await h.Issues.CreateIssue(
            new IssueCreateRequest(h.ProjectId, "story", "a story", null, epic.Key, null, null), default));
        var task = Created(await h.Issues.CreateIssue(
            new IssueCreateRequest(h.ProjectId, "task", "a task", null, story.Key, null, null), default));
        await h.OmitAsync(epic.Key, "alpha", "x");

        var inherited = await h.ReadAsync(task.Key);

        var carried = Assert.Single(inherited.OmittedRunners);
        Assert.Equal(("alpha", "x", epic.Key), (carried.Runner, carried.Reason, carried.FromKey));
        Assert.Empty(inherited.OmittedRunnersOwn);

        await h.OmitAsync(task.Key, "alpha", "y");

        var both = await h.ReadAsync(task.Key);
        var effective = Assert.Single(both.OmittedRunners);
        Assert.Equal(("alpha", "x", epic.Key), (effective.Runner, effective.Reason, effective.FromKey));
        var own = Assert.Single(both.OmittedRunnersOwn);
        Assert.Equal(("alpha", "y", (string?)null), (own.Runner, own.Reason, own.FromKey));
    }

    [Fact]
    public async Task AFreshTicket_AnswersBothListsAsEmptyArrays_NotNull_AndNotOmittedFromTheJson()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");

        var read = await h.ReadAsync(issue.Key);

        Assert.NotNull(read.OmittedRunnersOwn);
        Assert.Empty(read.OmittedRunnersOwn);
        Assert.NotNull(read.OmittedRunners);
        Assert.Empty(read.OmittedRunners);
        var json = System.Text.Json.JsonSerializer.SerializeToElement(
            read, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        foreach (var name in new[] { "omittedRunnersOwn", "omittedRunners" })
        {
            Assert.True(json.TryGetProperty(name, out var list), name);
            Assert.Equal(System.Text.Json.JsonValueKind.Array, list.ValueKind);
            Assert.Equal(0, list.GetArrayLength());
        }
    }

    // ---- Who may do it ----

    [Fact]
    public async Task AKeyWithNoPerson_MayDoBoth_AndIsTheActor()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");
        h.Caller.Person = null;
        h.Caller.Key = new EfApiKey { Name = "a key", Hash = [1], Prefix = "hatch_ak_x", CreatedAt = Now };

        await h.OmitAsync(issue.Key, "alpha", "x");
        var lifted = await h.LiftAsync(issue.Key, "alpha");

        Assert.Empty(lifted.OmittedRunnersOwn);
        var events = await h.EventsAsync(issue.Key);
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal("a key", e.Actor));
    }

    [Fact]
    public async Task APerson_IsTheActorOnTheirEvents()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");

        await h.OmitAsync(issue.Key, "alpha", "x");
        await h.LiftAsync(issue.Key, "alpha");

        Assert.All(await h.EventsAsync(issue.Key), e => Assert.Equal("a person", e.Actor));
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required StubCallerIdentity Caller { get; init; }
        public required IssueRunnerOmissionsController Controller { get; init; }
        public required IssuesController Issues { get; init; }
        public required IssueThreadController Thread { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }

        public async Task<IssueDto> FileAsync(string type, string title) =>
            Created(await Issues.CreateIssue(new IssueCreateRequest(ProjectId, type, title, null, null, null, null), default));

        public async Task<IssueDto> OmitAsync(string key, string runner, string? reason) =>
            Value(await Controller.PutOmittedRunner(key, runner, new OmitRunnerRequest(reason), default));

        public async Task<IssueDto> LiftAsync(string key, string runner) =>
            Value(await Controller.DeleteOmittedRunner(key, runner, default));

        public async Task<IssueDto> ReadAsync(string key) => Value(await Issues.GetIssue(key, default));

        /// <summary>The omissions' own events, oldest first.</summary>
        public async Task<IReadOnlyList<IssueEventDto>> EventsAsync(string key) =>
            Value(await Thread.GetEvents(key, default))
                .Where(e => e.Kind is EfHatchIssueEvent.RunnerOmitted or EfHatchIssueEvent.RunnerOmissionLifted)
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
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var caller = new StubCallerIdentity { Person = new EfPerson { Name = "a person", CreatedAt = Now, UpdatedAt = Now } };
        var actors = new StubActorDirectory();

        return new Harness
        {
            Db = db,
            Caller = caller,
            Controller = new IssueRunnerOmissionsController(db, actors, TestClaims.With(), caller, time),
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
}

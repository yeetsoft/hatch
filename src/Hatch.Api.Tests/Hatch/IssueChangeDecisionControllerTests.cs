using System.Reflection;
using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Models.Auth;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Api.Tests.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// A person's decision on an epic that serves nothing now: keep it running,
/// or shelve it with the open work under it.
/// </summary>
public class IssueChangeDecisionControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    // ---- Who may ----

    [Theory]
    [InlineData("shelve")]
    [InlineData("keep")]
    public async Task AKey_IsRefusedEitherDecision(string decision)
    {
        var method = typeof(IssueChangeDecisionController).GetMethod(nameof(IssueChangeDecisionController.PostDecision))!;
        var attribute = method.GetCustomAttribute<RequireRoleAttribute>()!;
        Assert.False(attribute.AcceptKey);

        var key = new EfApiKey
        {
            Id = Guid.NewGuid(), Name = "Claude", Prefix = "hatch_ak_abc", Hash = AuthTokens.Hash("hatch_ak_secret"), CreatedAt = Now,
        };
        var context = new DefaultHttpContext();
        context.SetApiKey(key);
        var options = Options.Create(new AuthOptions { Enabled = true });
        var caller = new CallerIdentity(
            new HttpContextAccessor { HttpContext = context }, new StubAuthService(null), options, new StubSiteSettings());
        var gate = new RoleGate(caller, options, NullLogger<RoleGate>.Instance);

        var verdict = await gate.EvaluateAsync(
            "POST", $"/api/issues/AER-1/changes/outcomes/1/decision?decision={decision}", null, attribute.Minimum, attribute.AcceptKey, default);

        Assert.False(verdict.IsAllowed);
        Assert.Equal(RoleDecision.KeyNotAccepted, verdict.Reason);
    }

    // ---- Refusals: each writes nothing ----

    [Fact]
    public async Task AnIdThatIsNotAnOutcomeOfThisAmbition_Is404()
    {
        var h = await NewAsync();
        var (ambition, _, outcome) = await h.ServesNothingAsync();
        var other = await h.FileAsync("ambition");

        Assert.IsType<NotFoundObjectResult>((await h.Controller.PostDecision(other, outcome.Id, new("keep"), default)).Result);
        Assert.IsType<NotFoundObjectResult>((await h.Controller.PostDecision(ambition, outcome.Id + 99, new("keep"), default)).Result);
        await h.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task ADecisionThatIsNeitherWord_Is400()
    {
        var h = await NewAsync();
        var (ambition, _, outcome) = await h.ServesNothingAsync();

        Assert.IsType<BadRequestObjectResult>((await h.Controller.PostDecision(ambition, outcome.Id, new("drop"), default)).Result);
        await h.AssertNothingWrittenAsync();
    }

    [Theory]
    [InlineData("carries_on")]
    [InlineData("not_touched")]
    public async Task AnOutcomeThatServesSomething_Is400(string word)
    {
        var h = await NewAsync();
        var (ambition, _, outcome) = await h.ServesNothingAsync(word);

        foreach (var decision in new[] { "keep", "shelve" })
            Assert.IsType<BadRequestObjectResult>((await h.Controller.PostDecision(ambition, outcome.Id, new(decision), default)).Result);

        await h.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task AnOutcomeAlreadyDecided_Is409_ForEitherDecision()
    {
        var h = await NewAsync();
        var (ambition, _, outcome) = await h.ServesNothingAsync();
        Ok(await h.Controller.PostDecision(ambition, outcome.Id, new("keep"), default));
        var written = await h.Db.IssueEvents.CountAsync();

        foreach (var decision in new[] { "keep", "shelve" })
            Assert.IsType<ConflictObjectResult>((await h.Controller.PostDecision(ambition, outcome.Id, new(decision), default)).Result);

        Assert.Equal(written, await h.Db.IssueEvents.CountAsync());
        Assert.Equal("keep", (await h.Db.ChangeOutcomes.SingleAsync()).Decision);
    }

    [Theory]
    [InlineData("done")]
    [InlineData("shelf")]
    public async Task ShelvingAnEpicNoLongerOpen_Is409(string column)
    {
        var h = await NewAsync();
        var (ambition, epic, outcome) = await h.ServesNothingAsync();
        await h.PutInAsync(epic, column == "done" ? h.Done : h.ShelfFirst);

        Assert.IsType<ConflictObjectResult>((await h.Controller.PostDecision(ambition, outcome.Id, new("shelve"), default)).Result);
        await h.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task ShelvingOnABoardWithNoDeferredColumn_Is400()
    {
        var h = await NewAsync(deferred: false);
        var (ambition, _, outcome) = await h.ServesNothingAsync();

        Assert.IsType<BadRequestObjectResult>((await h.Controller.PostDecision(ambition, outcome.Id, new("shelve"), default)).Result);
        await h.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task ShelvingIntoAProtectedColumnTheCallerMayNotMoveInto_Is403()
    {
        var h = await NewAsync(protectedShelf: true, mayMoveIntoProtected: false);
        var (ambition, epic, outcome) = await h.ServesNothingAsync();
        await h.FileAsync("story", epic);

        var forbidden = Assert.IsType<ObjectResult>((await h.Controller.PostDecision(ambition, outcome.Id, new("shelve"), default)).Result);

        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        await h.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task ShelvingIntoAProtectedColumnTheCallerMayMoveInto_Moves()
    {
        var h = await NewAsync(protectedShelf: true, mayMoveIntoProtected: true);
        var (ambition, epic, outcome) = await h.ServesNothingAsync();

        Ok(await h.Controller.PostDecision(ambition, outcome.Id, new("shelve"), default));

        Assert.Equal(h.ShelfFirst, (await h.IssueAsync(epic)).StatusId);
    }

    // ---- keep ----

    [Fact]
    public async Task Keep_RecordsTheDecisionAndMovesNothing()
    {
        var h = await NewAsync();
        var (ambition, epic, outcome) = await h.ServesNothingAsync();
        var story = await h.FileAsync("story", epic);
        await h.PutInAsync(epic, h.Doing);
        var storyColumn = (await h.IssueAsync(story)).StatusId;

        var dto = Ok(await h.Controller.PostDecision(ambition, outcome.Id, new("keep"), default));

        Assert.Equal("keep", dto.Decision!.Decision);
        Assert.Equal("a person", dto.Decision.DecidedBy);
        Assert.Equal(Now, dto.Decision.DecidedAt);
        Assert.Equal("doing", dto.EpicColumn);

        var row = await h.Db.ChangeOutcomes.SingleAsync();
        Assert.Equal(("keep", "a person", (DateTimeOffset?)Now), (row.Decision, row.DecidedBy, row.DecidedAt));
        Assert.Equal(h.Doing, (await h.IssueAsync(epic)).StatusId);
        Assert.Equal(storyColumn, (await h.IssueAsync(story)).StatusId);
        Assert.Equal(0, await h.Db.IssueEvents.CountAsync(e => e.Kind == EfHatchIssueEvent.StatusChanged));

        var decided = Assert.Single(await h.EventsAsync(epic, EfHatchIssueEvent.ChangeDecided));
        Assert.Equal(ambition, decided.GetProperty("ambition").GetString());
        Assert.Equal("keep", decided.GetProperty("decision").GetString());
    }

    // ---- shelve ----

    [Fact]
    public async Task Shelve_MovesTheEpicAndItsOpenWork_AndLeavesTheClosedAndTheDeferred()
    {
        var h = await NewAsync();
        var (ambition, epic, outcome) = await h.ServesNothingAsync();
        var openStory = await h.FileAsync("story", epic);
        var openTask = await h.FileAsync("task", openStory);
        var shelvedTask = await h.FileAsync("task", openStory);
        var doneStory = await h.FileAsync("story", epic);
        await h.PutInAsync(doneStory, h.Done);
        await h.PutInAsync(shelvedTask, h.ShelfSecond);
        await h.PutInAsync(epic, h.Doing);

        // Waits on the task from outside the epic.
        var elsewhere = await h.FileAsync("epic", ambition);
        var waiting = await h.FileAsync("story", elsewhere);
        h.Db.Dependencies.Add(new EfHatchIssueDependency
        {
            IssueId = (await h.IssueAsync(waiting)).Id, DependsOnId = (await h.IssueAsync(openTask)).Id,
            CreatedBy = "a person", CreatedAt = Now,
        });
        await h.Db.SaveChangesAsync();

        var dto = Ok(await h.Controller.PostDecision(ambition, outcome.Id, new("shelve"), default));

        Assert.Equal("shelf one", dto.EpicColumn);
        Assert.Equal("shelve", dto.Decision!.Decision);

        foreach (var moved in new[] { epic, openStory, openTask })
        {
            Assert.Equal(h.ShelfFirst, (await h.IssueAsync(moved)).StatusId);
            var change = Assert.Single(await h.EventsAsync(moved, EfHatchIssueEvent.StatusChanged));
            Assert.Equal(h.ShelfFirst, change.GetProperty("toId").GetInt32());
        }

        Assert.Equal(h.Done, (await h.IssueAsync(doneStory)).StatusId);
        Assert.Equal(h.ShelfSecond, (await h.IssueAsync(shelvedTask)).StatusId);
        Assert.Empty(await h.EventsAsync(doneStory, EfHatchIssueEvent.StatusChanged));
        Assert.Empty(await h.EventsAsync(shelvedTask, EfHatchIssueEvent.StatusChanged));
        Assert.Empty(await h.EventsAsync(waiting, EfHatchIssueEvent.StatusChanged));

        var waitingId = (await h.IssueAsync(waiting)).Id;
        var note = Assert.Single(await h.Db.Comments.Where(c => c.IssueId == waitingId).ToListAsync());
        Assert.Contains("shelf one", note.Body);

        var decided = Assert.Single(await h.EventsAsync(epic, EfHatchIssueEvent.ChangeDecided));
        Assert.Equal("shelve", decided.GetProperty("decision").GetString());

        var read = Assert.Single(Assert.Single(Value(await h.Changes.GetChanges(ambition, default))).Outcomes);
        Assert.Equal(outcome.Id, read.Id);
        Assert.Equal("shelve", read.Decision!.Decision);
        Assert.Equal("a person", read.Decision.DecidedBy);
        Assert.Equal(Now, read.Decision.DecidedAt);
    }

    // ---- Harness ----

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required IssueChangeDecisionController Controller { get; init; }
        public required IssueChangesController Changes { get; init; }
        public required IssueCapabilitiesController Capabilities { get; init; }
        public required IssuesController Issues { get; init; }
        public required int ProjectId { get; init; }
        public required int Doing { get; init; }
        public required int Done { get; init; }
        public required int ShelfFirst { get; init; }
        public required int ShelfSecond { get; init; }

        public async Task<string> FileAsync(string type, string? parentKey = null)
        {
            var result = await Issues.CreateIssue(new IssueCreateRequest(ProjectId, type, $"a {type}", null, parentKey, null, null), default);
            return result.Result is CreatedAtActionResult created
                ? ((IssueDto)created.Value!).Key
                : throw new InvalidOperationException($"expected a created issue, got {result.Result}");
        }

        public async Task<EfHatchIssue> IssueAsync(string key)
        {
            Assert.True(IssueKey.TryParse(key, out var projectKey, out var number));
            return await Db.Issues.WithKey(projectKey, number).SingleAsync();
        }

        public async Task PutInAsync(string key, int statusId)
        {
            var issue = await IssueAsync(key);
            issue.StatusId = statusId;
            await Db.SaveChangesAsync();
        }

        /// <summary>An ambition with an epic under it and an outcome recorded for the epic.</summary>
        public async Task<(string Ambition, string Epic, ChangeOutcomeDto Outcome)> ServesNothingAsync(string word = "serves_nothing")
        {
            var ambition = await FileAsync("ambition");
            var epic = await FileAsync("epic", ambition);
            Assert.IsType<CreatedAtActionResult>(
                (await Capabilities.PostCapability(ambition, new CapabilityCreateRequest("a", "a?"), default)).Result);

            var ambitionId = (await IssueAsync(ambition)).Id;
            var eventId = await Db.IssueEvents
                .Where(e => e.IssueId == ambitionId && e.Kind == EfHatchIssueEvent.CapabilityAdded)
                .Select(e => e.Id).SingleAsync();

            var written = await Changes.PostOutcome(ambition, eventId, new ChangeOutcomeRequest(epic, word, "because"), default);
            var dto = (ChangeOutcomeDto)((CreatedAtActionResult)written.Result!).Value!;
            return (ambition, epic, dto);
        }

        public async Task<List<JsonElement>> EventsAsync(string key, string kind)
        {
            var id = (await IssueAsync(key)).Id;
            var events = await Db.IssueEvents.Where(e => e.IssueId == id && e.Kind == kind).ToListAsync();
            return events.Select(e => JsonDocument.Parse(e.Payload!).RootElement.Clone()).ToList();
        }

        /// <summary>No decision on the row, no <c>change_decided</c>, no move and no note about a deferral.</summary>
        public async Task AssertNothingWrittenAsync()
        {
            Assert.All(await Db.ChangeOutcomes.ToListAsync(), o => Assert.Null(o.Decision));
            Assert.Equal(0, await Db.IssueEvents.CountAsync(e => e.Kind == EfHatchIssueEvent.ChangeDecided));
            Assert.Equal(0, await Db.IssueEvents.CountAsync(e => e.Kind == EfHatchIssueEvent.StatusChanged));
            Assert.Equal(0, await Db.Comments.CountAsync(c => c.Body.Contains("has been deferred")));
        }
    }

    private static async Task<Harness> NewAsync(bool deferred = true, bool protectedShelf = false, bool mayMoveIntoProtected = false)
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Add(project);
        var inbox = new EfHatchStatus { Name = "inbox", SortOrder = 10 };
        var doing = new EfHatchStatus { Name = "doing", SortOrder = 30, IsImplementation = true };
        var done = new EfHatchStatus { Name = "done", SortOrder = 50, IsTerminal = true };
        var shelfOne = new EfHatchStatus { Name = "shelf one", SortOrder = 60, IsDeferred = deferred, IsProtected = protectedShelf };
        var shelfTwo = new EfHatchStatus { Name = "shelf two", SortOrder = 70, IsDeferred = deferred };
        db.AddRange(inbox, doing, done, shelfOne, shelfTwo);
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var caller = new StubCallerIdentity { Person = new EfPerson { Name = "a person", CreatedAt = Now, UpdatedAt = Now } };
        var actors = new StubActorDirectory();
        var ranks = new RankService(db);

        return new Harness
        {
            Db = db,
            Controller = new IssueChangeDecisionController(db, ranks, actors, new StubAccess(mayMoveIntoProtected), caller, time),
            Changes = new IssueChangesController(db, caller, time),
            Capabilities = new IssueCapabilitiesController(db, caller, time),
            Issues = new IssuesController(db, ranks, actors, TestClaims.With(), caller, time, new ProjectAccess(db, actors)),
            ProjectId = project.Id,
            Doing = doing.Id,
            Done = done.Id,
            ShelfFirst = shelfOne.Id,
            ShelfSecond = shelfTwo.Id,
        };
    }

    private sealed class StubAccess(bool mayMoveIntoProtected) : IProjectAccess
    {
        public Task<bool> IsOwnerAsync(int projectId, CancellationToken ct) => Task.FromResult(mayMoveIntoProtected);

        public Task<bool> CanApproveAsync(int projectId, CancellationToken ct) => Task.FromResult(mayMoveIntoProtected);

        public Task<bool> CanMoveIntoAsync(int projectId, EfHatchStatus status, CancellationToken ct) =>
            Task.FromResult(!status.IsProtected || mayMoveIntoProtected);
    }

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

    private static T Ok<T>(ActionResult<T> result) =>
        result.Result is OkObjectResult ok
            ? (T)ok.Value!
            : throw new InvalidOperationException($"expected 200, got {result.Result}");
}

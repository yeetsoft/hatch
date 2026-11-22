using System.Reflection;
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
/// The waiting pull request an issue carries: written through its own route by
/// the caller, replaced, written only on a change, refused once a pull request
/// url is recorded, cleared by recording one through either route, and removed
/// by a person alone.
/// </summary>
public class IssueWaitingPullRequestControllerTests
{
    private const string Remote = "git@forge.example:owner/repo.git";
    private const string PullRequest = "https://forge.example/owner/repo/pull/7";
    private const string Sha = "2222222222222222222222222222222222222222";

    // ---- Writing it ----

    [Fact]
    public async Task AWrite_StoresTheRecord_NamesTheCallerAsRunner_AndWritesOneEvent()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var written = await h.WriteAsync(issue.Key);

        var waiting = Assert.IsType<WaitingPullRequestDto>(written.WaitingPullRequest);
        Assert.Equal("Area: what changed", waiting.Title);
        Assert.Equal("the body", waiting.Body);
        Assert.Equal("ha-1-thing", waiting.Branch);
        Assert.Equal(Sha, waiting.Sha);
        Assert.Equal(Remote, waiting.Remote);
        Assert.Equal("a person", waiting.Runner);
        Assert.Equal("the forge refused the push", waiting.Why);
        Assert.Equal(Now, waiting.At);

        var events = await h.EventsAsync(issue.Key);
        var e = Assert.Single(events);
        Assert.Equal(EfHatchIssueEvent.WaitingPullRequestChanged, e.Kind);
        Assert.Equal("a person", e.Actor);
        Assert.Equal(JsonValueKind.Null, Payload(e).GetProperty("from").ValueKind);
        var to = Payload(e).GetProperty("to");
        Assert.Equal("Area: what changed", to.GetProperty("title").GetString());
        Assert.Equal("ha-1-thing", to.GetProperty("branch").GetString());
        Assert.Equal(Sha, to.GetProperty("sha").GetString());
    }

    [Fact]
    public async Task TheCallerIsTheActor_WhenItIsAKey()
    {
        var h = await NewAsync();
        h.Caller.Person = null;
        h.Caller.Key = new EfApiKey { Name = "a runner key", Prefix = "hatch_ak_x", Hash = [1], CreatedAt = Now };
        var issue = await h.FileAsync();

        var written = await h.WriteAsync(issue.Key);

        Assert.Equal("a runner key", written.WaitingPullRequest!.Runner);
        Assert.Equal("a runner key", Assert.Single(await h.EventsAsync(issue.Key)).Actor);
    }

    [Fact]
    public async Task TheValuesAreTrimmed_AndAnAbsentBodyAndRemoteAreAllowed()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var written = Value(await h.Controller.PutWaitingPullRequest(
            issue.Key, new WaitingPullRequestRequest("  t  ", null, "  b  ", "  " + Sha + " ", "  ", "  w  "), default));

        var waiting = written.WaitingPullRequest!;
        Assert.Equal("t", waiting.Title);
        Assert.Equal("", waiting.Body);
        Assert.Equal("b", waiting.Branch);
        Assert.Equal(Sha, waiting.Sha);
        Assert.Null(waiting.Remote);
        Assert.Equal("w", waiting.Why);
    }

    [Fact]
    public async Task AnIdenticalWrite_WritesNoEvent_AndDoesNotMoveUpdatedAt()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.WriteAsync(issue.Key);
        var before = (await h.ReadAsync(issue.Key)).UpdatedAt;

        h.Time.Advance(TimeSpan.FromHours(1));
        var again = await h.WriteAsync(issue.Key);

        Assert.Equal(Now, again.WaitingPullRequest!.At);
        Assert.Equal(before, (await h.ReadAsync(issue.Key)).UpdatedAt);
        Assert.Single(await h.EventsAsync(issue.Key));
    }

    [Fact]
    public async Task ASecondWrite_ReplacesTheFirst_AndTheEventCarriesBothSides()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.WriteAsync(issue.Key);

        h.Time.Advance(TimeSpan.FromHours(1));
        var second = await h.WriteAsync(issue.Key, title: "Area: a better title", branch: "ha-1-other");

        Assert.Equal("Area: a better title", second.WaitingPullRequest!.Title);
        Assert.Equal(Now.AddHours(1), second.WaitingPullRequest.At);
        var events = await h.EventsAsync(issue.Key);
        Assert.Equal(2, events.Count);
        Assert.Equal("Area: what changed", Payload(events[1]).GetProperty("from").GetProperty("title").GetString());
        Assert.Equal("ha-1-thing", Payload(events[1]).GetProperty("from").GetProperty("branch").GetString());
        Assert.Equal("ha-1-other", Payload(events[1]).GetProperty("to").GetProperty("branch").GetString());
    }

    [Fact]
    public async Task TheIssueRead_CarriesIt_AndANullWhereThereIsNone()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        Assert.Null((await h.ReadAsync(issue.Key)).WaitingPullRequest);

        await h.WriteAsync(issue.Key);

        var read = await h.ReadAsync(issue.Key);
        Assert.Equal("ha-1-thing", read.WaitingPullRequest!.Branch);
        Assert.Equal("a person", read.WaitingPullRequest.Runner);
    }

    // ---- Refusals ----

    [Fact]
    public async Task AnUnknownKey_IsNotFound_ForBothActions()
    {
        var h = await NewAsync();

        var put = await h.Controller.PutWaitingPullRequest("AER-404", Request(), default);
        var delete = await h.Controller.DeleteWaitingPullRequest("AER-404", default);

        Assert.IsType<NotFoundResult>(put.Result);
        Assert.IsType<NotFoundResult>(delete);
    }

    [Theory]
    [InlineData("title", "title is required")]
    [InlineData("branch", "branch is required")]
    [InlineData("sha", "sha is required")]
    [InlineData("why", "why is required - say why it could not be opened where it was written")]
    public async Task ABlankRequiredField_IsRefusedWithASentence(string field, string sentence)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        var request = field switch
        {
            "title" => Request() with { Title = "  " },
            "branch" => Request() with { Branch = null },
            "sha" => Request() with { Sha = "" },
            _ => Request() with { Why = " " },
        };

        var refused = await h.Controller.PutWaitingPullRequest(issue.Key, request, default);

        var bad = Assert.IsType<BadRequestObjectResult>(refused.Result);
        Assert.Equal(sentence, bad.Value);
        Assert.Null((await h.ReadAsync(issue.Key)).WaitingPullRequest);
    }

    [Fact]
    public async Task ATitleOver256_AWhyOver500_AndABodyOverTheGitHubLimit_AreRefused()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var title = await h.Controller.PutWaitingPullRequest(issue.Key, Request() with { Title = new string('x', 257) }, default);
        var why = await h.Controller.PutWaitingPullRequest(issue.Key, Request() with { Why = new string('x', 501) }, default);
        var body = await h.Controller.PutWaitingPullRequest(
            issue.Key, Request() with { Body = new string('x', PullRequestEnvelopeWriter.GitHubBodyLimit + 1) }, default);

        Assert.Equal("a pull request title is at most 256 characters", Assert.IsType<BadRequestObjectResult>(title.Result).Value);
        Assert.Equal("why is at most 500 characters", Assert.IsType<BadRequestObjectResult>(why.Result).Value);
        Assert.Equal(
            $"a pull request body is at most {PullRequestEnvelopeWriter.GitHubBodyLimit} characters",
            Assert.IsType<BadRequestObjectResult>(body.Result).Value);

        // The limits themselves are allowed.
        var atLimit = Request() with
        {
            Title = new string('x', 256), Why = new string('x', 500), Body = new string('x', PullRequestEnvelopeWriter.GitHubBodyLimit),
        };
        Assert.NotNull(Value(await h.Controller.PutWaitingPullRequest(issue.Key, atLimit, default)).WaitingPullRequest);
    }

    [Fact]
    public async Task WithAPullRequestRecorded_ARecordIsRefused()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PatchPullRequestAsync(issue.Key);

        var refused = await h.Controller.PutWaitingPullRequest(issue.Key, Request(), default);

        var conflict = Assert.IsType<ConflictObjectResult>(refused.Result);
        Assert.Equal($"{issue.Key} has a pull request recorded - there is none waiting to be opened", conflict.Value);
        Assert.Null((await h.ReadAsync(issue.Key)).WaitingPullRequest);
    }

    // ---- Cleared by recording a url ----

    [Fact]
    public async Task RecordingAUrlThroughPatch_ClearsIt_InTheSameSave_WithAnEventNamingTheUrl()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.WriteAsync(issue.Key);

        await h.PatchPullRequestAsync(issue.Key);

        var read = await h.ReadAsync(issue.Key);
        Assert.Equal(PullRequest, read.PullRequestUrl);
        Assert.Null(read.WaitingPullRequest);
        var cleared = (await h.EventsAsync(issue.Key)).Last();
        Assert.Equal(EfHatchIssueEvent.WaitingPullRequestChanged, cleared.Kind);
        Assert.Equal("ha-1-thing", Payload(cleared).GetProperty("from").GetProperty("branch").GetString());
        Assert.Equal(JsonValueKind.Null, Payload(cleared).GetProperty("to").ValueKind);
        Assert.Equal(PullRequest, Payload(cleared).GetProperty("pullRequest").GetString());
        Assert.Equal(2, (await h.EventsAsync(issue.Key)).Count);
    }

    [Fact]
    public async Task RecordingAUrlThroughTheContributionRoute_ClearsIt_WithAnEvent()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.WriteAsync(issue.Key);

        await h.RecordThroughContributionAsync(issue.Key);

        var read = await h.ReadAsync(issue.Key);
        Assert.Equal(PullRequest, read.PullRequestUrl);
        Assert.Null(read.WaitingPullRequest);
        var cleared = (await h.EventsAsync(issue.Key)).Last();
        Assert.Equal(EfHatchIssueEvent.WaitingPullRequestChanged, cleared.Kind);
        Assert.Equal(PullRequest, Payload(cleared).GetProperty("pullRequest").GetString());
    }

    [Fact]
    public async Task RecordingAUrl_OnAnIssueWithNothingWaiting_AddsNoWaitingEvent()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.PatchPullRequestAsync(issue.Key);

        Assert.Empty(await h.EventsAsync(issue.Key));
    }

    [Fact]
    public async Task AMoveLeftOfReview_LeavesItInPlace()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.WriteAsync(issue.Key);

        await h.MoveAsync(issue.Key, h.Review);
        await h.MoveAsync(issue.Key, h.Todo);

        Assert.NotNull((await h.ReadAsync(issue.Key)).WaitingPullRequest);
    }

    // ---- Removing it ----

    [Fact]
    public async Task APersonsDelete_RemovesIt_AndWritesOneEvent()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.WriteAsync(issue.Key);

        var removed = await h.Controller.DeleteWaitingPullRequest(issue.Key, default);

        Assert.IsType<NoContentResult>(removed);
        Assert.Null((await h.ReadAsync(issue.Key)).WaitingPullRequest);
        var events = await h.EventsAsync(issue.Key);
        Assert.Equal(2, events.Count);
        Assert.Equal("ha-1-thing", Payload(events[1]).GetProperty("from").GetProperty("branch").GetString());
        Assert.Equal(JsonValueKind.Null, Payload(events[1]).GetProperty("to").ValueKind);
        Assert.Equal("a person", events[1].Actor);
    }

    [Fact]
    public async Task DeletingWhereNothingWaits_IsNoContent_WithNoEvent()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var removed = await h.Controller.DeleteWaitingPullRequest(issue.Key, default);

        Assert.IsType<NoContentResult>(removed);
        Assert.Empty(await h.EventsAsync(issue.Key));
    }

    [Fact]
    public void TheWriteAcceptsAKey_AndTheDeleteDoesNot()
    {
        static RequireRoleAttribute Role(string method) =>
            typeof(IssueWaitingPullRequestController).GetMethod(method)!.GetCustomAttribute<RequireRoleAttribute>()!;

        Assert.True(Role(nameof(IssueWaitingPullRequestController.PutWaitingPullRequest)).AcceptKey);
        Assert.False(Role(nameof(IssueWaitingPullRequestController.DeleteWaitingPullRequest)).AcceptKey);
    }

    // ---- The seams ----

    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    private static JsonElement Payload(IssueEventDto e) => e.Payload!.Value;

    private static WaitingPullRequestRequest Request() =>
        new("Area: what changed", "the body", "ha-1-thing", Sha, Remote, "the forge refused the push");

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Add(project);
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 10, IsImplementation = true };
        var review = new EfHatchStatus { Name = "review", SortOrder = 20 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 30, IsTerminal = true };
        db.AddRange(todo, review, done);
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var caller = new StubCallerIdentity { Person = new EfPerson { Name = "a person", CreatedAt = Now, UpdatedAt = Now } };
        var actors = new StubActorDirectory();

        return new Harness
        {
            Db = db,
            Caller = caller,
            Controller = new IssueWaitingPullRequestController(db, actors, TestClaims.With(), caller, time),
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time, new ProjectAccess(db, actors)),
            Contribution = new IssueContributionController(db, actors, TestClaims.With(), caller, time),
            Thread = new IssueThreadController(db, caller, time),
            Time = time,
            ProjectId = project.Id,
            Todo = todo.Id,
            Review = review.Id,
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

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required StubCallerIdentity Caller { get; init; }
        public required IssueWaitingPullRequestController Controller { get; init; }
        public required IssuesController Issues { get; init; }
        public required IssueContributionController Contribution { get; init; }
        public required IssueThreadController Thread { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }
        public required int Todo { get; init; }
        public required int Review { get; init; }

        public async Task<IssueDto> FileAsync() =>
            Created(await Issues.CreateIssue(new IssueCreateRequest(ProjectId, "task", "a thing", null, null, null, null), default));

        public async Task<IssueDto> WriteAsync(string key, string? title = null, string? branch = null) =>
            Value(await Controller.PutWaitingPullRequest(
                key, Request() with { Title = title ?? Request().Title, Branch = branch ?? Request().Branch }, default));

        public async Task<IssueDto> ReadAsync(string key)
        {
            Db.ChangeTracker.Clear();
            return Value(await Issues.GetIssue(key, default));
        }

        public async Task PatchPullRequestAsync(string key)
        {
            Value(await Issues.PatchIssue(
                key, new IssuePatchRequest(null, null, null, null, null, null, null, PullRequest, null, null), default));
            Db.ChangeTracker.Clear();
        }

        public async Task MoveAsync(string key, int statusId)
        {
            Value(await Issues.MoveIssue(key, new IssueMoveRequest(statusId, null, null), default));
            Db.ChangeTracker.Clear();
        }

        /// <summary>Every waiting-pull-request event, oldest first.</summary>
        public async Task<IReadOnlyList<IssueEventDto>> EventsAsync(string key)
        {
            Db.ChangeTracker.Clear();
            return Value(await Thread.GetEvents(key, default))
                .Where(e => e.Kind == EfHatchIssueEvent.WaitingPullRequestChanged)
                .Reverse()
                .ToList();
        }

        public async Task RecordThroughContributionAsync(string key)
        {
            Value(await Contribution.PutIssueContribution(
                key, new ContributionRequest(PullRequest, "ha-1-thing", "an-author", "MEMBER"), default));
            Db.ChangeTracker.Clear();
        }
    }
}

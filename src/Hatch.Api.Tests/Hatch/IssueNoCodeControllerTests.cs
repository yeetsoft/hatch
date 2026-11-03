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
/// The no-code declaration an issue carries: set through its own route,
/// trimmed and capped, written only on a change, refused once a branch or a
/// pull request exists, and set aside by either, or by a move to a column left
/// of review.
/// </summary>
public class IssueNoCodeControllerTests
{
    private const string Remote = "git@forge.example:owner/repo.git";
    private const string Canonical = "forge.example/owner/repo";
    private const string PullRequest = "https://forge.example/owner/repo/pull/7";

    // ---- Setting it ----

    [Fact]
    public async Task ASetReason_IsTrimmed_AndWritesOneEvent()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var set = await h.SetAsync(issue.Key, "  nothing to change  ");

        Assert.Equal("nothing to change", set.NoCode);
        Assert.Equal(Now, set.NoCodeAt);
        var written = Assert.Single(await h.EventsAsync(issue.Key));
        Assert.Equal(EfHatchIssueEvent.NoCodeChanged, written.Kind);
        Assert.Null(Payload(written).GetProperty("from").GetString());
        Assert.Equal("nothing to change", Payload(written).GetProperty("to").GetString());
    }

    [Fact]
    public async Task TheSameReasonTwice_WritesOneEvent_AndKeepsTheFirstTime()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.SetAsync(issue.Key, "nothing to change");

        h.Time.Advance(TimeSpan.FromHours(1));
        var again = await h.SetAsync(issue.Key, "nothing to change");

        Assert.Equal(Now, again.NoCodeAt);
        Assert.Single(await h.EventsAsync(issue.Key));
    }

    [Fact]
    public async Task AnEmptyReason_Clears_AndWritesAnEvent()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.SetAsync(issue.Key, "nothing to change");

        var cleared = await h.SetAsync(issue.Key, "");

        Assert.Null(cleared.NoCode);
        Assert.Null(cleared.NoCodeAt);
        var events = await h.EventsAsync(issue.Key);
        Assert.Equal(2, events.Count);
        Assert.Equal("nothing to change", Payload(events[1]).GetProperty("from").GetString());
        Assert.Null(Payload(events[1]).GetProperty("to").GetString());
    }

    [Fact]
    public async Task ANullReason_IsRefused()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var refused = await h.Controller.PutIssueNoCode(issue.Key, new NoCodeRequest(null), default);

        var bad = Assert.IsType<BadRequestObjectResult>(refused.Result);
        Assert.Equal("reason is required - send \"\" to clear", bad.Value);
    }

    [Fact]
    public async Task AReasonOver200Characters_IsRefused_AndTheCapIsAfterTheTrim()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var refused = await h.Controller.PutIssueNoCode(issue.Key, new NoCodeRequest(new string('x', 201)), default);

        var bad = Assert.IsType<BadRequestObjectResult>(refused.Result);
        Assert.Equal("a no-code reason is at most 200 characters", bad.Value);
        Assert.Equal(200, (await h.SetAsync(issue.Key, "  " + new string('x', 200) + "  ")).NoCode!.Length);
    }

    [Fact]
    public async Task AnUnknownKey_IsNotFound()
    {
        var h = await NewAsync();

        var refused = await h.Controller.PutIssueNoCode("AER-404", new NoCodeRequest("x"), default);

        Assert.IsType<NotFoundResult>(refused.Result);
    }

    // ---- Refused once there is a branch ----

    [Fact]
    public async Task WithAPullRequestRecorded_IsRefused()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PatchPullRequestAsync(issue.Key);

        var refused = await h.Controller.PutIssueNoCode(issue.Key, new NoCodeRequest("nothing to change"), default);

        var conflict = Assert.IsType<ConflictObjectResult>(refused.Result);
        Assert.Equal($"{issue.Key} has a pull request recorded - it did not finish with no code", conflict.Value);
        Assert.Null((await h.ReadAsync(issue.Key)).NoCode);
    }

    [Theory]
    [InlineData(MergeVerdicts.Clean)]
    [InlineData(MergeVerdicts.Conflicted)]
    [InlineData(MergeVerdicts.Ambiguous)]
    public async Task WithACountedBranchFindingCheck_IsRefused(string verdict)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutCheckAsync(issue.Key, verdict);

        var refused = await h.Controller.PutIssueNoCode(issue.Key, new NoCodeRequest("nothing to change"), default);

        var conflict = Assert.IsType<ConflictObjectResult>(refused.Result);
        Assert.Equal(
            $"{issue.Key} has a branch on origin ({Canonical}, {verdict}) - push it and record a pull request instead",
            conflict.Value);
    }

    [Fact]
    public async Task WithOnlyNoneChecks_IsAllowed()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutCheckAsync(issue.Key, MergeVerdicts.None);

        Assert.Equal("nothing to change", (await h.SetAsync(issue.Key, "nothing to change")).NoCode);
    }

    [Fact]
    public async Task WithACleanCheckOnARepositoryTheProjectNoLongerBinds_IsAllowed()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutCheckAsync(issue.Key, MergeVerdicts.Clean);
        await h.BindAsync("git@forge.example:owner/other.git");

        Assert.Equal("nothing to change", (await h.SetAsync(issue.Key, "nothing to change")).NoCode);
    }

    [Fact]
    public async Task ClearingIsNeverRefused_EvenWithABranch()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.SetAsync(issue.Key, "nothing to change");
        await h.PatchPullRequestAsync(issue.Key);

        Assert.Null((await h.SetAsync(issue.Key, "")).NoCode);
    }

    // ---- Set aside by a branch ----

    [Fact]
    public async Task ACleanMergeCheckAfterTheDeclaration_ClearsIt_NamingTheRemote()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.SetAsync(issue.Key, "nothing to change");

        await h.PutCheckAsync(issue.Key, MergeVerdicts.Clean);

        Assert.Null((await h.ReadAsync(issue.Key)).NoCode);
        var aside = Assert.Single(await h.EventsAsync(issue.Key, EfHatchIssueEvent.NoCodeSetAside));
        Assert.Equal("nothing to change", Payload(aside).GetProperty("reason").GetString());
        Assert.Equal(Canonical, Payload(aside).GetProperty("remote").GetString());
        Assert.Equal(MergeVerdicts.Clean, Payload(aside).GetProperty("verdict").GetString());
    }

    [Fact]
    public async Task ANoneMergeCheckAfterTheDeclaration_LeavesIt()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.SetAsync(issue.Key, "nothing to change");

        await h.PutCheckAsync(issue.Key, MergeVerdicts.None);

        Assert.Equal("nothing to change", (await h.ReadAsync(issue.Key)).NoCode);
        Assert.Empty(await h.EventsAsync(issue.Key, EfHatchIssueEvent.NoCodeSetAside));
    }

    [Fact]
    public async Task ACleanCheckOnARepositoryTheProjectDoesNotBind_LeavesIt()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.BindAsync("git@forge.example:owner/other.git");
        await h.SetAsync(issue.Key, "nothing to change");

        await h.PutCheckAsync(issue.Key, MergeVerdicts.Clean);

        Assert.Equal("nothing to change", (await h.ReadAsync(issue.Key)).NoCode);
    }

    [Fact]
    public async Task RecordingAPullRequest_ClearsIt_NamingTheUrl()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.SetAsync(issue.Key, "nothing to change");

        await h.PatchPullRequestAsync(issue.Key);

        var after = await h.ReadAsync(issue.Key);
        Assert.Null(after.NoCode);
        Assert.Null(after.NoCodeAt);
        var aside = Assert.Single(await h.EventsAsync(issue.Key, EfHatchIssueEvent.NoCodeSetAside));
        Assert.Equal("nothing to change", Payload(aside).GetProperty("reason").GetString());
        Assert.Equal(PullRequest, Payload(aside).GetProperty("pullRequest").GetString());
    }

    // ---- Cleared by a move ----

    [Fact]
    public async Task AMoveFromReviewToImplementation_Clears_ThroughMoveAndThroughPatch()
    {
        foreach (var viaPatch in new[] { false, true })
        {
            var h = await NewAsync();
            var issue = await h.FileAsync();
            await h.MoveAsync(issue.Key, h.Review);
            await h.SetAsync(issue.Key, "nothing to change");

            await h.MoveAsync(issue.Key, h.Todo, viaPatch);

            Assert.Null((await h.ReadAsync(issue.Key)).NoCode);
            var moved = Assert.Single(await h.EventsAsync(issue.Key, EfHatchIssueEvent.NoCodeChanged), e => Payload(e).TryGetProperty("moved", out _));
            Assert.Equal("nothing to change", Payload(moved).GetProperty("from").GetString());
            Assert.Null(Payload(moved).GetProperty("to").GetString());
            Assert.Equal("todo", Payload(moved).GetProperty("moved").GetString());
        }
    }

    [Fact]
    public async Task AMoveFromReviewToDone_Keeps_ThroughMoveAndThroughPatch()
    {
        foreach (var viaPatch in new[] { false, true })
        {
            var h = await NewAsync();
            var issue = await h.FileAsync();
            await h.MoveAsync(issue.Key, h.Review);
            await h.SetAsync(issue.Key, "nothing to change");

            await h.MoveAsync(issue.Key, h.Done, viaPatch);

            Assert.Equal("nothing to change", (await h.ReadAsync(issue.Key)).NoCode);
        }
    }

    [Fact]
    public async Task AMoveWithinReview_Keeps()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.MoveAsync(issue.Key, h.Review);
        await h.SetAsync(issue.Key, "nothing to change");

        await h.MoveAsync(issue.Key, h.Review);

        Assert.Equal("nothing to change", (await h.ReadAsync(issue.Key)).NoCode);
    }

    private static JsonElement Payload(IssueEventDto e) => e.Payload!.Value;

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required IssueNoCodeController Controller { get; init; }
        public required IssuesController Issues { get; init; }
        public required MergeCheckController Merge { get; init; }
        public required IssueThreadController Thread { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }
        public required int Todo { get; init; }
        public required int Review { get; init; }
        public required int Done { get; init; }

        public async Task<IssueDto> FileAsync() =>
            Created(await Issues.CreateIssue(new IssueCreateRequest(ProjectId, "task", "a thing", null, null, null, null), default));

        public async Task<IssueDto> SetAsync(string key, string reason) =>
            Value(await Controller.PutIssueNoCode(key, new NoCodeRequest(reason), default));

        public async Task<IssueDto> ReadAsync(string key)
        {
            Db.ChangeTracker.Clear();
            return Value(await Issues.GetIssue(key, default));
        }

        public async Task PutCheckAsync(string key, string verdict)
        {
            var branch = verdict is MergeVerdicts.Clean or MergeVerdicts.Conflicted;
            Value(await Merge.PutMergeCheck(
                key,
                new MergeCheckRequest(
                    Remote, "main", "1111111111111111111111111111111111111111", verdict,
                    branch ? "ha-1-thing" : null, branch ? "2222222222222222222222222222222222222222" : null,
                    verdict == MergeVerdicts.Conflicted ? ["a.cs"] : null, "box:/work/repo"),
                default));
            Db.ChangeTracker.Clear();
        }

        public async Task PatchPullRequestAsync(string key)
        {
            Value(await Issues.PatchIssue(
                key, new IssuePatchRequest(null, null, null, null, null, null, null, PullRequest, null, null), default));
            Db.ChangeTracker.Clear();
        }

        public async Task MoveAsync(string key, int statusId, bool viaPatch = false)
        {
            if (viaPatch)
                Value(await Issues.PatchIssue(
                    key, new IssuePatchRequest(null, null, null, statusId, null, null, null, null, null, null), default));
            else
                Value(await Issues.MoveIssue(key, new IssueMoveRequest(statusId, null, null), default));
            Db.ChangeTracker.Clear();
        }

        /// <summary>Bind a repository the checks above are not about.</summary>
        public async Task BindAsync(string remote)
        {
            var (canonical, _) = RemoteIdentity.Canonical(remote);
            Db.ProjectRepositories.Add(new EfHatchProjectRepository
            {
                ProjectId = ProjectId, Remote = remote, Canonical = canonical!, SortOrder = 1, CreatedAt = Now,
            });
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        /// <summary>The events of <paramref name="kind"/>, oldest first - both kinds if none is named.</summary>
        public async Task<IReadOnlyList<IssueEventDto>> EventsAsync(string key, string? kind = null)
        {
            Db.ChangeTracker.Clear();
            return Value(await Thread.GetEvents(key, default))
                .Where(e => kind is null ? e.Kind is EfHatchIssueEvent.NoCodeChanged or EfHatchIssueEvent.NoCodeSetAside : e.Kind == kind)
                .Reverse()
                .ToList();
        }
    }

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
            Controller = new IssueNoCodeController(db, actors, TestClaims.With(), caller, time),
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time, new ProjectAccess(db, actors)),
            Merge = new MergeCheckController(db, caller, time),
            Thread = new IssueThreadController(db, caller, time),
            Time = time,
            ProjectId = project.Id,
            Todo = todo.Id,
            Review = review.Id,
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

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result}");

    private static T Created<T>(ActionResult<T> result) =>
        result.Result is CreatedAtActionResult created
            ? (T)created.Value!
            : result.Value ?? throw new InvalidOperationException($"expected a created value, got {result.Result}");
}

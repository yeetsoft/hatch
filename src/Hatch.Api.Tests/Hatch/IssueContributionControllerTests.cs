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
/// The contribution record an issue carries: written once through its own
/// route by a key or a person, unique per pull request, setting the issue's own
/// pull request where it has none, carried on the issue and its card, and found
/// by the filters on the issue list.
/// </summary>
public class IssueContributionControllerTests
{
    private const string PullRequest = "https://forge.example/owner/repo/pull/7";
    private const string OtherPullRequest = "https://forge.example/owner/repo/pull/8";
    private const string Instance = "https://hatch.example";

    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private static ContributionRequest Record(
        string? url = PullRequest, string? branch = "feature/thing", string? login = "octocat",
        string? association = "FIRST_TIME_CONTRIBUTOR", string? instance = null, string? project = null,
        string? originKey = null, int? version = null, string? note = null) =>
        new(url, branch, login, association, instance, project, originKey, version, note);

    // ---- Writing it ----

    [Fact]
    public async Task AKey_Writes_TheRecord_AndTheEventNamesItsRunner()
    {
        var h = await NewAsync(asKey: true);
        var issue = await h.FileAsync();

        var written = await h.PutAsync(issue.Key, Record(instance: Instance + "/", project: "OTH", originKey: "OTH-4", version: 1));

        var c = written.Contribution!;
        Assert.Equal(PullRequest, c.PullRequestUrl);
        Assert.Equal("feature/thing", c.HeadBranch);
        Assert.Equal("octocat", c.AuthorLogin);
        Assert.Equal("FIRST_TIME_CONTRIBUTOR", c.AuthorAssociation);
        Assert.Equal(Now, c.FiledAt);
        Assert.Equal("runner-1", c.FiledBy);
        Assert.Equal(Instance, c.OriginInstance);
        Assert.Equal("OTH", c.OriginProjectKey);
        Assert.Equal("OTH-4", c.OriginKey);
        Assert.Equal(1, c.EnvelopeVersion);

        var filed = Assert.Single(await h.EventsAsync(issue.Key, EfHatchIssueEvent.ContributionFiled));
        Assert.Equal("runner-1", filed.Actor);
        var payload = filed.Payload!.Value;
        Assert.Equal(PullRequest, payload.GetProperty("pullRequestUrl").GetString());
        Assert.Equal("feature/thing", payload.GetProperty("headBranch").GetString());
        Assert.Equal("octocat", payload.GetProperty("authorLogin").GetString());
        Assert.Equal("FIRST_TIME_CONTRIBUTOR", payload.GetProperty("authorAssociation").GetString());
        Assert.Equal(Instance, payload.GetProperty("originInstance").GetString());
        Assert.Equal("OTH", payload.GetProperty("originProjectKey").GetString());
        Assert.Equal("OTH-4", payload.GetProperty("originKey").GetString());
    }

    [Fact]
    public async Task APerson_Writes_TheRecord_AndIsWhoFiledIt()
    {
        var h = await NewAsync(asKey: false);
        var issue = await h.FileAsync();

        var written = await h.PutAsync(issue.Key, Record());

        Assert.Equal("a person", written.Contribution!.FiledBy);
        Assert.Equal("a person", Assert.Single(await h.EventsAsync(issue.Key, EfHatchIssueEvent.ContributionFiled)).Actor);
    }

    [Fact]
    public async Task WritingTheSameRecordAgain_ChangesNothing_AndLeavesOneEvent()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue.Key, Record());

        h.Time.Advance(TimeSpan.FromHours(1));
        var again = await h.PutAsync(issue.Key, Record(branch: "another", login: "someone-else"));

        Assert.Equal(Now, again.Contribution!.FiledAt);
        Assert.Equal("feature/thing", again.Contribution.HeadBranch);
        Assert.Equal("octocat", again.Contribution.AuthorLogin);
        Assert.Single(await h.EventsAsync(issue.Key, EfHatchIssueEvent.ContributionFiled));
        Assert.Equal(1, await h.Db.Contributions.CountAsync());
    }

    [Fact]
    public async Task AMirroredAncestor_NeedsNoPullRequest_AndSetsNone()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var written = await h.PutAsync(issue.Key, Record(url: null, branch: null, instance: Instance, originKey: "OTH-4"));

        Assert.Null(written.Contribution!.PullRequestUrl);
        Assert.Null(written.PullRequestUrl);
        Assert.Empty(await h.EventsAsync(issue.Key, EfHatchIssueEvent.PullRequestChanged));
        var again = await h.PutAsync(issue.Key, Record(url: null, branch: null, instance: Instance, originKey: "OTH-4"));
        Assert.Equal(Now, again.Contribution!.FiledAt);
    }

    // ---- Refusals ----

    [Fact]
    public async Task ADifferentUrlOnAnIssueWithARecord_IsRefused()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue.Key, Record());

        var refused = await h.Controller.PutIssueContribution(issue.Key, Record(url: OtherPullRequest), default);

        var conflict = Assert.IsType<ConflictObjectResult>(refused.Result);
        Assert.Contains(PullRequest, (string)conflict.Value!);
        Assert.Equal(PullRequest, (await h.ReadAsync(issue.Key)).Contribution!.PullRequestUrl);
    }

    [Fact]
    public async Task AUrlAnotherIssueHolds_IsRefused_NamingThatIssue()
    {
        var h = await NewAsync();
        var first = await h.FileAsync();
        var second = await h.FileAsync();
        await h.PutAsync(first.Key, Record());

        var refused = await h.Controller.PutIssueContribution(second.Key, Record(), default);

        var conflict = Assert.IsType<ConflictObjectResult>(refused.Result);
        Assert.Contains(first.Key, (string)conflict.Value!);
        var read = await h.ReadAsync(second.Key);
        Assert.Null(read.Contribution);
        Assert.Null(read.PullRequestUrl);
        Assert.Empty(await h.EventsAsync(second.Key, EfHatchIssueEvent.ContributionFiled));
    }

    [Fact]
    public async Task AnIssueHoldingADifferentPullRequest_IsRefused_AndNothingIsWritten()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PatchPullRequestAsync(issue.Key, OtherPullRequest);

        var refused = await h.Controller.PutIssueContribution(issue.Key, Record(), default);

        var conflict = Assert.IsType<ConflictObjectResult>(refused.Result);
        Assert.Contains(OtherPullRequest, (string)conflict.Value!);
        var read = await h.ReadAsync(issue.Key);
        Assert.Null(read.Contribution);
        Assert.Equal(OtherPullRequest, read.PullRequestUrl);
        Assert.Empty(await h.EventsAsync(issue.Key, EfHatchIssueEvent.ContributionFiled));
        Assert.Equal(0, await h.Db.Contributions.CountAsync());
    }

    [Fact]
    public async Task AnIssueHoldingTheSamePullRequest_IsWritten_WithNoSecondPullRequestEvent()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PatchPullRequestAsync(issue.Key, PullRequest);

        var written = await h.PutAsync(issue.Key, Record());

        Assert.Equal(PullRequest, written.Contribution!.PullRequestUrl);
        Assert.Single(await h.EventsAsync(issue.Key, EfHatchIssueEvent.PullRequestChanged));
    }

    [Fact]
    public async Task AnUnsetPullRequest_IsFilledFromTheRecord_InTheSameWrite()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue.Key, Record());

        Assert.Equal(PullRequest, (await h.ReadAsync(issue.Key)).PullRequestUrl);
        var changed = Assert.Single(await h.EventsAsync(issue.Key, EfHatchIssueEvent.PullRequestChanged));
        Assert.Null(changed.Payload!.Value.GetProperty("from").GetString());
        Assert.Equal(PullRequest, changed.Payload!.Value.GetProperty("to").GetString());
    }

    [Fact]
    public async Task AnUnknownKey_IsNotFound()
    {
        var h = await NewAsync();

        Assert.IsType<NotFoundResult>((await h.Controller.PutIssueContribution("AER-404", Record(), default)).Result);
    }

    [Theory]
    [InlineData("not a url", "feature/x", "octocat", "NONE", null, null)]
    [InlineData(PullRequest, "feature/x", " ", "NONE", null, null)]
    [InlineData(PullRequest, "feature/x", "octocat", null, null, null)]
    [InlineData(PullRequest, "feature/x", "octocat", "NONE", Instance, null)]
    [InlineData(PullRequest, "feature/x", "octocat", "NONE", null, "OTH-4")]
    public async Task ABadBody_IsRefused_AndNothingIsWritten(
        string url, string branch, string? login, string? association, string? instance, string? originKey)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var refused = await h.Controller.PutIssueContribution(
            issue.Key, Record(url, branch, login, association, instance, null, originKey), default);

        Assert.IsType<BadRequestObjectResult>(refused.Result);
        Assert.Null((await h.ReadAsync(issue.Key)).Contribution);
    }

    [Fact]
    public async Task AFieldOverItsCap_IsRefused()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var refused = await h.Controller.PutIssueContribution(
            issue.Key, Record(login: new string('x', ContributionRequest.MaxAuthorLoginLength + 1)), default);

        var bad = Assert.IsType<BadRequestObjectResult>(refused.Result);
        Assert.Equal("authorLogin is at most 100 characters", bad.Value);
    }

    // ---- Reading it ----

    [Fact]
    public async Task TheIssueAndItsBoardCard_CarryTheRecord_AndAreNullWithout()
    {
        var h = await NewAsync();
        var with = await h.FileAsync();
        var without = await h.FileAsync();
        await h.PutAsync(with.Key, Record());

        Assert.Null((await h.ReadAsync(without.Key)).Contribution);
        Assert.Equal(PullRequest, (await h.ReadAsync(with.Key)).Contribution!.PullRequestUrl);

        h.Db.ChangeTracker.Clear();
        var board = Value(await h.Board.GetBoard(default));
        Assert.Equal(PullRequest, board.Issues.Single(c => c.Key == with.Key).Contribution!.PullRequestUrl);
        Assert.Null(board.Issues.Single(c => c.Key == without.Key).Contribution);
    }

    [Fact]
    public async Task TheListFilters_FindExactlyTheMatchingIssue()
    {
        var h = await NewAsync();
        var mirrored = await h.FileAsync();
        var contributed = await h.FileAsync();
        var bare = await h.FileAsync();
        await h.PutAsync(mirrored.Key, Record(url: OtherPullRequest, instance: Instance, project: "OTH", originKey: "OTH-4"));
        await h.PutAsync(contributed.Key, Record());
        // A pull request recorded the older way is found too.
        var recorded = await h.FileAsync();
        await h.PatchPullRequestAsync(recorded.Key, "https://forge.example/owner/repo/pull/9");

        Assert.Equal([contributed.Key], await h.SearchAsync(pullRequestUrl: PullRequest));
        Assert.Equal([recorded.Key], await h.SearchAsync(pullRequestUrl: "https://forge.example/owner/repo/pull/9"));
        Assert.Equal([mirrored.Key], await h.SearchAsync(originInstance: Instance, originKey: "OTH-4"));
        Assert.Equal([mirrored.Key], await h.SearchAsync(originInstance: Instance + "/", originKey: "OTH-4"));
        Assert.Empty(await h.SearchAsync(originInstance: Instance, originKey: "OTH-5"));
        Assert.Empty(await h.SearchAsync(originInstance: "https://elsewhere.example", originKey: "OTH-4"));
        Assert.Empty(await h.SearchAsync(pullRequestUrl: "https://forge.example/owner/repo/pull/70"));
        Assert.DoesNotContain(bare.Key, await h.SearchAsync(pullRequestUrl: PullRequest));
    }

    // ---- Harness ----

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required IssueContributionController Controller { get; init; }
        public required IssuesController Issues { get; init; }
        public required IssueThreadController Thread { get; init; }
        public required BoardController Board { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }

        public async Task<IssueDto> FileAsync() =>
            Created(await Issues.CreateIssue(new IssueCreateRequest(ProjectId, "task", "a thing", null, null, null, null), default));

        public async Task<IssueDto> PutAsync(string key, ContributionRequest request)
        {
            var written = Value(await Controller.PutIssueContribution(key, request, default));
            Db.ChangeTracker.Clear();
            return written;
        }

        public async Task<IssueDto> ReadAsync(string key)
        {
            Db.ChangeTracker.Clear();
            return Value(await Issues.GetIssue(key, default));
        }

        public async Task PatchPullRequestAsync(string key, string url)
        {
            Value(await Issues.PatchIssue(
                key, new IssuePatchRequest(null, null, null, null, null, null, null, url, null, null), default));
            Db.ChangeTracker.Clear();
        }

        /// <summary>The events of <paramref name="kind"/>, oldest first.</summary>
        public async Task<IReadOnlyList<IssueEventDto>> EventsAsync(string key, string kind)
        {
            Db.ChangeTracker.Clear();
            return Value(await Thread.GetEvents(key, default)).Where(e => e.Kind == kind).Reverse().ToList();
        }

        public async Task<IReadOnlyList<string>> SearchAsync(
            string? pullRequestUrl = null, string? originInstance = null, string? originKey = null)
        {
            Db.ChangeTracker.Clear();
            return Value(await Issues.SearchIssues(
                null, null, null, null, null, null, null, default, pullRequestUrl, originInstance, originKey))
                .Select(c => c.Key).ToList();
        }
    }

    private static async Task<Harness> NewAsync(bool asKey = true)
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Add(project);
        db.AddRange(
            new EfHatchStatus { Name = "todo", SortOrder = 10, IsImplementation = true },
            new EfHatchStatus { Name = "done", SortOrder = 30, IsTerminal = true });
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var caller = asKey
            ? new StubCallerIdentity { Key = new EfApiKey { Name = "runner-1", Hash = [1], Prefix = "hatch_ak_x", CreatedAt = Now } }
            : new StubCallerIdentity { Person = new EfPerson { Name = "a person", CreatedAt = Now, UpdatedAt = Now } };
        var actors = new StubActorDirectory();

        return new Harness
        {
            Db = db,
            Controller = new IssueContributionController(db, actors, TestClaims.With(), caller, time),
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time, new ProjectAccess(db, actors)),
            Thread = new IssueThreadController(db, caller, time),
            Board = new BoardController(db, actors, TestClaims.With(), time, TestClaims.DoneHorizon()),
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

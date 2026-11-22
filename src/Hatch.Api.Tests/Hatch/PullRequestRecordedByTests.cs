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
/// Who recorded an issue's pull request, and whether a key or a person -
/// set by a patch or by a filed contribution, cleared with the url, and null
/// on a row that held the url before this was kept.
/// </summary>
public class PullRequestRecordedByTests
{
    private const string PullRequest = "https://forge.example/owner/repo/pull/7";

    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task APatchByAKey_ReadsKeyAndTheKeysName()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        h.Caller.Person = null;
        h.Caller.Local = new Actor(ActorKind.Key, Guid.NewGuid(), "a-runner");
        await h.PatchPullRequestAsync(issue.Key, PullRequest);

        var read = await h.ReadAsync(issue.Key);
        Assert.Equal("a-runner", read.PullRequestRecordedBy);
        Assert.Equal("key", read.PullRequestRecordedByKind);
    }

    [Fact]
    public async Task APatchByAPerson_ReadsPersonAndTheirName()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.PatchPullRequestAsync(issue.Key, PullRequest);

        var read = await h.ReadAsync(issue.Key);
        Assert.Equal("Nathan", read.PullRequestRecordedBy);
        Assert.Equal("person", read.PullRequestRecordedByKind);
    }

    [Fact]
    public async Task PatchingTheUrlToEmpty_ClearsBoth()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PatchPullRequestAsync(issue.Key, PullRequest);

        await h.PatchPullRequestAsync(issue.Key, "");

        var read = await h.ReadAsync(issue.Key);
        Assert.Null(read.PullRequestUrl);
        Assert.Null(read.PullRequestRecordedBy);
        Assert.Null(read.PullRequestRecordedByKind);
    }

    [Fact]
    public async Task AContributionFiling_RecordsTheFiler()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        h.Caller.Person = null;
        h.Caller.Local = new Actor(ActorKind.Key, Guid.NewGuid(), "a-runner");
        await h.Contribution.PutIssueContribution(
            issue.Key,
            new ContributionRequest(PullRequest, "feature/thing", "octocat", "FIRST_TIME_CONTRIBUTOR", null, null, null, null, null),
            default);

        var read = await h.ReadAsync(issue.Key);
        Assert.Equal("a-runner", read.PullRequestRecordedBy);
        Assert.Equal("key", read.PullRequestRecordedByKind);
    }

    [Fact]
    public async Task ARowWithBothColumnsNull_ReadsNullForBoth()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PatchPullRequestAsync(issue.Key, PullRequest);

        var row = await h.Db.Issues.SingleAsync(i => i.Id == IssueId(h, issue.Key));
        row.PullRequestRecordedBy = null;
        row.PullRequestRecordedByKind = null;
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var read = await h.ReadAsync(issue.Key);
        Assert.Equal(PullRequest, read.PullRequestUrl);
        Assert.Null(read.PullRequestRecordedBy);
        Assert.Null(read.PullRequestRecordedByKind);
    }

    [Fact]
    public async Task TheIssueList_CardCarriesBoth()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PatchPullRequestAsync(issue.Key, PullRequest);

        var cards = Value(await h.Issues.SearchIssues(
            null, null, null, null, null, null, null, default, pullRequestUrl: PullRequest));

        var card = Assert.Single(cards);
        Assert.Equal("Nathan", card.PullRequestRecordedBy);
        Assert.Equal("person", card.PullRequestRecordedByKind);
    }

    [Fact]
    public async Task TheBoardCard_CarriesBoth()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PatchPullRequestAsync(issue.Key, PullRequest);

        h.Db.ChangeTracker.Clear();
        var board = Value(await h.Board.GetBoard(default));
        var card = board.Issues.Single(c => c.Key == issue.Key);
        Assert.Equal("Nathan", card.PullRequestRecordedBy);
        Assert.Equal("person", card.PullRequestRecordedByKind);
    }

    [Fact]
    public async Task APlanChildCard_CarriesBoth()
    {
        var h = await NewAsync(type: "epic");
        var parent = await h.FileAsync(type: "epic");
        var child = await h.FileAsync(type: "task", parentKey: parent.Key);
        await h.PatchPullRequestAsync(child.Key, PullRequest);

        h.Db.ChangeTracker.Clear();
        var plan = Value(await h.Plan.GetIssuePlan(parent.Key, default));
        var row = plan.Children.Single(c => c.Issue.Key == child.Key);
        Assert.Equal("Nathan", row.Issue.PullRequestRecordedBy);
        Assert.Equal("person", row.Issue.PullRequestRecordedByKind);
    }

    // ---- Harness ----

    private static long IssueId(Harness h, string key) =>
        h.Db.Issues.AsNoTracking().Single(i => i.Project!.Key == key.Split('-')[0] && i.Number == int.Parse(key.Split('-')[1])).Id;

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required StubCallerIdentity Caller { get; init; }
        public required IssuesController Issues { get; init; }
        public required IssueContributionController Contribution { get; init; }
        public required BoardController Board { get; init; }
        public required PlanController Plan { get; init; }
        public required int ProjectId { get; init; }

        public async Task<IssueDto> FileAsync(string type = "task", string? parentKey = null) =>
            Created(await Issues.CreateIssue(
                new IssueCreateRequest(ProjectId, type, "a thing", null, parentKey, null, null), default));

        public async Task PatchPullRequestAsync(string key, string url)
        {
            Value(await Issues.PatchIssue(
                key, new IssuePatchRequest(null, null, null, null, null, null, null, url, null, null), default));
            Db.ChangeTracker.Clear();
        }

        public async Task<IssueDto> ReadAsync(string key)
        {
            Db.ChangeTracker.Clear();
            return Value(await Issues.GetIssue(key, default));
        }
    }

    private static async Task<Harness> NewAsync(string type = "task")
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
        var caller = new StubCallerIdentity { Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now } };
        var actors = new StubActorDirectory();

        return new Harness
        {
            Db = db,
            Caller = caller,
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time, new ProjectAccess(db, actors)),
            Contribution = new IssueContributionController(db, actors, TestClaims.With(), caller, time),
            Board = new BoardController(db, actors, TestClaims.With(), time, TestClaims.DoneHorizon()),
            Plan = new PlanController(db, actors, TestClaims.With(), time),
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

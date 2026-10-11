using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="EfHatchIssue.PullRequestUrl"/> is already written by
/// <see cref="IssuesController.PatchIssue"/> and read back by
/// <see cref="IssueDto"/> (see <c>IssuesControllerTests.APullRequestUrl_IsSetAndCarriedBack</c>);
/// this pins that <see cref="IssueCardDto.PullRequestUrl"/> carries the same
/// value on every surface that hands back a card rather than the issue
/// itself - the board, a search, an issue's children rollup, and the plan.
/// </summary>
public class IssueProjectionPullRequestUrlTests
{
    [Fact]
    public async Task TheBoardCard_CarriesThePullRequestUrl()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "ship it");
        await h.SetPullRequestAsync(issue.Key, "https://forge.example/owner/repo/pull/1");

        var board = Value(await h.Board.GetBoard(default));
        var card = board.Issues.Single(c => c.Key == issue.Key);

        Assert.Equal("https://forge.example/owner/repo/pull/1", card.PullRequestUrl);
    }

    [Fact]
    public async Task SearchResults_CarryThePullRequestUrl()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "ship it");
        await h.SetPullRequestAsync(issue.Key, "https://forge.example/owner/repo/pull/2");

        var found = Value(await h.Issues.SearchIssues(null, null, null, null, null, issue.Title, default));
        var card = Assert.Single(found);

        Assert.Equal("https://forge.example/owner/repo/pull/2", card.PullRequestUrl);
    }

    [Fact]
    public async Task AnIssuesChildrenRollup_CarriesThePullRequestUrl()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the programme");
        var child = await h.FileAsync("task", "one phase", parentKey: epic.Key);
        await h.SetPullRequestAsync(child.Key, "https://forge.example/owner/repo/pull/3");

        var rollup = Value(await h.Plan.GetIssuePlan(epic.Key, default));
        var row = Assert.Single(rollup.Children);

        Assert.Equal("https://forge.example/owner/repo/pull/3", row.Issue.PullRequestUrl);
    }

    [Fact]
    public async Task ThePlan_CarriesThePullRequestUrl()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the programme");
        await h.SetPullRequestAsync(epic.Key, "https://forge.example/owner/repo/pull/4");

        var plan = Value(await h.Plan.GetPlan(null, default));
        var top = Assert.Single(plan.Epics);

        Assert.Equal("https://forge.example/owner/repo/pull/4", top.Issue.PullRequestUrl);
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required IssuesController Issues { get; init; }
        public required BoardController Board { get; init; }
        public required PlanController Plan { get; init; }
        public required int ProjectId { get; init; }

        public async Task<IssueDto> FileAsync(string type, string title, string? parentKey = null) =>
            Created(await Issues.CreateIssue(new IssueCreateRequest(ProjectId, type, title, null, parentKey, null, null), default));

        public async Task SetPullRequestAsync(string key, string url) =>
            Value(await Issues.PatchIssue(key, new IssuePatchRequest(null, null, null, null, null, null, null, url, null, null), default));
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
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time, new ProjectAccess(db, actors)),
            Board = new BoardController(db, actors, TestClaims.With(), time),
            Plan = new PlanController(db, actors, TestClaims.With(), time),
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

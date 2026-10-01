using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The event trail read across every issue: the page, the order, and
/// everything it refuses in a sentence.
/// </summary>
public class ActivityControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EventsFromTwoProjects_ComeBackInterleavedNewestFirstWithTheirOwnKeys()
    {
        var h = await NewAsync();

        var issueA = await h.FileAsync(h.ProjectAId);
        var issueB = await h.FileAsync(h.ProjectBId);

        // Both created events land on the same instant (the harness's fixed
        // clock) - issueA first, issueB second, so the Id tiebreak puts
        // issueB's ahead of issueA's.
        await h.EventAsync(issueA, Now.AddSeconds(10), kind: "status_changed");
        await h.EventAsync(issueB, Now.AddSeconds(8), kind: "status_changed");
        await h.EventAsync(issueA, Now.AddSeconds(-10), kind: "retitled");

        var page = await h.ActivityAsync(limit: 10);

        Assert.False(page.HasMore);
        Assert.Equal(5, page.Events.Count);
        Assert.Equal([issueA.Key, issueB.Key, issueB.Key, issueA.Key, issueA.Key], page.Events.Select(e => e.IssueKey).ToArray());
        Assert.Equal(["status_changed", "status_changed", "created", "created", "retitled"], page.Events.Select(e => e.Kind).ToArray());

        // Newest first throughout, including the tie.
        for (var i = 1; i < page.Events.Count; i++)
            Assert.True(page.Events[i - 1].At >= page.Events[i].At);
    }

    [Fact]
    public async Task NoParametersAtAll_IsAHundredRowsFromTheTop()
    {
        var h = await NewAsync();

        var issue = await h.FileAsync(h.ProjectAId); // one "created" event, at Now

        for (var i = 0; i < 150; i++)
            await h.EventAsync(issue, Now.AddSeconds(-(i + 1)), kind: "status_changed");

        var page = await h.ActivityAsync();

        Assert.Equal(100, page.Events.Count);
        Assert.True(page.HasMore);
        Assert.Equal(Now, page.Events[0].At);

        for (var i = 1; i < page.Events.Count; i++)
            Assert.True(page.Events[i - 1].At > page.Events[i].At);
    }

    [Fact]
    public async Task HasMore_IsTrueWhileThereIsMoreBehindAndFalseOnTheLastPage()
    {
        var h = await NewAsync();

        var issue = await h.FileAsync(h.ProjectAId); // one "created" event, at Now
        for (var i = 0; i < 4; i++)
            await h.EventAsync(issue, Now.AddSeconds(-(i + 1)), kind: "status_changed");

        // Five events total.
        Assert.True((await h.ActivityAsync(limit: 2, offset: 0)).HasMore);
        Assert.True((await h.ActivityAsync(limit: 2, offset: 2)).HasMore);

        var last = await h.ActivityAsync(limit: 2, offset: 4);
        Assert.Single(last.Events);
        Assert.False(last.HasMore);
    }

    [Fact]
    public async Task AnOffset_ReturnsTheNextPageWithNoRowRepeatedOrSkipped()
    {
        var h = await NewAsync();

        var issue = await h.FileAsync(h.ProjectAId); // one "created" event, at Now
        for (var i = 0; i < 6; i++)
            await h.EventAsync(issue, Now.AddSeconds(-(i + 1)), kind: "status_changed");

        // Seven events total.
        var whole = await h.ActivityAsync(limit: 10);
        Assert.Equal(7, whole.Events.Count);

        var page1 = await h.ActivityAsync(limit: 3, offset: 0);
        var page2 = await h.ActivityAsync(limit: 3, offset: 3);
        var page3 = await h.ActivityAsync(limit: 3, offset: 6);

        var paged = page1.Events.Concat(page2.Events).Concat(page3.Events).Select(e => e.Id).ToList();
        Assert.Equal(whole.Events.Select(e => e.Id).ToList(), paged);
        Assert.Equal(paged.Count, paged.Distinct().Count());
    }

    [Fact]
    public async Task ALimitOutsideTheCapOrANegativeOffset_IsRefusedWithASentence()
    {
        var h = await NewAsync();

        Assert.Equal("a limit is between 1 and 500 - not 0", await h.RefusalAsync(limit: 0));
        Assert.Equal("a limit is between 1 and 500 - not 501", await h.RefusalAsync(limit: 501));
        Assert.Equal("an offset is not negative - not -1", await h.RefusalAsync(offset: -1));
    }

    [Fact]
    public async Task AnInstallationWhereNothingHasHappened_AnswersAnEmptyListNotA404()
    {
        var h = await NewAsync();

        var page = await h.ActivityAsync();

        Assert.Empty(page.Events);
        Assert.False(page.HasMore);
    }

    // ---- Who may read it ----

    [Fact]
    public void TheRoute_AcceptsTheHatchScope()
    {
        var guard = typeof(ActivityController)
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .Single();

        Assert.Equal(ApiKeyScopes.Hatch, guard.AcceptScope);
    }

    // ---- Harness ----

    private sealed class Harness
    {
        public required ActivityController Activity { get; init; }

        public required IssuesController Issues { get; init; }

        public required HatchContext Db { get; init; }

        public required int ProjectAId { get; init; }

        public required int ProjectBId { get; init; }

        public async Task<IssueDto> FileAsync(int projectId, string type = "task", string title = "a thing") =>
            Created(await Issues.CreateIssue(new IssueCreateRequest(projectId, type, title, null, null, null, null), default));

        /// <summary>
        /// A row straight into the context rather than through a write path, so
        /// the test chooses the timestamp and the ordering it produces.
        /// </summary>
        public async Task<long> EventAsync(
            IssueDto issue, DateTimeOffset at, string kind = EfHatchIssueEvent.Created, string actor = "someone", string? payload = null)
        {
            IssueKey.TryParse(issue.Key, out var projectKey, out var number);
            var issueId = await Db.Issues.AsNoTracking().WithKey(projectKey, number).Select(i => i.Id).FirstAsync();

            var entry = new EfHatchIssueEvent
            {
                IssueId = issueId,
                Actor = actor,
                Kind = kind,
                Payload = payload,
                At = at,
            };

            Db.IssueEvents.Add(entry);
            await Db.SaveChangesAsync();
            return entry.Id;
        }

        public async Task<ActivityPageDto> ActivityAsync(int limit = 100, int offset = 0) =>
            Value(await Activity.GetActivity(limit, offset, default));

        public async Task<string> RefusalAsync(int limit = 100, int offset = 0)
        {
            var result = await Activity.GetActivity(limit, offset, default);
            return Assert.IsType<BadRequestObjectResult>(result.Result).Value?.ToString() ?? "";
        }
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var projectA = new EfHatchProject { Key = "AER", Name = "Aerie", CreatedAt = Now };
        var projectB = new EfHatchProject { Key = "OPS", Name = "Ops", CreatedAt = Now };
        db.Add(projectA);
        db.Add(projectB);
        db.Add(new EfHatchStatus { Name = "inbox", SortOrder = 10 });
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);

        var caller = new StubCallerIdentity
        {
            Key = new EfApiKey
            {
                Name = "hatch",
                Hash = [1],
                Prefix = "hatch_ak_x",
                Scopes = [ApiKeyScopes.Hatch],
                CreatedAt = Now,
            },
        };

        return new Harness
        {
            Activity = new ActivityController(db),
            Issues = new IssuesController(db, new RankService(db), new StubActorDirectory(), TestClaims.With(), caller, time),
            Db = db,
            ProjectAId = projectA.Id,
            ProjectBId = projectB.Id,
        };
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

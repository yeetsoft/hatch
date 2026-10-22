using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The search read, on every path that never touches a <c>SearchVector</c>:
/// the filters, the page, the order, the cards and everything it refuses in a
/// sentence. What a word matches and how it ranks needs Postgres, and is
/// proved there.
/// </summary>
public class SearchControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 3, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Day = new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);

    // ---- No words ----

    [Fact]
    public async Task NoParameters_ListsEveryIssueNewestFirst_WithTheCardIssuesAnswers()
    {
        var h = await NewAsync();
        var oldest = await h.FileAsync(h.ProjectAId, title: "oldest");
        var tiedLow = await h.FileAsync(h.ProjectBId, title: "tied low");
        var tiedHigh = await h.FileAsync(h.ProjectAId, title: "tied high");
        var newest = await h.FileAsync(h.ProjectAId, title: "newest");
        await h.StampAsync(oldest, Day);
        await h.StampAsync(tiedLow, Day.AddDays(1));
        await h.StampAsync(tiedHigh, Day.AddDays(1));
        await h.StampAsync(newest, Day.AddDays(2), closedAt: Day.AddDays(3));

        var result = await h.SearchAsync();

        Assert.Equal(4, result.Total);
        Assert.Equal([newest.Key, tiedHigh.Key, tiedLow.Key, oldest.Key], result.Hits.Select(x => x.Card.Key));

        var cards = Value(await h.Issues.SearchIssues(null, null, null, null, null, null, default))
            .ToDictionary(c => c.Key);
        foreach (var hit in result.Hits)
        {
            // RunnerTagsOwn is a list, which a record compares by reference.
            Assert.Equal(cards[hit.Card.Key] with { RunnerTagsOwn = [] }, hit.Card with { RunnerTagsOwn = [] });
            Assert.Equal(cards[hit.Card.Key].RunnerTagsOwn, hit.Card.RunnerTagsOwn);
            Assert.Empty(hit.Matched);
        }

        var first = result.Hits[0];
        Assert.Equal(Day.AddDays(2), first.CreatedAt);
        Assert.Equal(Day.AddDays(3), first.ClosedAt);
        Assert.Equal(first.Card.ClosedAt, first.ClosedAt);
        Assert.Null(result.Hits[1].ClosedAt);
    }

    [Fact]
    public async Task NoWords_OrdersByClosedOrCreated_ThenCreated()
    {
        var h = await NewAsync();
        var closedLate = await h.FileAsync(h.ProjectAId, title: "old, closed last");
        var open = await h.FileAsync(h.ProjectAId, title: "open");
        var closedEarly = await h.FileAsync(h.ProjectAId, title: "new, closed first");
        await h.StampAsync(closedLate, Day, closedAt: Day.AddDays(10));
        await h.StampAsync(open, Day.AddDays(5));
        await h.StampAsync(closedEarly, Day.AddDays(2), closedAt: Day.AddDays(3));

        var result = await h.SearchAsync();

        Assert.Equal([closedLate.Key, open.Key, closedEarly.Key], result.Hits.Select(x => x.Card.Key));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!! ()")]
    public async Task AQueryWithNoTerms_IsNoQueryAtAll(string q)
    {
        var h = await NewAsync();
        await h.FileAsync(h.ProjectAId);
        await h.FileAsync(h.ProjectBId);

        var without = await h.SearchAsync();
        var with = await h.SearchAsync(q: q);

        Assert.Equal(2, with.Total);
        Assert.Equal(without, with, SearchComparer.Instance);
    }

    [Fact]
    public async Task NothingFiled_IsAnEmptyPageWithATotalOfZero()
    {
        var h = await NewAsync();

        var result = await h.SearchAsync();

        Assert.Equal(0, result.Total);
        Assert.Empty(result.Hits);
    }

    // ---- The page ----

    [Fact]
    public async Task APage_KeepsTheTotalBeforePaging_AndPagesAreDisjointAndWhole()
    {
        var h = await NewAsync();
        for (var n = 0; n < 5; n++)
            await h.StampAsync(await h.FileAsync(h.ProjectAId, title: $"issue {n}"), Day.AddHours(n));

        var whole = (await h.SearchAsync()).Hits.Select(x => x.Card.Key).ToList();

        var first = await h.SearchAsync(limit: 2, offset: 0);
        var second = await h.SearchAsync(limit: 2, offset: 2);
        var third = await h.SearchAsync(limit: 2, offset: 4);
        var beyond = await h.SearchAsync(limit: 2, offset: 6);

        Assert.All([first, second, third, beyond], page => Assert.Equal(5, page.Total));
        Assert.Equal(whole, first.Hits.Concat(second.Hits).Concat(third.Hits).Select(x => x.Card.Key));
        Assert.Single(third.Hits);
        Assert.Empty(beyond.Hits);
    }

    [Fact]
    public async Task ALimitAtItsBounds_IsAccepted()
    {
        var h = await NewAsync();

        Assert.Equal(0, (await h.SearchAsync(limit: 1)).Total);
        Assert.Equal(0, (await h.SearchAsync(limit: SearchController.MaxLimit)).Total);
    }

    // ---- The filters ----

    [Fact]
    public async Task ProjectTypeAndStatus_EachNarrow_AndAnUnknownStatusIsZeroHitsNotARefusal()
    {
        var h = await NewAsync();
        var a = await h.FileAsync(h.ProjectAId, "task", "in A");
        var b = await h.FileAsync(h.ProjectBId, "bug", "in B");

        Assert.Equal([a.Key], (await h.SearchAsync(projectId: h.ProjectAId)).Hits.Select(x => x.Card.Key));
        Assert.Equal([b.Key], (await h.SearchAsync(type: "bug")).Hits.Select(x => x.Card.Key));
        Assert.Equal(2, (await h.SearchAsync(statusId: a.StatusId)).Total);

        var unknown = await h.SearchAsync(statusId: a.StatusId + 1000);
        Assert.Equal(0, unknown.Total);
        Assert.Empty(unknown.Hits);

        Assert.Empty((await h.SearchAsync(projectId: h.ProjectAId, type: "bug")).Hits);
    }

    [Fact]
    public async Task CreatedFrom_ABareDateIncludesTheMidnightItNames()
    {
        var h = await NewAsync();
        var before = await h.FileAsync(h.ProjectAId);
        var at = await h.FileAsync(h.ProjectAId);
        await h.StampAsync(before, Day.AddTicks(-1));
        await h.StampAsync(at, Day);

        var result = await h.SearchAsync(createdFrom: "2026-09-12");

        Assert.Equal([at.Key], result.Hits.Select(x => x.Card.Key));
    }

    [Fact]
    public async Task CreatedTo_ABareDateReachesTheEndOfThatDay_AnInstantIsUsedExactly()
    {
        var h = await NewAsync();
        var lastSecond = await h.FileAsync(h.ProjectAId);
        var nextMidnight = await h.FileAsync(h.ProjectAId);
        await h.StampAsync(lastSecond, Day.AddHours(23).AddMinutes(59).AddSeconds(59));
        await h.StampAsync(nextMidnight, Day.AddDays(1));

        var byDate = await h.SearchAsync(createdTo: "2026-09-12");
        Assert.Equal([lastSecond.Key], byDate.Hits.Select(x => x.Card.Key));

        // An instant is the bound itself: the issue one second later is out.
        var byInstant = await h.SearchAsync(createdTo: "2026-09-12T23:59:58Z");
        Assert.Empty(byInstant.Hits);

        var exact = await h.SearchAsync(createdTo: "2026-09-12T23:59:59Z");
        Assert.Equal([lastSecond.Key], exact.Hits.Select(x => x.Card.Key));
    }

    [Fact]
    public async Task ClosedFromAndTo_FollowTheSameRules_AndAnOpenIssueNeverMatchesEither()
    {
        var h = await NewAsync();
        var open = await h.FileAsync(h.ProjectAId);
        var atMidnight = await h.FileAsync(h.ProjectAId);
        var lastSecond = await h.FileAsync(h.ProjectAId);
        var nextMidnight = await h.FileAsync(h.ProjectAId);
        await h.StampAsync(atMidnight, Day.AddDays(-5), closedAt: Day);
        await h.StampAsync(lastSecond, Day.AddDays(-5), closedAt: Day.AddHours(23).AddMinutes(59).AddSeconds(59));
        await h.StampAsync(nextMidnight, Day.AddDays(-5), closedAt: Day.AddDays(1));

        var from = await h.SearchAsync(closedFrom: "2026-09-12");
        Assert.Equal(
            new[] { atMidnight.Key, lastSecond.Key, nextMidnight.Key }.Order(),
            from.Hits.Select(x => x.Card.Key).Order());

        var to = await h.SearchAsync(closedTo: "2026-09-12");
        Assert.Equal(new[] { atMidnight.Key, lastSecond.Key }.Order(), to.Hits.Select(x => x.Card.Key).Order());

        var exact = await h.SearchAsync(closedTo: "2026-09-12T23:59:58Z");
        Assert.Equal([atMidnight.Key], exact.Hits.Select(x => x.Card.Key));

        Assert.DoesNotContain(open.Key, from.Hits.Concat(to.Hits).Select(x => x.Card.Key));
    }

    // ---- What it refuses ----

    [Fact]
    public async Task AnUnknownType_IsRefusedInTheHousesSentence()
    {
        var h = await NewAsync();

        Assert.Equal(
            $"an issue is one of {string.Join(", ", EfHatchIssue.Types)} - not \"chore\"",
            await h.RefusalAsync(type: "chore"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task ALimitOutOfRange_IsRefused(int limit)
    {
        var h = await NewAsync();

        Assert.Equal($"a limit is between 1 and 200 - not {limit}", await h.RefusalAsync(limit: limit));
    }

    [Fact]
    public async Task ANegativeOffset_IsRefused()
    {
        var h = await NewAsync();

        Assert.Equal("an offset is not negative - not -1", await h.RefusalAsync(offset: -1));
    }

    [Theory]
    [InlineData("createdFrom")]
    [InlineData("createdTo")]
    [InlineData("closedFrom")]
    [InlineData("closedTo")]
    public async Task ADateThatIsNeitherADateNorAnInstant_NamesTheFieldItWasGivenFor(string field)
    {
        var h = await NewAsync();

        var refusal = field switch
        {
            "createdFrom" => await h.RefusalAsync(createdFrom: "yesterday"),
            "createdTo" => await h.RefusalAsync(createdTo: "yesterday"),
            "closedFrom" => await h.RefusalAsync(closedFrom: "yesterday"),
            _ => await h.RefusalAsync(closedTo: "yesterday"),
        };

        Assert.Equal(
            $"\"yesterday\" is not {field} - give a date (2026-09-12) or an instant (2026-09-12T17:00:00Z)",
            refusal);
    }

    [Fact]
    public async Task Refusals_AreReportedInOrder_TypeThenLimitThenOffsetThenDates()
    {
        var h = await NewAsync();

        Assert.StartsWith("an issue is one of", await h.RefusalAsync(type: "chore", limit: 0, offset: -1, createdFrom: "x"));
        Assert.StartsWith("a limit is between", await h.RefusalAsync(limit: 0, offset: -1, createdFrom: "x"));
        Assert.StartsWith("an offset is not negative", await h.RefusalAsync(offset: -1, createdFrom: "x"));
        Assert.StartsWith("\"x\" is not createdFrom", await h.RefusalAsync(createdFrom: "x", createdTo: "y", closedFrom: "z"));
        Assert.StartsWith("\"y\" is not createdTo", await h.RefusalAsync(createdTo: "y", closedFrom: "z"));
        Assert.StartsWith("\"z\" is not closedFrom", await h.RefusalAsync(closedFrom: "z", closedTo: "w"));
    }

    // ---- Who may read it ----

    [Fact]
    public void TheRoute_AcceptsTheHatchScope()
    {
        var guard = typeof(SearchController)
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .Single();

        Assert.Equal(ApiKeyScopes.Hatch, guard.AcceptScope);
    }

    // ---- Harness ----

    private sealed class Harness
    {
        public required SearchController Search { get; init; }

        public required IssuesController Issues { get; init; }

        public required HatchContext Db { get; init; }

        public required int ProjectAId { get; init; }

        public required int ProjectBId { get; init; }

        public async Task<IssueDto> FileAsync(int projectId, string type = "task", string title = "a thing") =>
            Created(await Issues.CreateIssue(new IssueCreateRequest(projectId, type, title, null, null, null, null), default));

        /// <summary>
        /// Sets the two instants on a row straight through the context rather
        /// than through a write path, so the test chooses them.
        /// </summary>
        public async Task StampAsync(IssueDto issue, DateTimeOffset createdAt, DateTimeOffset? closedAt = null)
        {
            IssueKey.TryParse(issue.Key, out var projectKey, out var number);
            var row = await Db.Issues.WithKey(projectKey, number).FirstAsync();
            row.CreatedAt = createdAt;
            row.ClosedAt = closedAt;
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async Task<SearchDto> SearchAsync(
            string? q = null, int? projectId = null, string? type = null, int? statusId = null,
            string? createdFrom = null, string? createdTo = null, string? closedFrom = null, string? closedTo = null,
            int limit = 50, int offset = 0) =>
            Value(await Search.GetSearch(q, projectId, type, statusId, createdFrom, createdTo, closedFrom, closedTo, limit, offset, default));

        public async Task<string> RefusalAsync(
            string? type = null, string? createdFrom = null, string? createdTo = null,
            string? closedFrom = null, string? closedTo = null, int limit = 50, int offset = 0)
        {
            var result = await Search.GetSearch(null, null, type, null, createdFrom, createdTo, closedFrom, closedTo, limit, offset, default);
            return Assert.IsType<BadRequestObjectResult>(result.Result).Value?.ToString() ?? "";
        }
    }

    /// <summary>Two pages are the same page if their totals and hits are equal, hit by hit.</summary>
    private sealed class SearchComparer : IEqualityComparer<SearchDto>
    {
        public static readonly SearchComparer Instance = new();

        public bool Equals(SearchDto? x, SearchDto? y) =>
            x is not null && y is not null && x.Total == y.Total && x.Hits.Count == y.Hits.Count &&
            x.Hits.Zip(y.Hits).All(p =>
                // RunnerTagsOwn is a list, which a record compares by reference.
                p.First.Card with { RunnerTagsOwn = [] } == p.Second.Card with { RunnerTagsOwn = [] } &&
                p.First.Card.RunnerTagsOwn.SequenceEqual(p.Second.Card.RunnerTagsOwn) &&
                p.First.CreatedAt == p.Second.CreatedAt &&
                p.First.ClosedAt == p.Second.ClosedAt && p.First.Matched.SequenceEqual(p.Second.Matched));

        public int GetHashCode(SearchDto obj) => obj.Total;
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

        var actors = new StubActorDirectory();

        return new Harness
        {
            Search = new SearchController(db, actors, TestClaims.With(), time),
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time, new ProjectAccess(db, actors)),
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

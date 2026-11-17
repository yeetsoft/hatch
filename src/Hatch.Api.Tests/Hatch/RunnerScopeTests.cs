using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// A runner's key reads through its owner's scope: what the dispatch routes
/// hand it, who is elected to drive a project, and which peers are counted as
/// able to take a ticket. Two projects, AAA and BBB, with BBB's issue ranked
/// first so that a scan which forgot the scope would answer it.
/// </summary>
public class RunnerScopeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    private const string Url = "https://example.com/o/r/pull/1";

    // ---- The dispatch routes ----

    [Fact]
    public async Task ASomeKey_NextAndQueueHoldOnlyItsProjects_EvenWhenAnOutsideIssueRanksFirst()
    {
        var h = await NewAsync();
        var work = h.Controller(ScopeSeed.Only(h.A));

        var queue = Value(await work.GetQueue(0, null, default));
        Assert.Equal([h.KeyA], queue.Select(e => e.Issue.Key));

        var next = Value(await work.GetNextWork(0, null, null, default));
        Assert.Equal(h.KeyA, next.Issue.Key);
    }

    [Fact]
    public async Task ANoneKey_QueueIsEmpty_AndNextIs204()
    {
        var h = await NewAsync();
        var work = h.Controller(VisibleProjects.Nothing);

        Assert.Empty(Value(await work.GetQueue(0, null, default)));
        Assert.IsType<NoContentResult>((await work.GetNextWork(0, null, null, default)).Result);
    }

    [Fact]
    public async Task AnAllKey_SeesBothProjects_AndBBBsIssueIsFirst()
    {
        var h = await NewAsync();
        var work = h.Controller(VisibleProjects.Everything);

        Assert.Equal(
            [h.KeyB, h.KeyA],
            Value(await work.GetQueue(0, null, default)).Select(e => e.Issue.Key).ToList());
        Assert.Equal(h.KeyB, Value(await work.GetNextWork(0, null, null, default)).Issue.Key);
    }

    [Fact]
    public async Task ASomeKey_AnIssueOutsideItsScope_Is404OnEveryRouteThatNamesOne()
    {
        var h = await NewAsync();
        var work = h.Controller(ScopeSeed.Only(h.A));
        var outside = h.KeyB;

        Assert.IsType<NotFoundResult>((await work.GetWork(outside, null, default)).Result);
        Assert.IsType<NotFoundResult>((await work.HopWork(outside, null, null, null, default)).Result);
        Assert.IsType<NotFoundResult>((await work.VerdictWork(outside, Guid.NewGuid(), null, null, null, default)).Result);
        Assert.IsType<NotFoundResult>((await work.MergedWork(outside, new PullRequestMergedRequest(Url, "somewhere:/one"), default)).Result);
        Assert.IsType<NotFoundResult>((await work.PushbackWork(outside, new PullRequestPushbackRequest(Url, "somewhere:/one"), default)).Result);
        Assert.IsType<NotFoundResult>((await work.LandedWork(outside, default)).Result);
        Assert.IsType<NotFoundResult>((await work.ClosedWork(outside, new PullRequestClosedRequest(Url, "somewhere:/one"), default)).Result);
    }

    [Fact]
    public async Task AnAllKey_ReachesTheSameIssue_ThatASomeKeyIsRefused()
    {
        var h = await NewAsync();

        var inside = await h.Controller(VisibleProjects.Everything).GetWork(h.KeyB, null, default);

        Assert.Equal(h.KeyB, Value(inside).Issue.Key);
    }

    [Fact]
    public async Task Review_ListsNothingFromOutsideTheKeysScope()
    {
        var h = await NewAsync(withReview: true);

        var some = Value(await h.Controller(ScopeSeed.Only(h.A)).GetReview(null, true, default));
        var none = Value(await h.Controller(VisibleProjects.Nothing).GetReview(null, true, default));
        var all = Value(await h.Controller(VisibleProjects.Everything).GetReview(null, true, default));

        Assert.Equal([h.ReviewKeyA], some.Select(r => r.Key));
        Assert.Empty(none);
        Assert.Equal([h.ReviewKeyA, h.ReviewKeyB], all.Select(r => r.Key).Order(StringComparer.Ordinal));
    }

    // ---- The merge train's driver ----

    [Fact]
    public async Task TheElection_NeverPicksARunnerWhoseOwnerIsScopedOutOfTheProject_HoweverOld()
    {
        var h = await NewAsync();
        var scoped = await h.PersonAsync(PersonScopeMode.Some, h.A);
        var everyone = await h.PersonAsync(PersonScopeMode.All);
        var old = RunnerName();
        var newer = RunnerName();
        await h.AddLoopRunnerAsync(old, scoped, firstSeen: Now.AddDays(-30));
        await h.AddLoopRunnerAsync(newer, everyone, firstSeen: Now.AddDays(-1));

        // Each of the two heartbeats reads through its own owner's view, as
        // the request does: the scoped runner never sees BBB at all.
        var told = await h.Runners(wall: true).DriversAsync(h.Scoped(VisibleProjects.Everything), h.Keys, Now, default);
        Assert.Equal(["AAA"], told[old]);
        Assert.Equal(["BBB"], told[newer]);

        var own = await h.Runners(wall: true).DriversAsync(h.Scoped(ScopeSeed.Only(h.A)), h.Keys, Now, default);
        Assert.Equal(["AAA"], own[old]);
        Assert.Empty(own[newer]);

        var byProject = await h.Runners(wall: true).ProjectDriversAsync(h.Scoped(VisibleProjects.Everything), h.Keys, Now, default);
        Assert.Equal(old, byProject[h.A].Name);
        Assert.Equal(newer, byProject[h.B].Name);
    }

    [Fact]
    public async Task TheElection_WithBothOwnersInScope_ElectsTheOldestAsBefore()
    {
        var h = await NewAsync();
        var both = await h.PersonAsync(PersonScopeMode.Some, h.A, h.B);
        var everyone = await h.PersonAsync(PersonScopeMode.All);
        var old = RunnerName();
        var newer = RunnerName();
        await h.AddLoopRunnerAsync(old, both, firstSeen: Now.AddDays(-30));
        await h.AddLoopRunnerAsync(newer, everyone, firstSeen: Now.AddDays(-1));

        var told = await h.Runners(wall: true).DriversAsync(h.Scoped(VisibleProjects.Everything), h.Keys, Now, default);

        Assert.Equal(["AAA", "BBB"], told[old].Order(StringComparer.Ordinal));
        Assert.Empty(told[newer]);
    }

    [Fact]
    public async Task TheElection_WithTheWallDown_IgnoresScopeAndElectsTheOldest()
    {
        var h = await NewAsync();
        var scoped = await h.PersonAsync(PersonScopeMode.None);
        var old = RunnerName();
        await h.AddLoopRunnerAsync(old, scoped, firstSeen: Now.AddDays(-30));
        await h.AddLoopRunnerAsync(RunnerName(), null, firstSeen: Now.AddDays(-1));

        var told = await h.Runners(wall: false).DriversAsync(h.Scoped(VisibleProjects.Everything), h.Keys, Now, default);

        Assert.Equal(["AAA", "BBB"], told[old].Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheElection_ARunnerWithNoOwner_IsUnbounded()
    {
        var h = await NewAsync();
        var old = RunnerName();
        await h.AddLoopRunnerAsync(old, null, firstSeen: Now.AddDays(-30));

        var told = await h.Runners(wall: true).DriversAsync(h.Scoped(VisibleProjects.Everything), h.Keys, Now, default);

        Assert.Equal(["AAA", "BBB"], told[old].Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task OwnerScopes_ReadsNoRowAsEverything_NoneAsNothing_AndSomeAsItsRawProjects()
    {
        var h = await NewAsync();
        var some = await h.PersonAsync(PersonScopeMode.Some, h.A);
        var none = await h.PersonAsync(PersonScopeMode.None);
        var all = await h.PersonAsync(PersonScopeMode.All);
        var unrowed = Guid.NewGuid();

        var scopes = await h.Runners(wall: true).OwnerScopesAsync(h.Scoped(VisibleProjects.Nothing), [some, none, all, unrowed], default);

        Assert.True(scopes[all].Unbounded);
        Assert.True(scopes[unrowed].Unbounded);
        Assert.False(scopes[none].Sees(h.A));
        Assert.True(scopes[some].Sees(h.A));
        Assert.False(scopes[some].Sees(h.B));
        Assert.Empty(await h.Runners(wall: false).OwnerScopesAsync(h.Scoped(VisibleProjects.Nothing), [some], default));
    }

    // ---- The peers ----

    [Fact]
    public async Task APeerWhoseOwnerIsScopedOut_IsNotATaker_AndTheQueueSaysWhy()
    {
        var h = await NewAsync();
        var scoped = await h.PersonAsync(PersonScopeMode.Some, h.A);
        var everyone = await h.PersonAsync(PersonScopeMode.All);
        var outsider = RunnerName();
        var insider = RunnerName();
        await h.AddLoopRunnerAsync(outsider, scoped);
        await h.AddLoopRunnerAsync(insider, everyone);

        var queue = Value(await h.Controller(VisibleProjects.Everything, wall: true).GetQueue(0, null, default, takers: true));

        var b = queue.Single(e => e.Issue.Key == h.KeyB);
        Assert.Equal([insider], b.Takers);
        Assert.Equal(new RunnerReasonDto(outsider, "its owner cannot see BBB"), Assert.Single(b.Untaken!));

        var a = queue.Single(e => e.Issue.Key == h.KeyA);
        Assert.Equal(new[] { insider, outsider }.Order(StringComparer.Ordinal), a.Takers);
    }

    [Fact]
    public async Task APeerWithNoScopeRow_IsStillATaker()
    {
        var h = await NewAsync();
        var peer = RunnerName();
        await h.AddLoopRunnerAsync(peer, Guid.NewGuid());

        var queue = Value(await h.Controller(VisibleProjects.Everything, wall: true).GetQueue(0, null, default, takers: true));

        Assert.All(queue, e => Assert.Equal([peer], e.Takers));
    }

    private static string RunnerName() => $"runner-{Guid.NewGuid():N}";

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result?.GetType().Name ?? "nothing"}");

    private static async Task<Harness> NewAsync(bool withReview = false)
    {
        var options = new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new HatchContext(options);

        var a = new EfHatchProject { Key = "AAA", Name = "A", CreatedAt = Now };
        var b = new EfHatchProject { Key = "BBB", Name = "B", CreatedAt = Now };
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        var doing = new EfHatchStatus { Name = "in progress", SortOrder = 30 };
        var review = new EfHatchStatus { Name = "review", SortOrder = 35 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 40, IsTerminal = true };
        db.AddRange(a, b, todo, doing, review, done);
        await db.SaveChangesAsync();

        db.AddRange(
            Playbook(todo.Id, doing.Id, "sonnet"),
            Playbook(doing.Id, review.Id, "sonnet"));
        await db.SaveChangesAsync();

        // BBB's issue ranks first in each column.
        var issueB = Issue(b.Id, todo.Id, 1000);
        var issueA = Issue(a.Id, todo.Id, 2000);
        db.AddRange(issueB, issueA);
        if (withReview)
            db.AddRange(Issue(b.Id, review.Id, 1000, number: 2), Issue(a.Id, review.Id, 2000, number: 2));
        await db.SaveChangesAsync();

        return new Harness(options, db, a.Id, b.Id);
    }

    private static EfHatchIssue Issue(int projectId, int statusId, long rank, int number = 1) => new()
    {
        ProjectId = projectId, Number = number, Type = "story", Title = $"issue {projectId}-{number}", Description = "",
        StatusId = statusId, Rank = rank, CreatedBy = "test", CreatedAt = Now, UpdatedAt = Now,
    };

    private static EfHatchPlaybook Playbook(int from, int to, string model) => new()
    {
        FromStatusId = from, ToStatusId = to, Types = "", Shape = "any", Prompt = "do the thing", Model = model,
        Effort = "high", CreatedAt = Now, UpdatedAt = Now,
    };

    private sealed class Harness(DbContextOptions<HatchContext> options, HatchContext db, int a, int b)
    {
        public int A { get; } = a;
        public int B { get; } = b;
        public string KeyA => "AAA-1";
        public string KeyB => "BBB-1";
        public string ReviewKeyA => "AAA-2";
        public string ReviewKeyB => "BBB-2";

        private readonly FakeTimeProvider time = new(Now);
        private readonly StubActorDirectory actors = new();
        private readonly StubHatchRevision revision = new();

        public IAuthService Keys { get; } = new AuthService(
            new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options),
            Options.Create(new AuthOptions()), new FakeTimeProvider(Now), NullLogger<AuthService>.Instance);

        /// <summary>A context that belongs to a request whose scope resolved to <paramref name="visible"/>.</summary>
        public HatchContext Scoped(VisibleProjects visible) => ScopeSeed.Request(options, visible);

        public Runners Runners(bool wall) =>
            new(Options.Create(new HatchOptions()), Options.Create(new AuthOptions { Enabled = wall }), revision);

        /// <summary>The work controller on a request whose scope resolved to <paramref name="visible"/>.</summary>
        public WorkController Controller(VisibleProjects visible, bool wall = false)
        {
            var scoped = Scoped(visible);
            var runners = Runners(wall);
            return new WorkController(
                scoped, actors, TestClaims.With(), time, Options.Create(new HatchOptions()), new RankService(scoped),
                new WorkControllerTests.StubCallerIdentity
                {
                    Key = new EfApiKey { Name = "hatch-loop", Prefix = "hatch_ak_", Hash = [], CreatedAt = Now },
                },
                runners, Keys, new RunnerSessions(runners));
        }

        /// <summary>A person with a scope row, and the projects it names when it is <c>some</c>.</summary>
        public async Task<Guid> PersonAsync(string mode, params int[] projects)
        {
            var id = Guid.NewGuid();
            db.PersonScopes.Add(new EfHatchPersonScope { PersonId = id, Mode = mode, UpdatedBy = "test", UpdatedAt = Now });
            db.PersonScopeProjects.AddRange(projects.Select(p => new EfHatchPersonScopeProject { PersonId = id, ProjectId = p }));
            await db.SaveChangesAsync();
            return id;
        }

        /// <summary>A live loop runner holding every checkout, as the dispatch tests' own rows are.</summary>
        public async Task AddLoopRunnerAsync(string name, Guid? owner, DateTimeOffset? firstSeen = null)
        {
            db.LegacyRunners.Add(new EfHatchRunner
            {
                Name = name,
                Kind = EfHatchRunner.LoopKind,
                FirstSeenAt = firstSeen ?? Now,
                LastSeenAt = Now,
                State = EfHatchRunner.Running,
                Standing = true,
                ForPersonId = owner,
            });
            await db.SaveChangesAsync();
        }
    }
}

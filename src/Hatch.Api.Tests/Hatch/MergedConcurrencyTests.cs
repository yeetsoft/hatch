using Hatch.Api.Ef;
using Hatch.Api.Models.Auth;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// Two runners report one merge at once: the first moves the issue, the second
/// is turned away with the gate's own 409, and nothing is written twice.
///
/// <para><b>These run against a real Postgres, and skip without one.</b> The
/// guarantee is the <c>AND "StatusId" = …</c> that <see cref="EfHatchIssue.StatusId"/>
/// being a concurrency token puts on every <c>UPDATE</c>; EF's in-memory
/// provider does not check tokens, so a race on it would prove nothing. Point
/// <c>HATCH_TEST_DATABASE_URL</c> at a scratch database, or run
/// <c>make test-api-db</c>.</para>
///
/// <para>The race is made certain rather than lucky: a save interceptor holds
/// the first two saves until both have arrived, so both requests have read the
/// issue before either writes it.</para>
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class MergedConcurrencyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    private const string PullRequest = "https://example.com/o/r/pull/1";

    [SkippableFact]
    public async Task TwoReportsOfOneMerge_MoveTheIssueOnce_AndTurnTheSecondAway()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync("task", "waiting for a merge", h.Review, PullRequest);

        var results = await h.ReportTwiceAsync(issue);

        Assert.Single(results, r => r.Result is null && r.Value is not null);
        Assert.Single(results, r => r.Result is ConflictObjectResult);

        await using var db = h.NewContext();
        var row = await db.Issues.Include(i => i.Events).AsNoTracking().SingleAsync(i => i.Id == issue.Id);
        Assert.Equal(h.Done, row.StatusId);
        Assert.Single(row.Events, e => e.Kind == EfHatchIssueEvent.StatusChanged);
    }

    [SkippableFact]
    public async Task TwoReportsOfOneMerge_GiveAStorysWaitingTaskOneEvent()
    {
        await using var h = await NewAsync();
        var story = await h.FileAsync("story", "shared pull request", h.Review, PullRequest, onePullRequest: true);
        var task = await h.FileAsync("task", "a part", h.Review, parentId: story.Id);

        var results = await h.ReportTwiceAsync(story);

        Assert.Single(results, r => r.Result is ConflictObjectResult);

        await using var db = h.NewContext();
        var row = await db.Issues.Include(i => i.Events).AsNoTracking().SingleAsync(i => i.Id == task.Id);
        Assert.Equal(h.Done, row.StatusId);
        Assert.Single(row.Events, e => e.Kind == EfHatchIssueEvent.StatusChanged);
    }

    [SkippableFact]
    public async Task TwoReportsOfOneMerge_FileOneChangelogTask_ForAParentlessIssue()
    {
        await using var h = await NewAsync(changelog: true);
        var issue = await h.FileAsync("task", "waiting for a merge", h.Review, PullRequest);

        var results = await h.ReportTwiceAsync(issue);

        Assert.Single(results, r => r.Result is ConflictObjectResult);

        await using var db = h.NewContext();
        var record = await db.ChangelogTasks.SingleAsync();
        Assert.Equal(issue.Id, record.SourceIssueId);
        Assert.Single(await db.IssueEvents
            .Where(e => e.IssueId == issue.Id && e.Kind == EfHatchIssueEvent.ChangelogTaskFiled).ToListAsync());
    }

    /// <summary>
    /// Holds the first two saves, across both contexts, until both have
    /// arrived - so both requests have read before either writes. Later saves,
    /// such as the winner's changelog task, pass straight through.
    /// </summary>
    private sealed class SaveBarrier : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var arrival = Interlocked.Increment(ref arrivals);
            if (arrival > 2) return result;

            if (arrival == 2) bothArrived.TrySetResult();
            await bothArrived.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return result;
        }
    }

    private static async Task<Harness> NewAsync(bool changelog = false)
    {
        Skip.IfNot(
            HatchDatabase.Available,
            "HATCH_TEST_DATABASE_URL is unset - the race is decided by the UPDATE's WHERE clause, which EF's " +
            "in-memory provider does not run. Run `make test-api-db`, or point the variable at a scratch database.");

        var connectionString = await HatchDatabase.PrepareAsync();

        await using var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var inbox = new EfHatchStatus { Name = "inbox", SortOrder = 10 };
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        var doing = new EfHatchStatus { Name = "in progress", SortOrder = 30 };
        var review = new EfHatchStatus { Name = "review", SortOrder = 35 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 40, IsTerminal = true };
        db.AddRange(project, inbox, todo, doing, review, done);
        await db.SaveChangesAsync();

        db.AddRange(
            Playbook(todo.Id, doing.Id, "epic", "opus"),
            Playbook(todo.Id, doing.Id, "", "sonnet"),
            Playbook(doing.Id, review.Id, "", "sonnet"));

        if (changelog)
        {
            var (canonical, _) = RemoteIdentity.Canonical("https://example.com/owner/repo.git");
            db.ProjectRepositories.Add(new EfHatchProjectRepository
            {
                ProjectId = project.Id,
                Remote = "https://example.com/owner/repo.git",
                Canonical = canonical!,
                SortOrder = 0,
                Changelog = true,
                CreatedAt = Now,
            });
        }

        await db.SaveChangesAsync();

        return new Harness(connectionString, project.Id, review.Id, done.Id);
    }

    private static EfHatchPlaybook Playbook(int from, int to, string types, string model) => new()
    {
        FromStatusId = from,
        ToStatusId = to,
        Types = types,
        Shape = "any",
        Prompt = "do the thing",
        Model = model,
        Effort = "high",
        DeniedTools = EfHatchPlaybook.WriteDeniedTools(null),
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    private sealed class Harness(string connectionString, int projectId, int review, int done) : IAsyncDisposable
    {
        private readonly SaveBarrier barrier = new();
        private readonly List<HatchContext> contexts = [];
        private int next = 1;

        public int Review { get; } = review;
        public int Done { get; } = done;

        /// <summary>A context of its own on the shared database, for a test's own reads.</summary>
        public HatchContext NewContext() => new(
            new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        public async Task<EfHatchIssue> FileAsync(
            string type, string title, int statusId, string? pullRequestUrl = null, long? parentId = null, bool? onePullRequest = null)
        {
            await using var db = NewContext();
            var issue = new EfHatchIssue
            {
                ProjectId = projectId,
                Number = next++,
                Type = type,
                Title = title,
                Description = "",
                StatusId = statusId,
                Rank = 1024,
                ParentId = parentId,
                PullRequestUrl = pullRequestUrl,
                OnePullRequest = onePullRequest,
                CreatedBy = "operator",
                CreatedAt = Now,
                UpdatedAt = Now,
            };
            db.Issues.Add(issue);

            // The numbers filed by hand above are the project's own, so the
            // counter a filed changelog task takes must come after them.
            (await db.Projects.SingleAsync(p => p.Id == projectId)).NextIssueNumber = next;
            await db.SaveChangesAsync();
            return issue;
        }

        /// <summary>
        /// Two requests reporting the merge of <paramref name="issue"/>, each
        /// through a controller of its own over a context of its own - the
        /// train driver and a runner that saw the merge on the forge.
        /// </summary>
        public async Task<ActionResult<IssueDto>[]> ReportTwiceAsync(EfHatchIssue issue)
        {
            var key = IssueKey.Format("AER", issue.Number);
            var request = new PullRequestMergedRequest(PullRequest, "somewhere:/checkouts/one");
            var first = NewController();
            var second = NewController();

            return await Task.WhenAll(
                first.MergedWork(key, request, default),
                second.MergedWork(key, request, default));
        }

        private WorkController NewController()
        {
            var db = new HatchContext(
                new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).AddInterceptors(barrier).Options);
            contexts.Add(db);

            var time = new FakeTimeProvider(Now);
            var caller = new WorkControllerTests.StubCallerIdentity
            {
                Key = new EfApiKey { Name = "hatch-loop", Prefix = "hatch_ak_", Hash = [], CreatedAt = Now },
            };
            var runners = new Runners(
                Options.Create(new HatchOptions()), Options.Create(new AuthOptions()), new StubHatchRevision());
            var coreDb = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var keys = new AuthService(
                coreDb, Options.Create(new AuthOptions()), time, NullLogger<AuthService>.Instance);

            return new WorkController(
                db, new StubActorDirectory(), TestClaims.With(), time, Options.Create(new HatchOptions()),
                new RankService(db), caller, runners, keys, new RunnerSessions(runners));
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var db in contexts) await db.DisposeAsync();
        }
    }
}

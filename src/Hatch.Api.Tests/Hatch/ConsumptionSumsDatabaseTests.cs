using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="WorkLogSums.ReadAsync"/>'s Postgres branch - the raw SQL bucket
/// arithmetic - against the pure fold it must agree with:
/// <see cref="ConsumptionReport.Fold"/> over the same sessions, read here off
/// a real Postgres rather than EF's in-memory provider, which never exercises
/// the raw SQL at all.
///
/// <para><b>These run against a real Postgres, and skip without one.</b> Point
/// <c>HATCH_TEST_DATABASE_URL</c> at a scratch database, or run
/// <c>make test-api-db</c>, which does it for you.</para>
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class ConsumptionSumsDatabaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    // ---- Harness ----

    private sealed class Harness : IAsyncDisposable
    {
        private readonly List<HatchContext> open = [];

        public required string ConnectionString { get; init; }

        public HatchContext Connect()
        {
            var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(ConnectionString).Options);
            open.Add(db);
            return db;
        }

        public ReportsController Reports() =>
            new(Connect(), new StubActorDirectory(), TestClaims.With(), new FakeTimeProvider(Now));

        public async ValueTask DisposeAsync()
        {
            foreach (var db in open) await db.DisposeAsync();
        }
    }

    private static async Task<Harness> NewAsync()
    {
        Skip.IfNot(
            HatchDatabase.Available,
            "HATCH_TEST_DATABASE_URL is unset - the raw SQL this pins only runs on a real Postgres. " +
            "Run `make test-api-db`, or point the variable at a scratch database.");

        return new Harness { ConnectionString = await HatchDatabase.PrepareAsync() };
    }

    // ---- One seeded log, with every edge case the plan names ----

    /// <summary>What the log holds, and the handles a test reads off it.</summary>
    private sealed record Log(
        int ProjectA, int ProjectB,
        long EpicA, long StoryA, long TaskA1, long TaskA2, long LooseStory, long LooseTask, long EpicB, long StoryB, long TaskB1,
        List<ConsumptionReport.Session> Sessions);

    private static async Task<Log> SeedAsync(Harness h)
    {
        var db = h.Connect();

        var projectA = new EfHatchProject { Key = "TAA", Name = "Alpha", CreatedAt = Now };
        var projectB = new EfHatchProject { Key = "TBB", Name = "Beta", CreatedAt = Now };
        var doing = new EfHatchStatus { Name = "doing", SortOrder = 10 };
        db.AddRange(projectA, projectB, doing);
        await db.SaveChangesAsync();

        var numbers = new Dictionary<int, int>();

        async Task<EfHatchIssue> IssueAsync(string type, int projectId, long? parentId)
        {
            numbers[projectId] = numbers.GetValueOrDefault(projectId) + 1;
            var issue = new EfHatchIssue
            {
                ProjectId = projectId, Number = numbers[projectId], Type = type, Title = type,
                Description = "", StatusId = doing.Id, ParentId = parentId, Rank = 1024,
                CreatedBy = "ann", CreatedAt = Now, UpdatedAt = Now,
            };
            db.Issues.Add(issue);
            await db.SaveChangesAsync();
            return issue;
        }

        var epicA = await IssueAsync("epic", projectA.Id, null);
        var storyA = await IssueAsync("story", projectA.Id, epicA.Id);
        var taskA1 = await IssueAsync("task", projectA.Id, storyA.Id);
        var taskA2 = await IssueAsync("task", projectA.Id, storyA.Id);
        // A parentless story, under no epic, with a task of its own.
        var looseStory = await IssueAsync("story", projectA.Id, null);
        var looseTask = await IssueAsync("task", projectA.Id, looseStory.Id);
        var epicB = await IssueAsync("epic", projectB.Id, null);
        var storyB = await IssueAsync("story", projectB.Id, epicB.Id);
        var taskB1 = await IssueAsync("task", projectB.Id, storyB.Id);

        var sessions = new List<ConsumptionReport.Session>
        {
            // Costs to eight places, and a session landing exactly on the short
            // window's `from`.
            new(taskA1.Id, Now.AddHours(-5), false, 111, 22, 3, 4, 0.12345678m, 1_000),
            // Just under the short window's `to`.
            new(taskA1.Id, Now.AddSeconds(-1), false, 50, 0, 0, 0, 2.5m, 500),
            // Unpriced, beside an errored one on the same issue.
            new(taskA2.Id, Now.AddHours(-3), false, 10, 0, 0, 0, null, 200),
            new(taskA2.Id, Now.AddHours(-2), true, 20, 0, 0, 0, 1m, 300),
            // Another project, inside the short window.
            new(taskB1.Id, Now.AddHours(-4), false, 30, 0, 0, 0, 3m, 400),
            // An issue with only unpriced sessions, outside the short window
            // and inside the long one.
            new(looseTask.Id, Now.AddHours(-6), false, 15, 0, 0, 0, null, 100),
            new(looseTask.Id, Now.AddHours(-7), false, 5, 0, 0, 0, null, 100),
            // Directly on an epic, and on a sub-epic's story - outside the
            // short window, inside the long one.
            new(epicA.Id, Now.AddDays(-9), false, 40, 0, 0, 0, 4.5m, 600),
            new(storyB.Id, Now.AddDays(-1), false, 60, 0, 0, 0, 5m, 700),
        };

        foreach (var s in sessions)
        {
            db.WorkLog.Add(new EfHatchWorkLogEntry
            {
                IssueId = s.IssueId, SessionId = $"s-{Guid.NewGuid():N}", StartedAt = s.EndedAt, EndedAt = s.EndedAt,
                DurationMs = s.DurationMs, IsError = s.IsError, CostUsd = s.CostUsd,
                InputTokens = s.InputTokens, OutputTokens = s.OutputTokens,
                CacheCreationTokens = s.CacheCreationTokens, CacheReadTokens = s.CacheReadTokens,
                CreatedAt = s.EndedAt,
            });
        }
        await db.SaveChangesAsync();

        return new Log(
            projectA.Id, projectB.Id, epicA.Id, storyA.Id, taskA1.Id, taskA2.Id,
            looseStory.Id, looseTask.Id, epicB.Id, storyB.Id, taskB1.Id, sessions);
    }

    /// <summary>
    /// The narrow rows the old controller read before this ticket - straight
    /// off the database rather than off <see cref="Log.Sessions"/>'s own
    /// literals, since a session's <c>CostUsd</c> comes back from Postgres at
    /// the column's own scale (eight places) and a hand-written decimal
    /// literal does not - the mismatch is in the test's input, not in either
    /// fold, and comparing against the same rows both folds actually see is
    /// what a byte-identical CSV needs.
    /// </summary>
    private static async Task<List<ConsumptionReport.Session>> LoadSessionsAsync(
        HatchContext db, DateTimeOffset from, DateTimeOffset to, List<int> projectIds)
    {
        var scoped = db.WorkLog.AsNoTracking().Where(w => w.EndedAt >= from && w.EndedAt < to);
        if (projectIds.Count > 0) scoped = scoped.Where(w => projectIds.Contains(w.Issue!.ProjectId));

        return await scoped
            .Select(w => new ConsumptionReport.Session(
                w.IssueId, w.EndedAt, w.IsError, w.InputTokens, w.OutputTokens,
                w.CacheCreationTokens, w.CacheReadTokens, w.CostUsd, w.DurationMs))
            .ToListAsync();
    }

    private static async Task<(List<ConsumptionReport.Candidate> Candidates, Dictionary<long, long?> ParentOf)> LoadHierarchyAsync(
        HatchContext db, string type, List<int> projectIds)
    {
        var all = await db.Issues.AsNoTracking()
            .Select(i => new { i.Id, i.ParentId, ProjectKey = i.Project!.Key, i.ProjectId, i.Number, i.Title, i.Type, i.StatusId })
            .ToListAsync();

        var parentOf = all.ToDictionary(i => i.Id, i => i.ParentId);

        var candidates = all
            .Where(i => type == "all" || i.Type == type)
            .Where(i => projectIds.Count == 0 || projectIds.Contains(i.ProjectId))
            .Select(i => new ConsumptionReport.Candidate(
                i.Id, i.ParentId, IssueKey.Format(i.ProjectKey, i.Number), i.Title, i.Type, i.ProjectKey, i.StatusId))
            .ToList();

        return (candidates, parentOf);
    }

    [SkippableFact]
    public async Task TheDatabasesSum_MatchesThePureFold_OverEveryTypeProjectAndSort()
    {
        await using var h = await NewAsync();
        var log = await SeedAsync(h);
        var db = h.Connect();

        var windows = new[]
        {
            // Hourly: a short range.
            (From: Now.AddHours(-5), To: Now, OffsetMinutes: 0),
            // Daily, with a non-zero offset: a long range.
            (From: Now.AddDays(-10), To: Now, OffsetMinutes: 180),
        };

        var projectFilters = new[] { new List<int>(), new List<int> { log.ProjectA } };
        var sorts = Enum.GetValues<ConsumptionReport.ConsumptionSort>();

        foreach (var (from, to, offsetMinutes) in windows)
        foreach (var type in new[] { "epic", "all" })
        foreach (var projectIds in projectFilters)
        foreach (var sort in sorts)
        {
            var bucketSize = WorkLogRollup.ChooseBucketSize(from, to);
            var window = WorkLogRollup.Align(from, to, bucketSize, offsetMinutes);

            var (candidates, parentOf) = await LoadHierarchyAsync(db, type, projectIds);

            var expectedSessions = await LoadSessionsAsync(db, from, to, projectIds);
            var expected = ConsumptionReport.Fold(expectedSessions, candidates, parentOf, window, sort);

            var sums = await WorkLogSums.ReadAsync(db, from, to, window.From, window.Length, projectIds, groupByIssue: true, default);
            var actual = ConsumptionReport.FoldSums(sums, candidates, parentOf, window, sort);

            var detail = $"window={from:O}..{to:O} type={type} projects=[{string.Join(',', projectIds)}] sort={sort}";
            Assert.True(expected.Totals == actual.Totals, $"{detail}: totals {expected.Totals} != {actual.Totals}");
            Assert.Equal(expected.Rows, actual.Rows);
            Assert.Equal(expected.Buckets, actual.Buckets);
            Assert.Equal(ConsumptionCsv.Write(expected.Rows), ConsumptionCsv.Write(actual.Rows));
        }
    }

    [SkippableFact]
    public async Task TheDatabasesRows_MatchTheInMemoryGrouping_PerIssueAndPerBucketAlone()
    {
        await using var h = await NewAsync();
        await SeedAsync(h);
        var db = h.Connect();

        var from = Now.AddDays(-10);
        var to = Now;
        var window = WorkLogRollup.Align(from, to, WorkLogRollup.ChooseBucketSize(from, to), offsetMinutes: 180);

        var expectedSessions = await LoadSessionsAsync(db, from, to, []);

        foreach (var groupByIssue in new[] { true, false })
        {
            var fromDb = await WorkLogSums.ReadAsync(db, from, to, window.From, window.Length, null, groupByIssue, default);
            var inMemory = WorkLogSums.Group(expectedSessions, window.From, window.Length, groupByIssue);

            Order(fromDb);
            Order(inMemory);

            Assert.Equal(inMemory, fromDb);
        }

        static void Order(List<WorkLogSums.SessionSum> rows) => rows.Sort((a, b) =>
            a.IssueId != b.IssueId ? a.IssueId.CompareTo(b.IssueId) : a.Bucket.CompareTo(b.Bucket));
    }

    [SkippableFact]
    public async Task TheBoundRead_PagesPastSessionsOutsideTheFilter_ToFindTheOneInside()
    {
        await using var h = await NewAsync();
        var db = h.Connect();

        var project = new EfHatchProject { Key = "TCC", Name = "Gamma", CreatedAt = Now };
        var doing = new EfHatchStatus { Name = "doing", SortOrder = 10 };
        db.AddRange(project, doing);
        await db.SaveChangesAsync();

        var epic = new EfHatchIssue
        {
            ProjectId = project.Id, Number = 1, Type = "epic", Title = "epic", Description = "",
            StatusId = doing.Id, Rank = 1024, CreatedBy = "ann", CreatedAt = Now, UpdatedAt = Now,
        };
        var stray = new EfHatchIssue
        {
            ProjectId = project.Id, Number = 2, Type = "task", Title = "stray", Description = "",
            StatusId = doing.Id, Rank = 1024, CreatedBy = "ann", CreatedAt = Now, UpdatedAt = Now,
        };
        db.AddRange(epic, stray);
        await db.SaveChangesAsync();

        var from = Now.AddDays(-30);

        // Thirty sessions on an issue with no epic above it - more than one
        // page of the bound read - all earlier than the epic's own session.
        for (var i = 0; i < 30; i++)
            db.WorkLog.Add(new EfHatchWorkLogEntry
            {
                IssueId = stray.Id, SessionId = $"stray-{i}", StartedAt = from.AddHours(i), EndedAt = from.AddHours(i),
                DurationMs = 100, InputTokens = 1, CreatedAt = from.AddHours(i),
            });

        var epicAt = from.AddDays(2);
        db.WorkLog.Add(new EfHatchWorkLogEntry
        {
            IssueId = epic.Id, SessionId = "epic-1", StartedAt = epicAt, EndedAt = epicAt,
            DurationMs = 100, InputTokens = 1, CostUsd = 1m, CreatedAt = epicAt,
        });

        await db.SaveChangesAsync();

        var report = h.Reports();
        var result = await report.GetConsumption(from.ToString("O"), Now.ToString("O"), null, null, ct: default);

        var dto = result.Value ?? throw new InvalidOperationException("expected a value");
        Assert.Equal(epicAt, dto.FirstSessionAt);
        Assert.Equal(epicAt, dto.LastSessionAt);
    }
}

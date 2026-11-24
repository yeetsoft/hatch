using System.Text.Json;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Api.Tests.Auth;
using Hatch.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What a runner's merge-train poll, <c>GET /api/work/review</c>, costs the
/// database (HA-2800), and that its answer has not changed.
///
/// <para>The poll runs every few seconds on every runner, so it is counted two
/// ways at two sizes of board: with nothing in review or merging, and with a
/// few rows in both. At 50 issues and at 2,000 the statements are the same
/// number, no statement reads <c>Issues</c> without a filter on a column or an
/// id, none reads a description, and no priority tree is built.</para>
///
/// <para>The golden files beside this one are the route's JSON, recorded on the
/// route as it was before it was made cheap. <c>HATCH_WRITE_GOLDEN=1</c> writes
/// them again; do that only when the answer is meant to change.</para>
///
/// <para>It points at <c>HATCH_TEST_DATABASE_URL</c>, which is dropped and
/// recreated, so it must never name a database anybody uses.</para>
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class ReviewPollCostTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    private const string Remote = "git@forge.test:acme/widget.git";
    private const string TrunkSha = "1111111111111111111111111111111111111111";

    private static readonly Guid LivePerson = new("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid GonePerson = new("00000000-0000-0000-0000-0000000000a2");

    private const string GoldenFolder = "src/Hatch.Api.Tests/Hatch/ReviewPollGolden";

    private const string SkipReason =
        "HATCH_TEST_DATABASE_URL is unset - the counts come from a real Postgres. Run `make test-api-db`, " +
        "or point the variable at a scratch database that may be dropped.";

    /// <summary>What a board holds besides the filler that makes it big.</summary>
    public enum Shape
    {
        /// <summary>Nothing in review or merging.</summary>
        Idle,

        /// <summary>Three in review, one merging, under an epic that is pre-approved and expedited. One of the three waits on its story, so the answer holds three rows.</summary>
        Busy,

        /// <summary><see cref="Busy"/> and more: a second pull request, a waiting one, a second merging row.</summary>
        Train,

        /// <summary><see cref="Train"/>, with the second merging row carrying a mark that it was brought current.</summary>
        Marked,

        /// <summary>Two projects: merging rows under epics in the other one, set by someone live and by someone gone.</summary>
        Across,
    }

    // ---- The cost ----

    [SkippableFact]
    public async Task WithNothingInReview_TheRouteCostsTwoStatements_AtEitherSize()
    {
        Skip.IfNot(HatchDatabase.Available, SkipReason);

        var small = await MeasureAsync(50, Shape.Idle);
        var large = await MeasureAsync(2000, Shape.Idle);

        Assert.Equal(0, small.Answer);
        Assert.Equal(0, large.Answer);
        Assert.True(small.Statements.Count == 2 && large.Statements.Count == 2, Describe(small, large));
        AssertNarrow(small);
        AssertNarrow(large);
        Assert.Equal(0, small.TreeBuilds);
        Assert.Equal(0, large.TreeBuilds);
    }

    [SkippableFact]
    public async Task WithRowsInReviewAndMerging_TheRouteCostsTheSameAtEitherSize()
    {
        Skip.IfNot(HatchDatabase.Available, SkipReason);

        var small = await MeasureAsync(50, Shape.Busy);
        var large = await MeasureAsync(2000, Shape.Busy);

        Assert.Equal(3, small.Answer);
        Assert.Equal(3, large.Answer);
        Assert.True(small.Statements.Count == large.Statements.Count, Describe(small, large));
        AssertNarrow(small);
        AssertNarrow(large);
        Assert.Equal(0, small.TreeBuilds);
        Assert.Equal(0, large.TreeBuilds);
    }

    [SkippableFact]
    public async Task WithRowsAcrossAProjectLine_TheRouteCostsTheSameAtEitherSize()
    {
        Skip.IfNot(HatchDatabase.Available, SkipReason);

        var small = await MeasureAsync(50, Shape.Across);
        var large = await MeasureAsync(2000, Shape.Across);

        Assert.True(small.Statements.Count == large.Statements.Count, Describe(small, large));
        AssertNarrow(small);
        AssertNarrow(large);
        Assert.Equal(0, small.TreeBuilds);
        Assert.Equal(0, large.TreeBuilds);
    }

    // ---- The answer ----

    [SkippableTheory]
    [InlineData(Shape.Train, "train")]
    [InlineData(Shape.Marked, "marked")]
    [InlineData(Shape.Across, "across")]
    public async Task TheAnswer_IsTheOneRecordedBeforeTheRouteWasMadeCheap(Shape shape, string name)
    {
        Skip.IfNot(HatchDatabase.Available, SkipReason);

        var measured = await MeasureAsync(50, shape);
        var path = Path.Combine(MinimalDatabases.RepositoryRoot(), GoldenFolder.Replace('/', Path.DirectorySeparatorChar), $"{name}.json");

        if (Environment.GetEnvironmentVariable("HATCH_WRITE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, measured.Json);
        }

        var expected = (await File.ReadAllTextAsync(path)).ReplaceLineEndings("\n");
        Assert.Equal(expected, measured.Json.ReplaceLineEndings("\n"));
    }

    // ---- Measuring ----

    private sealed record Measurement(
        int Answer, string Json, int TreeBuilds, IReadOnlyList<ReadCounter.Statement> Statements);

    /// <summary>
    /// Seeds a board of <paramref name="size"/> issues in the shape, then calls
    /// the route once on a fresh context and counts what it sent.
    /// </summary>
    private static async Task<Measurement> MeasureAsync(int size, Shape shape)
    {
        var connectionString = await SeedAsync(size, shape);

        var actors = new StubActorDirectory();
        actors.Live.Add(new Actor(ActorKind.Person, LivePerson, "live"));

        var answer = 0;
        var json = "";
        var treeBuilds = -1;

        var measured = await ReadCounter.MeasureAsync(connectionString, async db =>
        {
            using var scope = ScanTally.Begin("GET /api/work/review");
            var work = NewController(db, actors);

            var result = await work.GetReview([Remote], true, default);
            var rows = result.Value ?? throw new InvalidOperationException($"the route refused: {result.Result}");

            answer = rows.Count;
            json = JsonSerializer.Serialize(rows, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
            treeBuilds = scope.Tally.TreeBuilds;
        });

        return new Measurement(answer, json, treeBuilds, measured.Statements);
    }

    private static WorkController NewController(HatchContext db, StubActorDirectory actors)
    {
        var time = new FakeTimeProvider(Now);
        var auth = new AuthOptions();
        var authOptions = Options.Create(auth);
        var runners = new Runners(Options.Create(new HatchOptions()), authOptions, new StubHatchRevision());

        // The keys are checked against an in-memory database, which the counter does not see.
        var coreDb = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var keys = new AuthService(coreDb, authOptions, time, Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthService>.Instance);

        return new WorkController(
            db, actors, TestClaims.With(), time, Options.Create(new HatchOptions()),
            new RankService(db), new StubCallerIdentity(), runners, keys, new RunnerSessions(runners));
    }

    // ---- What the SQL may say ----

    /// <summary>
    /// Every statement that names <c>Issues</c> filters it on a column or an id,
    /// and none of them reads a description.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex FilteredOnColumnOrId = new(
        @"\.""(StatusId|Id|IssueId)""(::\w+)? = ANY \(", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static void AssertNarrow(Measurement m)
    {
        foreach (var statement in m.Statements.Where(s => s.Sql.Contains("\"Issues\"", StringComparison.Ordinal)))
        {
            // EF writes `i."StatusId"::bigint = ANY (@ids)`: the column, maybe cast, then ANY over the list.
            // A check or a contribution is read with `m."IssueId" = ANY (...)`, and EF joins Issues to it
            // by id for the caller's scope: that statement names Issues, and reads only the rows asked for.
            var filtered = FilteredOnColumnOrId.IsMatch(statement.Sql);
            Assert.True(filtered, $"a statement reads Issues with no filter on a column or an id:{Environment.NewLine}{statement.Sql}");
            Assert.DoesNotContain("\"Description\"", statement.Sql);
        }
    }

    private static string Describe(Measurement small, Measurement large) =>
        $"the route sent {small.Statements.Count} statements on the small board and {large.Statements.Count} on the large one:"
        + Environment.NewLine + string.Join(Environment.NewLine + "--" + Environment.NewLine, large.Statements.Select(s => s.Sql));

    // ---- The board ----

    private static async Task<string> SeedAsync(int size, Shape shape)
    {
        var connectionString = await HatchDatabase.PrepareAsync();

        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);
        await db.Database.OpenConnectionAsync();

        var inbox = new EfHatchStatus { Name = "inbox", SortOrder = 10 };
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        var doing = new EfHatchStatus { Name = "doing", SortOrder = 30, IsWip = true, IsImplementation = true };
        var review = new EfHatchStatus { Name = "review", SortOrder = 35, IsWip = true };
        var approved = new EfHatchStatus { Name = "approved", SortOrder = 37, IsProtected = true, MergesPullRequest = true };
        var done = new EfHatchStatus { Name = "done", SortOrder = 40, IsTerminal = true };
        db.AddRange(inbox, todo, doing, review, approved, done);

        var (canonical, _) = RemoteIdentity.Canonical(Remote);
        var a = new EfHatchProject
        {
            Key = "AER", Name = "Alpha", CreatedAt = Now.AddDays(-90), OnePullRequest = true,
            Repositories = [new EfHatchProjectRepository { Remote = Remote, Canonical = canonical!, SortOrder = 0, CreatedAt = Now.AddDays(-90) }],
        };
        // Beta holds no repository, so a poll that declares itself standing holds it.
        var b = new EfHatchProject { Key = "BEE", Name = "Beta", CreatedAt = Now.AddDays(-90), Landing = Landings.Trunk };
        db.Projects.Add(a);
        if (shape == Shape.Across) db.Projects.Add(b);
        await db.SaveChangesAsync();

        var numbers = new Dictionary<int, int>();
        var rank = 0L;

        EfHatchIssue Issue(EfHatchProject project, string type, string title, EfHatchStatus status, EfHatchIssue? parent = null)
        {
            numbers[project.Id] = numbers.GetValueOrDefault(project.Id) + 1;
            return new EfHatchIssue
            {
                ProjectId = project.Id, Number = numbers[project.Id], Type = type, Title = title, StatusId = status.Id,
                ParentId = parent?.Id, Rank = 1024L * ++rank, CreatedBy = "bo", CreatedAt = Now.AddDays(-30), UpdatedAt = Now.AddDays(-1),
                Description = "A long description, which the poll has no business reading. " + new string('x', 400),
            };
        }

        // ---- The rows the poll asks about ----
        var core = new List<EfHatchIssue>();
        var checks = new List<EfHatchMergeCheck>();

        if (shape != Shape.Idle)
        {
            // The epic is pre-approved and expedited, and what is under it inherits both.
            var epic = Issue(a, "epic", "the epic", doing);
            epic.Priority = PriorityLevels.Expedited;
            epic.PreApproval = PreApprovalLevels.PreApproved;
            epic.PrioritySetBy = LivePerson;
            epic.PreApprovalSetBy = LivePerson;
            db.Issues.Add(epic);

            var epicB = Issue(b, "epic", "the epic across the line", doing);
            var epicGone = Issue(b, "epic", "the other epic across the line", doing);
            epicB.Priority = epicGone.Priority = PriorityLevels.Expedited;
            epicB.PrioritySetBy = LivePerson;
            epicGone.PrioritySetBy = GonePerson;
            if (shape == Shape.Across) db.Issues.AddRange(epicB, epicGone);
            await db.SaveChangesAsync();

            if (shape == Shape.Across)
            {
                // Two rows in the merges column, the lower-ranked under the epic whose setter is live:
                // it is only the head if the level is read across the line.
                var gone = Issue(a, "story", "under an epic set by someone gone", approved, epicGone);
                var live = Issue(a, "story", "under an epic set by someone live", approved, epicB);
                var trunk = Issue(b, "story", "lands on the trunk", review);
                foreach (var row in new[] { gone, live })
                {
                    Pull(row);
                    checks.Add(Clean(row));
                }
                core.AddRange([gone, live, trunk]);
            }
            else
            {
                // Three in review: a story sharing one pull request, the task that waits on it, and a loose story.
                var story = Issue(a, "story", "shares one pull request", review, epic);
                story.OnePullRequest = true;
                Pull(story);
                db.Issues.Add(story);
                await db.SaveChangesAsync();
                var waiting = Issue(a, "task", "waits on the story", review, story);
                var loose = Issue(a, "story", "up for review", review);
                Pull(loose);
                loose.PullRequestReviewDecision = "APPROVED";
                loose.PullRequestMergeState = "CLEAN";

                // And one merging, under the epic.
                var head = Issue(a, "story", "at the head of the train", approved, epic);
                Pull(head);
                checks.Add(Clean(head));
                core.AddRange([waiting, loose, head]);

                if (shape is Shape.Train or Shape.Marked)
                {
                    var own = Issue(a, "task", "has a pull request of its own", review, story);
                    Pull(own);

                    var parked = Issue(a, "story", "written, not opened", review);
                    parked.WaitingPrTitle = "A pull request, written and waiting";
                    parked.WaitingPrBody = "What it changes.";
                    parked.WaitingPrBranch = "ha-9-parked";
                    parked.WaitingPrSha = "2222222222222222222222222222222222222222";
                    parked.WaitingPrRemote = Remote;
                    parked.WaitingPrRunner = "runner-1";
                    parked.WaitingPrWhy = "the forge was out of reach";
                    parked.WaitingPrAt = Now.AddHours(-3);

                    // Ranked before the head and not expedited: the head only because of its level, or of its mark.
                    var second = Issue(a, "story", "behind it on the train", approved);
                    second.Rank = head.Rank - 1;
                    Pull(second);
                    checks.Add(Clean(second));
                    if (shape == Shape.Marked)
                    {
                        second.BroughtCurrentAt = Now.AddHours(-1);
                        second.BroughtCurrentTrunkSha = TrunkSha;
                    }

                    core.AddRange([own, parked, second]);
                }
            }

            db.Issues.AddRange(core);
            await db.SaveChangesAsync();
            db.MergeChecks.AddRange(checks.Select(c => { c.IssueId = Resolve(core, c.IssueId); return c; }));
            await db.SaveChangesAsync();
        }

        // ---- The filler: the same depth at every size, and none of it in review ----
        var present = await db.Issues.CountAsync();
        var filler = Math.Max(0, size - present);
        var epics = Enumerable.Range(0, Math.Max(1, filler / 40)).Select(i => Issue(a, "epic", $"epic {i}", doing)).ToList();
        db.Issues.AddRange(epics);
        await db.SaveChangesAsync();

        var columns = new[] { todo, doing, inbox, done };
        var stories = Enumerable.Range(0, Math.Max(1, (filler - epics.Count) / 6))
            .Select(i => Issue(a, "story", $"story {i}", columns[i % columns.Length], epics[i % epics.Count])).ToList();
        db.Issues.AddRange(stories);
        await db.SaveChangesAsync();

        var tasks = Enumerable.Range(0, Math.Max(0, filler - epics.Count - stories.Count))
            .Select(i => Issue(a, "task", $"task {i}", columns[i % columns.Length], stories[i % stories.Count])).ToList();
        db.Issues.AddRange(tasks);
        await db.SaveChangesAsync();

        // Statistics for the planner, so the plan is the same every run.
        await db.Database.ExecuteSqlRawAsync("VACUUM (ANALYZE)");
        await ReadCounter.FlushAsync(db.Database.GetDbConnection());

        return connectionString;

        // A pull request, as the forge reports it.
        void Pull(EfHatchIssue issue)
        {
            issue.PullRequestUrl = $"https://forge.test/acme/widget/pull/{issue.Number}";
            issue.PullRequestMergeState = "CLEAN";
            issue.PullRequestReviewDecision = "APPROVED";
            issue.PullRequestHeadSha = $"{issue.Number:D40}";
            issue.PullRequestHeadBranch = $"ha-{issue.Number}-branch";
            issue.PullRequestHeadSince = Now.AddHours(-2);
        }

        EfHatchMergeCheck Clean(EfHatchIssue issue) => new()
        {
            // The issue's id is not known until it is saved; its number stands in, and Resolve swaps it.
            IssueId = issue.Number + (issue.ProjectId * 1_000_000L),
            Remote = Remote, Canonical = canonical!, Trunk = "main", TrunkSha = TrunkSha, Runner = "runner-1", CheckedBy = "runner-1",
            Branch = $"ha-{issue.Number}-branch", BranchSha = $"{issue.Number:D40}", Verdict = MergeVerdicts.Clean,
            CheckedAt = Now.AddMinutes(-30),
        };

        static long Resolve(List<EfHatchIssue> rows, long stand) =>
            rows.Single(r => r.Number + (r.ProjectId * 1_000_000L) == stand).Id;
    }
}

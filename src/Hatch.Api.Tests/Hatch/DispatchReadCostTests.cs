using System.Text;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Api.Services.Chat;
using Hatch.Api.Tests.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What each dispatch read costs the database, in statements, at two sizes of
/// board and two sizes of fleet, in one table. A change that adds a read per
/// runner or per gate fails here, and names the read and the count; a change
/// that removes one edits a number, so the reviewer sees it go down in the same
/// diff.
///
/// <para>The reads are <c>GET /api/work/next</c>, <c>GET /api/work/queue</c>,
/// <c>GET /api/work/queue?takers=true</c> and <c>GET /api/attention</c>. The
/// in-memory provider sends no SQL, so the count is taken on Postgres, through
/// the application's own <see cref="StatementCounter"/> inside a
/// <see cref="ScanTally"/>, and checked against <see cref="ReadCounter"/>, which
/// sees the same commands from the test's side: a difference means a command ran
/// outside the call.</para>
///
/// <para><b>The board.</b> 100 and 1,000 issues over the report seed's columns
/// and projects (see <see cref="ReadCostSeed"/>), with what dispatch reads and a
/// report does not: playbooks for the two columns before review, a dependency
/// on a tenth of the tasks, one ring of two that wait on each other, an open
/// question on one issue in twenty, a future <c>ReadyAt</c> on one in twenty,
/// and a tag on some of the tasks in <c>todo</c> that only the first runner has.
/// The history is the report seed's small one at both sizes: only the number of
/// issues varies.</para>
///
/// <para><b>The fleet.</b> One runner or four, all live and all declaring tags
/// and models. <c>runner-0</c> asks. The other three are marked preferred and
/// lack the tag, so a <c>next</c> from <c>runner-0</c> has to scan each of them
/// to learn that it may keep the tagged rows and must leave the rest.</para>
///
/// <para>It points at <c>HATCH_TEST_DATABASE_URL</c>, which is emptied on every
/// seed, so it must never name a database anybody uses.</para>
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class DispatchReadCostTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 31, 12, 0, 0, TimeSpan.Zero);

    private const string Next = "GET /api/work/next";
    private const string Queue = "GET /api/work/queue";
    private const string Takers = "GET /api/work/queue?takers=true";
    private const string Attention = "GET /api/attention";

    private static readonly string[] Reads = [Next, Queue, Takers, Attention];

    private static readonly int[] Boards = [100, 1000];
    private static readonly int[] Fleets = [1, 4];

    /// <summary>The tag only <c>runner-0</c> declares.</summary>
    private const string Gpu = "gpu";

    // ---- The table ----
    // Statements per call: (read, issues on the board, live runners) -> count.
    // Every statement of the call counts, the scans of peers included. A failure
    // prints the measured table in this shape, ready to paste.
    //
    // One scan of the board is 32 statements, here and at either size: it is what
    // each peer adds to next and to the takers read, and half of what each adds to
    // attention. The rest of every number is what the route does with the scan.
    //
    // It was 30 before HA-2795. The candidates were one statement that joined each
    // issue to its project, its repositories and its contribution, and carried every
    // description with it. They are now three: the issues without their description
    // or search vector, the projects they name with their repositories, and the
    // contributions. Two more statements a scan, and no description read - this
    // seed sets no description and no brief limit, so the scan never loads one.

    private static readonly Dictionary<(string Read, int Board, int Fleet), int> Pinned = new()
    {
        // next: its own scan (32), then resolving the one row it returns - the brief,
        // the row's notes and questions, its ancestors, its playbook (51), and one
        // statement more than before HA-2795: the scan's rows carry no description,
        // so the one issue it answers with is read for its own. A runner that is not
        // preferred asks each preferred peer whether it could take a row before
        // keeping it, and the row picked here is one no peer can take, so every peer
        // is scanned, once each: 32 more for each.
        [(Next, 100, 1)] = 84,
        [(Next, 100, 4)] = 180,
        [(Next, 1000, 1)] = 84,
        [(Next, 1000, 4)] = 180,

        // queue: its own scan (32), then the projection of every row it returns -
        // issues, comments, claims, the merge-train notes (19), and one statement
        // more than before HA-2795: with the default descriptions=true the rows'
        // descriptions are read together, once, for the answer. descriptions=false
        // would make it 51. No runner is named, so no peer is asked and the fleet
        // does not show.
        [(Queue, 100, 1)] = 52,
        [(Queue, 100, 4)] = 52,
        [(Queue, 1000, 1)] = 52,
        [(Queue, 1000, 4)] = 52,

        // queue?takers=true: the queue (52), the unnarrowed scan of the whole board
        // (32), and one scan for every live runner (32 each), plus three statements
        // to read the fleet. That is 2 + runners scans, as the proof above counts.
        [(Takers, 100, 1)] = 119,
        [(Takers, 100, 4)] = 215,
        [(Takers, 1000, 1)] = 119,
        [(Takers, 1000, 4)] = 215,

        // attention: six scans with one runner (deadlocks, the project line, runner
        // capacity, who is waiting on a runner), and two more, 64 statements, for
        // every further one. Two statements a scan more than before HA-2795, so 12
        // more with one runner and 24 more with four. The rest is what the route
        // reads besides scans. It also grows with the board: see GrowsWithBoardSize.
        [(Attention, 100, 1)] = 243,
        [(Attention, 100, 4)] = 435,
        [(Attention, 1000, 1)] = 358,
        [(Attention, 1000, 4)] = 550,
    };

    // ---- The shape ----
    // Four lists, each asserted whole. A later story that changes the shape of a
    // read moves its name from one list to another: one line, not sixteen numbers.

    /// <summary>Reads whose count is the same on a board of 100 and a board of 1,000, at each fleet.</summary>
    private static readonly string[] FlatInBoardSize = [Next, Queue, Takers];

    /// <summary>
    /// Reads whose count is greater on the larger board, at each fleet.
    /// <para><c>/api/attention</c>: <c>Questions.ProjectAsync</c> asks which ambition each waiting
    /// question hangs under, and <c>IssueServesController.AmbitionAboveAsync</c> does it by
    /// walking up the parents of the question's issue, one statement a level. A board of 100 sends 12
    /// of those statements and a board of 1,000 sends 127, because a question in twenty is open on
    /// both. It is the only statement that differs between the two boards.</para>
    /// </summary>
    private static readonly string[] GrowsWithBoardSize = [Attention];

    /// <summary>
    /// Reads whose count is greater at four runners than at one, on each board.
    /// <para>HA-2797 (peers judged from one load, under HA-2765) makes these equal: every peer's
    /// fold is made in memory from the one load, not by a scan of its own. Whoever lands it moves
    /// the three names to <see cref="FlatInFleet"/> and edits the numbers above, in the same diff.</para>
    /// </summary>
    private static readonly string[] GrowsWithFleet = [Next, Takers, Attention];

    /// <summary>
    /// Reads whose count is the same at one runner and at four, on each board. <c>queue</c> names
    /// no runner, so it asks no peer; the queue a runner asks for scans the peers it defers to, and
    /// is not one of the sixteen.
    /// </summary>
    private static readonly string[] FlatInFleet = [Queue];

    /// <summary>One call: what it cost, and what it showed it did.</summary>
    private sealed record Call(
        string Read, int Board, int Fleet, int Statements, int Scans, string Picked,
        IReadOnlyList<ReadCounter.Statement> Sent, IReadOnlyList<string> Problems);

    [SkippableFact]
    public async Task EveryDispatchRead_CostsWhatTheTableSays_AtTwoBoardsAndTwoFleets()
    {
        Skip.IfNot(
            HatchDatabase.Available,
            "HATCH_TEST_DATABASE_URL is unset - the counts come from a real Postgres. Run `make test-api-db`, " +
            "or point the variable at a scratch database that may be dropped.");

        var calls = new List<Call>();
        foreach (var board in Boards)
        {
            var (connectionString, _) = await ReadCostSeed.SeedAsync(
                ReadCostSeed.Small, Now, board, async (db, columns, issues) => await SeedDispatchAsync(db, columns, issues));

            foreach (var fleet in Fleets)
            {
                var keys = await SetFleetAsync(connectionString, fleet);
                foreach (var read in Reads) calls.Add(await CallAsync(connectionString, keys, read, board, fleet));
            }
        }

        var failures = new List<string>();

        // 1. The reads did real work, or the counts below are of a read that
        //    stopped early.
        foreach (var call in calls.Where(c => c.Problems.Count > 0))
            failures.Add($"{Name(call)}: {string.Join("; ", call.Problems)}{Environment.NewLine}{Detail(call)}");

        // 2. The numbers.
        foreach (var call in calls)
        {
            var expected = Pinned[(call.Read, call.Board, call.Fleet)];
            if (call.Statements != expected)
                failures.Add($"{Name(call)}: expected {expected} statements, got {call.Statements}.{Environment.NewLine}{Detail(call)}");
        }

        // 3. The shape, in words.
        int Count(string read, int board, int fleet) =>
            calls.Single(c => c.Read == read && c.Board == board && c.Fleet == fleet).Statements;

        foreach (var read in Reads)
        {
            var listed = new[]
            {
                (FlatInBoardSize, "FlatInBoardSize"), (GrowsWithBoardSize, "GrowsWithBoardSize"),
            }.Where(l => l.Item1.Contains(read)).Select(l => l.Item2).ToList();
            if (listed.Count != 1)
                failures.Add($"{read} is in {(listed.Count == 0 ? "neither" : "both")} of the board-size lists");

            var fleetListed = new[] { (GrowsWithFleet, "GrowsWithFleet"), (FlatInFleet, "FlatInFleet") }
                .Where(l => l.Item1.Contains(read)).Select(l => l.Item2).ToList();
            if (fleetListed.Count != 1)
                failures.Add($"{read} is in {(fleetListed.Count == 0 ? "neither" : "both")} of the fleet lists");
        }

        foreach (var fleet in Fleets)
        {
            foreach (var read in FlatInBoardSize)
                if (Count(read, Boards[0], fleet) != Count(read, Boards[1], fleet))
                    failures.Add($"{read} is listed as flat in board size, but at {fleet} runner(s) it costs " +
                                 $"{Count(read, Boards[0], fleet)} on {Boards[0]} issues and {Count(read, Boards[1], fleet)} on {Boards[1]}.");

            foreach (var read in GrowsWithBoardSize)
                if (Count(read, Boards[1], fleet) <= Count(read, Boards[0], fleet))
                    failures.Add($"{read} is listed as growing with board size, but at {fleet} runner(s) it costs " +
                                 $"{Count(read, Boards[0], fleet)} on {Boards[0]} issues and {Count(read, Boards[1], fleet)} on {Boards[1]} - move it to FlatInBoardSize.");
        }

        foreach (var board in Boards)
        {
            foreach (var read in GrowsWithFleet)
                if (Count(read, board, Fleets[1]) <= Count(read, board, Fleets[0]))
                    failures.Add($"{read} is listed as growing with the fleet, but on {board} issues it costs " +
                                 $"{Count(read, board, Fleets[0])} at {Fleets[0]} runner and {Count(read, board, Fleets[1])} at {Fleets[1]} - move it to FlatInFleet.");

            foreach (var read in FlatInFleet)
                if (Count(read, board, Fleets[0]) != Count(read, board, Fleets[1]))
                    failures.Add($"{read} is listed as flat in the fleet, but on {board} issues it costs " +
                                 $"{Count(read, board, Fleets[0])} at {Fleets[0]} runner and {Count(read, board, Fleets[1])} at {Fleets[1]}.");
        }

        if (failures.Count > 0)
            Assert.Fail(string.Join(Environment.NewLine, failures) + Environment.NewLine + Environment.NewLine + Table(calls));
    }

    // ---- One call ----

    private static string Name(Call c) => $"{c.Read} on {c.Board} issues with {c.Fleet} live runner(s)";

    /// <summary>The statements of a call, grouped by the first table each reads, and the line to paste.</summary>
    private static string Detail(Call c) =>
        $"    scans: {c.Scans}; first clear row: {(c.Picked.Length == 0 ? "none" : c.Picked)}" + Environment.NewLine
        + "    read by table: " + string.Join(", ", c.Sent.GroupBy(s => s.Table).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} x{g.Count()}"))
        + Environment.NewLine + $"    paste: [({Constant(c.Read)}, {c.Board}, {c.Fleet})] = {c.Statements},";

    private static string Constant(string read) => read switch
    {
        Next => nameof(Next), Queue => nameof(Queue), Takers => nameof(Takers), _ => nameof(Attention),
    };

    private static string Table(IEnumerable<Call> calls)
    {
        var text = new StringBuilder("Measured, paste-ready:").AppendLine();
        foreach (var read in Reads)
            foreach (var call in calls.Where(c => c.Read == read))
                text.AppendLine($"        [({Constant(read)}, {call.Board}, {call.Fleet})] = {call.Statements},");
        return text.ToString();
    }

    private static async Task<Call> CallAsync(string connectionString, Dictionary<string, Guid> keyIds, string read, int board, int fleet)
    {
        var counter = new ReadCounter();
        await using var db = new HatchContext(counter.Options(connectionString, StatementCounter.Instance));
        var time = new FakeTimeProvider(Now);
        var h = Harness.On(db, time, keyIds);

        counter.Reset();
        var problems = new List<string>();
        var picked = "";

        using var scope = ScanTally.Begin(read);
        switch (read)
        {
            case Next:
            {
                var result = await h.Work.GetNextWork(runner: "runner-0", tag: ["build", Gpu], model: ["sonnet"]);
                picked = result.Value?.Issue.Key ?? "";
                if (result.Value is null) problems.Add($"next returned no work ({result.Result?.GetType().Name}), so the count is of a read that stopped early");
                break;
            }
            case Queue:
            {
                var result = await h.Work.GetQueue();
                picked = result.Value?.FirstOrDefault(r => r.Blocked is null)?.Issue.Key ?? "";
                if (result.Value is null || !result.Value.Any(r => r.Blocked is null))
                    problems.Add("queue has no clear row, so the count is of a scan that stopped early");
                break;
            }
            case Takers:
            {
                var result = await h.Work.GetQueue(takers: true);
                picked = result.Value?.FirstOrDefault(r => r.Blocked is null)?.Issue.Key ?? "";
                if (result.Value is null || !result.Value.Any(r => r.Blocked is null))
                    problems.Add("queue?takers=true has no clear row, so the count is of a scan that stopped early");
                break;
            }
            default:
            {
                var result = await h.Attention.GetAttention(default);
                if (result.Value is null) problems.Add($"attention was refused ({result.Result?.GetType().Name})");
                else if (result.Value.Deadlocked is not { Count: > 0 }) problems.Add("attention found no deadlock, so the ring the seed made was not read");
                break;
            }
        }

        var statements = (int)scope.Tally.Statements;
        var sent = counter.Statements;
        if (sent.Count != statements)
            problems.Add($"the application's counter saw {statements} statements and the test's saw {sent.Count}: a command ran outside the call");

        // Peers are scanned: next scans each live peer once (the row it picks is
        // one no peer can take), and the takers read scans the board twice and
        // each live runner once.
        if (read == Next && scope.Tally.Scans != fleet)
            problems.Add($"next ran {scope.Tally.Scans} scans, not {fleet} (its own, and one per live peer)");

        if (read == Takers && scope.Tally.Scans != 2 + fleet)
            problems.Add($"queue?takers=true ran {scope.Tally.Scans} scans, not {2 + fleet} (one for the board, one for the undeclared board, one per live runner)");

        return new Call(read, board, fleet, statements, scope.Tally.Scans, picked, sent, problems);
    }

    // ---- The board ----

    /// <summary>What dispatch reads that a report does not. Everything is a function of an issue's place in the list, so both boards are built the same way.</summary>
    private static async Task SeedDispatchAsync(HatchContext db, ReadCostSeed.Board board, IReadOnlyList<EfHatchIssue> issues)
    {
        // Without playbooks every row is blocked at once and a scan stops early.
        db.Playbooks.AddRange(Playbook(board.Todo, board.Doing), Playbook(board.Doing, board.Review));

        var tasks = issues.Select((issue, index) => (issue, index)).Where(t => t.issue.Type == "task").ToList();

        // A tag only runner-0 has, on a third of the tasks in progress: the rows a
        // task can be pulled from. A task in to do waits for its story to start,
        // and is never judged for a peer.
        foreach (var (issue, _) in tasks.Where(t => t.issue.StatusId == board.Doing).Where((_, n) => n % 3 == 0))
            issue.RunnerTags = RunnerTagColumn.Write([Gpu]);

        // A dependency on a tenth of the tasks, onto an earlier issue in another column.
        var made = new HashSet<(long, long)>();
        foreach (var (issue, index) in tasks.Where((_, n) => n % 10 == 5))
        {
            for (var j = index - 1 - index % 7; j >= 0; j--)
            {
                if (issues[j].StatusId == issue.StatusId) continue;
                db.Dependencies.Add(new EfHatchIssueDependency { IssueId = issue.Id, DependsOnId = issues[j].Id, CreatedBy = ReadCostSeed.Actor, CreatedAt = Now });
                made.Add((issue.Id, issues[j].Id));
                break;
            }
        }

        // One ring, so the attention read has a deadlock to find: a story A in to
        // do waits on a story B in progress, B waits on its own open child C, and
        // C waits on A. Made from issues already on the board, in the first
        // project, which the seed leaves in none of these places: A loses its
        // parent, B moves to in progress, and C moves under B.
        // Members that nothing else holds: a question or a date on the issue or
        // above it is judged before a dependency is, and would hide the ring.
        var position = issues.Select((issue, n) => (issue.Id, n)).ToDictionary(t => t.Id, t => t.n);
        var byId = issues.ToDictionary(i => i.Id);
        bool Free(EfHatchIssue issue)
        {
            for (EfHatchIssue? at = issue; at is not null; at = at.ParentId is { } up ? byId[up] : null)
                if (position[at.Id] % 20 is 7 or 13) return false;
            return true;
        }

        var inA = issues.Where(i => i.ProjectId == board.ProjectA).ToList();
        var a = inA.First(i => i.Type == "story" && i.StatusId == board.Todo && position[i.Id] % 20 is not (7 or 13));
        var b = inA.First(i => i.Type == "story" && i.StatusId == board.Inbox && Free(i));
        var c = inA.First(i => i.Type == "task" && i.StatusId == board.Todo && position[i.Id] % 20 is not (7 or 13) && !made.Any(m => m.Item1 == i.Id));
        a.ParentId = null;
        b.StatusId = board.Doing;
        c.ParentId = b.Id;
        foreach (var (waiting, waitedOn) in new[] { (a, b), (c, a) })
            db.Dependencies.Add(new EfHatchIssueDependency { IssueId = waiting.Id, DependsOnId = waitedOn.Id, CreatedBy = ReadCostSeed.Actor, CreatedAt = Now });

        for (var i = 0; i < issues.Count; i++)
        {
            // The report seed notes only the first hundred issues, and a brief
            // reads an issue's notes, so the row a read picks would cost more on
            // the small board for want of anything but size. One note on every
            // issue the seed left without one makes the boards alike.
            if (i >= ReadCostSeed.Small.OldComments)
                db.Comments.Add(new EfHatchComment
                {
                    IssueId = issues[i].Id, Author = "ann", Body = "a note", Kind = EfHatchComment.Note, CreatedAt = Now.AddDays(-100).AddHours(i),
                });

            // An open question on one issue in twenty, never answered.
            if (i % 20 == 7)
                db.Comments.Add(new EfHatchComment
                {
                    IssueId = issues[i].Id, Author = ReadCostSeed.Actor, Body = "which of the two?", Kind = EfHatchComment.Question,
                    CreatedAt = Now.AddDays(-2),
                });

            // Not ready yet, on one in twenty.
            if (i % 20 == 13) issues[i].ReadyAt = Now.AddDays(3);
        }

        // The report seed asks its sixty questions of the sixty issues after the
        // epics, whatever the size of the board, so on a small board the first
        // task in progress has one and on a large board it has none - and a
        // read that resolves it reads its questions. Every task in progress that
        // has none is given one, answered, so the row a read picks looks the same
        // on both boards.
        var asked = (await db.Comments.Where(c => c.Kind == EfHatchComment.Question).Select(c => c.IssueId).ToListAsync()).ToHashSet();
        var questions = tasks.Select(t => t.issue)
            .Where(t => t.StatusId == board.Doing && !asked.Contains(t.Id))
            .Select(t => new EfHatchComment
            {
                IssueId = t.Id, Author = ReadCostSeed.Actor, Body = "which way?", Kind = EfHatchComment.Question, CreatedAt = Now.AddDays(-5),
            }).ToList();
        db.Comments.AddRange(questions);
        await db.SaveChangesAsync();

        db.Comments.AddRange(questions.Select(q => new EfHatchComment
        {
            IssueId = q.IssueId, Author = "ann", Body = "that way", Kind = EfHatchComment.Answer, AnswersId = q.Id, CreatedAt = Now.AddDays(-5).AddMinutes(30),
        }));
    }

    private static EfHatchPlaybook Playbook(int from, int to) => new()
    {
        FromStatusId = from, ToStatusId = to, Types = "", Shape = "any", Prompt = "do the thing", Model = "sonnet", Effort = "high",
        DeniedTools = EfHatchPlaybook.WriteDeniedTools(null), CreatedAt = Now, UpdatedAt = Now,
    };

    // ---- The fleet ----

    private static Guid KeyId(int n) => new(n + 1, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]);

    private static string RunnerName(int n) => $"runner-{n}";

    /// <summary>
    /// Replaces the runners with <paramref name="fleet"/> live ones. All declare
    /// models and tags; the first has one more tag than the rest, and the rest
    /// are preferred, so the first has to scan them to know what it may take.
    /// </summary>
    private static async Task<Dictionary<string, Guid>> SetFleetAsync(string connectionString, int fleet)
    {
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);
        db.LegacyRunners.RemoveRange(await db.LegacyRunners.ToListAsync());
        db.RunnerConfigs.RemoveRange(await db.RunnerConfigs.ToListAsync());
        await db.SaveChangesAsync();

        for (var n = 0; n < fleet; n++)
        {
            db.LegacyRunners.Add(new EfHatchRunner
            {
                Name = RunnerName(n), Kind = EfHatchRunner.LoopKind, FirstSeenAt = Now, LastSeenAt = Now, State = EfHatchRunner.Running,
                Tags = n == 0 ? $"build\n{Gpu}" : "build", Models = "sonnet", Where = $"box-{n}:/work",
            });

            if (n > 0)
                db.RunnerConfigs.Add(new EfHatchRunnerConfig { ApiKeyId = KeyId(n), Preferred = true, UpdatedAt = Now });
        }

        await db.SaveChangesAsync();
        return Enumerable.Range(0, 4).ToDictionary(RunnerName, KeyId);
    }

    // ---- The controllers ----

    /// <summary>The controllers, built over the one measured context.</summary>
    private sealed class Harness
    {
        public required WorkController Work { get; init; }
        public required AttentionController Attention { get; init; }

        public static Harness On(HatchContext db, FakeTimeProvider time, Dictionary<string, Guid> keyIds)
        {
            var actors = new StubActorDirectory();
            var auth = new AuthOptions();
            var authOptions = Microsoft.Extensions.Options.Options.Create(auth);
            var hatchOptions = Microsoft.Extensions.Options.Options.Create(new HatchOptions());
            var runners = new Runners(hatchOptions, authOptions, new StubHatchRevision());

            // The keys are an in-memory database the counter does not see, and
            // the one thing in it is who the runners are, so the peers come
            // through the same join production uses.
            var coreDb = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            foreach (var (name, id) in keyIds)
                coreDb.ApiKeys.Add(new EfApiKey { Id = id, Name = name, Prefix = "hatch_ak_", Hash = [], CreatedAt = Now });
            coreDb.SaveChanges();

            var keys = new AuthService(coreDb, authOptions, time, NullLogger<AuthService>.Instance);
            var sessions = new RunnerSessions(runners);
            var caller = new StubCallerIdentity();
            var claims = TestClaims.With();
            var dispatch = new Dispatch(db, actors, claims, time, keys, sessions);

            var status = new BackupStatus(time);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Hatch"] = "Host=db-test;Port=5433;Username=hatchuser;Password=s3cret;Database=hatchdb",
            }).Build();
            var backup = new BackupService(
                Microsoft.Extensions.Options.Options.Create(new HatchOptions { BackupPath = "" }), configuration,
                new NoBackupProcess(), status, time, NullLogger<BackupService>.Instance);

            return new Harness
            {
                Work = new WorkController(
                    db, actors, claims, time, hatchOptions, new RankService(db), caller, runners, keys, sessions,
                    new ChangelogMessages(new ChatOutbox(db, time), new ChatLinks(hatchOptions), db)),
                Attention = new AttentionController(
                    db, actors, runners, time, dispatch, backup, keys, new GooseWatch(time), hatchOptions, caller, sessions, authOptions),
            };
        }
    }

    private sealed class NoBackupProcess : IBackupProcess
    {
        public Task<BackupProcessResult> RunAsync(BackupProcessStart start, Stream stdout, CancellationToken ct) =>
            Task.FromResult(new BackupProcessResult(0, ""));
    }
}

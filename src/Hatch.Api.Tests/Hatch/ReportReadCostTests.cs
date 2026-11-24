using System.Text;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Api.Tests.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What every report read costs the database, at two sizes of history, in one
/// table. Anyone who changes a read edits a line of it, and so sees in the
/// same diff what the change did to the number of statements and rows.
///
/// <para>Counts, not times: a count is the same on every run, and a time is not.
/// Three are taken for each read. <b>Statements</b> are the commands sent.
/// <b>Rows</b> are what the database returned, with the rows of statements that
/// load <c>Issues</c> stated apart, since one row per issue per read is allowed.
/// <b>Read</b> is what Postgres itself read to answer, per table, which is the
/// only number that sees a scan whose result a <c>WHERE</c> threw away.</para>
///
/// <para><b>The two sizes.</b> Both hold 200 issues and the same thirty days of
/// work: about 500 moves, 150 claim events, 60 questions with answers, 300
/// sessions and the leaderboard-sized rest (see <see cref="ReadCostSeed"/>).
/// The large one holds, from before that window, six times the sessions and
/// comments and, for each issue, five more trips to review and back (so about
/// five times the moves) - the history a long-running board accumulates.</para>
///
/// <para><b>How the test reads.</b> A read in <see cref="Same"/> costs the same
/// at both sizes, and the test says what. A read in <see cref="Grows"/> costs
/// more at the large size, and the test says both costs. The test fails when a
/// read in <see cref="Grows"/> stops growing, so the list has to be edited, and
/// when a read outside it starts. A read that gets cheaper is a line moved from
/// the one list to the other.</para>
///
/// <para>The health reads build their API-key checks on an in-memory
/// <c>AppDbContext</c>, which this counter does not see; they are not part of
/// the hatch database.</para>
///
/// <para>It points at <c>HATCH_TEST_DATABASE_URL</c>, which is dropped and
/// recreated, so it must never name a database anybody uses.</para>
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class ReportReadCostTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 31, 12, 0, 0, TimeSpan.Zero);

    private static readonly string From = Now.AddDays(-ReadCostSeed.WindowDays).ToString("O");
    private static readonly string To = Now.ToString("O");

    /// <summary>What a read cost: statements, issue loads, rows, rows outside the issue loads, and rows Postgres read per table.</summary>
    public sealed record Row(int Statements, int IssueLoads, long Rows, long RowsOutsideIssueLoads, string Read);

    // ---- The table ----
    // Each cost is: statements, issue loads, rows returned, rows returned
    // outside the issue loads, rows Postgres read per table. Paste-ready: a
    // failure prints the measured table in these shapes.
    //
    // Reading it: every read here loads Issues, which is allowed once per
    // issue, so "outside the issue loads" is the number to watch. The reads
    // that return the same rows at both sizes and still read more are the ones
    // whose WHERE throws rows away after reading them.

    /// <summary>Reads that cost the same at both sizes.</summary>
    private static readonly Dictionary<string, Row> Same = new()
    {
        ["GET /api/reports/consumption"] = new(5, 2, 512, 302, "Issues=1000;Projects=2;WorkLogEntries=1500"),
    };

    /// <summary>Reads that still grow with history: the small cost, then the large.</summary>
    private static readonly Dictionary<string, (Row Small, Row Large)> Grows = new()
    {
        ["GET /api/reports/timeline"] = (
            new(11, 3, 1536, 956, "Comments=220;IssueEvents=1294;Issues=1001;Projects=8;Statuses=24"),
            new(11, 3, 3536, 2956, "Comments=720;IssueEvents=3294;Issues=1200;Projects=8;Statuses=24")),
        ["GET /api/reports/cumulative-flow"] = (
            new(3, 1, 1150, 950, "IssueEvents=1294;Issues=400;Projects=2;Statuses=6"),
            new(3, 1, 3150, 2950, "IssueEvents=3294;Issues=400;Projects=2;Statuses=6")),
        ["GET /api/reports/flow"] = (
            new(8, 1, 1645, 1445, "Comments=440;IssueEvents=3882;Issues=800;Projects=2;Statuses=6;WorkLogEntries=500"),
            new(8, 1, 3645, 3445, "Comments=780;IssueEvents=7242;Issues=1000;Projects=2;Statuses=6;WorkLogEntries=1500")),
        ["GET /api/reports/changelog (epic level)"] = (
            new(13, 5, 417, 12, "Comments=720;Issues=1024;Projects=3;Statuses=36"),
            new(13, 5, 417, 12, "Comments=2221;Issues=1223;Projects=3;Statuses=36")),
        ["GET /api/reports/changelog (story level)"] = (
            new(15, 7, 621, 12, "Comments=720;Issues=1244;Projects=3;Statuses=42"),
            new(15, 7, 621, 12, "Comments=2221;Issues=1443;Projects=3;Statuses=42")),
        ["GET /api/reports/changelog (loose=true)"] = (
            new(15, 7, 421, 12, "Comments=720;Issues=1251;Projects=4;Statuses=42"),
            new(15, 7, 421, 12, "Comments=2221;Issues=1450;Projects=4;Statuses=42")),
        ["GET /api/reports/activity"] = (
            new(12, 3, 1735, 1161, "Comments=220;IssueEvents=3088;Issues=1401;Projects=8;Statuses=18"),
            new(12, 3, 1735, 1161, "Comments=720;IssueEvents=4448;Issues=1600;Projects=8;Statuses=18")),
        ["GET /api/reports/consumption.csv"] = (
            new(3, 2, 510, 300, "Issues=600;Projects=2;WorkLogEntries=500"),
            new(3, 2, 510, 300, "Issues=600;Projects=2;WorkLogEntries=1500")),
        ["GET /api/work-log/history"] = (
            new(3, 0, 302, 302, "Issues=600;WorkLogEntries=1500"),
            new(3, 0, 302, 302, "Issues=600;WorkLogEntries=4500")),
        ["GET /api/work-log/sessions"] = (
            new(3, 0, 302, 302, "Issues=600;Projects=2;WorkLogEntries=1500"),
            new(3, 0, 302, 302, "Issues=600;Projects=2;WorkLogEntries=4500")),
        ["GET /api/work-log/by-playbook"] = (
            new(2, 0, 306, 306, "Issues=200;Statuses=6;WorkLogEntries=500"),
            new(2, 0, 306, 306, "Issues=200;Statuses=6;WorkLogEntries=1500")),
        ["GET /api/board/health"] = (
            new(38, 7, 3075, 1982, "Comments=1782;IssueEvents=19856;Issues=3002;Projects=16;Statuses=30;WorkLogEntries=500"),
            new(38, 7, 5075, 3982, "Comments=3963;IssueEvents=109146;Issues=3602;Projects=16;Statuses=30;WorkLogEntries=1500")),
        ["GET /api/board/health?project"] = (
            new(40, 7, 3084, 2020, "Comments=1782;IssueEvents=25160;Issues=3202;Projects=24;Statuses=30;WorkLogEntries=500"),
            new(40, 7, 5084, 4020, "Comments=3963;IssueEvents=143250;Issues=3802;Projects=24;Statuses=30;WorkLogEntries=1500")),
        ["ColumnNormals.ForAsync"] = (
            new(2, 1, 1144, 944, "IssueEvents=1294;Issues=200;Projects=2"),
            new(2, 1, 3144, 2944, "IssueEvents=3294;Issues=200;Projects=2")),

    };

    private sealed record Read(string Name, Func<Harness, Task> Call);

    private static readonly Read[] Reads =
    [
        new("GET /api/reports/timeline", h => Ok(h.Reports.GetTimeline(From, To, null, null, default))),
        new("GET /api/reports/cumulative-flow", h => Ok(h.Reports.GetCumulativeFlow(From, To, null, default))),
        new("GET /api/reports/flow", h => Ok(h.Reports.GetFlow(From, To, null, null, default))),
        new("GET /api/reports/changelog (epic level)",
            h => Ok(h.Reports.GetChangelogReport(From, To, null, null, 0, null, null, default))),
        new("GET /api/reports/changelog (story level)",
            h => Ok(h.Reports.GetChangelogReport(From, To, null, null, 0, "story", null, default))),
        new("GET /api/reports/changelog (loose=true)",
            h => Ok(h.Reports.GetChangelogReport(From, To, null, null, 0, null, "true", default))),
        new("GET /api/reports/activity",
            h => Ok(h.Reports.GetActivityReport(ReadCostSeed.Actor, From, To, null, 0, 30, 15, 3, false, default))),
        new("GET /api/reports/consumption",
            h => Ok(h.Reports.GetConsumption(From, To, null, null, 100, 0, null, null, default))),
        new("GET /api/reports/consumption.csv", async h =>
        {
            var result = await h.Reports.GetConsumptionCsv(From, To, null, null, 0, null, null, default);
            Assert.IsType<FileContentResult>(result);
        }),
        new("GET /api/work-log/history", h => Ok(h.WorkLog.GetHistory(From, To, null, 0, null, null, null, default))),
        new("GET /api/work-log/sessions", h => Ok(h.WorkLog.GetSessions(From, To, null, null, 20, null, null, default))),
        new("GET /api/work-log/by-playbook", h => Ok(h.WorkLog.GetByPlaybook(From, To, null, null, null, default))),
        new("GET /api/board/health", h => Ok(h.Health.GetBoardHealth("24h", null, default))),
        new("GET /api/board/health?project", h => Ok(h.Health.GetBoardHealth("24h", [ReadCostSeed.KeyA], default))),
        new("ColumnNormals.ForAsync", async h => await new ColumnNormals().ForAsync(h.Db, h.Statuses, Now, default)),
    ];

    private static async Task Ok<T>(Task<ActionResult<T>> call)
    {
        var result = await call;
        Assert.True(result.Value is not null,
            $"the read was refused ({result.Result?.GetType().Name}: {(result.Result as BadRequestObjectResult)?.Value}) - " +
            "a refusal costs nothing and would be written down as a cheap read");
    }

    [SkippableFact]
    public async Task EveryReportRead_CostsWhatTheTableSays_AtTwoSizesOfHistory()
    {
        Skip.IfNot(
            HatchDatabase.Available,
            "HATCH_TEST_DATABASE_URL is unset - the counts come from a real Postgres. Run `make test-api-db`, " +
            "or point the variable at a scratch database that may be dropped.");

        var small = await MeasureAllAsync(ReadCostSeed.Small);
        var large = await MeasureAllAsync(ReadCostSeed.Large);

        var failures = new List<string>();

        // The two boards differ only before the window. Said out loud here, so
        // a change to the seed that breaks it breaks this and not the numbers.
        var (sm, lg) = (sizes["small"], sizes["large"]);
        if (sm.WindowMoves != lg.WindowMoves || sm.WindowSessions != lg.WindowSessions)
            failures.Add($"the window differs between the sizes: {sm} and {lg}");
        if (lg.OldMoves < 5 * sm.OldMoves || lg.OldSessions < 5 * sm.OldSessions || lg.OldComments < 5 * sm.OldComments)
            failures.Add($"the large size holds less than five times the small one's history: {sm} and {lg}");
        if (sm.WindowSessions < 10 * ReadCostSeed.WindowDays)
            failures.Add($"the window holds {sm.WindowSessions} sessions, fewer than ten per day bucket");

        foreach (var read in Reads)
        {
            var s = small[read.Name];
            var l = large[read.Name];

            var listedSame = Same.TryGetValue(read.Name, out var same);
            var listedGrows = Grows.TryGetValue(read.Name, out var grows);

            if (listedSame == listedGrows)
                failures.Add($"{read.Name} is in {(listedSame ? "both" : "neither")} of the lists");
            else if (listedSame)
            {
                if (s.Row != l.Row) failures.Add($"{read.Name} is listed as the same at both sizes, but grew: {s.Row} -> {l.Row}");
                else if (s.Row != same) failures.Add($"{read.Name} costs {s.Row}, the table says {same}");
            }
            else
            {
                if (s.Row == l.Row) failures.Add($"{read.Name} is listed as growing, but costs {s.Row} at both sizes - move it to the list that does not");
                else if (s.Row != grows.Small || l.Row != grows.Large)
                    failures.Add($"{read.Name} costs {s.Row} then {l.Row}, the table says {grows.Small} then {grows.Large}");
            }
        }

        if (failures.Count > 0)
            Assert.Fail(
                string.Join(Environment.NewLine, failures) + Environment.NewLine + Environment.NewLine
                + Table(small, large) + Environment.NewLine + Statements(large));
    }

    private sealed record Measurement(Row Row, IReadOnlyList<ReadCounter.Statement> Statements);

    /// <summary>What a seed holds: its window and its history before the window.</summary>
    private sealed record Seeded(long WindowMoves, long OldMoves, long WindowSessions, long OldSessions, long OldComments);

    private static readonly Dictionary<string, Seeded> sizes = [];

    private static async Task<Seeded> CountSeedAsync(string connectionString)
    {
        var windowStart = Now.AddDays(-ReadCostSeed.WindowDays);
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);
        var moves = db.IssueEvents.Where(e => e.Kind == EfHatchIssueEvent.StatusChanged);
        return new Seeded(
            await moves.CountAsync(e => e.At >= windowStart), await moves.CountAsync(e => e.At < windowStart),
            await db.WorkLog.CountAsync(w => w.StartedAt >= windowStart), await db.WorkLog.CountAsync(w => w.StartedAt < windowStart),
            await db.Comments.CountAsync(c => c.Kind == EfHatchComment.Note && c.CreatedAt < windowStart));
    }

    private static async Task<Dictionary<string, Measurement>> MeasureAllAsync(ReadCostSeed.Size size)
    {
        var (connectionString, _) = await ReadCostSeed.SeedAsync(size, Now);
        sizes[size.Name] = await CountSeedAsync(connectionString);

        // Not counted: the statuses a read is handed rather than loads.
        await using var setup = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);
        var statuses = await setup.Statuses.AsNoTracking().ToListAsync();

        var measured = new Dictionary<string, Measurement>();

        foreach (var read in Reads)
        {
            // Every read gets a context, controllers and caches of its own, so
            // nothing a read before it left behind hides what this one does.
            var m = await ReadCounter.MeasureAsync(connectionString, async db =>
                await read.Call(Harness.On(db, statuses)));

            var c = m.Cost;
            measured[read.Name] = new Measurement(new Row(c.Statements, c.IssueLoads, c.Rows, c.RowsOutsideIssueLoads, c.Read), m.Statements);
        }

        return measured;
    }

    private static string Table(Dictionary<string, Measurement> small, Dictionary<string, Measurement> large)
    {
        var same = new StringBuilder();
        var grows = new StringBuilder();

        foreach (var read in Reads)
        {
            var (s, l) = (small[read.Name].Row, large[read.Name].Row);
            if (s == l)
                same.AppendLine($"        [\"{read.Name}\"] = {Literal(s)},");
            else
                grows.AppendLine($"        [\"{read.Name}\"] = ({Environment.NewLine}            {Literal(s)},{Environment.NewLine}            {Literal(l)}),");
        }

        return "Measured, paste-ready:" + Environment.NewLine
            + "Same:" + Environment.NewLine + same + "Grows:" + Environment.NewLine + grows;
    }

    private static string Literal(Row r) =>
        $"new({r.Statements}, {r.IssueLoads}, {r.Rows}, {r.RowsOutsideIssueLoads}, \"{r.Read}\")";

    private static string Statements(Dictionary<string, Measurement> measured)
    {
        var text = new StringBuilder("Statements at the large size, by first table:").AppendLine();

        foreach (var read in Reads)
        {
            text.AppendLine(read.Name);
            foreach (var g in measured[read.Name].Statements.GroupBy(s => s.Table).OrderBy(g => g.Key, StringComparer.Ordinal))
                text.AppendLine($"    {g.Key}: {g.Count()} statement(s), {g.Sum(s => s.Rows)} rows");
        }

        return text.ToString();
    }

    // ---- The counter itself ----

    [SkippableFact]
    public async Task TheCounter_CountsStatementsAndTheRowsTheyReturn()
    {
        var connectionString = await PrepareThreeProjectsAsync();

        var measured = await ReadCounter.MeasureAsync(
            connectionString, async db => Assert.Equal(3, (await db.Projects.AsNoTracking().ToListAsync()).Count));

        Assert.Equal(1, measured.Cost.Statements);
        Assert.Equal(3, measured.Cost.Rows);
        Assert.Equal(0, measured.Cost.IssueLoads);
    }

    [SkippableFact]
    public async Task TheCounter_ReportsASequentialScanAsExactlyTheRowsInTheTable()
    {
        var connectionString = await PrepareThreeProjectsAsync();

        var measured = await ReadCounter.MeasureAsync(
            connectionString, async db => await db.Projects.AsNoTracking().ToListAsync());

        Assert.Equal("Projects=3", measured.Cost.Read);
    }

    private static async Task<string> PrepareThreeProjectsAsync()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - the counts come from a real Postgres.");

        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);
        await db.Database.OpenConnectionAsync();
        db.Projects.AddRange(
            new EfHatchProject { Key = "ONE", Name = "one", CreatedAt = Now },
            new EfHatchProject { Key = "TWO", Name = "two", CreatedAt = Now },
            new EfHatchProject { Key = "THR", Name = "three", CreatedAt = Now });
        await db.SaveChangesAsync();
        await ReadCounter.FlushAsync(db.Database.GetDbConnection());
        return connectionString;
    }

    /// <summary>The controllers, built over the one measured context.</summary>
    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required List<EfHatchStatus> Statuses { get; init; }
        public required ReportsController Reports { get; init; }
        public required WorkLogController WorkLog { get; init; }
        public required BoardHealthController Health { get; init; }

        public static Harness On(HatchContext db, List<EfHatchStatus> statuses)
        {
            var time = new FakeTimeProvider(Now);
            var actors = new StubActorDirectory();
            var auth = new AuthOptions();
            var authOptions = Microsoft.Extensions.Options.Options.Create(auth);
            var runners = new Runners(
                Microsoft.Extensions.Options.Options.Create(new HatchOptions()), authOptions, new StubHatchRevision());

            // The keys are checked against an in-memory database. The counter
            // does not see it, and none of it is this board.
            var coreDb = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var keys = new AuthService(coreDb, authOptions, time, NullLogger<AuthService>.Instance);
            var sessions = new RunnerSessions(runners);

            return new Harness
            {
                Db = db,
                Statuses = statuses,
                Reports = new ReportsController(db, actors, TestClaims.With(), time),
                WorkLog = new WorkLogController(db, time),
                Health = new BoardHealthController(
                    db, actors, runners, new Dispatch(db, actors, TestClaims.With(), time, keys, sessions), time,
                    new ColumnNormals(), new WaitHistory(), new StubCallerIdentity(), authOptions, keys,
                    new RunnerStateLog(runners), sessions),
            };
        }
    }
}

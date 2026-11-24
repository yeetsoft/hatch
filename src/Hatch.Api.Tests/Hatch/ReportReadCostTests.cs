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
/// <para><b>How the test reads.</b> A read in <see cref="Same"/> does not grow
/// with history: it sends the same statements and returns the same rows at both
/// sizes, and Postgres reads no more of any table at the large size than at the
/// small one. It may read less, because on a table of a few hundred rows the
/// planner reads the whole table where it would use an index on a large one, so
/// both costs are written down. A read in <see cref="Grows"/> costs more at the
/// large size, and the test says both costs. The test fails when a read in
/// <see cref="Grows"/> stops growing, so the list has to be edited, and when a
/// read outside it starts. A read that gets cheaper is a line moved from the
/// one list to the other.</para>
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

    /// <summary>The limit the leaderboard read below is asked for.</summary>
    private const int LeaderboardLimit = 20;

    /// <summary>The podium the leaderboard asks for beside its table.</summary>
    private const int LeaderboardTop = 5;

    private static readonly DateTimeOffset WindowStart = Now.AddDays(-ReadCostSeed.WindowDays);

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

    /// <summary>Reads that do not grow with history: the small cost, then the large.</summary>
    private static readonly Dictionary<string, (Row Small, Row Large)> Same = new()
    {
        // The rows in the range, off the end-time index (HA-2782), and the two bounds. The small
        // board reads its whole table, for the reason in the summary above.
        ["GET /api/reports/consumption"] = (
            new(5, 2, 307, 97, "Issues=400;Projects=2;WorkLogEntries=550"),
            new(5, 2, 307, 97, "Issues=400;Projects=2;WorkLogEntries=350")),
        ["GET /api/reports/consumption.csv"] = (
            new(3, 2, 257, 47, "Issues=400;Projects=2;WorkLogEntries=500"),
            new(3, 2, 257, 47, "Issues=400;Projects=2;WorkLogEntries=300")),
        // Summed by the database: a row per bucket that holds a session, and the
        // two bounds. Reads the window and one row off each end of the end-time index.
        ["GET /api/work-log/history"] = (
            new(3, 0, 32, 32, "Issues=200;WorkLogEntries=502"),
            new(3, 0, 32, 32, "Issues=200;WorkLogEntries=302")),
        // The totals, the page, the paths read and the two bounds: at most
        // twenty rows of page, one of totals, one per distinct path, two bounds.
        ["GET /api/work-log/sessions"] = (
            new(5, 0, 24, 24, "Issues=600;Projects=1;WorkLogEntries=1502"),
            new(5, 0, 24, 24, "Issues=600;Projects=1;WorkLogEntries=902")),
        // The same, and the heaviest five: one more read, five more rows, and
        // still the totals, the paths and the bounds once - what the
        // leaderboard asks instead of the line above twice.
        ["GET /api/work-log/sessions?top"] = (
            new(6, 0, 29, 29, "Issues=800;Projects=2;WorkLogEntries=2002"),
            new(6, 0, 29, 29, "Issues=800;Projects=2;WorkLogEntries=1202")),
        ["GET /api/work-log/by-playbook"] = (
            new(2, 0, 306, 306, "Issues=200;Statuses=6;WorkLogEntries=500"),
            new(2, 0, 306, 306, "Issues=200;Statuses=6;WorkLogEntries=300")),
        // The health poll's own trail reads (HA-2783): the window off the event indexes, and for each
        // issue and kind one probe of (IssueId, Kind, At) for the newest earlier event. The same rows
        // at both sizes, and the large board reads no more of any table than the small one.
        ["BoardHealthTrail.MovesAsync"] = (
            new(3, 1, 904, 704, "IssueEvents=704;Issues=600"),
            new(3, 1, 904, 704, "IssueEvents=704;Issues=600")),
        ["BoardHealthTrail.ReadAsync (claims)"] = (
            new(3, 2, 550, 150, "IssueEvents=150;Issues=600"),
            new(3, 2, 550, 150, "IssueEvents=150;Issues=600")),
        ["BoardHealthTrail.LevelsAsync"] = (
            new(3, 1, 450, 250, "IssueEvents=250;Issues=600"),
            new(3, 1, 450, 250, "IssueEvents=250;Issues=600")),
        ["BoardHealthTrail.RecordsAsync"] = (
            new(3, 1, 450, 250, "IssueEvents=250;Issues=600"),
            new(3, 1, 450, 250, "IssueEvents=250;Issues=600")),
        ["BoardHealthTrail.RunnerStatesAsync"] = (
            new(4, 0, 21, 21, "RunnerStates=96"),
            new(4, 0, 21, 21, "RunnerStates=21")),
        // The status changes the range needs and no more (HA-2784): the window, the newest move before
        // it per issue, and for an issue that has not moved by its end the first one after. Two loads
        // of Issues: the report's rows, and BoardHealthTrail's probe, one row per issue each.
        ["GET /api/reports/cumulative-flow"] = (
            new(5, 2, 1110, 710, "IssueEvents=704;Issues=800;Projects=2;Statuses=6"),
            new(5, 2, 1110, 710, "IssueEvents=704;Issues=800;Projects=2;Statuses=6")),
    };

    /// <summary>
    /// Whether the large cost is more than the small one: a different number of
    /// statements or rows, or more rows read of any table. Less read is not
    /// growth - see the class summary.
    /// </summary>
    private static bool Grew(Row small, Row large)
    {
        if (small.Statements != large.Statements || small.IssueLoads != large.IssueLoads
            || small.Rows != large.Rows || small.RowsOutsideIssueLoads != large.RowsOutsideIssueLoads)
            return true;

        var before = ReadPerTable(small.Read);
        return ReadPerTable(large.Read).Any(t => t.Value > before.GetValueOrDefault(t.Key));
    }

    private static Dictionary<string, long> ReadPerTable(string read) =>
        read.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('='))
            .ToDictionary(part => part[0], part => long.Parse(part[1]));

    /// <summary>Reads that still grow with history: the small cost, then the large.</summary>
    private static readonly Dictionary<string, (Row Small, Row Large)> Grows = new()
    {
        // Its status changes are bounded to the range (HA-2784): IssueEvents is the same at both sizes,
        // and the test below holds it so. It stays here for the cards, which read Comments, and the
        // priority tree, which reads Issues, both with no range. Four loads of Issues: the report's
        // rows, BoardHealthTrail's probe, the cards and the priority tree.
        ["GET /api/reports/timeline"] = (
            new(13, 4, 1496, 716, "Comments=220;IssueEvents=704;Issues=1401;Projects=8;Statuses=24"),
            new(13, 4, 1496, 716, "Comments=720;IssueEvents=704;Issues=1600;Projects=8;Statuses=24")),
        // Read for its window alone (HA-2785): the moves the window needs, the questions asked in it or
        // answered in it, and the drops and the sessions as counts, a row for each of the two. Rows
        // returned are the same at both sizes. It stays here and not in Same for one figure: the query
        // filters join Issues in every statement, and at the large size Postgres scans Issues for the
        // question reads where the small one does not, 400 rows more. The test below holds apart what the
        // report is meant to keep equal. Two loads of Issues: the report's rows and the trail's probe.
        ["GET /api/reports/flow"] = (
            new(10, 2, 1233, 833, "Comments=561;IssueEvents=704;Issues=1000;Projects=2;Statuses=6;WorkLogEntries=500"),
            new(10, 2, 1233, 833, "Comments=241;IssueEvents=704;Issues=1400;Projects=2;Statuses=6;WorkLogEntries=300")),
        // QuestionTrail.ReadAsync with no range, so every question and every answer.
        ["GET /api/reports/changelog (epic level)"] = (
            new(13, 5, 417, 12, "Comments=720;Issues=1024;Projects=3;Statuses=36"),
            new(13, 5, 417, 12, "Comments=1561;Issues=1223;Projects=3;Statuses=36")),
        ["GET /api/reports/changelog (story level)"] = (
            new(15, 7, 621, 12, "Comments=720;Issues=1244;Projects=3;Statuses=42"),
            new(15, 7, 621, 12, "Comments=1561;Issues=1443;Projects=3;Statuses=42")),
        ["GET /api/reports/changelog (loose=true)"] = (
            new(15, 7, 421, 12, "Comments=720;Issues=1251;Projects=4;Statuses=42"),
            new(15, 7, 421, 12, "Comments=1561;Issues=1450;Projects=4;Statuses=42")),
        // Every status change with no time bound (HA-2787).
        ["GET /api/reports/activity"] = (
            new(12, 3, 1861, 1281, "Comments=220;IssueEvents=4088;Issues=1401;Projects=8;Statuses=18"),
            new(12, 3, 1861, 1281, "Comments=720;IssueEvents=6588;Issues=1600;Projects=8;Statuses=18")),
        // The trail reads are bounded (HA-2783, see the BoardHealthTrail lines above). What still
        // grows is the status changes with no time bound, ColumnNormals (HA-2786) and the dispatch
        // and family-gate reads (HA-2790). One statement and one issue load fewer than before
        // HA-2796: FamilyGate.ForAsync reads every row off the tree it is handed rather than
        // Issues a second time.
        ["GET /api/board/health"] = (
            new(60, 16, 5915, 3022, "Comments=1782;IssueEvents=3845;Issues=5202;LegacyRunners=3;Projects=20;RunnerStates=27;Statuses=30"),
            new(60, 16, 7915, 5022, "Comments=1923;IssueEvents=7257;Issues=5802;LegacyRunners=3;Projects=20;RunnerStates=3;Statuses=30")),
        ["GET /api/board/health?project"] = (
            new(64, 17, 5876, 2972, "Comments=1782;IssueEvents=3889;Issues=5604;LegacyRunners=3;Projects=26;RunnerStates=27;Statuses=30"),
            new(64, 17, 7876, 4972, "Comments=1923;IssueEvents=7301;Issues=6204;LegacyRunners=3;Projects=26;RunnerStates=3;Statuses=30")),
        ["ColumnNormals.ForAsync"] = (
            new(2, 1, 1144, 944, "IssueEvents=1794;Issues=200;Projects=2"),
            new(2, 1, 3144, 2944, "IssueEvents=5314;Issues=200;Projects=2")),
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
        new("GET /api/work-log/sessions", h => Ok(h.WorkLog.GetSessions(From, To, null, null, LeaderboardLimit, null, null, null, default))),
        new("GET /api/work-log/sessions?top",
            h => Ok(h.WorkLog.GetSessions(From, To, null, null, LeaderboardLimit, null, null, LeaderboardTop, default))),
        new("GET /api/work-log/by-playbook", h => Ok(h.WorkLog.GetByPlaybook(From, To, null, null, null, default))),
        new("GET /api/board/health", h => Ok(h.Health.GetBoardHealth("24h", null, default))),
        new("GET /api/board/health?project", h => Ok(h.Health.GetBoardHealth("24h", [ReadCostSeed.KeyA], default))),
        new("ColumnNormals.ForAsync", async h => await new ColumnNormals().ForAsync(h.Db, h.Statuses, Now, default)),
        // The poll's own trail reads, apart from the rest of the poll (HA-2783).
        new("BoardHealthTrail.MovesAsync", async h => await BoardHealthTrail.MovesAsync(h.Db, WindowStart, null, default)),
        new("BoardHealthTrail.ReadAsync (claims)", async h => await BoardHealthTrail.ReadAsync(
            h.Db, [EfHatchIssueEvent.ClaimTaken, EfHatchIssueEvent.ClaimReleased], WindowStart, null, null, default,
            olderOnlyIf: EfHatchIssueEvent.ClaimTaken)),
        new("BoardHealthTrail.LevelsAsync", async h => await BoardHealthTrail.LevelsAsync(h.Db, WindowStart, default)),
        new("BoardHealthTrail.RecordsAsync", async h => await BoardHealthTrail.RecordsAsync(h.Db, WindowStart, null, default)),
        new("BoardHealthTrail.RunnerStatesAsync", async h => await BoardHealthTrail.RunnerStatesAsync(
            h.Db, [.. Enumerable.Range(1, ReadCostSeed.Runners).Select(r => $"runner-{r}")], WindowStart, default)),
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
        if (sm.WindowMoves != lg.WindowMoves || sm.WindowSessions != lg.WindowSessions
            || sm.WindowLevels != lg.WindowLevels || sm.WindowRecords != lg.WindowRecords
            || sm.WindowRunnerStates != lg.WindowRunnerStates)
            failures.Add($"the window differs between the sizes: {sm} and {lg}");
        if (lg.OldMoves < 5 * sm.OldMoves || lg.OldSessions < 5 * sm.OldSessions || lg.OldComments < 5 * sm.OldComments
            || lg.OldLevels < 5 * sm.OldLevels || lg.OldRecords < 5 * sm.OldRecords || lg.OldRunnerStates < 5 * sm.OldRunnerStates)
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
                if (Grew(s.Row, l.Row)) failures.Add($"{read.Name} is listed as not growing, but grew: {s.Row} -> {l.Row}");
                else if (s.Row != same.Small || l.Row != same.Large)
                    failures.Add($"{read.Name} costs {s.Row} then {l.Row}, the table says {same.Small} then {same.Large}");
            }
            else
            {
                if (!Grew(s.Row, l.Row)) failures.Add($"{read.Name} is listed as growing, but costs {s.Row} then {l.Row} - move it to the list that does not");
                else if (s.Row != grows.Small || l.Row != grows.Large)
                    failures.Add($"{read.Name} costs {s.Row} then {l.Row}, the table says {grows.Small} then {grows.Large}");
            }
        }

        // Neither consumption route sends a candidate or descendant id list -
        // the bound reads page instead (see ReportsController.BoundSessionAsync)
        // - and this request names no project, so no array parameter should
        // reach the database at all.
        foreach (var name in new[] { "GET /api/reports/consumption", "GET /api/reports/consumption.csv" })
        {
            var worst = Math.Max(
                small[name].Statements.Max(s => s.MaxArrayParameter),
                large[name].Statements.Max(s => s.MaxArrayParameter));
            if (worst != 0)
                failures.Add($"{name} sends an array parameter of length {worst} - no id list should reach the database here");
        }

        // The leaderboard is summed by the database, so what it returns does not
        // grow with the sessions in the window: history is a row per bucket that
        // holds a session plus the two bounds, and sessions is the page, one row
        // of totals, a row per distinct path read, and the two bounds. Checked
        // apart from the table, which is about what Postgres read - these rows do not grow.
        foreach (var (size, measured) in new[] { ("small", small), ("large", large) })
        {
            var history = measured["GET /api/work-log/history"].Row.Rows;
            var historyBound = ReadCostSeed.WindowDays + 1 + 2;
            if (history > historyBound)
                failures.Add($"history returned {history} rows at the {size} size, more than a row per bucket and the two bounds ({historyBound})");

            var sessions = measured["GET /api/work-log/sessions"].Row.Rows;
            var sessionsBound = LeaderboardLimit + 1 + ReadCostSeed.DistinctReadPaths + 2;
            if (sessions > sessionsBound)
                failures.Add($"sessions returned {sessions} rows at the {size} size, more than the page, the totals, the paths read and the two bounds ({sessionsBound})");

            // The one request the leaderboard makes for both lists: the same,
            // and the top five besides. What is held equal here is what it
            // returns and how many statements it takes.
            var withTop = measured["GET /api/work-log/sessions?top"].Row;
            var withTopBound = LeaderboardLimit + LeaderboardTop + 1 + ReadCostSeed.DistinctReadPaths + 2;
            if (withTop.Rows > withTopBound)
                failures.Add($"sessions?top returned {withTop.Rows} rows at the {size} size, more than the page, the top, the totals, the paths read and the two bounds ({withTopBound})");
        }

        var topSmall = small["GET /api/work-log/sessions?top"].Row;
        var topLarge = large["GET /api/work-log/sessions?top"].Row;
        if (topSmall.Statements != topLarge.Statements || topSmall.Rows != topLarge.Rows)
            failures.Add($"sessions?top costs {topSmall.Statements} statements and {topSmall.Rows} rows at the small size and {topLarge.Statements} and {topLarge.Rows} at the large - it should be the same");

        // The reports read the moves their range needs and no more (HA-2784, HA-2785), so
        // the rows of IssueEvents are the same at both sizes. Timeline stays in
        // the Grows list for its cards and priority tree, so the shared Grew
        // check cannot see this figure alone, and it is held apart. Issues is loaded as many
        // times as the lines in the table say: the report's rows and the trail's
        // probe for cumulative flow (2), and those and the cards and the priority
        // tree for the timeline (4).
        foreach (var (name, loads) in new[]
                 { ("GET /api/reports/timeline", 4), ("GET /api/reports/cumulative-flow", 2), ("GET /api/reports/flow", 2) })
        {
            var (before, after) = (small[name].Row, large[name].Row);
            var (eventsBefore, eventsAfter) = (
                ReadPerTable(before.Read).GetValueOrDefault("IssueEvents"),
                ReadPerTable(after.Read).GetValueOrDefault("IssueEvents"));
            if (eventsBefore != eventsAfter || eventsBefore == 0)
                failures.Add($"{name} read {eventsBefore} IssueEvents rows at the small size and {eventsAfter} at the large - " +
                    "the same number is the point, and none would mean the read was not measured");
            if (before.IssueLoads != loads || after.IssueLoads != loads)
                failures.Add($"{name} loads Issues {before.IssueLoads} then {after.IssueLoads} times, not {loads}");
        }

        // The flow report reads its window alone (HA-2785): the rows IssueEvents and Comments return
        // are the same at both sizes, and the sessions are counted by the database, so no more than
        // the two groups come back from WorkLogEntries.
        {
            var (before, after) = (small["GET /api/reports/flow"], large["GET /api/reports/flow"]);

            foreach (var table in new[] { "IssueEvents", "Comments" })
            {
                var (rowsBefore, rowsAfter) = (ReturnedBy(before, table), ReturnedBy(after, table));
                if (rowsBefore != rowsAfter || rowsBefore == 0)
                    failures.Add($"GET /api/reports/flow returned {rowsBefore} {table} rows at the small size and {rowsAfter} at the large - " +
                        "the same number is the point, and none would mean the read was not measured");
            }

            foreach (var (size, m) in new[] { ("small", before), ("large", after) })
            {
                var sessions = ReturnedBy(m, "WorkLogEntries");
                if (sessions is 0 or > 2)
                    failures.Add($"GET /api/reports/flow returned {sessions} WorkLogEntries rows at the {size} size - the sessions are counted by the database, two groups at most");
            }
        }

        if (failures.Count > 0)
            Assert.Fail(
                string.Join(Environment.NewLine, failures) + Environment.NewLine + Environment.NewLine
                + Table(small, large) + Environment.NewLine + Statements(large));
    }

    private sealed record Measurement(Row Row, IReadOnlyList<ReadCounter.Statement> Statements);

    /// <summary>The rows the statements that begin on this table returned.</summary>
    private static long ReturnedBy(Measurement m, string table) =>
        m.Statements.Where(s => s.Table == table).Sum(s => s.Rows);

    /// <summary>What a seed holds: its window and its history before the window.</summary>
    private sealed record Seeded(
        long WindowMoves, long OldMoves, long WindowSessions, long OldSessions, long OldComments,
        long WindowLevels, long OldLevels, long WindowRecords, long OldRecords,
        long WindowRunnerStates, long OldRunnerStates);

    private static readonly Dictionary<string, Seeded> sizes = [];

    private static async Task<Seeded> CountSeedAsync(string connectionString)
    {
        var windowStart = Now.AddDays(-ReadCostSeed.WindowDays);
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);
        var moves = db.IssueEvents.Where(e => e.Kind == EfHatchIssueEvent.StatusChanged);
        var levels = db.IssueEvents.Where(e => e.Kind == EfHatchIssueEvent.PreApprovalChanged);
        var records = db.IssueEvents.Where(e => e.Kind == EfHatchIssueEvent.PullRequestChanged);
        return new Seeded(
            await moves.CountAsync(e => e.At >= windowStart), await moves.CountAsync(e => e.At < windowStart),
            await db.WorkLog.CountAsync(w => w.StartedAt >= windowStart), await db.WorkLog.CountAsync(w => w.StartedAt < windowStart),
            await db.Comments.CountAsync(c => c.Kind == EfHatchComment.Note && c.CreatedAt < windowStart),
            await levels.CountAsync(e => e.At >= windowStart), await levels.CountAsync(e => e.At < windowStart),
            await records.CountAsync(e => e.At >= windowStart), await records.CountAsync(e => e.At < windowStart),
            await db.RunnerStates.CountAsync(r => r.At >= windowStart), await db.RunnerStates.CountAsync(r => r.At < windowStart));
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
            if (!Grew(s, l))
                same.AppendLine($"        [\"{read.Name}\"] = ({Environment.NewLine}            {Literal(s)},{Environment.NewLine}            {Literal(l)}),");
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

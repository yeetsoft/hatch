using System.Data.Common;
using System.Text.Json;
using Hatch.Api.Modules.Hatch;
using Hatch.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The reads of the event trail and the work log that a scan makes, each shown
/// to be answered from an index (HA-2801). A table of a few thousand rows is
/// read end to end by the planner whatever indexes it has, so the plan is the
/// proof: the statement EF really sent is run under <c>EXPLAIN</c> on a
/// session that has sequential scans turned off, and the plan must name the
/// index. It points at <c>HATCH_TEST_DATABASE_URL</c>, which is emptied by each
/// test, so it must never name a database anybody uses.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class EventAndWorkLogIndexPlanTests
{
    private const string Runner = "mr-blue";
    private static readonly DateTimeOffset Now = new(2026, 1, 31, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The command a read sent: its text and its parameters, copied so they can be run again.</summary>
    private sealed record Sent(string Sql, List<NpgsqlParameter> Parameters);

    private sealed class Capture : DbCommandInterceptor
    {
        public List<Sent> Commands { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Note(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            Note(command);
            return new(result);
        }

        private void Note(DbCommand command) =>
            Commands.Add(new Sent(
                command.CommandText,
                command.Parameters.Cast<NpgsqlParameter>().Select(p => p.Clone()).ToList()));
    }

    private sealed record Seeded(string ConnectionString, EfHatchStatus Implementation, long AmbitionId);

    /// <summary>
    /// A board with thousands of rows that match none of the reads and a few
    /// that match each, then <c>VACUUM (ANALYZE)</c>, so the planner has the
    /// statistics it would have on a real one.
    /// </summary>
    private static async Task<Seeded> SeedAsync()
    {
        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var project = new EfHatchProject { Key = "PLN", Name = "Plan", CreatedAt = Now.AddDays(-120) };
        var doing = new EfHatchStatus { Name = "doing", SortOrder = 30, IsWip = true, IsImplementation = true };
        db.AddRange(project, doing);
        await db.SaveChangesAsync();

        var issues = Enumerable.Range(1, 40).Select(n => new EfHatchIssue
        {
            ProjectId = project.Id, Number = n, Type = n <= 5 ? "ambition" : "task", Title = $"issue {n}", StatusId = doing.Id,
            Rank = 1024L * n, CreatedBy = "bo", CreatedAt = Now.AddDays(-110), UpdatedAt = Now,
        }).ToList();
        db.Issues.AddRange(issues);
        await db.SaveChangesAsync();

        // One event in five is a release, and one release in twenty is this
        // runner's drop; the rest are other runners' releases or other kinds.
        for (var n = 0; n < 6000; n++)
        {
            var kind = (n % 5) switch
            {
                0 => EfHatchIssueEvent.ClaimReleased,
                1 => EfHatchIssueEvent.StatusChanged,
                2 => EfHatchIssueEvent.ClaimTaken,
                _ => EfHatchIssueEvent.Commented,
            };
            var mine = n % 100 == 0;
            var payload = kind == EfHatchIssueEvent.ClaimReleased
                ? JsonSerializer.Serialize(new { outcome = mine ? ClaimOutcomes.Dropped : ClaimOutcomes.Worked, runner = mine ? Runner : $"runner-{n % 7}" })
                : kind == EfHatchIssueEvent.StatusChanged ? JsonSerializer.Serialize(new { to = "doing", toId = doing.Id }) : null;

            db.IssueEvents.Add(new EfHatchIssueEvent
            {
                IssueId = issues[n % issues.Count].Id, Actor = "bo", Kind = kind, Payload = payload, At = Now.AddMinutes(-n),
            });
        }

        // Most sessions are tasks'; a handful are an ambition's worked passes.
        for (var n = 0; n < 3000; n++)
        {
            var ambition = n % 600 == 0;
            db.WorkLog.Add(new EfHatchWorkLogEntry
            {
                IssueId = issues[ambition ? n % 5 : 5 + n % 35].Id, SessionId = $"s{n}",
                StartedAt = Now.AddMinutes(-n * 3), EndedAt = Now.AddMinutes(-n * 3 + 2), DurationMs = 120_000,
                Title = $"session {n}", Turns = 3, InputTokens = 10, OutputTokens = 2, CreatedAt = Now.AddMinutes(-n * 3 + 2),
                IssueType = ambition ? "ambition" : "task", Outcome = ClaimOutcomes.Worked,
                PlaybookFromStatusId = doing.Id, PlaybookToStatusId = doing.Id,
            });
        }

        await db.SaveChangesAsync();

        await db.Database.OpenConnectionAsync();
        await using var vacuum = db.Database.GetDbConnection().CreateCommand();
        vacuum.CommandText = "VACUUM (ANALYZE)";
        await vacuum.ExecuteNonQueryAsync();

        return new Seeded(connectionString, doing, issues[0].Id);
    }

    /// <summary>Runs <paramref name="read"/> once and returns the first command it sent that matches <paramref name="pick"/>.</summary>
    private static async Task<Sent> CaptureAsync(string connectionString, Func<HatchContext, Task> read, Func<string, bool> pick)
    {
        var capture = new Capture();
        await using var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).AddInterceptors(capture).Options);
        await read(db);
        return capture.Commands.First(c => pick(c.Sql));
    }

    /// <summary>The plan Postgres picks for the statement, with sequential scans off for the session, as one string.</summary>
    private static async Task<string> ExplainAsync(string connectionString, Sent command)
    {
        await using var connection = new NpgsqlConnection(connectionString + ";Options=-c enable_seqscan=off");
        await connection.OpenAsync();
        await using var explain = new NpgsqlCommand("EXPLAIN (FORMAT JSON) " + command.Sql, connection);
        // Cloned, so each keeps the Postgres type EF gave it (jsonb, not text).
        foreach (var parameter in command.Parameters)
            explain.Parameters.Add(parameter.Clone());
        return (await explain.ExecuteScalarAsync())!.ToString()!;
    }

    [SkippableFact]
    public async Task LetGoBy_FindsItsReleasesInTheNewIndex_NotByReadingTheTable()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - the plan comes from a real Postgres.");
        var seeded = await SeedAsync();

        var found = await CaptureAsync(
            seeded.ConnectionString, db => LetGo.ByAsync(db, Runner, default),
            sql => sql.Contains("\"Kind\"") && sql.Contains("@>"));

        Assert.Contains("IX_IssueEvents_Payload", await ExplainAsync(seeded.ConnectionString, found));
    }

    [SkippableFact]
    public async Task LetGoBy_StillNamesOnlyTheRunnersOwnDrops()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - the plan comes from a real Postgres.");
        var seeded = await SeedAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(seeded.ConnectionString).Options);

        var by = await LetGo.ByAsync(db, Runner, default);

        Assert.All(by.Values, v => Assert.Equal(Runner, v.Runner));
    }

    [SkippableFact]
    public async Task SettledPasses_AreReadFromTheNewIndex_NotByReadingTheTable()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - the plan comes from a real Postgres.");
        var seeded = await SeedAsync();

        var found = await CaptureAsync(
            seeded.ConnectionString,
            db => FamilyGate.SettledPassesAsync(db, [seeded.Implementation], new Dictionary<long, List<long>>(), default),
            sql => sql.Contains("WorkLogEntries"));

        Assert.Contains("IX_WorkLogEntries_SettledPasses", await ExplainAsync(seeded.ConnectionString, found));
    }

    [SkippableFact]
    public async Task SendBackStreak_AsksForStatusChangesOnly_AndUsesAnIndexOfTheTrail()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - the plan comes from a real Postgres.");
        var seeded = await SeedAsync();

        var many = await CaptureAsync(
            seeded.ConnectionString,
            async db => await SendBackStreak.CountsAsync(db, [seeded.AmbitionId, seeded.AmbitionId + 1], "doing", default),
            sql => sql.Contains("IssueEvents"));
        Assert.Contains("\"Kind\" = 'status_changed'", many.Sql);
        Assert.Contains("IX_IssueEvents_", await ExplainAsync(seeded.ConnectionString, many));

        var single = await CaptureAsync(
            seeded.ConnectionString,
            async db => await SendBackStreak.ForIssueAsync(db, seeded.AmbitionId, "doing", default),
            sql => sql.Contains("IssueEvents"));
        Assert.Contains("\"Kind\" = 'status_changed'", single.Sql);
        Assert.Contains("IX_IssueEvents_", await ExplainAsync(seeded.ConnectionString, single));
    }
}

using System.Data.Common;
using System.Text.RegularExpressions;
using Hatch.Api.Modules.Hatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What one call cost the database: the statements it sent, the rows the
/// database handed back to it, and the rows Postgres read to answer, table by
/// table. Counts, never times - the same on every run.
///
/// <para>Two pieces make it. <see cref="ReadCounter"/> is an Entity Framework
/// command interceptor and sees what the application sees. The third number is
/// Postgres's own, taken from <c>pg_stat_user_tables</c> before and after, and
/// is the only one that notices a scan whose rows never reach the application
/// because a <c>WHERE</c> threw them away.</para>
///
/// <para>No production code changes for any of it. A test measures a call by
/// giving the context <see cref="Options"/>, then <see cref="MeasureAsync"/>.</para>
/// </summary>
internal sealed class ReadCounter : DbCommandInterceptor
{
    /// <summary>
    /// The top-level <c>FROM</c> of a statement that reads <c>Issues</c> as its
    /// source. EF writes a nested select's <c>FROM</c> indented, so anchoring to
    /// the start of a line leaves out an <c>EXISTS</c> over <c>Issues</c>
    /// inside a statement that reads something else.
    /// </summary>
    private static readonly Regex IssuesSource = new(
        $@"^FROM ""?{HatchContext.Schema}""?\.""?Issues""? AS", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex FirstTable = new(
        @"FROM ""?[^"".\s]+""?\.""?(?<table>[^""\s]+?)""? AS", RegexOptions.Compiled);

    private readonly List<Statement> statements = [];
    private bool counting = true;

    /// <summary>One command: its text, how many rows it returned, and the longest array-valued parameter it sent.</summary>
    public sealed class Statement(string sql)
    {
        public string Sql { get; } = sql;
        public long Rows { get; set; }

        /// <summary>
        /// The length of the longest collection-valued parameter this statement
        /// sent, or zero when none of its parameters are a collection - how a
        /// test tells an id list sent as a filter from one sent as a scope.
        /// </summary>
        public int MaxArrayParameter { get; set; }

        /// <summary>Whether <c>Issues</c> is the table this statement reads from.</summary>
        public bool LoadsIssues => IssuesSource.IsMatch(Sql);

        /// <summary>The first table the statement reads, or <c>-</c> when it names none.</summary>
        public string Table => FirstTable.Match(Sql) is { Success: true } m ? m.Groups["table"].Value : "-";
    }

    /// <summary>What a call cost.</summary>
    public sealed record Cost(
        int Statements, int IssueLoads, long Rows, long RowsOutsideIssueLoads, IReadOnlyDictionary<string, long> RowsRead)
    {
        /// <summary>The rows Postgres read, as one string a literal can hold: <c>Table=n;Table=n</c>, by name, empty tables left out.</summary>
        public string Read => string.Join(';', RowsRead.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => $"{r.Key}={r.Value}"));

        
    }

    /// <summary>A measured call: its cost, and the statements behind it.</summary>
    public sealed record Measured(Cost Cost, IReadOnlyList<Statement> Statements);

    public IReadOnlyList<Statement> Statements
    {
        get { lock (statements) return [.. statements]; }
    }

    /// <summary>
    /// The options for a context whose every command this counter sees, and
    /// <paramref name="more"/> beside it: a test that counts through the
    /// application's own interceptor as well passes it here.
    /// </summary>
    public DbContextOptions<HatchContext> Options(string connectionString, params IInterceptor[] more) =>
        new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).AddInterceptors([this, .. more]).Options;

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

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Note(command);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken ct = default)
    {
        Note(command);
        return new(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Note(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        Note(command);
        return new(result);
    }

    /// <summary>The reader is done: the rows it returned are the calls to <see cref="DataReaderClosingEventData.ReadCount"/> that found one.</summary>
    public override InterceptionResult DataReaderClosing(
        DbCommand command, DataReaderClosingEventData eventData, InterceptionResult result)
    {
        Closed(command, eventData);
        return result;
    }

    public override ValueTask<InterceptionResult> DataReaderClosingAsync(
        DbCommand command, DataReaderClosingEventData eventData, InterceptionResult result)
    {
        Closed(command, eventData);
        return new(result);
    }

    private void Closed(DbCommand command, DataReaderClosingEventData eventData)
    {
        if (Owner(command) is not { } seen) return;

        // ReadCount is the calls to Read, and a reader read to the end ends on
        // the call that returned false. A reader left on a row was closed
        // early, and every call it made returned true.
        var readToTheEnd = eventData.ReadCount > 0 && eventData.DataReader is NpgsqlDataReader { IsOnRow: false };
        seen.Rows = eventData.ReadCount - (readToTheEnd ? 1 : 0);
    }

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<DbCommand, Statement> owners = new();

    private Statement? Owner(DbCommand command) => owners.TryGetValue(command, out var s) ? s : null;

    private void Note(DbCommand command)
    {
        if (!counting) return;

        var statement = new Statement(command.CommandText) { MaxArrayParameter = MaxArrayParameterOf(command) };
        owners.AddOrUpdate(command, statement);
        lock (statements) statements.Add(statement);
    }

    /// <summary>The longest collection-valued parameter a command carries, or zero when it carries none.</summary>
    private static int MaxArrayParameterOf(DbCommand command)
    {
        var max = 0;

        foreach (DbParameter parameter in command.Parameters)
            if (parameter.Value is System.Collections.ICollection collection)
                max = Math.Max(max, collection.Count);

        return max;
    }

    /// <summary>Forgets what was counted, and starts counting.</summary>
    public void Reset()
    {
        lock (statements) statements.Clear();
        counting = true;
    }

    /// <summary>Stops counting, so what is sent afterwards (the stats flush) is not part of the call.</summary>
    public void Stop() => counting = false;

    // ---- Rows Postgres read ----

    /// <summary>
    /// Runs <paramref name="call"/> on a fresh context whose every command rides
    /// one connection, and says what it cost.
    ///
    /// <para>Postgres keeps its table counters in the backend that did the work
    /// and publishes them when the transaction ends, at most once a second. So
    /// the call runs on a single pinned connection, which is told to publish
    /// (<c>pg_stat_force_next_flush</c>) once the call is over, and the counters
    /// are read from a second connection. No sleeps. The flush is sent as a raw
    /// command, which the interceptor never sees.</para>
    /// </summary>
    public static async Task<Measured> MeasureAsync(string connectionString, Func<HatchContext, Task> call)
    {
        var counter = new ReadCounter();
        await using var db = new HatchContext(counter.Options(connectionString));
        await db.Database.OpenConnectionAsync();
        var pinned = db.Database.GetDbConnection();

        await FlushAsync(pinned);
        var before = await ReadStatsAsync(connectionString);

        counter.Reset();
        await call(db);
        counter.Stop();

        await FlushAsync(pinned);
        var after = await ReadStatsAsync(connectionString);

        var statements = counter.Statements;
        var read = after
            .Select(a => (Table: a.Key, Rows: a.Value - before.GetValueOrDefault(a.Key)))
            .Where(r => r.Rows != 0)
            .ToDictionary(r => r.Table, r => r.Rows);

        var issues = statements.Where(s => s.LoadsIssues).ToList();
        return new Measured(
            new Cost(
                statements.Count, issues.Count, statements.Sum(s => s.Rows),
                statements.Where(s => !s.LoadsIssues).Sum(s => s.Rows), read),
            statements);
    }

    /// <summary>Makes the connection publish its table counters now: one statement to set the flag, one to end a transaction under it.</summary>
    internal static async Task FlushAsync(DbConnection connection)
    {
        foreach (var sql in new[] { "SELECT pg_stat_force_next_flush()", "SELECT 1" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteScalarAsync();
        }
    }

    /// <summary>
    /// Rows read per table so far: sequential rows plus index-fetched rows,
    /// the two ways Postgres reads a row for a query. Read on a connection of
    /// its own, so the read does not disturb the one being measured.
    /// </summary>
    internal static async Task<Dictionary<string, long>> ReadStatsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT relname, COALESCE(seq_tup_read, 0) + COALESCE(idx_tup_fetch, 0) FROM pg_stat_user_tables";

        var rows = new Dictionary<string, long>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows[reader.GetString(0)] = reader.GetInt64(1);
        return rows;
    }
}

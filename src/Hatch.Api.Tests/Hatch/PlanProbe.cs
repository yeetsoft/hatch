using System.Data.Common;
using System.Text.Json;
using Hatch.Api.Modules.Hatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What the index-plan tests share (HA-2801, HA-2782): run a read, keep the
/// statements EF really sent, and ask Postgres for the plan of one of them on a
/// session that has sequential scans turned off. A table of a few thousand rows
/// is read end to end by the planner whatever indexes it has, so the plan only
/// proves the index is usable if the scan is not allowed.
/// </summary>
internal static class PlanProbe
{
    /// <summary>The command a read sent: its text and its parameters, copied so they can be run again.</summary>
    internal sealed record Sent(string Sql, List<NpgsqlParameter> Parameters);

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

    /// <summary>Runs <paramref name="read"/> once and returns every command it sent.</summary>
    internal static async Task<List<Sent>> CaptureAllAsync(string connectionString, Func<HatchContext, Task> read)
    {
        var capture = new Capture();
        await using var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).AddInterceptors(capture).Options);
        await read(db);
        return capture.Commands;
    }

    /// <summary>Runs <paramref name="read"/> once and returns the first command it sent that matches <paramref name="pick"/>.</summary>
    internal static async Task<Sent> CaptureAsync(string connectionString, Func<HatchContext, Task> read, Func<string, bool> pick) =>
        (await CaptureAllAsync(connectionString, read)).First(c => pick(c.Sql));

    /// <summary>The plan Postgres picks for the statement, with sequential scans off for the session, as one string.</summary>
    internal static async Task<string> ExplainAsync(string connectionString, Sent command)
    {
        await using var connection = new NpgsqlConnection(connectionString + ";Options=-c enable_seqscan=off");
        await connection.OpenAsync();
        await using var explain = new NpgsqlCommand("EXPLAIN (FORMAT JSON) " + command.Sql, connection);
        // Cloned, so each keeps the Postgres type EF gave it (jsonb, not text).
        foreach (var parameter in command.Parameters)
            explain.Parameters.Add(parameter.Clone());
        return (await explain.ExecuteScalarAsync())!.ToString()!;
    }

    /// <summary>The relations a plan reads end to end: every <c>Seq Scan</c> node, by table name.</summary>
    internal static List<string> SeqScans(string plan)
    {
        var found = new List<string>();
        Walk(JsonDocument.Parse(plan).RootElement);
        return found;

        void Walk(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Object)
            {
                if (node.TryGetProperty("Node Type", out var type) && type.GetString() == "Seq Scan"
                    && node.TryGetProperty("Relation Name", out var relation))
                    found.Add(relation.GetString()!);
                foreach (var property in node.EnumerateObject()) Walk(property.Value);
            }
            else if (node.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in node.EnumerateArray()) Walk(item);
            }
        }
    }

    /// <summary>The plan's node types, in the order they appear, for asserting a plan has no <c>Sort</c>.</summary>
    internal static List<string> NodeTypes(string plan)
    {
        var found = new List<string>();
        Walk(JsonDocument.Parse(plan).RootElement);
        return found;

        void Walk(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Object)
            {
                if (node.TryGetProperty("Node Type", out var type)) found.Add(type.GetString()!);
                foreach (var property in node.EnumerateObject()) Walk(property.Value);
            }
            else if (node.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in node.EnumerateArray()) Walk(item);
            }
        }
    }
}

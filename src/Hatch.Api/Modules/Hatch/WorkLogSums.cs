using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The work log, summed by the database rather than walked session by session
/// in C# - one row per issue per time bucket, which is what
/// <see cref="ConsumptionReport.FoldSums"/> credits up the hierarchy. Internal
/// so HA-2787's own read of <see cref="WorkLogRollup"/> can call it too, with
/// <c>groupByIssue: false</c> for a read that wants one row per bucket and
/// never per issue.
/// </summary>
internal static class WorkLogSums
{
    /// <summary>
    /// One issue's (or, ungrouped, the whole filter's) totals inside one bucket.
    /// A class, not a struct: EF's <c>SqlQuery&lt;T&gt;</c> maps a non-scalar
    /// result to an ad-hoc entity type, which refuses a value type outright.
    /// </summary>
    public sealed record SessionSum(
        long IssueId,
        int Bucket,
        int Sessions,
        int Errors,
        long InputTokens,
        long OutputTokens,
        long CacheCreationTokens,
        long CacheReadTokens,
        decimal CostUsd,
        int Unpriced,
        long DurationMs,
        DateTimeOffset First,
        DateTimeOffset Last);

    /// <summary>
    /// Every session with <c>EndedAt</c> in <c>[from, to)</c>, summed per issue
    /// (or, with <paramref name="groupByIssue"/> false, per bucket alone) and
    /// per bucket of <paramref name="length"/> starting at <paramref name="origin"/>.
    /// </summary>
    /// <remarks>
    /// <paramref name="origin"/> and <paramref name="length"/> are a window's own
    /// <c>From</c> and <c>Length</c> - the bucket a session falls in is computed
    /// the same way <see cref="ConsumptionReport.Fold"/> computes it, so a row
    /// read here and a row folded in memory never disagree about which bucket a
    /// session belongs to.
    /// </remarks>
    internal static async Task<List<SessionSum>> ReadAsync(
        HatchContext db, DateTimeOffset from, DateTimeOffset to, DateTimeOffset origin, TimeSpan length,
        IReadOnlyList<int>? projectIds, bool groupByIssue, CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
            return await ReadNpgsqlAsync(db, from, to, origin, length, projectIds, groupByIssue, ct);

        var scoped = db.WorkLog.AsNoTracking().Where(w => w.EndedAt >= from && w.EndedAt < to);
        if (projectIds is { Count: > 0 }) scoped = scoped.Where(w => projectIds.Contains(w.Issue!.ProjectId));

        var rows = await scoped
            .Select(w => new ConsumptionReport.Session(
                w.IssueId, w.EndedAt, w.IsError, w.InputTokens, w.OutputTokens,
                w.CacheCreationTokens, w.CacheReadTokens, w.CostUsd, w.DurationMs))
            .ToListAsync(ct);

        return Group(rows, origin, length, groupByIssue);
    }

    /// <summary>
    /// The same sum, taken in memory rather than by the database - what every
    /// provider but Postgres reads with, and what the Postgres test compares
    /// its own rows against. EF's in-memory provider does not sum a decimal
    /// projection the way Npgsql does (<see cref="WorkLogRollup"/>'s own
    /// <c>Fold</c> says so); summing here in a plain LINQ <c>GroupBy</c> over
    /// rows already pulled into memory sidesteps that rather than relying on it.
    /// </summary>
    internal static List<SessionSum> Group(
        IReadOnlyList<ConsumptionReport.Session> rows, DateTimeOffset origin, TimeSpan length, bool groupByIssue) =>
        rows
            .GroupBy(r => (
                IssueId: groupByIssue ? r.IssueId : 0L,
                Bucket: (int)((r.EndedAt - origin).Ticks / length.Ticks)))
            .Select(g => new SessionSum(
                g.Key.IssueId,
                g.Key.Bucket,
                g.Count(),
                g.Count(r => r.IsError),
                g.Sum(r => r.InputTokens),
                g.Sum(r => r.OutputTokens),
                g.Sum(r => r.CacheCreationTokens),
                g.Sum(r => r.CacheReadTokens),
                g.Sum(r => r.CostUsd ?? 0m),
                g.Count(r => r.CostUsd is null),
                g.Sum(r => r.DurationMs),
                g.Min(r => r.EndedAt),
                g.Max(r => r.EndedAt)))
            .ToList();

    /// <summary>
    /// The bucket index and the nine aggregates, shared by both branches below -
    /// only the <c>SELECT</c>'s issue column and the <c>GROUP BY</c> differ
    /// between a per-issue and a bucket-only read. <c>{0}</c> is the window's
    /// origin, <c>{1}</c> its bucket length in seconds; <c>{2}</c> and <c>{3}</c>
    /// are the range.
    /// </summary>
    private const string BucketAndTotalsSql =
        "floor(extract(epoch from (w.\"EndedAt\" - {0})) / {1})::int AS \"Bucket\", " +
        "count(*)::int AS \"Sessions\", " +
        "count(*) FILTER (WHERE w.\"IsError\")::int AS \"Errors\", " +
        "coalesce(sum(w.\"InputTokens\"), 0)::bigint AS \"InputTokens\", " +
        "coalesce(sum(w.\"OutputTokens\"), 0)::bigint AS \"OutputTokens\", " +
        "coalesce(sum(w.\"CacheCreationTokens\"), 0)::bigint AS \"CacheCreationTokens\", " +
        "coalesce(sum(w.\"CacheReadTokens\"), 0)::bigint AS \"CacheReadTokens\", " +
        "coalesce(sum(w.\"CostUsd\"), 0)::numeric(18,8) AS \"CostUsd\", " +
        "count(*) FILTER (WHERE w.\"CostUsd\" IS NULL)::int AS \"Unpriced\", " +
        "coalesce(sum(w.\"DurationMs\"), 0)::bigint AS \"DurationMs\", " +
        "min(w.\"EndedAt\") AS \"First\", " +
        "max(w.\"EndedAt\") AS \"Last\" ";

    /// <summary>
    /// The Postgres branch: one <c>GROUP BY</c> over <c>WorkLogEntries</c>,
    /// raw because EF's translation of the interval arithmetic in
    /// <see cref="BucketAndTotalsSql"/> cannot be checked without running it.
    /// Joins <c>Issues</c> only when a project filter is given, so an unfiltered
    /// read - the ordinary case - never pays for it, and a null array parameter's
    /// type is never left for Npgsql to guess.
    /// </summary>
    private static async Task<List<SessionSum>> ReadNpgsqlAsync(
        HatchContext db, DateTimeOffset from, DateTimeOffset to, DateTimeOffset origin, TimeSpan length,
        IReadOnlyList<int>? projectIds, bool groupByIssue, CancellationToken ct)
    {
        var issueSelect = groupByIssue ? "w.\"IssueId\"" : "0::bigint";
        var groupBy = groupByIssue ? "w.\"IssueId\", \"Bucket\"" : "\"Bucket\"";

        var sql = projectIds is { Count: > 0 } array
            ? FormattableStringFactory.Create(
                "SELECT " + issueSelect + " AS \"IssueId\", " + BucketAndTotalsSql +
                "FROM hatch.\"WorkLogEntries\" AS w " +
                "JOIN hatch.\"Issues\" AS i ON i.\"Id\" = w.\"IssueId\" " +
                "WHERE w.\"EndedAt\" >= {2} AND w.\"EndedAt\" < {3} AND i.\"ProjectId\" = ANY({4}) " +
                "GROUP BY " + groupBy,
                origin, length.TotalSeconds, from, to, array.ToArray())
            : FormattableStringFactory.Create(
                "SELECT " + issueSelect + " AS \"IssueId\", " + BucketAndTotalsSql +
                "FROM hatch.\"WorkLogEntries\" AS w " +
                "WHERE w.\"EndedAt\" >= {2} AND w.\"EndedAt\" < {3} " +
                "GROUP BY " + groupBy,
                origin, length.TotalSeconds, from, to);

        return await db.Database.SqlQuery<SessionSum>(sql).ToListAsync(ct);
    }
}

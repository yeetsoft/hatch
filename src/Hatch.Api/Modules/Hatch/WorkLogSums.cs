using Hatch.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The work log's sums, worked out by the database: totals, buckets along a
/// time axis, and the paths read most.
/// </summary>
/// <remarks>
/// Every method takes a query that is <em>already</em> filtered - by scope,
/// project, outcome and range, as the caller sees fit - so none of those rules
/// is repeated or reachable from here. What a caller gets back is a few rows
/// however many sessions the filter holds: one for the totals, one per bucket
/// that holds a session, at most <see cref="WorkLogRollup.MaxTopReads"/> for the
/// reads. That is the whole point of the file.
///
/// Internal and static, and not private to the leaderboard, because the
/// consumption report and any later report that adds up the log asks the same
/// questions of it.
///
/// The arithmetic is the one <c>WorkLogRollup.Fold</c> states, kept exact:
/// cost is summed at the column's eight places, an unpriced session adds
/// nothing and is counted beside the sum, and an errored session is counted and
/// summed like any other.
/// </remarks>
internal static class WorkLogSums
{
    /// <summary>One group's sums, as the database answers them.</summary>
    private sealed record SumRow(
        int Sessions,
        int Errors,
        long InputTokens,
        long OutputTokens,
        long CacheCreationTokens,
        long CacheReadTokens,
        decimal CostUsd,
        int UnpricedSessions,
        long DurationMs);

    /// <summary>What a range with no session in it totals.</summary>
    public static readonly WorkLogTotalsDto None = new(0, 0, 0, 0, 0, 0, 0, 0m, 0, 0);

    private static WorkLogTotalsDto Dto(SumRow r) => new(
        r.Sessions,
        r.Errors,
        r.InputTokens,
        r.OutputTokens,
        r.CacheCreationTokens,
        r.CacheReadTokens,
        // The headline, added here from the four sums so it has one
        // definition - the same one WorkLogRollup.Fold gives it.
        r.InputTokens + r.OutputTokens + r.CacheCreationTokens + r.CacheReadTokens,
        r.CostUsd,
        r.UnpricedSessions,
        r.DurationMs);

    /// <summary>
    /// The totals of every session in the query, in one read that answers one
    /// row. A query that holds nothing totals <see cref="None"/>.
    /// </summary>
    public static async Task<WorkLogTotalsDto> TotalsAsync(IQueryable<EfHatchWorkLogEntry> entries, CancellationToken ct)
    {
        var row = await entries
            .GroupBy(_ => 1)
            .Select(g => new SumRow(
                g.Count(),
                g.Count(w => w.IsError),
                g.Sum(w => w.InputTokens),
                g.Sum(w => w.OutputTokens),
                g.Sum(w => w.CacheCreationTokens),
                g.Sum(w => w.CacheReadTokens),
                g.Sum(w => w.CostUsd ?? 0m),
                g.Count(w => w.CostUsd == null),
                g.Sum(w => w.DurationMs)))
            .FirstOrDefaultAsync(ct);

        return row is null ? None : Dto(row);
    }

    /// <summary>
    /// The totals of the sessions in each bucket that holds one, keyed by the
    /// bucket's index from <paramref name="from"/>. A bucket with nothing in it
    /// has no entry, and the caller says what an empty one is.
    /// </summary>
    /// <remarks>
    /// The caller has already narrowed the query to <c>from &lt;= EndedAt &lt;
    /// from + length * buckets</c>, which is what keeps every index inside the
    /// grid. The index is floored explicitly: Postgres rounds a double cast to
    /// an integer where C# truncates, and a session just under a bucket's far
    /// edge would otherwise land in the next one.
    /// </remarks>
    public static async Task<Dictionary<int, WorkLogTotalsDto>> ByBucketAsync(
        IQueryable<EfHatchWorkLogEntry> entries, DateTimeOffset from, TimeSpan length, CancellationToken ct)
    {
        var seconds = length.TotalSeconds;

        var rows = await entries
            .GroupBy(w => (int)Math.Floor((w.EndedAt - from).TotalSeconds / seconds))
            .Select(g => new
            {
                Bucket = g.Key,
                Sums = new SumRow(
                    g.Count(),
                    g.Count(w => w.IsError),
                    g.Sum(w => w.InputTokens),
                    g.Sum(w => w.OutputTokens),
                    g.Sum(w => w.CacheCreationTokens),
                    g.Sum(w => w.CacheReadTokens),
                    g.Sum(w => w.CostUsd ?? 0m),
                    g.Count(w => w.CostUsd == null),
                    g.Sum(w => w.DurationMs)),
            })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.Bucket, r => Dto(r.Sums));
    }

    /// <summary>
    /// Several totals added into one. Exact, since every field is a long, an
    /// int or a decimal - so buckets add to the range's totals by construction.
    /// </summary>
    public static WorkLogTotalsDto Add(IEnumerable<WorkLogTotalsDto> parts) =>
        parts.Aggregate(None, (a, b) => new WorkLogTotalsDto(
            a.Sessions + b.Sessions,
            a.Errors + b.Errors,
            a.InputTokens + b.InputTokens,
            a.OutputTokens + b.OutputTokens,
            a.CacheCreationTokens + b.CacheCreationTokens,
            a.CacheReadTokens + b.CacheReadTokens,
            a.TotalTokens + b.TotalTokens,
            a.CostUsd + b.CostUsd,
            a.UnpricedSessions + b.UnpricedSessions,
            a.DurationMs + b.DurationMs));

    /// <summary>One element of one session's <c>Reads</c> list, unnested by the database.</summary>
    internal sealed class ReadElement
    {
        public long EntryId { get; set; }
        public string Path { get; set; } = "";
        public long Count { get; set; }
    }

    /// <summary>
    /// The paths read most across the sessions in the query, summed and ranked
    /// by Postgres: the same answer as <see cref="WorkLogRollup.FoldReads"/>
    /// over their <c>Reads</c> columns, without reading the columns.
    /// </summary>
    /// <remarks>
    /// Postgres only: it unnests the jsonb list with <c>jsonb_array_elements</c>,
    /// which EF has no translation for, so the statement is written out and
    /// <em>joined to</em> the caller's query on the entry id. The join is what
    /// carries every filter, including the context's project scope, without a
    /// copy of any of them in SQL. The in-memory provider has no jsonb, so the
    /// caller falls back to the fold over the column.
    ///
    /// Each element is guarded by its jsonb type - a list, holding objects, with
    /// a string path and a whole-number count - so a row that is malformed adds
    /// nothing, as <see cref="WorkLogRollup.ReadReads"/> does for a column that
    /// will not parse. The one difference: a list with one bad element among
    /// good ones loses that element here and the whole row there. Nothing writes
    /// such a list - <see cref="WorkLogRollup.WriteReads"/> only ever stores
    /// well-formed ones.
    ///
    /// Ranked count descending and then path ascending <em>ordinally</em>:
    /// <c>COLLATE "C"</c> is what makes the database order <c>B</c> before
    /// <c>a</c> as the fold does, where its default collation would not.
    /// </remarks>
    public static async Task<IReadOnlyList<WorkLogReadDto>> TopReadsAsync(
        HatchContext db, IQueryable<EfHatchWorkLogEntry> entries, CancellationToken ct)
    {
        var entity = db.Model.FindEntityType(typeof(EfHatchWorkLogEntry))!;
        var table = $"\"{entity.GetSchema() ?? HatchContext.Schema}\".\"{entity.GetTableName()}\"";
        var id = entity.FindProperty(nameof(EfHatchWorkLogEntry.Id))!.GetColumnName();
        var reads = entity.FindProperty(nameof(EfHatchWorkLogEntry.Reads))!.GetColumnName();

        // Only rows holding a list are unnested, and only elements that are
        // what WriteReads writes are kept. The count is cast inside a CASE so
        // the cast cannot run on an element the filter would have dropped.
        // No braces anywhere in the text: the raw call formats it.
        var elements = db.Database.SqlQueryRaw<ReadElement>(
            $$"""
             SELECT w."{{id}}" AS "EntryId",
                    e.value ->> 'path' AS "Path",
                    CASE WHEN e.value ->> 'count' ~ '^-?[0-9]+$' AND length(e.value ->> 'count') <= 10
                         THEN (e.value ->> 'count')::bigint END AS "Count"
             FROM {{table}} AS w
             CROSS JOIN LATERAL jsonb_array_elements(
                 CASE WHEN jsonb_typeof(w."{{reads}}") = 'array' THEN w."{{reads}}" ELSE '[]'::jsonb END) AS e
             WHERE jsonb_typeof(e.value) = 'object'
               AND jsonb_typeof(e.value -> 'path') = 'string'
               AND jsonb_typeof(e.value -> 'count') = 'number'
               AND e.value ->> 'count' ~ '^-?[0-9]+$'
               AND length(e.value ->> 'count') <= 10
             """);

        var ranked = await elements
            .Join(entries, e => e.EntryId, w => w.Id, (e, _) => e)
            .GroupBy(e => e.Path)
            .Select(g => new { Path = g.Key, Count = g.Sum(e => e.Count) })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => EF.Functions.Collate(x.Path, "C"))
            .Take(WorkLogRollup.MaxTopReads)
            .ToListAsync(ct);

        return ranked.ConvertAll(x => new WorkLogReadDto(x.Path, (int)x.Count));
    }
}

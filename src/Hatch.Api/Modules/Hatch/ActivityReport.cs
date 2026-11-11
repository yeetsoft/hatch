namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// One actor's events over a range, folded into counts by kind, a rundown per
/// issue touched, and the newest rows - the shape <c>GET
/// /api/hatch/reports/activity</c> answers.
///
/// Pure in the way <see cref="CumulativeFlowReport"/> is: rows in, a shape
/// out, with no database, no clock and no DTOs, so the rules can be pinned by
/// tests that build a trail in memory. The rows are light - no payload - since
/// the counts never read one; the caller fetches a payload for only the rows
/// it lists.
/// </summary>
public static class ActivityReport
{
    /// <summary>One event, less its payload. The caller has already narrowed to one actor and one range.</summary>
    public sealed record Row(long Id, long IssueId, int ProjectId, string Kind, DateTimeOffset At);

    public sealed record KindCount(string Kind, int Count);

    /// <summary>One issue the actor touched: when first and last, how many events, and of which kinds.</summary>
    public sealed record IssueEntry(
        long IssueId, DateTimeOffset First, DateTimeOffset Last, int Count, IReadOnlyList<KindCount> Kinds);

    /// <summary>
    /// <see cref="Total"/>, <see cref="Kinds"/> and <see cref="Issues"/> cover every
    /// narrowed row; <see cref="Events"/> is only the newest few.
    /// </summary>
    public sealed record Result(
        int Total, IReadOnlyList<KindCount> Kinds, IReadOnlyList<IssueEntry> Issues,
        IReadOnlyList<Row> Events, bool Truncated);

    /// <summary>
    /// Counts, per-issue rundown and the newest <paramref name="maxEvents"/> rows.
    /// </summary>
    /// <remarks>
    /// Does not filter on time: the range is the caller's query. Narrows by
    /// <paramref name="projectIds"/> when it names any, and by nothing otherwise.
    /// </remarks>
    public static Result Fold(IReadOnlyList<Row> rows, IReadOnlyCollection<int> projectIds, int maxEvents)
    {
        var narrowed = projectIds.Count == 0
            ? rows
            : rows.Where(r => projectIds.Contains(r.ProjectId)).ToList();

        var issues = narrowed
            .GroupBy(r => r.IssueId)
            .Select(g => new IssueEntry(
                g.Key, g.Min(r => r.At), g.Max(r => r.At), g.Count(), KindsOf(g)))
            .OrderByDescending(i => i.Last)
            .ThenByDescending(i => i.IssueId)
            .ToList();

        var events = narrowed
            .OrderByDescending(r => r.At)
            .ThenByDescending(r => r.Id)
            .Take(maxEvents)
            .ToList();

        return new Result(narrowed.Count, KindsOf(narrowed), issues, events, narrowed.Count > maxEvents);
    }

    /// <summary>Most first, then by name.</summary>
    private static List<KindCount> KindsOf(IEnumerable<Row> rows) =>
        rows.GroupBy(r => r.Kind)
            .Select(g => new KindCount(g.Key, g.Count()))
            .OrderByDescending(k => k.Count)
            .ThenBy(k => k.Kind, StringComparer.Ordinal)
            .ToList();
}

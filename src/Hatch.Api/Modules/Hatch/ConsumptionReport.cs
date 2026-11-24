namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// What each issue - and everything beneath it - consumed in a window: tokens,
/// notional USD and runner time, read off the work log rather than the board.
///
/// Pure in the way <see cref="ChangelogReport"/> and <see cref="FlowReport"/>
/// are: rows and sessions in, a shape out, with no database and no clock, so
/// the walk-up rule can be pinned by tests that build a tree in memory.
/// </summary>
public static class ConsumptionReport
{
    /// <summary>One work-log session, as this fold needs it.</summary>
    public readonly record struct Session(
        long IssueId,
        DateTimeOffset EndedAt,
        bool IsError,
        long InputTokens,
        long OutputTokens,
        long CacheCreationTokens,
        long CacheReadTokens,
        decimal? CostUsd,
        long DurationMs);

    /// <summary>
    /// One issue that matches the filter - project, type and status - whether
    /// or not it has a session in the window. <see cref="ParentId"/> is its
    /// immediate parent in the tracker, not necessarily a candidate itself.
    /// </summary>
    public readonly record struct Candidate(
        long Id, long? ParentId, string Key, string Title, string Type, string ProjectKey, int StatusId);

    /// <summary>One candidate's own row: what its subtree cost inside the window.</summary>
    /// <param name="ParentKey">
    /// The nearest candidate directly above this one, or null when its own
    /// parent is not itself a row - the pointer a reader follows to see the
    /// nesting a flat list cannot otherwise show.
    /// </param>
    public sealed record Row(
        string Key, string Title, string Type, string ProjectKey, int StatusId, string? ParentKey,
        WorkLogTotalsDto Totals, DateTimeOffset FirstSessionAt, DateTimeOffset LastSessionAt);

    /// <summary>One equal slice of the window's time axis, folded the way <see cref="WorkLogRollup.SeriesAsync"/> folds one.</summary>
    public sealed record Bucket(DateTimeOffset Start, DateTimeOffset End, WorkLogTotalsDto Totals);

    /// <summary>The whole answer: the filter's own totals, its rows, and its time buckets.</summary>
    public sealed record Result(WorkLogTotalsDto Totals, IReadOnlyList<Row> Rows, IReadOnlyList<Bucket> Buckets);

    /// <summary>How <see cref="Fold"/> ranks its rows. Every one of them descending - see <see cref="WorkLogSort"/> for why.</summary>
    public enum ConsumptionSort { Activity, Tokens, Cost, Time }

    /// <summary>The wire form of a sort, which is its lowercase name.</summary>
    public static string Name(ConsumptionSort sort) => sort switch
    {
        ConsumptionSort.Tokens => "tokens",
        ConsumptionSort.Cost => "cost",
        ConsumptionSort.Time => "time",
        _ => "activity",
    };

    /// <summary>
    /// Every candidate a session's issue sits beneath - itself included -
    /// credited once for every session in the window, plus the window's own
    /// totals and buckets, credited once per session regardless of how many
    /// candidates it fell beneath.
    /// </summary>
    /// <remarks>
    /// <paramref name="sessions"/> is assumed already filtered to the window
    /// and to the requested projects - this fold does not re-check either.
    /// <paramref name="parentOf"/> is every issue's parent, not only
    /// candidates', because a session's issue or an intervening ancestor need
    /// not be a candidate itself for a candidate further up to earn the
    /// credit. A session whose walk meets no candidate at all is dropped: it
    /// contributes to no row, no total and no bucket.
    /// </remarks>
    public static Result Fold(
        IReadOnlyList<Session> sessions, IReadOnlyList<Candidate> candidates,
        IReadOnlyDictionary<long, long?> parentOf, WorkLogWindow window, ConsumptionSort sort) =>
        FoldSums(sessions.Select(s => ToSum(s, window)).ToList(), candidates, parentOf, window, sort);

    /// <summary>
    /// <see cref="Fold"/>'s own session, as the one-session sum
    /// <see cref="FoldSums"/> reads - so a session folded here and a row read
    /// off the database by <see cref="WorkLogSums.ReadAsync"/> are credited by
    /// exactly the same code.
    /// </summary>
    private static WorkLogSums.SessionSum ToSum(Session session, WorkLogWindow window)
    {
        var bucket = (int)((session.EndedAt - window.From).Ticks / window.Length.Ticks);

        return new WorkLogSums.SessionSum(
            session.IssueId, bucket, 1, session.IsError ? 1 : 0,
            session.InputTokens, session.OutputTokens, session.CacheCreationTokens, session.CacheReadTokens,
            session.CostUsd ?? 0m, session.CostUsd is null ? 1 : 0, session.DurationMs,
            session.EndedAt, session.EndedAt);
    }

    /// <summary>
    /// Every candidate a session's issue sits beneath, credited from a row
    /// already summed per issue per bucket - what <see cref="Fold"/> itself
    /// calls, turning each session into a one-session sum first, and what
    /// <see cref="ReportsController.GetConsumption"/> calls directly over what
    /// <see cref="WorkLogSums.ReadAsync"/> read off the database.
    /// </summary>
    /// <remarks>
    /// One narrow row per issue per bucket, not one per session - crediting a
    /// sum up the hierarchy is the same walk <see cref="CandidatesAbove"/>
    /// already does, just added into a row and a bucket instead of a session.
    /// </remarks>
    internal static Result FoldSums(
        IReadOnlyList<WorkLogSums.SessionSum> sums, IReadOnlyList<Candidate> candidates,
        IReadOnlyDictionary<long, long?> parentOf, WorkLogWindow window, ConsumptionSort sort)
    {
        var candidateById = candidates.ToDictionary(c => c.Id);

        var totalSums = new List<WorkLogSums.SessionSum>();
        var rowSums = new Dictionary<long, List<WorkLogSums.SessionSum>>();
        var bucketSums = new List<WorkLogSums.SessionSum>[(int)window.Buckets];
        for (var i = 0; i < bucketSums.Length; i++) bucketSums[i] = [];

        foreach (var sum in sums)
        {
            var path = CandidatesAbove(sum.IssueId, candidateById, parentOf);
            if (path.Count == 0) continue;

            totalSums.Add(sum);

            if (sum.Bucket >= 0 && sum.Bucket < bucketSums.Length) bucketSums[sum.Bucket].Add(sum);

            foreach (var id in path)
            {
                if (!rowSums.TryGetValue(id, out var list)) rowSums[id] = list = [];
                list.Add(sum);
            }
        }

        var rows = rowSums
            .Select(kv =>
            {
                var candidate = candidateById[kv.Key];
                var own = kv.Value;
                var parentKey = candidate.ParentId is { } parentId && candidateById.TryGetValue(parentId, out var parent)
                    ? parent.Key
                    : null;

                return new Row(
                    candidate.Key, candidate.Title, candidate.Type, candidate.ProjectKey, candidate.StatusId,
                    parentKey, FoldTotals(own), own.Min(s => s.First), own.Max(s => s.Last));
            });

        IOrderedEnumerable<Row> ordered = sort switch
        {
            ConsumptionSort.Tokens => rows.OrderByDescending(r => r.Totals.TotalTokens),
            ConsumptionSort.Time => rows.OrderByDescending(r => r.Totals.DurationMs),
            // A row with nothing priced sorts as if its cost were below every
            // real one - WorkLogSort.Cost's own -1m sentinel for a null CostUsd,
            // read here off the fold rather than off a single session's price.
            ConsumptionSort.Cost => rows.OrderByDescending(
                r => r.Totals.UnpricedSessions == r.Totals.Sessions ? -1m : r.Totals.CostUsd),
            _ => rows.OrderByDescending(r => r.LastSessionAt),
        };

        var sortedRows = ordered.ThenBy(r => r.Key, StringComparer.Ordinal).ToList();

        var buckets = new List<Bucket>(bucketSums.Length);
        for (var i = 0; i < bucketSums.Length; i++)
        {
            var start = window.From + window.Length * i;
            buckets.Add(new Bucket(start, start + window.Length, FoldTotals(bucketSums[i])));
        }

        return new Result(FoldTotals(totalSums), sortedRows, buckets);
    }

    /// <summary>
    /// Whether <paramref name="issueId"/> or anything above it is one of
    /// <paramref name="candidateIds"/> - <see cref="CandidatesAbove"/>'s own
    /// walk, stopping at the first hit rather than collecting every one, for
    /// <see cref="ReportsController"/>'s bound reads: a session counts in
    /// <c>firstSessionAt</c>/<c>lastSessionAt</c> exactly when it would have
    /// earned a candidate above it a row.
    /// </summary>
    public static bool HasCandidateAbove(
        long issueId, IReadOnlySet<long> candidateIds, IReadOnlyDictionary<long, long?> parentOf)
    {
        var seen = new HashSet<long>();
        var current = (long?)issueId;

        while (current is { } id && seen.Add(id))
        {
            if (candidateIds.Contains(id)) return true;
            current = parentOf.TryGetValue(id, out var parent) ? parent : null;
        }

        return false;
    }

    /// <summary>
    /// Every candidate on the walk from <paramref name="issueId"/> up through
    /// <paramref name="parentOf"/>, itself included - a <c>seen</c> set
    /// guarding a cycle the same way <see cref="Rollup"/>'s own descent does,
    /// so data nothing else should produce still terminates rather than spinning.
    /// </summary>
    private static List<long> CandidatesAbove(
        long issueId, Dictionary<long, Candidate> candidateById, IReadOnlyDictionary<long, long?> parentOf)
    {
        var found = new List<long>();
        var seen = new HashSet<long>();
        var current = (long?)issueId;

        while (current is { } id && seen.Add(id))
        {
            if (candidateById.ContainsKey(id)) found.Add(id);
            current = parentOf.TryGetValue(id, out var parent) ? parent : null;
        }

        return found;
    }

    /// <summary>
    /// The arithmetic, its own copy rather than a reach into
    /// <see cref="WorkLogRollup"/>'s private fold - this report's rows are not
    /// that fold's rows. Sums of sums: each <see cref="WorkLogSums.SessionSum"/>
    /// is already one issue's one bucket, added together the same way whether
    /// there is one of them (<see cref="Fold"/>'s own one-session sums) or many.
    /// </summary>
    private static WorkLogTotalsDto FoldTotals(IReadOnlyCollection<WorkLogSums.SessionSum> rows)
    {
        var input = rows.Sum(r => r.InputTokens);
        var output = rows.Sum(r => r.OutputTokens);
        var cacheCreation = rows.Sum(r => r.CacheCreationTokens);
        var cacheRead = rows.Sum(r => r.CacheReadTokens);

        return new WorkLogTotalsDto(
            rows.Sum(r => r.Sessions),
            rows.Sum(r => r.Errors),
            input,
            output,
            cacheCreation,
            cacheRead,
            input + output + cacheCreation + cacheRead,
            rows.Sum(r => r.CostUsd),
            rows.Sum(r => r.Unpriced),
            rows.Sum(r => r.DurationMs));
    }
}

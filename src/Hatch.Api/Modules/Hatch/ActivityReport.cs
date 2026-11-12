namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// One actor's events over a range, folded into counts by kind, a rundown per
/// issue touched, the newest rows, a bucketed series with rolling means, and
/// the trail bracketed into working sessions - the shape <c>GET
/// /api/reports/activity</c> answers.
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
    /// <param name="ViaKey">The name of the key a person's own key typed this event under, or null for an event under the actor's own name.</param>
    public sealed record Row(
        long Id, long IssueId, int ProjectId, string Kind, DateTimeOffset At, string? ViaKey = null);

    /// <summary>One <c>claim_taken</c>, <c>claim_released</c> or <c>claim_lapsed</c> on an issue's trail, whoever wrote it.</summary>
    public sealed record ClaimEvent(long Id, long IssueId, string Actor, string Kind, DateTimeOffset At);

    /// <summary>One work-log session's wall-clock span on an issue. A row records no key, so this names none.</summary>
    public sealed record WorkSpan(long IssueId, DateTimeOffset From, DateTimeOffset To);

    public sealed record KindCount(string Kind, int Count);

    /// <summary>One issue the actor touched: when first and last, how many events, and of which kinds.</summary>
    public sealed record IssueEntry(
        long IssueId, DateTimeOffset First, DateTimeOffset Last, int Count, IReadOnlyList<KindCount> Kinds);

    /// <summary>One bucket of the series: a plain count and hours, and each's trailing mean over <see cref="Estimate.WindowDays"/>.</summary>
    public sealed record SeriesPoint(
        DateTimeOffset At, int Events, double Hours, double RollingEvents, double RollingHours);

    /// <summary>
    /// A maximal run of the actor's events with no gap over <see cref="Estimate.GapMinutes"/>
    /// between two consecutive ones - see <see cref="Fold"/> for how the run is cut.
    /// </summary>
    /// <param name="IssueIds">Every issue touched during the session, distinct, first-seen order.</param>
    public sealed record Session(
        DateTimeOffset From, DateTimeOffset To, int Events, IReadOnlyList<long> IssueIds, double EstimatedMinutes);

    /// <summary>The totals <see cref="Sessions"/> add up to, beside the parameters they were computed with.</summary>
    public sealed record Estimate(int Sessions, double Minutes, double GapMinutes, double LeadMinutes, int WindowDays);

    /// <summary>
    /// <see cref="Total"/>, <see cref="Kinds"/> and <see cref="Issues"/> cover every
    /// narrowed row; <see cref="Events"/> is only the newest few.
    /// </summary>
    /// <param name="Series">One point per bucket, oldest first.</param>
    /// <param name="Sessions">Newest <see cref="Session.From"/> first.</param>
    public sealed record Result(
        int Total, IReadOnlyList<KindCount> Kinds, IReadOnlyList<IssueEntry> Issues,
        IReadOnlyList<Row> Events, bool Truncated,
        IReadOnlyList<SeriesPoint> Series, IReadOnlyList<Session> Sessions, Estimate Estimate);

    /// <summary>
    /// Of the events a person's own keys wrote, the ones a hand typed - a
    /// heuristic, read off the trail alone.
    /// </summary>
    /// <remarks>
    /// An event is the loop's when a work-log session of the same issue spans
    /// it (a row records no key, so the issue and the instant are all there is
    /// to match), or when the issue's trail shows the key's <c>claim_taken</c>
    /// with nothing ending that claim before it. A claim is ended by a release,
    /// a lapse, or another take, from anyone, so a runner that died without
    /// releasing swallows no later event. The claim events themselves are the
    /// loop's own. Everything else under the key is a hand's.
    /// </remarks>
    /// <param name="keyRows">Every event under the person's keys in range, each with <see cref="Row.ViaKey"/> set.</param>
    /// <param name="claims">The claim events of the issues <paramref name="keyRows"/> touch, from any time before the range's end.</param>
    public static List<Row> HandTyped(
        IReadOnlyList<Row> keyRows, IReadOnlyList<ClaimEvent> claims, IReadOnlyList<WorkSpan> workLog)
    {
        var claimsByIssue = claims
            .GroupBy(c => c.IssueId)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.At).ThenBy(c => c.Id).ToList());
        var spansByIssue = workLog.ToLookup(w => w.IssueId);

        return keyRows.Where(r => !IsLoops(r, claimsByIssue, spansByIssue)).ToList();
    }

    private static bool IsLoops(
        Row row, Dictionary<long, List<ClaimEvent>> claimsByIssue, ILookup<long, WorkSpan> spansByIssue)
    {
        if (row.Kind.StartsWith("claim_", StringComparison.Ordinal)) return true;

        if (spansByIssue[row.IssueId].Any(w => w.From <= row.At && row.At <= w.To)) return true;

        if (!claimsByIssue.TryGetValue(row.IssueId, out var trail)) return false;

        // Walk the issue's claim events up to this row: the last one decides,
        // and it only holds when it is this key's own take.
        ClaimEvent? last = null;
        foreach (var claim in trail)
        {
            if (claim.At > row.At || (claim.At == row.At && claim.Id > row.Id)) break;
            last = claim;
        }

        return last is { Kind: "claim_taken" } && last.Actor == row.ViaKey;
    }

    /// <summary>
    /// Counts, per-issue rundown, the newest <paramref name="maxEvents"/> rows, a
    /// bucketed series with rolling means, and the trail bracketed into sessions.
    /// </summary>
    /// <remarks>
    /// Does not filter on time: the range is the caller's query. Narrows by
    /// <paramref name="projectIds"/> when it names any, and by nothing otherwise -
    /// every figure below, including the series and the sessions, is computed
    /// over that same narrowed set.
    /// </remarks>
    /// <param name="window">Already snapped outward onto the bucket grid by <see cref="WorkLogRollup.Align"/>.</param>
    /// <param name="gap">The longest stretch between two consecutive events that still counts as one session.</param>
    /// <param name="lead">Added to every session's span, including a lone event's.</param>
    /// <param name="windowDays">How many days the rolling mean trails over, expressed in buckets.</param>
    public static Result Fold(
        IReadOnlyList<Row> rows, IReadOnlyCollection<int> projectIds, int maxEvents,
        WorkLogWindow window, TimeSpan gap, TimeSpan lead, int windowDays)
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

        var sessions = SessionsOf(narrowed, gap, lead);
        var series = SeriesOf(narrowed, sessions, window, windowDays);

        var estimate = new Estimate(
            sessions.Count, sessions.Sum(s => s.EstimatedMinutes), gap.TotalMinutes, lead.TotalMinutes, windowDays);

        return new Result(
            narrowed.Count, KindsOf(narrowed), issues, events, narrowed.Count > maxEvents,
            series, sessions, estimate);
    }

    /// <summary>
    /// The actor's trail, in time order, bracketed into maximal runs with no gap
    /// over <paramref name="gap"/> between two consecutive events - newest
    /// <see cref="Session.From"/> first.
    /// </summary>
    private static List<Session> SessionsOf(IReadOnlyList<Row> narrowed, TimeSpan gap, TimeSpan lead)
    {
        var ordered = narrowed.OrderBy(r => r.At).ThenBy(r => r.Id).ToList();

        var sessions = new List<Session>();
        List<Row>? current = null;

        foreach (var row in ordered)
        {
            if (current is { Count: > 0 } && row.At - current[^1].At > gap)
            {
                sessions.Add(SessionOf(current, lead));
                current = null;
            }

            (current ??= []).Add(row);
        }

        if (current is { Count: > 0 }) sessions.Add(SessionOf(current, lead));

        sessions.Reverse();
        return sessions;
    }

    private static Session SessionOf(List<Row> rows, TimeSpan lead)
    {
        var from = rows[0].At;
        var to = rows[^1].At;
        var issueIds = rows.Select(r => r.IssueId).Distinct().ToList();

        return new Session(from, to, rows.Count, issueIds, (to - from).TotalMinutes + lead.TotalMinutes);
    }

    /// <summary>
    /// One point per bucket of <paramref name="window"/>, oldest first: a plain
    /// count of events and hours credited to the bucket a session <em>starts</em>
    /// in, each beside its own trailing mean.
    /// </summary>
    private static List<SeriesPoint> SeriesOf(
        IReadOnlyList<Row> narrowed, IReadOnlyList<Session> sessions, WorkLogWindow window, int windowDays)
    {
        var buckets = (int)window.Buckets;
        var eventCounts = new int[buckets];
        var hours = new double[buckets];

        foreach (var row in narrowed)
        {
            var index = BucketIndex(row.At, window);
            if (index >= 0 && index < buckets) eventCounts[index]++;
        }

        foreach (var session in sessions)
        {
            var index = BucketIndex(session.From, window);
            if (index >= 0 && index < buckets) hours[index] += session.EstimatedMinutes / 60;
        }

        // The window expressed in buckets, so a daily report trails three
        // buckets for the same three days an hourly one trails seventy-two.
        var windowBuckets = Math.Max(1, (int)(TimeSpan.FromDays(windowDays).Ticks / window.Length.Ticks));

        var points = new List<SeriesPoint>(buckets);
        for (var i = 0; i < buckets; i++)
        {
            var start = Math.Max(0, i - windowBuckets + 1);
            var span = i - start + 1;

            double rollingEvents = 0, rollingHours = 0;
            for (var j = start; j <= i; j++)
            {
                rollingEvents += eventCounts[j];
                rollingHours += hours[j];
            }

            points.Add(new SeriesPoint(
                window.From + window.Length * i, eventCounts[i], hours[i], rollingEvents / span, rollingHours / span));
        }

        return points;
    }

    private static int BucketIndex(DateTimeOffset at, WorkLogWindow window) =>
        (int)((at - window.From).Ticks / window.Length.Ticks);

    /// <summary>Most first, then by name.</summary>
    private static List<KindCount> KindsOf(IEnumerable<Row> rows) =>
        rows.GroupBy(r => r.Kind)
            .Select(g => new KindCount(g.Key, g.Count()))
            .OrderByDescending(k => k.Count)
            .ThenBy(k => k.Kind, StringComparer.Ordinal)
            .ToList();
}

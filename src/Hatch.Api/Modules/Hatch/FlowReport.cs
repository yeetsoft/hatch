namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// How issues moved through each board column over a window: how many
/// entered it, how many left, how long the ones that left dwelled, and how
/// many still stand there - the per-column half of what a flow report draws.
/// The window half (hops, send-backs, questions, sessions) is a later task's
/// and is zero here.
///
/// Pure in the way <see cref="CumulativeFlowReport"/> is: rows and events in,
/// a shape out, with no database and no clock, so the rules can be pinned by
/// tests that build a board and a history in memory.
/// </summary>
public static class FlowReport
{
    /// <summary>One raw issue event, as the fold needs it - only <c>status_changed</c> ones are read.</summary>
    public sealed record Event(long IssueId, long EventId, string Kind, DateTimeOffset At, string? Payload);

    /// <summary>One question asked of an issue - unused until the window half is filled in.</summary>
    public sealed record Question(long IssueId, long Id, DateTimeOffset AskedAt, DateTimeOffset? AnsweredAt);

    /// <summary>One work-log session against an issue - unused until the window half is filled in.</summary>
    public sealed record Session(long IssueId, DateTimeOffset EndedAt, bool IsError);

    /// <summary>One board column's counts over the window.</summary>
    public sealed record Column(int StatusId, int Entered, int Left, int Dwelled,
        long? MedianDwellSeconds, long? P90DwellSeconds, int Standing);

    /// <summary>How many times one hop kind carried an issue across the window - always zero here.</summary>
    public sealed record Hop(string Kind, int Count);

    /// <summary>The window-wide half of a flow report - all zero and null until a later task fills it in.</summary>
    public sealed record Window(int Dropped, IReadOnlyList<Hop> Hops, int SendBacks,
        int QuestionsAsked, int QuestionsAnswered, long? MedianAnswerWaitSeconds, long? LongestAnswerWaitSeconds,
        int Sessions, int SessionErrors);

    public sealed record Result(IReadOnlyList<Column> Columns, Window Window, int Unresolved);

    /// <summary>The seven hop kinds, in this fixed order - see HA-149, HA-113, HA-394 and HA-560.</summary>
    private static readonly string[] HopKindOrder =
        [HopKinds.Express, HopKinds.Parent, HopKinds.Epic, HopKinds.Under, HopKinds.SendBack, HopKinds.Approve, HopKinds.Close];

    /// <summary>
    /// Per board column, how many issues entered and left it, the dwell of the
    /// stretches that left, and how many stand there at <paramref name="to"/>,
    /// over <c>[from, to)</c> - plus the count of unresolved stretches across
    /// every kept row.
    /// </summary>
    /// <remarks>
    /// <paramref name="events"/>, <paramref name="questions"/> and
    /// <paramref name="sessions"/> are accepted for the window half this task
    /// leaves zero; only <paramref name="events"/> is read, and only its
    /// <c>status_changed</c> rows.
    /// </remarks>
    public static Result Fold(
        IReadOnlyList<TimelineReport.Row> rows, IReadOnlyList<Event> events, IReadOnlyList<Question> questions,
        IReadOnlyList<Session> sessions, IReadOnlyList<EfHatchStatus> statuses,
        DateTimeOffset from, DateTimeOffset to, int? projectId, string? type)
    {
        var ordered = statuses.OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToList();
        var columns = Columns.Board(ordered).Select(s => s.Id).ToList();
        var slot = new Dictionary<int, int>();
        for (var i = 0; i < columns.Count; i++) slot[columns[i]] = i;

        var board = TimelineReport.IndexOf(statuses);
        var eventsByIssue = events.ToLookup(e => e.IssueId);

        var entered = new int[columns.Count];
        var left = new int[columns.Count];
        var standing = new int[columns.Count];
        var dwell = new List<long>[columns.Count];
        for (var i = 0; i < dwell.Length; i++) dwell[i] = [];

        var kept = rows.Where(r => (projectId is null || r.ProjectId == projectId) && (type is null || r.Type == type));

        var unresolved = 0;
        foreach (var row in kept)
        {
            var changes = eventsByIssue[row.Id]
                .Where(e => e.Kind == EfHatchIssueEvent.StatusChanged)
                .Select(e => TimelineReport.ParseChange(e.IssueId, e.EventId, e.At, e.Payload))
                .OfType<TimelineReport.Change>();

            var (stretches, rowUnresolved) = TimelineReport.RebuildOf(row, changes, board, from, to);
            unresolved += rowUnresolved;

            foreach (var s in stretches)
            {
                if (!s.Resolved || s.StatusId is not { } id || !slot.TryGetValue(id, out var at)) continue;

                if (s.From >= from && s.From < to) entered[at]++;

                if (s.To is { } end && end >= from && end < to)
                {
                    left[at]++;
                    dwell[at].Add((long)Math.Round((end - s.From).TotalSeconds));
                }

                if (s.From <= to && (s.To is null || s.To > to)) standing[at]++;
            }
        }

        var cols = columns
            .Select((id, i) => new Column(id, entered[i], left[i], dwell[i].Count, Median(dwell[i]), Percentile90(dwell[i]), standing[i]))
            .ToList();

        var window = new Window(
            Dropped: 0, Hops: HopKindOrder.Select(k => new Hop(k, 0)).ToList(), SendBacks: 0,
            QuestionsAsked: 0, QuestionsAnswered: 0, MedianAnswerWaitSeconds: null, LongestAnswerWaitSeconds: null,
            Sessions: 0, SessionErrors: 0);

        return new Result(cols, window, unresolved);
    }

    /// <summary>
    /// The middle value, sorted ascending: the middle one on an odd count, or
    /// the two middles averaged and rounded up on an even one. Null when
    /// empty.
    /// </summary>
    internal static long? Median(List<long> values)
    {
        if (values.Count == 0) return null;

        var sorted = values.Order().ToList();
        var n = sorted.Count;
        if (n % 2 == 1) return sorted[n / 2];

        return (sorted[n / 2 - 1] + sorted[n / 2] + 1) / 2;
    }

    /// <summary>The 90th percentile by nearest rank, sorted ascending. Null when empty.</summary>
    internal static long? Percentile90(List<long> values)
    {
        if (values.Count == 0) return null;

        var sorted = values.Order().ToList();
        var n = sorted.Count;
        return sorted[(9 * n + 9) / 10 - 1];
    }
}

using System.Text.Json;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// How issues moved through each board column over a window: how many
/// entered it, how many left, how long the ones that left dwelled, and how
/// many still stand there - plus the window-wide counts (hops, send-backs,
/// questions, sessions) that do not belong to any one column.
///
/// Pure in the way <see cref="CumulativeFlowReport"/> is: rows and events in,
/// a shape out, with no database and no clock, so the rules can be pinned by
/// tests that build a board and a history in memory.
/// </summary>
public static class FlowReport
{
    /// <summary>One raw issue event, as the fold needs it - <c>status_changed</c> and <c>claim_released</c> are read.</summary>
    public sealed record Event(long IssueId, long EventId, string Kind, DateTimeOffset At, string? Payload);

    /// <summary>
    /// One question asked of an issue. <paramref name="AnsweredAt"/> is when a
    /// person first answered it; <paramref name="LapsedAt"/> is when the board
    /// answered it itself by lapsing it, and is only set where no person has.
    /// </summary>
    public sealed record Question(
        long IssueId, long Id, DateTimeOffset AskedAt, DateTimeOffset? AnsweredAt, DateTimeOffset? LapsedAt = null);

    /// <summary>One work-log session against an issue.</summary>
    public sealed record Session(long IssueId, DateTimeOffset EndedAt, bool IsError);

    /// <summary>One board column's counts over the window.</summary>
    public sealed record Column(int StatusId, int Entered, int Left, int Dwelled,
        long? MedianDwellSeconds, long? P90DwellSeconds, int Standing);

    /// <summary>How many times one hop kind carried an issue across the window.</summary>
    public sealed record Hop(string Kind, int Count);

    /// <summary>The window-wide half of a flow report, over the issues the filters keep.</summary>
    public sealed record Window(int Dropped, IReadOnlyList<Hop> Hops, int SendBacks,
        int QuestionsAsked, int QuestionsAnswered, int QuestionsLapsed, long? MedianAnswerWaitSeconds, long? LongestAnswerWaitSeconds,
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
    /// <paramref name="sessions"/> feed the <see cref="Window"/> half too,
    /// each scoped to a kept row's <see cref="TimelineReport.Row.Id"/> - an
    /// event, question or session against an issue the filters drop counts
    /// nothing.
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
        var questionsByIssue = questions.ToLookup(q => q.IssueId);
        var sessionsByIssue = sessions.ToLookup(s => s.IssueId);

        var entered = new int[columns.Count];
        var left = new int[columns.Count];
        var standing = new int[columns.Count];
        var dwell = new List<long>[columns.Count];
        for (var i = 0; i < dwell.Length; i++) dwell[i] = [];

        var kept = rows.Where(r => (projectId is null || r.ProjectId == projectId) && (type is null || r.Type == type));

        var dropped = 0;
        var hopCounts = HopKindOrder.ToDictionary(k => k, _ => 0);
        var sendBacks = 0;
        var questionsAsked = 0;
        var questionsAnswered = 0;
        var questionsLapsed = 0;
        var waits = new List<long>();
        var sessionCount = 0;
        var sessionErrors = 0;

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

            foreach (var e in eventsByIssue[row.Id])
            {
                if (e.At < from || e.At >= to) continue;

                if (e.Kind == EfHatchIssueEvent.StatusChanged)
                {
                    if (HopKindOf(e.Payload, board) is { } kind) hopCounts[kind]++;

                    if (TimelineReport.ParseChange(e.IssueId, e.EventId, e.At, e.Payload) is { } change
                        && TimelineReport.Resolve(change.From, change.FromId, board) is ({ } fromId, true)
                        && TimelineReport.Resolve(change.To, change.ToId, board) is ({ } toId, true)
                        && slot.TryGetValue(fromId, out var fromSlot)
                        && slot.TryGetValue(toId, out var toSlot)
                        && toSlot < fromSlot)
                    {
                        sendBacks++;
                    }
                }
                else if (e.Kind == EfHatchIssueEvent.ClaimReleased && IsDropped(e.Payload))
                {
                    dropped++;
                }
            }

            foreach (var q in questionsByIssue[row.Id])
            {
                if (q.AskedAt >= from && q.AskedAt < to) questionsAsked++;

                if (q.AnsweredAt is { } answeredAt && answeredAt >= from && answeredAt < to)
                {
                    questionsAnswered++;
                    waits.Add(Math.Max(0, (long)Math.Round((answeredAt - q.AskedAt).TotalSeconds)));
                }

                if (q.LapsedAt is { } lapsedAt && lapsedAt >= from && lapsedAt < to) questionsLapsed++;
            }

            foreach (var ses in sessionsByIssue[row.Id])
            {
                if (ses.EndedAt < from || ses.EndedAt >= to) continue;

                sessionCount++;
                if (ses.IsError) sessionErrors++;
            }
        }

        var cols = columns
            .Select((id, i) => new Column(id, entered[i], left[i], dwell[i].Count, Median(dwell[i]), Percentile90(dwell[i]), standing[i]))
            .ToList();

        var window = new Window(
            Dropped: dropped, Hops: HopKindOrder.Select(k => new Hop(k, hopCounts[k])).ToList(), SendBacks: sendBacks,
            QuestionsAsked: questionsAsked, QuestionsAnswered: questionsAnswered, QuestionsLapsed: questionsLapsed,
            MedianAnswerWaitSeconds: Median(waits), LongestAnswerWaitSeconds: waits.Count == 0 ? null : waits.Max(),
            Sessions: sessionCount, SessionErrors: sessionErrors);

        return new Result(cols, window, unresolved);
    }

    /// <summary>
    /// A <c>status_changed</c> payload's hop flag, or null where it carries
    /// none - a person's drag, a <c>merged</c>/<c>pushback</c>/<c>landed</c>
    /// move and a check verdict move all write no flag and so are not hops.
    /// The approve and close hops share one flag, <c>preapproved</c>; only the
    /// destination - terminal or not - tells them apart.
    /// </summary>
    internal static string? HopKindOf(string? payload, TimelineReport.BoardIndex board)
    {
        if (IssueEventPayload.Parse(payload) is not { ValueKind: JsonValueKind.Object } json) return null;

        if (json.TryGetProperty("sendback", out var sendback) && sendback.ValueKind == JsonValueKind.True) return HopKinds.SendBack;
        if (json.TryGetProperty("pulled", out var pulled) && pulled.ValueKind == JsonValueKind.True) return HopKinds.Parent;
        if (json.TryGetProperty("epic", out var epic) && epic.ValueKind == JsonValueKind.True) return HopKinds.Epic;
        if (json.TryGetProperty("under", out _)) return HopKinds.Under;
        if (json.TryGetProperty("express", out var express) && express.ValueKind == JsonValueKind.True) return HopKinds.Express;

        if (json.TryGetProperty("preapproved", out var preapproved) && preapproved.ValueKind == JsonValueKind.True)
        {
            var change = TimelineReport.ParseChange(0, 0, default, payload);
            return TimelineReport.Resolve(change?.To, change?.ToId, board) is ({ } toId, true) && board.Terminal.Contains(toId)
                ? HopKinds.Close
                : HopKinds.Approve;
        }

        return null;
    }

    /// <summary>A <c>claim_released</c> payload's own outcome, matched ordinal against <see cref="ClaimOutcomes.Dropped"/>.</summary>
    private static bool IsDropped(string? payload) =>
        IssueEventPayload.Parse(payload) is { ValueKind: JsonValueKind.Object } json
        && json.TryGetProperty("outcome", out var outcome) && outcome.ValueKind == JsonValueKind.String
        && outcome.GetString() == ClaimOutcomes.Dropped;

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

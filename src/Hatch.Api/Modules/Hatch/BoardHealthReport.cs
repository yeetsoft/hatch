using System.Text.Json;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The board's health over a window of 24 equal bars: how many issues moved
/// right, how many stood waiting, in flight and done at the end of each bar,
/// and how much was waiting on a person.
///
/// Pure in the way <see cref="CumulativeFlowReport"/> is: rows in, a record
/// out, with no database, no clock and no DTOs, so the rules can be pinned by
/// tests that build a board and a history in memory.
/// </summary>
public static class BoardHealthReport
{
    /// <summary>How many bars a window is drawn in.</summary>
    public const int BarCount = 24;

    /// <summary>The window sizes, in hours, a caller may ask for.</summary>
    public static readonly IReadOnlyList<int> WindowHours = [2, 4, 8, 12, 24];

    /// <summary>One issue as the fold needs it: its row, and the two values the route reads as they stand today.</summary>
    public sealed record Issue(TimelineReport.Row Row, int OwnPreApproval, bool HasPullRequest);

    /// <summary>One <c>status_changed</c> event, with who made it.</summary>
    public sealed record Move(long IssueId, long EventId, DateTimeOffset At, string Actor, string? Payload);

    /// <summary>One claim event: <see cref="EfHatchIssueEvent.ClaimTaken"/>, <see cref="EfHatchIssueEvent.ClaimReleased"/> or <see cref="EfHatchIssueEvent.ClaimCleared"/>.</summary>
    public sealed record ClaimEvent(long IssueId, long EventId, string Kind, DateTimeOffset At, string Actor);

    /// <summary>One change of an issue's own pre-approval, both ends named by <see cref="PreApprovalLevels"/>.</summary>
    public sealed record PreApprovalChange(long IssueId, long EventId, DateTimeOffset At, string From, string To);

    /// <summary>One change to the pull request recorded on an issue: whether a URL stood before, and whether one stands after.</summary>
    public sealed record PullRequestChange(long IssueId, long EventId, DateTimeOffset At, bool Was, bool Recorded);

    /// <summary>The attention panel's own answer for the instant <c>now</c>.</summary>
    public readonly record struct Attention(int PullRequests, int Questions, long? OldestSeconds);

    /// <summary>The window's bars: where it starts and ends, and how wide a bar is.</summary>
    public sealed record Axis(DateTimeOffset From, DateTimeOffset To, int BarMinutes);

    /// <summary>
    /// One bar. <see cref="Board"/>, <see cref="Runner"/> and <see cref="Person"/> split
    /// the moves right made inside it; the rest is read at its end, or at
    /// <c>now</c> for the last bar.
    /// </summary>
    public sealed record Bar(
        DateTimeOffset Start, DateTimeOffset End, int Board, int Runner, int Person,
        int Waiting, int InFlight, int Done, int WaitingPullRequests, int WaitingQuestions,
        long? OldestInFlightSeconds, long? OldestWaitSeconds)
    {
        public int Moved => Board + Runner + Person;
    }

    public sealed record Result(
        DateTimeOffset From, DateTimeOffset To, int BarMinutes, IReadOnlyList<Bar> Bars,
        int Total, int PreviousTotal, int PastNormal, bool HasWorkColumn);

    /// <summary>
    /// The window <paramref name="windowHours"/> long that ends in the bar holding
    /// <paramref name="now"/>. A bar is <c>windowHours * 60 / 24</c> minutes wide and
    /// the last one starts at <paramref name="now"/> floored to a multiple of that
    /// width on the UTC clock, so a bar never slides while <paramref name="now"/>
    /// stays inside it.
    /// </summary>
    public static Axis AxisOf(int windowHours, DateTimeOffset now)
    {
        if (!WindowHours.Contains(windowHours))
            throw new ArgumentOutOfRangeException(nameof(windowHours), windowHours, "a window is 2, 4, 8, 12 or 24 hours");

        var minutes = windowHours * 60 / BarCount;
        var width = TimeSpan.FromMinutes(minutes).Ticks;
        var sinceEpoch = now.UtcTicks - DateTime.UnixEpoch.Ticks;
        var floored = sinceEpoch - ((sinceEpoch % width) + width) % width;
        var lastStart = new DateTimeOffset(floored + DateTime.UnixEpoch.Ticks, TimeSpan.Zero);

        var span = TimeSpan.FromTicks(width);
        return new Axis(lastStart - span * (BarCount - 1), lastStart + span, minutes);
    }

    /// <summary>
    /// The health of the board over <see cref="AxisOf"/>'s window ending at
    /// <paramref name="now"/>, over the rows <paramref name="projectKeys"/> keeps
    /// (null keeps all).
    /// </summary>
    /// <remarks>
    /// Every list may hold more than the window needs; the fold ignores the rest.
    /// <para><b>Callers must supply</b>, for each issue, the newest status change,
    /// claim event, pre-approval change and pull request change from before the
    /// earliest instant read, so a stretch's start and an open claim are right.
    /// <see cref="PreApprovalChange.From"/> and <see cref="PullRequestChange.Was"/>
    /// carry the state before a list's first event, since the route reads only two
    /// windows and today's value is not the value then.</para>
    /// <para><b>Known gaps.</b> A check approving a merge, and a pull request held by
    /// a conflict or a failed build, count as waiting on a person in past bars; only
    /// the attention panel knows the difference, and the last bar takes its answer
    /// when given. Leaf-ness (<paramref name="leaves"/>) is today's, not then.
    /// Pre-approval is walked up the parents the issue has today and ignores the
    /// project line <c>PriorityTree.HonoursAcross</c> honours. None of these is
    /// closed unless the trail holds enough to do it exactly.</para>
    /// <para>Bands are read the way <see cref="CumulativeFlowReport"/> reads columns,
    /// so the two agree at every bar's end.</para>
    /// </remarks>
    public static Result Fold(
        IReadOnlyList<Issue> rows, IReadOnlyList<Move> moves, IReadOnlyList<ClaimEvent> claims,
        IReadOnlyList<PreApprovalChange> levels, IReadOnlyList<PullRequestChange> prRecords,
        IReadOnlyList<FlowReport.Question> questions, IReadOnlyList<EfHatchStatus> statuses,
        int windowHours, DateTimeOffset now, IReadOnlySet<string>? projectKeys, IReadOnlySet<long> leaves,
        IReadOnlyDictionary<int, long> normals, Attention? attention)
    {
        var axis = AxisOf(windowHours, now);
        var width = TimeSpan.FromMinutes(axis.BarMinutes);

        var ordered = statuses.OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToList();
        var board = Columns.Board(ordered);
        var work = Columns.Implementation(ordered);
        var review = Columns.AwaitingReview(ordered);
        var index = TimelineReport.IndexOf(statuses);

        var slot = new Dictionary<int, int>();
        for (var i = 0; i < board.Count; i++) slot[board[i].Id] = i;

        var firstTerminal = board.FindIndex(s => s.IsTerminal);
        var workSlot = work is { } w && slot.TryGetValue(w.Id, out var ws) ? ws : -1;
        var hasWork = workSlot >= 0;

        var scoped = projectKeys is null ? rows : rows.Where(r => projectKeys.Contains(r.Row.ProjectKey)).ToList();
        if (scoped.Count == 0 || scoped.Min(r => r.Row.CreatedAt) > axis.To)
            return new Result(axis.From, axis.To, axis.BarMinutes, [], 0, 0, 0, hasWork);

        // Every row, scoped or not: a parent outside the scope still carries a level down.
        var everyone = rows.GroupBy(r => r.Row.Id).ToDictionary(g => g.Key, g => g.First());

        var movesByIssue = moves.ToLookup(m => m.IssueId);
        var claimsByIssue = claims.ToLookup(c => c.IssueId);
        var levelsByIssue = levels.ToLookup(c => c.IssueId);
        var recordsByIssue = prRecords.ToLookup(c => c.IssueId);
        var questionsByIssue = questions.ToLookup(q => q.IssueId);

        var previousFrom = axis.From - width * BarCount;
        var starts = Enumerable.Range(0, BarCount).Select(k => axis.From + width * k).ToList();
        var reads = starts.Select((s, k) => k == BarCount - 1 ? now : s + width).ToList();

        var movedBoard = new int[BarCount];
        var movedRunner = new int[BarCount];
        var movedPerson = new int[BarCount];
        var previousTotal = 0;

        var waiting = new int[BarCount];
        var inFlight = new int[BarCount];
        var done = new int[BarCount];
        var oldestFlight = new long?[BarCount];
        var waitingPrs = new int[BarCount];
        var waitingQuestions = new int[BarCount];
        var oldestWait = new long?[BarCount];
        var pastNormal = 0;

        void Longest(long?[] into, int bar, DateTimeOffset since, DateTimeOffset read)
        {
            var seconds = (long)Math.Round((read - since).TotalSeconds);
            if (into[bar] is not { } current || seconds > current) into[bar] = seconds;
        }

        foreach (var issue in scoped)
        {
            var row = issue.Row;
            var changes = movesByIssue[row.Id]
                .Select(m => TimelineReport.ParseChange(m.IssueId, m.EventId, m.At, m.Payload))
                .OfType<TimelineReport.Change>()
                .OrderBy(c => c.At).ThenBy(c => c.EventId)
                .ToList();

            // The stretches, cut as the cumulative flow cuts them.
            var stretches = TimelineReport.StretchesOf(row, changes, index)
                .Where(s => s.To is null || s.To > s.From)
                .Where(s => s.Resolved && s.StatusId is not null && slot.ContainsKey(s.StatusId.Value))
                .Select(s => (Id: s.StatusId!.Value, Opens: s.From > row.CreatedAt ? s.From : row.CreatedAt, s.To))
                .ToList();

            bool Reads((int Id, DateTimeOffset Opens, DateTimeOffset? To) s, DateTimeOffset r) =>
                s.Opens <= r && (s.To is null || s.To > r);

            // Moved right.
            var claimTrail = claimsByIssue[row.Id].OrderBy(c => c.EventId).ToList();
            foreach (var move in movesByIssue[row.Id])
            {
                if (move.At < previousFrom || move.At >= axis.To) continue;
                if (TimelineReport.ParseChange(move.IssueId, move.EventId, move.At, move.Payload) is not { } change) continue;
                if (TimelineReport.Resolve(change.From, change.FromId, index) is not ({ } fromId, true)
                    || TimelineReport.Resolve(change.To, change.ToId, index) is not ({ } toId, true)
                    || !slot.TryGetValue(fromId, out var fromSlot) || !slot.TryGetValue(toId, out var toSlot)
                    || toSlot <= fromSlot)
                    continue;

                if (move.At < axis.From)
                {
                    previousTotal++;
                    continue;
                }

                var bar = (int)((move.At - axis.From).Ticks / width.Ticks);
                if (IsBoard(move.Payload, index)) movedBoard[bar]++;
                else if (OpenClaimActor(claimTrail, move.At) == move.Actor) movedRunner[bar]++;
                else movedPerson[bar]++;
            }

            // Bands, and the oldest in flight.
            var isLeaf = leaves.Contains(row.Id);
            if (hasWork)
            {
                for (var k = 0; k < BarCount; k++)
                {
                    foreach (var s in stretches.Where(s => Reads(s, reads[k])))
                    {
                        var at = slot[s.Id];
                        if (board[at].IsTerminal)
                        {
                            if (s.Opens >= axis.From) done[k]++;
                        }
                        else if (at == 0 || (firstTerminal >= 0 && at > firstTerminal)) { }
                        else if (at < workSlot) waiting[k]++;
                        else
                        {
                            inFlight[k]++;
                            if (isLeaf) Longest(oldestFlight, k, s.Opens, reads[k]);
                        }
                    }
                }

                foreach (var s in stretches.Where(s => Reads(s, now)))
                {
                    var at = slot[s.Id];
                    if (board[at].IsTerminal || at == 0 || at < workSlot || (firstTerminal >= 0 && at > firstTerminal)) continue;
                    if (isLeaf && normals.TryGetValue(s.Id, out var normal) && (now - s.Opens).TotalSeconds > normal) pastNormal++;
                }
            }

            // Waiting on a person.
            DateTimeOffset? TerminalEntryAfter(DateTimeOffset after) => stretches
                .Where(s => board[slot[s.Id]].IsTerminal && s.Opens > after)
                .Select(s => (DateTimeOffset?)s.Opens)
                .Min();

            foreach (var q in questionsByIssue[row.Id])
            {
                if (stretches.Any(s => board[slot[s.Id]].IsTerminal && Reads(s, q.AskedAt))) continue;

                var ends = new[] { q.AnsweredAt, q.LapsedAt, TerminalEntryAfter(q.AskedAt) }.Where(e => e is not null).Select(e => e!.Value).ToList();
                var end = ends.Count == 0 ? (DateTimeOffset?)null : ends.Min();

                for (var k = 0; k < BarCount; k++)
                {
                    var r = reads[k];
                    if (q.AskedAt > r || (end is { } e && e <= r)) continue;
                    waitingQuestions[k]++;
                    Longest(oldestWait, k, q.AskedAt, r);
                }
            }

            if (review is { } reviewColumn)
            {
                for (var k = 0; k < BarCount; k++)
                {
                    var r = reads[k];
                    var entered = stretches.Where(s => s.Id == reviewColumn.Id && Reads(s, r)).Select(s => (DateTimeOffset?)s.Opens).FirstOrDefault();
                    if (entered is not { } enteredAt) continue;

                    var (recorded, recordedAt) = RecordedAt(issue, recordsByIssue[row.Id], r);
                    if (!recorded) continue;
                    if (EffectiveLevel(issue.Row.Id, everyone, levelsByIssue, r) == PreApprovalLevels.PreApprovedName) continue;

                    var since = recordedAt > enteredAt ? recordedAt : enteredAt;
                    waitingPrs[k]++;
                    Longest(oldestWait, k, since, r);
                }
            }
        }

        // The last bar is the attention panel's own count, when it is given.
        if (attention is { } panel)
        {
            waitingPrs[BarCount - 1] = panel.PullRequests;
            waitingQuestions[BarCount - 1] = panel.Questions;
            oldestWait[BarCount - 1] = panel.OldestSeconds;
        }

        var bars = new List<Bar>(BarCount);
        for (var k = 0; k < BarCount; k++)
        {
            bars.Add(new Bar(
                starts[k], starts[k] + width, movedBoard[k], movedRunner[k], movedPerson[k],
                waiting[k], inFlight[k], done[k], waitingPrs[k], waitingQuestions[k], oldestFlight[k], oldestWait[k]));
        }

        return new Result(axis.From, axis.To, axis.BarMinutes, bars, bars.Sum(b => b.Moved), previousTotal, pastNormal, hasWork);
    }

    /// <summary>A move the board made itself: a hop, or a merge, a landing or a push back.</summary>
    private static bool IsBoard(string? payload, TimelineReport.BoardIndex board)
    {
        if (FlowReport.HopKindOf(payload, board) is not null) return true;
        if (IssueEventPayload.Parse(payload) is not { ValueKind: JsonValueKind.Object } json) return false;

        return new[] { "merged", "landed", "pushback" }
            .Any(flag => json.TryGetProperty(flag, out var v) && v.ValueKind == JsonValueKind.True);
    }

    /// <summary>
    /// The actor of the claim open at <paramref name="at"/>, replayed in event id
    /// order: a take at or before it opens, a close strictly before it ends, so
    /// a take or a close at the same instant counts as held.
    /// </summary>
    private static string? OpenClaimActor(List<ClaimEvent> trail, DateTimeOffset at)
    {
        string? open = null;
        foreach (var e in trail)
        {
            if (e.Kind == EfHatchIssueEvent.ClaimTaken)
            {
                if (e.At <= at) open = e.Actor;
            }
            else if ((e.Kind == EfHatchIssueEvent.ClaimReleased || e.Kind == EfHatchIssueEvent.ClaimCleared) && e.At < at)
            {
                open = null;
            }
        }

        return open;
    }

    /// <summary>
    /// Whether a pull request stood on the issue at <paramref name="r"/>, and
    /// since when: the last event at or before it, else the first event's
    /// <c>Was</c>, else today's value. A record known only from before the trail
    /// counts from the issue's creation.
    /// </summary>
    private static (bool Recorded, DateTimeOffset At) RecordedAt(
        Issue issue, IEnumerable<PullRequestChange> events, DateTimeOffset r)
    {
        var trail = events.OrderBy(e => e.At).ThenBy(e => e.EventId).ToList();
        if (trail.Count == 0) return (issue.HasPullRequest, issue.Row.CreatedAt);

        var last = trail.LastOrDefault(e => e.At <= r);
        return last is null ? (trail[0].Was, issue.Row.CreatedAt) : (last.Recorded, last.At);
    }

    /// <summary>
    /// The pre-approval that governs the issue at <paramref name="r"/>, by name:
    /// its own where it has one, else the nearest ancestor's, walked up the
    /// parents it has today through every row.
    /// </summary>
    private static string EffectiveLevel(
        long issueId, Dictionary<long, Issue> everyone, ILookup<long, PreApprovalChange> levels, DateTimeOffset r)
    {
        var seen = new HashSet<long>();
        long? at = issueId;
        while (at is { } id && seen.Add(id) && everyone.TryGetValue(id, out var issue))
        {
            var level = LevelAt(issue, levels[id], r);
            if (level != PreApprovalLevels.NoneName) return level;
            at = issue.Row.ParentId;
        }

        return PreApprovalLevels.NoneName;
    }

    private static string LevelAt(Issue issue, IEnumerable<PreApprovalChange> events, DateTimeOffset r)
    {
        var trail = events.OrderBy(e => e.At).ThenBy(e => e.EventId).ToList();
        if (trail.Count == 0) return PreApprovalLevels.Name(issue.OwnPreApproval);

        var last = trail.LastOrDefault(e => e.At <= r);
        return last?.To ?? trail[0].From;
    }
}

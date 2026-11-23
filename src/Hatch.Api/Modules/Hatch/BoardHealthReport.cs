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

    /// <summary>One claim event: <see cref="EfHatchIssueEvent.ClaimTaken"/>, <see cref="EfHatchIssueEvent.ClaimReleased"/>, <see cref="EfHatchIssueEvent.ClaimCleared"/> or <see cref="EfHatchIssueEvent.ClaimLapsed"/>. The Moved fold ignores a lapse; the runners' lanes read it.</summary>
    public sealed record ClaimEvent(long IssueId, long EventId, string Kind, DateTimeOffset At, string Actor, string? Payload = null);

    /// <summary>One change of an issue's own pre-approval, both ends named by <see cref="PreApprovalLevels"/>.</summary>
    public sealed record PreApprovalChange(long IssueId, long EventId, DateTimeOffset At, string From, string To);

    /// <summary>One change to the pull request recorded on an issue: whether a URL stood before, and whether one stands after.</summary>
    public sealed record PullRequestChange(long IssueId, long EventId, DateTimeOffset At, bool Was, bool Recorded);

    /// <summary>The attention panel's own answer for the instant <c>now</c>.</summary>
    public readonly record struct Attention(int PullRequests, int Questions, long? OldestSeconds);

    /// <summary>The window's bars: where it starts and ends, and how wide a bar is.</summary>
    public sealed record Axis(DateTimeOffset From, DateTimeOffset To, int BarMinutes);

    /// <summary>
    /// The four age groups of the in-flight issues with no children at one read. They add up
    /// to those issues, not to <see cref="Bar.InFlight"/>, which also counts parents.
    /// </summary>
    public sealed record Ages(int Healthy, int Aging, int PastNormal, int NoNormal);

    /// <summary>
    /// One issue in flight at <c>now</c>: its row, the column and when it entered, how long it
    /// has stood there in whole seconds, the column's normal, and its <see cref="ColumnNormals.AgeBand"/>.
    /// </summary>
    public sealed record InFlightRow(
        TimelineReport.Row Row, int StatusId, DateTimeOffset Opens, long Seconds, long? NormalSeconds, string Band);

    /// <summary>
    /// One bar. <see cref="Board"/>, <see cref="Runner"/> and <see cref="Person"/> split
    /// the moves right made inside it; the rest is read at its end, or at
    /// <c>now</c> for the last bar.
    /// </summary>
    public sealed record Bar(
        DateTimeOffset Start, DateTimeOffset End, int Board, int Runner, int Person,
        int Waiting, int InFlight, int Done, int WaitingPullRequests, int WaitingQuestions,
        long? OldestInFlightSeconds, long? OldestWaitSeconds, Ages Ages)
    {
        public int Moved => Board + Runner + Person;
    }

    public sealed record Result(
        DateTimeOffset From, DateTimeOffset To, int BarMinutes, IReadOnlyList<Bar> Bars,
        int Total, int PreviousTotal, int PastNormal, bool HasWorkColumn, IReadOnlyList<InFlightRow> InFlightNow);

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
    /// when given. Leaf-ness (<paramref name="leaves"/>) is today's, not then, and an earlier
    /// bar's age groups are judged against today's normals.
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
            return new Result(axis.From, axis.To, axis.BarMinutes, [], 0, 0, 0, hasWork, []);

        // Every row, scoped or not: a parent outside the scope still carries a level down.
        var everyone = rows.GroupBy(r => r.Row.Id).ToDictionary(g => g.Key, g => g.First());

        var movesByIssue = moves.ToLookup(m => m.IssueId);
        var levelsByIssue = levels.ToLookup(c => c.IssueId);
        var recordsByIssue = prRecords.ToLookup(c => c.IssueId);
        var questionsByIssue = questions.ToLookup(q => q.IssueId);

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
        var ageHealthy = new int[BarCount];
        var ageAging = new int[BarCount];
        var agePast = new int[BarCount];
        var ageNone = new int[BarCount];
        var listed = new List<InFlightRow>();

        // Moved right: the scoped issues' own forward moves, by the rule the
        // runners' lanes read too.
        var scopedIds = scoped.Select(r => r.Row.Id).ToHashSet();
        foreach (var m in ForwardMoves(moves.Where(m => scopedIds.Contains(m.IssueId)).ToList(), claims, statuses, axis))
        {
            if (m.At < axis.From)
            {
                previousTotal++;
                continue;
            }

            var bar = (int)((m.At - axis.From).Ticks / width.Ticks);
            if (m.Kind == MoveKind.Board) movedBoard[bar]++;
            else if (m.Kind == MoveKind.Runner) movedRunner[bar]++;
            else movedPerson[bar]++;
        }

        void Longest(long?[] into, int bar, DateTimeOffset since, DateTimeOffset read)
        {
            var seconds = (long)Math.Round((read - since).TotalSeconds);
            if (into[bar] is not { } current || seconds > current) into[bar] = seconds;
        }

        foreach (var issue in scoped)
        {
            var row = issue.Row;
            // The stretches, cut as the cumulative flow cuts them.
            var stretches = StretchesOf(row, movesByIssue[row.Id], index, slot);

            bool Reads((int Id, DateTimeOffset Opens, DateTimeOffset? To) s, DateTimeOffset r) =>
                s.Opens <= r && (s.To is null || s.To > r);

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
                            if (isLeaf)
                            {
                                Longest(oldestFlight, k, s.Opens, reads[k]);
                                switch (ColumnNormals.AgeBand(normals, s.Id, s.Opens, reads[k]))
                                {
                                    case ColumnNormals.NoNormal: ageNone[k]++; break;
                                    case ColumnNormals.PastNormalBand: agePast[k]++; break;
                                    case ColumnNormals.Healthy: ageHealthy[k]++; break;
                                    default: ageAging[k]++; break;
                                }
                            }
                        }
                    }
                }

                foreach (var s in stretches.Where(s => Reads(s, now)))
                {
                    var at = slot[s.Id];
                    if (board[at].IsTerminal || at == 0 || at < workSlot || (firstTerminal >= 0 && at > firstTerminal)) continue;
                    if (!isLeaf) continue;
                    listed.Add(new InFlightRow(
                        row, s.Id, s.Opens, (long)Math.Round((now - s.Opens).TotalSeconds),
                        normals.TryGetValue(s.Id, out var normal) ? normal : null,
                        ColumnNormals.AgeBand(normals, s.Id, s.Opens, now)));
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
                waiting[k], inFlight[k], done[k], waitingPrs[k], waitingQuestions[k], oldestFlight[k], oldestWait[k],
                new Ages(ageHealthy[k], ageAging[k], agePast[k], ageNone[k])));
        }

        // The answer's past-normal count is the list's own, so the two cannot differ.
        var inFlightNow = listed
            .OrderByDescending(r => r.Seconds).ThenBy(r => r.Opens).ThenBy(r => r.Row.Id)
            .ToList();
        var pastNormal = inFlightNow.Count(r => r.Band == ColumnNormals.PastNormalBand);

        return new Result(axis.From, axis.To, axis.BarMinutes, bars, bars.Sum(b => b.Moved), previousTotal, pastNormal, hasWork, inFlightNow);
    }

    /// <summary>
    /// An issue's stretches, cut as the cumulative flow cuts them: a zero-length
    /// stretch dropped, an unresolved or off-board one dropped, and a stretch
    /// never opening before the issue was created. <see cref="Fold"/> and
    /// <see cref="LastWaited"/> read the same stretches.
    /// </summary>
    private static List<(int Id, DateTimeOffset Opens, DateTimeOffset? To)> StretchesOf(
        TimelineReport.Row row, IEnumerable<Move> moves, TimelineReport.BoardIndex index, Dictionary<int, int> slot)
    {
        var changes = moves
            .Select(m => TimelineReport.ParseChange(m.IssueId, m.EventId, m.At, m.Payload))
            .OfType<TimelineReport.Change>()
            .OrderBy(c => c.At).ThenBy(c => c.EventId)
            .ToList();

        return TimelineReport.StretchesOf(row, changes, index)
            .Where(s => s.To is null || s.To > s.From)
            .Where(s => s.Resolved && s.StatusId is not null && slot.ContainsKey(s.StatusId.Value))
            .Select(s => (Id: s.StatusId!.Value, Opens: s.From > row.CreatedAt ? s.From : row.CreatedAt, s.To))
            .ToList();
    }

    /// <summary>How far back the last wait is looked for, and the oldest an answer can say.</summary>
    public static readonly TimeSpan WaitLookback = TimeSpan.FromDays(30);

    /// <summary>
    /// The instant the latest wait on a person ended, inside <c>[from, to]</c>,
    /// over the rows <paramref name="projectKeys"/> keeps (null keeps all); null
    /// when none ended in that range.
    /// </summary>
    /// <remarks>
    /// "Waited" is the bars' own rule (<see cref="Fold"/>): a question that was
    /// open, or a pull request in review that was recorded and not pre-approved.
    /// A wait ends at an event's own instant - an answer or a lapse, the issue
    /// leaving review or entering a terminal column, the record removed, or
    /// pre-approval raised on the issue or an ancestor - not at a bar's edge.
    /// A pull request's wait ends at a candidate instant <c>e</c> when it held
    /// one tick before and does not hold at <c>e</c>. A wait still open at
    /// <paramref name="to"/> has not ended and is skipped.
    /// <para>Takes the same inputs as <see cref="Fold"/>: for each issue, the
    /// newest event before <paramref name="from"/> as well as every event after
    /// it, and the same known gaps.</para>
    /// </remarks>
    public static DateTimeOffset? LastWaited(
        IReadOnlyList<Issue> rows, IReadOnlyList<Move> moves, IReadOnlyList<PullRequestChange> prRecords,
        IReadOnlyList<PreApprovalChange> levels, IReadOnlyList<FlowReport.Question> questions,
        IReadOnlyList<EfHatchStatus> statuses, DateTimeOffset from, DateTimeOffset to, IReadOnlySet<string>? projectKeys)
    {
        var ordered = statuses.OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToList();
        var board = Columns.Board(ordered);
        var review = Columns.AwaitingReview(ordered);
        var index = TimelineReport.IndexOf(statuses);

        var slot = new Dictionary<int, int>();
        for (var i = 0; i < board.Count; i++) slot[board[i].Id] = i;

        var scoped = projectKeys is null ? rows : rows.Where(r => projectKeys.Contains(r.Row.ProjectKey)).ToList();
        if (scoped.Count == 0) return null;

        var everyone = rows.GroupBy(r => r.Row.Id).ToDictionary(g => g.Key, g => g.First());
        var movesByIssue = moves.ToLookup(m => m.IssueId);
        var levelsByIssue = levels.ToLookup(c => c.IssueId);
        var recordsByIssue = prRecords.ToLookup(c => c.IssueId);
        var questionsByIssue = questions.ToLookup(q => q.IssueId);

        DateTimeOffset? latest = null;
        void Ended(DateTimeOffset at)
        {
            if (at < from || at > to) return;
            if (latest is not { } current || at > current) latest = at;
        }

        static bool Stands((int Id, DateTimeOffset Opens, DateTimeOffset? To) s, DateTimeOffset r) =>
            s.Opens <= r && (s.To is null || s.To > r);

        foreach (var issue in scoped)
        {
            var row = issue.Row;
            var stretches = StretchesOf(row, movesByIssue[row.Id], index, slot);

            foreach (var q in questionsByIssue[row.Id])
            {
                if (stretches.Any(s => board[slot[s.Id]].IsTerminal && Stands(s, q.AskedAt))) continue;

                var terminalEntry = stretches
                    .Where(s => board[slot[s.Id]].IsTerminal && s.Opens > q.AskedAt)
                    .Select(s => (DateTimeOffset?)s.Opens)
                    .Min();
                var ends = new[] { q.AnsweredAt, q.LapsedAt, terminalEntry }
                    .Where(e => e is not null).Select(e => e!.Value).ToList();
                if (ends.Count == 0) continue;

                var end = ends.Min();
                if (end > q.AskedAt) Ended(end);
            }

            if (review is not { } reviewColumn) continue;

            bool Waits(DateTimeOffset r)
            {
                if (!stretches.Any(s => s.Id == reviewColumn.Id && Stands(s, r))) return false;
                if (!RecordedAt(issue, recordsByIssue[row.Id], r).Recorded) return false;
                return EffectiveLevel(row.Id, everyone, levelsByIssue, r) != PreApprovalLevels.PreApprovedName;
            }

            var candidates = new HashSet<DateTimeOffset>();
            foreach (var s in stretches)
            {
                candidates.Add(s.Opens);
                if (s.To is { } closed) candidates.Add(closed);
            }

            foreach (var c in recordsByIssue[row.Id]) candidates.Add(c.At);

            var seen = new HashSet<long>();
            long? up = row.Id;
            while (up is { } id && seen.Add(id) && everyone.TryGetValue(id, out var above))
            {
                foreach (var c in levelsByIssue[id]) candidates.Add(c.At);
                up = above.Row.ParentId;
            }

            foreach (var e in candidates.Where(e => e >= from && e <= to))
            {
                if (Waits(e - TimeSpan.FromTicks(1)) && !Waits(e)) Ended(e);
            }
        }

        return latest;
    }

    /// <summary>
    /// What the health read answers for "when did anything last wait": the later
    /// of the window's own answer and the kept thirty-day one, dropped when older
    /// than thirty days, else a floor. Null, not a floor, when nothing is in scope
    /// (<paramref name="earliestCreated"/> is null) or something waits now.
    /// </summary>
    /// <remarks>
    /// The floor is <paramref name="now"/> minus thirty days, so a page can say
    /// "over 30d"; on a board younger than that it is the earliest scoped issue's
    /// creation, since nothing could have waited before it.
    /// </remarks>
    public static (DateTimeOffset? At, bool IsFloor) Settle(
        DateTimeOffset? window, DateTimeOffset? kept, int waitingNow, DateTimeOffset? earliestCreated, DateTimeOffset now)
    {
        if (earliestCreated is not { } created || waitingNow > 0) return (null, false);

        var floor = now - WaitLookback;
        var best = window is { } w && kept is { } k ? (w > k ? w : k) : window ?? kept;
        if (best is { } at && at >= floor) return (at, false);

        return (created > floor ? created : floor, true);
    }

    /// <summary>Who made a move right: the board itself, the runner holding the claim, or anybody else.</summary>
    public enum MoveKind { Board, Runner, Person }

    /// <summary>One move right, with who made it. <see cref="Actor"/> is the event's own actor.</summary>
    public sealed record ForwardMove(long IssueId, long EventId, DateTimeOffset At, MoveKind Kind, string Actor);

    /// <summary>
    /// Every move right made from the start of the window before
    /// <paramref name="axis"/>'s to its end, each with its kind. The one rule
    /// behind the Moved tile's counts and the runners' <c>moved</c> cells, so
    /// the two cannot disagree: a move is the board's when it is a hop, a
    /// merge, a landing or a push back; a runner's when the actor is the one
    /// holding the issue's claim at that instant; a person's otherwise.
    /// </summary>
    /// <remarks>
    /// Takes any moves and claims, not only a scope's: the Moved tile hands it
    /// the scoped issues' moves and the lanes hand it everyone's. A column the
    /// board does not have, or a move between columns it cannot resolve, is
    /// not a move right.
    /// </remarks>
    public static IReadOnlyList<ForwardMove> ForwardMoves(
        IReadOnlyList<Move> moves, IReadOnlyList<ClaimEvent> claims, IReadOnlyList<EfHatchStatus> statuses, Axis axis)
    {
        var width = TimeSpan.FromMinutes(axis.BarMinutes);
        var previousFrom = axis.From - width * BarCount;

        var board = Columns.Board(statuses.OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToList());
        var index = TimelineReport.IndexOf(statuses);
        var slot = new Dictionary<int, int>();
        for (var i = 0; i < board.Count; i++) slot[board[i].Id] = i;

        var claimTrails = claims.GroupBy(c => c.IssueId)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.EventId).ToList());

        var forward = new List<ForwardMove>();
        foreach (var move in moves)
        {
            if (move.At < previousFrom || move.At >= axis.To) continue;
            if (TimelineReport.ParseChange(move.IssueId, move.EventId, move.At, move.Payload) is not { } change) continue;
            if (TimelineReport.Resolve(change.From, change.FromId, index) is not ({ } fromId, true)
                || TimelineReport.Resolve(change.To, change.ToId, index) is not ({ } toId, true)
                || !slot.TryGetValue(fromId, out var fromSlot) || !slot.TryGetValue(toId, out var toSlot)
                || toSlot <= fromSlot)
                continue;

            var kind = IsBoard(move.Payload, index) ? MoveKind.Board
                : claimTrails.TryGetValue(move.IssueId, out var trail) && OpenClaimActor(trail, move.At) == move.Actor ? MoveKind.Runner
                : MoveKind.Person;
            forward.Add(new ForwardMove(move.IssueId, move.EventId, move.At, kind, move.Actor));
        }

        return forward;
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

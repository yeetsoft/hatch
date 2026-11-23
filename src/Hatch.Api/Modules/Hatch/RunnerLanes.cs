using Hatch.Contracts;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// A lane for each runner over the board health window: one cell per bar,
/// saying the most telling thing the runner did in it.
///
/// Pure in the way <see cref="BoardHealthReport"/> is: rows in, a record out,
/// with no database, no clock and no DTOs, so the rules can be pinned by tests
/// that build a history in memory.
/// </summary>
/// <remarks>
/// <para>A cell is the first of these that holds in its bar: <see cref="LetGo"/>,
/// <see cref="Down"/>, <see cref="Moved"/>, <see cref="Working"/>, <see cref="Idle"/>,
/// <see cref="Paused"/>, <see cref="Off"/>, <see cref="Blank"/>. The state record
/// (<see cref="RunnerStateRule"/>) says whether a runner was up; the claim
/// trail says what it held; the forward moves are the Moved tile's own
/// (<see cref="BoardHealthReport.ForwardMoves"/>), so the two agree by construction.</para>
///
/// <para><b>Known gaps.</b> A bar before the state record began draws from claims
/// alone and is otherwise blank. A claim left open, or ended by a lapse, reads
/// as held only until the record first says the runner went gone. The work-log
/// row of a failed session has no runner on it, so it belongs to the holder
/// open when it reached the board; a lease lost to another runner puts the row
/// on the new holder.</para>
/// </remarks>
public static class RunnerLanes
{
    public const string LetGo = "let-go";
    public const string Down = "down";
    public const string Moved = "moved";
    public const string Working = "working";
    public const string Idle = "idle";
    public const string Paused = "paused";
    public const string Off = "off";
    public const string Blank = "blank";

    public const string Dropped = "dropped";
    public const string Lapsed = "lapsed";
    public const string Stalled = "stalled";
    public const string Errored = "errored";

    public const string OutOfUsage = "out-of-usage";
    public const string AgentDown = "agent-down";
    public const string Board = "board";

    /// <summary>When a bar holds several let-gos, the first of these is the one it says.</summary>
    private static readonly string[] LetGoOrder = [Dropped, Lapsed, Stalled, Errored];

    /// <summary>One runner as the fold needs it: who it is, when it was last heard from, whether it is live now, and its state timeline.</summary>
    /// <param name="Name">The runner's name, which is the actor on its claims.</param>
    /// <param name="LastSeenAt">When the runner was last heard from.</param>
    /// <param name="LiveNow">Whether the runner is live now; read only when it has no timeline.</param>
    /// <param name="Timeline">The runner's state changes, oldest first, folded by <see cref="RunnerStateRule.Timeline"/>. Empty when the record never began.</param>
    public sealed record RunnerInput(
        string Name, DateTimeOffset LastSeenAt, bool LiveNow, IReadOnlyList<RunnerStateRule.Change> Timeline);

    /// <summary>One <c>stalled</c> event: an increment left the ticket where it found it. The actor is the runner that marked it.</summary>
    public sealed record Stall(long IssueId, DateTimeOffset At, string Actor);

    /// <summary>One work-log row whose session ended in error, placed by when it reached the board.</summary>
    public sealed record FailedSession(long IssueId, DateTimeOffset At);

    public sealed record Cell(string State, string? Why);

    public sealed record Lane(string Name, bool Up, bool Paused, DateTimeOffset LastSeenAt, IReadOnlyList<Cell> Cells);

    public sealed record Result(int Up, int Paused, IReadOnlyList<Lane> Lanes);

    /// <summary>A claim held from <c>Start</c>, to <c>End</c> or still. <c>Unsure</c> when nothing says it was let go.</summary>
    private sealed record Interval(long IssueId, string Runner, DateTimeOffset Start, DateTimeOffset? End, bool Unsure);

    private sealed record LetGoAt(string Runner, DateTimeOffset At, string Why);

    private sealed record Stretch(string State, string? Kind, DateTimeOffset From, DateTimeOffset? To);

    /// <summary>
    /// The lanes of <paramref name="runners"/> over <paramref name="axis"/>'s
    /// bars, the last of which ends at <paramref name="now"/>. A runner has a
    /// lane when some bar is neither off nor blank. Lanes are sorted up now
    /// first, then heard from most recently, then by name, ordinal.
    /// </summary>
    /// <param name="claims">Every claim event of every issue from the window's start, plus each issue's newest one before it when it is a take.</param>
    /// <param name="moves">The forward moves, from <see cref="BoardHealthReport.ForwardMoves"/>.</param>
    public static Result Fold(
        IReadOnlyList<RunnerInput> runners, IReadOnlyList<BoardHealthReport.ClaimEvent> claims,
        IReadOnlyList<BoardHealthReport.ForwardMove> moves, IReadOnlyList<Stall> stalls,
        IReadOnlyList<FailedSession> failed, BoardHealthReport.Axis axis, DateTimeOffset now)
    {
        var width = TimeSpan.FromMinutes(axis.BarMinutes);
        var starts = Enumerable.Range(0, BoardHealthReport.BarCount).Select(k => axis.From + width * k).ToList();
        // Half open, so an instant belongs to one bar; the last takes `now` itself.
        var ends = starts.Select((s, k) => k == BoardHealthReport.BarCount - 1 ? now.AddTicks(1) : s + width).ToList();

        var (intervals, releases) = Replay(claims);

        var letGos = new List<LetGoAt>(releases);
        foreach (var s in stalls) letGos.Add(new LetGoAt(s.Actor, s.At, Stalled));
        foreach (var f in failed)
        {
            var holder = intervals
                .Where(i => i.IssueId == f.IssueId && i.Start <= f.At && (i.End is null || f.At <= i.End))
                .OrderBy(i => i.Start)
                .FirstOrDefault();
            if (holder is not null) letGos.Add(new LetGoAt(holder.Runner, f.At, Errored));
        }

        var lanes = new List<Lane>();
        foreach (var runner in runners.GroupBy(r => r.Name, StringComparer.Ordinal).Select(g => g.First()))
        {
            var timeline = runner.Timeline;
            var stretches = StretchesOf(timeline);
            var own = letGos.Where(l => l.Runner == runner.Name).ToList();
            var moved = moves
                .Where(m => m.Kind == BoardHealthReport.MoveKind.Runner && m.Actor == runner.Name && m.At >= axis.From)
                .ToList();
            var held = Capped(intervals.Where(i => i.Runner == runner.Name), timeline);

            var cells = new List<Cell>(BoardHealthReport.BarCount);
            for (var k = 0; k < BoardHealthReport.BarCount; k++)
                cells.Add(CellOf(starts[k], ends[k], timeline, stretches, own, moved, held));

            if (cells.All(c => c.State is Off or Blank)) continue;

            var (up, paused) = NowOf(runner, timeline, now);
            lanes.Add(new Lane(runner.Name, up, paused, runner.LastSeenAt, cells));
        }

        var ordered = lanes
            .OrderByDescending(l => l.Up)
            .ThenByDescending(l => l.LastSeenAt)
            .ThenBy(l => l.Name, StringComparer.Ordinal)
            .ToList();

        return new Result(ordered.Count(l => l.Up), ordered.Count(l => l.Up && l.Paused), ordered);
    }

    private static Cell CellOf(
        DateTimeOffset start, DateTimeOffset end, IReadOnlyList<RunnerStateRule.Change> timeline, List<Stretch> stretches,
        List<LetGoAt> letGos, List<BoardHealthReport.ForwardMove> moved, List<(DateTimeOffset Start, DateTimeOffset? End)> held)
    {
        var inBar = letGos.Where(l => l.At >= start && l.At < end).Select(l => l.Why).ToList();
        if (inBar.Count > 0) return new Cell(LetGo, LetGoOrder.First(inBar.Contains));

        var seen = stretches.Where(t => t.From < end && (t.To is null || t.To > start)).ToList();

        if (seen.FirstOrDefault(t => t.State == RunnerStateRule.AgentDown && t.Kind != RunnerAgentDownKinds.BoardPaused) is { } down)
        {
            return new Cell(Down, down.Kind switch
            {
                RunnerAgentDownKinds.NotStarting => RunnerAgentDownKinds.NotStarting,
                RunnerAgentDownKinds.SignedOut => RunnerAgentDownKinds.SignedOut,
                _ => AgentDown,
            });
        }

        if (seen.Any(t => t.State == RunnerStateRule.Exhausted)) return new Cell(Down, OutOfUsage);

        if (moved.Any(m => m.At >= start && m.At < end)) return new Cell(Moved, null);

        if (held.Any(h => Overlaps(h.Start, h.End, start, end))) return new Cell(Working, null);

        if (seen.Any(t => t.State is RunnerStateRule.Running or RunnerStateRule.Stopping)) return new Cell(Idle, null);

        var paused = seen.Where(t => IsPaused(t.State, t.Kind)).ToList();
        if (paused.Count > 0)
            return new Cell(Paused, paused.Any(t => t.State == RunnerStateRule.AgentDown) ? Board : null);

        // Covered, and none of it up: gone. Before the record began there is nothing to say.
        return timeline.Count > 0 && timeline[0].At < end ? new Cell(Off, null) : new Cell(Blank, null);
    }

    /// <summary>A pause a person set, or one the board imposed, which is stored as an agent down of its own kind.</summary>
    private static bool IsPaused(string state, string? kind) =>
        state == RunnerStateRule.Paused || (state == RunnerStateRule.AgentDown && kind == RunnerAgentDownKinds.BoardPaused);

    private static bool Overlaps(DateTimeOffset from, DateTimeOffset? to, DateTimeOffset start, DateTimeOffset end) =>
        from < end && (to is null || to > start || (to == from && from >= start));

    /// <summary>
    /// The claim intervals and the releases that are let-gos, from each issue's
    /// claim events replayed in event id order: a take opens, a release or a
    /// clear closes, a take over an open claim closes it, and a lapse closes it
    /// and is the let-go of the holder it lapsed - its actor is whoever took
    /// over.
    /// </summary>
    private static (List<Interval> Intervals, List<LetGoAt> Releases) Replay(IReadOnlyList<BoardHealthReport.ClaimEvent> claims)
    {
        var intervals = new List<Interval>();
        var releases = new List<LetGoAt>();

        foreach (var issue in claims.GroupBy(c => c.IssueId))
        {
            (string Runner, DateTimeOffset Start)? open = null;

            foreach (var e in issue.OrderBy(c => c.EventId))
            {
                if (e.Kind == EfHatchIssueEvent.ClaimTaken)
                {
                    if (open is { } over) intervals.Add(new Interval(issue.Key, over.Runner, over.Start, e.At, false));
                    open = (e.Actor, e.At);
                }
                else if (e.Kind == EfHatchIssueEvent.ClaimReleased || e.Kind == EfHatchIssueEvent.ClaimCleared)
                {
                    if (e.Kind == EfHatchIssueEvent.ClaimReleased && FlowReport.IsDropped(e.Payload))
                        releases.Add(new LetGoAt(e.Actor, e.At, Dropped));

                    if (open is { } closed) intervals.Add(new Interval(issue.Key, closed.Runner, closed.Start, e.At, false));
                    open = null;
                }
                else if (e.Kind == EfHatchIssueEvent.ClaimLapsed && open is { } lapsed)
                {
                    releases.Add(new LetGoAt(lapsed.Runner, e.At, Lapsed));
                    intervals.Add(new Interval(issue.Key, lapsed.Runner, lapsed.Start, e.At, true));
                    open = null;
                }
            }

            if (open is { } still) intervals.Add(new Interval(issue.Key, still.Runner, still.Start, null, true));
        }

        return (intervals, releases);
    }

    /// <summary>
    /// A runner's claim intervals for the cells: one still open, or ended by a
    /// lapse, ends no later than the instant the timeline first says gone after
    /// its start, and one that started while the runner was gone held nothing.
    /// </summary>
    private static List<(DateTimeOffset Start, DateTimeOffset? End)> Capped(
        IEnumerable<Interval> intervals, IReadOnlyList<RunnerStateRule.Change> timeline)
    {
        var held = new List<(DateTimeOffset Start, DateTimeOffset? End)>();
        foreach (var i in intervals)
        {
            var end = i.End;
            if (i.Unsure && timeline.Count > 0)
            {
                DateTimeOffset? gone = StateAt(timeline, i.Start)?.State == RunnerStateRule.Gone
                    ? i.Start
                    : timeline.Where(c => c.State == RunnerStateRule.Gone && c.At > i.Start).Select(c => (DateTimeOffset?)c.At).Min();
                if (gone is { } at && (end is null || at < end)) end = at;
                if (end is { } e && e <= i.Start) continue;
            }

            held.Add((i.Start, end));
        }

        return held;
    }

    private static RunnerStateRule.Change? StateAt(IReadOnlyList<RunnerStateRule.Change> timeline, DateTimeOffset t) =>
        timeline.LastOrDefault(c => c.At <= t);

    private static List<Stretch> StretchesOf(IReadOnlyList<RunnerStateRule.Change> timeline) =>
        timeline.Select((c, i) => new Stretch(c.State, c.Kind, c.At, i + 1 < timeline.Count ? timeline[i + 1].At : null)).ToList();

    /// <summary>Whether the runner is up now, and if so whether it is paused: from the timeline, else from whether it is live.</summary>
    private static (bool Up, bool Paused) NowOf(RunnerInput runner, IReadOnlyList<RunnerStateRule.Change> timeline, DateTimeOffset now)
    {
        if (StateAt(timeline, now) is not { } state) return (runner.LiveNow, false);

        var up = state.State != RunnerStateRule.Gone;
        return (up, up && IsPaused(state.State, state.Kind));
    }
}

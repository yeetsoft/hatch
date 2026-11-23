using Hatch.Contracts;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// What state a runner is in at an instant, judged from the facts its rows
/// hold - and the instants at which the clock alone changes the answer. Pure:
/// rows in, a shape out, no database and no clock, the way
/// <see cref="CumulativeFlowReport"/> and <see cref="TimelineReport"/> fold
/// theirs.
/// </summary>
/// <remarks>
/// <para>The page ranks a runner's live sessions, and a pause-all writes only a
/// session's own state, so the runner's state is judged from its
/// <em>sources</em> - the heartbeat row and each session row - not from any
/// one column. Each source is judged in the order the page judges a row
/// (<c>factsActivity</c> in <c>lib/runners.ts</c>): gone, then agent-down, then
/// exhausted, then paused, then stopping, then running. An hourly token
/// throttle reads as running, since it is not one of the six words.</para>
///
/// <para>The runner takes its highest-ranked source. Paused and stopping tie,
/// and the source heard from more recently wins. A source that is gone counts
/// only when every source is: a runner with a live session is not gone because
/// an old row beside it is.</para>
/// </remarks>
public static class RunnerStateRule
{
    public const string Running = RunnerStates.Running;
    public const string Paused = RunnerStates.Paused;
    public const string Stopping = RunnerStates.Stopping;
    public const string Exhausted = "exhausted";
    public const string AgentDown = "agent-down";
    public const string Gone = "gone";

    /// <summary>The six words, in the order the page decides them.</summary>
    public static readonly string[] All = [Gone, AgentDown, Exhausted, Paused, Stopping, Running];

    /// <summary>
    /// One row that speaks for a runner: the heartbeat row, or one session.
    /// Only the facts the judgement reads, so a test builds one in memory.
    /// </summary>
    /// <param name="LastSeenAt">The last time the row was heard from.</param>
    /// <param name="EndedAt">When a session ended, or null: a heartbeat row never has one.</param>
    /// <param name="Retired">A heartbeat row a newer one superseded: gone from its last beat.</param>
    /// <param name="State">What a person asked of the row: running, paused or stopping.</param>
    /// <param name="ExhaustedUntil">When the account was due back, or null.</param>
    /// <param name="AgentDownUntil">When the agent was due back, or null.</param>
    /// <param name="AgentDownKind">Why it was down, read only while the stretch lasts.</param>
    public sealed record Source(
        DateTimeOffset LastSeenAt,
        DateTimeOffset? EndedAt,
        bool Retired,
        string State,
        DateTimeOffset? ExhaustedUntil,
        DateTimeOffset? AgentDownUntil,
        string? AgentDownKind);

    /// <summary>A state, with the kind of agent-down that earned it (null for every other).</summary>
    public readonly record struct Answer(string State, string? Kind);

    /// <summary>The state a runner came to be in at an instant, from the clock alone.</summary>
    public sealed record Change(DateTimeOffset At, string State, string? Kind);

    /// <summary>
    /// The state at <paramref name="t"/>. No sources at all reads gone - the
    /// caller decides whether a runner with no rows has a state to write.
    /// </summary>
    public static Answer At(IReadOnlyList<Source> sources, TimeSpan goneAfter, DateTimeOffset t)
    {
        Answer? best = null;
        var bestRank = -1;
        DateTimeOffset bestSeen = default;

        foreach (var source in sources)
        {
            var judged = Judge(source, goneAfter, t);
            var rank = Rank(judged.State);

            var wins = rank > bestRank
                || (rank == bestRank && rank == Rank(Paused) && source.LastSeenAt > bestSeen);
            if (!wins) continue;

            best = judged;
            bestRank = rank;
            bestSeen = source.LastSeenAt;
        }

        return best ?? new Answer(Gone, null);
    }

    /// <summary>
    /// Every instant in (<paramref name="after"/>, <paramref name="upTo"/>] at
    /// which the answer changes by the clock alone - a source going quiet past
    /// the limit, or a stretch of exhausted or agent-down running out - with
    /// the state it changed to. The facts are held as they stand: nothing is
    /// assumed about what a beat might have written in between.
    /// </summary>
    public static IReadOnlyList<Change> ClockChanges(
        IReadOnlyList<Source> sources, TimeSpan goneAfter, DateTimeOffset after, DateTimeOffset upTo)
    {
        var instants = new SortedSet<DateTimeOffset>();
        foreach (var source in sources)
        {
            instants.Add(GoneFrom(source, goneAfter));
            if (source.ExhaustedUntil is { } exhausted) instants.Add(exhausted);
            if (source.AgentDownUntil is { } down) instants.Add(down);
        }

        var changes = new List<Change>();
        var before = At(sources, goneAfter, after);

        foreach (var instant in instants)
        {
            if (instant <= after || instant > upTo) continue;

            var now = At(sources, goneAfter, instant);
            if (now.State == before.State) continue;

            changes.Add(new Change(instant, now.State, now.Kind));
            before = now;
        }

        return changes;
    }

    /// <summary>
    /// A runner's whole timeline: its recorded rows, then the clock's tail,
    /// each folded so that adjacent changes into the same state and kind are
    /// one. Two beats that raced write the same row twice, and the tail judges
    /// from the facts as they stand, so it can repeat the last state when a
    /// beat overwrote them after that row - hence the second fold. The states
    /// route and the lanes on the board health read both take their timeline
    /// from here, so they cannot disagree.
    /// </summary>
    /// <param name="rows">The recorded rows, oldest first.</param>
    /// <param name="tail">What the clock has changed since the last row, oldest first.</param>
    public static List<Change> Timeline(IReadOnlyList<Change> rows, IReadOnlyList<Change> tail)
    {
        var folded = Fold(rows);
        if (rows.Count == 0) return folded;

        folded.AddRange(tail);
        return Fold(folded);
    }

    private static List<Change> Fold(IReadOnlyList<Change> changes)
    {
        var folded = new List<Change>(changes.Count);
        foreach (var change in changes)
        {
            if (folded.Count > 0 && folded[^1].State == change.State && folded[^1].Kind == change.Kind) continue;
            folded.Add(change);
        }

        return folded;
    }

    /// <summary>
    /// The instant a source is gone from. A live session or heartbeat row is
    /// gone one limit after it was last heard from. A session that ended is
    /// gone from the earlier of that and its end, so a loop that ended its own
    /// session is gone at once and a swept one at the limit. A retired heartbeat
    /// row is gone from its last beat.
    /// </summary>
    public static DateTimeOffset GoneFrom(Source source, TimeSpan goneAfter)
    {
        if (source.Retired) return source.LastSeenAt;

        var silent = source.LastSeenAt + goneAfter;
        return source.EndedAt is { } ended && ended < silent ? ended : silent;
    }

    /// <summary>
    /// The instant a runner with no live source became gone: the latest of its
    /// sources' own. A write that changes the facts (a retirement) cannot be
    /// settled beforehand, so this is where the record puts the gone row.
    /// </summary>
    public static DateTimeOffset? GoneAt(IReadOnlyList<Source> sources, TimeSpan goneAfter) =>
        sources.Count == 0 ? null : sources.Max(s => GoneFrom(s, goneAfter));

    private static Answer Judge(Source source, TimeSpan goneAfter, DateTimeOffset t)
    {
        if (t >= GoneFrom(source, goneAfter)) return new Answer(Gone, null);
        if (source.AgentDownUntil is { } down && down > t) return new Answer(AgentDown, source.AgentDownKind);
        if (source.ExhaustedUntil is { } exhausted && exhausted > t) return new Answer(Exhausted, null);

        return source.State switch
        {
            Paused => new Answer(Paused, null),
            Stopping => new Answer(Stopping, null),
            _ => new Answer(Running, null),
        };
    }

    /// <summary>Gone is the lowest: it counts only when nothing else is live. Paused and stopping tie.</summary>
    private static int Rank(string state) => state switch
    {
        AgentDown => 5,
        Exhausted => 4,
        Paused or Stopping => 3,
        Running => 2,
        _ => 1,
    };
}

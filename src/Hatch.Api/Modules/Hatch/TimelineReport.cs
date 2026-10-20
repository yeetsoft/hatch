using System.Text.Json;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// When each issue was in a column work is done in: the status history, folded
/// into the stretches an issue spent in an active column.
///
/// Pure in the way <see cref="Rollup"/> and <see cref="WorkLogRollup"/> are -
/// rows and changes in, shapes out, with no database, no clock and no DTOs - so
/// the rules can be pinned by tests that build a board in memory. This half is
/// per issue: <see cref="ParseChange"/> reads one status-change event,
/// <see cref="IndexOf"/> works the board out once, and <see cref="SegmentsOf"/>
/// turns one issue's changes into its active segments.
///
/// <para>"Active" is measured off the board rather than named: every column
/// after the first that is neither terminal nor deferred. The first column is
/// where work waits, a terminal column is where it ended, and a deferred column
/// is a siding, so a stretch in any of the three is a gap in the drawing.</para>
/// </summary>
public static class TimelineReport
{
    /// <summary>One issue, as the fold needs it.</summary>
    public sealed record Row(
        long Id, long? ParentId, int ProjectId, string ProjectKey, int Number, string Type,
        int StatusId, long Rank, DateTimeOffset CreatedAt, string CreatedBy);

    /// <summary>
    /// One status-change event. Names and ids are both kept: the id survives a
    /// rename and the name is all a pre-id event has.
    /// </summary>
    public sealed record Change(
        long IssueId, long EventId, DateTimeOffset At, string? From, string? To, int? FromId, int? ToId);

    /// <summary>
    /// A stretch in one active column. <see cref="To"/> is null while it is
    /// still going on at the end of the range.
    /// </summary>
    public sealed record Segment(int StatusId, DateTimeOffset From, DateTimeOffset? To);

    /// <summary>One line of the drawing: an issue and its segments, or a group of lines.</summary>
    public sealed record Node(
        long? IssueId, string CreatedBy, bool IsLeaf, IReadOnlyList<Segment> Segments, IReadOnlyList<Node> Children);

    public sealed record Result(
        IReadOnlyList<Node> Epics, Node? Other, IReadOnlyList<string> Creators, int Unresolved);

    /// <summary>
    /// What the board's shape says, worked out once for everybody who asks:
    /// which columns draw, and which columns exist at all. Built only by
    /// <see cref="IndexOf"/>.
    /// </summary>
    public sealed class BoardIndex
    {
        internal HashSet<int> Active { get; }
        internal HashSet<int> Terminal { get; }
        internal HashSet<int> All { get; }
        internal Dictionary<string, int> ByName { get; }

        private BoardIndex(HashSet<int> active, HashSet<int> terminal, HashSet<int> all, Dictionary<string, int> byName)
        {
            Active = active;
            Terminal = terminal;
            All = all;
            ByName = byName;
        }

        internal static BoardIndex Of(IReadOnlyList<EfHatchStatus> statuses)
        {
            var ordered = statuses.OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToList();
            var board = Columns.Board(ordered);

            // The board is empty or one column long: nothing is ever drawn.
            var active = board.Skip(1).Where(s => !s.IsTerminal && !s.IsDeferred).Select(s => s.Id).ToHashSet();

            var terminal = board.Where(s => s.IsTerminal).Select(s => s.Id).ToHashSet();

            // Over every current status, deferred included: a deferred column is
            // a resolved column that draws nothing.
            var all = new HashSet<int>();
            var byName = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var status in ordered)
            {
                all.Add(status.Id);
                byName.TryAdd(status.Name, status.Id);
            }

            return new BoardIndex(active, terminal, all, byName);
        }
    }

    /// <summary>
    /// A status-change event's payload as a <see cref="Change"/>, or null when
    /// the event says nothing usable - it will not parse, is not an object, or
    /// names no column it moved to. A null <c>from</c> is legal: an issue with
    /// no previous column.
    /// </summary>
    public static Change? ParseChange(long issueId, long eventId, DateTimeOffset at, string? payload)
    {
        if (IssueEventPayload.Parse(payload) is not { ValueKind: JsonValueKind.Object } json) return null;

        var from = Name(json, "from");
        var to = Name(json, "to");
        var fromId = Id(json, "fromId");
        var toId = Id(json, "toId");
        if (to is null && toId is null) return null;

        return new Change(issueId, eventId, at, from, to, fromId, toId);
    }

    private static string? Name(JsonElement json, string key) =>
        json.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Id(JsonElement json, string key) =>
        json.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var id)
            ? id
            : null;

    public static BoardIndex IndexOf(IReadOnlyList<EfHatchStatus> statuses) => BoardIndex.Of(statuses);

    /// <summary>
    /// One column to stand in for a stretch: <paramref name="resolved"/> is false
    /// when the event names a column the board no longer has.
    /// </summary>
    internal sealed record Stretch(int? StatusId, bool Resolved, DateTimeOffset From, DateTimeOffset? To);

    /// <summary>
    /// One issue's stretches rebuilt from its raw history: a zero-length
    /// stretch dropped, and consecutive resolved stretches in the same column
    /// merged into one - plus how many of the stretches in <c>[from, to]</c>
    /// were in a column that could not be resolved, counted before the merge.
    /// </summary>
    /// <remarks>
    /// <paramref name="changes"/> is the whole of the issue's history and is
    /// deliberately not cut at <paramref name="to"/>: an issue whose first move
    /// is after the range was in that move's <c>from</c> column during it, not
    /// in its current one. The caller passes only this issue's changes.
    /// </remarks>
    internal static (List<Stretch> Stretches, int Unresolved) RebuildOf(
        Row row, IEnumerable<Change> changes, BoardIndex board, DateTimeOffset from, DateTimeOffset to)
    {
        var ordered = changes.OrderBy(c => c.At).ThenBy(c => c.EventId).ToList();

        // (a) a stretch that ends no later than it starts is dropped.
        var stretches = StretchesOf(row, ordered, board).Where(s => s.To is null || s.To > s.From).ToList();

        var unresolved = stretches.Count(s => !s.Resolved && Overlaps(s, from, to));

        // (b) consecutive resolved stretches in the same column are one. They
        // are contiguous by construction, so the previous kept stretch is the
        // neighbour; a deferred stretch between two active ones keeps them two.
        var merged = new List<Stretch>();
        foreach (var s in stretches)
        {
            if (merged.Count > 0 && merged[^1] is { Resolved: true } last && s.Resolved && last.StatusId == s.StatusId)
                merged[^1] = last with { To = s.To };
            else
                merged.Add(s);
        }

        return (merged, unresolved);
    }

    /// <summary>
    /// One issue's active segments inside <c>[from, to]</c>, and how many of its
    /// stretches in that range were in a column that could not be resolved.
    /// </summary>
    /// <remarks>
    /// <paramref name="changes"/> is the whole of the issue's history and is
    /// deliberately not cut at <paramref name="to"/>: an issue whose first move
    /// is after the range was in that move's <c>from</c> column during it, not
    /// in its current one. The caller passes only this issue's changes.
    /// </remarks>
    public static (IReadOnlyList<Segment> Segments, int Unresolved) SegmentsOf(
        Row row, IEnumerable<Change> changes, BoardIndex board, DateTimeOffset from, DateTimeOffset to)
    {
        var (merged, unresolved) = RebuildOf(row, changes, board, from, to);

        // (c) only active columns draw, (d) clipped to the range.
        var segments = new List<Segment>();
        foreach (var s in merged)
        {
            if (!s.Resolved || s.StatusId is not { } id || !board.Active.Contains(id)) continue;

            var start = s.From > from ? s.From : from;
            var end = s.To is { } e && e < to ? e : to;
            if (end <= start) continue;

            segments.Add(new Segment(id, start, s.To is { } stop && stop <= to ? end : null));
        }

        return (segments, unresolved);
    }

    internal static bool Overlaps(Stretch s, DateTimeOffset from, DateTimeOffset to)
    {
        var start = s.From > from ? s.From : from;
        var end = s.To is { } e && e < to ? e : to;
        return end > start;
    }

    internal static List<Stretch> StretchesOf(Row row, List<Change> ordered, BoardIndex board)
    {
        if (ordered.Count == 0)
        {
            var resolved = board.All.Contains(row.StatusId);
            return [new Stretch(row.StatusId, resolved, row.CreatedAt, null)];
        }

        var stretches = new List<Stretch>();

        var first = ordered[0];
        if (first.From is not null || first.FromId is not null)
        {
            var (id, resolved) = Resolve(first.From, first.FromId, board);
            stretches.Add(new Stretch(id, resolved, row.CreatedAt, first.At));
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            var (id, resolved) = Resolve(ordered[i].To, ordered[i].ToId, board);
            DateTimeOffset? end = i + 1 < ordered.Count ? ordered[i + 1].At : null;
            stretches.Add(new Stretch(id, resolved, ordered[i].At, end));
        }

        return stretches;
    }

    private const string EpicType = "epic";

    /// <summary>One top-level row's walk: its node when it made the answer, and its subtree's unresolved count.</summary>
    private sealed record Built(Row Root, Node? Node, int Unresolved);

    /// <summary>
    /// The tree half: every issue's segments, folded under the top-level epics
    /// and the loose roots above them. A top-level epic is an epic with no epic
    /// above it; a loose root is a parentless non-epic. Children ride with
    /// their root whatever the filters say - <paramref name="projectId"/> and
    /// <paramref name="createdBy"/> narrow only the roots.
    /// </summary>
    /// <remarks>
    /// The kept loose roots are the children of <see cref="Result.Other"/>, which
    /// is null when there are none. <see cref="Result.Creators"/> ignores
    /// <paramref name="createdBy"/> so a picker can still offer the people the
    /// filter is hiding.
    /// </remarks>
    public static Result Fold(
        IReadOnlyList<Row> rows, IReadOnlyList<Change> changes, IReadOnlyList<EfHatchStatus> statuses,
        DateTimeOffset from, DateTimeOffset to, int? projectId, string? createdBy)
    {
        var board = IndexOf(statuses);
        var byId = rows.ToDictionary(r => r.Id);
        var children = rows
            .Where(r => r.ParentId is not null)
            .OrderBy(r => r.Rank).ThenBy(r => r.Id)
            .GroupBy(r => r.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());
        var changesByIssue = changes.ToLookup(c => c.IssueId);

        bool IsTopLevelEpic(Row r) =>
            r.Type == EpicType && (r.ParentId is not { } p || !byId.TryGetValue(p, out var parent) || parent.Type != EpicType);

        var roots = rows
            .Where(r => (IsTopLevelEpic(r) || (r.Type != EpicType && r.ParentId is null))
                && (projectId is null || r.ProjectId == projectId))
            .OrderBy(r => r.Rank).ThenBy(r => r.Id)
            .ToList();

        // Shared across every root: an issue is drawn, and counted, once.
        var visited = new HashSet<long>();

        (Node Node, int Unresolved) Walk(Row row)
        {
            var (segments, unresolved) = SegmentsOf(row, changesByIssue[row.Id], board, from, to);

            var kept = new List<Node>();
            if (children.TryGetValue(row.Id, out var below))
            {
                foreach (var child in below)
                {
                    // A top-level epic is drawn as an epic, once, never under what it sits in.
                    if (child.Type == EpicType && row.Type != EpicType) continue;
                    if (!visited.Add(child.Id)) continue;

                    var (node, sub) = Walk(child);
                    unresolved += sub;
                    if (node.Segments.Count > 0 || node.Children.Count > 0) kept.Add(node);
                }
            }

            var self = new Node(row.Id, row.CreatedBy, !children.ContainsKey(row.Id), segments, kept);
            return (self, unresolved);
        }

        var built = new List<Built>();
        foreach (var root in roots)
        {
            visited.Add(root.Id);
            var (node, unresolved) = Walk(root);
            var inAnswer = root.Type == EpicType ? node.Segments.Count > 0 : node.Segments.Count > 0 || node.Children.Count > 0;
            built.Add(new Built(root, inAnswer ? node : null, unresolved));
        }

        var creators = built
            .Where(b => b.Node is not null)
            .Select(b => b.Root.CreatedBy)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var wanted = string.IsNullOrWhiteSpace(createdBy) ? null : createdBy.Trim();
        var narrowed = wanted is null
            ? built
            : built.Where(b => string.Equals(b.Root.CreatedBy, wanted, StringComparison.Ordinal)).ToList();

        var epics = narrowed
            .Where(b => b.Root.Type == EpicType && b.Node is not null)
            .OrderBy(b => b.Node!.Segments[0].From)
            .ThenBy(b => b.Root.ProjectKey, StringComparer.Ordinal)
            .ThenBy(b => b.Root.Number)
            .Select(b => b.Node!)
            .ToList();

        var loose = narrowed
            .Where(b => b.Root.Type != EpicType && b.Node is not null)
            .Select(b => b.Node!)
            .ToList();

        Node? other = null;
        if (loose.Count > 0)
        {
            var hull = HullOf(loose);
            other = new Node(IssueId: null, CreatedBy: "", IsLeaf: false, Segments: hull is null ? [] : [hull], Children: loose);
        }

        return new Result(epics, other, creators, narrowed.Sum(b => b.Unresolved));
    }

    /// <summary>
    /// One segment over every segment at any depth beneath <paramref name="roots"/>:
    /// from the earliest start, to the latest end or to nothing when any is
    /// open, in the column of the earliest-starting segment (the first met in
    /// tree order on a tie). Null when there is no segment at all.
    /// </summary>
    private static Segment? HullOf(IReadOnlyList<Node> roots)
    {
        Segment? first = null;
        DateTimeOffset? latest = null;
        var open = false;

        void Visit(Node node)
        {
            foreach (var segment in node.Segments)
            {
                if (first is null || segment.From < first.From) first = segment;
                if (segment.To is not { } end) open = true;
                else if (latest is null || end > latest) latest = end;
            }

            foreach (var child in node.Children) Visit(child);
        }

        foreach (var root in roots) Visit(root);

        return first is null ? null : new Segment(first.StatusId, first.From, open ? null : latest);
    }

    /// <summary>
    /// An id, when the event carries one, must name a current status - the name
    /// is not tried, since a deleted column's name may have been reused. With no
    /// id the name is matched exactly.
    /// </summary>
    internal static (int? StatusId, bool Resolved) Resolve(string? name, int? id, BoardIndex board)
    {
        if (id is { } given) return board.All.Contains(given) ? (given, true) : (null, false);
        if (name is not null && board.ByName.TryGetValue(name, out var byName)) return (byName, true);
        return (null, false);
    }
}

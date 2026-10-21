namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// How many issues sat in each board column at each instant - the cumulative
/// flow half of what <see cref="TimelineReport"/> already rebuilds per issue,
/// swept once across every issue's stretches in time order rather than asking
/// at every grid point whether each issue was there.
///
/// Pure in the way <see cref="TimelineReport"/> is: rows and changes in, a
/// shape out, with no database, no clock and no DTOs, so the rules can be
/// pinned by tests that build a board and a history in memory.
///
/// <para>Membership is decided against <see cref="Columns.Board"/>, not
/// <see cref="TimelineReport.BoardIndex.Active"/>: the Timeline skips the
/// first column and drops the terminal ones because it draws neither, but a
/// cumulative flow needs both - an issue waiting in the first column, or
/// closed in the last one, still sat somewhere.</para>
/// </summary>
public static class CumulativeFlowReport
{
    /// <summary>One instant on the grid, and how many issues sat in each column then.</summary>
    public sealed record Point(DateTimeOffset At, IReadOnlyList<int> Counts);

    public sealed record Result(
        DateTimeOffset From, DateTimeOffset To, string Step,
        IReadOnlyList<int> Columns, IReadOnlyList<Point> Points, int Unresolved);

    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);

    /// <summary>
    /// Counts per board column, swept from <paramref name="start"/> to
    /// <paramref name="to"/>.
    /// </summary>
    /// <remarks>
    /// <paramref name="to"/> is the range as resolved, not clamped to
    /// <paramref name="now"/>: the sweep itself stops no later than
    /// <paramref name="now"/>, since nothing is known of a column past it, but
    /// <see cref="Result.Unresolved"/> is measured against <paramref name="to"/>
    /// unclamped - the same bound <see cref="TimelineReport.Fold"/> is handed -
    /// so the two answer the same number over the same history and range.
    /// </remarks>
    public static Result Fold(
        IReadOnlyList<TimelineReport.Row> rows, IReadOnlyList<TimelineReport.Change> changes,
        IReadOnlyList<EfHatchStatus> statuses, DateTimeOffset start, DateTimeOffset to, DateTimeOffset now)
    {
        var ordered = statuses.OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToList();
        var columns = Columns.Board(ordered).Select(s => s.Id).ToList();
        var slot = new Dictionary<int, int>();
        for (var i = 0; i < columns.Count; i++) slot[columns[i]] = i;

        var end = to < now ? to : now;
        var step = StepOf(start, end);

        DateTimeOffset? first = rows.Count > 0 ? rows.Min(r => r.CreatedAt) : null;
        if (first is not { } firstCreatedAt || firstCreatedAt > end)
            return new Result(start, to, step, columns, [], 0);

        var from = RangeBounds.ClampStart(start, end, firstCreatedAt);
        if (end < from)
            return new Result(from, to, step, columns, [], 0);

        var board = TimelineReport.IndexOf(statuses);
        var changesByIssue = changes.ToLookup(c => c.IssueId);

        var unresolved = 0;
        var deltas = new List<(DateTimeOffset At, int Slot, int Delta)>();

        foreach (var row in rows)
        {
            var issueChanges = changesByIssue[row.Id].OrderBy(c => c.At).ThenBy(c => c.EventId).ToList();
            var stretches = TimelineReport.StretchesOf(row, issueChanges, board)
                .Where(s => s.To is null || s.To > s.From);

            foreach (var s in stretches)
            {
                if (!s.Resolved && TimelineReport.Overlaps(s, from, to)) unresolved++;
                if (!s.Resolved || s.StatusId is not { } id || !slot.TryGetValue(id, out var at)) continue;

                var opens = s.From > row.CreatedAt ? s.From : row.CreatedAt;
                if (board.Terminal.Contains(id) && opens < from) continue;

                deltas.Add((opens, at, +1));
                if (s.To is { } closes) deltas.Add((closes, at, -1));
            }
        }

        deltas.Sort((a, b) => a.At.CompareTo(b.At));

        var grid = GridOf(from, end, step);
        var counts = new int[columns.Count];
        var points = new List<Point>(grid.Count);
        var cursor = 0;
        foreach (var at in grid)
        {
            while (cursor < deltas.Count && deltas[cursor].At <= at)
            {
                counts[deltas[cursor].Slot] += deltas[cursor].Delta;
                cursor++;
            }

            points.Add(new Point(at, (int[])counts.Clone()));
        }

        return new Result(from, to, step, columns, points, unresolved);
    }

    /// <summary>
    /// The grid's bucket size, chosen from the span of <c>[from, end]</c>: the
    /// finest of hour, day or week whose point count fits
    /// <see cref="WorkLogController.MaxBuckets"/>, falling back to week - the
    /// widest bucket this draws - rather than refusing a range that outgrows
    /// even that.
    /// </summary>
    private static string StepOf(DateTimeOffset from, DateTimeOffset end)
    {
        var span = end - from;
        if (PointsOver(span, Hour) <= WorkLogController.MaxBuckets) return "hour";
        if (PointsOver(span, Day) <= WorkLogController.MaxBuckets) return "day";
        return "week";
    }

    private static int PointsOver(TimeSpan span, TimeSpan step) =>
        span <= TimeSpan.Zero ? 1 : (int)((span.Ticks + step.Ticks - 1) / step.Ticks) + 1;

    private static TimeSpan SpanOf(string step) => step switch
    {
        "hour" => Hour,
        "day" => Day,
        _ => Week,
    };

    /// <summary>
    /// Every grid point from <paramref name="from"/> to <paramref name="end"/>,
    /// <paramref name="step"/> apart, with <paramref name="end"/> appended when
    /// it does not already land on the grid.
    /// </summary>
    private static List<DateTimeOffset> GridOf(DateTimeOffset from, DateTimeOffset end, string step)
    {
        var span = SpanOf(step);
        var grid = new List<DateTimeOffset>();
        for (var at = from; at <= end; at += span) grid.Add(at);
        if (grid[^1] < end) grid.Add(end);
        return grid;
    }
}

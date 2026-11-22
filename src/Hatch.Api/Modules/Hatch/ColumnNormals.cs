using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// How long a card normally stands in each active column: the ninetieth
/// percentile of the stays that ended over the last thirty days, across every
/// project, counting only issues with no children at all.
///
/// The percentile is not written a second time here. <see cref="FlowReport.Fold"/>
/// already answers it per column (<c>Dwelled</c> and <c>P90DwellSeconds</c>:
/// nearest rank, the whole stay, a stay counts when it ends in the range), and
/// the board's normal is that answer, so the flow page and the board cannot
/// disagree about it.
///
/// A class rather than a static because it keeps its last answer: the read
/// walks every issue and every status change, and a board is refetched after
/// every action. Like <see cref="DoneHorizon"/>, it takes the instant it is
/// judging against and never reads a clock.
/// </summary>
public sealed class ColumnNormals
{
    /// <summary>One column's normal, in seconds, or null where it has too few stays to go on.</summary>
    public sealed record Normal(int StatusId, long? Seconds);

    /// <summary>The fewest finished stays a column needs before it has a normal.</summary>
    public const int MinimumStays = 10;

    /// <summary>How far back a stay may have ended and still count.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(30);

    /// <summary>How long an answer is reused, while the board's columns are unchanged.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Whether a card that entered <paramref name="statusId"/> at <paramref name="enteredAt"/>
    /// has stood past that column's normal at <paramref name="now"/>: the column has a
    /// normal, and the time in it is strictly greater. The board's aged flag and the
    /// board health's past-normal count both read this, so neither can disagree.
    /// </summary>
    public static bool PastNormal(
        IReadOnlyDictionary<int, long> normalOf, int statusId, DateTimeOffset enteredAt, DateTimeOffset now) =>
        normalOf.TryGetValue(statusId, out var seconds) && (now - enteredAt).TotalSeconds > seconds;

    /// <summary>The two lists <see cref="Fold"/> reads, as <see cref="ReadAsync"/> returns them.</summary>
    private sealed record Rows(List<TimelineReport.Row> Issues, List<FlowReport.Event> Moves);

    private readonly SemaphoreSlim _one = new(1, 1);
    private IReadOnlyList<Normal>? _answer;
    private DateTimeOffset _madeAt;
    private string? _fingerprint;

    /// <summary>
    /// One entry for every active column, in board order. A column with fewer
    /// than <see cref="MinimumStays"/> stays over the window has a null
    /// <see cref="Normal.Seconds"/>.
    /// </summary>
    public static IReadOnlyList<Normal> Fold(
        IReadOnlyList<TimelineReport.Row> rows, IReadOnlyList<FlowReport.Event> events,
        IReadOnlyList<EfHatchStatus> statuses, DateTimeOffset now)
    {
        // A parent's own stay is its children's work over again, and says nothing
        // about how long a piece of work normally waits.
        var parents = rows.Where(r => r.ParentId is not null).Select(r => r.ParentId!.Value).ToHashSet();
        var kept = rows.Where(r => !parents.Contains(r.Id)).ToList();

        var flow = FlowReport.Fold(kept, events, [], [], statuses, now - Window, now, null, null);
        var byColumn = flow.Columns.ToDictionary(c => c.StatusId);

        var active = TimelineReport.IndexOf(statuses).Active;
        var ordered = statuses.OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToList();

        return Columns.Board(ordered)
            .Where(s => active.Contains(s.Id))
            .Select(s => byColumn.TryGetValue(s.Id, out var c) && c.Dwelled >= MinimumStays
                ? new Normal(s.Id, c.P90DwellSeconds)
                : new Normal(s.Id, null))
            .ToList();
    }

    /// <summary>
    /// The normals as of <paramref name="now"/>: the last answer while the
    /// board's columns are as they were and under five minutes have passed,
    /// otherwise a fresh read. Concurrent callers wait for one read. Nothing is
    /// kept if the read throws or is cancelled.
    /// </summary>
    public async Task<IReadOnlyList<Normal>> ForAsync(
        HatchContext db, List<EfHatchStatus> statuses, DateTimeOffset now, CancellationToken ct)
    {
        var fingerprint = Fingerprint(statuses);

        await _one.WaitAsync(ct);
        try
        {
            if (_answer is { } held && _fingerprint == fingerprint && now - _madeAt < Lifetime)
                return held;

            var read = await ReadAsync(db, ct);
            var answer = Fold(read.Issues, read.Moves, statuses, now);

            _answer = answer;
            _madeAt = now;
            _fingerprint = fingerprint;
            return answer;
        }
        finally
        {
            _one.Release();
        }
    }

    /// <summary>
    /// What a normal depends on besides the history: every column's id, place
    /// and flags. A flag changed by an operator recomputes at once.
    /// </summary>
    private static string Fingerprint(List<EfHatchStatus> statuses) =>
        string.Join(";", statuses
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
            .Select(s => $"{s.Id},{s.SortOrder},{s.IsTerminal},{s.IsDeferred}"));

    private static async Task<Rows> ReadAsync(HatchContext db, CancellationToken ct)
    {
        var issues = (await db.Issues.IgnoreQueryFilters().AsNoTracking()
                .Select(i => new
                {
                    i.Id,
                    i.ParentId,
                    i.ProjectId,
                    ProjectKey = i.Project!.Key,
                    i.Number,
                    i.Type,
                    i.StatusId,
                    i.Rank,
                    i.CreatedAt,
                    i.CreatedBy,
                })
                .ToListAsync(ct))
            .Select(i => new TimelineReport.Row(
                i.Id, i.ParentId, i.ProjectId, i.ProjectKey, i.Number, i.Type,
                i.StatusId, i.Rank, i.CreatedAt, i.CreatedBy))
            .ToList();

        var moves = (await db.IssueEvents.IgnoreQueryFilters().AsNoTracking()
                .Where(e => e.Kind == EfHatchIssueEvent.StatusChanged)
                .Select(e => new { e.IssueId, e.Id, e.Kind, e.At, e.Payload })
                .ToListAsync(ct))
            .OrderBy(e => e.IssueId)
            .ThenBy(e => e.At)
            .ThenBy(e => e.Id)
            .Select(e => new FlowReport.Event(e.IssueId, e.Id, e.Kind, e.At, e.Payload))
            .ToList();

        return new Rows(issues, moves);
    }
}

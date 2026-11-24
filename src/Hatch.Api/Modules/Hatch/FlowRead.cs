using Hatch.Api.Ef;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The flow report read for its window alone: the rows <see cref="FlowReport.Fold"/>
/// needs to answer what it would from every move, release, session and question
/// there is, and nothing older. <see cref="ReportsController.GetFlow"/> and the
/// tests that hold the two answers equal both go through here.
/// </summary>
/// <remarks>
/// <para>The moves are <see cref="BoardHealthTrail.ReadAsync"/> with a closed window
/// and the whole of the instant before it: each issue's moves in the window, the
/// newest one before it, and its first one after when it has none by the end. That
/// is enough to count what entered, left, hopped and stands, and to measure a stay
/// that began inside the window. It is not always enough to measure one that began
/// before it. <see cref="TimelineReport.RebuildOf"/> makes a stretch from the
/// issue's creation up to the first move it is given, in the column that move
/// came from, and merges neighbours in the same column. When the cut's first move
/// is an <c>X to X</c> move, or follows a stay of no length, that invented stretch
/// merges with the stay and starts it at the creation rather than where it began.
/// The walk back reads the earlier moves of only those issues, one instant at a
/// time, until something that is not the same stay precedes it. Which column a
/// move names stays <see cref="TimelineReport.Resolve"/>'s, reached through
/// <see cref="TimelineReport.RebuildOf"/>; nothing here decides it.</para>
/// <para>The window's other counts are the database's: dropped claims,
/// sessions and session errors come back as counts, not as a row each.</para>
/// </remarks>
internal static class FlowRead
{
    /// <summary>The same test as <see cref="FlowReport.IsDropped"/>, as the containment Postgres can run on a payload.</summary>
    private const string DroppedJson = "{\"outcome\":\"" + ClaimOutcomes.Dropped + "\"}";

    /// <summary>
    /// What <see cref="FlowReport.Fold"/> answers for <c>[start, end)</c> over the
    /// issues the filters keep, from the rows that window needs.
    /// </summary>
    /// <param name="rows">Every issue, as <see cref="FlowReport.Fold"/> is handed them.</param>
    /// <param name="start">The start of the window, already clamped as the route clamps it.</param>
    internal static async Task<FlowReport.Result> ReadAsync(
        HatchContext db, IReadOnlyList<TimelineReport.Row> rows, IReadOnlyList<EfHatchStatus> statuses,
        DateTimeOffset start, DateTimeOffset end, IReadOnlyList<int>? projectIds, string? type, CancellationToken ct)
    {
        var kept = FlowReport.Kept(rows, projectIds, type).ToList();
        var narrowed = (projectIds is not null && projectIds.Count > 0) || type is not null;
        var issueIds = narrowed ? kept.Select(r => r.Id).ToList() : null;

        var moves = await BoardHealthTrail.ReadAsync(
            db, [EfHatchIssueEvent.StatusChanged], start, null, issueIds, ct, until: end, wholeInstant: true);
        moves.AddRange(await WalkBackAsync(db, kept, moves, statuses, start, end, ct));

        var events = moves
            .OrderBy(e => e.IssueId).ThenBy(e => e.At).ThenBy(e => e.Id)
            .Select(e => new FlowReport.Event(e.IssueId, e.Id, e.Kind, e.At, e.Payload))
            .ToList();

        var questions = await QuestionTrail.ReadAsync(db, start, end, ct, laterAnswerBeatsLapse: true);

        // No claim events and no sessions go in: those three counts are set below.
        var result = FlowReport.Fold(rows, events, questions, [], statuses, start, end, projectIds, type);

        var dropped = await CountDroppedAsync(db, start, end, projectIds, type, ct);
        var (sessions, errors) = await CountSessionsAsync(db, start, end, projectIds, type, ct);

        return result with { Window = result.Window with { Dropped = dropped, Sessions = sessions, SessionErrors = errors } };
    }

    /// <summary>
    /// The earlier moves of the issues whose stay that leaves inside the window
    /// may have been measured from too early a start - see the class remarks.
    /// An issue is looked at when the cut holds a move before the window. Its
    /// stay open at the window's start is the one in question when it is the
    /// first stay, is in a column the board has, and ends inside the window; the
    /// read goes back one instant at a time while that holds. An issue where
    /// something precedes the stay needs nothing, which is every issue on a board
    /// that does not write moves from a column into itself.
    /// </summary>
    private static async Task<List<BoardHealthTrail.Ev>> WalkBackAsync(
        HatchContext db, List<TimelineReport.Row> kept, List<BoardHealthTrail.Ev> moves,
        IReadOnlyList<EfHatchStatus> statuses, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var board = TimelineReport.IndexOf(statuses);
        var rowOf = kept.ToDictionary(r => r.Id);
        var added = new List<BoardHealthTrail.Ev>();

        foreach (var trail in moves.GroupBy(e => e.IssueId))
        {
            if (!rowOf.TryGetValue(trail.Key, out var row) || !trail.Any(e => e.At < start)) continue;

            var cut = trail.ToList();
            var earliest = cut.Min(e => e.At);

            while (true)
            {
                var changes = cut
                    .Select(e => TimelineReport.ParseChange(e.IssueId, e.Id, e.At, e.Payload))
                    .OfType<TimelineReport.Change>();
                var (stretches, _) = TimelineReport.RebuildOf(row, changes, board, start, end);

                var open = stretches.FindIndex(s => s.From < start && (s.To is null || s.To >= start));
                if (open != 0 || !stretches[0].Resolved || stretches[0].To is not { } leaves || leaves >= end) break;

                var earlier = await BoardHealthTrail.EarlierAsync(db, EfHatchIssueEvent.StatusChanged, trail.Key, earliest, ct);
                if (earlier.Count == 0) break;

                cut.AddRange(earlier);
                added.AddRange(earlier);
                earliest = earlier[0].At;
            }
        }

        return added;
    }

    /// <summary>The <c>claim_released</c> events in the window that dropped the claim, over the kept issues.</summary>
    private static async Task<int> CountDroppedAsync(
        HatchContext db, DateTimeOffset start, DateTimeOffset end, IReadOnlyList<int>? projectIds, string? type, CancellationToken ct)
    {
        var released = db.IssueEvents.AsNoTracking()
            .Where(e => e.Kind == EfHatchIssueEvent.ClaimReleased && e.At >= start && e.At < end);

        if (projectIds is { Count: > 0 })
        {
            var ids = projectIds.ToList();
            released = released.Where(e => ids.Contains(e.Issue!.ProjectId));
        }

        if (type is not null) released = released.Where(e => e.Issue!.Type == type);

        if (db.Database.IsNpgsql())
            return await released.CountAsync(e => EF.Functions.JsonContains(e.Payload!, DroppedJson), ct);

        // Substring prefilter, then the fold's own test: InMemory has no containment.
        var payloads = await released
            .Where(e => e.Payload != null && e.Payload.Contains("outcome"))
            .Select(e => e.Payload)
            .ToListAsync(ct);
        return payloads.Count(FlowReport.IsDropped);
    }

    /// <summary>Sessions that ended in the window over the kept issues, and how many of them were errors: two rows at most.</summary>
    private static async Task<(int Sessions, int Errors)> CountSessionsAsync(
        HatchContext db, DateTimeOffset start, DateTimeOffset end, IReadOnlyList<int>? projectIds, string? type, CancellationToken ct)
    {
        var sessions = db.WorkLog.AsNoTracking().Where(w => w.EndedAt >= start && w.EndedAt < end);

        if (projectIds is { Count: > 0 })
        {
            var ids = projectIds.ToList();
            sessions = sessions.Where(w => ids.Contains(w.Issue!.ProjectId));
        }

        if (type is not null) sessions = sessions.Where(w => w.Issue!.Type == type);

        var counts = await sessions
            .GroupBy(w => w.IsError)
            .Select(g => new { IsError = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return (counts.Sum(c => c.Count), counts.Where(c => c.IsError).Sum(c => c.Count));
    }
}

using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// An ambition's changes, read rather than stored - see docs/hatch.md,
/// "ChangeOutcome". One piece of code, so the dispatcher story and
/// <see cref="IssueChangesController"/> read a change the same way.
/// </summary>
/// <remarks>
/// A change is every <see cref="EfHatchIssueEvent.CapabilityAdded"/>,
/// <see cref="EfHatchIssueEvent.CapabilityEdited"/> and
/// <see cref="EfHatchIssueEvent.CapabilityRemoved"/> event on one ambition
/// that falls strictly after the previous change's newest event and up to
/// and including the next one. <see cref="EfHatchIssueEvent.CapabilityMoved"/>
/// is never part of one. Each distinct <c>ChangeEventId</c> a <c>worked</c>
/// work log row names, for this ambition, is a boundary that closes
/// everything beneath it - nothing here checks that the id it names is
/// really one of this ambition's events; a boundary that names a stray id
/// still closes what came before it. Events after the last boundary, if any,
/// are the open change.
/// </remarks>
public static class AmbitionChanges
{
    /// <summary>The three kinds a change is made of - shared with <see cref="IssueChangesController"/>'s own validation.</summary>
    internal static readonly string[] Kinds =
    [
        EfHatchIssueEvent.CapabilityAdded,
        EfHatchIssueEvent.CapabilityEdited,
        EfHatchIssueEvent.CapabilityRemoved,
    ];

    /// <summary>One change: the events it carries and whether it has been worked out.</summary>
    /// <param name="Id">The id of the change's newest event.</param>
    /// <param name="After">The id of the event that closed the previous change, or 0 before the first.</param>
    /// <param name="Upto">The id of the event that closed this change, or <see cref="long.MaxValue"/> while it is open.</param>
    public sealed record AmbitionChange(
        long Id, long After, long Upto, DateTimeOffset SavedAt,
        IReadOnlyList<EfHatchIssueEvent> Events, DateTimeOffset? WorkedOutAt);

    /// <summary>
    /// Each of <paramref name="ambitionIds"/>'s changes, newest first. An
    /// ambition never changed is absent rather than an empty list, so a
    /// caller that only wants the ones with something need not check twice.
    /// </summary>
    public static async Task<Dictionary<long, List<AmbitionChange>>> ReadAsync(
        HatchContext db, IReadOnlyCollection<long> ambitionIds, CancellationToken ct)
    {
        var result = new Dictionary<long, List<AmbitionChange>>();
        if (ambitionIds.Count == 0) return result;

        var events = await db.IssueEvents.AsNoTracking()
            .Where(e => ambitionIds.Contains(e.IssueId) && Kinds.Contains(e.Kind))
            .OrderBy(e => e.Id)
            .ToListAsync(ct);
        if (events.Count == 0) return result;

        var boundaries = await db.WorkLog.AsNoTracking()
            .Where(w => ambitionIds.Contains(w.IssueId) && w.Outcome == ClaimOutcomes.Worked && w.ChangeEventId != null)
            .Select(w => new { w.IssueId, ChangeEventId = w.ChangeEventId!.Value, w.EndedAt })
            .ToListAsync(ct);

        foreach (var group in events.GroupBy(e => e.IssueId))
        {
            var own = group.ToList();
            var ownBoundaries = boundaries
                .Where(b => b.IssueId == group.Key)
                .GroupBy(b => b.ChangeEventId)
                .Select(b => (ChangeEventId: b.Key, WorkedOutAt: b.Min(x => x.EndedAt)))
                .OrderBy(b => b.ChangeEventId)
                .ToList();

            var changes = new List<AmbitionChange>();
            var after = 0L;

            foreach (var boundary in ownBoundaries)
            {
                var closed = own.Where(e => e.Id > after && e.Id <= boundary.ChangeEventId).ToList();
                if (closed.Count > 0)
                    changes.Add(new AmbitionChange(
                        closed.Max(e => e.Id), after, boundary.ChangeEventId,
                        closed.Max(e => e.At), closed, boundary.WorkedOutAt));

                after = boundary.ChangeEventId;
            }

            var open = own.Where(e => e.Id > after).ToList();
            if (open.Count > 0)
                changes.Add(new AmbitionChange(open.Max(e => e.Id), after, long.MaxValue, open.Max(e => e.At), open, null));

            if (changes.Count == 0) continue;

            changes.Reverse();
            result[group.Key] = changes;
        }

        return result;
    }
}

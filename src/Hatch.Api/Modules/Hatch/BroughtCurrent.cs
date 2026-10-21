using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// When each issue last changed column, for deciding whether a
/// <see cref="EfHatchIssue.BroughtCurrentAt"/> mark is from this visit to the
/// merges column or an earlier one. A query over the trail rather than a clear
/// at every site that writes <c>StatusId</c>: a site that forgot would
/// silently resurrect a stale mark.
/// </summary>
public static class BroughtCurrent
{
    /// <summary>
    /// The time of each issue's latest <c>status_changed</c> event, in one
    /// query. An issue that never changed column has no entry.
    /// </summary>
    public static async Task<Dictionary<long, DateTimeOffset>> LatestStatusChangesAsync(
        HatchContext db, IReadOnlyList<long> issueIds, CancellationToken ct)
    {
        if (issueIds.Count == 0) return [];

        return await db.IssueEvents.AsNoTracking()
            .Where(e => issueIds.Contains(e.IssueId) && e.Kind == EfHatchIssueEvent.StatusChanged)
            .GroupBy(e => e.IssueId)
            .Select(g => new { IssueId = g.Key, At = g.Max(e => e.At) })
            .ToDictionaryAsync(g => g.IssueId, g => g.At, ct);
    }
}

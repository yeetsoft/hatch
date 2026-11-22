using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The change an ambition is aimed at, if one qualifies: the one rule the scan
/// and the work route share, so the queue never says a session will work out a
/// change that the claim then does not hand out.
/// </summary>
/// <remarks>
/// A change qualifies when it is the ambition's newest, it has not been worked
/// out, and at least one open epic beneath the ambition was filed before the
/// change's newest event - an epic filed after it was written with the change in
/// view, so there is nothing to weigh. An open epic is a direct child in a column
/// that is neither terminal nor deferred. A change is read by
/// <see cref="AmbitionChanges"/>, the code the changes read uses, so a
/// <c>worked</c> row naming it, a newer event, and a reorder that is not a change
/// at all are all decided there and not here.
/// </remarks>
public static class WorkOutChanges
{
    /// <summary>One epic the change is weighed against.</summary>
    public sealed record Epic(string Key, string Title, string Column, IReadOnlyList<int> Serves);

    /// <summary>The change an ambition is aimed at, with the epics to weigh it against.</summary>
    public sealed record Candidate(AmbitionChanges.AmbitionChange Change, IReadOnlyList<Epic> Epics)
    {
        /// <summary>The one sentence the queue shows: what the session will do, and to how many epics.</summary>
        public string Sentence() =>
            $"works out what a change does to {Epics.Count} {(Epics.Count == 1 ? "epic" : "epics")}";
    }

    /// <summary>
    /// For each of <paramref name="ambitionIds"/> that has one, the change it is
    /// aimed at, keyed by ambition id. An ambition with none is absent.
    /// </summary>
    public static async Task<Dictionary<long, Candidate>> ForAsync(
        HatchContext db, FamilyGate family, List<EfHatchStatus> statuses,
        IReadOnlyCollection<long> ambitionIds, CancellationToken ct)
    {
        var found = new Dictionary<long, Candidate>();
        if (ambitionIds.Count == 0) return found;

        var changes = await AmbitionChanges.ReadAsync(db, ambitionIds, ct);
        var newest = changes
            .Where(c => c.Value.Count > 0 && c.Value[0].WorkedOutAt is null)
            .ToDictionary(c => c.Key, c => c.Value[0]);
        if (newest.Count == 0) return found;

        var open = newest.Keys.ToDictionary(id => id, id => family.EpicOpenChildren(id, statuses));
        var epicIds = open.Values.SelectMany(ids => ids).Distinct().ToList();
        if (epicIds.Count == 0) return found;

        // Every project's issues, as the tree is read: an epic in a project the
        // caller cannot see still stands beneath the ambition.
        var epics = (await db.Issues.IgnoreQueryFilters().AsNoTracking()
                .Where(i => epicIds.Contains(i.Id) && i.Type == "epic")
                .Select(i => new
                {
                    i.Id, i.Number, i.Title, i.StatusId, i.CreatedAt, ProjectKey = i.Project!.Key,
                })
                .ToListAsync(ct))
            .ToDictionary(i => i.Id);

        var serves = (await db.CapabilityLinks.IgnoreQueryFilters().AsNoTracking()
                .Where(l => epicIds.Contains(l.IssueId))
                .Select(l => new { l.IssueId, l.Capability!.Position })
                .ToListAsync(ct))
            .GroupBy(l => l.IssueId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)g.Select(l => l.Position).Order().ToList());

        foreach (var (ambitionId, change) in newest)
        {
            var weighed = open[ambitionId]
                .Where(id => epics.TryGetValue(id, out var e) && e.CreatedAt < change.SavedAt)
                .Select(id => epics[id])
                .OrderBy(e => e.ProjectKey, StringComparer.Ordinal).ThenBy(e => e.Number)
                .Select(e => new Epic(
                    IssueKey.Format(e.ProjectKey, e.Number), e.Title,
                    statuses.First(s => s.Id == e.StatusId).Name,
                    serves.GetValueOrDefault(e.Id, [])))
                .ToList();
            if (weighed.Count > 0) found[ambitionId] = new Candidate(change, weighed);
        }

        return found;
    }

    /// <summary>The wire form of a candidate, as a dispatch carries it.</summary>
    public static WorkChangeDto ToDto(Candidate c) => new(
        c.Change.Id,
        c.Change.Events.Select(IssueChangesController.MapEntry).ToList(),
        c.Epics.Select(e => new WorkChangeEpicDto(e.Key, e.Title, e.Column, e.Serves)).ToList());
}

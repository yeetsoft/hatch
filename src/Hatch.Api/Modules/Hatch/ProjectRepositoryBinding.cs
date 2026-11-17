using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Which project a repository binds - the rule <see cref="TrunkBuildBugController.PostBug"/>
/// and <see cref="TrunkBuildController"/> both need and must not drift apart on: the project
/// that names the repository primary, lowest project id first - else the lowest-id project
/// that binds it at all.
/// </summary>
public static class ProjectRepositoryBinding
{
    /// <summary>The winner, by canonical, for every canonical in <paramref name="canonicals"/> that something binds - one query for the lot, so a caller resolving many rows takes no N+1.</summary>
    public static async Task<IReadOnlyDictionary<string, (int Id, string Key)>> WinnersAsync(
        HatchContext db, IReadOnlyCollection<string> canonicals, CancellationToken ct)
    {
        // Every project's bindings: the winner is the same project whoever asks,
        // or two callers would file the same outage's bug under different
        // projects. What leaves is an id and a key.
        var rows = await db.ProjectRepositories.IgnoreQueryFilters().AsNoTracking()
            .Where(r => canonicals.Contains(r.Canonical))
            .Select(r => new { r.Canonical, r.SortOrder, r.ProjectId, Key = r.Project!.Key })
            .ToListAsync(ct);

        return rows.GroupBy(r => r.Canonical).ToDictionary(g => g.Key, g =>
        {
            var winner = g.OrderBy(r => r.SortOrder == 0 ? 0 : 1).ThenBy(r => r.ProjectId).First();
            return (winner.ProjectId, winner.Key);
        });
    }

    /// <summary>The winner for one canonical, by the same rule - or null when nothing binds it.</summary>
    public static async Task<(int Id, string Key)?> WinnerAsync(HatchContext db, string canonical, CancellationToken ct)
    {
        var winners = await WinnersAsync(db, [canonical], ct);
        return winners.TryGetValue(canonical, out var winner) ? winner : null;
    }
}

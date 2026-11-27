using Hatch.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// What an ambition has cost, and whether that has reached its budget.
/// The one place the sum is made: the route that reads it, the refusal that
/// stops work on it and the Ambitions page all come through
/// <see cref="SpentAsync"/> - one ambition at a time by way of
/// <see cref="ReadAsync"/> - so they cannot come to mean different things.
/// </summary>
/// <remarks>
/// Spent is the summed cost of every work log row on the ambition and on
/// everything beneath it, at any depth and in any project, from the day the
/// ambition began - no date window. It is worked out on each read and held
/// nowhere. A session with no price adds nothing and is counted instead.
///
/// It does not use <see cref="WorkLogRollup.TotalsAsync"/>, and the reason is
/// scope: that fold reads through the caller's query filters, so a person who
/// cannot see one project holding an epic of the ambition would read a smaller
/// figure than a key or an admin does. A budget is a fact about the ambition,
/// not about who is asking, so both the issue walk and the work log read here
/// ignore the filters, as <see cref="ChangelogFiles.ProjectAsync"/> does. Only
/// totals leave; no row a caller could not read does.
/// </remarks>
public static class AmbitionBudget
{
    /// <summary>The most a budget may be: a ceiling on a typing mistake, not a policy.</summary>
    public const decimal MaxBudgetUsd = 1_000_000_000m;

    public static async Task<IssueBudgetDto> ReadAsync(HatchContext db, EfHatchIssue ambition, CancellationToken ct)
    {
        var sum = (await SpentAsync(db, [ambition.Id], ct))[ambition.Id];

        return Of(ambition.BudgetUsd, sum);
    }

    /// <summary>
    /// The budget as every reader tells it, from a budget and what <see cref="SpentAsync"/>
    /// summed: the Ambitions row and the ambition's own page both come through here.
    /// </summary>
    public static IssueBudgetDto Of(decimal? budget, (decimal Spent, int Unpriced) sum) =>
        new(budget, sum.Spent, sum.Unpriced, Spent: IsSpent(budget, sum.Spent));

    /// <summary>Whether this much spent has reached this budget. No budget is never reached.</summary>
    public static bool IsSpent(decimal? budget, decimal spent) => budget is { } most && spent >= most;

    /// <summary>
    /// What each of these ambitions has cost, and how many of its sessions have
    /// no price: one read of the issue tree and one of the work log, however
    /// many are asked about. Every id asked for has an entry, and asking about
    /// none reads nothing.
    /// </summary>
    public static async Task<Dictionary<long, (decimal Spent, int Unpriced)>> SpentAsync(
        HatchContext db, IReadOnlyCollection<long> ambitionIds, CancellationToken ct)
    {
        if (ambitionIds.Count == 0) return [];

        var tree = Rollup.ShapeOf(
            (await db.Issues.IgnoreQueryFilters().AsNoTracking()
                .OrderBy(i => i.Rank).ThenBy(i => i.Id)
                .Select(i => new { i.Id, i.ParentId, i.StatusId })
                .ToListAsync(ct))
            .Select(i => new Rollup.Node(i.Id, i.ParentId, i.StatusId))
            .ToList());

        var under = ambitionIds.Distinct().ToDictionary(id => id, id => tree.DescendantIds(id).Append(id).ToList());
        var ids = under.Values.SelectMany(v => v).Distinct().ToList();

        var costs = (await db.WorkLog.IgnoreQueryFilters().AsNoTracking()
                .Where(w => ids.Contains(w.IssueId))
                .Select(w => new { w.IssueId, w.CostUsd })
                .ToListAsync(ct))
            .ToLookup(w => w.IssueId, w => w.CostUsd);

        return under.ToDictionary(
            a => a.Key,
            a =>
            {
                var rows = a.Value.SelectMany(id => costs[id]).ToList();
                return (rows.Sum(c => c ?? 0m), rows.Count(c => c is null));
            });
    }
}

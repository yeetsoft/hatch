using Hatch.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// What an ambition has cost, and whether that has reached its budget.
/// The one place the sum is made: the route that reads it and the refusal that
/// later stops work on it both call <see cref="ReadAsync"/>, so the two cannot
/// come to mean different things.
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
        var tree = Rollup.ShapeOf(
            (await db.Issues.IgnoreQueryFilters().AsNoTracking()
                .OrderBy(i => i.Rank).ThenBy(i => i.Id)
                .Select(i => new { i.Id, i.ParentId, i.StatusId })
                .ToListAsync(ct))
            .Select(i => new Rollup.Node(i.Id, i.ParentId, i.StatusId))
            .ToList());

        var ids = tree.DescendantIds(ambition.Id).Append(ambition.Id).ToList();

        var costs = await db.WorkLog.IgnoreQueryFilters().AsNoTracking()
            .Where(w => ids.Contains(w.IssueId))
            .Select(w => w.CostUsd)
            .ToListAsync(ct);

        var spent = costs.Sum(c => c ?? 0m);
        var unpriced = costs.Count(c => c is null);

        return new IssueBudgetDto(
            ambition.BudgetUsd, spent, unpriced,
            Spent: ambition.BudgetUsd is { } budget && spent >= budget);
    }
}

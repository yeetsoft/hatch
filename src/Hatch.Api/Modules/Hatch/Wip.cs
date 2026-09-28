using Hatch.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Which columns are in the WIP section, and how much of one slice of the board
/// they may hold at once - the one place that says which columns count, the way
/// <see cref="Columns"/> is the one place that says which columns the board
/// draws.
///
/// <see cref="EfHatchStatus.IsWip"/> is the stored flag; a column keeps it
/// whatever else is true of it. This is where the flag is narrowed to the
/// columns that actually count - the ones that are neither deferred nor
/// terminal - so a flag stranded on a column that has since become either does
/// not silently hold the limit down.
/// </summary>
public static class Wip
{
    /// <summary>
    /// The stories-and-bugs slice, as both <c>WipController</c>'s read and the
    /// board (HA-88) will share it: the limit if one is held, the types it
    /// counts, and the flagged columns that are neither deferred nor terminal,
    /// in board order.
    /// </summary>
    public static async Task<WipSectionDto> SectionAsync(HatchContext db, CancellationToken ct)
    {
        var limit = await db.WipLimits.AsNoTracking()
            .Where(w => w.Types == EfHatchWipLimit.StoriesAndBugs)
            .Select(w => (int?)w.Limit)
            .SingleOrDefaultAsync(ct);

        var statusIds = await db.Statuses.AsNoTracking()
            .Where(s => s.IsWip && !s.IsDeferred && !s.IsTerminal)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
            .Select(s => s.Id)
            .ToListAsync(ct);

        return new WipSectionDto(limit, EfHatchPlaybook.SplitTypes(EfHatchWipLimit.StoriesAndBugs), statusIds);
    }
}

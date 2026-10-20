using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// What shipped, a window at a time: wires the database to
/// <see cref="Changelog"/>, which does all of the windowing, grouping and
/// ordering.
/// </summary>
[ApiController]
[Route("api/hatch/changelog")]
[RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
public class ChangelogController(HatchContext db, TimeProvider time) : ControllerBase
{
    /// <summary>The window when a caller resizes nothing: the newest fourteen days that shipped something.</summary>
    private const int DefaultDays = 14;

    [HttpGet]
    public async Task<ActionResult<ChangelogDto>> GetChangelog(
        [FromQuery] string? before, [FromQuery] int? projectId,
        [FromQuery] int days = DefaultDays, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();

        if (!RangeBounds.TryBound(before, now, out var beforeAt))
            return BadRequest(RangeBounds.NotAnInstant(before));

        if (days < 1)
            return BadRequest($"a window is at least one day - not {days}");

        if (projectId is { } wanted && !await db.Projects.AnyAsync(p => p.Id == wanted, ct))
            return BadRequest($"there is no project {wanted}");

        var closed = db.Issues.AsNoTracking()
            .Where(i => i.ParentId == null && i.Status!.IsTerminal && i.ClosedAt != null);

        if (projectId is { } project) closed = closed.Where(i => i.ProjectId == project);

        // One unfiltered-by-window projection, shared by the window boundary
        // below and the month counts - a second, bounded query only runs once
        // the window says there is anything inside it to fetch.
        var closes = await closed.Select(i => i.ClosedAt!.Value).ToListAsync(ct);

        var months = Changelog.Months(closes);
        var window = Changelog.ResolveWindow(closes, beforeAt, days);

        if (window.OldestDayStart is not { } oldestDayStart)
            return new ChangelogDto([], false, null, months);

        var rows = await closed
            .Where(i => i.ClosedAt >= oldestDayStart && i.ClosedAt < beforeAt)
            .Select(i => new
            {
                i.Id,
                ProjectKey = i.Project!.Key,
                i.Number,
                i.Type,
                i.Title,
                i.ReleaseNote,
                ClosedAt = i.ClosedAt!.Value,
            })
            .ToListAsync(ct);

        var tree = await Rollup.LoadAsync(db, ct);

        var entries = rows
            .Select(r => new Changelog.Entry(
                r.Id, IssueKey.Format(r.ProjectKey, r.Number), r.Title, r.Type, r.ProjectKey,
                r.ReleaseNote, r.ClosedAt, tree.DescendantCount(r.Id)))
            .ToList();

        return new ChangelogDto(Changelog.Days(entries), window.HasOlder, window.Oldest, months);
    }
}

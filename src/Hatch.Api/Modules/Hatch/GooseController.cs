using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The goose's record - see <see cref="GooseJob"/>. A run holds what was seen
/// across every project, so a caller whose scope is not everything is shown an
/// empty list and a run that reads as not found, rather than a run with some
/// of it hidden.
/// </summary>
[ApiController]
[Route("api/goose")]
[RequireRole(PersonRole.User, AcceptKey = true)]
public class GooseController(HatchContext db) : ControllerBase
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    /// <summary>Runs newest first, without the per-check detail.</summary>
    [HttpGet("runs")]
    public async Task<ActionResult<IReadOnlyList<GooseRunDto>>> ListRuns([FromQuery] int? limit, CancellationToken ct)
    {
        if (!db.ScopeUnbounded) return Ok(Array.Empty<GooseRunDto>());

        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

        var runs = await db.GooseRuns.AsNoTracking()
            .OrderByDescending(r => r.LastRanAt)
            .ThenByDescending(r => r.Id)
            .Take(take)
            .Select(r => new GooseRunDto(r.Id, r.FirstRanAt, r.LastRanAt, r.Sweeps, r.LongestMs))
            .ToListAsync(ct);

        return Ok(runs);
    }

    /// <summary>One run, with what each check looked at and concluded.</summary>
    [HttpGet("runs/{id:long}")]
    public async Task<ActionResult<GooseRunDetailDto>> GetRun(long id, CancellationToken ct)
    {
        if (!db.ScopeUnbounded) return NotFound();

        var run = await db.GooseRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (run is null) return NotFound();

        var entries = GooseEntry.Read(run.Entries)
            .Select(e => new GooseEntryDto(e.Check, e.Looked, e.Concluded, e.Failed, e.Error))
            .ToList();

        return Ok(new GooseRunDetailDto(run.Id, run.FirstRanAt, run.LastRanAt, run.Sweeps, run.LongestMs, entries));
    }
}

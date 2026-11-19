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

    /// <summary>
    /// Open findings first, longest-standing first, then closed ones, newest
    /// first. <paramref name="open"/> narrows it to the open ones. Scoped the
    /// same way <see cref="EfHatchGooseFinding"/>'s query filter reads it: a
    /// finding naming a project a caller cannot see is left out, not an
    /// all-or-nothing read the way the run routes above are.
    /// </summary>
    [HttpGet("findings")]
    public async Task<ActionResult<IReadOnlyList<GooseFindingDto>>> ListFindings([FromQuery] bool? open, CancellationToken ct)
    {
        var findings = db.GooseFindings.AsNoTracking().Include(f => f.Project).Include(f => f.BugIssue).ThenInclude(i => i!.Project);

        var rows = await (open == true ? findings.Where(f => f.ClosedAt == null) : findings)
            .ToListAsync(ct);

        var ordered = rows
            .OrderByDescending(f => f.ClosedAt == null)
            .ThenBy(f => f.ClosedAt == null ? f.BeganAt : DateTimeOffset.MaxValue)
            .ThenByDescending(f => f.ClosedAt ?? DateTimeOffset.MinValue)
            .Select(f => new GooseFindingDto(
                f.Id, f.Check, f.Subject, f.Project?.Key, f.Sentence,
                GooseFindingEvidence.Read(f.Evidence).Select(e => new GooseFindingEvidenceDto(e.Label, e.Value)).ToList(),
                f.BeganAt, f.OpenedAt, f.ClosedAt,
                f.BugIssue is { } bug ? IssueKey.Format(bug.Project!.Key, bug.Number) : null))
            .ToList();

        return Ok(ordered);
    }
}

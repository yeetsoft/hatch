using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// What a session proposed changing about a playbook, read back: the pending
/// queue a person works through, and the one behind any id, approved or
/// rejected included. Reading is open to an API key, the way <see cref="PlaybooksController"/>'s
/// own read is - a session that proposed a change may want to see where it
/// landed.
/// </summary>
/// <remarks>
/// Each action carries its own <see cref="RequireRoleAttribute"/> rather than
/// the class, because the submission route the next task adds takes the same
/// attribute and a later story adds a person-only verb beside it - the group
/// is not uniform the way <see cref="QuestionsController"/>'s is.
/// </remarks>
[ApiController]
[Route("api/hatch/playbook-proposals")]
public class PlaybookProposalsController(
    HatchContext db, ICallerIdentity caller, TimeProvider time, ModelFamilies? families = null) : ControllerBase
{
    /// <summary>
    /// The queue for one state, oldest first - the order a person works it in.
    /// </summary>
    /// <param name="state">
    /// Null or <c>"pending"</c> for the ones waiting on a person; <c>"approved"</c> or
    /// <c>"rejected"</c> for that one; <c>"all"</c> for every state.
    /// </param>
    [HttpGet]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public async Task<ActionResult<IReadOnlyList<PlaybookProposalDto>>> GetProposals([FromQuery] string? state, CancellationToken ct)
    {
        var effective = state ?? EfHatchPlaybookProposal.Pending;
        if (effective != "all" && !EfHatchPlaybookProposal.States.Contains(effective))
            return BadRequest($"\"{state}\" is not a state - pending, approved, rejected or all");

        var query = db.PlaybookProposals.AsNoTracking().Include(p => p.Changes).AsQueryable();
        if (effective != "all") query = query.Where(p => p.State == effective);

        var rows = await query.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).ToListAsync(ct);
        var dtos = await PlaybookProposalProjection.ToDtosAsync(db, rows, ct);
        return new ActionResult<IReadOnlyList<PlaybookProposalDto>>(dtos);
    }

    /// <summary>One proposal, with every change it carries.</summary>
    [HttpGet("{id:int}")]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public async Task<ActionResult<PlaybookProposalDto>> GetProposal(int id, CancellationToken ct)
    {
        var row = await db.PlaybookProposals.AsNoTracking().Include(p => p.Changes)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (row is null) return NotFound();

        return (await PlaybookProposalProjection.ToDtosAsync(db, [row], ct)).Single();
    }
}

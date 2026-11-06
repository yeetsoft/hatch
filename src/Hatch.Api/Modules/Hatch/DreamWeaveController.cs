using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Files the epic a weave press asks for - the one route under
/// <c>/dreams</c> that writes an issue rather than a dream. See
/// <see cref="DreamWeave"/> for the title and the brief it is filed with,
/// and <see cref="Dispatch.MatchWeave"/> for the column an epic is born in.
/// </summary>
/// <remarks>
/// No class-level <see cref="RequireRoleAttribute"/>, for
/// <see cref="ProjectsController"/>'s reason: the attribute is
/// <c>AllowMultiple = false</c>, so <see cref="DreamsController"/>'s own
/// class-level one - which accepts the Hatch scope - would silently
/// *replace* this action's tighter, person-only one rather than sit under
/// it. Decorating the one action explicitly is what keeps a key out.
/// </remarks>
[ApiController]
[Route("api/hatch/dreams/weave")]
public class DreamWeaveController(
    HatchContext db, TimeProvider time, ICallerIdentity caller,
    IActorDirectory actors, RankService ranks, IssueClaims claims) : ControllerBase
{
    [HttpPost]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<IssueDto>> PostWeave(DreamWeaveRequest request, CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } refusal) return refusal;

        var mode = request.Mode;
        if (mode is null || !DreamWeave.Modes.Contains(mode))
            return BadRequest($"a mode is one of seed, pressing, common, next - not \"{mode}\"");

        var seed = request.Seed?.Trim();
        if (mode == "seed")
        {
            if (string.IsNullOrWhiteSpace(seed)) return BadRequest("a seed is needed for mode seed");
        }
        else if (!string.IsNullOrWhiteSpace(seed))
        {
            return BadRequest("a seed is for mode seed");
        }

        if (request.ProjectId is not { } projectId)
            return BadRequest("a weave needs a project - an epic needs a home");

        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return NotFound($"there is no project {projectId}");

        var dreamIds = (request.DreamIds ?? []).Distinct().ToList();
        var known = (await db.Dreams.Where(d => dreamIds.Contains(d.Id)).Select(d => d.Id).ToListAsync(ct)).ToHashSet();
        foreach (var id in dreamIds)
        {
            if (!known.Contains(id)) return BadRequest($"there is no dream {id}");
        }

        var playbooks = await db.Playbooks.Include(p => p.FromStatus)
            .Where(p => p.ProjectId == null || p.ProjectId == projectId).ToListAsync(ct);
        var row = Dispatch.MatchWeave(playbooks, projectId);
        if (row is null)
            return BadRequest($"{project.Key} has no playbook row for an epic - add one on the Playbooks page first");

        var issues = new IssuesController(db, ranks, actors, claims, caller, time, new ProjectAccess(db, actors)) { ControllerContext = ControllerContext };
        var created = await issues.CreateIssueAsync(
            new IssueCreateRequest(projectId, "epic", DreamWeave.Title(mode, seed), DreamWeave.Describe(mode, seed, projectId, dreamIds), null, null, null),
            PriorityLevels.Normal, ct, statusId: row.FromStatusId);

        if (created.Result is not CreatedAtActionResult { Value: IssueDto dto }) return created.Result!;

        return Created($"/api/hatch/issues/{dto.Key}", dto);
    }

    /// <summary>
    /// A person, not a key - see the type's own remark. Weaving spends a
    /// breakdown session at the largest model, which is a person's call to
    /// press, not an agent's to make for itself. Checked here as well as
    /// declared in the attribute, for <see cref="ProjectsController"/>'s
    /// reason: a keyless runner that named itself with the runner header is
    /// refused too, whether or not it carries a credential.
    /// </summary>
    private async Task<ObjectResult?> NotAPerson(CancellationToken ct) =>
        await caller.IsProgramAsync(ct)
            ? new ObjectResult("weaving spends a breakdown session at the largest model - a person's to press, not an agent's")
            {
                StatusCode = StatusCodes.Status403Forbidden,
            }
            : null;
}

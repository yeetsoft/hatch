using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Files the draft ambition a shaping press asks for - words, chosen dreams
/// and uploaded files in one call. See <see cref="AmbitionShape"/> for the
/// title and the brief it is filed with, and
/// <see cref="Dispatch.MatchShapingPress"/> for the column it is born in.
/// </summary>
/// <remarks>
/// No class-level <see cref="RequireRoleAttribute"/>, for the reason
/// <see cref="DreamWeaveController"/> has none: the attribute is
/// <c>AllowMultiple = false</c>, so a class-level one that accepts a key
/// would replace this action's tighter, person-only one. Decorating the one
/// action is what keeps a key out.
/// </remarks>
[ApiController]
[Route("api/dreams/shape")]
public class DreamShapeController(
    HatchContext db, TimeProvider time, ICallerIdentity caller,
    IActorDirectory actors, RankService ranks, IssueClaims claims) : ControllerBase
{
    [HttpPost]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<IssueDto>> PostShape(DreamShapeRequest request, CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } refusal) return refusal;

        if (request.ProjectId is not { } projectId)
            return BadRequest("shaping an ambition needs a project - an ambition needs a home");

        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return NotFound($"there is no project {projectId}");

        var words = request.Words?.Trim();
        var dreamIds = (request.DreamIds ?? []).Distinct().ToList();
        var attachmentIds = (request.AttachmentIds ?? []).Distinct().ToList();
        if (string.IsNullOrEmpty(words) && dreamIds.Count == 0 && attachmentIds.Count == 0)
            return BadRequest("give words, a dream or a file to shape an ambition from");

        var knownDreams = (await db.Dreams.Where(d => dreamIds.Contains(d.Id)).Select(d => d.Id).ToListAsync(ct)).ToHashSet();
        foreach (var id in dreamIds)
        {
            if (!knownDreams.Contains(id)) return BadRequest($"there is no dream {id}");
        }

        // Never the bytes: a draft only links to the files.
        var found = await db.Attachments.AsNoTracking()
            .Where(a => attachmentIds.Contains(a.Id))
            .Select(a => new { a.Id, a.FileName, a.ContentType, a.ProjectId })
            .ToListAsync(ct);
        var files = new List<ShapeFile>();
        foreach (var id in attachmentIds)
        {
            var file = found.FirstOrDefault(a => a.Id == id);
            if (file is null) return BadRequest($"there is no file {id}");
            if (file.ProjectId is { } owner && owner != projectId) return BadRequest($"file {id} belongs to another project");
            files.Add(new ShapeFile(file.Id, file.FileName, file.ContentType));
        }

        var playbooks = await db.Playbooks.Where(p => p.ProjectId == null || p.ProjectId == projectId).ToListAsync(ct);
        var row = Dispatch.MatchShapingPress(playbooks, projectId);
        if (row is null)
            return BadRequest($"{project.Key} has no playbook row that shapes an ambition - add one on the Playbooks page first");

        var issues = new IssuesController(db, ranks, actors, claims, caller, time, new ProjectAccess(db, actors)) { ControllerContext = ControllerContext };
        var created = await issues.CreateIssueAsync(
            new IssueCreateRequest(projectId, "ambition", AmbitionShape.Title(words), AmbitionShape.Describe(words, files), null, null, null),
            PriorityLevels.Normal, ct, statusId: row.FromStatusId);

        if (created.Result is not CreatedAtActionResult { Value: IssueDto dto }) return created.Result!;

        if (dreamIds.Count > 0)
        {
            var recorded = await new IssueDreamsController(db, caller, time)
                .PutIssueDreams(dto.Key, new IssueDreamsWriteRequest(dreamIds), ct);
            // The ids were checked above, so this is a backstop.
            if (recorded.Result is not null and not OkObjectResult) return recorded.Result;
        }

        return Created($"/api/issues/{dto.Key}", dto);
    }

    /// <summary>
    /// A person, not a key - see the type's own remark. Checked here as well
    /// as declared in the attribute, for the reason
    /// <see cref="DreamWeaveController"/> does: a keyless runner that named
    /// itself with the runner header is refused too.
    /// </summary>
    private async Task<ObjectResult?> NotAPerson(CancellationToken ct) =>
        await caller.IsProgramAsync(ct)
            ? new ObjectResult("shaping an ambition is a person's to press, not an agent's")
            {
                StatusCode = StatusCodes.Status403Forbidden,
            }
            : null;
}

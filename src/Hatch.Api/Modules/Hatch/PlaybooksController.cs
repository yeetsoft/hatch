using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The matrix an agent is dispatched by: for a transition and a set of issue
/// types, what to say, which model to say it to, and how hard to think.
///
/// The split down the middle of this controller is the whole design. Reading is
/// open to an API key, because an agent has to be told what it was dispatched
/// with. Writing is not: <see cref="RequireAdminAttribute"/> with no scope
/// named refuses a key outright, so the rows that choose an agent's
/// instructions, its model and its budget can only be changed by a person at a
/// browser.
/// </summary>
/// <remarks>
/// That refusal is not a guess about what an agent would do with the write. It
/// is the one edge in this graph that closes a loop: an agent that can widen
/// its own prompt and raise its own effort has no fixed point to settle at, and
/// the failure is unbounded spend rather than a wrong answer. Cutting the edge
/// costs nothing - nobody wants a tracker that retunes itself unwatched - and
/// it is cut here rather than asked for politely in a prompt.
/// </remarks>
[ApiController]
[Route("api/hatch/playbooks")]
public class PlaybooksController(HatchContext db, TimeProvider time, ModelFamilies? families = null) : ControllerBase
{
    /// <summary>Every row, in board order - the order the page draws them in.</summary>
    [HttpGet]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public async Task<ActionResult<IReadOnlyList<PlaybookDto>>> GetPlaybooks(CancellationToken ct)
    {
        var rows = await db.Playbooks.AsNoTracking()
            .Include(p => p.FromStatus)
            .Include(p => p.ToStatus)
            .Include(p => p.Project)
            .ToListAsync(ct);

        return rows
            .OrderBy(p => p.FromStatus!.SortOrder)
            .ThenBy(p => p.ToStatus!.SortOrder)
            .ThenBy(p => p.Types, StringComparer.Ordinal)
            .Select(ToDto)
            .ToList();
    }

    [HttpPost]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<PlaybookDto>> CreatePlaybook(PlaybookCreateRequest request, CancellationToken ct)
    {
        var types = EfHatchPlaybook.NormalizeTypes(request.Types);
        var shape = request.Shape?.Trim() ?? EfHatchPlaybook.DefaultShape;
        if (PlaybookFile.InvalidShape(shape) is { } shapeError) return BadRequest(shapeError);

        int? projectId = null;
        if (request.ProjectId is not null)
        {
            var (value, projectIdError) = PlaybookPatches.ParseProjectId(request.ProjectId);
            if (projectIdError is not null) return BadRequest(projectIdError);
            projectId = value;
        }
        if (projectId is not null)
        {
            var project = await db.Projects
                .Where(p => p.Id == projectId)
                .Select(p => new { p.Key, p.RepoSourced })
                .FirstOrDefaultAsync(ct);
            if (project is null) return BadRequest($"there is no project {projectId}");
            if (project.RepoSourced)
                return BadRequest($"project {project.Key} reads its playbooks from .hatch/playbooks.yaml on its trunk, not from a row here");
        }

        if (await PlaybookPatches.Refusal(db, request.FromStatusId, request.ToStatusId, types, shape, projectId, id: null, ct) is { } error)
            return BadRequest(error);

        var prompt = request.Prompt?.Trim() ?? "";
        if (PlaybookFile.InvalidPrompt(prompt) is { } promptError) return BadRequest(promptError);

        var model = request.Model?.Trim() ?? EfHatchPlaybook.DefaultModel;
        var effort = request.Effort?.Trim() ?? EfHatchPlaybook.DefaultEffort;
        if (PlaybookPatches.InvalidDispatch(model, effort, families) is { } dispatchError) return BadRequest(dispatchError);

        int? budget = null;
        if (request.Budget is not null)
        {
            var (value, budgetError) = PlaybookFile.ParseBudget(request.Budget);
            if (budgetError is not null) return BadRequest(budgetError);
            budget = value;
        }

        int? briefLimit = null;
        if (request.BriefLimit is not null)
        {
            var (value, briefLimitError) = PlaybookFile.ParseBriefLimit(request.BriefLimit);
            if (briefLimitError is not null) return BadRequest(briefLimitError);
            briefLimit = value;
        }

        var deniedTools = EfHatchPlaybook.NormalizeDeniedTools(request.DeniedTools);
        if (PlaybookFile.InvalidDeniedTools(deniedTools) is { } deniedToolsError) return BadRequest(deniedToolsError);

        var now = time.GetUtcNow();
        var playbook = new EfHatchPlaybook
        {
            FromStatusId = request.FromStatusId,
            ToStatusId = request.ToStatusId,
            Types = types,
            Shape = shape,
            Prompt = prompt,
            Model = model,
            Effort = effort,
            Budget = budget,
            BriefLimit = briefLimit,
            DeniedTools = EfHatchPlaybook.WriteDeniedTools(deniedTools),
            IsCheck = request.IsCheck ?? false,
            ProjectId = projectId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Playbooks.Add(playbook);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetPlaybooks), await LoadDtoAsync(playbook.Id, ct));
    }

    [HttpPatch("{id:int}")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<PlaybookDto>> PatchPlaybook(int id, PlaybookPatchRequest request, CancellationToken ct)
    {
        var playbook = await db.Playbooks.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (playbook is null) return NotFound();

        var current = PlaybookState.Of(playbook);
        if (await PlaybookPatches.RefusalAsync(db, id, current, request, families, ct) is { } error)
            return BadRequest(error);

        var next = PlaybookPatches.Apply(current, request);
        playbook.FromStatusId = next.FromStatusId;
        playbook.ToStatusId = next.ToStatusId;
        playbook.Types = next.Types;
        playbook.Shape = next.Shape;
        playbook.Prompt = next.Prompt;
        playbook.Model = next.Model;
        playbook.Effort = next.Effort;
        playbook.Budget = next.Budget;
        playbook.BriefLimit = next.BriefLimit;
        playbook.DeniedTools = next.DeniedTools;
        playbook.IsCheck = next.IsCheck;
        playbook.ProjectId = next.ProjectId;
        playbook.UpdatedAt = time.GetUtcNow();

        await db.SaveChangesAsync(ct);
        return await LoadDtoAsync(id, ct);
    }

    [HttpDelete("{id:int}")]
    [RequireRole(PersonRole.User)]
    public async Task<IActionResult> DeletePlaybook(int id, CancellationToken ct)
    {
        var playbook = await db.Playbooks.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (playbook is null) return NotFound();

        db.Playbooks.Remove(playbook);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---- Reading back ----

    private async Task<PlaybookDto> LoadDtoAsync(int id, CancellationToken ct)
    {
        var row = await db.Playbooks.AsNoTracking()
            .Include(p => p.FromStatus)
            .Include(p => p.ToStatus)
            .Include(p => p.Project)
            .FirstAsync(p => p.Id == id, ct);

        return ToDto(row);
    }

    internal static PlaybookDto ToDto(EfHatchPlaybook p) => new(
        p.Id,
        p.FromStatusId,
        p.FromStatus?.Name ?? "",
        p.ToStatusId,
        p.ToStatus?.Name ?? "",
        EfHatchPlaybook.SplitTypes(p.Types),
        p.Shape,
        p.EffectivePrompt,
        p.DefaultPrompt,
        p.Model,
        p.Effort,
        p.Budget,
        p.BriefLimit,
        EfHatchPlaybook.ReadDeniedTools(p.DeniedTools) ?? [],
        p.IsCheck,
        p.UpdatedAt,
        p.ProjectId,
        p.Project?.Key,
        p.Project?.Name,
        p.ProjectId is not null ? "project" : "board");
}

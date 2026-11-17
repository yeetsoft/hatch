using Hatch.Api.Common;
using Hatch.Api.Services.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// What each person may see of the projects, set by an admin. Three routes,
/// all admin and all person-only: a key never reads or writes a scope, since
/// one that could would widen its own owner's view.
/// </summary>
/// <remarks>
/// No class-level attribute, for the reason <see cref="ProjectsController"/>
/// gives: every action is decorated explicitly.
/// </remarks>
[ApiController]
[Route("api/scopes")]
public class ScopesController(
    HatchContext db, IActorDirectory actors, ICallerIdentity caller, TimeProvider time,
    ILogger<ScopesController> logger) : ControllerBase
{
    /// <summary>Every person's scope. A person with no row reads as <c>all</c>, with every project id.</summary>
    [HttpGet]
    [RequireAdmin]
    public async Task<ActionResult<IReadOnlyList<PersonScopeDto>>> GetScopes(CancellationToken ct)
    {
        var people = (await actors.LiveAsync(ct)).Where(a => a.Kind == ActorKind.Person).Select(a => a.Id).ToList();
        var allProjects = await AllProjectIdsAsync(ct);
        var rows = await db.PersonScopes.AsNoTracking().ToDictionaryAsync(s => s.PersonId, ct);
        var chosen = (await db.PersonScopeProjects.AsNoTracking().ToListAsync(ct))
            .Where(p => allProjects.Contains(p.ProjectId))
            .GroupBy(p => p.PersonId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.ProjectId).Order().ToList());

        return people.Select(id => Dto(id, rows.GetValueOrDefault(id)?.Mode, allProjects, chosen.GetValueOrDefault(id))).ToList();
    }

    [HttpGet("{personId:guid}")]
    [RequireAdmin]
    public async Task<ActionResult<PersonScopeDto>> GetScope(Guid personId, CancellationToken ct)
    {
        if (await actors.ResolveAsync(ActorKind.Person, personId, ct) is null) return NotFound();

        return await ReadAsync(personId, ct);
    }

    /// <summary>
    /// Sets one person's scope, an admin's own included. <c>ProjectIds</c> is
    /// read only under <c>some</c>; writing <c>all</c> or <c>none</c> leaves no
    /// project list behind. Nothing is written unless everything is valid.
    /// </summary>
    [HttpPut("{personId:guid}")]
    [RequireAdmin]
    public async Task<ActionResult<PersonScopeDto>> PutScope(Guid personId, PersonScopeWriteRequest request, CancellationToken ct)
    {
        if (await actors.ResolveAsync(ActorKind.Person, personId, ct) is null) return NotFound();

        var mode = request.Mode?.Trim();
        if (!PersonScopeMode.IsKnown(mode))
            return BadRequest($"a scope is \"none\", \"all\" or \"some\" - not \"{request.Mode}\"");

        var wanted = new List<int>();
        if (mode == PersonScopeMode.Some)
        {
            if (request.ProjectIds is null) return BadRequest("a scope of \"some\" needs projectIds");

            wanted = request.ProjectIds.Distinct().ToList();
            var known = await AllProjectIdsAsync(ct);
            if (wanted.FirstOrDefault(id => !known.Contains(id), -1) is var unknown and not -1)
                return BadRequest($"there is no project {unknown}");
        }

        var actor = await caller.ActorNameAsync(ct);
        var row = await db.PersonScopes.FirstOrDefaultAsync(s => s.PersonId == personId, ct);
        if (row is null)
            db.PersonScopes.Add(new EfHatchPersonScope { PersonId = personId, Mode = mode!, UpdatedBy = actor, UpdatedAt = time.GetUtcNow() });
        else
            (row.Mode, row.UpdatedBy, row.UpdatedAt) = (mode!, actor, time.GetUtcNow());

        db.PersonScopeProjects.RemoveRange(await db.PersonScopeProjects.Where(p => p.PersonId == personId).ToListAsync(ct));
        db.PersonScopeProjects.AddRange(wanted.Select(id => new EfHatchPersonScopeProject { PersonId = personId, ProjectId = id }));
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Scope of {PersonId} set to {Mode} ({Count} projects) by {Actor}", personId, mode, wanted.Count, actor);

        return await ReadAsync(personId, ct);
    }

    private async Task<PersonScopeDto> ReadAsync(Guid personId, CancellationToken ct)
    {
        var allProjects = await AllProjectIdsAsync(ct);
        var mode = (await db.PersonScopes.AsNoTracking().FirstOrDefaultAsync(s => s.PersonId == personId, ct))?.Mode;
        var chosen = (await db.PersonScopeProjects.AsNoTracking().Where(p => p.PersonId == personId).Select(p => p.ProjectId).ToListAsync(ct))
            .Where(allProjects.Contains).Order().ToList();

        return Dto(personId, mode, allProjects, chosen);
    }

    private async Task<IReadOnlySet<int>> AllProjectIdsAsync(CancellationToken ct) =>
        (await db.Projects.AsNoTracking().Select(p => p.Id).ToListAsync(ct)).ToHashSet();

    /// <summary>No row and <c>all</c> read alike - every project; <c>none</c> reads as none; <c>some</c> as what was chosen.</summary>
    private static PersonScopeDto Dto(Guid personId, string? mode, IReadOnlySet<int> allProjects, List<int>? chosen) => mode switch
    {
        PersonScopeMode.None => new(personId, PersonScopeMode.None, []),
        PersonScopeMode.Some => new(personId, PersonScopeMode.Some, chosen ?? []),
        _ => new(personId, PersonScopeMode.All, allProjects.Order().ToList()),
    };
}

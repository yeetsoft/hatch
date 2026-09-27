using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Projects, which in Hatch are key namespaces rather than containers - see
/// <see cref="EfHatchProject"/>. Six verbs, and the interesting half of them
/// is what they refuse.
/// </summary>
/// <remarks>
/// No class-level attribute, and that is the fifth time this split has been
/// drawn (<see cref="RunnersController"/>, <see cref="AssigneeController"/>,
/// <see cref="IssuePlaybookController"/>, <see cref="SettingsController"/>):
/// <see cref="RequireRoleAttribute"/> is <c>AllowMultiple = false</c>, so a
/// method-level attribute silently *replaces* a class-level one rather than
/// tightening it. Decorating every action explicitly is what keeps
/// <see cref="PutRepositories"/> closed to a key whatever else is added
/// beside it.
/// </remarks>
[ApiController]
[Route("api/hatch/projects")]
public class ProjectsController(HatchContext db, TimeProvider time, ICallerIdentity caller) : ControllerBase
{
    /// <summary>A project may bind at most this many remotes - generous for anything a repository page has to draw in one row.</summary>
    public const int MaxRepositories = 20;

    [HttpGet]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public async Task<ActionResult<IReadOnlyList<ProjectDto>>> GetProjects(CancellationToken ct)
    {
        var projects = await db.Projects.AsNoTracking()
            .OrderBy(p => p.Key)
            .Select(p => new ProjectDto(
                p.Id, p.Key, p.Name, p.Issues.Count, p.CreatedAt,
                p.Repositories.OrderBy(r => r.SortOrder)
                    .Select(r => new ProjectRepositoryDto(r.Remote, r.Canonical, r.BaseBranch))
                    .ToList()))
            .ToListAsync(ct);

        return projects;
    }

    [HttpPost]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public async Task<ActionResult<ProjectDto>> CreateProject(ProjectCreateRequest request, CancellationToken ct)
    {
        // Upper-cased on the way in rather than refused: keys are shouted in
        // storage, and typing one in lower case is not a mistake worth a 400.
        var key = request.Key?.Trim().ToUpperInvariant();
        if (!EfHatchProject.IsValidKey(key))
            return BadRequest($"a project key is two to six letters or digits starting with a letter - not \"{request.Key}\"");

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name)) return BadRequest("a project needs a name");
        if (name.Length > EfHatchProject.MaxNameLength)
            return BadRequest($"a project name is at most {EfHatchProject.MaxNameLength} characters");

        // Checked here for the sentence, and enforced by a unique index
        // underneath for the race - the message is the point of doing it twice.
        if (await db.Projects.AnyAsync(p => p.Key == key, ct))
            return Conflict($"{key} is already taken");

        var project = new EfHatchProject { Key = key!, Name = name, CreatedAt = time.GetUtcNow() };
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetProjects), new ProjectDto(project.Id, project.Key, project.Name, 0, project.CreatedAt, []));
    }

    /// <summary>
    /// Renames a project, and - if the body says so - rekeys it.
    ///
    /// The name is the ordinary edit. The key is the one that costs something:
    /// every <c>AER-12</c> in a commit message, a branch name, or a chat log
    /// goes dead, because those are references this database has never seen and
    /// cannot rewrite. What survives is the structure - parentage is a foreign
    /// key and numbers are their own column, so every story stays under its
    /// epic and <c>AER-12</c> becomes <c>OPS-12</c> rather than becoming a
    /// different issue.
    ///
    /// The speed bump is the browser's: this endpoint refuses a bad key and
    /// takes a good one, and does not ask twice. A program that has decided to
    /// rekey a project has decided.
    /// </summary>
    [HttpPatch("{id:int}")]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public async Task<ActionResult<ProjectDto>> PatchProject(int id, ProjectPatchRequest request, CancellationToken ct)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null) return NotFound();

        // Both fields are optional here, unlike on a create - a request that
        // only rekeys must not have to re-send a name it is not touching.
        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (string.IsNullOrEmpty(name)) return BadRequest("a project needs a name");
            if (name.Length > EfHatchProject.MaxNameLength)
                return BadRequest($"a project name is at most {EfHatchProject.MaxNameLength} characters");

            project.Name = name;
        }

        if (request.Key is not null)
        {
            // Upper-cased on the way in, the same way a create does it: keys are
            // shouted in storage, and typing one in lower case is not a mistake
            // worth a 400.
            var key = request.Key.Trim().ToUpperInvariant();
            if (!EfHatchProject.IsValidKey(key))
                return BadRequest($"a project key is two to six letters or digits starting with a letter - not \"{request.Key}\"");

            if (key != project.Key)
            {
                if (await db.Projects.AnyAsync(p => p.Key == key && p.Id != id, ct))
                    return Conflict($"{key} is already taken");

                project.Key = key;
            }
        }

        await db.SaveChangesAsync(ct);

        var count = await db.Issues.CountAsync(i => i.ProjectId == id, ct);
        var repositories = await RepositoriesAsync(id, ct);
        return new ProjectDto(project.Id, project.Key, project.Name, count, project.CreatedAt, repositories);
    }

    /// <summary>
    /// Deletes an empty project. Never a full one: the issues would have to go
    /// somewhere, and every answer to "where" is worse than making the operator
    /// deal with them first.
    /// </summary>
    /// <remarks>
    /// Its bindings go with it - <see cref="HatchContext"/> cascades
    /// <see cref="EfHatchProjectRepository"/> off <see cref="EfHatchProject"/>,
    /// unlike an issue, which is why this delete never has to check for one.
    /// </remarks>
    [HttpDelete("{id:int}")]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public async Task<IActionResult> DeleteProject(int id, CancellationToken ct)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null) return NotFound();

        var count = await db.Issues.CountAsync(i => i.ProjectId == id, ct);
        if (count > 0) return Conflict($"{project.Key} still has {count} issue{(count == 1 ? "" : "s")} in it");

        db.Projects.Remove(project);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---- Repositories ----

    /// <summary>The remotes this project is bound to, in order. Read by everything a dispatch needs - it is what tells a runner where to check out.</summary>
    [HttpGet("{id:int}/repositories")]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public async Task<ActionResult<IReadOnlyList<ProjectRepositoryDto>>> GetRepositories(int id, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == id, ct)) return NotFound();

        return await RepositoriesAsync(id, ct);
    }

    /// <summary>
    /// Replaces the whole ordered list of remotes - the first entry is the
    /// primary. Refused as a whole, naming the entry, when any remote is
    /// empty, over the limit, unparseable, or a duplicate of another entry by
    /// canonical form; re-sending the list it already holds writes nothing.
    /// </summary>
    /// <remarks>
    /// Person only, and checked twice for <see cref="NotAPerson"/>'s reason: a
    /// runner that could bind a remote could point every runner on the board
    /// at a repository nobody chose - the exact edge playbooks and runner
    /// bounds are already closed to a key.
    /// </remarks>
    [HttpPut("{id:int}/repositories")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<IReadOnlyList<ProjectRepositoryDto>>> PutRepositories(
        int id, List<ProjectRepositoryWriteRequest> request, CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } refusal) return refusal;

        if (!await db.Projects.AnyAsync(p => p.Id == id, ct)) return NotFound();

        if (request.Count > MaxRepositories)
            return BadRequest($"a project may bind at most {MaxRepositories} repositories");

        // Every entry validated before anything is touched, so a request
        // naming one good entry and one bad one changes neither.
        var parsed = new List<(string Remote, string Canonical, string? BaseBranch)>(request.Count);
        for (var i = 0; i < request.Count; i++)
        {
            var position = i + 1;
            var remote = request[i].Remote?.Trim();
            if (string.IsNullOrEmpty(remote))
                return BadRequest($"entry {position} needs a remote");
            if (remote.Length > EfHatchProjectRepository.MaxRemoteLength)
                return BadRequest($"entry {position}'s remote is at most {EfHatchProjectRepository.MaxRemoteLength} characters");

            var baseBranch = request[i].BaseBranch?.Trim();
            if (baseBranch is { Length: 0 }) baseBranch = null;
            if (baseBranch is { Length: > EfHatchProjectRepository.MaxBaseBranchLength })
                return BadRequest($"entry {position}'s base branch is at most {EfHatchProjectRepository.MaxBaseBranchLength} characters");

            var (canonical, error) = RemoteIdentity.Canonical(remote);
            if (canonical is null) return BadRequest($"entry {position}: {error}");

            parsed.Add((remote, canonical, baseBranch));
        }

        for (var i = 1; i < parsed.Count; i++)
        {
            for (var j = 0; j < i; j++)
            {
                if (parsed[i].Canonical == parsed[j].Canonical)
                    return BadRequest($"entry {i + 1} is the same repository as entry {j + 1}");
            }
        }

        var current = await db.ProjectRepositories
            .Where(r => r.ProjectId == id)
            .OrderBy(r => r.SortOrder)
            .ToListAsync(ct);

        var unchanged = current.Count == parsed.Count &&
            current.Zip(parsed).All(pair => pair.First.Remote == pair.Second.Remote && pair.First.BaseBranch == pair.Second.BaseBranch);

        if (!unchanged)
        {
            db.ProjectRepositories.RemoveRange(current);

            var now = time.GetUtcNow();
            db.ProjectRepositories.AddRange(parsed.Select((r, i) => new EfHatchProjectRepository
            {
                ProjectId = id,
                Remote = r.Remote,
                Canonical = r.Canonical,
                BaseBranch = r.BaseBranch,
                SortOrder = i,
                CreatedAt = now,
            }));

            await db.SaveChangesAsync(ct);
        }

        return await RepositoriesAsync(id, ct);
    }

    private async Task<List<ProjectRepositoryDto>> RepositoriesAsync(int projectId, CancellationToken ct) =>
        await db.ProjectRepositories.AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .OrderBy(r => r.SortOrder)
            .Select(r => new ProjectRepositoryDto(r.Remote, r.Canonical, r.BaseBranch))
            .ToListAsync(ct);

    // ---- The one narrowing ----

    /// <summary>
    /// A person, not a key - see the type's own remark. Checked in the action
    /// as well as declared in the attribute, for
    /// <see cref="RunnersController"/>'s reason: <c>RoleGate</c> is dormant
    /// wherever the wall is off, which is all of local development, and a
    /// guarantee that evaporates under a switch is not a guarantee. A keyless
    /// runner that named itself with the runner header is refused here too -
    /// it is an agent whether or not it carries a credential.
    /// </summary>
    private async Task<ObjectResult?> NotAPerson(CancellationToken ct) =>
        await caller.IsProgramAsync(ct)
            ? new ObjectResult("which repositories a project points at is the operator's to set, not an agent's")
            {
                StatusCode = StatusCodes.Status403Forbidden,
            }
            : null;
}

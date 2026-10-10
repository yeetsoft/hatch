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
public class ProjectsController(
    HatchContext db, TimeProvider time, ICallerIdentity caller,
    IActorDirectory actors, IProjectAccess access, ILogger<ProjectsController> logger) : ControllerBase
{
    /// <summary>A project may bind at most this many remotes - generous for anything a repository page has to draw in one row.</summary>
    public const int MaxRepositories = 20;

    [HttpGet]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public async Task<ActionResult<IReadOnlyList<ProjectDto>>> GetProjects(CancellationToken ct)
    {
        var projects = await db.Projects.AsNoTracking().OrderBy(p => p.Key).ToListAsync(ct);

        var counts = await db.Issues.GroupBy(i => i.ProjectId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);

        var reposByProject = (await db.ProjectRepositories.AsNoTracking().OrderBy(r => r.SortOrder).ToListAsync(ct))
            .GroupBy(r => r.ProjectId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ProjectRepositoryDto>)g.Select(r => new ProjectRepositoryDto(r.Remote, r.Canonical, r.BaseBranch)).ToList());

        var logosByProject = await db.ProjectLogos.AsNoTracking()
            .Select(l => new { l.ProjectId, l.UpdatedAt })
            .ToDictionaryAsync(l => l.ProjectId, l => (DateTimeOffset?)l.UpdatedAt, ct);

        var memberRows = await db.ProjectMembers.AsNoTracking().ToListAsync(ct);
        var rowsByProject = memberRows.GroupBy(m => m.ProjectId).ToDictionary(g => g.Key, g => g.ToList());
        var personById = (await actors.LiveAsync(ct)).Where(a => a.Kind == ActorKind.Person).ToDictionary(a => a.Id);
        var me = await actors.MeAsync(ct);

        return projects.Select(p =>
        {
            var rows = rowsByProject.GetValueOrDefault(p.Id, []);
            var members = (IReadOnlyList<ProjectMemberDto>)rows
                .Where(row => personById.ContainsKey(row.PersonId))
                .Select(row => new ProjectMemberDto(row.PersonId, personById[row.PersonId].Name, row.Role))
                .ToList();
            var canAdminister = me is { Kind: ActorKind.Person } && rows.Any(row => row.PersonId == me.Id && row.Role == ProjectMemberRole.Owner);
            var canApprove = canAdminister || (me is { Kind: ActorKind.Person } && rows.Any(row => row.PersonId == me.Id && row.Role == ProjectMemberRole.Approver));

            return ToDto(p, counts.GetValueOrDefault(p.Id), reposByProject.GetValueOrDefault(p.Id, []), logosByProject.GetValueOrDefault(p.Id), members, canApprove, canAdminister);
        }).ToList();
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

        var color = request.Color?.Trim();
        if (string.IsNullOrEmpty(color)) color = null;
        else if (!EfHatchProject.IsValidColor(color)) return BadRequest($"a project colour is a hex value like #6b7280 - not \"{request.Color}\"");
        else color = EfHatchProject.NormalizeColor(color);

        var icon = request.Icon?.Trim();
        if (string.IsNullOrEmpty(icon)) icon = null;
        else if (!EfHatchProject.IsValidIcon(icon)) return BadRequest($"a project icon is 1-40 lower-case letters, digits or hyphens - not \"{request.Icon}\"");

        var project = new EfHatchProject { Key = key!, Name = name, Color = color, Icon = icon, CreatedAt = time.GetUtcNow() };
        db.Projects.Add(project);

        var principal = await actors.PrincipalAsync(ct);
        if (principal is { Kind: ActorKind.Person })
        {
            db.ProjectMembers.Add(new EfHatchProjectMember
            {
                Project = project,
                PersonId = principal.Id,
                Role = ProjectMemberRole.Owner,
                CreatedAt = time.GetUtcNow(),
            });
        }

        await db.SaveChangesAsync(ct);

        var me = await actors.MeAsync(ct);
        var members = (IReadOnlyList<ProjectMemberDto>)(principal is { Kind: ActorKind.Person } p
            ? [new ProjectMemberDto(p.Id, p.Name, ProjectMemberRole.Owner)]
            : []);
        var isSelfOwner = me is { Kind: ActorKind.Person } && principal is { Kind: ActorKind.Person } && me.Id == principal.Id;

        return CreatedAtAction(nameof(GetProjects), ToDto(project, 0, [], null, members, isSelfOwner, isSelfOwner));
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

        if (request.Color is not null)
        {
            var color = request.Color.Trim();
            if (color.Length == 0) project.Color = null;
            else
            {
                if (!EfHatchProject.IsValidColor(color))
                    return BadRequest($"a project colour is a hex value like #6b7280 - not \"{request.Color}\"");
                project.Color = EfHatchProject.NormalizeColor(color);
            }
        }

        if (request.Icon is not null)
        {
            var icon = request.Icon.Trim();
            if (icon.Length == 0) project.Icon = null;
            else
            {
                if (!EfHatchProject.IsValidIcon(icon))
                    return BadRequest($"a project icon is 1-40 lower-case letters, digits or hyphens - not \"{request.Icon}\"");
                project.Icon = icon;
            }
        }

        await db.SaveChangesAsync(ct);

        var count = await db.Issues.CountAsync(i => i.ProjectId == id, ct);
        var repositories = await RepositoriesAsync(id, ct);
        var logoUpdatedAt = await LogoUpdatedAtAsync(id, ct);
        var (members, canApprove, canAdminister) = await MembersAsync(id, ct);
        return ToDto(project, count, repositories, logoUpdatedAt, members, canApprove, canAdminister);
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

    // ---- Logo ----

    [HttpGet("{id:int}/logo")]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public async Task<IActionResult> GetLogo(int id, CancellationToken ct)
    {
        var logo = await db.ProjectLogos.AsNoTracking().FirstOrDefaultAsync(l => l.ProjectId == id, ct);
        if (logo is null) return NotFound();

        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "private, max-age=0, must-revalidate";

        var etag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{logo.UpdatedAt.UtcTicks:x}\"");
        return File(logo.Bytes, logo.ContentType, lastModified: logo.UpdatedAt, entityTag: etag);
    }

    [HttpPut("{id:int}/logo")]
    [RequireRole(PersonRole.User)]
    [RequestSizeLimit(PersonPhoto.MaxBytes + 1024)]
    public async Task<ActionResult<ProjectDto>> PutLogo(int id, CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } refusal) return refusal;

        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null) return NotFound();

        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();

        if (!PersonPhoto.TryDetectContentType(bytes, out var contentType, out var error)) return BadRequest(error);

        var now = time.GetUtcNow();
        var logo = await db.ProjectLogos.FirstOrDefaultAsync(l => l.ProjectId == id, ct);
        if (logo is null)
            db.ProjectLogos.Add(new EfHatchProjectLogo { ProjectId = id, Bytes = bytes, ContentType = contentType, UpdatedAt = now });
        else
        {
            logo.Bytes = bytes;
            logo.ContentType = contentType;
            logo.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        var count = await db.Issues.CountAsync(i => i.ProjectId == id, ct);
        var (members, canApprove, canAdminister) = await MembersAsync(id, ct);
        return ToDto(project, count, await RepositoriesAsync(id, ct), now, members, canApprove, canAdminister);
    }

    [HttpDelete("{id:int}/logo")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<ProjectDto>> DeleteLogo(int id, CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } refusal) return refusal;

        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null) return NotFound();

        var logo = await db.ProjectLogos.FirstOrDefaultAsync(l => l.ProjectId == id, ct);
        if (logo is null) return NotFound();

        db.ProjectLogos.Remove(logo);
        await db.SaveChangesAsync(ct);
        var count = await db.Issues.CountAsync(i => i.ProjectId == id, ct);
        var (members, canApprove, canAdminister) = await MembersAsync(id, ct);
        return ToDto(project, count, await RepositoriesAsync(id, ct), null, members, canApprove, canAdminister);
    }

    // ---- Members ----

    /// <summary>Who owns or approves on this project, read by everybody - reading who is on a project is not a power.</summary>
    [HttpGet("{id:int}/members")]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public async Task<ActionResult<IReadOnlyList<ProjectMemberDto>>> GetMembers(int id, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == id, ct)) return NotFound();

        return await LiveMembersAsync(id, ct);
    }

    /// <summary>
    /// Sets one person's role, adding them if they hold none yet. Only an
    /// owner may call this, and it refuses to leave a project with no owner at
    /// all - the one state <see cref="ClaimProject"/> exists to recover from.
    /// </summary>
    [HttpPut("{id:int}/members/{personId:guid}")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<IReadOnlyList<ProjectMemberDto>>> PutMember(
        int id, Guid personId, ProjectMemberWriteRequest request, CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } refusal) return refusal;

        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null) return NotFound();

        if (!await access.IsOwnerAsync(id, ct))
            return new ObjectResult($"only an owner of {project.Key} may add or remove its members") { StatusCode = StatusCodes.Status403Forbidden };

        if (await actors.ResolveAsync(ActorKind.Person, personId, ct) is null)
            return BadRequest("there is no such person");

        var role = request.Role?.Trim();
        if (!ProjectMemberRole.IsKnown(role))
            return BadRequest($"a role is \"owner\" or \"approver\" - not \"{request.Role}\"");

        var members = await LiveMembersAsync(id, ct);
        var existing = await db.ProjectMembers.FirstOrDefaultAsync(m => m.ProjectId == id && m.PersonId == personId, ct);

        if (existing is { Role: ProjectMemberRole.Owner } && role != ProjectMemberRole.Owner &&
            members.Count(m => m.Role == ProjectMemberRole.Owner) == 1)
            return Conflict($"{project.Key} would be left with no owner");

        if (existing is null)
        {
            db.ProjectMembers.Add(new EfHatchProjectMember { ProjectId = id, PersonId = personId, Role = role!, CreatedAt = time.GetUtcNow() });
        }
        else if (existing.Role != role)
        {
            existing.Role = role!;
        }
        else
        {
            return await LiveMembersAsync(id, ct);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Project {ProjectKey} member {PersonId} set to {Role} by {Actor}", project.Key, personId, role, await caller.ActorNameAsync(ct));

        return await LiveMembersAsync(id, ct);
    }

    /// <summary>Takes a member off a project. Only an owner may call this, and it refuses to leave a project with no owner at all.</summary>
    [HttpDelete("{id:int}/members/{personId:guid}")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<IReadOnlyList<ProjectMemberDto>>> DeleteMember(int id, Guid personId, CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } refusal) return refusal;

        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null) return NotFound();

        if (!await access.IsOwnerAsync(id, ct))
            return new ObjectResult($"only an owner of {project.Key} may add or remove its members") { StatusCode = StatusCodes.Status403Forbidden };

        var row = await db.ProjectMembers.FirstOrDefaultAsync(m => m.ProjectId == id && m.PersonId == personId, ct);
        if (row is null) return NotFound();

        var members = await LiveMembersAsync(id, ct);
        if (row.Role == ProjectMemberRole.Owner && members.Count(m => m.Role == ProjectMemberRole.Owner) == 1)
            return Conflict($"{project.Key} would be left with no owner");

        db.ProjectMembers.Remove(row);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Project {ProjectKey} member {PersonId} removed from {Actor}", project.Key, personId, await caller.ActorNameAsync(ct));

        return await LiveMembersAsync(id, ct);
    }

    /// <summary>
    /// Takes ownership of a project that has none - the way out of the state
    /// every owner-removal guard above exists to prevent anybody reaching any
    /// other way.
    /// </summary>
    [HttpPost("{id:int}/claim")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<IReadOnlyList<ProjectMemberDto>>> ClaimProject(int id, CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } refusal) return refusal;

        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null) return NotFound();

        var members = await LiveMembersAsync(id, ct);
        if (members.Any(m => m.Role == ProjectMemberRole.Owner))
            return Conflict($"{project.Key} already has an owner");

        var me = await actors.MeAsync(ct);
        var personId = me!.Id;

        var existing = await db.ProjectMembers.FirstOrDefaultAsync(m => m.ProjectId == id && m.PersonId == personId, ct);
        if (existing is null)
            db.ProjectMembers.Add(new EfHatchProjectMember { ProjectId = id, PersonId = personId, Role = ProjectMemberRole.Owner, CreatedAt = time.GetUtcNow() });
        else
            existing.Role = ProjectMemberRole.Owner;

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Project {ProjectKey} claimed by {Actor}", project.Key, await caller.ActorNameAsync(ct));

        return await LiveMembersAsync(id, ct);
    }

    private static ProjectDto ToDto(
        EfHatchProject project, int issueCount, IReadOnlyList<ProjectRepositoryDto> repositories, DateTimeOffset? logoUpdatedAt,
        IReadOnlyList<ProjectMemberDto> members, bool canApprove, bool canAdminister) =>
        new(project.Id, project.Key, project.Name, issueCount, project.CreatedAt, project.Color, project.Icon, repositories, logoUpdatedAt,
            members, canApprove, canAdminister);

    private async Task<List<ProjectMemberDto>> LiveMembersAsync(int projectId, CancellationToken ct)
    {
        var rows = await db.ProjectMembers.AsNoTracking().Where(m => m.ProjectId == projectId).ToListAsync(ct);

        var members = new List<ProjectMemberDto>(rows.Count);
        foreach (var row in rows)
        {
            if (await actors.ResolveAsync(ActorKind.Person, row.PersonId, ct) is not { } person) continue;
            members.Add(new ProjectMemberDto(row.PersonId, person.Name, row.Role));
        }

        return members;
    }

    /// <summary>
    /// A single project's <see cref="ProjectDto.Members"/>/<see cref="ProjectDto.CanApprove"/>/
    /// <see cref="ProjectDto.CanAdminister"/>, computed the same way <see cref="GetProjects"/>'s
    /// batched version is: one raw row query, <see cref="ProjectMemberDto"/> resolved per row, and
    /// the two booleans checked against the same raw rows and <see cref="IActorDirectory.MeAsync"/> -
    /// not <see cref="IProjectAccess"/>, which is a second, independent expression of the same rule.
    /// </summary>
    private async Task<(IReadOnlyList<ProjectMemberDto> Members, bool CanApprove, bool CanAdminister)> MembersAsync(int projectId, CancellationToken ct)
    {
        var rows = await db.ProjectMembers.AsNoTracking().Where(m => m.ProjectId == projectId).ToListAsync(ct);

        var members = new List<ProjectMemberDto>(rows.Count);
        foreach (var row in rows)
        {
            if (await actors.ResolveAsync(ActorKind.Person, row.PersonId, ct) is not { } person) continue;
            members.Add(new ProjectMemberDto(row.PersonId, person.Name, row.Role));
        }

        var me = await actors.MeAsync(ct);
        var canAdminister = me is { Kind: ActorKind.Person } && rows.Any(row => row.PersonId == me.Id && row.Role == ProjectMemberRole.Owner);
        var canApprove = canAdminister || (me is { Kind: ActorKind.Person } && rows.Any(row => row.PersonId == me.Id && row.Role == ProjectMemberRole.Approver));

        return (members, canApprove, canAdminister);
    }

    private async Task<List<ProjectRepositoryDto>> RepositoriesAsync(int projectId, CancellationToken ct) =>
        await db.ProjectRepositories.AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .OrderBy(r => r.SortOrder)
            .Select(r => new ProjectRepositoryDto(r.Remote, r.Canonical, r.BaseBranch))
            .ToListAsync(ct);

    private Task<DateTimeOffset?> LogoUpdatedAtAsync(int projectId, CancellationToken ct) =>
        db.ProjectLogos.AsNoTracking().Where(l => l.ProjectId == projectId)
            .Select(l => (DateTimeOffset?)l.UpdatedAt).FirstOrDefaultAsync(ct);

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
            ? new ObjectResult("which repositories a project points at, its logo, and who owns or approves on it are the operator's to set, not an agent's")
            {
                StatusCode = StatusCodes.Status403Forbidden,
            }
            : null;
}

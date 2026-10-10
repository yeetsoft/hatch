using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The board's columns. Rows rather than an enum because the operator adds and
/// reorders them from a page, and an enum would make "add a review column" a
/// deploy.
/// </summary>
/// <remarks>
/// No class-level attribute, the same split <see cref="ProjectsController"/>
/// draws: <see cref="RequireRoleAttribute"/> is <c>AllowMultiple = false</c>,
/// so a method-level attribute silently *replaces* a class-level one rather
/// than tightening it. Decorating every action explicitly is what keeps
/// <see cref="PutExpressSkips"/>, <see cref="PutParentPulls"/> and
/// <see cref="PutAgentFiles"/>, <see cref="PutImplementation"/>,
/// <see cref="PutProtected"/> and <see cref="PutMergesPullRequest"/> closed to a
/// key whatever else is added beside it - each decides which gates the loop may pass unattended, the same kind
/// of power <see cref="PutRepositories"/> guards over there.
/// </remarks>
[ApiController]
[Route("api/hatch/statuses")]
public class StatusesController(HatchContext db, ICallerIdentity caller) : ControllerBase
{
    [HttpGet]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public async Task<ActionResult<IReadOnlyList<StatusDto>>> GetStatuses(CancellationToken ct)
    {
        var statuses = await db.Statuses.AsNoTracking()
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Id)
            .Select(s => new StatusDto(
                s.Id, s.Name, s.SortOrder, s.IsTerminal, s.IsDeferred, s.IsWip, s.Color, s.ExpressSkips, s.ParentPulls,
                s.AgentFiles, s.IsImplementation, s.IsProtected, s.MergesPullRequest))
            .ToListAsync(ct);

        return statuses;
    }

    [HttpPost]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public async Task<ActionResult<StatusDto>> CreateStatus(StatusCreateRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (Invalid(name) is { } error) return BadRequest(error);
        if (InvalidColor(request.Color) is { } colorError) return BadRequest(colorError);
        if (await db.Statuses.AnyAsync(s => s.Name == name, ct)) return Conflict($"there is already a \"{name}\" column");

        var status = new EfHatchStatus
        {
            Name = name!,
            Color = request.Color is null ? EfHatchStatus.DefaultColor : EfHatchStatus.NormalizeColor(request.Color),
            // A column with no stated position goes on the right, a gap past
            // the last one - the same sparse trick ranks use a size up, so the
            // next insertion between two columns is a single write.
            SortOrder = request.SortOrder ?? await NextSortOrderAsync(ct),
            IsTerminal = request.IsTerminal ?? false,
            IsDeferred = request.IsDeferred ?? false,
        };
        db.Statuses.Add(status);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(
            nameof(GetStatuses),
            new StatusDto(
                status.Id, status.Name, status.SortOrder, status.IsTerminal, status.IsDeferred, status.IsWip,
                status.Color, status.ExpressSkips, status.ParentPulls, status.AgentFiles, status.IsImplementation,
                status.IsProtected, status.MergesPullRequest));
    }

    [HttpPatch("{id:int}")]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public async Task<ActionResult<StatusDto>> PatchStatus(int id, StatusPatchRequest request, CancellationToken ct)
    {
        var status = await db.Statuses.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (status is null) return NotFound();

        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (Invalid(name) is { } error) return BadRequest(error);
            if (await db.Statuses.AnyAsync(s => s.Name == name && s.Id != id, ct))
                return Conflict($"there is already a \"{name}\" column");
            status.Name = name;
        }

        if (request.Color is not null)
        {
            if (InvalidColor(request.Color) is { } colorError) return BadRequest(colorError);
            status.Color = EfHatchStatus.NormalizeColor(request.Color);
        }

        if (request.SortOrder is { } sortOrder) status.SortOrder = sortOrder;

        if (request.IsTerminal is { } terminal)
        {
            if (terminal && status.MergesPullRequest)
                return BadRequest($"\"{status.Name}\" merges the pull request - untick that before making it a done column");
            if (terminal && status.IsProtected)
                return BadRequest($"\"{status.Name}\" is protected - untick that before making it a done column");
            status.IsTerminal = terminal;
        }

        if (request.IsDeferred is { } deferred)
        {
            if (deferred && status.MergesPullRequest)
                return BadRequest($"\"{status.Name}\" merges the pull request - untick that before making it deferred");
            if (deferred && status.IsProtected)
                return BadRequest($"\"{status.Name}\" is protected - untick that before making it deferred");
            status.IsDeferred = deferred;
        }

        await db.SaveChangesAsync(ct);
        return new StatusDto(
            status.Id, status.Name, status.SortOrder, status.IsTerminal, status.IsDeferred, status.IsWip,
            status.Color, status.ExpressSkips, status.ParentPulls, status.AgentFiles,
            status.IsImplementation, status.IsProtected, status.MergesPullRequest);
    }

    /// <summary>
    /// Ticks or unticks <em>Express skips</em>. Its own action rather than one
    /// more field on <see cref="PatchStatus"/>, because that route is open to a
    /// key: this one decides which columns the loop may carry an express issue
    /// past with nobody reading it first, the same kind of power
    /// <see cref="IssueExpressController"/> guards on the issue itself.
    /// </summary>
    [HttpPut("{id:int}/express-skips")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<StatusDto>> PutExpressSkips(int id, ExpressSkipsRequest request, CancellationToken ct)
    {
        var status = await db.Statuses.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (status is null) return NotFound();

        status.ExpressSkips = request.ExpressSkips;
        await db.SaveChangesAsync(ct);

        return new StatusDto(
            status.Id, status.Name, status.SortOrder, status.IsTerminal, status.IsDeferred, status.IsWip,
            status.Color, status.ExpressSkips, status.ParentPulls, status.AgentFiles,
            status.IsImplementation, status.IsProtected, status.MergesPullRequest);
    }

    /// <summary>
    /// Ticks or unticks <em>Parent pulls</em>. Its own action rather than one
    /// more field on <see cref="PatchStatus"/>, for the same reason
    /// <see cref="PutExpressSkips"/> is: this one decides which columns carry a
    /// child on with no session while its parent stands in the implementation
    /// column, and that is a playbook's kind of power.
    /// </summary>
    [HttpPut("{id:int}/parent-pulls")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<StatusDto>> PutParentPulls(int id, ParentPullsRequest request, CancellationToken ct)
    {
        var status = await db.Statuses.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (status is null) return NotFound();

        status.ParentPulls = request.ParentPulls;
        await db.SaveChangesAsync(ct);

        return new StatusDto(
            status.Id, status.Name, status.SortOrder, status.IsTerminal, status.IsDeferred, status.IsWip,
            status.Color, status.ExpressSkips, status.ParentPulls, status.AgentFiles,
            status.IsImplementation, status.IsProtected, status.MergesPullRequest);
    }

    /// <summary>
    /// Ticks or unticks <em>Agent files</em>: the column a program's own
    /// issues are born in. Refuses a deferred or a terminal column - parked or
    /// shipped work is not somewhere work is born, the same refusal
    /// <see cref="WipController"/> gives those columns - and ticking one
    /// column clears every other, so at most one ever holds it; unticking is
    /// always allowed, including on a column that became deferred or terminal
    /// after being ticked, the same stranded-flag tolerance <c>IsWip</c>
    /// gives that case.
    /// </summary>
    [HttpPut("{id:int}/agent-files")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<StatusDto>> PutAgentFiles(int id, AgentFilesRequest request, CancellationToken ct)
    {
        var status = await db.Statuses.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (status is null) return NotFound();

        if (request.AgentFiles)
        {
            if (status.IsDeferred) return BadRequest($"\"{status.Name}\" is deferred - a program's issues are not born there");
            if (status.IsTerminal) return BadRequest($"\"{status.Name}\" is a done column - a program's issues are not born there");

            var others = await db.Statuses.Where(s => s.Id != id && s.AgentFiles).ToListAsync(ct);
            foreach (var other in others) other.AgentFiles = false;
        }

        status.AgentFiles = request.AgentFiles;
        await db.SaveChangesAsync(ct);

        return new StatusDto(
            status.Id, status.Name, status.SortOrder, status.IsTerminal, status.IsDeferred, status.IsWip,
            status.Color, status.ExpressSkips, status.ParentPulls, status.AgentFiles,
            status.IsImplementation, status.IsProtected, status.MergesPullRequest);
    }

    /// <summary>
    /// Ticks or unticks <em>Implementation</em>: the column where code gets
    /// written. Refuses a deferred or a terminal column - parked or shipped
    /// work is not somewhere code is written, the same refusal
    /// <see cref="PutAgentFiles"/> gives those columns - and ticking one
    /// column clears every other, so at most one ever holds it; unticking is
    /// always allowed, including on a column that became deferred or terminal
    /// after being ticked, the same stranded-flag tolerance <c>IsWip</c>
    /// gives that case.
    /// </summary>
    [HttpPut("{id:int}/implementation")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<StatusDto>> PutImplementation(int id, ImplementationRequest request, CancellationToken ct)
    {
        var status = await db.Statuses.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (status is null) return NotFound();

        if (request.IsImplementation)
        {
            if (status.IsDeferred) return BadRequest($"\"{status.Name}\" is deferred - code is not written there");
            if (status.IsTerminal) return BadRequest($"\"{status.Name}\" is a done column - code is not written there");

            var others = await db.Statuses.Where(s => s.Id != id && s.IsImplementation).ToListAsync(ct);
            foreach (var other in others) other.IsImplementation = false;
        }

        status.IsImplementation = request.IsImplementation;
        await db.SaveChangesAsync(ct);

        return new StatusDto(
            status.Id, status.Name, status.SortOrder, status.IsTerminal, status.IsDeferred, status.IsWip,
            status.Color, status.ExpressSkips, status.ParentPulls, status.AgentFiles,
            status.IsImplementation, status.IsProtected, status.MergesPullRequest);
    }

    /// <summary>
    /// Ticks or unticks <em>Protected</em>: who may move work into this column -
    /// an owner or approver of the issue's project, never a key, never a hop,
    /// never a dispatch (HA-293 asks the gate; this ticket only carries the
    /// flag). Refuses a deferred or a terminal column, the same refusal
    /// <see cref="PutAgentFiles"/> gives those columns for its own question;
    /// refuses unticking while <see cref="MergesPullRequest"/> still holds,
    /// since a merge column with nobody guarding its door would let anything
    /// that can move a card merge its own code (HA-289's decision). Checked in
    /// the action as well as declared on the attribute
    /// (<see cref="NotAPerson"/>), the <see cref="ProjectsController.NotAPerson"/>
    /// idiom: <c>RoleGate</c> is dormant wherever the wall is off, and a keyless
    /// runner there is a program too.
    /// </summary>
    [HttpPut("{id:int}/protected")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<StatusDto>> PutProtected(int id, ProtectedRequest request, CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } refusal) return refusal;

        var status = await db.Statuses.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (status is null) return NotFound();

        if (request.On)
        {
            if (status.IsDeferred) return BadRequest($"\"{status.Name}\" is deferred - parked work is never protected");
            if (status.IsTerminal) return BadRequest($"\"{status.Name}\" is a done column - shipped work is never protected");
        }
        else if (status.MergesPullRequest)
        {
            return BadRequest($"\"{status.Name}\" merges the pull request - untick that first");
        }

        status.IsProtected = request.On;
        await db.SaveChangesAsync(ct);

        return new StatusDto(
            status.Id, status.Name, status.SortOrder, status.IsTerminal, status.IsDeferred, status.IsWip,
            status.Color, status.ExpressSkips, status.ParentPulls, status.AgentFiles, status.IsImplementation,
            status.IsProtected, status.MergesPullRequest);
    }

    /// <summary>
    /// Ticks or unticks <em>Merges the pull request</em>: whether the runner's
    /// poll merges an issue's pull request once it stands here, current and
    /// green, with no session (HA-289's epic). Requires <see cref="IsProtected"/>
    /// already on - a column that merges but is not protected would let
    /// anything that can move a card, a key included, merge its own code - and
    /// refuses a deferred or a terminal column for the same reason
    /// <see cref="PutProtected"/> does. Unticking is always allowed, the
    /// stranded-flag tolerance <c>IsWip</c> gives that case.
    /// </summary>
    [HttpPut("{id:int}/merges-pull-request")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<StatusDto>> PutMergesPullRequest(int id, MergesPullRequestRequest request, CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } refusal) return refusal;

        var status = await db.Statuses.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (status is null) return NotFound();

        if (request.On)
        {
            if (status.IsDeferred) return BadRequest($"\"{status.Name}\" is deferred - parked work never merges a pull request");
            if (status.IsTerminal) return BadRequest($"\"{status.Name}\" is a done column - shipped work never merges a pull request");
            if (!status.IsProtected) return BadRequest($"\"{status.Name}\" is not protected - tick that first");
        }

        status.MergesPullRequest = request.On;
        await db.SaveChangesAsync(ct);

        return new StatusDto(
            status.Id, status.Name, status.SortOrder, status.IsTerminal, status.IsDeferred, status.IsWip,
            status.Color, status.ExpressSkips, status.ParentPulls, status.AgentFiles, status.IsImplementation,
            status.IsProtected, status.MergesPullRequest);
    }

    /// <summary>
    /// Deletes an unused column. The issues in a column are the reason it
    /// cannot simply go - moving them somewhere on the operator's behalf would
    /// be this endpoint deciding that a dozen tickets are now "todo".
    /// </summary>
    [HttpDelete("{id:int}")]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public async Task<IActionResult> DeleteStatus(int id, CancellationToken ct)
    {
        var status = await db.Statuses.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (status is null) return NotFound();

        var count = await db.Issues.CountAsync(i => i.StatusId == id, ct);
        if (count > 0)
            return Conflict($"\"{status.Name}\" still holds {count} issue{(count == 1 ? "" : "s")}");

        // Not in the plan's table, and here because the alternative is a state
        // the UI cannot get out of: a board with no columns has nowhere to put
        // a new issue, so deleting the last one would leave Hatch unusable
        // until somebody opened psql.
        if (await db.Statuses.CountAsync(ct) == 1)
            return Conflict("a board needs at least one column");

        db.Statuses.Remove(status);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<int> NextSortOrderAsync(CancellationToken ct)
    {
        var last = await db.Statuses.OrderByDescending(s => s.SortOrder).Select(s => (int?)s.SortOrder).FirstOrDefaultAsync(ct);
        return (last ?? 0) + 10;
    }

    /// <summary>
    /// Null is "leave it alone" here as everywhere; anything else has to be a
    /// colour this can store, said in one shape - see
    /// <see cref="EfHatchStatus.IsValidColor"/>.
    /// </summary>
    private static string? InvalidColor(string? color) =>
        color is null || EfHatchStatus.IsValidColor(color)
            ? null
            : $"a column colour is a hex value like #6b7280 - not \"{color}\"";

    private static string? Invalid(string? name) => name switch
    {
        null or "" => "a column needs a name",
        { Length: > EfHatchStatus.MaxNameLength } => $"a column name is at most {EfHatchStatus.MaxNameLength} characters",
        _ => null,
    };

    private async Task<ObjectResult?> NotAPerson(CancellationToken ct) =>
        await caller.IsProgramAsync(ct)
            ? new ObjectResult("whether a column is protected, or merges the pull request, is the operator's to set - not an agent's")
            {
                StatusCode = StatusCodes.Status403Forbidden,
            }
            : null;
}

using Hatch.Api.Common;
using Hatch.Api.Ef;
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
/// <see cref="PutAgentFiles"/> closed to a key whatever else is added beside
/// it - each decides which gates the loop may pass unattended, the same kind
/// of power <see cref="PutRepositories"/> guards over there.
/// </remarks>
[ApiController]
[Route("api/hatch/statuses")]
public class StatusesController(HatchContext db) : ControllerBase
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
                s.AgentFiles))
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
                status.Color, status.ExpressSkips, status.ParentPulls, status.AgentFiles));
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
        if (request.IsTerminal is { } terminal) status.IsTerminal = terminal;
        if (request.IsDeferred is { } deferred) status.IsDeferred = deferred;

        await db.SaveChangesAsync(ct);
        return new StatusDto(
            status.Id, status.Name, status.SortOrder, status.IsTerminal, status.IsDeferred, status.IsWip,
            status.Color, status.ExpressSkips, status.ParentPulls, status.AgentFiles);
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
            status.Color, status.ExpressSkips, status.ParentPulls, status.AgentFiles);
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
            status.Color, status.ExpressSkips, status.ParentPulls, status.AgentFiles);
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
            status.Color, status.ExpressSkips, status.ParentPulls, status.AgentFiles);
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
}

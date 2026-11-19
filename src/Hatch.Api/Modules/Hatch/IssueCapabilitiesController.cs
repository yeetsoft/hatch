using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// An ambition's capabilities: rows with a statement and a how-to-tell line,
/// read in order and written one at a time. Every route answers 400 on an
/// issue that is not an ambition.
/// </summary>
/// <remarks>
/// <see cref="EfHatchCapability.Position"/> runs 1 to n with no gaps, and
/// every write here leaves it so - there is no unique index to lean on, so the
/// tests do. A write that changes nothing writes no event and moves no
/// <c>UpdatedAt</c>, as <see cref="IssueDreamsController"/> does. A key may
/// read anywhere but write only left of the implementation column: see
/// <see cref="GateAsync"/>.
/// </remarks>
[ApiController]
[Route("api/issues/{key}/capabilities")]
[RequireRole(PersonRole.User, AcceptKey = true)]
public class IssueCapabilitiesController(HatchContext db, ICallerIdentity caller, TimeProvider time) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CapabilityDto[]>> GetCapabilities(string key, CancellationToken ct)
    {
        if (await LoadAsync(key, ct) is not { } issue) return NotFound();
        if (NotAnAmbition(issue, key) is { } refusal) return refusal;

        return await ReadAsync(issue.Id, ct);
    }

    [HttpPost]
    public async Task<ActionResult<CapabilityDto>> PostCapability(string key, CapabilityCreateRequest request, CancellationToken ct)
    {
        if (await LoadAsync(key, ct) is not { } issue) return NotFound();
        if (NotAnAmbition(issue, key) is { } refusal) return refusal;
        if (await GateAsync(issue, ct) is { } gated) return gated;

        if (Blank(request.Statement, request.HowToTell) is { } blank) return blank;
        var statement = request.Statement.Trim();
        var howToTell = request.HowToTell.Trim();
        if (TooLong(statement, howToTell) is { } tooLong) return tooLong;

        var rows = await RowsAsync(issue.Id, ct);
        var position = request.Position ?? rows.Count + 1;
        if (position < 1 || position > rows.Count + 1)
            return BadRequest($"position {position} is outside 1 to {rows.Count + 1} - {key} holds {rows.Count}");

        var now = time.GetUtcNow();
        var actor = await caller.ActorNameAsync(ct);

        // The row first, so its id is there for the event: two saves, one transaction.
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;

        foreach (var row in rows.Where(r => r.Position >= position).OrderByDescending(r => r.Position))
        {
            row.Position++;
        }

        var added = new EfHatchCapability
        {
            IssueId = issue.Id,
            Position = position,
            Statement = statement,
            HowToTell = howToTell,
            CreatedBy = actor,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Capabilities.Add(added);
        await db.SaveChangesAsync(ct);

        issue.Events.Add(Event(actor, EfHatchIssueEvent.CapabilityAdded, new { id = added.Id, position, statement }, now));
        issue.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);

        return CreatedAtAction(nameof(GetCapabilities), new { key }, Map(added));
    }

    [HttpPatch("{id:long}")]
    public async Task<ActionResult<CapabilityDto>> PatchCapability(string key, long id, CapabilityPatchRequest request, CancellationToken ct)
    {
        if (await LoadAsync(key, ct) is not { } issue) return NotFound();
        if (NotAnAmbition(issue, key) is { } refusal) return refusal;
        if (await GateAsync(issue, ct) is { } gated) return gated;

        var row = await db.Capabilities.FirstOrDefaultAsync(c => c.Id == id && c.IssueId == issue.Id, ct);
        if (row is null) return NotFound();

        if (request.Statement is not null && Blank(request.Statement) is { } blankStatement) return blankStatement;
        if (request.HowToTell is not null && Blank(request.HowToTell) is { } blankHowToTell) return blankHowToTell;

        var statement = request.Statement?.Trim() ?? row.Statement;
        var howToTell = request.HowToTell?.Trim() ?? row.HowToTell;
        if (TooLong(statement, howToTell) is { } tooLong) return tooLong;

        if (statement == row.Statement && howToTell == row.HowToTell) return Map(row);

        var now = time.GetUtcNow();
        var actor = await caller.ActorNameAsync(ct);

        issue.Events.Add(Event(
            actor,
            EfHatchIssueEvent.CapabilityEdited,
            new
            {
                id = row.Id,
                position = row.Position,
                from = new { statement = row.Statement, howToTell = row.HowToTell },
                to = new { statement, howToTell },
            },
            now));

        row.Statement = statement;
        row.HowToTell = howToTell;
        row.UpdatedAt = now;
        issue.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        return Map(row);
    }

    /// <summary>
    /// Puts the capabilities in the order the body names. The body must name
    /// every capability once and nothing else; a body that names the order
    /// already there writes nothing.
    /// </summary>
    [HttpPut("order")]
    public async Task<ActionResult<CapabilityDto[]>> PutOrder(string key, CapabilityOrderRequest request, CancellationToken ct)
    {
        if (await LoadAsync(key, ct) is not { } issue) return NotFound();
        if (NotAnAmbition(issue, key) is { } refusal) return refusal;
        if (await GateAsync(issue, ct) is { } gated) return gated;

        var rows = await RowsAsync(issue.Id, ct);
        var wanted = request.Ids;
        var held = rows.Select(r => r.Id).ToHashSet();

        if (wanted.Distinct().Count() != wanted.Count)
            return BadRequest("the order names a capability twice");
        if (wanted.Where(id => !held.Contains(id)).Select(id => (long?)id).FirstOrDefault() is { } foreign)
            return BadRequest($"capability {foreign} is not one of {key}'s");
        if (wanted.Count != rows.Count)
            return BadRequest($"the order names {wanted.Count} of {key}'s {rows.Count} capabilities - it must name every one");

        var before = rows.Select(r => r.Id).ToList();
        if (!before.SequenceEqual(wanted))
        {
            var now = time.GetUtcNow();
            var actor = await caller.ActorNameAsync(ct);
            var byId = rows.ToDictionary(r => r.Id);

            for (var i = 0; i < wanted.Count; i++)
            {
                var row = byId[wanted[i]];
                if (row.Position == i + 1) continue;
                row.Position = i + 1;
                row.UpdatedAt = now;
            }

            issue.Events.Add(Event(actor, EfHatchIssueEvent.CapabilityMoved, new { from = before, to = wanted }, now));
            issue.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }

        return await ReadAsync(issue.Id, ct);
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> DeleteCapability(string key, long id, CancellationToken ct)
    {
        if (await LoadAsync(key, ct) is not { } issue) return NotFound();
        if (NotAnAmbition(issue, key) is { } refusal) return refusal;
        if (await GateAsync(issue, ct) is { } gated) return gated;

        var rows = await RowsAsync(issue.Id, ct);
        if (rows.FirstOrDefault(r => r.Id == id) is not { } row) return NotFound();

        var now = time.GetUtcNow();
        var actor = await caller.ActorNameAsync(ct);

        foreach (var later in rows.Where(r => r.Position > row.Position).OrderBy(r => r.Position))
        {
            later.Position--;
            later.UpdatedAt = now;
        }

        db.Capabilities.Remove(row);
        issue.Events.Add(Event(
            actor, EfHatchIssueEvent.CapabilityRemoved, new { id = row.Id, position = row.Position, statement = row.Statement }, now));
        issue.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>
    /// A key, not a person, and only once the ambition has reached the
    /// implementation column, passed it, or been deferred. What an ambition
    /// asks for is settled before work on it starts; after that, changing it
    /// is a person's call. Reads are never gated. A person is never refused
    /// here. Checked in the action rather than left to <c>RoleGate</c>, for
    /// <see cref="IssuesController"/>'s reason: this route has to go on
    /// accepting a key.
    /// </summary>
    private async Task<ObjectResult?> GateAsync(EfHatchIssue issue, CancellationToken ct)
    {
        if (!await caller.IsProgramAsync(ct)) return null;

        var statuses = await db.Statuses.AsNoTracking().ToListAsync(ct);

        if (statuses.Any(s => s.Id == issue.StatusId && s.IsDeferred))
            return Forbidden("writing a capability on a deferred ambition is a person's call, not an agent's");

        return Columns.AtOrPastImplementation(statuses, issue.StatusId)
            ? Forbidden("writing a capability past the implementation column is a person's call, not an agent's")
            : null;
    }

    private static ObjectResult Forbidden(string why) => new(why) { StatusCode = StatusCodes.Status403Forbidden };

    private static BadRequestObjectResult? NotAnAmbition(EfHatchIssue issue, string key) =>
        issue.Type == "ambition" ? null : new($"only an ambition takes capabilities - {key} is a {issue.Type}");

    private BadRequestObjectResult? Blank(params string[] lines) =>
        lines.Any(string.IsNullOrWhiteSpace) ? new("a capability needs a statement and a how-to-tell line, and neither may be blank") : null;

    private BadRequestObjectResult? TooLong(string statement, string howToTell)
    {
        if (statement.Length > EfHatchCapability.MaxStatementLength)
            return new($"a statement is at most {EfHatchCapability.MaxStatementLength} characters");
        if (howToTell.Length > EfHatchCapability.MaxHowToTellLength)
            return new($"a how-to-tell line is at most {EfHatchCapability.MaxHowToTellLength} characters");
        return null;
    }

    private async Task<List<EfHatchCapability>> RowsAsync(long issueId, CancellationToken ct) =>
        await db.Capabilities.Where(c => c.IssueId == issueId).OrderBy(c => c.Position).ThenBy(c => c.Id).ToListAsync(ct);

    private async Task<CapabilityDto[]> ReadAsync(long issueId, CancellationToken ct) =>
        (await db.Capabilities.AsNoTracking().Where(c => c.IssueId == issueId).OrderBy(c => c.Position).ThenBy(c => c.Id).ToListAsync(ct))
            .Select(Map)
            .ToArray();

    private static CapabilityDto Map(EfHatchCapability c) =>
        new(c.Id, c.Position, c.Statement, c.HowToTell, c.CreatedBy, c.CreatedAt, c.UpdatedAt);

    private async Task<EfHatchIssue?> LoadAsync(string key, CancellationToken ct)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return null;

        return await db.Issues.Include(i => i.Project).WithKey(projectKey, number).FirstOrDefaultAsync(ct);
    }

    private static EfHatchIssueEvent Event(string actor, string kind, object? payload, DateTimeOffset at) => new()
    {
        Actor = actor,
        Kind = kind,
        Payload = payload is null ? null : JsonSerializer.Serialize(payload),
        At = at,
    };
}

using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// An ambition's changes, and what each epic beneath it says a change does -
/// see docs/hatch.md, "ChangeOutcome". A change is read, never stored - see
/// <see cref="AmbitionChanges"/>.
/// </summary>
/// <remarks>
/// Every route answers <c>400</c> on an issue that is not an ambition. Unlike
/// <see cref="IssueCapabilitiesController"/>, writing here is never gated on
/// the column - the same reason <c>POST .../verdicts</c> is not: recording
/// what a change did to an epic is a judgment about the work, not a change to
/// what the ambition asks for.
/// </remarks>
[ApiController]
[Route("api/issues/{key}/changes")]
[RequireRole(PersonRole.User, AcceptKey = true)]
public class IssueChangesController(HatchContext db, ICallerIdentity caller, TimeProvider time) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ChangeDto[]>> GetChanges(string key, CancellationToken ct)
    {
        if (await LoadAsync(key, ct) is not { } issue) return NotFound();
        if (NotAnAmbition(issue, key) is { } refusal) return refusal;

        var changes = (await AmbitionChanges.ReadAsync(db, [issue.Id], ct)).GetValueOrDefault(issue.Id, []);
        if (changes.Count == 0) return Array.Empty<ChangeDto>();

        var outcomes = await db.ChangeOutcomes.AsNoTracking()
            .Where(o => o.IssueId == issue.Id)
            .Select(o => new
            {
                o.Id, o.ChangeEventId, o.Outcome, o.Why, o.RecordedBy, o.RecordedAt, o.Decision, o.DecidedBy, o.DecidedAt,
                EpicProjectKey = o.Epic!.Project!.Key, EpicNumber = o.Epic!.Number,
                EpicTitle = o.Epic!.Title, EpicColumn = o.Epic!.Status!.Name,
            })
            .ToListAsync(ct);

        // Two rows for the same epic in the same change keep only the newest -
        // ordinary data from before POST started replacing in place, or a row
        // moved here by a later fix.
        var newest = outcomes
            .GroupBy(o => new { o.ChangeEventId, o.EpicProjectKey, o.EpicNumber })
            .Select(g => g.OrderByDescending(x => x.Id).First())
            .ToList();

        return changes.Select(c => new ChangeDto(
            c.Id, c.SavedAt, c.WorkedOutAt is not null, c.WorkedOutAt,
            c.Events.Select(MapEntry).ToList(),
            newest
                .Where(o => o.ChangeEventId > c.After && o.ChangeEventId <= c.Upto)
                .OrderBy(o => o.EpicProjectKey, StringComparer.Ordinal).ThenBy(o => o.EpicNumber)
                .Select(o => new ChangeOutcomeDto(
                    o.ChangeEventId, IssueKey.Format(o.EpicProjectKey, o.EpicNumber), o.EpicTitle, o.EpicColumn,
                    o.Outcome, o.Why, o.RecordedBy, o.RecordedAt,
                    o.Decision is null ? null : new ChangeDecisionDto(o.Decision, o.DecidedBy!, o.DecidedAt!.Value)))
                .ToList()))
            .ToArray();
    }

    /// <summary>
    /// Records one epic's outcome on one change. Refused, in order: an issue
    /// that is not an ambition; an <paramref name="eventId"/> that is not an
    /// added, edited or removed event of it; an epic that is not a direct
    /// child of it; an outcome that is not one of the three words; a why
    /// that is blank or too long. A row already decided is refused with
    /// <c>409</c> and nothing written. Recording the same outcome and why
    /// again writes nothing.
    /// </summary>
    [HttpPost("{eventId:long}/outcomes")]
    public async Task<ActionResult<ChangeOutcomeDto>> PostOutcome(string key, long eventId, ChangeOutcomeRequest request, CancellationToken ct)
    {
        if (await LoadAsync(key, ct) is not { } issue) return NotFound();
        if (NotAnAmbition(issue, key) is { } refusal) return refusal;

        var isChangeEvent = await db.IssueEvents.AsNoTracking()
            .AnyAsync(e => e.Id == eventId && e.IssueId == issue.Id && AmbitionChanges.Kinds.Contains(e.Kind), ct);
        if (!isChangeEvent)
            return BadRequest($"{eventId} is not an added, edited or removed event of {key}");

        var epicKey = (request.EpicKey ?? "").Trim();
        if (!IssueKey.TryParse(epicKey, out var epicProjectKey, out var epicNumber))
            return BadRequest($"\"{epicKey}\" is not an issue key");

        var epic = await db.Issues.Include(i => i.Project).Include(i => i.Status).WithKey(epicProjectKey, epicNumber).FirstOrDefaultAsync(ct);
        if (epic is null) return BadRequest($"there is no {epicKey}");
        if (epic.Type != "epic" || epic.ParentId != issue.Id)
            return BadRequest($"{epicKey} is not an epic directly under {key}");

        var outcome = (request.Outcome ?? "").Trim();
        if (outcome is not (EfHatchChangeOutcome.CarriesOn or EfHatchChangeOutcome.NotTouched or EfHatchChangeOutcome.ServesNothing))
            return BadRequest(
                $"an outcome is one of {EfHatchChangeOutcome.CarriesOn}, {EfHatchChangeOutcome.NotTouched} and {EfHatchChangeOutcome.ServesNothing}");

        var why = (request.Why ?? "").Trim();
        if (why.Length == 0) return BadRequest("why may not be blank");
        if (why.Length > EfHatchChangeOutcome.MaxWhyLength)
            return BadRequest($"why is at most {EfHatchChangeOutcome.MaxWhyLength} characters");

        var existing = await db.ChangeOutcomes
            .FirstOrDefaultAsync(o => o.IssueId == issue.Id && o.ChangeEventId == eventId && o.EpicId == epic.Id, ct);
        if (existing?.Decision is not null)
            return Conflict($"a person has already decided {epicKey}'s outcome on this change");

        var epicFormatted = IssueKey.Format(epic.Project!.Key, epic.Number);

        if (existing is not null && existing.Outcome == outcome && existing.Why == why)
            return Ok(MapOutcome(existing, epicFormatted, epic.Title, epic.Status!.Name));

        var now = time.GetUtcNow();
        var actor = await caller.ActorNameAsync(ct);

        EfHatchChangeOutcome row;
        if (existing is not null)
        {
            existing.Outcome = outcome;
            existing.Why = why;
            existing.RecordedBy = actor;
            existing.RecordedAt = now;
            row = existing;
        }
        else
        {
            row = new EfHatchChangeOutcome
            {
                IssueId = issue.Id, EpicId = epic.Id, ChangeEventId = eventId,
                Outcome = outcome, Why = why, RecordedBy = actor, RecordedAt = now,
            };
            db.ChangeOutcomes.Add(row);
        }

        epic.Events.Add(Event(actor, EfHatchIssueEvent.ChangeOutcomeRecorded, new { ambition = key, changeEventId = eventId, outcome, why }, now));
        epic.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        var dto = MapOutcome(row, epicFormatted, epic.Title, epic.Status!.Name);
        return existing is null ? CreatedAtAction(nameof(GetChanges), new { key }, dto) : Ok(dto);
    }

    private static ChangeOutcomeDto MapOutcome(EfHatchChangeOutcome o, string epicKey, string epicTitle, string epicColumn) =>
        new(o.ChangeEventId, epicKey, epicTitle, epicColumn, o.Outcome, o.Why, o.RecordedBy, o.RecordedAt,
            o.Decision is null ? null : new ChangeDecisionDto(o.Decision, o.DecidedBy!, o.DecidedAt!.Value));

    /// <summary>
    /// One event of a change, read tolerantly: a payload that will not parse,
    /// or does not carry the field wanted, gives null rather than throwing.
    /// </summary>
    private static ChangeEntryDto MapEntry(EfHatchIssueEvent e)
    {
        var kind = e.Kind switch
        {
            EfHatchIssueEvent.CapabilityAdded => "added",
            EfHatchIssueEvent.CapabilityEdited => "changed",
            EfHatchIssueEvent.CapabilityRemoved => "removed",
            _ => "",
        };

        var payload = IssueEventPayload.Parse(e.Payload);
        var capabilityId = ReadLong(payload, "id");
        var position = ReadInt(payload, "position");

        string? statementBefore = null, howToTellBefore = null, statementAfter = null, howToTellAfter = null;

        if (e.Kind == EfHatchIssueEvent.CapabilityAdded)
        {
            statementAfter = ReadString(payload, "statement");
            howToTellAfter = ReadString(payload, "howToTell");
        }
        else if (e.Kind == EfHatchIssueEvent.CapabilityRemoved)
        {
            statementBefore = ReadString(payload, "statement");
            howToTellBefore = ReadString(payload, "howToTell");
        }
        else if (e.Kind == EfHatchIssueEvent.CapabilityEdited)
        {
            var from = ReadElement(payload, "from");
            var to = ReadElement(payload, "to");
            statementBefore = ReadString(from, "statement");
            howToTellBefore = ReadString(from, "howToTell");
            statementAfter = ReadString(to, "statement");
            howToTellAfter = ReadString(to, "howToTell");
        }

        return new ChangeEntryDto(
            e.Id, kind, capabilityId, position, statementBefore, howToTellBefore, statementAfter, howToTellAfter, e.Actor, e.At);
    }

    private static JsonElement? ReadElement(JsonElement? element, string property) =>
        element?.ValueKind == JsonValueKind.Object && element.Value.TryGetProperty(property, out var value) ? value : null;

    private static string? ReadString(JsonElement? element, string property)
    {
        var value = ReadElement(element, property);
        return value?.ValueKind == JsonValueKind.String ? value.Value.GetString() : null;
    }

    private static long? ReadLong(JsonElement? element, string property)
    {
        var value = ReadElement(element, property);
        return value?.ValueKind == JsonValueKind.Number && value.Value.TryGetInt64(out var n) ? n : null;
    }

    private static int? ReadInt(JsonElement? element, string property)
    {
        var value = ReadElement(element, property);
        return value?.ValueKind == JsonValueKind.Number && value.Value.TryGetInt32(out var n) ? n : null;
    }

    private static BadRequestObjectResult? NotAnAmbition(EfHatchIssue issue, string key) =>
        issue.Type == "ambition" ? null : new($"only an ambition has changes - {key} is a {issue.Type}");

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

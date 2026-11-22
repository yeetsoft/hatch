using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// A person's decision on an epic that serves nothing now: shelve it with the
/// work under it, or keep it running - see docs/hatch.md, "ChangeOutcome".
/// </summary>
/// <remarks>
/// A class of its own, with no class-level role, because
/// <see cref="IssueChangesController"/> accepts a key on the whole class and
/// this route must not. Shelving or keeping is a person's call, never an
/// agent's - the same reason a key is refused a move into a protected column.
/// </remarks>
[ApiController]
[Route("api/issues/{key}/changes/outcomes/{id:long}/decision")]
public class IssueChangeDecisionController(
    HatchContext db,
    RankService ranks,
    IActorDirectory actors,
    IProjectAccess access,
    ICallerIdentity caller,
    TimeProvider time) : ControllerBase
{
    /// <summary>
    /// Records <paramref name="request"/>'s decision on one outcome. Refused,
    /// in order, with nothing written: an issue that is not an ambition; an
    /// <paramref name="id"/> that is not an outcome of it (<c>404</c>); a
    /// decision that is not <c>shelve</c> or <c>keep</c>; an outcome that is
    /// not <c>serves_nothing</c>; an outcome already decided (<c>409</c>).
    /// Then, for <c>shelve</c>: an epic no longer open (<c>409</c>), a board
    /// with no deferred column, and a deferred column the caller may not move
    /// work into (<c>403</c>).
    /// </summary>
    /// <remarks>
    /// <c>keep</c> moves nothing. <c>shelve</c> moves the epic and the open
    /// work under it into the first deferred column, as a deferral does, and
    /// leaves work already in a terminal or a deferred column where it is -
    /// all in one transaction with the decision. No changelog task is filed:
    /// a deferred column is not a close.
    /// </remarks>
    [HttpPost]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<ChangeOutcomeDto>> PostDecision(string key, long id, ChangeDecisionRequest request, CancellationToken ct)
    {
        if (await LoadAsync(key, ct) is not { } ambition) return NotFound();
        if (IssueChangesController.NotAnAmbition(ambition, key) is { } refusal) return refusal;

        var row = await db.ChangeOutcomes
            .Include(o => o.Epic).ThenInclude(e => e!.Project)
            .FirstOrDefaultAsync(o => o.Id == id && o.IssueId == ambition.Id, ct);
        if (row?.Epic is not { } epic) return NotFound($"{id} is not an outcome of {key}");

        var decision = (request?.Decision ?? "").Trim();
        if (decision is not (EfHatchChangeOutcome.Shelve or EfHatchChangeOutcome.Keep))
            return BadRequest($"a decision is one of {EfHatchChangeOutcome.Shelve} and {EfHatchChangeOutcome.Keep}");

        var epicKey = IssueKey.Format(epic.Project!.Key, epic.Number);
        if (row.Outcome != EfHatchChangeOutcome.ServesNothing)
            return BadRequest($"only an outcome that serves nothing is decided - {epicKey}'s is {row.Outcome}");
        if (row.Decision is not null)
            return Conflict($"a person has already decided {epicKey}'s outcome on this change");

        var statuses = await db.Statuses.AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync(ct);
        var byId = statuses.ToDictionary(s => s.Id);

        var now = time.GetUtcNow();
        var actor = await caller.ActorNameAsync(ct);

        if (decision == EfHatchChangeOutcome.Keep)
        {
            Record(row, epic, key, decision, actor, now);
            await db.SaveChangesAsync(ct);
            return Ok(MapOutcome(row, epic, epicKey, byId));
        }

        var epicColumn = byId.GetValueOrDefault(epic.StatusId);
        if (epicColumn is { IsTerminal: true } or { IsDeferred: true })
            return Conflict($"{epicKey} is in \"{epicColumn.Name}\", so there is nothing left to shelve");

        var target = statuses.FirstOrDefault(s => s.IsDeferred);
        if (target is null) return BadRequest("this board has no deferred column to shelve into");

        var descendantIds = await Rollup.DescendantIdsAsync(db, epic.Id, ct);
        var below = await db.Issues.Include(i => i.Project)
            .Where(i => descendantIds.Contains(i.Id))
            .ToListAsync(ct);
        var moving = new List<EfHatchIssue> { epic };
        moving.AddRange(below
            .Where(i => byId.GetValueOrDefault(i.StatusId) is not ({ IsTerminal: true } or { IsDeferred: true }))
            .OrderBy(i => i.Project!.Key, StringComparer.Ordinal).ThenBy(i => i.Number));

        if (target.IsProtected)
        {
            foreach (var project in moving.Select(i => i.Project!).DistinctBy(p => p.Id))
            {
                if (await access.CanMoveIntoAsync(project.Id, target, ct)) continue;

                return new ObjectResult($"only an owner or approver of {project.Key} may move work into \"{target.Name}\"")
                {
                    StatusCode = StatusCodes.Status403Forbidden,
                };
            }
        }

        var who = await SetterAuthority.CallerAsync(actors, ct);

        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;

        var bottoms = new ColumnBottoms(ranks);
        var shelved = new List<long>();

        foreach (var issue in moving)
        {
            var from = byId.GetValueOrDefault(issue.StatusId);
            issue.Events.Add(Event(actor, EfHatchIssueEvent.StatusChanged, IssueEventPayload.StatusChanged(from, target), now));

            Starts.Record(issue, from is not null && Columns.CrossesStart(statuses, from.Id, target.Id), who, now);

            issue.StatusId = target.Id;
            issue.ClosedAt = Columns.ClosedAt(from, target, now, issue.ClosedAt);
            if (NoCode.ClearOnMove(issue, target, statuses, actor, now) is { } cleared)
                issue.Events.Add(cleared);
            issue.Rank = await bottoms.NextAsync(target.Id, ct);
            issue.UpdatedAt = now;

            shelved.Add(issue.Id);
        }

        // As a move into a deferred column does it: whoever is waiting on one
        // of these is told once, on their own ticket.
        await Deferrals.NoteAsync(db, shelved, target, actor, now, ct);

        Record(row, epic, key, decision, actor, now);
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);

        return Ok(MapOutcome(row, epic, epicKey, byId));
    }

    private static void Record(EfHatchChangeOutcome row, EfHatchIssue epic, string ambitionKey, string decision, string actor, DateTimeOffset now)
    {
        row.Decision = decision;
        row.DecidedBy = actor;
        row.DecidedAt = now;

        epic.Events.Add(Event(
            actor, EfHatchIssueEvent.ChangeDecided, new { ambition = ambitionKey, changeEventId = row.ChangeEventId, decision }, now));
        epic.UpdatedAt = now;
    }

    private static ChangeOutcomeDto MapOutcome(
        EfHatchChangeOutcome row, EfHatchIssue epic, string epicKey, IReadOnlyDictionary<int, EfHatchStatus> byId) =>
        IssueChangesController.MapOutcome(row, epicKey, epic.Title, byId[epic.StatusId].Name);

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

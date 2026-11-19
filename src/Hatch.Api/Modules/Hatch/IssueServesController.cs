using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// What an epic serves: the capabilities of the ambition it hangs beneath,
/// read and replaced whole.
/// </summary>
/// <remarks>
/// Only an epic writes, and only capabilities of the one ambition above it, at
/// any depth - <see cref="EfHatchIssue.LegalParentTypes"/> lets at most one
/// ambition stand anywhere above an epic. <see cref="IssuesController"/>
/// holds the other half of the rule: a reparent that would leave an epic
/// serving a capability of an ambition it no longer hangs beneath is refused.
/// A key may write in any column - a link is a statement about the work, as a
/// dependency is, not about what an ambition asks for.
/// </remarks>
[ApiController]
[Route("api/issues/{key}/serves")]
[RequireRole(PersonRole.User, AcceptKey = true)]
public class IssueServesController(HatchContext db, ICallerIdentity caller, TimeProvider time) : ControllerBase
{
    /// <summary>
    /// Replaces the set of capabilities this epic serves. A request that names
    /// the set already there writes nothing, logs nothing and moves no
    /// <c>UpdatedAt</c>, as <see cref="IssueDreamsController"/> does.
    /// </summary>
    [HttpPut]
    public async Task<ActionResult<ServedCapabilityDto[]>> PutServes(string key, IssueServesWriteRequest request, CancellationToken ct)
    {
        if (await LoadAsync(key, ct) is not { } issue) return NotFound();
        if (issue.Type != "epic") return BadRequest($"only an epic serves capabilities - {key} is a {issue.Type}");

        var requested = request.CapabilityIds.Distinct().ToList();
        var ambitionId = await AmbitionAboveAsync(db, issue.Id, ct);

        var owners = await db.Capabilities.AsNoTracking()
            .Where(c => requested.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.IssueId, ct);

        foreach (var id in requested)
        {
            if (!owners.TryGetValue(id, out var owner)) return BadRequest($"there is no capability {id}");
            if (owner == ambitionId) continue;

            var ownerKey = await IssueProjection.KeyOfAsync(db, owner, ct);
            return BadRequest(ambitionId is null
                ? $"capability {id} belongs to {ownerKey}, and {key} hangs beneath no ambition"
                : $"capability {id} belongs to {ownerKey}, not to {await IssueProjection.KeyOfAsync(db, ambitionId.Value, ct)}, "
                    + $"the ambition {key} hangs beneath");
        }

        var current = await db.CapabilityLinks.Where(l => l.IssueId == issue.Id).Select(l => l.CapabilityId).ToListAsync(ct);

        if (!current.ToHashSet().SetEquals(requested))
        {
            var toAdd = requested.Except(current).ToList();
            var toRemove = current.Except(requested).ToList();

            if (toRemove.Count > 0)
            {
                var rows = await db.CapabilityLinks
                    .Where(l => l.IssueId == issue.Id && toRemove.Contains(l.CapabilityId))
                    .ToListAsync(ct);
                db.CapabilityLinks.RemoveRange(rows);
            }

            var now = time.GetUtcNow();
            var actor = await caller.ActorNameAsync(ct);

            foreach (var capabilityId in toAdd)
            {
                db.CapabilityLinks.Add(new EfHatchCapabilityLink
                {
                    CapabilityId = capabilityId,
                    IssueId = issue.Id,
                    Actor = actor,
                    At = now,
                });
            }

            issue.Events.Add(Event(
                actor,
                EfHatchIssueEvent.ServesChanged,
                new { from = current.OrderBy(x => x), to = requested.OrderBy(x => x) },
                now));

            issue.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }

        return await ReadAsync(issue.Id, ct);
    }

    /// <summary>The capabilities this issue serves - any type of issue, and an empty list for one that serves none.</summary>
    [HttpGet]
    public async Task<ActionResult<ServedCapabilityDto[]>> GetServes(string key, CancellationToken ct)
    {
        if (await LoadAsync(key, ct) is not { } issue) return NotFound();

        return await ReadAsync(issue.Id, ct);
    }

    /// <summary>
    /// The id of the ambition an issue hangs beneath, at any depth, or null.
    /// An ambition takes no parent and at most one stands above anything, so
    /// the first one found walking up is the one. Starts at the issue given, so
    /// an ambition answers with itself.
    /// </summary>
    internal static async Task<long?> AmbitionAboveAsync(HatchContext db, long? from, CancellationToken ct)
    {
        var seen = new HashSet<long>();
        var current = from;

        while (current is { } id && seen.Add(id))
        {
            var step = await db.Issues.Where(i => i.Id == id).Select(i => new { i.Type, i.ParentId }).FirstOrDefaultAsync(ct);
            if (step is null) return null;
            if (step.Type == "ambition") return id;
            current = step.ParentId;
        }

        return null;
    }

    private async Task<ServedCapabilityDto[]> ReadAsync(long issueId, CancellationToken ct)
    {
        var rows = await db.CapabilityLinks.AsNoTracking()
            .Where(l => l.IssueId == issueId)
            .Select(l => new
            {
                AmbitionProject = l.Capability!.Issue!.Project!.Key,
                AmbitionNumber = l.Capability!.Issue!.Number,
                l.CapabilityId,
                l.Capability!.Position,
                l.Capability!.Statement,
            })
            .ToListAsync(ct);

        var newest = await IssueCapabilitiesController.NewestVerdictsAsync(db, rows.Select(r => r.CapabilityId), ct);

        return rows
            .OrderBy(r => r.AmbitionProject, StringComparer.Ordinal).ThenBy(r => r.AmbitionNumber).ThenBy(r => r.Position).ThenBy(r => r.CapabilityId)
            .Select(r => new ServedCapabilityDto(
                IssueKey.Format(r.AmbitionProject, r.AmbitionNumber), r.CapabilityId, r.Position, r.Statement,
                newest.TryGetValue(r.CapabilityId, out var verdict) ? verdict.Verdict : EfHatchCapabilityVerdict.NotChecked))
            .ToArray();
    }

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

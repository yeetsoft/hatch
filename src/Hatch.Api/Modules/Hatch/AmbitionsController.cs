using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Where every ambition stands, in one call: its capabilities counted four
/// ways and the epics open beneath it.
///
/// Nothing is stored. Every number is worked out on read, from the same rows
/// and the same tree the queue reads, so this page and the queue cannot
/// disagree about what is waiting on a person.
/// </summary>
[ApiController]
[Route("api/ambitions")]
[RequireRole(PersonRole.User, AcceptKey = true)]
public class AmbitionsController(HatchContext db, IActorDirectory actors) : ControllerBase
{
    private const string Ambition = "ambition";

    private const string Epic = "epic";

    /// <summary>
    /// Every ambition the reader may see, in board order: column sort order,
    /// then rank, then id.
    /// </summary>
    /// <param name="projectId">
    /// Whose ambitions to list, or every project's when absent or empty;
    /// repeatable to name several, as <c>/api/plan</c> takes it. An id naming
    /// no project contributes nothing.
    /// </param>
    /// <remarks>
    /// A capability is counted once, in the first of these that is true: its
    /// newest verdict holds; an open epic serves it and every one of those
    /// waits on a person; an open epic serves it; none of those. An epic is
    /// open when it is neither closed nor deferred, and waits on a person when
    /// a hold-level question is on it or an ancestor, when a dangerous flag
    /// stands over any open question there, or when it is held. A question on
    /// a story beneath the epic folds nothing: the walk only goes up.
    /// </remarks>
    [HttpGet]
    public async Task<ActionResult<AmbitionRowDto[]>> GetAmbitions([FromQuery] List<int>? projectId, CancellationToken ct)
    {
        var projectIds = (projectId ?? []).Distinct().ToList();

        var ambitions = await InProject(db.Issues.AsNoTracking().Where(i => i.Type == Ambition), projectIds)
            .OrderBy(i => i.Status!.SortOrder)
            .ThenBy(i => i.Rank)
            .ThenBy(i => i.Id)
            .Select(i => new
            {
                i.Id,
                ProjectKey = i.Project!.Key,
                i.Number,
                i.Title,
                i.StatusId,
                StatusName = i.Status!.Name,
                i.WipLimit,
            })
            .ToListAsync(ct);
        if (ambitions.Count == 0) return Array.Empty<AmbitionRowDto>();

        var priorities = await PriorityTree.ForReaderAsync(db, actors, ct);
        var ambitionIds = ambitions.Select(a => a.Id).ToList();

        // The open ones are PassFold's rule: not terminal, not deferred. A child
        // the reader's scope hides is not in this query, so it is not in the body.
        var epics = await db.Issues.AsNoTracking()
            .Where(i => i.Type == Epic && i.ParentId != null && ambitionIds.Contains(i.ParentId.Value))
            .Where(i => !i.Status!.IsTerminal && !i.Status!.IsDeferred)
            .OrderBy(i => i.Rank)
            .ThenBy(i => i.Id)
            .Select(i => new
            {
                i.Id,
                ParentId = i.ParentId!.Value,
                ProjectKey = i.Project!.Key,
                i.Number,
                i.Title,
                i.StatusId,
                StatusName = i.Status!.Name,
                i.Held,
            })
            .ToListAsync(ct);

        var capabilities = await db.Capabilities.AsNoTracking()
            .Where(c => ambitionIds.Contains(c.IssueId))
            .OrderBy(c => c.Position)
            .ThenBy(c => c.Id)
            .Select(c => new { c.Id, c.IssueId, c.Position })
            .ToListAsync(ct);
        var newest = await IssueCapabilitiesController.NewestVerdictsAsync(db, capabilities.Select(c => c.Id), ct);

        var epicIds = epics.Select(e => e.Id).ToList();
        var links = epicIds.Count == 0
            ? []
            : await db.CapabilityLinks.AsNoTracking()
                .Where(l => epicIds.Contains(l.IssueId))
                .Select(l => new { l.CapabilityId, l.IssueId })
                .ToListAsync(ct);

        var capabilityById = capabilities.ToDictionary(c => c.Id);
        var epicById = epics.ToDictionary(e => e.Id);

        // Only a capability of the epic's own ambition is served: a link written
        // across ambitions is not a position this epic has.
        var servesOf = links
            .Where(l => capabilityById.TryGetValue(l.CapabilityId, out var c) && c.IssueId == epicById[l.IssueId].ParentId)
            .GroupBy(l => l.IssueId)
            .ToDictionary(g => g.Key, g => g.Select(l => capabilityById[l.CapabilityId].Position).Order().ToList());

        var waits = epics.ToDictionary(e => e.Id, e => WaitingWhy(priorities, e.Id, e.Held));

        var rows = new List<AmbitionRowDto>(ambitions.Count);
        foreach (var a in ambitions)
        {
            var open = epics.Where(e => e.ParentId == a.Id).ToList();
            var held = 0;
            var waiting = 0;
            var flying = 0;
            var notStarted = 0;
            var own = capabilities.Where(c => c.IssueId == a.Id).ToList();

            foreach (var c in own)
            {
                var servers = open.Where(e => servesOf.GetValueOrDefault(e.Id)?.Contains(c.Position) == true).ToList();

                if (newest.GetValueOrDefault(c.Id)?.Verdict == EfHatchCapabilityVerdict.Holds) held++;
                else if (servers.Count > 0 && servers.All(e => waits[e.Id] is not null)) waiting++;
                else if (servers.Count > 0) flying++;
                else notStarted++;
            }

            rows.Add(new AmbitionRowDto(
                IssueKey.Format(a.ProjectKey, a.Number),
                a.ProjectKey,
                a.Title,
                a.StatusId,
                a.StatusName,
                a.WipLimit ?? 1,
                own.Count,
                held,
                flying,
                waiting,
                notStarted,
                open.Select(e => new AmbitionOpenEpicDto(
                    IssueKey.Format(e.ProjectKey, e.Number),
                    e.ProjectKey,
                    e.Title,
                    e.StatusId,
                    e.StatusName,
                    servesOf.GetValueOrDefault(e.Id) ?? [],
                    waits[e.Id] is not null,
                    waits[e.Id])).ToList(),
                priorities.Disclose(a.Id).ChildBeyondScope));
        }

        return rows.ToArray();
    }

    /// <summary>
    /// One line saying why an epic waits on a person, or null when it does not.
    /// The first of three that is true wins. None names a key or quotes a
    /// reason, because the cause may sit on an ancestor the reader cannot see.
    /// </summary>
    private static string? WaitingWhy(PriorityTree priorities, long epicId, bool held)
    {
        var question = priorities.EffectiveQuestion(epicId);

        if (question is { } q && q.Level == QuestionLevels.HoldName)
            return "A question is holding it until somebody answers.";
        if (question is not null && priorities.EffectiveDangerous(epicId).Reason is not null)
            return "It is flagged dangerous and a question is open on it.";
        if (held)
            return "It is held until somebody releases it.";

        return null;
    }

    /// <summary>The optional project filter.</summary>
    private static IQueryable<EfHatchIssue> InProject(IQueryable<EfHatchIssue> issues, List<int> projectIds) =>
        projectIds.Count > 0 ? issues.Where(i => projectIds.Contains(i.ProjectId)) : issues;
}

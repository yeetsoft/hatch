using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Where every ambition stands, in one call: its stage and what it needs next,
/// its capabilities counted four ways and the epics open beneath it.
///
/// Nothing is stored. Every number is worked out on read, from the same rows
/// and the same tree the queue reads, so this page and the queue cannot
/// disagree about what is waiting on a person.
/// </summary>
[ApiController]
[Route("api/ambitions")]
[RequireRole(PersonRole.User, AcceptKey = true)]
public class AmbitionsController(HatchContext db, IActorDirectory actors, TimeProvider time) : ControllerBase
{
    private const string Ambition = "ambition";

    private const string Epic = "epic";

    /// <summary>How long a run of holds may have lasted and still be new.</summary>
    private static readonly TimeSpan NewlyHoldingWindow = TimeSpan.FromDays(7);

    /// <summary>
    /// Every ambition the reader may see, grouped by stage: <c>budget spent</c>,
    /// <c>running</c>, <c>stopped</c>, <c>complete</c>, <c>ready</c>,
    /// <c>shaping</c>, <c>draft</c>, then <c>done</c> and <c>shelved</c>
    /// together. Inside a group they keep board order: column sort order, then
    /// rank, then id.
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
    ///
    /// <para>The stage is <see cref="AmbitionStage.Of"/>'s. Everything it needs
    /// is read once for the whole list - the columns, the playbooks, the queue's
    /// own gate and the budgets - so the cost does not grow with the number of
    /// ambitions.</para>
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
                i.ProjectId,
                ProjectKey = i.Project!.Key,
                i.Number,
                i.Title,
                i.StatusId,
                StatusName = i.Status!.Name,
                i.WipLimit,
                i.BudgetUsd,
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

        var heldLongAgo = await HeldBeforeAsync(
            capabilities.Select(c => c.Id).Where(id => newest.GetValueOrDefault(id)?.Verdict == EfHatchCapabilityVerdict.Holds).ToList(), ct);

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

        var statuses = await db.Statuses.AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync(ct);
        var statusById = statuses.ToDictionary(s => s.Id);
        var playbooks = await db.Playbooks.AsNoTracking().ToListAsync(ct);

        // Only an ambition in the implementation column asks why its pass is not
        // running or what it has spent, so only such a row costs the reads.
        var implementationId = Columns.Implementation(statuses)?.Id;
        var inPass = ambitions.Where(a => a.StatusId == implementationId).ToList();

        // The gate counts every child, as the queue does, whatever the reader's
        // scope holds: the reader's tree is read unfiltered, so it serves here.
        var family = inPass.Count == 0 ? null : await FamilyGate.ForAsync(db, actors, statuses, ct, priorities);
        var spent = await AmbitionBudget.SpentAsync(db, inPass.Where(a => a.BudgetUsd is not null).Select(a => a.Id).ToList(), ct);
        var now = time.GetUtcNow();

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

            var (stage, why) = AmbitionStage.Of(
                statusById[a.StatusId], statuses, own.Count, held,
                Dispatch.MatchShapingPress(playbooks, a.ProjectId)?.FromStatusId,
                budgetSpent: spent.TryGetValue(a.Id, out var sum) && AmbitionBudget.IsSpent(a.BudgetUsd, sum.Spent),
                passFold: a.StatusId == implementationId ? family?.PassFold(a.Id, a.WipLimit, statuses, now)?.Sentence : null);

            rows.Add(new AmbitionRowDto(
                IssueKey.Format(a.ProjectKey, a.Number),
                a.ProjectKey,
                a.Title,
                a.StatusId,
                a.StatusName,
                stage,
                why,
                a.WipLimit ?? 1,
                own.Count,
                held,
                flying,
                waiting,
                notStarted,
                own.Count(c => newest.GetValueOrDefault(c.Id)?.Verdict == EfHatchCapabilityVerdict.Holds && !heldLongAgo.Contains(c.Id)),
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

        // A stable sort: the board order the rows arrived in holds inside a group.
        return rows.OrderBy(r => AmbitionStage.Group(r.Stage)).ToArray();
    }

    /// <summary>
    /// Of the capabilities given, all of which hold now, those whose unbroken
    /// run of holds began more than seven days ago. The run reaches back that
    /// far when a holds verdict that old has nothing but holds after it, so a
    /// does_not_hold or cannot_tell in between starts the run again. One query
    /// for the whole read.
    /// </summary>
    private async Task<HashSet<long>> HeldBeforeAsync(List<long> holding, CancellationToken ct)
    {
        if (holding.Count == 0) return [];

        var cutoff = time.GetUtcNow() - NewlyHoldingWindow;
        var old = await db.CapabilityVerdicts.AsNoTracking()
            .Where(v => holding.Contains(v.CapabilityId) && v.Verdict == EfHatchCapabilityVerdict.Holds && v.CreatedAt < cutoff)
            .Where(v => !db.CapabilityVerdicts.Any(w => w.CapabilityId == v.CapabilityId && w.Id > v.Id && w.Verdict != EfHatchCapabilityVerdict.Holds))
            .Select(v => v.CapabilityId)
            .Distinct()
            .ToListAsync(ct);

        return old.ToHashSet();
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

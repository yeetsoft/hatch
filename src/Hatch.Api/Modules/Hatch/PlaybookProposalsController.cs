using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// What a session proposed changing about a playbook: the submission that
/// files one and changes nothing live, and reading it back - the pending
/// queue a person works through, and the one behind any id, approved or
/// rejected included. Reading is open to an API key, the way <see cref="PlaybooksController"/>'s
/// own read is - a session that proposed a change may want to see where it
/// landed, and the one that filed it may be the same key that could not
/// have changed the row itself.
/// </summary>
/// <remarks>
/// Each action carries its own <see cref="RequireRoleAttribute"/> rather than
/// the class, because a later story adds a person-only verb beside these -
/// the group is not uniform the way <see cref="QuestionsController"/>'s is.
/// </remarks>
[ApiController]
[Route("api/hatch/playbook-proposals")]
public class PlaybookProposalsController(
    HatchContext db, ICallerIdentity caller, TimeProvider time, ModelFamilies? families = null) : ControllerBase
{
    /// <summary>
    /// Files a proposal: every change is checked against the live board and
    /// against every other pending proposal before anything is saved, so a
    /// refusal midway through a multi-change request leaves no partial row
    /// behind. Nothing live moves - not even a playbook's own <c>UpdatedAt</c>.
    /// </summary>
    [HttpPost]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public async Task<ActionResult<PlaybookProposalDto>> SubmitProposal(PlaybookProposalRequest request, CancellationToken ct)
    {
        var reason = request.Reason?.Trim() ?? "";
        if (reason.Length == 0) return BadRequest("a proposal needs a reason");
        if (reason.Length > EfHatchComment.MaxBodyLength)
            return BadRequest($"a reason is at most {EfHatchComment.MaxBodyLength} characters");

        if (request.Changes is null || request.Changes.Count == 0)
            return BadRequest("a proposal needs at least one change");

        EfHatchIssue? issue = null;
        if (!string.IsNullOrWhiteSpace(request.IssueKey))
        {
            if (!IssueKey.TryParse(request.IssueKey, out var projectKey, out var number)) return NotFound();
            issue = await db.Issues.WithKey(projectKey, number).FirstOrDefaultAsync(ct);
            if (issue is null) return NotFound();
        }

        var seen = new Dictionary<int, int>();
        var rows = new List<EfHatchPlaybook>();

        for (var i = 0; i < request.Changes.Count; i++)
        {
            var change = request.Changes[i];
            var n = i + 1;
            var prefix = $"change {n} (playbook {change.PlaybookId}): ";

            if (!change.NamesAField) return BadRequest(prefix + "names no field to change");

            var row = await db.Playbooks.AsNoTracking()
                .Include(p => p.FromStatus).Include(p => p.ToStatus).Include(p => p.Project)
                .FirstOrDefaultAsync(p => p.Id == change.PlaybookId, ct);
            if (row is null) return BadRequest(prefix + $"there is no playbook {change.PlaybookId}");

            if (seen.TryGetValue(change.PlaybookId, out var earlier))
                return BadRequest(prefix + $"playbook {change.PlaybookId} is already changed by change {earlier} of this proposal");
            seen[change.PlaybookId] = n;

            var patch = change.ToPatch();
            if (await PlaybookPatches.RefusalAsync(db, change.PlaybookId, PlaybookState.Of(row), patch, families, ct) is { } error)
                return BadRequest(prefix + error);

            var proposalIds = await db.PlaybookProposalChanges.AsNoTracking()
                .Where(c => c.PlaybookId == change.PlaybookId)
                .Select(c => c.ProposalId)
                .ToListAsync(ct);
            if (proposalIds.Count > 0)
            {
                var pendingId = await db.PlaybookProposals.AsNoTracking()
                    .Where(p => proposalIds.Contains(p.Id) && p.State == EfHatchPlaybookProposal.Pending)
                    .Select(p => p.Id)
                    .FirstOrDefaultAsync(ct);
                if (pendingId != 0)
                    return BadRequest(prefix + $"playbook {change.PlaybookId} already has a pending change, proposal {pendingId}");
            }

            rows.Add(row);
        }

        var now = time.GetUtcNow();
        var author = await caller.ActorNameAsync(ct);

        var proposal = new EfHatchPlaybookProposal
        {
            Reason = reason,
            IssueId = issue?.Id,
            Author = author,
            State = EfHatchPlaybookProposal.Pending,
            CreatedAt = now,
            Changes = request.Changes.Select((change, i) => new EfHatchPlaybookProposalChange
            {
                ProposalId = 0,
                PlaybookId = rows[i].Id,
                Before = EfHatchPlaybookProposalChange.WriteBefore(PlaybooksController.ToDto(rows[i])),
                Patch = EfHatchPlaybookProposalChange.WritePatch(change.ToPatch()),
                BasedOnUpdatedAt = rows[i].UpdatedAt,
            }).ToList(),
        };
        db.PlaybookProposals.Add(proposal);
        await db.SaveChangesAsync(ct);

        if (issue is not null)
        {
            issue.Events.Add(new EfHatchIssueEvent
            {
                Actor = author,
                Kind = EfHatchIssueEvent.PlaybookProposed,
                Payload = JsonSerializer.Serialize(new { proposalId = proposal.Id, changes = request.Changes.Count, reason }),
                At = now,
            });
            await db.SaveChangesAsync(ct);
        }

        var dto = (await PlaybookProposalProjection.ToDtosAsync(db, [proposal], ct))[0];
        return CreatedAtAction(nameof(GetProposal), new { id = proposal.Id }, dto);
    }

    /// <summary>
    /// The queue for one state, oldest first - the order a person works it in.
    /// </summary>
    /// <param name="state">
    /// Null or <c>"pending"</c> for the ones waiting on a person; <c>"approved"</c> or
    /// <c>"rejected"</c> for that one; <c>"all"</c> for every state.
    /// </param>
    [HttpGet]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public async Task<ActionResult<IReadOnlyList<PlaybookProposalDto>>> GetProposals([FromQuery] string? state, CancellationToken ct)
    {
        var effective = state ?? EfHatchPlaybookProposal.Pending;
        if (effective != "all" && !EfHatchPlaybookProposal.States.Contains(effective))
            return BadRequest($"\"{state}\" is not a state - pending, approved, rejected or all");

        var query = db.PlaybookProposals.AsNoTracking().Include(p => p.Changes).AsQueryable();
        if (effective != "all") query = query.Where(p => p.State == effective);

        var rows = await query.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).ToListAsync(ct);
        var dtos = await PlaybookProposalProjection.ToDtosAsync(db, rows, ct);
        return new ActionResult<IReadOnlyList<PlaybookProposalDto>>(dtos);
    }

    /// <summary>One proposal, with every change it carries.</summary>
    [HttpGet("{id:int}")]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public async Task<ActionResult<PlaybookProposalDto>> GetProposal(int id, CancellationToken ct)
    {
        var row = await db.PlaybookProposals.AsNoTracking().Include(p => p.Changes)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (row is null) return NotFound();

        return (await PlaybookProposalProjection.ToDtosAsync(db, [row], ct)).Single();
    }

    /// <summary>
    /// Marks every pending proposal rejected, refusing if the caller's set of ids is not
    /// exactly what is pending now - the page it read from may be stale.
    /// </summary>
    [HttpPost("reject-all")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<PlaybookProposalRejectResponse>> RejectAll(
        PlaybookProposalDecisionRequest request, CancellationToken ct)
    {
        if (await PlaybookWrites.Refusal(caller, Request, ct) is { } refusal) return refusal;

        var (pending, conflict) = await LoadExactPendingAsync(request.ProposalIds, ct);
        if (conflict is not null) return conflict;

        var now = time.GetUtcNow();
        var actor = await caller.ActorNameAsync(ct);

        foreach (var proposal in pending)
        {
            proposal.State = EfHatchPlaybookProposal.Rejected;
            proposal.DecidedAt = now;
            proposal.DecidedBy = actor;
        }

        await WriteDecisionEventsAsync(pending, EfHatchIssueEvent.PlaybookProposalRejected, now, actor, ct);

        await db.SaveChangesAsync(ct);
        return new PlaybookProposalRejectResponse(pending.Count);
    }

    /// <summary>
    /// Every row still pending, tracked - checked against <paramref name="proposalIds"/> as a set,
    /// so a caller working from a page that has since changed is refused rather than deciding the
    /// wrong rows.
    /// </summary>
    private async Task<(List<EfHatchPlaybookProposal> Pending, ObjectResult? Conflict)> LoadExactPendingAsync(
        IReadOnlyList<int>? proposalIds, CancellationToken ct)
    {
        var pending = await db.PlaybookProposals.Include(p => p.Changes)
            .Where(p => p.State == EfHatchPlaybookProposal.Pending)
            .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .ToListAsync(ct);

        var given = (proposalIds ?? []).ToHashSet();
        if (!given.SetEquals(pending.Select(p => p.Id)))
            return (pending, Conflict("the pending proposals have changed since the page was read - reload"));

        return (pending, null);
    }

    /// <summary>
    /// Writes <paramref name="kind"/> on the issue each decided proposal still names, in the same
    /// shape <see cref="SubmitProposal"/> already writes <see cref="EfHatchIssueEvent.PlaybookProposed"/>
    /// with. Does not save - the caller's own <c>SaveChangesAsync</c> covers this too.
    /// </summary>
    private async Task WriteDecisionEventsAsync(
        IReadOnlyList<EfHatchPlaybookProposal> decided, string kind, DateTimeOffset now, string actor, CancellationToken ct)
    {
        var issueIds = decided.Select(p => p.IssueId).Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        if (issueIds.Count == 0) return;

        var issues = await db.Issues.Where(i => issueIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);

        foreach (var proposal in decided)
        {
            if (proposal.IssueId is not { } issueId) continue;
            if (!issues.TryGetValue(issueId, out var issue)) continue;

            issue.Events.Add(new EfHatchIssueEvent
            {
                Actor = actor,
                Kind = kind,
                Payload = JsonSerializer.Serialize(new { proposalId = proposal.Id }),
                At = now,
            });
        }
    }
}

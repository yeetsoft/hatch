using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Files the bug the Human half's *File a bug* button asks for (HA-95) - HA-30's
/// shape, built from what the trunk build already carries rather than typed by
/// hand from the forge's page.
/// </summary>
/// <remarks>
/// The method below accepts a key, so a key may take it too -
/// gated at runtime per project, since the attribute itself cannot vary by
/// project: expediting at a project's own standing permission (turned on by
/// the project's owner, on that project alone) is not the same choice as a
/// stranger expediting one ticket, the choice <see cref="IssueExpediteController"/>
/// still keeps a person's alone. Filing itself goes through
/// <see cref="IssuesController.CreateIssueAsync"/>, so the bug's number, rank,
/// first column and <c>created</c> event are the ordinary ones - this route's
/// own work is choosing the project, writing the description, and attaching
/// the row to what it filed.
/// </remarks>
[ApiController]
public class TrunkBuildBugController(
    HatchContext db, RankService ranks, IActorDirectory actors, IssueClaims claims, ICallerIdentity caller,
    TimeProvider time) : ControllerBase
{
    /// <summary>
    /// Files the bug, or - if this row already has one - answers that bug
    /// again and files nothing: a second press, or a second call, never gets
    /// a second bug. <paramref name="request"/> is optional and, field by
    /// field, lets a person choose express, the priority, and the column the
    /// bug is born in - absent means file exactly as this route always has. A
    /// key chooses none of the three: they come off the project's auto-heal
    /// settings instead, and only where that project has auto-filing on.
    /// </summary>
    [HttpPost("api/hatch/trunk-builds/{id:long}/bug")]
    [RequireRole(PersonRole.User, AcceptKey = true)]
    public async Task<ActionResult<IssueDto>> PostBug(long id, TrunkBuildBugRequest? request, CancellationToken ct)
    {
        var row = await db.TrunkBuilds.Include(t => t.BugIssue).ThenInclude(i => i!.Project)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (row is null) return NotFound();

        if (row.BugIssue is { } already)
            return await IssueProjection.ToDtoAsync(db, actors, already, claims, time.GetUtcNow(), ct);

        // The row that offered the button a moment ago is not this call's to
        // act on once a later build has passed - the same rule the attention
        // read lists it by.
        var failing = row.Verdict == BuildVerdicts.Failed
            || (row.Verdict == BuildVerdicts.Pending && row.Failing != null);
        if (!failing) return Conflict("this trunk is not failing any more");

        var winner = await ProjectRepositoryBinding.WinnerAsync(db, row.Canonical, ct);
        if (winner is not { Id: var projectId, Key: var projectKey })
            return BadRequest($"no project binds {row.Canonical} - bind it on the Projects page");

        var key = await caller.ApiKeyAsync(ct);

        int priority;
        int preApproval;
        int? statusId;
        if (key is not null)
        {
            var project = await db.Projects.FirstAsync(p => p.Id == projectId, ct);
            if (!project.AutoHealBugEnabled)
                return BadRequest($"{projectKey} has auto-filing off - turn it on from the Projects page");

            priority = project.AutoHealBugPriority;
            preApproval = project.AutoHealBugPreApproval;
            statusId = project.AutoHealBugStatusId;
        }
        else
        {
            priority = PriorityLevels.Expedited;
            if (request?.Priority is { } priorityName && !PriorityLevels.TryParse(priorityName, out priority))
                return BadRequest($"\"{priorityName}\" is not a priority - paused, economy, low, normal, expedited or emergency");

            preApproval = (request?.Express ?? false) ? PreApprovalLevels.Express : PreApprovalLevels.None;
            statusId = request?.StatusId;
        }

        var failingChecks = EfHatchBuildCheck.ReadFailing(row.Failing);
        var description = string.Join("\n\n",
        [
            $"{row.Remote} ({row.Canonical})",
            $"Sha: {row.Sha}",
            .. failingChecks.Select(f => f.Url is { } url ? $"[{f.Name}]({url})" : f.Name),
        ]);

        var issues = new IssuesController(db, ranks, actors, claims, caller, time, new ProjectAccess(db, actors)) { ControllerContext = ControllerContext };
        var created = await issues.CreateIssueAsync(
            new IssueCreateRequest(projectId, "bug", $"Build failing on {row.Trunk}", description, null, null, null),
            priority, ct,
            preApproval: preApproval,
            statusId: statusId);

        if (created.Result is not CreatedAtActionResult { Value: IssueDto dto }) return created.Result!;

        IssueKey.TryParse(dto.Key, out var dtoProjectKey, out var number);
        row.BugIssueId = await db.Issues.WithKey(dtoProjectKey, number).Select(i => i.Id).SingleAsync(ct);
        await db.SaveChangesAsync(ct);

        return dto;
    }
}

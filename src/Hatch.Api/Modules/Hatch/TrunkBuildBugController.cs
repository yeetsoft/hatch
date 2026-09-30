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
/// No class-level attribute, so the route below takes no key scope, the same
/// cut <see cref="IssueExpediteController"/> makes and for the same reason: the
/// bug this files is expedited, and expedite is a person's call. Filing itself
/// goes through <see cref="IssuesController.CreateIssueAsync"/>, so the bug's
/// number, rank, first column and <c>created</c> event are the ordinary ones -
/// this route's own work is choosing the project, writing the description, and
/// attaching the row to what it filed.
/// </remarks>
[ApiController]
public class TrunkBuildBugController(
    HatchContext db, RankService ranks, IActorDirectory actors, IssueClaims claims, ICallerIdentity caller,
    TimeProvider time) : ControllerBase
{
    /// <summary>
    /// Files the bug, or - if this row already has one - answers that bug
    /// again and files nothing: a second press, or a second person, never gets
    /// a second bug.
    /// </summary>
    [HttpPost("api/hatch/trunk-builds/{id:long}/bug")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<IssueDto>> PostBug(long id, CancellationToken ct)
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

        // The project that binds the repository: the one that names it
        // primary, lowest project id first: else the lowest-id project that
        // binds it at all.
        var projectId = await db.ProjectRepositories.AsNoTracking()
            .Where(r => r.Canonical == row.Canonical)
            .OrderBy(r => r.SortOrder == 0 ? 0 : 1)
            .ThenBy(r => r.ProjectId)
            .Select(r => (int?)r.ProjectId)
            .FirstOrDefaultAsync(ct);
        if (projectId is not { } project)
            return BadRequest($"no project binds {row.Canonical} - bind it on the Projects page");

        var failingChecks = EfHatchBuildCheck.ReadFailing(row.Failing);
        var description = string.Join("\n\n",
        [
            $"{row.Remote} ({row.Canonical})",
            $"Sha: {row.Sha}",
            .. failingChecks.Select(f => f.Url is { } url ? $"[{f.Name}]({url})" : f.Name),
        ]);

        var issues = new IssuesController(db, ranks, actors, claims, caller, time) { ControllerContext = ControllerContext };
        var created = await issues.CreateIssueAsync(
            new IssueCreateRequest(project, "bug", $"Build failing on {row.Trunk}", description, null, null, null),
            PriorityLevels.Expedited, ct);

        if (created.Result is not CreatedAtActionResult { Value: IssueDto dto }) return created.Result!;

        IssueKey.TryParse(dto.Key, out var projectKey, out var number);
        row.BugIssueId = await db.Issues.WithKey(projectKey, number).Select(i => i.Id).SingleAsync(ct);
        await db.SaveChangesAsync(ct);

        return dto;
    }
}

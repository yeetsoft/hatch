using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Files the bug a person hands a goose finding to an agent with - HA-2516, in
/// <see cref="TrunkBuildBugController"/>'s shape, built from what the finding
/// already carries (<see cref="FindingBugs"/>).
/// </summary>
/// <remarks>
/// The method below takes no key. A key filing work for itself is the loop the
/// goose must never become: the sweep only ever says what it saw, and a person
/// decides what to do about it. Filing goes through
/// <see cref="IssuesController.CreateIssueAsync"/>, so the bug's number, rank,
/// first column and <c>created</c> event are the ordinary ones. A finding whose
/// query filter hides it from the caller reads as not found, so a caller whose
/// scope is narrowed never reaches a finding that names no project.
/// </remarks>
[ApiController]
public class GooseFindingBugController(
    HatchContext db, RankService ranks, IActorDirectory actors, IssueClaims claims, ICallerIdentity caller,
    TimeProvider time, IOptions<HatchOptions> options) : ControllerBase
{
    /// <summary>The first of the two keys the finding's advisory lock is taken on - arbitrary but fixed. Goose's own sweep lock is one bigint key, so the two key spaces never meet.</summary>
    private const int LockClass = 0x676F6F;

    /// <summary>
    /// Files the bug, or - if this finding already has one - answers that bug
    /// again and files nothing: a second press, or a second call at the same
    /// moment, never gets a second bug. A closed finding is filed like an open
    /// one. <paramref name="request"/> is optional and, field by field, lets a
    /// person choose the priority, the pre-approval level and the column the
    /// bug is born in - absent means expedited, none, the leftmost column.
    /// <c>preapproved</c> asks what filing an issue at that level asks: an owner
    /// or approver of the project. The bug goes to the finding's own project;
    /// a finding that names none needs <c>projectKey</c>.
    /// </summary>
    [HttpPost("api/goose/findings/{id:long}/bug")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<IssueDto>> PostBug(long id, GooseFindingBugRequest? request, CancellationToken ct)
    {
        var finding = await db.GooseFindings.Include(f => f.Project).Include(f => f.BugIssue).ThenInclude(i => i!.Project)
            .FirstOrDefaultAsync(f => f.Id == id, ct);
        if (finding is null) return NotFound();

        if (finding.BugIssue is { } already)
            return await IssueProjection.ToDtoAsync(db, actors, already, claims, time.GetUtcNow(), ct);

        EfHatchProject project;
        if (finding.Project is { } own)
        {
            project = own;
            if (request?.ProjectKey is { } named && !string.IsNullOrWhiteSpace(named)
                && !string.Equals(named.Trim(), own.Key, StringComparison.OrdinalIgnoreCase))
                return BadRequest($"this finding is about {own.Key}, so its bug goes there, not to {named.Trim()}");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request?.ProjectKey))
                return BadRequest("this finding names no project - say which one with projectKey");

            var wanted = request.ProjectKey.Trim().ToUpperInvariant();
            var found = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Key == wanted, ct);
            if (found is null) return BadRequest($"{wanted} is not a project you can see");
            project = found;
        }

        var priority = PriorityLevels.Expedited;
        if (request?.Priority is { } priorityName && !PriorityLevels.TryParse(priorityName, out priority))
            return BadRequest($"\"{priorityName}\" is not a priority - paused, economy, low, normal, expedited or emergency");

        var preApproval = PreApprovalLevels.None;
        if (request?.PreApproval is { } levelName && !PreApprovalLevels.TryParse(levelName, out preApproval))
            return BadRequest(IssuePreApprovalController.UnknownLevel(levelName));

        var access = new ProjectAccess(db, actors);
        if (preApproval == PreApprovalLevels.PreApproved
            && await IssuePreApprovalController.LevelRefusalAsync(db, access, project, PreApprovalLevels.None, preApproval, ct) is { } forbidden)
            return forbidden;

        // Two calls at once would both see no bug and file two. The lock is
        // Postgres's, on a connection held for the whole route, and not a
        // transaction: CreateIssueAsync retries a number collision after a
        // failed save, which an aborted transaction cannot. The in-memory
        // provider has no such thing to take.
        var locked = db.Database.IsNpgsql();
        if (locked)
        {
            await db.Database.OpenConnectionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock({LockClass}, {unchecked((int)id)})", ct);
        }

        try
        {
            // Read afresh: the call that held the lock before this one may have
            // stored its bug after this route's first read.
            var filedId = await db.GooseFindings.AsNoTracking()
                .Where(f => f.Id == id).Select(f => f.BugIssueId).FirstOrDefaultAsync(ct);
            if (filedId is { } filed)
            {
                var existing = await db.Issues.AsNoTracking().Include(i => i.Project).FirstOrDefaultAsync(i => i.Id == filed, ct);
                if (existing is null) return NotFound();
                return await IssueProjection.ToDtoAsync(db, actors, existing, claims, time.GetUtcNow(), ct);
            }

            var issues = new IssuesController(db, ranks, actors, claims, caller, time, access) { ControllerContext = ControllerContext };
            var created = await issues.CreateIssueAsync(
                new IssueCreateRequest(
                    project.Id, "bug", FindingBugs.Title(finding), FindingBugs.Description(finding, options.Value), null, null, null),
                priority, ct,
                preApproval: preApproval,
                statusId: request?.StatusId);

            if (created.Result is not CreatedAtActionResult { Value: IssueDto dto }) return created.Result!;

            // CreateIssueAsync clears the change tracker when it retries a
            // number collision, so the finding is read again before it is written.
            IssueKey.TryParse(dto.Key, out var dtoProjectKey, out var number);
            var bugId = await db.Issues.WithKey(dtoProjectKey, number).Select(i => i.Id).SingleAsync(ct);
            var stored = await db.GooseFindings.SingleAsync(f => f.Id == id, ct);
            stored.BugIssueId = bugId;
            await db.SaveChangesAsync(ct);

            return dto;
        }
        finally
        {
            if (locked)
            {
                try
                {
                    await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock({LockClass}, {unchecked((int)id)})", CancellationToken.None);
                }
                finally
                {
                    await db.Database.CloseConnectionAsync();
                }
            }
        }
    }
}

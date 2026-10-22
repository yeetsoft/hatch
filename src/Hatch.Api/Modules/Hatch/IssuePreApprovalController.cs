using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The night train's flag, and the approve press given in advance. Set by a
/// person, honoured by the dispatcher: an express issue standing in a column
/// marked <see cref="EfHatchStatus.ExpressSkips"/> is carried to the next
/// column with no session, as long as it has no unanswered question - see
/// <see cref="EfHatchIssue.PreApproval"/>.
///
/// It is a gate-passer and not a sort key, the opposite of
/// <see cref="EfHatchIssue.Priority"/>: every fold an issue already meets
/// still folds it, and this changes only whether a column marked for it needs
/// a session at all.
/// </summary>
/// <remarks>
/// <para>Its own controller for the reason <see cref="IssueExpediteController"/>
/// is, and cut the same way: no class-level attribute, so the writes below
/// inherit no scope from anything. Pre-approval decides which gates the loop
/// may pass unattended, and that is a playbook's kind of power - a key that
/// could set it could carry its own ticket through the night unattended, or
/// approve it in advance (docs/hatch.md, "The one edge that is deliberately
/// cut").</para>
///
/// <para>Setting it is gated by what it touches: express by
/// <see cref="IProjectAccess.CanMoveIntoAsync"/> against the column
/// <see cref="Columns.SendBack"/> names, pre-approved by
/// <see cref="IProjectAccess.CanApproveAsync"/> - the same two questions
/// <see cref="IssuesController.ProtectedMoveRefusalAsync"/> asks of an
/// ordinary move, asked here of the level itself.</para>
///
/// <para>Reading is open, like everything else a dispatch needs: both
/// <see cref="IssueDto"/> and <see cref="IssueCardDto"/> carry the flag.</para>
/// </remarks>
[ApiController]
[Route("api/hatch/issues")]
public class IssuePreApprovalController(
    HatchContext db, IActorDirectory actors, IssueClaims claims, ICallerIdentity caller,
    TimeProvider time, IProjectAccess access) : ControllerBase
{
    /// <summary>
    /// Set the level, by name - see <see cref="PreApprovalLevels"/> and
    /// <see cref="PreApprovalRequest"/>.
    /// </summary>
    [HttpPut("{key}/pre-approval")]
    [RequireRole(PersonRole.User)]
    public Task<ActionResult<IssueDto>> PutIssuePreApproval(string key, PreApprovalRequest request, CancellationToken ct) =>
        PreApprovalLevels.TryParse(request.Level, out var level)
            ? SetAsync(key, level, ct)
            : Task.FromResult<ActionResult<IssueDto>>(BadRequest(UnknownLevel(request.Level)));

    /// <summary>
    /// The 400's sentence, shared with <c>POST /issues</c> so a level named at
    /// filing is refused in the words <c>PUT .../pre-approval</c> uses.
    /// </summary>
    internal static string UnknownLevel(string? name) =>
        $"\"{name}\" is not a pre-approval level - none, express or preapproved";

    /// <summary>
    /// Mark it, or unmark it. The legacy two-level alias for
    /// <see cref="PutIssuePreApproval"/> - see <see cref="ExpressRequest"/>.
    /// </summary>
    /// <remarks>
    /// Nothing else on the issue is touched, and nothing else touches this: a
    /// move, a retitle and a reparent all leave the flag exactly as it was
    /// set, because it is written here and nowhere else - except at filing,
    /// where it is inherited from an express parent.
    /// </remarks>
    [HttpPut("{key}/express")]
    [RequireRole(PersonRole.User)]
    public Task<ActionResult<IssueDto>> PutIssueExpress(string key, ExpressRequest request, CancellationToken ct) =>
        SetAsync(key, request.Express ? PreApprovalLevels.Express : PreApprovalLevels.None, ct);

    /// <summary>
    /// Write the level if it differs from what the issue already holds, and
    /// leave a trail entry naming both sides. Shared by both routes above, so
    /// there is one place that decides what changing this means, and one
    /// place that gates it.
    /// </summary>
    /// <remarks>
    /// <para><see cref="NotAPerson"/> first and unconditionally: setting this
    /// is a person's call for every level, so a key or a keyless runner where
    /// the wall is off is refused before the issue is even looked up.</para>
    ///
    /// <para>Setting the value the issue already holds writes nothing, asks
    /// no further question, and does not move <c>UpdatedAt</c>, the same as
    /// every other edit in Hatch - so a control pressed twice on a card two
    /// people are looking at leaves one trail entry and not three.</para>
    ///
    /// <para>Otherwise exactly one of two gates applies. Pre-approved, on
    /// either side of the change, is an approver's own act - setting it is
    /// the approve press given in advance, and lowering from it undoes that
    /// act, so both need the same <see cref="IProjectAccess.CanApproveAsync"/>.
    /// Every other transition is none-to-express or express-to-none, gated
    /// the way an ordinary move into <see cref="Columns.SendBack"/> would be -
    /// nobody is refused where the board has no such column.</para>
    /// </remarks>
    private async Task<ActionResult<IssueDto>> SetAsync(string key, int level, CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } refusal) return refusal;

        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return NotFound();

        var issue = await db.Issues.Include(i => i.Project).WithKey(projectKey, number).FirstOrDefaultAsync(ct);
        if (issue is null) return NotFound();

        if (level != issue.PreApproval)
        {
            if (await LevelRefusalAsync(db, access, issue.Project!, issue.PreApproval, level, ct) is { } forbidden)
                return forbidden;

            var now = time.GetUtcNow();

            issue.Events.Add(new EfHatchIssueEvent
            {
                Actor = await caller.ActorNameAsync(ct),
                Kind = EfHatchIssueEvent.PreApprovalChanged,
                Payload = JsonSerializer.Serialize(new
                {
                    from = PreApprovalLevels.Name(issue.PreApproval),
                    to = PreApprovalLevels.Name(level),
                }),
                At = now,
            });

            issue.PreApproval = level;
            issue.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }

        return await IssueProjection.ToDtoAsync(db, actors, issue, claims, time.GetUtcNow(), ct);
    }

    /// <summary>
    /// The gate on changing the level from <paramref name="from"/> to
    /// <paramref name="to"/>, shared by <see cref="SetAsync"/> and by filing an
    /// issue at a level, so the two cannot come to disagree about who may set
    /// what. Null when the change is allowed - including when there is none.
    /// </summary>
    /// <remarks>
    /// Exactly one of two gates applies; see <see cref="SetAsync"/> for why.
    /// </remarks>
    internal static async Task<ObjectResult?> LevelRefusalAsync(
        HatchContext db, IProjectAccess access, EfHatchProject project, int from, int to, CancellationToken ct)
    {
        if (from == to) return null;

        if (to == PreApprovalLevels.PreApproved || from == PreApprovalLevels.PreApproved)
        {
            if (!await access.CanApproveAsync(project.Id, ct))
                return new ObjectResult($"only an owner or approver of {project.Key} may set pre-approval - it is the approve press given in advance")
                {
                    StatusCode = StatusCodes.Status403Forbidden,
                };

            return null;
        }

        var statuses = await db.Statuses.AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync(ct);
        var sendBack = Columns.SendBack(statuses);

        if (sendBack is not null && !await access.CanMoveIntoAsync(project.Id, sendBack, ct))
            return new ObjectResult($"only an owner or approver of {project.Key} may move work into \"{sendBack.Name}\"")
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };

        return null;
    }

    /// <summary>
    /// A person, not a key. Setting this is an operator's call for every
    /// level, not an agent's, for <see cref="RunnersController.NotAPerson"/>'s
    /// reason: a key that could set it could carry its own ticket through the
    /// night unattended, or approve it in advance.
    /// </summary>
    private async Task<ObjectResult?> NotAPerson(CancellationToken ct) =>
        await caller.IsProgramAsync(ct)
            ? new ObjectResult("setting an issue's pre-approval level is a person's call, not an agent's")
            {
                StatusCode = StatusCodes.Status403Forbidden,
            }
            : null;
}

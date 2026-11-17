using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Whether an issue's work prefers or requires a local model - see
/// <see cref="EfHatchIssue.LocalModel"/>. Set by a person; nothing in
/// dispatch reads it yet.
/// </summary>
/// <remarks>
/// <para>Its own controller, cut the way <see cref="IssuePreApprovalController"/>
/// is: no class-level attribute, so the write below inherits no key acceptance
/// from anything. Choosing what runs an issue's work is an operator's call, not
/// an agent's.</para>
///
/// <para>Unlike pre-approval it opens no gate and needs no approver: it changes
/// cost and quality, not which columns the loop may pass. Any person may set
/// it.</para>
///
/// <para>Reading is open: <see cref="IssueDto"/> and <see cref="IssueCardDto"/>
/// carry the level, its own and where it was inherited from.</para>
/// </remarks>
[ApiController]
[Route("api/issues")]
public class IssueLocalModelController(
    HatchContext db, IActorDirectory actors, IssueClaims claims, ICallerIdentity caller,
    TimeProvider time) : ControllerBase
{
    /// <summary>
    /// Set the level, by name - see <see cref="LocalModelLevels"/> and
    /// <see cref="LocalModelRequest"/>.
    /// </summary>
    /// <remarks>
    /// <para><see cref="NotAPerson"/> first and unconditionally, so a key, or a
    /// keyless runner where the wall is off, is refused before the issue is
    /// looked up.</para>
    ///
    /// <para>Setting the value the issue already holds writes nothing and does
    /// not move <c>UpdatedAt</c>, the same as every other edit in Hatch.</para>
    /// </remarks>
    [HttpPut("{key}/local-model")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<IssueDto>> PutIssueLocalModel(string key, LocalModelRequest request, CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } refusal) return refusal;

        if (!LocalModelLevels.TryParse(request.Level, out var level))
            return BadRequest(UnknownLevel(request.Level));

        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return NotFound();

        var issue = await db.Issues.Include(i => i.Project).WithKey(projectKey, number).FirstOrDefaultAsync(ct);
        if (issue is null) return NotFound();

        if (level != issue.LocalModel)
        {
            var now = time.GetUtcNow();

            issue.Events.Add(new EfHatchIssueEvent
            {
                Actor = await caller.ActorNameAsync(ct),
                Kind = EfHatchIssueEvent.LocalModelChanged,
                Payload = JsonSerializer.Serialize(new
                {
                    from = LocalModelLevels.Name(issue.LocalModel),
                    to = LocalModelLevels.Name(level),
                }),
                At = now,
            });

            issue.LocalModel = level;
            issue.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }

        return await IssueProjection.ToDtoAsync(db, actors, issue, claims, time.GetUtcNow(), ct);
    }

    /// <summary>
    /// The 400's sentence, shared with <c>POST /issues</c> so a level named at
    /// filing is refused in the words <c>PUT .../local-model</c> uses.
    /// </summary>
    internal static string UnknownLevel(string? name) =>
        $"\"{name}\" is not a local-model level - none, prefer or require";

    /// <summary>
    /// A person, not a key - see <see cref="IssuePreApprovalController"/>.
    /// </summary>
    private async Task<ObjectResult?> NotAPerson(CancellationToken ct) =>
        await caller.IsProgramAsync(ct)
            ? new ObjectResult("setting an issue's local-model level is a person's call, not an agent's")
            {
                StatusCode = StatusCodes.Status403Forbidden,
            }
            : null;
}

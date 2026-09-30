using System.Globalization;
using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// How many of an epic's stories may be in progress at once - see
/// <see cref="EfHatchIssue.WipLimit"/>. Holding stories to it is a separate
/// mechanism; this route is only the setting.
/// </summary>
/// <remarks>
/// Its own controller for <see cref="IssuePlaybookController"/>'s reason:
/// <see cref="IssuesController"/> accepts the <c>hatch</c> scope on every
/// route it holds, so a field here on <c>IssuePatchRequest</c> would be an
/// agent that can raise its own ceiling - the exact edge
/// <see cref="WipController"/> cuts for the board's own limits. It is cut in
/// the route rather than asked for in a prompt, because a rule an agent is
/// merely told is a rule an agent can reason its way past.
///
/// Reading is open, like everything else a dispatch needs: an agent is
/// entitled to know how many of its siblings it is competing against, and
/// <see cref="IssueDto"/> carries the field.
/// </remarks>
[ApiController]
[Route("api/hatch/issues")]
public class IssueWipLimitController(
    HatchContext db, IActorDirectory actors, IssueClaims claims, ICallerIdentity caller, TimeProvider time) : ControllerBase
{
    /// <summary>
    /// Set, change or clear the limit. Null leaves it alone and <c>""</c> - or
    /// whitespace, which trims to it - clears it back to
    /// <see cref="IssueWipLimitRequest.DefaultLimit"/>.
    /// </summary>
    [HttpPatch("{key}/wip")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<IssueDto>> PatchIssueWipLimit(
        string key, IssueWipLimitRequest request, CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } refusal) return refusal;

        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return NotFound();

        var issue = await db.Issues.Include(i => i.Project).WithKey(projectKey, number).FirstOrDefaultAsync(ct);
        if (issue is null) return NotFound();

        if (issue.Type != "epic")
            return BadRequest($"only an epic takes a limit on its stories at once - {key} is a {issue.Type}");

        if (request.Limit is null)
            return await IssueProjection.ToDtoAsync(db, actors, issue, claims, time.GetUtcNow(), ct);

        var trimmed = request.Limit.Trim();
        int? limit;
        if (trimmed.Length == 0)
        {
            limit = null;
        }
        else if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 1)
        {
            limit = parsed;
        }
        else
        {
            return BadRequest($"stories at once is a whole number of one or more - not \"{trimmed}\"");
        }

        if (limit != issue.WipLimit)
        {
            var actor = await caller.ActorNameAsync(ct);
            issue.Events.Add(new EfHatchIssueEvent
            {
                Actor = actor,
                Kind = EfHatchIssueEvent.WipLimitChanged,
                Payload = JsonSerializer.Serialize(new { from = issue.WipLimit, to = limit }),
                At = time.GetUtcNow(),
            });

            issue.WipLimit = limit;
            issue.UpdatedAt = time.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }

        return await IssueProjection.ToDtoAsync(db, actors, issue, claims, time.GetUtcNow(), ct);
    }

    // ---- The one narrowing ----

    /// <summary>
    /// A person, not a key - see the type's own remark. Checked in the action
    /// as well as declared in the attribute, for <see cref="WipController.NotAPerson"/>'s
    /// reason: <c>RoleGate</c> is dormant wherever the wall is off, and a
    /// keyless runner there is a program too.
    /// </summary>
    private async Task<ObjectResult?> NotAPerson(CancellationToken ct) =>
        await caller.IsProgramAsync(ct)
            ? new ObjectResult("how many of an epic's stories run at once is the operator's to set - not an agent's")
            {
                StatusCode = StatusCodes.Status403Forbidden,
            }
            : null;
}

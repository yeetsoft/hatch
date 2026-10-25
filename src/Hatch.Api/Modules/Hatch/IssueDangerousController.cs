using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Why running an issue - and everything beneath it - is dangerous. See
/// <see cref="EfHatchIssue.Dangerous"/>.
/// </summary>
/// <remarks>
/// Its own controller, and open to a key the same way <see
/// cref="IssueRunnerTagsController"/> is: this is a statement about which runner
/// may pick the ticket up, not a power over the board, and the flag only ever
/// narrows who runs it. No type restriction, no claim gate, no lock - any issue
/// may carry a reason, any key may set it at any time. `PATCH`, create and the
/// bulk endpoint carry nothing for it; this route is the only way in or out.
/// </remarks>
[ApiController]
[Route("api/hatch/issues")]
public class IssueDangerousController(
    HatchContext db, IActorDirectory actors, IssueClaims claims, ICallerIdentity caller,
    TimeProvider time) : ControllerBase
{
    /// <summary>
    /// Set the reason, trimmed; empty or whitespace clears it, and an absent
    /// or null reason is refused so clearing is something the caller says.
    /// Setting the value the issue already holds writes nothing and does not
    /// move <c>UpdatedAt</c>, the same as every other edit in Hatch.
    /// </summary>
    [HttpPut("{key}/dangerous")]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public async Task<ActionResult<IssueDto>> PutIssueDangerous(string key, DangerousRequest request, CancellationToken ct)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return NotFound();

        var issue = await db.Issues.Include(i => i.Project).WithKey(projectKey, number).FirstOrDefaultAsync(ct);
        if (issue is null) return NotFound();

        if (request.Reason is null) return BadRequest("reason is required - send \"\" to clear");

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (reason is { Length: > DangerousRequest.MaxLength })
            return BadRequest($"a dangerous reason is at most {DangerousRequest.MaxLength} characters");

        if (reason != issue.Dangerous)
        {
            var now = time.GetUtcNow();

            issue.Events.Add(new EfHatchIssueEvent
            {
                Actor = await caller.ActorNameAsync(ct),
                Kind = EfHatchIssueEvent.DangerousChanged,
                Payload = JsonSerializer.Serialize(new { from = issue.Dangerous, to = reason }),
                At = now,
            });

            issue.Dangerous = reason;
            issue.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }

        return await IssueProjection.ToDtoAsync(db, actors, issue, claims, time.GetUtcNow(), ct);
    }
}

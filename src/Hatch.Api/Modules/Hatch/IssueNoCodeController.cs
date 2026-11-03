using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Why an issue finished with no code. See <see cref="EfHatchIssue.NoCode"/>.
/// </summary>
/// <remarks>
/// Its own controller, and open to a key the same way <see
/// cref="IssueDangerousController"/> is: it has the reach of recording a pull
/// request through `PATCH`, which is also key-open, and gives a key no power
/// over the board. Any issue may carry one, but not once it has a branch or a
/// pull request - those show it did finish with code. `PATCH`, create and the
/// bulk endpoint carry nothing for it; this route is the only way in, and
/// <see cref="NoCode"/> holds the rules that clear it.
/// </remarks>
[ApiController]
[Route("api/hatch/issues")]
public class IssueNoCodeController(
    HatchContext db, IActorDirectory actors, IssueClaims claims, ICallerIdentity caller,
    TimeProvider time) : ControllerBase
{
    /// <summary>
    /// Set the reason, trimmed; empty or whitespace clears it, and an absent
    /// or null reason is refused so clearing is something the caller says.
    /// Setting the value the issue already holds writes nothing and does not
    /// move <c>UpdatedAt</c>, the same as every other edit in Hatch.
    /// </summary>
    [HttpPut("{key}/no-code")]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public async Task<ActionResult<IssueDto>> PutIssueNoCode(string key, NoCodeRequest request, CancellationToken ct)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return NotFound();

        var issue = await db.Issues
            .Include(i => i.Project).ThenInclude(p => p!.Repositories)
            .Include(i => i.MergeChecks)
            .WithKey(projectKey, number).FirstOrDefaultAsync(ct);
        if (issue is null) return NotFound();

        if (request.Reason is null) return BadRequest("reason is required - send \"\" to clear");

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (reason is { Length: > NoCodeRequest.MaxLength })
            return BadRequest($"a no-code reason is at most {NoCodeRequest.MaxLength} characters");

        if (reason is not null)
        {
            if (!string.IsNullOrWhiteSpace(issue.PullRequestUrl))
                return Conflict($"{key} has a pull request recorded - it did not finish with no code");

            var branch = ReviewWork.Counted(issue.Project!.Repositories.ToList(), issue.MergeChecks.ToList())
                .FirstOrDefault(m => m.Verdict is MergeVerdicts.Clean or MergeVerdicts.Conflicted or MergeVerdicts.Ambiguous);
            if (branch is not null)
                return Conflict($"{key} has a branch on origin ({branch.Canonical}, {branch.Verdict}) - push it and record a pull request instead");
        }

        if (reason != issue.NoCode)
        {
            var now = time.GetUtcNow();

            issue.Events.Add(new EfHatchIssueEvent
            {
                Actor = await caller.ActorNameAsync(ct),
                Kind = EfHatchIssueEvent.NoCodeChanged,
                Payload = JsonSerializer.Serialize(new { from = issue.NoCode, to = reason }),
                At = now,
            });

            issue.NoCode = reason;
            issue.NoCodeAt = reason is null ? null : now;
            issue.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }

        return await IssueProjection.ToDtoAsync(db, actors, issue, claims, time.GetUtcNow(), ct);
    }
}

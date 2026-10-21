using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Where a runner says the merge train has just brought an issue's branch
/// current, so the board keeps the train's head on that row rather than
/// re-electing it from scratch on the next read. See
/// <see cref="EfHatchIssue.BroughtCurrentAt"/>.
///
/// A key may write it: the caller is a runner, and a fact only a person could
/// enter would be one nobody ever entered.
/// </summary>
[ApiController]
[Route("api/hatch/issues/{key}/brought-current")]
[RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
public class BroughtCurrentController(
    HatchContext db, ICallerIdentity caller, TimeProvider time) : ControllerBase
{
    /// <summary>
    /// Marks the issue as the one the train last brought current, and at which
    /// trunk sha.
    /// </summary>
    /// <remarks>
    /// An event is written on every call, with no dedup against a repeat: the
    /// train only reports an update it actually performed, so every valid call
    /// is news. A row outside a column that merges pull requests is refused -
    /// the train has no business with it.
    /// </remarks>
    [HttpPut]
    public async Task<IActionResult> PutBroughtCurrent(
        string key, BroughtCurrentRequest request, CancellationToken ct)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return NotFound();

        var issue = await db.Issues.WithKey(projectKey, number).FirstOrDefaultAsync(ct);
        if (issue is null) return NotFound();

        var status = await db.Statuses.AsNoTracking().FirstOrDefaultAsync(s => s.Id == issue.StatusId, ct);
        if (status is null || !status.MergesPullRequest)
            return Conflict("this issue is not in a column that merges pull requests, so the train has not brought it current");

        var trunk = request.Trunk?.Trim() ?? "";
        if (trunk.Length == 0) return BadRequest("a mark names the trunk the branch was brought current from");
        if (trunk.Length > EfHatchMergeCheck.MaxRefLength)
            return BadRequest($"a trunk is at most {EfHatchMergeCheck.MaxRefLength} characters");

        var trunkSha = request.TrunkSha?.Trim() ?? "";
        if (trunkSha.Length == 0) return BadRequest("a mark names the sha the trunk stood at");
        if (trunkSha.Length > EfHatchMergeCheck.MaxShaLength)
            return BadRequest($"a sha is at most {EfHatchMergeCheck.MaxShaLength} characters");

        var now = time.GetUtcNow();
        var actor = await caller.ActorNameAsync(ct);

        issue.BroughtCurrentAt = now;
        issue.BroughtCurrentTrunkSha = trunkSha;
        issue.Events.Add(new EfHatchIssueEvent
        {
            Actor = actor,
            Kind = EfHatchIssueEvent.BroughtCurrent,
            Payload = JsonSerializer.Serialize(new { trunk, trunkSha, runner = request.Runner?.Trim() }),
            At = now,
        });

        await db.SaveChangesAsync(ct);

        return NoContent();
    }
}

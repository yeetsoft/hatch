using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Where a check session writes its verdict about an issue - see
/// <see cref="EfHatchVerdict"/> for what the row is and why it carries real
/// navigation properties to the playbook and status it was given under.
/// </summary>
/// <remarks>
/// Open to a key and a person, the way <see cref="MergeCheckController"/> is:
/// the caller is whatever ran the check, and a verdict only a person could
/// enter would be one an agent session could never record for itself.
///
/// There is no <c>GET</c>. Nothing reads this row back over HTTP -
/// HA-555's apply endpoint finds it by a direct EF query and alone sets
/// <see cref="EfHatchVerdict.AppliedAt"/>, so this endpoint answers
/// <c>204 No Content</c> with no read DTO.
/// </remarks>
[ApiController]
[Route("api/hatch/issues/{key}/verdicts")]
[RequireRole(PersonRole.User, AcceptKey = true)]
public class VerdictsController(HatchContext db, ICallerIdentity caller, TimeProvider time) : ControllerBase
{
    /// <summary>
    /// Records (or replaces, for the issue's live claim token) the verdict a
    /// check session reached about this issue, and writes a
    /// <see cref="EfHatchIssueEvent.VerdictRecorded"/> event.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> PostVerdict(string key, VerdictCreateRequest request, CancellationToken ct)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return NotFound();

        var issue = await db.Issues.WithKey(projectKey, number).FirstOrDefaultAsync(ct);
        if (issue is null) return NotFound();

        var playbook = issue.ClaimPlaybookId is null
            ? null
            : await db.Playbooks.FirstOrDefaultAsync(p => p.Id == issue.ClaimPlaybookId.Value, ct);
        if (playbook is null || !playbook.IsCheck)
            return BadRequest($"a verdict needs a check playbook covering {key} where it stands, and none does");

        var verdict = request.Verdict?.Trim() ?? "";
        if (!CheckVerdicts.All.Contains(verdict))
            return BadRequest($"\"{request.Verdict}\" is not a verdict - it is \"pass\" or \"fail\"");

        var findings = request.Findings?.Trim();
        if (string.IsNullOrEmpty(findings)) findings = null;
        if (findings is { Length: > EfHatchComment.MaxBodyLength })
            return BadRequest($"findings are at most {EfHatchComment.MaxBodyLength} characters");

        var now = time.GetUtcNow();
        var actor = await caller.ActorNameAsync(ct);
        var claimToken = issue.ClaimToken!.Value;

        var row = await db.Verdicts.FirstOrDefaultAsync(
            v => v.IssueId == issue.Id && v.ClaimToken == claimToken, ct);

        if (row is null)
        {
            row = new EfHatchVerdict
            {
                IssueId = issue.Id,
                PlaybookId = playbook.Id,
                StatusId = issue.StatusId,
                Verdict = verdict,
                Findings = findings,
                Author = actor,
                ClaimToken = claimToken,
                CreatedAt = now,
            };
            db.Verdicts.Add(row);
        }

        row.PlaybookId = playbook.Id;
        row.StatusId = issue.StatusId;
        row.Verdict = verdict;
        row.Findings = findings;
        row.Author = actor;
        row.ClaimToken = claimToken;
        row.CreatedAt = now;

        issue.Events.Add(new EfHatchIssueEvent
        {
            Actor = actor,
            Kind = EfHatchIssueEvent.VerdictRecorded,
            Payload = JsonSerializer.Serialize(new { verdict, playbookId = issue.ClaimPlaybookId }),
            At = now,
        });

        await db.SaveChangesAsync(ct);

        return NoContent();
    }
}

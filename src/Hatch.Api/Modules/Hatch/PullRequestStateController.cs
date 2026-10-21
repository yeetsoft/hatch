using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Where a runner writes down what the forge says of an issue's recorded pull
/// request - whether it can merge, what its reviews add up to, and the commit
/// those were read at. See <see cref="EfHatchIssue.PullRequestMergeState"/>.
///
/// A key may write it, which is the whole point: the caller is a runner, and a
/// state only a person could enter would be one nobody ever entered. It is a
/// fact any runner reads the same way from the forge, and a wrong one stalls
/// nothing - nothing acts on it yet.
/// </summary>
/// <remarks>
/// Nothing here looks at the issue's column. A state read before anybody moved
/// the ticket is still true.
/// </remarks>
[ApiController]
[Route("api/hatch/issues/{key}/pull-request-state")]
[RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
public class PullRequestStateController(
    HatchContext db, ICallerIdentity caller, TimeProvider time) : ControllerBase
{
    /// <summary>
    /// Replaces the state kept for the pull request recorded on this issue.
    /// </summary>
    /// <remarks>
    /// An event is written when the merge state, the review decision or the head
    /// sha differs from what was stored, and the first read counts. A state that
    /// repeats the stored one refreshes the time it was read and writes nothing,
    /// so a poll that looks every interval leaves one entry rather than a
    /// hundred - and does not move the issue's <c>UpdatedAt</c>, which would
    /// reorder the board on every tick. The time the head arrived moves only with
    /// the sha: a new merge state or review decision on the same head leaves it.
    /// </remarks>
    [HttpPut]
    public async Task<IActionResult> PutPullRequestState(
        string key, PullRequestStateRequest request, CancellationToken ct)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return NotFound();

        var issue = await db.Issues.WithKey(projectKey, number).FirstOrDefaultAsync(ct);
        if (issue is null) return NotFound();

        var recorded = issue.PullRequestUrl?.Trim();
        if (string.IsNullOrEmpty(recorded))
            return Conflict("no pull request is recorded on this issue, so there is no state to keep");

        var url = request.Url?.Trim() ?? "";
        if (!string.Equals(url, recorded, StringComparison.Ordinal))
            return Conflict($"\"{url}\" is not the pull request recorded on this issue");

        var mergeState = request.MergeState?.Trim() ?? "";
        if (!MergeStates.All.Contains(mergeState))
            return BadRequest($"a merge state is one of {string.Join(", ", MergeStates.All)}");

        var reviewDecision = request.ReviewDecision?.Trim() ?? "";
        if (!ReviewDecisions.All.Contains(reviewDecision))
            return BadRequest($"a review decision is one of {string.Join(", ", ReviewDecisions.All)}");

        var headSha = request.HeadSha?.Trim() ?? "";
        if (headSha.Length == 0) return BadRequest("a state names the sha the pull request's head stood at");
        if (headSha.Length > EfHatchMergeCheck.MaxShaLength)
            return BadRequest($"a sha is at most {EfHatchMergeCheck.MaxShaLength} characters");

        var now = time.GetUtcNow();
        var actor = await caller.ActorNameAsync(ct);

        var changed = issue.PullRequestMergeState != mergeState
            || issue.PullRequestReviewDecision != reviewDecision
            || issue.PullRequestHeadSha != headSha;
        var first = issue.PullRequestMergeState is null
            && issue.PullRequestReviewDecision is null
            && issue.PullRequestHeadSha is null;
        var from = first
            ? null
            : Side(issue.PullRequestMergeState, issue.PullRequestReviewDecision, issue.PullRequestHeadSha);

        var headMoved = issue.PullRequestHeadSha != headSha;

        issue.PullRequestMergeState = mergeState;
        issue.PullRequestReviewDecision = reviewDecision;
        issue.PullRequestHeadSha = headSha;
        if (headMoved) issue.PullRequestHeadSince = now;
        issue.PullRequestReadAt = now;

        if (changed)
        {
            issue.Events.Add(new EfHatchIssueEvent
            {
                Actor = actor,
                Kind = EfHatchIssueEvent.PullRequestStateChanged,
                Payload = JsonSerializer.Serialize(new { from, to = Side(mergeState, reviewDecision, headSha) }),
                At = now,
            });
            issue.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>One side of a change: the three things read. Spelled out rather than handed a record, as every payload in this module is - a record would serialise with capitals.</summary>
    private static object Side(string? mergeState, string? reviewDecision, string? headSha) =>
        new { mergeState, reviewDecision, headSha };
}

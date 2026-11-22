using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// A pull request that is written and waiting to be opened. See
/// <see cref="EfHatchIssue.WaitingPrTitle"/>.
/// </summary>
/// <remarks>
/// Its own controller, shaped like <see cref="IssueNoCodeController"/>: writing
/// is open to a key, since a runner that could not open the pull request where
/// it worked is the one that writes it, and gives a key no power over the
/// board. Removing is a person's alone. <c>PATCH</c>, create and the bulk
/// endpoint carry nothing for it, and <see cref="WaitingPullRequest"/> holds
/// the rule that clears it when a url is recorded.
/// </remarks>
[ApiController]
[Route("api/issues")]
public class IssueWaitingPullRequestController(
    HatchContext db, IActorDirectory actors, IssueClaims claims, ICallerIdentity caller,
    TimeProvider time) : ControllerBase
{
    /// <summary>
    /// Store the one waiting pull request, replacing any already there. The
    /// caller is the runner. Writing what the issue already holds writes
    /// nothing and does not move <c>UpdatedAt</c>.
    /// </summary>
    [HttpPut("{key}/waiting-pull-request")]
    [RequireRole(PersonRole.User, AcceptKey = true)]
    public async Task<ActionResult<IssueDto>> PutWaitingPullRequest(
        string key, WaitingPullRequestRequest request, CancellationToken ct)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return NotFound();

        var issue = await db.Issues.WithKey(projectKey, number).FirstOrDefaultAsync(ct);
        if (issue is null) return NotFound();

        var title = request.Title?.Trim();
        var branch = request.Branch?.Trim();
        var sha = request.Sha?.Trim();
        var why = request.Why?.Trim();
        var body = request.Body ?? "";
        var remote = string.IsNullOrWhiteSpace(request.Remote) ? null : request.Remote.Trim();

        if (string.IsNullOrEmpty(title)) return BadRequest("title is required");
        if (title.Length > WaitingPullRequestRequest.MaxTitleLength)
            return BadRequest($"a pull request title is at most {WaitingPullRequestRequest.MaxTitleLength} characters");
        if (string.IsNullOrEmpty(branch)) return BadRequest("branch is required");
        if (string.IsNullOrEmpty(sha)) return BadRequest("sha is required");
        if (string.IsNullOrEmpty(why)) return BadRequest("why is required - say why it could not be opened where it was written");
        if (why.Length > WaitingPullRequestRequest.MaxWhyLength)
            return BadRequest($"why is at most {WaitingPullRequestRequest.MaxWhyLength} characters");
        if (body.Length > PullRequestEnvelopeWriter.GitHubBodyLimit)
            return BadRequest($"a pull request body is at most {PullRequestEnvelopeWriter.GitHubBodyLimit} characters");

        if (!string.IsNullOrWhiteSpace(issue.PullRequestUrl))
            return Conflict($"{key} has a pull request recorded - there is none waiting to be opened");

        var same = issue.WaitingPrTitle == title && issue.WaitingPrBody == body && issue.WaitingPrBranch == branch
            && issue.WaitingPrSha == sha && issue.WaitingPrRemote == remote && issue.WaitingPrWhy == why;

        if (!same)
        {
            var now = time.GetUtcNow();
            var actor = await caller.ActorNameAsync(ct);

            issue.Events.Add(new EfHatchIssueEvent
            {
                Actor = actor,
                Kind = EfHatchIssueEvent.WaitingPullRequestChanged,
                Payload = JsonSerializer.Serialize(new
                {
                    from = WaitingPullRequest.Side(issue),
                    to = new { title, branch, sha },
                }),
                At = now,
            });

            issue.WaitingPrTitle = title;
            issue.WaitingPrBody = body;
            issue.WaitingPrBranch = branch;
            issue.WaitingPrSha = sha;
            issue.WaitingPrRemote = remote;
            issue.WaitingPrRunner = actor;
            issue.WaitingPrWhy = why;
            issue.WaitingPrAt = now;
            issue.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }

        return await IssueProjection.ToDtoAsync(db, actors, issue, claims, time.GetUtcNow(), ct);
    }

    /// <summary>
    /// Remove the waiting pull request. A person's alone: a key gets <c>403</c>.
    /// Nothing to remove is <c>204</c> too, with no event.
    /// </summary>
    [HttpDelete("{key}/waiting-pull-request")]
    [RequireRole(PersonRole.User)]
    public async Task<IActionResult> DeleteWaitingPullRequest(string key, CancellationToken ct)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return NotFound();

        var issue = await db.Issues.WithKey(projectKey, number).FirstOrDefaultAsync(ct);
        if (issue is null) return NotFound();

        if (WaitingPullRequest.Has(issue))
        {
            var now = time.GetUtcNow();

            issue.Events.Add(new EfHatchIssueEvent
            {
                Actor = await caller.ActorNameAsync(ct),
                Kind = EfHatchIssueEvent.WaitingPullRequestChanged,
                Payload = JsonSerializer.Serialize(new { from = WaitingPullRequest.Side(issue), to = (object?)null }),
                At = now,
            });

            WaitingPullRequest.Clear(issue);
            issue.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }

        return NoContent();
    }
}

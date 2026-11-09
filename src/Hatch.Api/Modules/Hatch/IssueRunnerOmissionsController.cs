using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The runners an issue - and everything beneath it - is closed to. See <see
/// cref="EfHatchIssueRunnerOmission"/>.
/// </summary>
/// <remarks>
/// Its own controller, and open to a key the same way <see
/// cref="IssueDangerousController"/> is: this is a statement about which runner
/// may pick the ticket up, not a power over the board, and an omission only ever
/// narrows who runs it. No type restriction, no claim gate, no lock. `PATCH`,
/// create and the bulk endpoint carry nothing for it; these routes are the only
/// way in or out, and they touch only the issue's own rows - an omission an
/// ancestor carries is lifted on the ancestor.
/// </remarks>
[ApiController]
[Route("api/hatch/issues")]
public class IssueRunnerOmissionsController(
    HatchContext db, IActorDirectory actors, IssueClaims claims, ICallerIdentity caller,
    TimeProvider time) : ControllerBase
{
    /// <summary>
    /// Omit a runner from the issue, or rewrite the reason it is omitted. The
    /// runner name and the reason are trimmed; an empty or absent reason is
    /// stored as none. Sending what the issue already holds writes nothing and
    /// does not move <c>UpdatedAt</c>, the same as every other edit in Hatch.
    /// </summary>
    [HttpPut("{key}/omitted-runners/{runner}")]
    [RequireRole(PersonRole.User, AcceptKey = true)]
    public async Task<ActionResult<IssueDto>> PutOmittedRunner(
        string key, string runner, OmitRunnerRequest request, CancellationToken ct)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return NotFound();

        var issue = await db.Issues.Include(i => i.Project).WithKey(projectKey, number).FirstOrDefaultAsync(ct);
        if (issue is null) return NotFound();

        var runnerName = Uri.UnescapeDataString(runner).Trim();
        if (runnerName.Length == 0) return BadRequest("a runner name is required");
        if (runnerName.Length > EfHatchRunner.MaxNameLength)
            return BadRequest($"a runner name is at most {EfHatchRunner.MaxNameLength} characters");

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (reason is { Length: > OmitRunnerRequest.MaxLength })
            return BadRequest($"an omission reason is at most {OmitRunnerRequest.MaxLength} characters");

        var existing = await db.RunnerOmissions
            .FirstOrDefaultAsync(o => o.IssueId == issue.Id && o.RunnerName == runnerName, ct);

        if (existing is null || existing.Reason != reason)
        {
            var now = time.GetUtcNow();
            var actor = await caller.ActorNameAsync(ct);
            var was = existing?.Reason;

            if (existing is null)
            {
                db.RunnerOmissions.Add(new EfHatchIssueRunnerOmission
                {
                    IssueId = issue.Id,
                    RunnerName = runnerName,
                    Reason = reason,
                    By = actor,
                    At = now,
                });
            }
            else
            {
                existing.Reason = reason;
                existing.By = actor;
                existing.At = now;
            }

            issue.Events.Add(new EfHatchIssueEvent
            {
                Actor = actor,
                Kind = EfHatchIssueEvent.RunnerOmitted,
                Payload = JsonSerializer.Serialize(new { runner = runnerName, reason, from = was }),
                At = now,
            });

            issue.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }

        return await IssueProjection.ToDtoAsync(db, actors, issue, claims, time.GetUtcNow(), ct);
    }

    /// <summary>
    /// Lift the issue's own omission of a runner. An omission the issue only
    /// inherits is not its to lift: the answer names the ancestor carrying it
    /// and leaves that row alone.
    /// </summary>
    [HttpDelete("{key}/omitted-runners/{runner}")]
    [RequireRole(PersonRole.User, AcceptKey = true)]
    public async Task<ActionResult<IssueDto>> DeleteOmittedRunner(string key, string runner, CancellationToken ct)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return NotFound();

        var issue = await db.Issues.Include(i => i.Project).WithKey(projectKey, number).FirstOrDefaultAsync(ct);
        if (issue is null) return NotFound();

        var runnerName = Uri.UnescapeDataString(runner).Trim();
        var row = await db.RunnerOmissions
            .FirstOrDefaultAsync(o => o.IssueId == issue.Id && o.RunnerName == runnerName, ct);

        if (row is null)
        {
            var tree = await PriorityTree.ForAsync(db, ct);
            var inherited = tree.EffectiveOmittedRunners(issue.Id).FirstOrDefault(o => o.Runner == runnerName);

            return inherited?.FromKey is { } ancestor
                ? NotFound($"{key} has no own omission for \"{runnerName}\" - it is inherited from {ancestor}")
                : NotFound();
        }

        var now = time.GetUtcNow();

        db.RunnerOmissions.Remove(row);
        issue.Events.Add(new EfHatchIssueEvent
        {
            Actor = await caller.ActorNameAsync(ct),
            Kind = EfHatchIssueEvent.RunnerOmissionLifted,
            Payload = JsonSerializer.Serialize(new { runner = runnerName }),
            At = now,
        });

        issue.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        return await IssueProjection.ToDtoAsync(db, actors, issue, claims, time.GetUtcNow(), ct);
    }
}

using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Which runners may implement an issue's own work - its own list, never a
/// value inherited from the project. See <see cref="EfHatchIssue.RunnerTags"/>.
/// </summary>
/// <remarks>
/// Its own controller, and open to a key the same way <see
/// cref="IssueOnePrController"/> is: this is a statement about which runner
/// may pick the ticket up, not a power over the board, so a key is given
/// nothing it could not already do by filing the ticket tagged that way in
/// the first place. No type restriction, no claim gate, no lock - any issue
/// may carry tags, any key may set them at any time. `PATCH`, create and the
/// bulk endpoint carry nothing for it; this route is the only way in or out.
/// </remarks>
[ApiController]
[Route("api/hatch/issues")]
public class IssueRunnerTagsController(
    HatchContext db, IActorDirectory actors, IssueClaims claims, ICallerIdentity caller,
    TimeProvider time) : ControllerBase
{
    /// <summary>
    /// Replaces the issue's own list whole - see <see cref="RunnerTags.Normalise"/>
    /// for the rule a tag answers to. Re-sending the list it already holds
    /// writes nothing and does not move <c>UpdatedAt</c>, the same as every
    /// other edit in Hatch.
    /// </summary>
    [HttpPut("{key}/runner-tags")]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public async Task<ActionResult<IssueDto>> PutIssueRunnerTags(string key, IssueRunnerTagsWriteRequest request, CancellationToken ct)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return NotFound();

        var issue = await db.Issues.Include(i => i.Project).WithKey(projectKey, number).FirstOrDefaultAsync(ct);
        if (issue is null) return NotFound();

        if (request.Tags is null) return BadRequest("tags is required - send [] to clear");

        var tagError = RunnerTags.Normalise(request.Tags, out var normalised);
        if (tagError is not null) return BadRequest(tagError);

        var current = RunnerTagColumn.Read(issue.RunnerTags);
        if (!current.SequenceEqual(normalised))
        {
            var now = time.GetUtcNow();

            issue.Events.Add(new EfHatchIssueEvent
            {
                Actor = await caller.ActorNameAsync(ct),
                Kind = EfHatchIssueEvent.RunnerTagsChanged,
                Payload = JsonSerializer.Serialize(new { from = current, to = normalised }),
                At = now,
            });

            issue.RunnerTags = RunnerTagColumn.Write(normalised);
            issue.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }

        return await IssueProjection.ToDtoAsync(db, actors, issue, claims, time.GetUtcNow(), ct);
    }
}

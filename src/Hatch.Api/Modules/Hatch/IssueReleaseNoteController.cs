using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The release note on an issue - the sentence or two the changelog shows for it
/// once it is finished. See <see cref="EfHatchIssue.ReleaseNote"/>.
/// </summary>
/// <remarks>
/// <para>Its own controller, and the only way in: <see cref="IssuePatchRequest"/>
/// carries no field for it. A patch is open to any key with no claim check, so a
/// second way in there would leave the rule below a way round it.</para>
///
/// <para>A person may write it at any time. A program caller - a key, or a
/// keyless runner that named itself - may write it only under the issue's live
/// claim, by sending the claim's token, so a session can only write the note of
/// the ticket it is working on. The two refusals are
/// <see cref="WorkController.VerdictWork"/>'s, and a lapsed claim counts as no
/// claim at all.</para>
///
/// <para>One second claim is accepted, and no other: the live claim of the
/// changelog task the board recorded for the issue
/// (<see cref="EfHatchChangelogTask"/>), which writes the note of the issue it
/// was filed for when nobody wrote it earlier. The event then carries
/// <c>via</c>, the task's key.</para>
/// </remarks>
[ApiController]
[Route("api/issues")]
public class IssueReleaseNoteController(
    HatchContext db, IActorDirectory actors, IssueClaims claims, ICallerIdentity caller,
    TimeProvider time) : ControllerBase
{
    /// <summary>
    /// Set the note, trimmed; null, empty or whitespace clears it. Setting the
    /// value the issue already holds writes nothing and does not move
    /// <c>UpdatedAt</c>, the same as every other edit in Hatch - so a control
    /// pressed twice leaves one trail entry and not two.
    /// </summary>
    [HttpPut("{key}/release-note")]
    [RequireRole(PersonRole.User, AcceptKey = true)]
    public async Task<ActionResult<IssueDto>> PutIssueReleaseNote(
        string key, ReleaseNoteRequest request, [FromQuery] Guid? heldToken, CancellationToken ct)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return NotFound();

        var issue = await db.Issues.Include(i => i.Project).WithKey(projectKey, number).FirstOrDefaultAsync(ct);
        if (issue is null) return NotFound();

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note is { Length: > ReleaseNoteRequest.MaxLength })
            return BadRequest($"a release note is at most {ReleaseNoteRequest.MaxLength} characters");

        var now = time.GetUtcNow();

        string? via = null;
        if (await caller.IsProgramAsync(ct))
        {
            var own = claims.IsLive(ClaimSnapshot.Of(issue), now) && issue.ClaimToken == heldToken;
            if (!own)
            {
                via = await ChangelogTaskHoldingAsync(issue, heldToken, now, ct);
                if (via is null)
                {
                    if (!claims.IsLive(ClaimSnapshot.Of(issue), now))
                        return Conflict($"{key} holds no live claim, so there is nothing for this token to match");
                    return Conflict($"{key}'s live claim does not match this token");
                }
            }
        }

        if (note != issue.ReleaseNote)
        {
            issue.Events.Add(new EfHatchIssueEvent
            {
                Actor = await caller.ActorNameAsync(ct),
                Kind = EfHatchIssueEvent.ReleaseNoteChanged,
                Payload = via is null
                    ? JsonSerializer.Serialize(new { from = issue.ReleaseNote, to = note })
                    : JsonSerializer.Serialize(new { from = issue.ReleaseNote, to = note, via }),
                At = now,
            });

            issue.ReleaseNote = note;
            issue.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }

        return await IssueProjection.ToDtoAsync(db, actors, issue, claims, now, ct);
    }

    /// <summary>
    /// The key of the changelog task the board recorded for
    /// <paramref name="issue"/> whose live claim <paramref name="heldToken"/>
    /// is, or null. The one claim besides the issue's own that may write its
    /// note - see <see cref="ChangelogTasks"/>.
    /// </summary>
    private async Task<string?> ChangelogTaskHoldingAsync(EfHatchIssue issue, Guid? heldToken, DateTimeOffset now, CancellationToken ct)
    {
        if (heldToken is null) return null;

        var tasks = await db.ChangelogTasks.AsNoTracking()
            .Include(t => t.Issue).ThenInclude(i => i!.Project)
            .Where(t => t.SourceIssueId == issue.Id)
            .ToListAsync(ct);
        var task = tasks.FirstOrDefault(t => t.Issue is not null
            && t.Issue.ClaimToken == heldToken && claims.IsLive(ClaimSnapshot.Of(t.Issue), now));
        return task?.Issue is null ? null : IssueKey.Format(task.Issue.Project!.Key, task.Issue.Number);
    }
}

using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The two writes that set and clear the mark an increment leaves on an issue
/// it did not finish - see <see cref="EfHatchIssue.StalledAt"/> and
/// <see cref="EfHatchIssue.Held"/>.
/// </summary>
/// <remarks>
/// <para>Its own controller for the reason <see cref="IssueExpressController"/>
/// is, and cut the same way: no class-level attribute, so neither write below
/// inherits a scope from anything - each says its own.</para>
/// </remarks>
[ApiController]
[Route("api/hatch/issues")]
public class IssueStallController(
    HatchContext db, IActorDirectory actors, IssueClaims claims, ICallerIdentity caller,
    TimeProvider time) : ControllerBase
{
    /// <summary>
    /// A runner saying this increment left the ticket where it found it, and
    /// why. Open to a key - a dispatcher is exactly who leaves this mark.
    /// </summary>
    /// <remarks>
    /// A second mark replaces the first: the row holds only the latest
    /// <see cref="EfHatchIssue.StalledWhy"/>, but every call still writes its
    /// own event, so the trail keeps every sentence a dispatch ever left even
    /// though the row answers with only the newest.
    /// </remarks>
    [HttpPut("{key}/stall")]
    public async Task<ActionResult<IssueDto>> PutStall(string key, StallRequest request, CancellationToken ct)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return NotFound();

        var issue = await db.Issues.Include(i => i.Project).WithKey(projectKey, number).FirstOrDefaultAsync(ct);
        if (issue is null) return NotFound();

        var why = Normalise(request.Why);
        var now = time.GetUtcNow();

        issue.Events.Add(new EfHatchIssueEvent
        {
            Actor = await caller.ActorNameAsync(ct),
            Kind = EfHatchIssueEvent.Stalled,
            Payload = JsonSerializer.Serialize(new { why }),
            At = now,
        });

        issue.StalledAt = now;
        issue.StalledWhy = why;
        issue.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        return await IssueProjection.ToDtoAsync(db, actors, issue, claims, time.GetUtcNow(), ct);
    }

    /// <summary>
    /// Hold the ticket, or resume it - a person's call, never a key's: an
    /// agent that could hold its own ticket could park it off the board for
    /// the night.
    /// </summary>
    /// <remarks>
    /// Resuming clears the stall mark along with <see cref="EfHatchIssue.Held"/>,
    /// so <em>Resume now</em> leaves nothing behind for the dispatcher to fold
    /// on - even where <see cref="EfHatchIssue.Held"/> was already false, which
    /// is the ordinary case: nothing ever held this ticket, a plain stall just
    /// needs resuming.
    /// </remarks>
    [HttpPut("{key}/hold")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<IssueDto>> PutHold(string key, HoldRequest request, CancellationToken ct)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return NotFound();

        var issue = await db.Issues.Include(i => i.Project).WithKey(projectKey, number).FirstOrDefaultAsync(ct);
        if (issue is null) return NotFound();

        // RoleGate is dormant wherever the wall is off, and a guarantee that
        // evaporates under a switch is not a guarantee - the same reason
        // IssueClaimController.NotAPerson asks again in the action.
        if (await caller.IsProgramAsync(ct))
        {
            return new ObjectResult("holding a ticket is the operator's, not an agent's")
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
        }

        var changesHeld = request.Held != issue.Held;
        var clearsStall = !request.Held && (issue.StalledAt is not null || issue.StalledWhy is not null);

        // Setting the value the issue already holds writes nothing and does
        // not move UpdatedAt, the same as every other edit in Hatch - except
        // that "nothing to do" here also has to account for a stall mark that
        // still needs clearing even though Held itself is not changing.
        if (changesHeld || clearsStall)
        {
            var now = time.GetUtcNow();

            issue.Events.Add(new EfHatchIssueEvent
            {
                Actor = await caller.ActorNameAsync(ct),
                Kind = request.Held ? EfHatchIssueEvent.Held : EfHatchIssueEvent.Resumed,
                Payload = JsonSerializer.Serialize(new { from = issue.Held, to = request.Held }),
                At = now,
            });

            issue.Held = request.Held;
            if (!request.Held)
            {
                issue.StalledAt = null;
                issue.StalledWhy = null;
            }

            issue.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }

        return await IssueProjection.ToDtoAsync(db, actors, issue, claims, time.GetUtcNow(), ct);
    }

    /// <summary>
    /// One line at most, trimmed and capped - copied from
    /// <see cref="IssueClaimController"/> rather than shared with it:
    /// <see cref="EfHatchIssue.StalledWhy"/>'s cap is a coincidence of using
    /// the same constant as claim chatter, not a reason to couple the two
    /// controllers.
    /// </summary>
    private static string? Normalise(string? why)
    {
        if (why is null) return null;

        var line = why.ReplaceLineEndings("\n").Split('\n')[0].Trim();
        return line.Length > EfHatchIssue.MaxClaimChatterLength
            ? line[..EfHatchIssue.MaxClaimChatterLength]
            : line;
    }
}

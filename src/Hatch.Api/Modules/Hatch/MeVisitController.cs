using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// When a person last opened a page, kept for the person: the one write behind
/// the Ambitions page's "since you last looked".
/// </summary>
/// <remarks>
/// Its own controller, with no class-level attribute, for the reason
/// <see cref="MeHomeController"/> is: <see cref="MeController"/> is open to a
/// key, and a visit is a person's own, never an agent's. Kept on the server so
/// a phone and a desk agree.
/// </remarks>
[ApiController]
[Route("api/me/visits")]
public class MeVisitController(
    HatchContext db,
    ICallerIdentity caller,
    IActorDirectory directory,
    TimeProvider time) : ControllerBase
{
    /// <summary>
    /// Records that the caller opened the Ambitions page just now, and answers
    /// the instant stored. A second write moves the same row. No body.
    /// </summary>
    [HttpPut("ambitions")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<PersonVisitDto>> PutAmbitions(CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } refusal) return refusal;

        if (await directory.PrincipalAsync(ct) is not { } principal)
            return new ObjectResult("recording a visit needs somebody signed in")
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };

        var now = time.GetUtcNow();

        var row = await db.PersonVisits.FirstOrDefaultAsync(
            v => v.PersonId == principal.Id && v.Page == PersonVisitPage.Ambitions, ct);
        if (row is null)
            db.PersonVisits.Add(new EfHatchPersonVisit { PersonId = principal.Id, Page = PersonVisitPage.Ambitions, VisitedAt = now });
        else
            row.VisitedAt = now;

        await db.SaveChangesAsync(ct);

        return new PersonVisitDto(PersonVisitPage.Ambitions, now);
    }

    /// <summary>
    /// A person, not a key - checked in the action as well as declared in the
    /// attribute, for <see cref="MeHomeController"/>'s reason: <see cref="RoleGate"/>
    /// is dormant wherever the wall is off, and a guarantee that evaporates
    /// under a switch is not a guarantee. A keyless runner that named itself
    /// with the runner header is refused too.
    /// </summary>
    private async Task<ObjectResult?> NotAPerson(CancellationToken ct) =>
        await caller.IsProgramAsync(ct)
            ? new ObjectResult("recording a visit is a person's own, not an agent's")
            {
                StatusCode = StatusCodes.Status403Forbidden,
            }
            : null;
}

using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Hatch.Api.Services.Settings;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The one write on <c>/api/me</c>: a person chooses the screen Hatch opens on.
/// </summary>
/// <remarks>
/// Its own controller, with no class-level attribute, for the reason
/// <see cref="ProjectsController"/> splits its reads from its writes:
/// <see cref="MeController"/> is open to a key, and a home screen is a
/// person's own choice, never an agent's.
/// </remarks>
[ApiController]
[Route("api/me")]
public class MeHomeController(
    HatchContext db,
    ICallerIdentity caller,
    IActorDirectory directory,
    TimeProvider time,
    ISiteSettingsService settings,
    ICallerScope callerScope,
    IOptions<AuthOptions> options) : ControllerBase
{
    /// <summary>
    /// Saves the caller's home screen and answers what <c>GET /api/me</c> would
    /// now say. The checks run person, then principal, then the word, so a key
    /// never learns which words are valid.
    /// </summary>
    [HttpPut("home")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<MeDto>> PutHome([FromBody] PutHomeRequest body, CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } refusal) return refusal;

        if (await directory.PrincipalAsync(ct) is not { } principal)
            return new ObjectResult("setting a home screen needs somebody signed in")
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };

        if (body?.Home is not { } home || !PersonHomeValue.IsKnown(home))
            return BadRequest($"a home screen is one of {PersonHomeValue.Board}, {PersonHomeValue.Ambitions}");

        var now = time.GetUtcNow();

        var row = await db.PersonHomes.FirstOrDefaultAsync(h => h.PersonId == principal.Id, ct);
        if (row is null)
            db.PersonHomes.Add(new EfHatchPersonHome { PersonId = principal.Id, Home = home, UpdatedAt = now });
        else
        {
            row.Home = home;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);

        // One place composes the answer: the same call GET /api/me makes.
        return await new MeController(caller, settings, callerScope, options, db).GetMe(ct);
    }

    // ---- The one narrowing ----

    /// <summary>
    /// A person, not a key - checked in the action as well as declared in the
    /// attribute, for <see cref="RunnersController"/>'s reason:
    /// <see cref="RoleGate"/> is dormant wherever the wall is off, and a
    /// guarantee that evaporates under a switch is not a guarantee. A keyless
    /// runner that named itself with the runner header is refused too.
    /// </summary>
    private async Task<ObjectResult?> NotAPerson(CancellationToken ct) =>
        await caller.IsProgramAsync(ct)
            ? new ObjectResult("setting a home screen is a person's own, not an agent's")
            {
                StatusCode = StatusCodes.Status403Forbidden,
            }
            : null;
}

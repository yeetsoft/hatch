using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// My own Claude headroom - the battery in the nav strip.
///
/// Not the account's own endpoint, proxied: the reading is whatever this
/// caller's own runners last reported on their heartbeat, off the session's own
/// stream (see <see cref="RunnersController"/> and HA-134's decisions). What
/// crosses the wire is Hatch's own vocabulary - <c>window</c>, <c>label</c>,
/// <c>tone</c> - and nothing shaped like the account it came from.
///
/// <c>204 No Content</c> where no runner of this caller's has ever reported a
/// reading. That is the important answer here and it is not an error: a fresh
/// install is in that state, and so is anybody who has never run a runner. This
/// is a nav widget, not a dispatch a caller asked something of, so an ownerless
/// key answers the same <c>204</c> rather than the refusal
/// <see cref="WorkController"/> gives a <c>--mine</c> pass - see
/// <see cref="PrincipalAsync"/>'s remarks there.
/// </summary>
[ApiController]
[Route("api/hatch/utilization")]
[RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
public class UtilizationController(HatchContext db, IActorDirectory actors, TimeProvider time) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<UtilizationReading>> Get(CancellationToken ct)
    {
        var principal = await actors.PrincipalAsync(ct);
        if (principal is null) return NoContent();

        var runners = await db.Runners.AsNoTracking()
            .Where(r => r.ForPersonId == principal.Id && r.Usage != null)
            .ToListAsync(ct);

        var reading = Utilization.Of(runners, time.GetUtcNow());
        return reading is null ? NoContent() : reading;
    }
}

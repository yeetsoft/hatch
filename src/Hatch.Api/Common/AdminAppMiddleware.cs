using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Common;

/// <summary>
/// Keeps the board itself off screens that have no business rendering it: it
/// serves the origin's document requests to a User and above. The admin app
/// was the first thing this gated, under its own /apps/admin prefix that
/// nothing ships any more; the board is the second, and - now that it is the
/// whole origin rather than one bundle under /apps - the boundary is every
/// document request rather than a short list of prefixes.
///
/// The bundle is the boundary here, not the data behind it - every verb that
/// matters carries <see cref="RequireRoleAttribute"/> of its own, so a copy of
/// the JavaScript would buy an attacker nothing. What this buys is the far more
/// ordinary thing: a household where the board is not one tap away on a family
/// member's phone, and a page that never renders half-loaded with a column of
/// 403s in it.
///
/// A flat <c>404</c>, and that is the interesting choice. A <c>403</c> here
/// would confirm that the board exists at this path and that the caller is
/// simply not welcome in it, which is an invitation to go looking; a 404 is
/// indistinguishable from an install built without the bundle. The API's
/// refusals are 403s for exactly the inverse reason (<see cref="RequireRoleAttribute"/>).
///
/// Registered immediately after <see cref="AuthMiddleware"/> and before the
/// static file handlers, because a path check in middleware is the only place
/// that covers both halves of how the board is served: its static assets *and*
/// the MapFallbackToFile route that answers every client-side deep link
/// beneath it. Guarding one and not the other would leave /issues/AER-12
/// serving index.html to anyone.
/// </summary>
public class AdminAppMiddleware(RequestDelegate next, IOptions<AuthOptions> options)
{
    public async Task InvokeAsync(HttpContext context, IRoleGate gate, IAuthGate wall)
    {
        if (!gate.Enabled || !IsGated(context.Request.Path, context.Request.Host.Value, wall))
        {
            await next(context);
            return;
        }

        var decision = await gate.EvaluateAsync(
            context.Request.Method,
            context.Request.Path,
            context.Connection.RemoteIpAddress?.ToString(),
            PersonRole.User,
            // No key, deliberately: these are bundles for a browser to run,
            // and an API key has no browser. A key that asks for one gets the
            // same 404 a non-admin does.
            acceptKey: false,
            context.RequestAborted);

        if (decision.IsAllowed)
        {
            await next(context);
            return;
        }

        // Same reasoning as the wall's refusal: a cached one outlives the
        // change that fixes it, and "I made her an admin and her phone still
        // says the page doesn't exist" is not a symptom anyone will connect to
        // a cache entry.
        //
        // No log line of its own: RoleGate already emitted the Warning, with
        // the reason and the grant on it. One refusal, one line - a second one
        // here would double every entry an operator greps for.
        context.Response.Headers.CacheControl = "no-store";

        // A refusal for a *person* - a device nobody has claimed, or somebody
        // waiting to be let in - is not a secret worth keeping: they are
        // signed in, and the shell can tell them so. Sent to the sign-in path
        // with no return address, because there is nowhere for them to return
        // to yet. Only for a navigation; a fetch or an asset gets the flat 404.
        if (decision.Reason is RoleDecision.NoPerson or RoleDecision.PendingApproval
            && AuthChallenge.PrefersRedirect(context.Request.Headers, context.Request.Method))
        {
            context.Response.Redirect(options.Value.SignInPath);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
    }

    /// <summary>
    /// Every document request is the board's now, except an API route (which
    /// carries <see cref="RequireRoleAttribute"/> of its own) and whatever the
    /// wall already lets through unchallenged - the sign-in shell, the health
    /// probes, and the handful of cookie-less callers <see cref="IAuthGate"/>'s
    /// allow-list names. Canonicalised through the same path Kestrel will
    /// actually serve, the way <see cref="Controllers.AuthController"/>'s
    /// forwardAuth endpoint reads Traefik's forwarded URI - a prefix check alone
    /// would read /media/%2e%2e/plan as exempt when it is /plan by the time it
    /// is served.
    /// </summary>
    private static bool IsGated(PathString path, string? host, IAuthGate wall)
    {
        var canonical = AuthChallenge.CanonicalPath(path.Value);
        if (canonical.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)) return false;

        return !wall.IsExempt(canonical, host);
    }
}

using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Hatch.Api.Services.Calendar;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Controllers;

/// <summary>
/// The two ends of signing in with Google: <c>start</c> hands the browser to
/// Google's account chooser, <c>callback</c> turns what comes back into a
/// person and a grant cookie - the same cookie an invite redemption sets, so
/// nothing downstream can tell the two ways in apart.
///
/// Both routes are exempt from the wall (AuthGate), because they are how a
/// caller with no grant acquires one. What makes the callback safe to leave
/// open is the single-use state row it spends, not a guard: the input is
/// Google's authorization code, not something guessable, so it carries no rate
/// limiter either. Every failure is a redirect to the sign-in shell with an
/// <c>error</c> code, since the browser navigated here and a JSON body would
/// strand it.
///
/// The id token is read without checking its signature, for the reason
/// <see cref="GoogleOAuthService.EmailFromIdToken"/> gives. What is checked is
/// whom it was issued to and by.
/// </summary>
[ApiController]
[Route("api/auth/google")]
public class GoogleSignInController(
    AppDbContext db,
    IGoogleOAuthService oauth,
    IOptions<AuthOptions> options,
    TimeProvider time,
    ILogger<GoogleSignInController> logger) : ControllerBase
{
    public const string NotConfiguredError = "google_not_configured";

    private const string DefaultLanding = "/apps/hatch/";

    private static readonly string[] Issuers = ["https://accounts.google.com", "accounts.google.com"];

    /// <summary>Same allowance as the calendar flow: long enough for a first consent screen, short enough that an abandoned one is gone.</summary>
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    private AuthOptions Auth => options.Value;

    [HttpGet("start")]
    public async Task<IActionResult> Start([FromQuery(Name = "r")] string? returnTo, CancellationToken ct)
    {
        var google = Auth.Google;
        if (!google.Configured) return BackToSignIn(NotConfiguredError);

        var now = time.GetUtcNow();

        // Swept here rather than by a job, as the calendar flow does.
        db.OAuthStates.RemoveRange(await db.OAuthStates.Where(s => s.ExpiresAt <= now).ToListAsync(ct));

        var redirectUri = ResolveRedirectUri(google);
        var state = Pkce.NewState();
        var verifier = Pkce.NewVerifier();

        db.OAuthStates.Add(new EfOAuthState
        {
            State = state,
            CodeVerifier = verifier,
            RedirectUri = redirectUri,
            // Checked before it is stored, so a row never holds a destination
            // that would send a signed-in browser to another origin.
            ReturnTo = AuthChallenge.SafeReturnTo(returnTo) is { Length: <= 512 } safe ? safe : null,
            CreatedAt = now,
            ExpiresAt = now + StateLifetime,
        });
        await db.SaveChangesAsync(ct);

        return Redirect(oauth.BuildSignInUrl(google.ClientId, redirectUri, state, Pkce.ChallengeFor(verifier)));
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken ct)
    {
        var google = Auth.Google;

        // The person declining at the chooser arrives as access_denied.
        if (!string.IsNullOrWhiteSpace(error)) return Refuse(error);
        if (string.IsNullOrWhiteSpace(state)) return Refuse("missing_state");
        if (string.IsNullOrWhiteSpace(code)) return Refuse("missing_code");
        if (!google.Configured) return Refuse(NotConfiguredError);

        var pending = await db.OAuthStates.FirstOrDefaultAsync(s => s.State == state, ct);
        if (pending is null) return Refuse("unknown_state");

        // Single use across replicas: the DELETE has to affect the row, and
        // EF raises a concurrency exception when it does not, so exactly one
        // callback is allowed to spend this authorization. Remove-and-save
        // rather than the calendar callback's ExecuteDelete, which is the same
        // guarantee in a form the in-memory provider these tests run on can
        // also express.
        db.OAuthStates.Remove(pending);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Refuse("unknown_state");
        }

        var now = time.GetUtcNow();
        if (pending.ExpiresAt <= now) return Refuse("expired_state");

        var result = await oauth.ExchangeCodeAsync(
            code, pending.CodeVerifier, pending.RedirectUri, google.ClientId, google.ClientSecret, ct);
        if (!result.Succeeded) return Refuse(result.Error ?? "token_exchange_failed");

        var token = GoogleIdToken.Parse(result.Tokens.IdToken);
        if (token is null) return Refuse("no_id_token");
        if (string.IsNullOrWhiteSpace(token.Sub)) return Refuse("no_subject");
        if (!string.Equals(token.Aud, google.ClientId, StringComparison.Ordinal)) return Refuse("wrong_audience");
        if (token.Iss is null || !Issuers.Contains(token.Iss, StringComparer.Ordinal)) return Refuse("wrong_issuer");
        if (token.Email is null || !token.EmailVerified) return Refuse("email_unverified");

        var identity = await db.ExternalIdentities.Include(i => i.Person)
            .FirstOrDefaultAsync(i => i.Provider == EfExternalIdentity.GoogleProvider && i.Subject == token.Sub, ct);

        // Only when the sub is unknown: an Admin may have pre-approved this
        // address. The verified email is consulted here and nowhere else - once
        // bound, the sub is the identity, so a bound row is never found by this
        // query (it requires Subject == null).
        if (identity is null)
        {
            var address = token.Email.ToLowerInvariant();
            identity = await db.ExternalIdentities.Include(i => i.Person)
                .FirstOrDefaultAsync(i => i.Provider == EfExternalIdentity.GoogleProvider && i.Subject == null && i.Email == address, ct);
            if (identity is not null)
            {
                identity.Subject = token.Sub;
                logger.LogInformation("Google sign-in claimed a pre-approved identity for person {PersonId}", identity.PersonId);
            }
        }

        if (identity is null)
        {
            // The first person to sign in on an install with no Admin becomes
            // one, since only an Admin can promote anybody and Pending is a
            // locked door otherwise. The rule is "no Admin", not "no people".
            // Accepted race: two first sign-ins in the same instant could both
            // read "no Admin" and both become one. An Admin can demote, and it
            // is a one-time window on a fresh install.
            var firstAdmin = !await db.People.AnyAsync(p => p.Role == PersonRole.Admin, ct);
            var person = new EfPerson
            {
                Name = NameFor(token),
                Role = firstAdmin ? PersonRole.Admin : PersonRole.Pending,
                CreatedAt = now,
                UpdatedAt = now,
            };
            identity = new EfExternalIdentity
            {
                Provider = EfExternalIdentity.GoogleProvider,
                Subject = token.Sub,
                Email = token.Email,
                Person = person,
                CreatedAt = now,
                LastSignInAt = now,
            };
            db.ExternalIdentities.Add(identity);
            if (firstAdmin)
                logger.LogWarning("Google sign-in created person {PersonId} as the first Administrator", person.Id);
            else
                logger.LogInformation("Google sign-in created person {PersonId} ({Role})", person.Id, person.Role);
        }
        else
        {
            identity.Email = token.Email;
            identity.LastSignInAt = now;
        }

        var userAgent = Request.Headers.UserAgent.ToString();
        var cookieToken = AuthTokens.NewToken();
        var grant = new EfAuthGrant
        {
            TokenHash = AuthTokens.Hash(cookieToken),
            Label = UserAgentLabel.From(userAgent),
            Person = identity.Person,
            Kind = AuthGrantKind.Interactive,
            CreatedAt = now,
            CookieIssuedAt = now,
            LastSeenAt = now,
            LastSeenIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = userAgent.Length > 512 ? userAgent[..512] : userAgent,
        };
        db.AuthGrants.Add(grant);

        // One SaveChanges: a person, their identity and their first grant land
        // together or not at all.
        await db.SaveChangesAsync(ct);

        AuthCookie.Issue(Response, Auth, cookieToken);
        logger.LogInformation("Google sign-in minted grant {GrantId} ({Label}) for person {PersonId}", grant.Id, grant.Label, identity.PersonId);

        return Redirect(pending.ReturnTo is { } target && AuthChallenge.SafeReturnTo(target) is { } safe ? safe : DefaultLanding);
    }

    /// <summary>The token's name if it has a usable one, else the email's local part, else a placeholder an admin can rename.</summary>
    private static string NameFor(GoogleIdToken token)
    {
        if (PersonName.TryNormalize(token.Name, out var name, out _)) return name;

        return PersonName.FromEmail(token.Email);
    }

    /// <summary>
    /// The registered URI wins whenever it is set - Google compares it
    /// byte-for-byte - else it is derived from the request, which is the
    /// proxy's scheme and host because Program.cs configures UseForwardedHeaders.
    /// </summary>
    private string ResolveRedirectUri(GoogleAuthOptions google) =>
        !string.IsNullOrWhiteSpace(google.RedirectUri)
            ? google.RedirectUri
            : $"{Request.Scheme}://{Request.Host}/api/auth/google/callback";

    private IActionResult Refuse(string error)
    {
        var code = CalendarOAuthController.Sanitize(error);
        logger.LogWarning("Google sign-in refused: {Error}", code);
        return BackToSignIn(code);
    }

    private IActionResult BackToSignIn(string error) =>
        Redirect(QueryHelpers.AddQueryString(Auth.SignInPath, "error", CalendarOAuthController.Sanitize(error)));
}

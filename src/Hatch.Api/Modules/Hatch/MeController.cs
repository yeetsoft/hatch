using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Hatch.Api.Services.DeviceMapping;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// What Hatch calls whoever is at the browser, so the nav strip can say it back
/// to them and know what they may see.
///
/// Answers in both modes: <c>kind: person</c> when a grant holds a person (the
/// wall is up, or a browser signed in anyway), <c>kind: local</c> when the wall
/// is off and the machine's own person is speaking. <c>204 No Content</c> only
/// for a key or a runner, which is not somebody at a keyboard - the strip this
/// feeds is a browser's, and it should draw nothing rather than an empty box.
/// </summary>
/// <remarks>
/// A read of an identity and not a way to set one. Writing it is
/// <see cref="SettingsController"/>, which writes the same setting this reads -
/// already the first thing <see cref="LocalCaller.PersonNameOf"/> consults, so
/// the two need nothing between them.
/// </remarks>
[ApiController]
[Route("api/hatch/me")]
[RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
public class MeController(
    ICallerIdentity caller,
    ISiteSettingsService settings,
    IOptions<AuthOptions> options) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MeDto>> GetMe(CancellationToken ct)
    {
        // A key or a runner is a program, not somebody at a keyboard, so it
        // reads as "nobody" here.
        if (await caller.IsProgramAsync(ct)) return NoContent();

        if (await caller.PersonAsync(ct) is { } signedIn)
            return new MeDto("person", signedIn.Name, signedIn.Role.ToString().ToLowerInvariant(), true, true, false);

        if (await caller.LocalAsync(ct) is not { Kind: ActorKind.Person } person) return NoContent();

        // Asked of the two sources rather than inferred by comparing the name
        // to the default, so an operator who genuinely calls themselves
        // "friend" is not nagged about it for the life of the install.
        var configured = LocalCaller.IsNamed(
            (await settings.GetAsync(ct)).LocalPersonName,
            options.Value.LocalPerson.Name);

        return new MeDto("local", person.Name, null, configured, false, options.Value.Google.Configured);
    }
}

/// <summary>
/// Who is at the browser, as the nav strip needs them.
/// </summary>
/// <param name="Kind"><c>local</c> (the wall is off) or <c>person</c> (a grant holds a person).</param>
/// <param name="Name">What events written from this browser will say.</param>
/// <param name="Role"><c>user</c> or <c>admin</c> for a person; null for local, which has no role to gate on.</param>
/// <param name="Configured">
/// Whether anybody actually said so. False means the name is the built-in
/// default and the strip explains what to set - which is the one thing an
/// operator who has just started Hatch for the first time needs told, and the
/// one thing no amount of correct behaviour would tell them. Always true for a
/// person, who is named because they enrolled.
/// </param>
/// <param name="CanSignOut">Whether there is a grant to end. False for local.</param>
/// <param name="CanSignIn">
/// Whether an authority is configured to sign in with. False for a person,
/// who already holds a grant and so has nothing to sign in to.
/// </param>
public record MeDto(string Kind, string Name, string? Role, bool Configured, bool CanSignOut, bool CanSignIn);

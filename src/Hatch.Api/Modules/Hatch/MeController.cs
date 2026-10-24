using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Hatch.Api.Services.DeviceMapping;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// What Hatch calls whoever - or whatever - is calling, so the nav strip can
/// say it back to them and know what they may see.
///
/// Answers in five modes: <c>kind: key</c> for an API key, <c>kind: runner</c>
/// for a wall-off runner header, <c>kind: person</c> when a grant holds a
/// person (the wall is up, or a browser signed in anyway), <c>kind: local</c>
/// when the wall is off and the machine's own person is speaking, <c>kind:
/// none</c> when the wall is off and nobody local answered either - a browser
/// that has not signed in anywhere. <c>204 No Content</c> only for an
/// unauthenticated browser against a walled Hatch - no grant, no key, no
/// local actor.
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
        if (await caller.ApiKeyAsync(ct) is { } key)
            return new MeDto("key", key.Name, key.Owner?.Role.ToString().ToLowerInvariant(),
                true, false, false, key.Owner?.Name, key.Scopes);

        if (await caller.LocalAsync(ct) is { Kind: ActorKind.Key } runner)
            return new MeDto("runner", runner.Name, null, true, false, false);

        if (await caller.PersonAsync(ct) is { } signedIn)
            return new MeDto("person", signedIn.Name, signedIn.Role.ToString().ToLowerInvariant(), true, true, false);

        var local = await caller.LocalAsync(ct);
        if (local is not { Kind: ActorKind.Person } person)
        {
            // The wall is off and nobody local answered either - not a key,
            // not a runner, not a grant, not even the machine's own person.
            // The sign-in page still redeems an invite without the wall or
            // Google configured, so this caller can always sign in.
            if (!options.Value.Enabled && local is null)
                return new MeDto("none", "", null, true, false, true);

            return NoContent();
        }

        // Asked of the two sources rather than inferred by comparing the name
        // to the default, so an operator who genuinely calls themselves
        // "friend" is not nagged about it for the life of the install.
        var configured = LocalCaller.IsNamed(
            (await settings.GetAsync(ct)).LocalPersonName,
            options.Value.LocalPerson.Name);

        return new MeDto("local", person.Name, null, configured, false, options.Value.Google.Configured);
    }
}

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
/// What Hatch calls whoever - or whatever - is calling, so the nav strip can
/// say it back to them and know what they may see.
///
/// Answers in four modes: <c>kind: key</c> for an API key, <c>kind: person</c>
/// when a grant holds a person (the wall is up, or a browser signed in
/// anyway), <c>kind: local</c> when the wall is off and the machine's own
/// person is speaking, <c>kind: none</c> when the wall is off and nobody
/// local answered either - a browser that has not signed in anywhere.
/// <c>204 No Content</c> only for an unauthenticated browser against a walled
/// Hatch - no grant, no key, no local actor.
/// </summary>
/// <remarks>
/// A read of an identity and not a way to set one. Writing it is
/// <see cref="SettingsController"/>, which writes the same setting this reads -
/// already the first thing <see cref="LocalCaller.PersonNameOf"/> consults, so
/// the two need nothing between them.
/// </remarks>
[ApiController]
[Route("api/me")]
[RequireRole(PersonRole.User, AcceptKey = true)]
public class MeController(
    ICallerIdentity caller,
    ISiteSettingsService settings,
    ICallerScope callerScope,
    IOptions<AuthOptions> options,
    HatchContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MeDto>> GetMe(CancellationToken ct)
    {
        // What ScopeResolutionFilter already read for this request, so it
        // costs nothing here.
        var scope = ScopeOf(await callerScope.ResolveAsync(ct));

        if (await caller.ApiKeyAsync(ct) is { } key)
            return new MeDto("key", key.Name, key.Owner?.Role.ToString().ToLowerInvariant(),
                true, false, false, key.Owner?.Name, scope, await HomeOfAsync(key.OwnerPersonId, ct));

        if (await caller.PersonAsync(ct) is { } signedIn)
            return new MeDto("person", signedIn.Name, signedIn.Role.ToString().ToLowerInvariant(), true, true, false,
                Scope: scope, Home: await HomeOfAsync(signedIn.Id, ct));

        var local = await caller.LocalAsync(ct);
        if (local is not { Kind: ActorKind.Person } person)
        {
            // The wall is off and nobody local answered either - not a key,
            // not a grant, not even the machine's own person. The sign-in
            // page still redeems an invite without the wall or Google
            // configured, so this caller can always sign in.
            if (!options.Value.Enabled && local is null)
                return new MeDto("none", "", null, true, false, true, Scope: scope, Home: await HomeOfAsync(null, ct));

            return NoContent();
        }

        // Asked of the two sources rather than inferred by comparing the name
        // to the default, so an operator who genuinely calls themselves
        // "friend" is not nagged about it for the life of the install.
        var configured = LocalCaller.IsNamed(
            (await settings.GetAsync(ct)).LocalPersonName,
            options.Value.LocalPerson.Name);

        return new MeDto("local", person.Name, null, configured, false, options.Value.Google.Configured,
            Scope: scope, Home: await HomeOfAsync(person.Id, ct));
    }

    /// <summary>
    /// The person's own row, or <see cref="PersonHomeValue.Default"/> for nobody,
    /// no row, or a value nobody recognises - a stale value reads as the board
    /// and never breaks the nav strip.
    /// </summary>
    private async Task<string> HomeOfAsync(Guid? personId, CancellationToken ct)
    {
        if (personId is not { } id)
            return PersonHomeValue.Default;

        var home = await db.PersonHomes.AsNoTracking()
            .Where(h => h.PersonId == id)
            .Select(h => h.Home)
            .FirstOrDefaultAsync(ct);

        return PersonHomeValue.IsKnown(home) ? home! : PersonHomeValue.Default;
    }

    /// <summary>
    /// <c>none</c> is a bounded scope with nothing in it, which is also what a
    /// <c>some</c> whose projects were all deleted comes to: the mode says what
    /// the caller sees, not what a row says.
    /// </summary>
    private static MeScopeDto ScopeOf(VisibleProjects visible) =>
        visible.Unbounded ? new MeScopeDto(PersonScopeMode.All, [])
        : visible.ProjectIds.Count == 0 ? new MeScopeDto(PersonScopeMode.None, [])
        : new MeScopeDto(PersonScopeMode.Some, [.. visible.ProjectIds.Order()]);
}

using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Models.Auth;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Hatch.Api.Controllers;

/// <summary>
/// Minting and revoking the credentials that are not browsers - the admin API
/// Keys page, and nothing else.
///
/// Plain <c>[RequireAdmin]</c> on the whole controller, with no
/// <c>AcceptScope</c>, and that is the load-bearing line in this file: a key
/// cannot mint a key. Allowing it would make the scope system decorative, since
/// any key could quietly issue itself a second one carrying whatever it liked.
/// Credentials are handed out by a person, at a screen, on purpose.
/// </summary>
/// <remarks>
/// A sibling of <see cref="AuthController"/> rather than four more actions on
/// it. That controller is already the app's longest and carries the two
/// allow-listed endpoints the wall depends on; these three share nothing with
/// it but a route prefix, and the prefix is what says they belong to the same
/// boundary.
/// </remarks>
[ApiController]
[Route("api/auth/keys")]
[RequireAdmin]
public class ApiKeysController(
    IAuthService auth, IActorDirectory actors, ICallerIdentity caller, ILogger<ApiKeysController> logger) : ControllerBase
{
    /// <summary>
    /// Every key, newest first, revoked ones included. A revocation is a fact
    /// worth seeing - "that key is gone" is the answer to a question somebody
    /// is asking, and a row that vanished answers nothing.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ApiKeyDto>>> ListKeys(CancellationToken ct)
    {
        var keys = await auth.ListApiKeysAsync(ct);
        return keys.Select(ApiKeyDto.From).ToList();
    }

    /// <summary>
    /// The scopes a key may carry. A read of its own so the mint form offers the
    /// server's list rather than spelling one in the bundle, and so the list
    /// response stays the plain array its callers already expect.
    /// </summary>
    [HttpGet("scopes")]
    public ActionResult<IReadOnlyList<string>> ListScopes() => ApiKeyScopes.All;

    /// <summary>
    /// Mints a key. The response carries the secret in plaintext - the only
    /// moment it exists outside a hash - so it can be copied into a file once
    /// and never again after the tab is closed. A lost key is replaced by
    /// minting another, not by looking this one up.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ApiKeyMintedDto>> CreateKey([FromBody] CreateApiKeyRequest? request, CancellationToken ct)
    {
        var name = request?.Name?.Trim();
        if (string.IsNullOrEmpty(name)) return BadRequest("a key needs a name - it is what the audit trail will call it");
        if (name.Length > EfApiKey.MaxNameLength)
            return BadRequest($"a key name is at most {EfApiKey.MaxNameLength} characters");

        // Validated rather than stored as sent: an unknown scope is a typo, and
        // a typo silently stored is a key that mysteriously reaches nothing.
        var scopes = request?.Scopes ?? [];
        if (scopes.FirstOrDefault(s => !ApiKeyScopes.IsKnown(s)) is { } unknown)
            return BadRequest($"\"{unknown}\" is not a scope - the scopes are {string.Join(", ", ApiKeyScopes.All)}");

        // No owner named defaults to whoever is minting it - the admin at the
        // screen - or to nobody where the wall is off and there is no admin to
        // default to. An owner that was named is checked against the same
        // liveness a --mine pass will read it with later, so a key can never
        // be minted pointed at a person who does not exist.
        Guid? ownerPersonId;
        if (request?.OwnerPersonId is { } named)
        {
            if (await actors.ResolveAsync(ActorKind.Person, named, ct) is null)
                return BadRequest("that person does not exist");
            ownerPersonId = named;
        }
        else
        {
            ownerPersonId = (await caller.PersonAsync(ct))?.Id;
        }

        var created = await auth.CreateApiKeyAsync(name, scopes.Distinct(StringComparer.Ordinal).ToList(), ownerPersonId, ct);
        if (created is null) return Conflict($"there is already a key called \"{name}\"");

        // A live credential has no business in a cache, anyone's.
        Response.Headers.CacheControl = "no-store";

        logger.LogInformation("API key {KeyId} minted for {Name}", created.Key.Id, created.Key.Name);
        return new ApiKeyMintedDto(ApiKeyDto.From(created.Key), created.Secret);
    }

    /// <summary>
    /// Stops a key working. A POST rather than a DELETE because the row stays:
    /// the audit trail names this key, and "who was Claude in March" is a
    /// question a deleted row cannot answer.
    /// </summary>
    [HttpPost("{id:guid}/revoke")]
    public async Task<IActionResult> RevokeKey(Guid id, CancellationToken ct) =>
        await auth.RevokeApiKeyAsync(id, ct) ? NoContent() : NotFound();

    /// <summary>
    /// Changes or clears whose tickets this key's <c>--mine</c> reaches. Only
    /// an admin ever calls this - the class attribute already says so - and
    /// deliberately never the key itself: a key that could set its own owner
    /// could adopt anybody's tickets, which is the edge this whole feature is
    /// built not to reopen.
    /// </summary>
    [HttpPut("{id:guid}/owner")]
    public async Task<IActionResult> SetOwner(Guid id, [FromBody] ApiKeyOwnerRequest? request, CancellationToken ct)
    {
        if (request?.PersonId is { } personId && await actors.ResolveAsync(ActorKind.Person, personId, ct) is null)
            return BadRequest("that person does not exist");

        return await auth.SetApiKeyOwnerAsync(id, request?.PersonId, ct) ? NoContent() : NotFound();
    }
}

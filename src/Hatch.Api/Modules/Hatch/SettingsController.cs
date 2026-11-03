using System.Globalization;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Models.Auth;
using Hatch.Api.Services.Auth;
using Hatch.Api.Services.DeviceMapping;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The settings a Hatch install of its own configures: the Claude
/// subscription token that drives the battery, the OpenAI and xAI API keys the
/// other agents' containers authenticate with, and what to call whoever is
/// sitting at this machine.
///
/// Here rather than on the admin app's Settings page because an install with no
/// admin app - which is every install that is only somebody's tracker - still
/// has to be able to set them. They are the same site settings the admin page
/// wrote, under the same keys, stored the same way, so an operator who set the
/// token before this existed finds it already set here. The agent keys are
/// held exactly as the token is: protected at rest, dots on read, <c>""</c>
/// clears.
///
/// The two routes that read and write them are <c>[RequireRole(User)]</c> with
/// no <c>AcceptScope</c>, unlike <see cref="UtilizationController"/> and
/// <see cref="LocalPersonController"/> beside them: those are reads an agent has
/// a use for, and these are not. An API key that could write here could set the
/// name every event in the house is signed with, or swap the credential the
/// account's headroom is read through. A person, at a browser, or nobody.
///
/// The site's default token caps are written from the same <c>PUT</c> but are
/// held to more than the rest of it: an admin's to set, never a key's or a
/// runner's, because they bound what every runner without a cap of its own may
/// spend. That is checked in the action, not only the attribute - see
/// <see cref="PutHatchSettings"/>.
///
/// No class-level attribute even so, and that is the third time this split has
/// been drawn (<see cref="RunnersController"/>, <see cref="AssigneeController"/>,
/// <see cref="IssuePlaybookController"/>): <see cref="GetCredential"/> below, and
/// <see cref="GetClaudeToken"/> with it, are cut the other way - a keyless
/// runner is their ordinary caller - and <c>RequireAdminAttribute</c> is
/// <c>AllowMultiple = false</c>, so a method-level attribute silently
/// *replaces* a class-level one rather than tightening it. Decorating every
/// action explicitly is what keeps the two person-only routes person-only
/// whatever is added beside them.
/// </summary>
[ApiController]
[Route("api/hatch/settings")]
public class SettingsController(
    AppDbContext db,
    ISiteSettingsService siteSettings,
    IAgentCredential credentials,
    IOptions<AuthOptions> authOptions,
    ICallerIdentity caller) : ControllerBase
{
    /// <summary>What is stood in for a secret that is set. The admin app's SettingsController redacts to the same string, because it is the same answer.</summary>
    private const string Redacted = "••••••••";

    [HttpGet]
    [RequireRole(PersonRole.User)]
    public async Task<HatchSettingsDto> GetHatchSettings(CancellationToken ct) => await ReadAsync(ct);

    /// <summary>
    /// An agent's credential itself, wrapped, for the one caller that has to
    /// have it: the container runner's entrypoint, which starts an agent CLI of
    /// its own and has nowhere else to read a credential from. So a friend
    /// configures theirs on the Settings page and in no second place.
    /// <c>claude</c>, <c>codex</c> and <c>grok</c> are answered; anything else
    /// is a 404, and one that is unset or blank is a 204.
    /// </summary>
    /// <remarks>
    /// <para>Two gates, and they are independent on purpose. The attribute
    /// accepts a Hatch-scoped key or a keyless runner, unlike the two routes
    /// above it - a dispatcher is the ordinary caller here, and a person at a
    /// browser is the unusual one. Then the first line refuses everybody,
    /// caller and credential alike, wherever the wall is up: a credential that
    /// crosses a network Hatch does not own is a different question from one
    /// handed to a container on the same laptop, and this route only answers
    /// the second one. An install with its wall on has enrolled devices, TLS
    /// and somewhere to put a secret properly, and should.</para>
    ///
    /// <para><c>AuthErrorDto</c> and a bare 403 rather than <c>Forbid()</c>,
    /// which expects an authentication scheme this app does not register - the
    /// same shape <c>RequireAdminAttribute</c> itself refuses with, so both
    /// gates read alike from outside.</para>
    /// </remarks>
    [HttpGet("credentials/{agent}")]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public async Task<ActionResult<CredentialDto>> GetCredential(string agent, CancellationToken ct) =>
        await CredentialAsync(agent, ct);

    /// <summary>
    /// The Claude token, kept for an older container; new callers use
    /// <c>credentials/{agent}</c>. Answers exactly what it always did, through
    /// the same gates, because it is the same method.
    /// </summary>
    [HttpGet("claude-token")]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public async Task<ActionResult<CredentialDto>> GetClaudeToken(CancellationToken ct) =>
        await CredentialAsync("claude", ct);

    private async Task<ActionResult<CredentialDto>> CredentialAsync(string agent, CancellationToken ct)
    {
        if (authOptions.Value.Enabled)
        {
            return new ObjectResult(new AuthErrorDto(
                "this Hatch has its wall on, and a token that crosses a network is a different question - " +
                "the container runner is for an install with its wall off"))
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
        }

        if (!SiteSettingAgentCredential.Agents.Contains(agent, StringComparer.Ordinal)) return NotFound();

        // Through the credential rather than reading the setting again: it is
        // the one interface that produces this plaintext, and "not set" and
        // "set to blank" are already the same answer there.
        var credential = await credentials.GetAsync(agent, ct);
        if (credential is null) return NoContent();

        return new CredentialDto(SecretProtector.Protect(credential));
    }

    /// <summary>
    /// The bulk-edit convention, which is Hatch's convention everywhere
    /// (docs/hatch-planning.md): a field left out is left alone, <c>""</c>
    /// clears it, anything else replaces it.
    ///
    /// That is what makes "clearing the token removes it" a value rather than a
    /// second verb - <see cref="SecretProtector.Protect"/> answers <c>""</c>
    /// for an empty input and stores it untagged, so a cleared token reads back
    /// exactly like one nobody ever set and <see cref="IAgentCredential"/>
    /// answers null for both.
    /// </summary>
    /// <remarks>
    /// <para>The default token caps are the same convention with a count in
    /// place of a secret: left out leaves one, <c>""</c> clears it, a value
    /// (<see cref="TokenCount.TryParse"/>'s notation) sets it.</para>
    ///
    /// <para>The order of the body is the guarantee that a refusal writes
    /// nothing, the way <c>RunnersController.PatchRunner</c> documents it: gate,
    /// then parse every present field, then write. The gate answers before the
    /// parse so that a caller who may not set a default learns nothing about
    /// what would have been accepted. It is checked here as well as by the
    /// attribute, for <c>RunnersController.NotAPerson</c>'s reason: the role
    /// gate is dormant wherever the wall is off. A request that carries neither
    /// default is not gated by it, so a non-admin's name-only <c>PUT</c> is
    /// unchanged.</para>
    /// </remarks>
    [HttpPut]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<HatchSettingsDto>> PutHatchSettings(HatchSettingsWriteRequest request, CancellationToken ct)
    {
        if (request.DefaultMaxTokens is not null || request.DefaultMaxTokensPerHour is not null)
        {
            if (await NotAnAdmin(ct) is { } refusal) return refusal;
        }

        var tokenWrites = new List<(string Key, string Stored)>();
        foreach (var (key, label, sent) in new (string, string, string?)[]
        {
            (SiteSettingKeys.RunnerDefaultMaxTokens, "the default max tokens", request.DefaultMaxTokens),
            (SiteSettingKeys.RunnerDefaultMaxTokensPerHour, "the default max tokens per hour", request.DefaultMaxTokensPerHour),
        })
        {
            if (sent is null) continue;

            var trimmed = sent.Trim();
            if (trimmed.Length == 0)
            {
                tokenWrites.Add((key, ""));
            }
            else if (TokenCount.TryParse(trimmed, out var count))
            {
                tokenWrites.Add((key, count.ToString(CultureInfo.InvariantCulture)));
            }
            else
            {
                return BadRequest($"{label} takes {TokenCount.Refusal} - not \"{trimmed}\"");
            }
        }

        var wrote = false;

        (string Key, string? Value)[] secrets =
        [
            (SiteSettingKeys.ClaudeSubscriptionToken, request.ClaudeSubscriptionToken),
            (SiteSettingKeys.CodexApiKey, request.CodexApiKey),
            (SiteSettingKeys.XaiApiKey, request.XaiApiKey),
        ];

        foreach (var (key, value) in secrets)
        {
            if (value is not { } secret) continue;

            await UpsertAsync(key, SecretProtector.Protect(secret), ct);
            wrote = true;
        }

        // Stored as it was typed. The read side already decides what an
        // unusable name means - LocalCaller.PersonNameOf falls back to config
        // and then to its own default for anything PersonName refuses - so a
        // name this page stores degrades exactly the way one from
        // Auth__LocalPerson__Name would, and guarding it twice would mean two
        // places to keep in step.
        if (request.LocalPersonName is { } name)
        {
            await UpsertAsync(SiteSettingKeys.LocalPersonName, name, ct);
            wrote = true;
        }

        foreach (var (key, stored) in tokenWrites)
        {
            await UpsertAsync(key, stored, ct);
            wrote = true;
        }

        // Before the read below, for the reason the admin controller gives:
        // somebody who saves a setting and is immediately shown something
        // derived from it should not be told the old answer for the length of
        // the cache TTL. It is also what makes the battery reflect a new token
        // without a restart.
        if (wrote) siteSettings.Invalidate();

        return await ReadAsync(ct);
    }

    /// <summary>
    /// The default caps are an admin's: refused to a key or a keyless runner
    /// whether the wall is up or not, and with the wall up to anybody who is
    /// not an admin. With the wall off there are no roles, so a caller that is
    /// not a program is the operator at their own machine.
    /// </summary>
    private async Task<ObjectResult?> NotAnAdmin(CancellationToken ct)
    {
        const string Sentence = "the default token budget is an admin's to set";

        var refused = await caller.IsProgramAsync(ct)
            || (authOptions.Value.Enabled && (await caller.PersonAsync(ct))?.Role is not >= PersonRole.Admin);

        return refused
            ? new ObjectResult(Sentence) { StatusCode = StatusCodes.Status403Forbidden }
            : null;
    }

    private async Task UpsertAsync(string key, string value, CancellationToken ct)
    {
        var setting = await db.SiteSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (setting is null)
        {
            db.SiteSettings.Add(new EfSiteSetting { Key = key, Value = value });
        }
        else
        {
            setting.Value = value;
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<HatchSettingsDto> ReadAsync(CancellationToken ct)
    {
        var settings = await siteSettings.GetAsync(ct);

        return new HatchSettingsDto(
            // The snapshot has already deobfuscated the token, so "is it set"
            // is asked of the plaintext rather than of SecretProtector.HasValue,
            // which expects the stored form. Same answer, one query fewer - and
            // the value itself never leaves this method.
            ClaudeSubscriptionToken: RedactedIfSet(settings.ClaudeSubscriptionToken),
            LocalPersonNameApplies: !authOptions.Value.Enabled,
            LocalPersonName: settings.LocalPersonName ?? "",
            CodexApiKey: RedactedIfSet(settings.CodexApiKey),
            XaiApiKey: RedactedIfSet(settings.XaiApiKey),
            DefaultMaxTokens: settings.RunnerDefaultMaxTokens,
            DefaultMaxTokensPerHour: settings.RunnerDefaultMaxTokensPerHour);
    }

    private static string RedactedIfSet(string? secret) => string.IsNullOrWhiteSpace(secret) ? "" : Redacted;
}

/// <summary>
/// What Hatch's Settings page draws.
/// </summary>
/// <param name="ClaudeSubscriptionToken">Dots when one is set, empty when none is. Never the token: a page that held one would be a page that leaked one.</param>
/// <param name="LocalPersonNameApplies">
/// Whether there is a name here to set at all - false wherever the wall is up,
/// which is every cluster install, because there a person is a person because
/// they enrolled and their name is already on everything they write. The same
/// flag <see cref="LocalCaller"/> is gated on, so the page and the identity
/// agree by construction.
/// </param>
/// <param name="LocalPersonName">The name as stored, or empty. Not a secret, so it is handed back to be edited.</param>
/// <param name="CodexApiKey">Dots when one is set, empty when none is - the same answer as the token, for the same reason.</param>
/// <param name="XaiApiKey">Dots when one is set, empty when none is.</param>
/// <param name="DefaultMaxTokens">The site-wide cap on tokens a runner with none of its own runs under, or null when none is set.</param>
/// <param name="DefaultMaxTokensPerHour">The site-wide cap on tokens per hour a runner with none of its own runs under, or null when none is set.</param>
public record HatchSettingsDto(
    string ClaudeSubscriptionToken,
    bool LocalPersonNameApplies,
    string LocalPersonName,
    string CodexApiKey,
    string XaiApiKey,
    long? DefaultMaxTokens,
    long? DefaultMaxTokensPerHour);

/// <summary>
/// An edit to any of the settings, or several. Null - or omitted - leaves one
/// alone; <c>""</c> clears it. The two agent keys and the two default token
/// caps trail with defaults, so a request that names only the first two behaves
/// as it did before they existed. The caps take <see cref="TokenCount"/>'s
/// notation, as text, and are an admin's alone.
/// </summary>
public record HatchSettingsWriteRequest(
    string? ClaudeSubscriptionToken,
    string? LocalPersonName,
    string? CodexApiKey = null,
    string? XaiApiKey = null,
    string? DefaultMaxTokens = null,
    string? DefaultMaxTokensPerHour = null);

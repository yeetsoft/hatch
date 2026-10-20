using Hatch.Api.Services.DeviceMapping;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// An agent's credential, or null.
///
/// One member, and null is an ordinary value rather than a startup failure: an
/// installation with none of its runners in a container never needs one set at
/// all. <see cref="SettingsController"/>'s <c>credentials/{agent}</c> route and
/// the older <c>claude-token</c> beside it are the only readers - the container
/// runner's entrypoint is the only thing that ever calls them - and binding to
/// this interface rather than to where the credential happens to be kept is
/// what makes moving it a change to one class.
/// </summary>
public interface IAgentCredential
{
    /// <summary>
    /// The credential for <paramref name="agent"/> as currently configured, or
    /// null when there is none - and null for an agent this Hatch does not know
    /// too. Never throws, and never distinguishes "not set" from "set to blank".
    /// </summary>
    Task<string?> GetAsync(string agent, CancellationToken ct);
}

/// <summary>
/// The credentials as site settings, read fresh on each call.
///
/// Read rather than captured for the reason <see cref="Photos.ImmichClient"/>
/// re-reads its host and key: the settings are editable from the admin page
/// while the app runs, and an OAuth token that expires is pasted in again far
/// more often than the process restarts.
///
/// They are stored obfuscated and redacted on read like every other
/// secret-valued setting - see SettingsController's <c>IsSecret</c>, which is
/// the whole of that job - so the plaintext reaches this class and nothing else.
/// </summary>
public class SiteSettingAgentCredential(ISiteSettingsService siteSettings) : IAgentCredential
{
    /// <summary>The agent ids a credential can be asked for: the route segment, lower-case by contract.</summary>
    public static readonly string[] Agents = ["claude", "codex", "grok"];

    public async Task<string?> GetAsync(string agent, CancellationToken ct)
    {
        if (!Agents.Contains(agent, StringComparer.Ordinal)) return null;

        var settings = await siteSettings.GetAsync(ct);
        var credential = agent switch
        {
            "claude" => settings.ClaudeSubscriptionToken,
            "codex" => settings.CodexApiKey,
            _ => settings.XaiApiKey,
        };
        return string.IsNullOrWhiteSpace(credential) ? null : credential.Trim();
    }
}

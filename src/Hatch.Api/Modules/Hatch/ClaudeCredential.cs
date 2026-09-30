using Hatch.Api.Services.DeviceMapping;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The Claude subscription token, or null.
///
/// One member, and null is an ordinary value rather than a startup failure: an
/// installation with none of its runners in a container never needs one set at
/// all. <see cref="SettingsController"/>'s <c>claude-token</c> route is the only
/// reader - the container runner's entrypoint, <c>hatch runner-claude-token</c>,
/// is the only thing that ever calls it - and binding to this interface rather
/// than to where the token happens to be kept is what makes moving it a change
/// to one class.
/// </summary>
public interface IClaudeCredential
{
    /// <summary>The token as currently configured, or null when there is none. Never throws, and never distinguishes "not set" from "set to blank".</summary>
    Task<string?> GetTokenAsync(CancellationToken ct);
}

/// <summary>
/// The token as a site setting, read fresh on each call.
///
/// Read rather than captured for the reason <see cref="Photos.ImmichClient"/>
/// re-reads its host and key: the setting is editable from the admin page
/// while the app runs, and an OAuth token that expires is pasted in again far
/// more often than the process restarts.
///
/// It is stored obfuscated and redacted on read like every other secret-valued
/// setting - see SettingsController's <c>IsSecret</c>, which is the whole of
/// that job - so the token reaches this class and nothing else.
/// </summary>
public class SiteSettingClaudeCredential(ISiteSettingsService siteSettings) : IClaudeCredential
{
    public async Task<string?> GetTokenAsync(CancellationToken ct)
    {
        var settings = await siteSettings.GetAsync(ct);
        var token = settings.ClaudeSubscriptionToken;
        return string.IsNullOrWhiteSpace(token) ? null : token.Trim();
    }
}

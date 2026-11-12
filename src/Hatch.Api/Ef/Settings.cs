using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Hatch.Api.Ef;

/// <summary>Admin-editable scalar settings, replacing the "Dashboard" appsettings section. See SiteSettingKeys for the keys currently in use.</summary>
[Table("SiteSettings")]
public class EfSiteSetting
{
    [Key]
    public required string Key { get; set; }

    public required string Value { get; set; }
}

/// <summary>The well-known SiteSetting keys.</summary>
public static class SiteSettingKeys
{
    public const string TimeZone = "TimeZone";

    /// <summary>OAuth client credentials for the operator's own Google Cloud project, used for sign-in. Operator-supplied rather than shipped, since Hatch redeploys to other households (docs/ethos.md).</summary>
    public const string GoogleClientId = "GoogleClientId";
    public const string GoogleClientSecret = "GoogleClientSecret";

    /// <summary>
    /// Optional exact-match override for the OAuth redirect URI. When blank it's
    /// derived from the incoming request. It exists because Google compares the
    /// redirect URI byte-for-byte against a registered value, and a proxy can
    /// rewrite what the app believes its own host is.
    /// </summary>
    public const string GoogleOAuthRedirectUri = "GoogleOAuthRedirectUri";

    /// <summary>
    /// Anthropic API key. Operator-supplied like the Google credentials above:
    /// it is billed to whoever runs this house, so it is a setting rather than
    /// anything shipped (docs/ethos.md).
    /// </summary>
    public const string AnthropicApiKey = "AnthropicApiKey";

    /// <summary>
    /// What Hatch calls whoever is sitting at this machine, when the wall is
    /// off and there is nobody to authenticate - see LocalCaller. Unset means
    /// the value the app was started with, else a built-in default; the setting
    /// exists so the name can be changed without a restart.
    /// </summary>
    public const string LocalPersonName = "LocalPersonName";

    /// <summary>
    /// A Claude subscription OAuth token (<c>sk-ant-oat...</c>), used by Hatch
    /// to read the account's own headroom - see Modules/Hatch/AgentCredential.
    /// A setting rather than an environment variable for the reason the Google
    /// and Anthropic keys above are one, plus a reason of its own: it is
    /// genuinely optional, and the provisioning path cannot express an optional
    /// cluster secret. Blank means Hatch simply has no battery in its nav, which
    /// is a supported state and not an error.
    /// </summary>
    public const string ClaudeSubscriptionToken = "ClaudeSubscriptionToken";

    /// <summary>
    /// An OpenAI API key, which <c>codex exec</c> reads as <c>CODEX_API_KEY</c>.
    /// A setting rather than an environment variable, for the reason the Claude
    /// token above is one. Blank means the container has no Codex credential,
    /// which is a supported state and not an error.
    /// </summary>
    public const string CodexApiKey = "CodexApiKey";

    /// <summary>
    /// An xAI API key, which the Grok CLI reads as <c>XAI_API_KEY</c>. A setting
    /// rather than an environment variable, for the same reason. Blank means the
    /// container has no Grok credential, which is a supported state and not an
    /// error.
    /// </summary>
    public const string XaiApiKey = "XaiApiKey";

    /// <summary>A site-wide cap on tokens a runner with none of its own runs under.</summary>
    public const string RunnerDefaultMaxTokens = "RunnerDefaultMaxTokens";

    /// <summary>A site-wide cap on tokens per hour a runner with none of its own runs under.</summary>
    public const string RunnerDefaultMaxTokensPerHour = "RunnerDefaultMaxTokensPerHour";
}

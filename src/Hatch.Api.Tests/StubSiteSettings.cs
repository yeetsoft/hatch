using Hatch.Api.Services.Settings;

namespace Hatch.Api.Tests;

/// <summary>
/// A SiteSettings snapshot whose parameters are all defaulted, so a test names
/// only the handful of settings it is actually about. Shared across features
/// rather than per-folder: the snapshot is one record with many members, and a
/// copy per test area means every new setting is edited in several places.
/// </summary>
internal sealed class StubSiteSettings(
    string? googleClientId = "client-id.apps.googleusercontent.com",
    string? googleClientSecret = "GOCSPX-secret",
    string? googleOAuthRedirectUri = null,
    string timeZone = "America/New_York",
    string? anthropicApiKey = null,
    string? claudeSubscriptionToken = null,
    string? codexApiKey = null,
    string? xaiApiKey = null,
    string? localPersonName = null,
    long? runnerDefaultMaxTokens = null,
    long? runnerDefaultMaxTokensPerHour = null) : ISiteSettingsService
{
    public Task<SiteSettingsSnapshot> GetAsync(CancellationToken ct) => Task.FromResult(new SiteSettingsSnapshot(
        TimeZone: timeZone,
        WeatherEntity: null,
        GoogleClientId: googleClientId,
        GoogleClientSecret: googleClientSecret,
        GoogleOAuthRedirectUri: googleOAuthRedirectUri,
        AnthropicApiKey: anthropicApiKey,
        ClaudeSubscriptionToken: claudeSubscriptionToken,
        CodexApiKey: codexApiKey,
        XaiApiKey: xaiApiKey,
        LocalPersonName: localPersonName,
        RunnerDefaultMaxTokens: runnerDefaultMaxTokens,
        RunnerDefaultMaxTokensPerHour: runnerDefaultMaxTokensPerHour));

    /// <summary>Counted rather than ignored: SettingsController is supposed to call it on every write.</summary>
    public int Invalidations { get; private set; }

    public void Invalidate() => Invalidations++;
}

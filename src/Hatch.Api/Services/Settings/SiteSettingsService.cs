using System.Globalization;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hatch.Api.Services.Settings;

/// <summary>
/// Typed snapshot of the SiteSetting table. The five secrets are <see cref="OpenedSecret"/>s, so a
/// snapshot that is printed, logged or serialized shows <c>[secret]</c> and never a value.
/// </summary>
public record SiteSettingsSnapshot(
    string TimeZone,
    string? GoogleClientId,
    OpenedSecret? GoogleClientSecret,
    string? GoogleOAuthRedirectUri,
    OpenedSecret? AnthropicApiKey,
    OpenedSecret? ClaudeSubscriptionToken,
    OpenedSecret? CodexApiKey,
    OpenedSecret? XaiApiKey,
    string? LocalPersonName,
    long? RunnerDefaultMaxTokens,
    long? RunnerDefaultMaxTokensPerHour,
    IReadOnlySet<string>? UnreadableSecrets = null)
{
    /// <summary>Whether the setting <paramref name="key"/> holds a stored value that could not be opened.</summary>
    public bool IsUnreadable(string key) => UnreadableSecrets?.Contains(key) == true;
}

public interface ISiteSettingsService
{
    Task<SiteSettingsSnapshot> GetAsync(CancellationToken ct);

    /// <summary>
    /// Drops the cached snapshot so the next read comes from the table. Called
    /// by SettingsController on every write - see the note on
    /// <see cref="SiteSettingsService"/> for what it does and does not fix.
    /// </summary>
    void Invalidate();
}

/// <summary>
/// The read side of the site's scalar settings - SettingsController is the
/// write side, CRUDing EfSiteSetting directly.
///
/// Cached, and invalidated two ways because neither alone is enough. The
/// controller calls <see cref="Invalidate"/> on every write, which covers the
/// case that is actually visible to a person: saving a setting and immediately
/// reading back something derived from it, on the replica that took the write.
/// The TTL stays as the backstop for the other two replicas, which have no
/// signal and would otherwise serve the old value until something restarted -
/// so a stale read is bounded to CacheTtl rather than living forever, which is
/// what a bare in-memory cache would do.
/// </summary>
public class SiteSettingsService(IDbContextFactory<AppDbContext> dbFactory, TimeProvider time, SecretProtector protector, ILogger<SiteSettingsService> logger) : ISiteSettingsService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim gate = new(1, 1);
    private SiteSettingsSnapshot? cached;
    private DateTimeOffset expiresAt;

    // Setting key -> the stored text last warned about. Touched only while the
    // gate is held. Keeps the warning to once per setting until the stored text
    // changes, and a key that reads fine again is dropped so a later break warns.
    private readonly Dictionary<string, string> warnedAbout = [];

    public void Invalidate()
    {
        // Expiring rather than nulling, so a concurrent reader gets the old
        // snapshot for the moment before the reload lands instead of blocking
        // on a rebuild it did not ask for.
        expiresAt = DateTimeOffset.MinValue;
    }

    public async Task<SiteSettingsSnapshot> GetAsync(CancellationToken ct)
    {
        if (cached is { } fresh && time.GetUtcNow() < expiresAt) return fresh;

        await gate.WaitAsync(ct);
        try
        {
            if (cached is { } stillFresh && time.GetUtcNow() < expiresAt) return stillFresh;

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var values = await db.SiteSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);

            var unreadable = new HashSet<string>(StringComparer.Ordinal);

            var snapshot = new SiteSettingsSnapshot(
                TimeZone: values.GetValueOrDefault(SiteSettingKeys.TimeZone, "America/New_York"),
                GoogleClientId: NullIfEmpty(values.GetValueOrDefault(SiteSettingKeys.GoogleClientId)),
                // The read side has to hand back something usable, so the
                // client secret is opened here.
                GoogleClientSecret: Opened(SiteSettingKeys.GoogleClientSecret, values, unreadable),
                GoogleOAuthRedirectUri: NullIfEmpty(values.GetValueOrDefault(SiteSettingKeys.GoogleOAuthRedirectUri)),
                // Opened for the same reason as the Google secret above:
                // the game module has to hand it to an SDK, not display it.
                AnthropicApiKey: Opened(SiteSettingKeys.AnthropicApiKey, values, unreadable),
                ClaudeSubscriptionToken: Opened(SiteSettingKeys.ClaudeSubscriptionToken, values, unreadable),
                CodexApiKey: Opened(SiteSettingKeys.CodexApiKey, values, unreadable),
                XaiApiKey: Opened(SiteSettingKeys.XaiApiKey, values, unreadable),
                // No default: "nobody has said" is a distinct answer from any
                // name, and it is what tells the nav strip to explain how to
                // set one. LocalCaller decides what unset means.
                LocalPersonName: NullIfEmpty(values.GetValueOrDefault(SiteSettingKeys.LocalPersonName)),
                // Unset is null, and so is anything that is not a whole number
                // of at least one: a cleared default is stored as "", and a
                // hand-edited row must not read as a cap of zero.
                RunnerDefaultMaxTokens: ParseTokens(values, SiteSettingKeys.RunnerDefaultMaxTokens),
                RunnerDefaultMaxTokensPerHour: ParseTokens(values, SiteSettingKeys.RunnerDefaultMaxTokensPerHour),
                UnreadableSecrets: unreadable);

            cached = snapshot;
            expiresAt = time.GetUtcNow() + CacheTtl;
            return snapshot;
        }
        finally
        {
            gate.Release();
        }
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// Opens what SettingsController sealed, under the row it was sealed for. Unlike that controller's own
    /// call sites, this one is on the path of every settings read in the app,
    /// so a value that isn't readable - hand-edited in the DB, or written by a
    /// newer Hatch under a scheme this one doesn't know, or sealed under a key
    /// that is gone - degrades to "unset"
    /// rather than throwing out of the snapshot. SecretProtector.TryUnprotect
    /// already answers false for all of them, which is why this does not catch.
    ///
    /// A stored value that is not blank but does not open is recorded in
    /// <paramref name="unreadable"/> and warned about by name, once until the
    /// stored text changes. Neither the stored text nor any plaintext reaches the log.
    /// </summary>
    private OpenedSecret? Opened(string key, IReadOnlyDictionary<string, string> values, ISet<string> unreadable)
    {
        var stored = values.GetValueOrDefault(key);
        var opened = protector.TryUnprotect(stored, SecretStores.SiteSetting(key), out var secret);

        if (!opened)
        {
            unreadable.Add(key);
            if (!warnedAbout.TryGetValue(key, out var warned) || warned != stored)
            {
                warnedAbout[key] = stored;
                logger.LogWarning("Site setting {Setting} is stored but cannot be opened; enter it again on the Settings page", key);
            }
        }
        else
        {
            warnedAbout.Remove(key);
        }

        return secret;
    }

    private static long? ParseTokens(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var raw) && long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 1
            ? parsed
            : null;
}

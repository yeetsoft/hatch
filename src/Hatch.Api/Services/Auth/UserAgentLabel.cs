namespace Hatch.Api.Services.Auth;

/// <summary>
/// A name for a device that a person can recognise on the Sessions page -
/// "Chrome on macOS" rather than a wall of Mozilla/5.0. Deliberately a handful
/// of substring rules and not a user-agent parser: the label is a courtesy, the
/// raw string is kept beside it on the grant, and a wrong guess costs a
/// slightly vaguer name.
/// </summary>
public static class UserAgentLabel
{
    public const string Fallback = "Web browser";

    public static string From(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return Fallback;

        var browser = Browser(userAgent);
        var platform = Platform(userAgent);

        return (browser, platform) switch
        {
            (null, null) => Fallback,
            (null, _) => $"Browser on {platform}",
            (_, null) => browser,
            _ => $"{browser} on {platform}",
        };
    }

    // Order matters: Edge and Opera also say Chrome, and Chrome also says Safari.
    private static string? Browser(string ua) =>
        Has(ua, "Edg/") || Has(ua, "EdgA/") || Has(ua, "EdgiOS/") ? "Edge"
        : Has(ua, "OPR/") ? "Opera"
        : Has(ua, "Firefox/") || Has(ua, "FxiOS/") ? "Firefox"
        : Has(ua, "Chrome/") || Has(ua, "CriOS/") ? "Chrome"
        : Has(ua, "Safari/") ? "Safari"
        : null;

    // Order matters: iPhone user agents say "like Mac OS X", Android says Linux.
    private static string? Platform(string ua) =>
        Has(ua, "iPhone") ? "iPhone"
        : Has(ua, "iPad") ? "iPad"
        : Has(ua, "Android") ? "Android"
        : Has(ua, "Windows") ? "Windows"
        : Has(ua, "Mac OS X") || Has(ua, "Macintosh") ? "macOS"
        : Has(ua, "CrOS") ? "ChromeOS"
        : Has(ua, "Linux") ? "Linux"
        : null;

    private static bool Has(string ua, string needle) => ua.Contains(needle, StringComparison.Ordinal);
}

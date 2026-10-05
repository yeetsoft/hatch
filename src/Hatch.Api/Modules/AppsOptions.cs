using System.Text.RegularExpressions;

namespace Hatch.Api.Modules;

/// <summary>
/// Deploy-time config shared by the family shell's apps (the "Apps" appsettings
/// section). Registered in <see cref="ModuleRegistration.AddAppModules"/>
/// rather than Program.cs, so it stays part of the module seam.
/// </summary>
public class AppsOptions
{
    public const string SectionName = "Apps";

    /// <summary>
    /// Absolute base URL of this Hatch install as it should be *printed* -
    /// e.g. "https://home.example.com". Empty by default and supplied at
    /// deploy (compose.yaml passes <c>Apps__PublicBaseUrl</c> from the
    /// <c>HATCH_PUBLIC_URL</c> operator variable); deliberately never a literal
    /// in the repo, per docs/ethos.md.
    ///
    /// It exists because a QR label is not a link in a page: it's taped to a
    /// box for a decade, and it has to carry the canonical host rather than
    /// whichever hostname or IP the laptop that printed the sheet happened to
    /// be using. The same goes for a link that leaves Hatch - the issue link a
    /// dispatched session writes into a pull request - which a runner reaching
    /// Hatch as <c>http://api:8080</c> could not otherwise get right. Left empty,
    /// the runner links with the address it reaches Hatch at. Everything
    /// else in the shell is same-origin and needs no base URL at all.
    /// </summary>
    public string PublicBaseUrl { get; set; } = "";

    /// <summary>
    /// The value as a client should use it, or null if it is unset or unusable.
    /// Trailing slashes go (callers append rooted paths), and anything that
    /// isn't an absolute http(s) URL is rejected rather than passed on: a bare
    /// "home.example.com" printed into a QR gives a camera nothing to open, and
    /// that failure would only show up on paper.
    /// </summary>
    public static string? NormalizeBaseUrl(string? value)
    {
        var trimmed = value?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(trimmed)) return null;

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                ? trimmed
                : null;
    }

    /// <summary>
    /// The Android apps allowed to open this install's links without the user
    /// granting it by hand - deploy-time only, and never a literal in the repo,
    /// per docs/ethos.md (HA-265). Empty by default; <c>compose.yaml</c> passes
    /// <c>Apps__AssetLinks__0__…</c> from the operator's <c>HATCH_ANDROID_PACKAGE</c>
    /// and <c>HATCH_ANDROID_CERT_FINGERPRINT</c> variables. Unlike
    /// <see cref="PublicBaseUrl"/>, a bad entry here is not left to degrade quietly:
    /// <see cref="AssetLinksValidation"/> rejects it at startup, because Android's
    /// own verification already fails silently and the config feeding it must not.
    /// </summary>
    public AssetLinkPackage[] AssetLinks { get; set; } = [];

    public static bool IsValidPackageName(string value) =>
        Regex.IsMatch(value, @"^[a-zA-Z][a-zA-Z0-9_]*(\.[a-zA-Z][a-zA-Z0-9_]*)+$", RegexOptions.None, TimeSpan.FromSeconds(1));

    public static bool IsValidFingerprint(string value) =>
        Regex.IsMatch(value, @"^([0-9A-Fa-f]{2}:){31}[0-9A-Fa-f]{2}$", RegexOptions.None, TimeSpan.FromSeconds(1));
}

/// <summary>
/// A single statement's worth of <see cref="AppsOptions.AssetLinks"/>: one Android
/// app, identified by its package name, trusted for every certificate fingerprint
/// it was signed with (ordinarily one, until a key rotation needs both old and new
/// recognised at once).
/// </summary>
public class AssetLinkPackage
{
    public string PackageName { get; set; } = "";
    public string[] Sha256CertFingerprints { get; set; } = [];

    /// <summary>
    /// compose.yaml's blank-default slot: every field empty. The options binder has no
    /// way to represent "array index absent", so this is what an unconfigured phone
    /// binds to rather than an empty array - and it reads as unconfigured both here and
    /// in the route that serves it (AssetLinksController), not as the malformed entry it
    /// would be if only some of its fields were blank.
    /// </summary>
    public bool IsUnconfigured =>
        string.IsNullOrEmpty(PackageName) && Array.TrueForAll(Sha256CertFingerprints, string.IsNullOrEmpty);
}

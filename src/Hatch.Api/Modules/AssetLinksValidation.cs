using Microsoft.Extensions.Options;

namespace Hatch.Api.Modules;

/// <summary>
/// Walks <see cref="AppsOptions.AssetLinks"/> at startup (<c>.ValidateOnStart()</c>,
/// ModuleRegistration.cs) and fails loudly on a malformed entry, rather than letting
/// a bad package name or fingerprint reach <c>/.well-known/assetlinks.json</c> only to
/// fail Android's own, invisible verification later. See "Why validation differs from
/// PublicBaseUrl" on HA-265.
/// </summary>
public class AssetLinksValidation : IValidateOptions<AppsOptions>
{
    public ValidateOptionsResult Validate(string? name, AppsOptions options)
    {
        var assetLinks = options.AssetLinks;
        for (var i = 0; i < assetLinks.Length; i++)
        {
            var package = assetLinks[i];

            // compose.yaml's Apps__AssetLinks__0__... defaults to "${HATCH_ANDROID_PACKAGE:-}" -
            // blank until an operator has a phone to configure. The options binder turns that
            // into one entry with every field blank, not into an empty array (there is no way
            // to bind an array index "present but absent" otherwise), so a slot left blank in
            // every field reads as unconfigured - same as PublicBaseUrl empty - rather than as
            // the malformed entry it would be if only *some* of its fields were blank.
            if (string.IsNullOrEmpty(package.PackageName) && Array.TrueForAll(package.Sha256CertFingerprints, string.IsNullOrEmpty))
            {
                continue;
            }

            if (!AppsOptions.IsValidPackageName(package.PackageName))
            {
                return ValidateOptionsResult.Fail(
                    $"Apps:AssetLinks:{i}:PackageName '{package.PackageName}' is not a valid Android package name.");
            }

            if (package.Sha256CertFingerprints.Length == 0)
            {
                return ValidateOptionsResult.Fail(
                    $"Apps:AssetLinks:{i}:Sha256CertFingerprints is empty - at least one fingerprint is required.");
            }

            for (var j = 0; j < package.Sha256CertFingerprints.Length; j++)
            {
                var fingerprint = package.Sha256CertFingerprints[j];
                if (!AppsOptions.IsValidFingerprint(fingerprint))
                {
                    return ValidateOptionsResult.Fail(
                        $"Apps:AssetLinks:{i}:Sha256CertFingerprints:{j} '{fingerprint}' is not a valid SHA-256 certificate fingerprint.");
                }
            }
        }

        return ValidateOptionsResult.Success;
    }
}

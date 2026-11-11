using Hatch.Api.Modules.Hatch;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Modules;

/// <summary>
/// Walks <see cref="HatchOptions.AssetLinks"/> at startup (<c>.ValidateOnStart()</c>,
/// HatchModule.cs) and fails loudly on a malformed entry, rather than letting
/// a bad package name or fingerprint reach <c>/.well-known/assetlinks.json</c> only to
/// fail Android's own, invisible verification later. See "Why validation differs from
/// PublicBaseUrl" on HA-265.
/// </summary>
public class AssetLinksValidation : IValidateOptions<HatchOptions>
{
    public ValidateOptionsResult Validate(string? name, HatchOptions options)
    {
        var assetLinks = options.AssetLinks;
        for (var i = 0; i < assetLinks.Length; i++)
        {
            var package = assetLinks[i];

            if (package.IsUnconfigured)
            {
                continue;
            }

            if (!HatchOptions.IsValidPackageName(package.PackageName))
            {
                return ValidateOptionsResult.Fail(
                    $"Hatch:AssetLinks:{i}:PackageName '{package.PackageName}' is not a valid Android package name.");
            }

            if (package.Sha256CertFingerprints.Length == 0)
            {
                return ValidateOptionsResult.Fail(
                    $"Hatch:AssetLinks:{i}:Sha256CertFingerprints is empty - at least one fingerprint is required.");
            }

            for (var j = 0; j < package.Sha256CertFingerprints.Length; j++)
            {
                var fingerprint = package.Sha256CertFingerprints[j];
                if (!HatchOptions.IsValidFingerprint(fingerprint))
                {
                    return ValidateOptionsResult.Fail(
                        $"Hatch:AssetLinks:{i}:Sha256CertFingerprints:{j} '{fingerprint}' is not a valid SHA-256 certificate fingerprint.");
                }
            }
        }

        return ValidateOptionsResult.Success;
    }
}

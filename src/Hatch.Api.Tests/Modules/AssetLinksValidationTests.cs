using Hatch.Api.Modules;

namespace Hatch.Api.Tests.Modules;

/// <summary>
/// Direct construction, no host - the style <c>AppsConfigTests.cs</c> already uses for
/// <see cref="AppsOptions"/>. This is the one options class in the app validated at
/// startup rather than left to degrade lazily (see "Why validation differs from
/// PublicBaseUrl" on HA-265), so it gets its own coverage of what fails and why.
/// </summary>
public class AssetLinksValidationTests
{
    private const string FingerprintA = "AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99";
    private const string FingerprintB = "11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00";

    [Fact]
    public void Validate_Succeeds_ForOneValidPackage()
    {
        var options = Options(("com.example.hatch", [FingerprintA]));

        Assert.True(new AssetLinksValidation().Validate(null, options).Succeeded);
    }

    [Fact]
    public void Validate_Succeeds_ForTwoValidPackages()
    {
        var options = Options(
            ("com.example.hatch", [FingerprintA]),
            ("com.example.hatch.debug", [FingerprintB]));

        Assert.True(new AssetLinksValidation().Validate(null, options).Succeeded);
    }

    [Fact]
    public void Validate_Succeeds_ForAnEmptyList()
        => Assert.True(new AssetLinksValidation().Validate(null, new AppsOptions { AssetLinks = [] }).Succeeded);

    /// <summary>
    /// compose.yaml defaults the one wired slot to every field blank when nobody has
    /// configured a phone yet - that must start clean, not throw, or every install
    /// without the Android app crashes on boot.
    /// </summary>
    [Fact]
    public void Validate_Succeeds_ForASlotLeftEntirelyBlank()
        => Assert.True(new AssetLinksValidation().Validate(null, Options(("", [""]))).Succeeded);

    [Theory]
    [InlineData("hatch")]            // no dot
    [InlineData("1com.example")]     // leading digit
    [InlineData("")]                 // blank, but a fingerprint is configured - a real typo, not an unconfigured slot
    public void Validate_Fails_ForAMalformedPackageName(string packageName)
    {
        var result = new AssetLinksValidation().Validate(null, Options((packageName, [FingerprintA])));

        Assert.True(result.Failed);
        Assert.Contains("PackageName", result.FailureMessage);
        Assert.Contains("AssetLinks:0", result.FailureMessage);
    }

    [Theory]
    [InlineData("AA:BB:CC")]                                    // wrong length
    [InlineData("AABBCCDDEEFF00112233445566778899AABBCCDDEEFF0011223344556677889900")] // missing colons
    public void Validate_Fails_ForAMalformedFingerprint(string fingerprint)
    {
        var result = new AssetLinksValidation().Validate(null, Options(("com.example.hatch", [fingerprint])));

        Assert.True(result.Failed);
        Assert.Contains("Sha256CertFingerprints", result.FailureMessage);
        Assert.Contains("AssetLinks:0", result.FailureMessage);
    }

    [Fact]
    public void Validate_Accepts_LowercaseHex()
        => Assert.True(new AssetLinksValidation().Validate(null, Options(("com.example.hatch", [FingerprintA.ToLowerInvariant()]))).Succeeded);

    [Fact]
    public void Validate_Fails_ForAnEmptyFingerprintArray()
    {
        var result = new AssetLinksValidation().Validate(null, Options(("com.example.hatch", [])));

        Assert.True(result.Failed);
        Assert.Contains("Sha256CertFingerprints", result.FailureMessage);
        Assert.Contains("AssetLinks:0", result.FailureMessage);
    }

    private static AppsOptions Options(params (string PackageName, string[] Fingerprints)[] packages) => new()
    {
        AssetLinks = [.. packages.Select(p => new AssetLinkPackage
        {
            PackageName = p.PackageName,
            Sha256CertFingerprints = p.Fingerprints,
        })],
    };
}

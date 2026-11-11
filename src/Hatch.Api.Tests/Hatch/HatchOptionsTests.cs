using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The install's public address, which a pull request's link back to its ticket
/// is printed from. It gets its own tests because its only other feedback loop
/// is a link a reviewer cannot open.
/// </summary>
public class HatchOptionsTests
{
    [Theory]
    [InlineData("https://home.example.com", "https://home.example.com")]
    [InlineData("https://home.example.com/", "https://home.example.com")]
    [InlineData("  https://home.example.com//  ", "https://home.example.com")]
    [InlineData("http://192.168.1.10:8080", "http://192.168.1.10:8080")]
    // A path prefix survives: some installs sit behind a proxy on a sub-path.
    [InlineData("https://example.com/hatch", "https://example.com/hatch")]
    public void NormalizeBaseUrl_KeepsUsableValues_WithoutATrailingSlash(string configured, string expected)
        => Assert.Equal(expected, HatchOptions.NormalizeBaseUrl(configured));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("home.example.com")]     // no scheme - nothing to open
    [InlineData("/apps/hatch")]          // relative
    [InlineData("ftp://home.example.com")]
    public void NormalizeBaseUrl_RejectsWhatCannotBeOpened(string? configured)
        => Assert.Null(HatchOptions.NormalizeBaseUrl(configured));
}

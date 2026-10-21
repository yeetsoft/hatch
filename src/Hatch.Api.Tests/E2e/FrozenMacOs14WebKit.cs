namespace Hatch.Api.Tests.E2e;

/// <summary>
/// Whether this host is macOS 14, where Playwright's WebKit build is frozen and
/// never opens a page - docs/browser-testing.md, "WebKit on macOS 14 does not
/// open a page." Checked by the OS's own major version (OperatingSystem's
/// product-version API), not Darwin's kernel version, which runs nine higher
/// (Sonoma's kernel is Darwin 23) - so a macOS 15+ host still runs the phone
/// cases once Playwright ships it an unfrozen WebKit build.
/// </summary>
internal static class FrozenMacOs14WebKit
{
    public const string SkipReason =
        "Playwright's WebKit build for macOS 14 is frozen: it answers neither " +
        "Page.overrideSetting PushAPIEnabled nor Playwright.closePage, so opening a " +
        "page never returns. See docs/browser-testing.md, " +
        "\"WebKit on macOS 14 does not open a page.\"";

    public static bool Here =>
        OperatingSystem.IsMacOS()
        && OperatingSystem.IsMacOSVersionAtLeast(14)
        && !OperatingSystem.IsMacOSVersionAtLeast(15);
}

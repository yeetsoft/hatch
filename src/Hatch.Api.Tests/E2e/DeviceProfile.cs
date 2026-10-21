namespace Hatch.Api.Tests.E2e;

/// <summary>
/// A Playwright device by name, paired with the engine it needs. The .NET
/// descriptor itself does not carry this: <c>IPlaywright.Devices</c> is a
/// dictionary of <c>BrowserNewContextOptions</c>, which has no engine
/// property (checked by reflection against Microsoft.Playwright 1.63.0), so
/// the engine is named once here, beside the device name.
/// </summary>
internal sealed record DeviceProfile(string Device, string Engine)
{
    public static readonly DeviceProfile IPhone14Pro = new("iPhone 14 Pro", "webkit");
}

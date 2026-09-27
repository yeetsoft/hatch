using Hatch.Api.Services.Auth;

namespace Hatch.Api.Tests.Auth;

public class FirstAdminNoticeTests
{
    private static AuthOptions Options(bool enabled, bool google) => new()
    {
        Enabled = enabled,
        Google = google ? new GoogleAuthOptions { ClientId = "id", ClientSecret = "secret" } : new GoogleAuthOptions(),
    };

    [Fact]
    public void WallOffSaysNothing() => Assert.Null(FirstAdminNotice.For(Options(false, true), anyAdmin: false));

    [Fact]
    public void AnExistingAdminSaysNothing() => Assert.Null(FirstAdminNotice.For(Options(true, true), anyAdmin: true));

    [Fact]
    public void WallOnWithNoAdminSaysTheNextSignInBecomesTheAdministrator()
    {
        var notice = FirstAdminNotice.For(Options(true, true), anyAdmin: false);

        Assert.Contains("next person to sign in with Google becomes the Administrator", notice);
        Assert.DoesNotContain("not configured", notice);
        Assert.DoesNotContain("http", notice);
    }

    [Fact]
    public void WithGoogleUnsetItNamesTheSettings()
    {
        var notice = FirstAdminNotice.For(Options(true, false), anyAdmin: false);

        Assert.Contains("Auth__Google__ClientId", notice);
        Assert.Contains("Auth__Google__ClientSecret", notice);
        Assert.DoesNotContain("http", notice);
    }
}

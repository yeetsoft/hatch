namespace Hatch.Api.Services.Auth;

/// <summary>
/// What the migrate step says about how the first Administrator comes to
/// exist. Null when there is nothing to say: the wall is off, or somebody
/// already is one. Names settings, never an address.
/// </summary>
public static class FirstAdminNotice
{
    public static string? For(AuthOptions options, bool anyAdmin)
    {
        if (!options.Enabled || anyAdmin) return null;

        var message = "No Administrator exists yet. The next person to sign in with Google becomes the Administrator.";
        if (!options.Google.Configured)
            message += " Google sign-in is not configured: set Auth__Google__ClientId and Auth__Google__ClientSecret.";
        return message;
    }
}

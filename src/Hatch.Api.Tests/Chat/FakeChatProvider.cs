using Hatch.Api.Services.Chat;

namespace Hatch.Api.Tests.Chat;

/// <summary>
/// A chat provider that answers from a table of token to workspace, so nothing
/// that uses it calls a chat service. Any other token is refused with a fixed
/// sentence that does not echo it. The tokens are the shape of a real bot token
/// and name nobody, so a browser case can type them.
/// </summary>
internal sealed class FakeChatProvider : IChatProvider
{
    public const string AcmeToken = "xoxb-1111-acmeSecretPart";
    public const string AcmeNewToken = "xoxb-2222-acmeReplacementPart";
    public const string GlobexToken = "xoxb-3333-globexSecretPart";
    public const string RefusedToken = "xoxb-9999-refusedSecretPart";

    public const string RefusalSentence = "the fake refused that token";
    public const string ManifestText = "{\"fake\":true}";

    public Dictionary<string, (string Id, string Name)> Workspaces { get; } = new()
    {
        [AcmeToken] = ("TACME", "Acme"),
        [AcmeNewToken] = ("TACME", "Acme"),
        [GlobexToken] = ("TGLOBEX", "Globex"),
    };

    public int Checks { get; private set; }

    public string Name => "fake";

    public string Manifest() => ManifestText;

    public Task<ChatTokenCheck> CheckTokenAsync(string token, CancellationToken ct)
    {
        Checks++;

        return Task.FromResult(Workspaces.TryGetValue(token, out var w)
            ? ChatTokenCheck.Workspace(w.Id, w.Name)
            : ChatTokenCheck.Refused(RefusalSentence));
    }
}

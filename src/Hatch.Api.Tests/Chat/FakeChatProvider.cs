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

    /// <summary>Every channel a token can see, keyed by the token. A token with no entry sees none.</summary>
    public Dictionary<string, List<ChatChannel>> Channels { get; } = new();

    /// <summary>A sentence to refuse a channel id with, instead of posting to it.</summary>
    public Dictionary<string, string> PostRefusals { get; } = new();

    /// <summary>Every message posted, in order.</summary>
    public List<(string Token, string ChannelId, string Text)> Posts { get; } = [];

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

    public Task<ChatChannelList> ListChannelsAsync(string token, CancellationToken ct) =>
        Task.FromResult(Workspaces.ContainsKey(token)
            ? ChatChannelList.Of(Channels.GetValueOrDefault(token, []))
            : ChatChannelList.Refused(RefusalSentence));

    public Task<ChatPostResult> PostMessageAsync(string token, string channelId, string text, CancellationToken ct)
    {
        Posts.Add((token, channelId, text));

        return Task.FromResult(PostRefusals.TryGetValue(channelId, out var refusal)
            ? ChatPostResult.Refused(refusal)
            : ChatPostResult.Delivered());
    }
}

namespace Hatch.Api.Services.Chat;

/// <summary>
/// What Hatch needs from a chat service to connect a workspace: the app
/// manifest an admin pastes in, and a way to ask whether a token is good. The
/// controller knows nothing of any one service; the class that does is the
/// only place that names it.
/// </summary>
public interface IChatProvider
{
    /// <summary>The value stored in <c>ChatWorkspaces.Provider</c>.</summary>
    string Name { get; }

    /// <summary>The app manifest, as the JSON text to paste into the service's own screen.</summary>
    string Manifest();

    /// <summary>
    /// Asks the service who the token belongs to. Never throws for a refusal or
    /// an unreachable service, and never puts the token in what it returns.
    /// </summary>
    Task<ChatTokenCheck> CheckTokenAsync(string token, CancellationToken ct);

    /// <summary>
    /// Every channel the token can see, archived ones excluded. Fail-soft like
    /// <see cref="CheckTokenAsync"/>: never throws for a refusal, a throttle or
    /// a dead network.
    /// </summary>
    Task<ChatChannelList> ListChannelsAsync(string token, CancellationToken ct);

    /// <summary>
    /// Posts one message to a channel. Fail-soft like <see cref="CheckTokenAsync"/>.
    /// </summary>
    Task<ChatPostResult> PostMessageAsync(string token, string channelId, string text, CancellationToken ct);
}

/// <summary>
/// The answer to "is this token good": the workspace it belongs to, or one
/// plain sentence saying why not. Exactly one side is set.
/// </summary>
public record ChatTokenCheck(string? WorkspaceId, string? WorkspaceName, string? Refusal)
{
    public static ChatTokenCheck Workspace(string id, string name) => new(id, name, null);

    public static ChatTokenCheck Refused(string sentence) => new(null, null, sentence);

    public bool Accepted => Refusal is null && !string.IsNullOrEmpty(WorkspaceId);
}

/// <summary>One channel the token can see.</summary>
public record ChatChannel(string Id, string Name, bool IsPrivate, bool IsMember);

/// <summary>Every channel, or one plain sentence saying why not. Exactly one side is set.</summary>
public record ChatChannelList(IReadOnlyList<ChatChannel>? Channels, string? Refusal)
{
    public static ChatChannelList Of(IReadOnlyList<ChatChannel> channels) => new(channels, null);

    public static ChatChannelList Refused(string sentence) => new(null, sentence);
}

/// <summary>Whether a message was posted, or one plain sentence saying why not.</summary>
public record ChatPostResult(bool Posted, string? Refusal)
{
    public static ChatPostResult Delivered() => new(true, null);

    public static ChatPostResult Refused(string sentence) => new(false, sentence);
}

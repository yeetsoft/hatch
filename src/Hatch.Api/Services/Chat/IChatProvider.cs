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
    /// Asks the service whether an app-level token is good. The shape check is
    /// here, so a token of the wrong kind is refused without being sent. Fail-soft
    /// like <see cref="CheckTokenAsync"/>; the token, and any address the service
    /// answers with, is never in what it returns.
    /// </summary>
    Task<ChatAppTokenCheck> CheckAppTokenAsync(string appToken, CancellationToken ct);

    /// <summary>
    /// Every channel the token can see, archived ones excluded. Fail-soft like
    /// <see cref="CheckTokenAsync"/>: never throws for a refusal, a throttle or
    /// a dead network.
    /// </summary>
    Task<ChatChannelList> ListChannelsAsync(string token, CancellationToken ct);

    /// <summary>
    /// The member whose email is this one, or a null member when there is none
    /// (or the member is deactivated). A refusal is a sentence, not a missing
    /// member. Fail-soft like <see cref="CheckTokenAsync"/>; the email is never
    /// in what it returns, a URL or a log line.
    /// </summary>
    Task<ChatMemberFound> FindMemberByEmailAsync(string token, string email, CancellationToken ct);

    /// <summary>
    /// Every member a message could go to: deactivated members, bots and the
    /// service's own assistant are left out, and no email is returned. Fail-soft
    /// like <see cref="CheckTokenAsync"/>; a throttle stops the read and says how
    /// long it asked for, with no retry.
    /// </summary>
    Task<ChatMemberList> ListMembersAsync(string token, CancellationToken ct);

    /// <summary>
    /// Opens (or finds) the direct message conversation with a member and
    /// returns its id. Fail-soft like <see cref="CheckTokenAsync"/>.
    /// </summary>
    Task<ChatConversationOpened> OpenDirectMessageAsync(string token, string memberId, CancellationToken ct);

    /// <summary>
    /// Posts one message to a channel. Fail-soft like <see cref="CheckTokenAsync"/>.
    /// </summary>
    Task<ChatPostResult> PostMessageAsync(string token, string channelId, string text, CancellationToken ct);

    /// <summary>
    /// Posts a message to a channel, formatted the service's own way. Never
    /// throws, and never puts the token in what it returns.
    /// </summary>
    Task<ChatSendResult> PostAsync(string token, string channelId, ChatMessageContent content, CancellationToken ct);

    /// <summary>
    /// Edits a message already posted, in place. Never throws, and never puts
    /// the token in what it returns.
    /// </summary>
    Task<ChatSendResult> UpdateAsync(string token, string channelId, string messageId, ChatMessageContent content, CancellationToken ct);

    /// <summary>
    /// Answers one button press, in a reply only the presser sees, at the address
    /// the service gave for that press. The address is the credential, so there is
    /// no token. Never throws, and never puts the address in a log line or in what
    /// it returns. A reply that cannot be sent is dropped: the caller does not retry.
    /// </summary>
    Task<ChatSendResult> ReplyAsync(string replyUrl, ChatMessageContent content, CancellationToken ct);

    /// <summary>
    /// Opens the service's live connection with an app-level token and listens
    /// until it ends: <paramref name="opened"/> is awaited once the service says
    /// the connection is ready, and <paramref name="pressed"/> is called, and
    /// returns at once, for each button press. Every message the service sends is
    /// acknowledged before anything else is done with it. Never throws; both
    /// callbacks may, and a throw from one does not end the listening. The
    /// token, the connection address and a press's reply address are never in a
    /// log line, an exception or what it returns.
    /// </summary>
    Task<ChatListenEnd> ListenAsync(
        string appToken, Func<CancellationToken, Task> opened, Action<ChatPress> pressed, CancellationToken ct);
}

/// <summary>
/// One button press, as the service reported it. <see cref="ReplyUrl"/> is an
/// address the service gave for answering this press only: it is left out of
/// <see cref="ToString"/> so that logging a press never writes it down.
/// </summary>
/// <param name="MemberId">The service's id for the person who pressed.</param>
/// <param name="ChannelId">The channel the message is in.</param>
/// <param name="MessageId">The service's id for the message the button is on.</param>
/// <param name="ActionId">The id the button was given when the message was sent.</param>
/// <param name="Value">The value the button was given, or null when it had none.</param>
/// <param name="ReplyUrl">Where to answer this press. Never log it.</param>
public record ChatPress(string MemberId, string ChannelId, string MessageId, string ActionId, string? Value, string ReplyUrl)
{
    public override string ToString() =>
        $"ChatPress {{ MemberId = {MemberId}, ChannelId = {ChannelId}, MessageId = {MessageId}, ActionId = {ActionId}, Value = {Value} }}";
}

/// <summary>How a listen ended, in the terms the caller acts on.</summary>
public enum ChatListenEndKind
{
    /// <summary>The connection was working and ended: reopen it at once.</summary>
    Reopen,

    /// <summary>It could not be opened, or ended before it was ready: back off. <see cref="ChatListenEnd.RetryAfter"/> is how long a throttle asked for.</summary>
    Refused,

    /// <summary>The service says this app has the connection turned off: keep trying, at the longest wait.</summary>
    LinkDisabled,

    /// <summary>The caller cancelled.</summary>
    Stopped,
}

/// <summary>
/// How a listen ended. <see cref="Sentence"/> is plain, says why, and never
/// holds the token or an address. <see cref="RetryAfter"/> is how long a
/// throttle asked for, or null.
/// </summary>
public record ChatListenEnd(ChatListenEndKind Kind, string? Sentence, TimeSpan? RetryAfter)
{
    public static ChatListenEnd Reopen(string sentence) => new(ChatListenEndKind.Reopen, sentence, null);

    public static ChatListenEnd Refused(string sentence, TimeSpan? retryAfter = null) => new(ChatListenEndKind.Refused, sentence, retryAfter);

    public static ChatListenEnd LinkDisabled(string sentence) => new(ChatListenEndKind.LinkDisabled, sentence, null);

    public static ChatListenEnd Stopped() => new(ChatListenEndKind.Stopped, null, null);
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

/// <summary>
/// The answer to "is this app-level token good": accepted, or one plain
/// sentence saying why not. It carries no connection address.
/// </summary>
public record ChatAppTokenCheck(string? Refusal)
{
    public static ChatAppTokenCheck Accepted() => new((string?)null);

    public static ChatAppTokenCheck Refused(string sentence) => new(sentence);

    public bool IsAccepted => Refusal is null;
}

/// <summary>One channel the token can see.</summary>
public record ChatChannel(string Id, string Name, bool IsPrivate, bool IsMember);

/// <summary>Every channel, or one plain sentence saying why not. Exactly one side is set.</summary>
public record ChatChannelList(IReadOnlyList<ChatChannel>? Channels, string? Refusal)
{
    public static ChatChannelList Of(IReadOnlyList<ChatChannel> channels) => new(channels, null);

    public static ChatChannelList Refused(string sentence) => new(null, sentence);
}

/// <summary>One member of a workspace. Never carries an email.</summary>
/// <param name="Id">The service's own id for the member.</param>
/// <param name="Name">The member's full name, else their handle.</param>
/// <param name="DisplayName">The name they chose to be shown as, or null.</param>
public record ChatMember(string Id, string Name, string? DisplayName);

/// <summary>A member, or none, or one plain sentence saying why the lookup could not be made.</summary>
public record ChatMemberFound(ChatMember? Member, string? Refusal)
{
    public static ChatMemberFound Of(ChatMember? member) => new(member, null);

    public static ChatMemberFound Refused(string sentence) => new(null, sentence);
}

/// <summary>Every member, or one plain sentence saying why not. Exactly one side is set.</summary>
public record ChatMemberList(IReadOnlyList<ChatMember>? Members, string? Refusal)
{
    public static ChatMemberList Of(IReadOnlyList<ChatMember> members) => new(members, null);

    public static ChatMemberList Refused(string sentence) => new(null, sentence);
}

/// <summary>The id of a direct message conversation, or one plain sentence saying why not.</summary>
public record ChatConversationOpened(string? ConversationId, string? Refusal)
{
    public static ChatConversationOpened Of(string id) => new(id, null);

    public static ChatConversationOpened Refused(string sentence) => new(null, sentence);
}

/// <summary>Whether a message was posted, or one plain sentence saying why not.</summary>
public record ChatPostResult(bool Posted, string? Refusal)
{
    public static ChatPostResult Delivered() => new(true, null);

    public static ChatPostResult Refused(string sentence) => new(false, sentence);
}

/// <summary>How a send ended, in the terms the sender acts on.</summary>
public enum ChatSendOutcome
{
    /// <summary>The service took the message.</summary>
    Sent,

    /// <summary>The service asked for a pause. Not the message's fault.</summary>
    Throttled,

    /// <summary>The service could not be reached, or failed on its side.</summary>
    Unreachable,

    /// <summary>The token, or the app's permission, is not accepted: every room in the workspace is affected.</summary>
    WorkspaceRefused,

    /// <summary>This channel, or this message in it, is refused; the workspace is fine.</summary>
    RoomRefused,
}

/// <summary>
/// The answer to a post or an edit. <see cref="MessageId"/> is the service's id
/// for the message when it took one. <see cref="Sentence"/> is plain and never
/// holds the token. <see cref="RetryAfter"/> is how long a throttle asked for,
/// or null when it did not say.
/// </summary>
public record ChatSendResult(ChatSendOutcome Outcome, string? MessageId, string? Sentence, TimeSpan? RetryAfter)
{
    public static ChatSendResult Sent(string? messageId) => new(ChatSendOutcome.Sent, messageId, null, null);

    public static ChatSendResult Throttled(TimeSpan? retryAfter, string sentence) => new(ChatSendOutcome.Throttled, null, sentence, retryAfter);

    public static ChatSendResult Failed(ChatSendOutcome outcome, string sentence) => new(outcome, null, sentence, null);
}

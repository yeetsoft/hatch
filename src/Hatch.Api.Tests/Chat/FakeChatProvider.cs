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

    /// <summary>Every post and edit, in order, whatever the script answered.</summary>
    public List<SendCall> Sends { get; } = [];

    /// <summary>
    /// Answers each post or edit; may throw. Unset, a post is sent under a new
    /// id (<c>ts-1</c>, <c>ts-2</c>...) and an edit is sent under its own.
    /// </summary>
    public Func<SendCall, ChatSendResult>? Script { get; set; }

    /// <summary>Every member a token can list, keyed by the token. A token with no entry lists none.</summary>
    public Dictionary<string, List<ChatMember>> Members { get; } = new();

    /// <summary>Per token, the member each email belongs to. An email with no entry has no member.</summary>
    public Dictionary<string, Dictionary<string, ChatMember>> Emails { get; } = new();

    /// <summary>A sentence to refuse a token's member list and email lookups with.</summary>
    public Dictionary<string, string> MemberRefusals { get; } = new();

    /// <summary>A sentence to refuse opening a direct message to a member id with.</summary>
    public Dictionary<string, string> OpenRefusals { get; } = new();

    /// <summary>Every email looked up, in order.</summary>
    public List<(string Token, string Email)> Lookups { get; } = [];

    /// <summary>How many times the member list was read.</summary>
    public int MemberListReads { get; private set; }

    /// <summary>Every direct message opened, in order, whatever the answer.</summary>
    public List<(string Token, string MemberId)> Opens { get; } = [];

    private int posted;

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

    public Task<ChatMemberFound> FindMemberByEmailAsync(string token, string email, CancellationToken ct)
    {
        Lookups.Add((token, email));

        if (MemberRefusals.TryGetValue(token, out var refusal)) return Task.FromResult(ChatMemberFound.Refused(refusal));

        return Task.FromResult(ChatMemberFound.Of(
            Emails.TryGetValue(token, out var table) && table.TryGetValue(email, out var member) ? member : null));
    }

    public Task<ChatMemberList> ListMembersAsync(string token, CancellationToken ct)
    {
        MemberListReads++;

        if (MemberRefusals.TryGetValue(token, out var refusal)) return Task.FromResult(ChatMemberList.Refused(refusal));

        return Task.FromResult(ChatMemberList.Of(Members.GetValueOrDefault(token, [])));
    }

    public Task<ChatConversationOpened> OpenDirectMessageAsync(string token, string memberId, CancellationToken ct)
    {
        Opens.Add((token, memberId));

        return Task.FromResult(OpenRefusals.TryGetValue(memberId, out var refusal)
            ? ChatConversationOpened.Refused(refusal)
            : ChatConversationOpened.Of($"D-{memberId}"));
    }

    public Task<ChatPostResult> PostMessageAsync(string token, string channelId, string text, CancellationToken ct)
    {
        Posts.Add((token, channelId, text));

        return Task.FromResult(PostRefusals.TryGetValue(channelId, out var refusal)
            ? ChatPostResult.Refused(refusal)
            : ChatPostResult.Delivered());
    }

    public Task<ChatSendResult> PostAsync(string token, string channelId, ChatMessageContent content, CancellationToken ct) =>
        Send(new SendCall("post", token, channelId, null, content));

    public Task<ChatSendResult> UpdateAsync(string token, string channelId, string messageId, ChatMessageContent content, CancellationToken ct) =>
        Send(new SendCall("update", token, channelId, messageId, content));

    private Task<ChatSendResult> Send(SendCall call)
    {
        Sends.Add(call);

        if (Script is not null) return Task.FromResult(Script(call));

        return Task.FromResult(ChatSendResult.Sent(call.MessageId ?? $"ts-{++posted}"));
    }
}

/// <summary>One post (no message id) or edit (the id it edits), as the provider was asked.</summary>
internal sealed record SendCall(string Kind, string Token, string ChannelId, string? MessageId, ChatMessageContent Content);

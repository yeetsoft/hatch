using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Hatch.Api.Services.Chat;

/// <summary>
/// Slack, as a chat provider. The only class that names Slack: the controller
/// asks <see cref="IChatProvider"/> and gets a workspace or a sentence back.
///
/// Fail-soft, like <c>GoogleOAuthService</c>: a refusal, a throttle or a dead
/// network is a sentence, never an exception. The token travels in one header
/// and is never in a URL, a body, a log line or a sentence returned from here.
/// </summary>
public class SlackChatProvider(
    IHttpClientFactory httpClientFactory, ILogger<SlackChatProvider> logger, ISocketConnector? sockets = null) : IChatProvider
{
    private readonly ISocketConnector connector = sockets ?? new ClientWebSocketConnector();

    /// <summary>The named HttpClient registered in Program.cs. No BaseAddress - the call is absolute.</summary>
    public const string HttpClientName = "Slack";

    public const string ProviderName = "slack";

    public const string AuthTestEndpoint = "https://slack.com/api/auth.test";

    public const string ConversationsListEndpoint = "https://slack.com/api/conversations.list";

    public const string UsersLookupByEmailEndpoint = "https://slack.com/api/users.lookupByEmail";

    public const string UsersListEndpoint = "https://slack.com/api/users.list";

    public const string ConversationsOpenEndpoint = "https://slack.com/api/conversations.open";

    /// <summary>Asks Slack for a socket address. Used only to learn whether an app-level token is good; the address is never read.</summary>
    public const string AppsConnectionsOpenEndpoint = "https://slack.com/api/apps.connections.open";

    /// <summary>Slackbot reports <c>is_bot: false</c>, so it is named.</summary>
    public const string SlackbotId = "USLACKBOT";

    public const string PostMessageEndpoint = "https://slack.com/api/chat.postMessage";

    public const string UpdateMessageEndpoint = "https://slack.com/api/chat.update";

    /// <summary>
    /// The longest text Hatch sends, in characters after escaping. Slack accepts
    /// 4,000 and truncates past that; this leaves room.
    /// </summary>
    public const int MaxTextLength = 3500;

    /// <summary>The longest a title is allowed to run, after escaping.</summary>
    public const int MaxTitleLength = 300;

    private const string Ellipsis = "…";

    /// <summary>A bot token's prefix. A user token (<c>xoxp-</c>) would act as a person, which is not what is wanted.</summary>
    public const string BotTokenPrefix = "xoxb-";

    /// <summary>An app-level token's prefix, the kind that opens a socket connection.</summary>
    public const string AppTokenPrefix = "xapp-";

    public const string AppName = "Hatch";

    /// <summary>
    /// The bot scopes the app asks for, each with the Slack method that needs it.
    /// <c>chat:write</c> posts only to channels the bot has been added to.
    /// </summary>
    public static readonly IReadOnlyList<(string Scope, string Method)> BotScopes =
    [
        ("chat:write", "chat.postMessage, chat.update"),
        ("channels:read", "conversations.list (public channels)"),
        ("groups:read", "conversations.list (private channels the bot is in)"),
        ("users:read", "users.list"),
        ("users:read.email", "users.lookupByEmail"),
        ("im:write", "conversations.open (a direct message)"),
    ];

    private const string TransportSentence = "Slack could not be reached; try again";

    private static readonly IReadOnlyDictionary<string, string> Refusals = new Dictionary<string, string>
    {
        ["invalid_auth"] = "Slack did not accept that token; check it was copied whole",
        ["not_authed"] = "Slack did not receive a token",
        ["token_revoked"] = "That token has been revoked in Slack; install the app again for a new one",
        ["token_expired"] = "That token has expired; install the app again for a new one",
        ["account_inactive"] = "The Slack account that token belongs to is no longer active",
        ["not_in_channel"] = "the app has not been invited to that channel; in Slack, add Hatch to it",
        ["channel_not_found"] = "that channel could not be found",
        ["is_archived"] = "that channel is archived",
        ["missing_scope"] = "the app is missing a permission it needs; install it again",
        ["ratelimited"] = TransportSentence,
        ["message_not_found"] = "the message is gone from Slack, so it cannot be edited",
        ["cant_update_message"] = "Slack does not allow that message to be edited",
        ["edit_window_closed"] = "the time Slack allows for editing that message has passed",
        ["users_not_found"] = "Slack could not find that member",
        ["user_disabled"] = "that Slack member has been deactivated",
        ["cannot_dm_bot"] = "Slack does not allow a direct message to a bot",
    };

    /// <summary>
    /// What <c>apps.connections.open</c> refusals mean for an app-level token.
    /// The bot token sentences in <see cref="Refusals"/> say to install the app
    /// again, which would not help here.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> AppTokenRefusals = new Dictionary<string, string>
    {
        ["invalid_auth"] = "Slack did not accept that app-level token; check it was copied whole, or make a new one",
        ["not_authed"] = "Slack did not receive an app-level token; make a new one",
        ["token_revoked"] = "That app-level token has been revoked in Slack; make a new one",
        ["token_expired"] = "That app-level token has expired; make a new one",
        ["missing_scope"] = "That app-level token lacks the connections:write scope; make a new one with it",
    };

    private const string ThrottledSentence = "Slack asked Hatch to slow down; it will try again";

    private static readonly HashSet<string> ThrottleCodes = ["ratelimited", "rate_limited"];

    private static readonly HashSet<string> UnreachableCodes =
        ["service_unavailable", "internal_error", "fatal_error", "request_timeout"];

    private static readonly HashSet<string> WorkspaceCodes =
    [
        "invalid_auth", "not_authed", "token_revoked", "token_expired", "account_inactive", "missing_scope",
        "org_login_required", "team_access_not_granted", "ekm_access_denied",
    ];

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public string Name => ProviderName;

    public string Manifest()
    {
        var manifest = new Dictionary<string, object?>
        {
            ["display_information"] = new Dictionary<string, object?> { ["name"] = AppName },
            ["features"] = new Dictionary<string, object?>
            {
                ["bot_user"] = new Dictionary<string, object?> { ["display_name"] = AppName },
            },
            ["oauth_config"] = new Dictionary<string, object?>
            {
                ["scopes"] = new Dictionary<string, object?> { ["bot"] = BotScopes.Select(s => s.Scope).ToList() },
            },
            ["settings"] = new Dictionary<string, object?>
            {
                ["socket_mode_enabled"] = true,
                ["interactivity"] = new Dictionary<string, object?> { ["is_enabled"] = true },
                ["org_deploy_enabled"] = false,
                ["token_rotation_enabled"] = false,
            },
        };

        return JsonSerializer.Serialize(manifest, Indented);
    }

    public async Task<ChatAppTokenCheck> CheckAppTokenAsync(string appToken, CancellationToken ct)
    {
        // Before any request: a token of the wrong kind is not sent to Slack.
        if (!appToken.StartsWith(AppTokenPrefix, StringComparison.Ordinal))
            return ChatAppTokenCheck.Refused($"That is not an app-level token; a Slack app-level token starts with {AppTokenPrefix}");

        // No body. Only whether Slack said ok is read: the reply carries a
        // one-use socket address, which is never read, kept or logged.
        var reply = await CallAsync("apps.connections.open", appToken,
            () => new HttpRequestMessage(HttpMethod.Post, AppsConnectionsOpenEndpoint), ct);

        if (reply.Refusal is null) return ChatAppTokenCheck.Accepted();

        return ChatAppTokenCheck.Refused(AppTokenRefusalSentence(reply));
    }

    /// <summary>A refused <c>apps.connections.open</c>, in the words an app-level token needs.</summary>
    private static string AppTokenRefusalSentence(SlackReply reply) =>
        reply.Code is not null && AppTokenRefusals.TryGetValue(reply.Code, out var sentence) ? sentence : reply.Refusal!;

    private const string ClosedBeforeReadySentence = "Slack closed the connection before it was ready; try again";

    private const string ClosedSentence = "Slack closed the connection";

    private const string DroppedSentence = "The connection to Slack failed";

    public async Task<ChatListenEnd> ListenAsync(
        string appToken, Func<CancellationToken, Task> opened, Action<ChatPress> pressed, CancellationToken ct)
    {
        // Before any request, as CheckAppTokenAsync does: a token of the wrong kind is not sent.
        if (!appToken.StartsWith(AppTokenPrefix, StringComparison.Ordinal))
            return ChatListenEnd.Refused($"That is not an app-level token; a Slack app-level token starts with {AppTokenPrefix}");

        SlackReply reply;
        try
        {
            reply = await CallAsync("apps.connections.open", appToken,
                () => new HttpRequestMessage(HttpMethod.Post, AppsConnectionsOpenEndpoint), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return ChatListenEnd.Stopped();
        }

        if (reply.Refusal is not null) return ChatListenEnd.Refused(AppTokenRefusalSentence(reply), reply.RetryAfter);

        // The address is one-use and secret: it is read here and goes nowhere but the connector.
        if (Str(reply.Root!.Value, "url") is not { } url
            || !Uri.TryCreate(url, UriKind.Absolute, out var address)
            || address.Scheme != Uri.UriSchemeWss)
            return ChatListenEnd.Refused(Unreachable("Slack apps.connections.open named no usable address"));

        // A stop that lands while the open call is in flight must not go on to connect.
        if (ct.IsCancellationRequested) return ChatListenEnd.Stopped();

        ISocketConnection connection;
        try
        {
            connection = await connector.ConnectAsync(address, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return ChatListenEnd.Stopped();
        }
        catch (Exception ex)
        {
            // The exception's type alone: a WebSocketException's message can carry the address.
            logger.LogWarning("Slack socket could not be opened: {Kind}", ex.GetType().Name);
            return ChatListenEnd.Refused(TransportSentence);
        }

        await using (connection)
        {
            var ready = false;

            try
            {
                while (true)
                {
                    var text = await connection.ReceiveTextAsync(ct);
                    if (text is null)
                        return ready ? ChatListenEnd.Reopen(ClosedSentence) : ChatListenEnd.Refused(ClosedBeforeReadySentence);

                    JsonDocument document;
                    try
                    {
                        document = JsonDocument.Parse(text);
                    }
                    catch (JsonException)
                    {
                        // Not ours to read, and nothing from it is kept.
                        continue;
                    }

                    using (document)
                    {
                        var root = document.RootElement;
                        if (root.ValueKind != JsonValueKind.Object) continue;

                        // Slack waits three seconds for this, so it comes before anything else.
                        if (Str(root, "envelope_id") is { } envelope)
                            await connection.SendTextAsync(JsonSerializer.Serialize(new Dictionary<string, string> { ["envelope_id"] = envelope }), ct);

                        switch (Str(root, "type"))
                        {
                            case "hello":
                                ready = true;
                                try
                                {
                                    await opened(ct);
                                }
                                catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
                                {
                                    logger.LogWarning("The Slack listener's opened callback failed: {Kind}", ex.GetType().Name);
                                }

                                break;

                            case "disconnect":
                                var linkDisabled = Str(root, "reason") == "link_disabled";
                                await connection.CloseAsync();

                                return linkDisabled
                                    ? ChatListenEnd.LinkDisabled("Socket Mode is turned off in the Slack app; turn it on to hear button presses")
                                    : ChatListenEnd.Reopen("Slack asked Hatch to reconnect");

                            case "interactive":
                                if (PressOf(root) is { } press)
                                {
                                    try
                                    {
                                        pressed(press);
                                    }
                                    catch (Exception ex)
                                    {
                                        logger.LogWarning("The Slack listener's pressed callback failed: {Kind}", ex.GetType().Name);
                                    }
                                }

                                break;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                await connection.CloseAsync();
                return ChatListenEnd.Stopped();
            }
            catch (SocketMessageTooLargeException)
            {
                logger.LogWarning("Slack sent a message over the size allowed");
                return ready ? ChatListenEnd.Reopen(DroppedSentence) : ChatListenEnd.Refused(DroppedSentence);
            }
            catch (Exception ex) when (ex is WebSocketException or IOException or ObjectDisposedException)
            {
                // The type alone, never the message.
                logger.LogWarning("The Slack socket failed: {Kind}", ex.GetType().Name);
                return ready ? ChatListenEnd.Reopen(DroppedSentence) : ChatListenEnd.Refused(DroppedSentence);
            }
        }
    }

    /// <summary>
    /// The first action of a <c>block_actions</c> payload, or null for any other
    /// payload and for a press missing a part. Only <c>value</c> may be missing.
    /// </summary>
    private static ChatPress? PressOf(JsonElement envelope)
    {
        if (!envelope.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object) return null;
        if (Str(payload, "type") != "block_actions") return null;

        var member = payload.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object ? Str(user, "id") : null;
        var channel = payload.TryGetProperty("channel", out var c) && c.ValueKind == JsonValueKind.Object ? Str(c, "id") : null;
        var message =
            (payload.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.Object ? Str(m, "ts") : null)
            ?? (payload.TryGetProperty("container", out var k) && k.ValueKind == JsonValueKind.Object ? Str(k, "message_ts") : null);
        var replyUrl = Str(payload, "response_url");

        if (!payload.TryGetProperty("actions", out var actions) || actions.ValueKind != JsonValueKind.Array || actions.GetArrayLength() == 0)
            return null;

        var action = actions[0];
        if (action.ValueKind != JsonValueKind.Object) return null;

        var actionId = Str(action, "action_id");
        if (member is null || channel is null || message is null || actionId is null || replyUrl is null) return null;

        return new ChatPress(member, channel, message, actionId, Str(action, "value"), replyUrl);
    }

    public async Task<ChatTokenCheck> CheckTokenAsync(string token, CancellationToken ct)
    {
        if (!token.StartsWith(BotTokenPrefix, StringComparison.Ordinal))
            return ChatTokenCheck.Refused($"That is not a bot token; a Slack bot token starts with {BotTokenPrefix}");

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, AuthTestEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, ct);

            if ((int)response.StatusCode == 429)
            {
                logger.LogWarning("Slack auth.test was throttled");
                return ChatTokenCheck.Refused(TransportSentence);
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return ChatTokenCheck.Refused(Unreachable($"Slack auth.test answered {(int)response.StatusCode} with no object"));

            if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
            {
                var id = Str(root, "team_id");
                if (id is null) return ChatTokenCheck.Refused(Unreachable("Slack auth.test accepted the token but named no workspace"));

                return ChatTokenCheck.Workspace(id, Str(root, "team") ?? id);
            }

            var code = Str(root, "error") ?? $"http_{(int)response.StatusCode}";
            // The code, never the body.
            logger.LogWarning("Slack auth.test refused a token: {Code}", code);

            return ChatTokenCheck.Refused(RefusalSentenceFor(code, "Slack refused the token"));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // The exception's type alone: a message from the HTTP stack can carry the request.
            logger.LogWarning("Slack auth.test could not be completed: {Kind}", ex.GetType().Name);
            return ChatTokenCheck.Refused(TransportSentence);
        }
    }

    public async Task<ChatChannelList> ListChannelsAsync(string token, CancellationToken ct)
    {
        var channels = new List<ChatChannel>();
        var seenCursors = new HashSet<string>();
        string? cursor = null;

        while (true)
        {
            var url = $"{ConversationsListEndpoint}?types=public_channel,private_channel&exclude_archived=true&limit=200"
                + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");

            try
            {
                var client = httpClientFactory.CreateClient(HttpClientName);
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var response = await client.SendAsync(request, ct);

                if ((int)response.StatusCode == 429)
                {
                    logger.LogWarning("Slack conversations.list was throttled");
                    return ChatChannelList.Refused(TransportSentence);
                }

                var body = await response.Content.ReadAsStringAsync(ct);
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return ChatChannelList.Refused(Unreachable($"Slack conversations.list answered {(int)response.StatusCode} with no object"));

                if (!(root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True))
                {
                    var code = Str(root, "error") ?? $"http_{(int)response.StatusCode}";
                    logger.LogWarning("Slack conversations.list was refused: {Code}", code);
                    return ChatChannelList.Refused(RefusalSentenceFor(code, "Slack refused"));
                }

                if (root.TryGetProperty("channels", out var list) && list.ValueKind == JsonValueKind.Array)
                    foreach (var item in list.EnumerateArray())
                    {
                        var id = Str(item, "id");
                        if (id is null) continue;

                        channels.Add(new ChatChannel(id, Str(item, "name") ?? id, Bool(item, "is_private"), Bool(item, "is_member")));
                    }

                var next = root.TryGetProperty("response_metadata", out var meta) ? Str(meta, "next_cursor") : null;
                if (string.IsNullOrEmpty(next) || !seenCursors.Add(next)) break;

                cursor = next;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                logger.LogWarning("Slack conversations.list could not be completed: {Kind}", ex.GetType().Name);
                return ChatChannelList.Refused(TransportSentence);
            }
        }

        return ChatChannelList.Of(channels);
    }

    public async Task<ChatMemberFound> FindMemberByEmailAsync(string token, string email, CancellationToken ct)
    {
        // In the body, never the URL: a URL is what a proxy and a log keep.
        var reply = await CallAsync("users.lookupByEmail", token,
            () => new HttpRequestMessage(HttpMethod.Post, UsersLookupByEmailEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["email"] = email }),
            }, ct);

        // No member, or a deactivated one: an answer, not a refusal.
        if (reply.Code == "users_not_found") return ChatMemberFound.Of(null);
        if (reply.Refusal is not null) return ChatMemberFound.Refused(reply.Refusal);

        return ChatMemberFound.Of(
            reply.Root!.Value.TryGetProperty("user", out var user) && MemberOf(user) is { } member ? member : null);
    }

    public async Task<ChatMemberList> ListMembersAsync(string token, CancellationToken ct)
    {
        var members = new List<ChatMember>();
        var seenCursors = new HashSet<string>();
        string? cursor = null;

        while (true)
        {
            var url = $"{UsersListEndpoint}?limit=200" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var reply = await CallAsync("users.list", token, () => new HttpRequestMessage(HttpMethod.Get, url), ct);

            // A refusal or a throttle ends the read. A throttle says how long
            // Slack asked for: no retry, no sleep.
            if (reply.Refusal is not null) return ChatMemberList.Refused(reply.Refusal);

            var root = reply.Root!.Value;
            if (root.TryGetProperty("members", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var item in list.EnumerateArray())
                    if (MemberOf(item) is { } member) members.Add(member);

            var next = root.TryGetProperty("response_metadata", out var meta) ? Str(meta, "next_cursor") : null;
            if (string.IsNullOrEmpty(next) || !seenCursors.Add(next)) break;

            cursor = next;
        }

        return ChatMemberList.Of(members);
    }

    public async Task<ChatConversationOpened> OpenDirectMessageAsync(string token, string memberId, CancellationToken ct)
    {
        var reply = await CallAsync("conversations.open", token,
            () => new HttpRequestMessage(HttpMethod.Post, ConversationsOpenEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["users"] = memberId }),
            }, ct);

        if (reply.Refusal is not null) return ChatConversationOpened.Refused(reply.Refusal);

        return reply.Root!.Value.TryGetProperty("channel", out var channel) && Str(channel, "id") is { } id
            ? ChatConversationOpened.Of(id)
            : ChatConversationOpened.Refused(Unreachable("Slack conversations.open accepted the call and named no conversation"));
    }

    /// <summary>
    /// What came back from one call: the answer's object, or a sentence. Slack's
    /// error <see cref="Code"/> is kept for a caller that treats one as an answer;
    /// it is never the body, and never the request.
    /// </summary>
    private sealed record SlackReply(JsonElement? Root, string? Refusal, string? Code, TimeSpan? RetryAfter = null);

    /// <summary>
    /// One call, fail-soft: a refusal, a throttle, a dead network or an answer
    /// that is not an object is a sentence in the reply, never an exception.
    /// Logs the method and Slack's error code, nothing from the request.
    /// </summary>
    private async Task<SlackReply> CallAsync(string method, string token, Func<HttpRequestMessage> build, CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = build();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, ct);

            if ((int)response.StatusCode == 429)
            {
                logger.LogWarning("Slack {Method} was throttled", method);
                var after = RetryAfterOf(response);
                return new SlackReply(null, ThrottleSentence(after), "ratelimited", after);
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new SlackReply(null, Unreachable($"Slack {method} answered {(int)response.StatusCode} with no object"), null);

            if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
                return new SlackReply(root.Clone(), null, null);

            var code = Str(root, "error") ?? $"http_{(int)response.StatusCode}";
            // The code, never the body.
            logger.LogWarning("Slack {Method} was refused: {Code}", method, code);

            if (ThrottleCodes.Contains(code))
            {
                var after = RetryAfterOf(response);
                return new SlackReply(null, ThrottleSentence(after), code, after);
            }

            return new SlackReply(null, RefusalSentenceFor(code, "Slack refused"), code);
        }
        catch (Exception ex) when ((ex is HttpRequestException or JsonException or TaskCanceledException) && !ct.IsCancellationRequested)
        {
            // The exception's type alone: a message from the HTTP stack can carry the request.
            logger.LogWarning("Slack {Method} could not be completed: {Kind}", method, ex.GetType().Name);
            return new SlackReply(null, TransportSentence, null);
        }
    }

    private static string ThrottleSentence(TimeSpan? after) =>
        after is { } wait
            ? $"Slack asked Hatch to wait {Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds))} seconds before asking again"
            : "Slack asked Hatch to slow down; try again shortly";

    /// <summary>
    /// A member a message could go to, or null for one that cannot take it:
    /// deactivated, a bot, an app's user, or Slackbot. No email is read.
    /// </summary>
    private static ChatMember? MemberOf(JsonElement user)
    {
        if (user.ValueKind != JsonValueKind.Object) return null;

        var id = Str(user, "id");
        if (id is null || id == SlackbotId) return null;
        if (Bool(user, "deleted") || Bool(user, "is_bot") || Bool(user, "is_app_user")) return null;

        JsonElement? profile = user.TryGetProperty("profile", out var p) && p.ValueKind == JsonValueKind.Object ? p : null;
        var name = Str(user, "real_name") ?? (profile is { } a ? Str(a, "real_name") : null) ?? Str(user, "name") ?? id;
        var display = profile is { } b ? Str(b, "display_name") : null;

        return new ChatMember(id, name, display);
    }

    public async Task<ChatPostResult> PostMessageAsync(string token, string channelId, string text, CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, PostMessageEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["channel"] = channelId, ["text"] = text }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, ct);

            if ((int)response.StatusCode == 429)
            {
                logger.LogWarning("Slack chat.postMessage was throttled");
                return ChatPostResult.Refused(TransportSentence);
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return ChatPostResult.Refused(Unreachable($"Slack chat.postMessage answered {(int)response.StatusCode} with no object"));

            if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True) return ChatPostResult.Delivered();

            var code = Str(root, "error") ?? $"http_{(int)response.StatusCode}";
            logger.LogWarning("Slack chat.postMessage was refused: {Code}", code);

            return ChatPostResult.Refused(RefusalSentenceFor(code, "Slack refused"));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Slack chat.postMessage could not be completed: {Kind}", ex.GetType().Name);
            return ChatPostResult.Refused(TransportSentence);
        }
    }

    public Task<ChatSendResult> PostAsync(string token, string channelId, ChatMessageContent content, CancellationToken ct) =>
        SendAsync(PostMessageEndpoint, "chat.postMessage", token,
            new Dictionary<string, string> { ["channel"] = channelId }, content, ct);

    public Task<ChatSendResult> UpdateAsync(string token, string channelId, string messageId, ChatMessageContent content, CancellationToken ct) =>
        SendAsync(UpdateMessageEndpoint, "chat.update", token,
            new Dictionary<string, string> { ["channel"] = channelId, ["ts"] = messageId }, content, ct);

    public async Task<ChatSendResult> ReplyAsync(string replyUrl, ChatMessageContent content, CancellationToken ct)
    {
        // The address came over Slack's own socket. It is the only credential this call has, so it is
        // never logged, never in a sentence, and never sent anywhere that is not https.
        if (!Uri.TryCreate(replyUrl, UriKind.Absolute, out var address) || address.Scheme != Uri.UriSchemeHttps)
            return ChatSendResult.Failed(ChatSendOutcome.RoomRefused, "Slack gave no usable address to reply to");

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            var payload = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["text"] = Format(content),
                ["response_type"] = "ephemeral",
                ["replace_original"] = false,
            });
            using var request = new HttpRequestMessage(HttpMethod.Post, address)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            using var response = await client.SendAsync(request, ct);
            var status = (int)response.StatusCode;

            if (status is >= 200 and < 300) return ChatSendResult.Sent(null);

            if (status == 429)
            {
                logger.LogWarning("Slack press reply was throttled");
                return ChatSendResult.Throttled(RetryAfterOf(response), "Slack asked Hatch to slow down; this reply was dropped");
            }

            if (status >= 500)
            {
                logger.LogWarning("Slack press reply answered {Status}", status);
                return ChatSendResult.Failed(ChatSendOutcome.Unreachable, TransportSentence);
            }

            // Slack answers a refusal with a plain-text code. Anything else is not read.
            var body = (await response.Content.ReadAsStringAsync(ct)).Trim();
            var code = System.Text.RegularExpressions.Regex.IsMatch(body, "^[a-z_]{1,40}$") ? body : $"http_{status}";
            logger.LogWarning("Slack press reply was refused: {Code}", code);
            return ChatSendResult.Failed(ChatSendOutcome.RoomRefused, RefusalSentenceFor(code, "Slack refused"));
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested)
        {
            // The exception's type alone: its message can carry the address.
            logger.LogWarning("Slack press reply could not be completed: {Kind}", ex.GetType().Name);
            return ChatSendResult.Failed(ChatSendOutcome.Unreachable, TransportSentence);
        }
    }

    private async Task<ChatSendResult> SendAsync(
        string endpoint, string method, string token, Dictionary<string, string> form, ChatMessageContent content, CancellationToken ct)
    {
        form["text"] = Format(content);
        form["unfurl_links"] = "false";
        form["unfurl_media"] = "false";

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new FormUrlEncodedContent(form) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, ct);
            var status = (int)response.StatusCode;

            if (status == 429)
            {
                logger.LogWarning("Slack {Method} was throttled", method);
                return ChatSendResult.Throttled(RetryAfterOf(response), ThrottledSentence);
            }

            if (status >= 500)
            {
                logger.LogWarning("Slack {Method} answered {Status}", method, status);
                return ChatSendResult.Failed(ChatSendOutcome.Unreachable, TransportSentence);
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                logger.LogWarning("Slack {Method} answered {Status} with no object", method, status);
                return ChatSendResult.Failed(ChatSendOutcome.Unreachable, TransportSentence);
            }

            if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
            {
                var id = Str(root, "ts");
                if (id is null) logger.LogWarning("Slack {Method} accepted a message and named no id", method);

                return ChatSendResult.Sent(id);
            }

            var code = Str(root, "error") ?? $"http_{status}";
            // The code, never the body.
            logger.LogWarning("Slack {Method} was refused: {Code}", method, code);

            if (ThrottleCodes.Contains(code)) return ChatSendResult.Throttled(RetryAfterOf(response), ThrottledSentence);
            if (UnreachableCodes.Contains(code)) return ChatSendResult.Failed(ChatSendOutcome.Unreachable, TransportSentence);

            return ChatSendResult.Failed(
                WorkspaceCodes.Contains(code) ? ChatSendOutcome.WorkspaceRefused : ChatSendOutcome.RoomRefused,
                RefusalSentenceFor(code, "Slack refused"));
        }
        catch (Exception ex) when ((ex is HttpRequestException or JsonException or TaskCanceledException) && !ct.IsCancellationRequested)
        {
            // The exception's type alone: a message from the HTTP stack can carry the request.
            logger.LogWarning("Slack {Method} could not be completed: {Kind}", method, ex.GetType().Name);
            return ChatSendResult.Failed(ChatSendOutcome.Unreachable, TransportSentence);
        }
    }

    private static TimeSpan? RetryAfterOf(HttpResponseMessage response) =>
        response.Headers.RetryAfter?.Delta is { } delta && delta >= TimeSpan.Zero ? delta : null;

    /// <summary>
    /// A message as Slack text: the title in bold, the body, each line on its
    /// own line, then the links on a last line. Everything that came from a
    /// ticket is escaped so it cannot mention, link or break the text; a URL is
    /// Hatch's own and goes in as it is. A message too long is cut at the body,
    /// then the lines, and the links are always kept.
    /// </summary>
    internal static string Format(ChatMessageContent content)
    {
        var title = "*" + Cut(OneLine(content.Title), MaxTitleLength - 2) + "*";
        var links = LinksLine(content.Links);

        var room = Math.Max(0, MaxTextLength - title.Length - (links is null ? 0 : links.Length + 1));

        var body = string.IsNullOrWhiteSpace(content.Body) ? null : content.Body.Trim();
        var lines = content.Lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

        // Each part costs its own length and the newline before it.
        var bodyCost = body is null ? 0 : Escape(body).Length + 1;
        var linesCost = lines.Sum(l => Escape(l).Length + 1);

        var parts = new List<string>();
        if (bodyCost + linesCost <= room)
        {
            if (body is not null) parts.Add(Escape(body));
            parts.AddRange(lines.Select(Escape));
        }
        else if (body is not null && room - linesCost - 1 >= MinCut)
        {
            // The body gives way first.
            parts.Add(Cut(body, room - linesCost - 1));
            parts.AddRange(lines.Select(Escape));
        }
        else
        {
            // The body is gone, and the lines give way from the last.
            var left = room;
            foreach (var line in lines)
            {
                var escaped = Escape(line);
                if (escaped.Length + 1 <= left)
                {
                    parts.Add(escaped);
                    left -= escaped.Length + 1;
                    continue;
                }

                if (left - 1 >= MinCut) parts.Add(Cut(line, left - 1));
                break;
            }
        }

        var text = new StringBuilder(title);
        foreach (var part in parts) text.Append('\n').Append(part);
        if (links is not null) text.Append('\n').Append(links);

        return text.ToString();
    }

    /// <summary>A cut shorter than this is not worth showing.</summary>
    private const int MinCut = 20;

    private static string? LinksLine(IReadOnlyList<ChatLink> links)
    {
        var shown = links
            .Where(l => !string.IsNullOrWhiteSpace(l.Label))
            .Select(l => l.Url is { } url && IsSafeUrl(url)
                ? $"<{url}|{Cut(OneLine(l.Label), 80)}>"
                : Cut(OneLine(l.Label), 80))
            .ToList();

        return shown.Count == 0 ? null : string.Join(" · ", shown);
    }

    // A URL here is Hatch's own, but one with a character that ends a Slack link early would turn the rest into text.
    private static bool IsSafeUrl(string url) =>
        url.Length > 0 && !url.Any(c => c is '<' or '>' or '|' || char.IsWhiteSpace(c));

    private static string OneLine(string text) =>
        text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();

    /// <summary>Slack's three characters, the ampersand first so the entities are not escaped again.</summary>
    private static string Escape(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>
    /// The text escaped, at most <paramref name="max"/> characters long,
    /// ending in an ellipsis when it was cut. Measured after escaping and cut
    /// between characters, so no entity is split and no pair is broken.
    /// </summary>
    private static string Cut(string text, int max)
    {
        var whole = Escape(text);
        if (whole.Length <= max) return whole;

        var cut = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            var piece = Escape(rune.ToString());
            if (cut.Length + piece.Length > max - Ellipsis.Length) break;
            cut.Append(piece);
        }

        return cut.Append(Ellipsis).ToString();
    }

    private string Unreachable(string why)
    {
        logger.LogWarning("{Why}", why);
        return TransportSentence;
    }

    private static string RefusalSentenceFor(string code, string fallbackPrefix) =>
        Refusals.TryGetValue(code, out var sentence) ? sentence : $"{fallbackPrefix}: {code}";

    private static string? Str(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static bool Bool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}

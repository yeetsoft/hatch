using System.Net.Http.Headers;
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
public class SlackChatProvider(IHttpClientFactory httpClientFactory, ILogger<SlackChatProvider> logger) : IChatProvider
{
    /// <summary>The named HttpClient registered in Program.cs. No BaseAddress - the call is absolute.</summary>
    public const string HttpClientName = "Slack";

    public const string ProviderName = "slack";

    public const string AuthTestEndpoint = "https://slack.com/api/auth.test";

    public const string ConversationsListEndpoint = "https://slack.com/api/conversations.list";

    public const string PostMessageEndpoint = "https://slack.com/api/chat.postMessage";

    /// <summary>A bot token's prefix. A user token (<c>xoxp-</c>) would act as a person, which is not what is wanted.</summary>
    public const string BotTokenPrefix = "xoxb-";

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
    };

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
                ["socket_mode_enabled"] = false,
                ["org_deploy_enabled"] = false,
                ["token_rotation_enabled"] = false,
            },
        };

        return JsonSerializer.Serialize(manifest, Indented);
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

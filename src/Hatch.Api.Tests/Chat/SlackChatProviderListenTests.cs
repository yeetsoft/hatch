using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text.Json;
using Hatch.Api.Services.Chat;
using Hatch.Api.Tests.Hatch;
using Microsoft.Extensions.Logging;

namespace Hatch.Api.Tests.Chat;

/// <summary>
/// The Socket Mode listener, over a fake socket and a stub for the call that
/// opens it: nothing here reaches Slack or opens a real connection. The thin
/// <c>ClientWebSocket</c> wrapper itself has no test, for that reason. The field
/// names and disconnect reasons are as remembered from Slack's documentation,
/// so these tests pin them and a real workspace is the check.
/// </summary>
public class SlackChatProviderListenTests
{
    private const string AppToken = "xapp-1-A111-222-secret-app-token";
    private const string Address = "wss://wss-primary.example.test/link/?ticket=secret-ticket&app_id=A111";
    private const string ReplyUrl = "https://hooks.example.test/actions/secret-reply";

    private static readonly string OpenReply = $$"""{"ok":true,"url":"{{Address}}"}""";

    private const string Hello = """{"type":"hello","num_connections":1}""";

    private static string Press(string? messageTs = "1700000000.000100", string value = "\"yes\"", string? replyUrl = ReplyUrl) =>
        "{\"envelope_id\":\"env-1\",\"type\":\"interactive\",\"accepts_response_payload\":false,\"payload\":{"
        + "\"type\":\"block_actions\",\"user\":{\"id\":\"U1\"},\"channel\":{\"id\":\"C1\"},"
        + (messageTs is null ? "" : $"\"message\":{{\"ts\":\"{messageTs}\"}},")
        + (replyUrl is null ? "" : $"\"response_url\":\"{replyUrl}\",")
        + $"\"actions\":[{{\"action_id\":\"answer\",\"value\":{value}}}]}}}}";

    // ---- opening ----

    [Fact]
    public async Task Hello_CallsOpened_AndTheOpenCallIsAPostWithTheAppToken()
    {
        var (provider, http, sockets, _) = Build(Frame.Text(Hello), Frame.Close());
        var opened = 0;

        var end = await provider.ListenAsync(AppToken, _ => { opened++; return Task.CompletedTask; }, _ => { }, default);

        Assert.Equal(1, opened);
        Assert.Equal(ChatListenEndKind.Reopen, end.Kind);
        var request = Assert.Single(http.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("https://slack.com/api/apps.connections.open", request.Url);
        Assert.Equal($"Bearer {AppToken}", Assert.Single(http.AuthorizationHeaders));
        Assert.Equal(new Uri(Address), Assert.Single(sockets.Addresses));
    }

    [Fact]
    public async Task APressIsReadIntoEveryPart()
    {
        var presses = await Presses(Press());

        Assert.Equal(new ChatPress("U1", "C1", "1700000000.000100", "answer", "yes", ReplyUrl), Assert.Single(presses));
    }

    [Fact]
    public async Task TheMessageIdFallsBackToTheContainer_AndAMissingValueIsNull()
    {
        var press = """
            {"envelope_id":"e","type":"interactive","payload":{"type":"block_actions","user":{"id":"U1"},"channel":{"id":"C1"},
            "container":{"message_ts":"9.9"},"response_url":"https://hooks.example.test/x","actions":[{"action_id":"a"}]}}
            """;

        var got = Assert.Single(await Presses(press));

        Assert.Equal("9.9", got.MessageId);
        Assert.Null(got.Value);
    }

    [Fact]
    public void APressPrintsWithoutItsReplyAddress()
    {
        var press = new ChatPress("U1", "C1", "1.2", "answer", "yes", ReplyUrl);

        var text = press.ToString();

        Assert.DoesNotContain("secret-reply", text);
        Assert.DoesNotContain("hooks.example.test", text);
        Assert.Contains("U1", text);
        Assert.Contains("answer", text);
    }

    [Fact]
    public async Task EveryEnvelopeIsAcknowledged_BeforeThePressIsHandedOver()
    {
        var order = new List<string>();
        var (provider, _, sockets, _) = Build(Frame.Text(Hello), Frame.Text(Press()), Frame.Close());
        sockets.Connection.Order = order;

        await provider.ListenAsync(AppToken, _ => Task.CompletedTask, _ => order.Add("pressed"), default);

        Assert.Equal(["sent:{\"envelope_id\":\"env-1\"}", "pressed"], order);
    }

    // ---- a callback that throws ----

    [Fact]
    public async Task AThrowingPressedAndAThrowingOpened_DoNotEndTheRead()
    {
        var (provider, _, sockets, logs) = Build(
            Frame.Text(Hello), Frame.Text(Press()), Frame.Text(Press()), Frame.Text("""{"type":"disconnect","reason":"refresh_requested"}"""));
        var pressed = 0;

        var end = await provider.ListenAsync(
            AppToken, _ => throw new InvalidOperationException(ReplyUrl), _ => { pressed++; throw new InvalidOperationException(ReplyUrl); }, default);

        Assert.Equal(2, pressed);
        Assert.Equal(ChatListenEndKind.Reopen, end.Kind);
        Assert.Equal(2, sockets.Connection.Sent.Count);
        AssertNothingLeaked(logs, end);
    }

    // ---- what is acknowledged and dropped ----

    [Theory]
    [InlineData("""{"envelope_id":"e1","type":"events_api","payload":{"event":{"type":"message"}}}""")]
    [InlineData("""{"envelope_id":"e1","type":"something_new"}""")]
    [InlineData("""{"envelope_id":"e1","type":"interactive","payload":{"type":"view_submission"}}""")]
    [InlineData("""{"envelope_id":"e1","type":"interactive","payload":{"type":"block_actions","actions":[]}}""")]
    public async Task AnUnknownEnvelope_OrANonPress_IsAcknowledgedAndDropped(string frame)
    {
        var (provider, _, sockets, _) = Build(Frame.Text(Hello), Frame.Text(frame), Frame.Close());
        var pressed = 0;

        var end = await provider.ListenAsync(AppToken, _ => Task.CompletedTask, _ => pressed++, default);

        Assert.Equal(0, pressed);
        Assert.Equal(["{\"envelope_id\":\"e1\"}"], sockets.Connection.Sent);
        Assert.Equal(ChatListenEndKind.Reopen, end.Kind);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("channel")]
    [InlineData("message")]
    [InlineData("response_url")]
    [InlineData("action_id")]
    public async Task APressMissingAPart_IsAcknowledgedAndDropped(string missing)
    {
        var frame = missing switch
        {
            "user" => Press().Replace("\"user\":{\"id\":\"U1\"},", ""),
            "channel" => Press().Replace("\"channel\":{\"id\":\"C1\"},", ""),
            "message" => Press(messageTs: null),
            "response_url" => Press(replyUrl: null),
            _ => Press().Replace("\"action_id\":\"answer\",", ""),
        };
        Assert.NotEqual(Press(), frame);

        var (provider, _, sockets, _) = Build(Frame.Text(Hello), Frame.Text(frame), Frame.Close());
        var pressed = 0;

        await provider.ListenAsync(AppToken, _ => Task.CompletedTask, _ => pressed++, default);

        Assert.Equal(0, pressed);
        Assert.Single(sockets.Connection.Sent);
    }

    [Fact]
    public async Task NonJsonText_IsIgnored_AndTheReadGoesOn()
    {
        var presses = await Presses("not json at all " + ReplyUrl, "[1,2]", "42");

        Assert.Empty(presses);
    }

    // ---- how it ends ----

    [Theory]
    [InlineData("warning")]
    [InlineData("refresh_requested")]
    [InlineData("something_else")]
    public async Task ADisconnect_ClosesOurSide_AndEndsReopen(string reason)
    {
        var (provider, _, sockets, _) = Build(Frame.Text(Hello), Frame.Text($"{{\"type\":\"disconnect\",\"reason\":\"{reason}\"}}"));

        var end = await provider.ListenAsync(AppToken, _ => Task.CompletedTask, _ => { }, default);

        Assert.Equal(ChatListenEndKind.Reopen, end.Kind);
        Assert.Equal(1, sockets.Connection.Closes);
    }

    [Fact]
    public async Task LinkDisabled_EndsLinkDisabled_WithASentenceSayingSocketModeIsOff()
    {
        var (provider, _, _, _) = Build(Frame.Text(Hello), Frame.Text("""{"type":"disconnect","reason":"link_disabled"}"""));

        var end = await provider.ListenAsync(AppToken, _ => Task.CompletedTask, _ => { }, default);

        Assert.Equal(ChatListenEndKind.LinkDisabled, end.Kind);
        Assert.Contains("Socket Mode is turned off", end.Sentence);
    }

    [Fact]
    public async Task ADroppedSocket_AfterHello_EndsReopen()
    {
        var (provider, _, _, _) = Build(Frame.Text(Hello), Frame.Close());

        var end = await provider.ListenAsync(AppToken, _ => Task.CompletedTask, _ => { }, default);

        Assert.Equal(ChatListenEndKind.Reopen, end.Kind);
    }

    [Fact]
    public async Task AReceiveThatThrows_EndsReopenAfterHello_AndRefusedBefore()
    {
        var failure = new WebSocketException("failed for " + Address);

        var (after, _, _, afterLogs) = Build(Frame.Text(Hello), Frame.Throw(failure));
        var reopened = await after.ListenAsync(AppToken, _ => Task.CompletedTask, _ => { }, default);

        var (before, _, _, beforeLogs) = Build(Frame.Throw(failure));
        var refused = await before.ListenAsync(AppToken, _ => Task.CompletedTask, _ => { }, default);

        Assert.Equal(ChatListenEndKind.Reopen, reopened.Kind);
        Assert.Equal(ChatListenEndKind.Refused, refused.Kind);
        AssertNothingLeaked(afterLogs, reopened);
        AssertNothingLeaked(beforeLogs, refused);
    }

    [Fact]
    public async Task ASendThatThrows_EndsReopen()
    {
        var (provider, _, sockets, _) = Build(Frame.Text(Hello), Frame.Text(Press()));
        sockets.Connection.SendError = new WebSocketException("failed for " + Address);

        var end = await provider.ListenAsync(AppToken, _ => Task.CompletedTask, _ => { }, default);

        Assert.Equal(ChatListenEndKind.Reopen, end.Kind);
    }

    [Fact]
    public async Task AMessageOverTheLimit_EndsReopenAfterHello()
    {
        var (provider, _, _, _) = Build(Frame.Text(Hello), Frame.Throw(new SocketMessageTooLargeException()));

        var end = await provider.ListenAsync(AppToken, _ => Task.CompletedTask, _ => { }, default);

        Assert.Equal(ChatListenEndKind.Reopen, end.Kind);
    }

    [Fact]
    public async Task ACloseBeforeHello_IsRefused()
    {
        var (provider, _, _, _) = Build(Frame.Close());

        var end = await provider.ListenAsync(AppToken, _ => Task.CompletedTask, _ => { }, default);

        Assert.Equal(ChatListenEndKind.Refused, end.Kind);
        Assert.False(string.IsNullOrWhiteSpace(end.Sentence));
    }

    [Fact]
    public async Task Cancellation_ClosesTheSocket_AndEndsStopped()
    {
        using var cts = new CancellationTokenSource();
        var (provider, _, sockets, _) = Build(Frame.Text(Hello));

        var end = await provider.ListenAsync(AppToken, _ => { cts.Cancel(); return Task.CompletedTask; }, _ => { }, cts.Token);

        Assert.Equal(ChatListenEndKind.Stopped, end.Kind);
        Assert.Equal(1, sockets.Connection.Closes);
        Assert.True(sockets.Connection.Disposed);
    }

    [Fact]
    public async Task Cancellation_BeforeTheOpenCall_EndsStopped()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var (provider, _, sockets, _) = Build();

        var end = await provider.ListenAsync(AppToken, _ => Task.CompletedTask, _ => { }, cts.Token);

        Assert.Equal(ChatListenEndKind.Stopped, end.Kind);
        Assert.Empty(sockets.Addresses);
    }

    // ---- refusals to open ----

    [Fact]
    public async Task ARefusedOpen_IsRefused_WithTheAppTokenSentence_AndNoSocket()
    {
        var (provider, _, sockets, logs) = Build(
            openReply: StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"ok":false,"error":"invalid_auth"}"""));

        var end = await provider.ListenAsync(AppToken, _ => Task.CompletedTask, _ => { }, default);

        Assert.Equal(ChatListenEndKind.Refused, end.Kind);
        Assert.Equal("Slack did not accept that app-level token; check it was copied whole, or make a new one", end.Sentence);
        Assert.Null(end.RetryAfter);
        Assert.Empty(sockets.Addresses);
        AssertNothingLeaked(logs, end);
    }

    [Fact]
    public async Task AThrottle_IsRefused_AndCarriesHowLongSlackAsked()
    {
        var throttled = StubHttpMessageHandler.Json((HttpStatusCode)429, "{}");
        throttled.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
        var (provider, _, sockets, _) = Build(openReply: throttled);

        var end = await provider.ListenAsync(AppToken, _ => Task.CompletedTask, _ => { }, default);

        Assert.Equal(ChatListenEndKind.Refused, end.Kind);
        Assert.Equal(TimeSpan.FromSeconds(7), end.RetryAfter);
        Assert.Contains("7 seconds", end.Sentence);
        Assert.Empty(sockets.Addresses);
    }

    [Fact]
    public async Task AThrottleInTheBody_CarriesHowLongSlackAsked()
    {
        var throttled = StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"ok":false,"error":"ratelimited"}""");
        throttled.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(3));
        var (provider, _, _, _) = Build(openReply: throttled);

        var end = await provider.ListenAsync(AppToken, _ => Task.CompletedTask, _ => { }, default);

        Assert.Equal(TimeSpan.FromSeconds(3), end.RetryAfter);
    }

    [Fact]
    public async Task AWrongPrefix_IsRefused_AndNothingIsSent()
    {
        var (provider, http, sockets, _) = Build();

        var end = await provider.ListenAsync("xoxb-1-not-an-app-token", _ => Task.CompletedTask, _ => { }, default);

        Assert.Equal(ChatListenEndKind.Refused, end.Kind);
        Assert.Empty(http.Requests);
        Assert.Empty(sockets.Addresses);
        Assert.DoesNotContain("not-an-app-token", end.Sentence);
    }

    [Theory]
    [InlineData("""{"ok":true}""")]
    [InlineData("""{"ok":true,"url":"ws://wss.example.test/link"}""")]
    [InlineData("""{"ok":true,"url":"https://wss.example.test/link"}""")]
    [InlineData("""{"ok":true,"url":"/relative/link"}""")]
    [InlineData("""{"ok":true,"url":"not an address"}""")]
    public async Task NoUsableAddress_IsRefused_AndNoSocketIsOpened(string reply)
    {
        var (provider, _, sockets, logs) = Build(openReply: StubHttpMessageHandler.Json(HttpStatusCode.OK, reply));

        var end = await provider.ListenAsync(AppToken, _ => Task.CompletedTask, _ => { }, default);

        Assert.Equal(ChatListenEndKind.Refused, end.Kind);
        Assert.Empty(sockets.Addresses);
        Assert.DoesNotContain("wss.example.test", end.Sentence + string.Join("", logs.Messages));
    }

    [Fact]
    public async Task AFailedConnect_IsRefused_WithNoAddressAnywhere()
    {
        var (provider, _, sockets, logs) = Build();
        sockets.ConnectError = new WebSocketException("could not reach " + Address);

        var end = await provider.ListenAsync(AppToken, _ => Task.CompletedTask, _ => { }, default);

        Assert.Equal(ChatListenEndKind.Refused, end.Kind);
        AssertNothingLeaked(logs, end);
    }

    // ---- keep-alive and logging ----

    [Fact]
    public void TheRealSocket_PingsAndGivesUpOnASilentPeer_EveryTwentySeconds()
    {
        using var socket = new ClientWebSocket();

        ClientWebSocketConnector.Configure(socket.Options);

        Assert.Equal(TimeSpan.FromSeconds(20), socket.Options.KeepAliveInterval);
        Assert.Equal(TimeSpan.FromSeconds(20), socket.Options.KeepAliveTimeout);
    }

    [Fact]
    public async Task AWholeSession_LeavesNoTokenAndNoAddressInALogLineOrASentence()
    {
        var (provider, _, _, logs) = Build(
            Frame.Text(Hello),
            Frame.Text("garbage " + Address),
            Frame.Text(Press()),
            Frame.Text(Press(messageTs: null)),
            Frame.Throw(new WebSocketException("dropped " + Address + " " + ReplyUrl)));
        var seen = new List<ChatPress>();

        var end = await provider.ListenAsync(
            AppToken, _ => throw new InvalidOperationException(Address), p => { seen.Add(p); throw new InvalidOperationException(ReplyUrl); }, default);

        Assert.Single(seen);
        Assert.Equal(ChatListenEndKind.Reopen, end.Kind);
        AssertNothingLeaked(logs, end);
        Assert.NotEmpty(logs.Messages);
    }

    // ---- helpers ----

    private static async Task<IReadOnlyList<ChatPress>> Presses(params string[] frames)
    {
        var script = new List<Frame> { Frame.Text(Hello) };
        script.AddRange(frames.Select(Frame.Text));
        script.Add(Frame.Close());
        var (provider, _, _, _) = Build(script.ToArray());
        var seen = new List<ChatPress>();

        await provider.ListenAsync(AppToken, _ => Task.CompletedTask, seen.Add, default);

        return seen;
    }

    private static (SlackChatProvider Provider, StubHttpMessageHandler Http, FakeSocketConnector Sockets, CapturedLogs Logs) Build(
        params Frame[] frames) => Build(frames, null);

    private static (SlackChatProvider Provider, StubHttpMessageHandler Http, FakeSocketConnector Sockets, CapturedLogs Logs) Build(
        HttpResponseMessage openReply) => Build([], openReply);

    private static (SlackChatProvider Provider, StubHttpMessageHandler Http, FakeSocketConnector Sockets, CapturedLogs Logs) Build(
        Frame[] frames, HttpResponseMessage? openReply)
    {
        var http = new StubHttpMessageHandler(openReply ?? StubHttpMessageHandler.Json(HttpStatusCode.OK, OpenReply));
        var sockets = new FakeSocketConnector(new FakeConnection(frames));
        var logs = new CapturedLogs();
        var provider = new SlackChatProvider(
            new StubHttpClientFactory(http), new LoggerFactory([logs]).CreateLogger<SlackChatProvider>(), sockets);

        return (provider, http, sockets, logs);
    }

    private static void AssertNothingLeaked(CapturedLogs logs, ChatListenEnd end)
    {
        var everything = string.Join("\n", logs.Messages) + "\n" + end.Sentence + "\n" + JsonSerializer.Serialize(end);

        Assert.DoesNotContain("secret-app-token", everything);
        Assert.DoesNotContain("secret-ticket", everything);
        Assert.DoesNotContain("wss-primary", everything);
        Assert.DoesNotContain("secret-reply", everything);
        Assert.DoesNotContain("hooks.example.test", everything);
    }

    /// <summary>One thing the fake socket does when read: say a text, close, or throw.</summary>
    private sealed record Frame(string? Message, Exception? Error, bool Closes)
    {
        public static Frame Text(string text) => new(text, null, false);

        public static Frame Close() => new(null, null, true);

        public static Frame Throw(Exception error) => new(null, error, false);
    }

    private sealed class FakeSocketConnector(FakeConnection connection) : ISocketConnector
    {
        public FakeConnection Connection { get; } = connection;

        public List<Uri> Addresses { get; } = [];

        public Exception? ConnectError { get; set; }

        public Task<ISocketConnection> ConnectAsync(Uri address, CancellationToken ct)
        {
            Addresses.Add(address);
            if (ConnectError is not null) return Task.FromException<ISocketConnection>(ConnectError);

            return Task.FromResult<ISocketConnection>(Connection);
        }
    }

    /// <summary>Plays its frames in order, then waits to be cancelled, as a quiet socket does.</summary>
    private sealed class FakeConnection(Frame[] frames) : ISocketConnection
    {
        private readonly Queue<Frame> queue = new(frames);

        public List<string> Sent { get; } = [];

        /// <summary>When set, each send is also written here as <c>sent:</c> and its text, so a test can read the order against another event.</summary>
        public List<string>? Order { get; set; }

        public Exception? SendError { get; set; }

        public int Closes { get; private set; }

        public bool Disposed { get; private set; }

        public async Task<string?> ReceiveTextAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (!queue.TryDequeue(out var frame))
            {
                await Task.Delay(Timeout.Infinite, ct);
                return null;
            }

            if (frame.Error is not null) throw frame.Error;

            return frame.Closes ? null : frame.Message;
        }

        public Task SendTextAsync(string text, CancellationToken ct)
        {
            if (SendError is not null) return Task.FromException(SendError);

            Sent.Add(text);
            Order?.Add("sent:" + text);

            return Task.CompletedTask;
        }

        public Task CloseAsync()
        {
            Closes++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}

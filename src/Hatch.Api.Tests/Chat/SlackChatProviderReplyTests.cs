using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Hatch.Api.Services.Chat;
using Microsoft.Extensions.Logging;

namespace Hatch.Api.Tests.Chat;

/// <summary>
/// Answering a button press at the address Slack gave for it. Nothing here calls
/// Slack; <see cref="StubHttpMessageHandler"/> answers. The address is the only
/// credential, so each case checks it is nowhere in a log line or a sentence.
/// </summary>
public class SlackChatProviderReplyTests
{
    private const string Address = "https://hooks.example/actions/T1/secret-part/other-secret";

    private static ChatMessageContent Message(string title = "This button no longer works") =>
        new(title, "Answer it from Hatch.", [], [new ChatLink("Ballot", "https://hatch.example/ballot")]);

    [Fact]
    public async Task AReply_IsOnePost_ToTheAddressGiven_WithNoToken_AsAnEphemeralMessage()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(HttpStatusCode.OK, "ok"));

        var result = await NewProvider(handler, out _).ReplyAsync(Address, Message(), default);

        Assert.Equal(ChatSendOutcome.Sent, result.Outcome);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal(Address, request.Url);
        Assert.Empty(handler.AuthorizationHeaders);

        using var body = JsonDocument.Parse(request.Body);
        var root = body.RootElement;
        Assert.Equal(SlackChatProvider.Format(Message()), root.GetProperty("text").GetString());
        Assert.Equal("ephemeral", root.GetProperty("response_type").GetString());
        Assert.False(root.GetProperty("replace_original").GetBoolean());
    }

    [Fact]
    public async Task ATitleFromATicket_IsEscapedInTheText()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(HttpStatusCode.OK, "ok"));

        await NewProvider(handler, out _).ReplyAsync(Address, Message("<!channel> & <@U1>"), default);

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        var text = body.RootElement.GetProperty("text").GetString()!;
        Assert.StartsWith("*&lt;!channel&gt; &amp; &lt;@U1&gt;*", text);
        Assert.DoesNotContain("<!channel>", text);
        Assert.DoesNotContain("<@U1>", text);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "used_url", "Slack refused: used_url")]
    [InlineData(HttpStatusCode.NotFound, "expired_url", "Slack refused: expired_url")]
    [InlineData(HttpStatusCode.BadRequest, "invalid_payload", "Slack refused: invalid_payload")]
    [InlineData(HttpStatusCode.BadRequest, "<html>The address is https://hooks.example/actions/T1/secret-part</html>", "Slack refused: http_400")]
    public async Task ARefusal_IsASentence_WithTheCodeAlone(HttpStatusCode status, string body, string sentence)
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(status, body));

        var result = await NewProvider(handler, out var log).ReplyAsync(Address, Message(), default);

        Assert.Equal(ChatSendOutcome.RoomRefused, result.Outcome);
        Assert.Equal(sentence, result.Sentence);
        NothingHoldsTheAddress(result, log);
    }

    [Fact]
    public async Task ATooManyRequestsAnswer_IsThrottled_ButSaysTheReplyWasDropped()
    {
        var throttled = StubHttpMessageHandler.Json((HttpStatusCode)429, "ratelimited");
        throttled.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));

        var result = await NewProvider(new StubHttpMessageHandler(throttled), out var log).ReplyAsync(Address, Message(), default);

        Assert.Equal(ChatSendOutcome.Throttled, result.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(7), result.RetryAfter);
        Assert.Contains("dropped", result.Sentence);
        Assert.DoesNotContain("try again", result.Sentence);
        NothingHoldsTheAddress(result, log);
    }

    [Fact]
    public async Task AServerFailure_IsUnreachable()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(HttpStatusCode.BadGateway, "bad gateway"));

        var result = await NewProvider(handler, out var log).ReplyAsync(Address, Message(), default);

        Assert.Equal(ChatSendOutcome.Unreachable, result.Outcome);
        NothingHoldsTheAddress(result, log);
    }

    [Theory]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(TaskCanceledException))]
    public async Task ADeadNetwork_IsUnreachable_NeverThrows_AndLogsTheTypeAlone(Type error)
    {
        var handler = new ThrowingHandler((Exception)Activator.CreateInstance(error, $"failed calling {Address}")!);

        var result = await NewProvider(handler, out var log).ReplyAsync(Address, Message(), default);

        Assert.Equal(ChatSendOutcome.Unreachable, result.Outcome);
        Assert.Contains(error.Name, string.Join('\n', log.Lines));
        NothingHoldsTheAddress(result, log);
    }

    [Theory]
    [InlineData("/actions/T1/secret-part")]
    [InlineData("http://hooks.example/actions/T1/secret-part")]
    [InlineData("")]
    [InlineData("not an address")]
    public async Task AnAddressThatIsNotAbsoluteHttps_SendsNothing(string address)
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(HttpStatusCode.OK, "ok"));

        var result = await NewProvider(handler, out var log).ReplyAsync(address, Message(), default);

        Assert.Equal(ChatSendOutcome.RoomRefused, result.Outcome);
        Assert.Empty(handler.Requests);
        Assert.Empty(log.Lines);
        Assert.DoesNotContain("secret-part", result.Sentence);
    }

    private static void NothingHoldsTheAddress(ChatSendResult result, CapturingLogger log)
    {
        Assert.DoesNotContain("secret-part", result.Sentence);
        Assert.DoesNotContain("hooks.example", result.Sentence);
        foreach (var line in log.Lines)
        {
            Assert.DoesNotContain("secret-part", line);
            Assert.DoesNotContain("hooks.example", line);
        }
    }

    private static SlackChatProvider NewProvider(HttpMessageHandler handler, out CapturingLogger log)
    {
        log = new CapturingLogger();

        return new SlackChatProvider(new StubHttpClientFactory(handler), log);
    }

    private sealed class ThrowingHandler(Exception error) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromException<HttpResponseMessage>(error);
    }

    private sealed class CapturingLogger : ILogger<SlackChatProvider>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add($"{formatter(state, exception)} {exception}");
    }
}

using System.Net;
using Hatch.Api.Services.Chat;
using Hatch.Api.Tests.Hatch;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hatch.Api.Tests.Chat;

/// <summary>
/// What Slack's <c>auth.test</c> says about a token, and what a person is told
/// of it. Slack answers 200 with <c>ok:false</c> and an error code, so the code
/// is the thing under test; the token is never in a URL, a body or a sentence.
/// </summary>
public class SlackChatProviderTests
{
    private const string Token = "xoxb-1234-secret-value";

    [Fact]
    public async Task ATokenIsSentToAuthTest_AsABearerHeader_AndInNoUrlOrBody()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"ok":true,"team_id":"T1","team":"Acme"}"""));

        await NewProvider(handler).CheckTokenAsync(Token, default);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("https://slack.com/api/auth.test", request.Url);
        Assert.Equal($"Bearer {Token}", Assert.Single(handler.AuthorizationHeaders));
        Assert.DoesNotContain("secret-value", request.Url);
        Assert.DoesNotContain("secret-value", request.Body);
    }

    [Fact]
    public async Task AnAcceptedToken_GivesTheWorkspaceIdAndName()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"ok":true,"team_id":"T1","team":"Acme"}"""));

        var check = await NewProvider(handler).CheckTokenAsync(Token, default);

        Assert.True(check.Accepted);
        Assert.Equal("T1", check.WorkspaceId);
        Assert.Equal("Acme", check.WorkspaceName);
        Assert.Null(check.Refusal);
    }

    [Theory]
    [InlineData("invalid_auth", "did not accept")]
    [InlineData("not_authed", "did not receive")]
    [InlineData("token_revoked", "revoked")]
    [InlineData("token_expired", "expired")]
    [InlineData("account_inactive", "no longer active")]
    public async Task EachKnownError_GivesItsOwnPlainSentence(string code, string fragment)
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(HttpStatusCode.OK, $$"""{"ok":false,"error":"{{code}}"}"""));

        var check = await NewProvider(handler).CheckTokenAsync(Token, default);

        Assert.False(check.Accepted);
        Assert.Contains(fragment, check.Refusal);
        Assert.DoesNotContain(code, check.Refusal);
        AssertNoToken(check);
    }

    [Fact]
    public async Task AnUnknownError_NamesTheCode()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"ok":false,"error":"ratelimited_or_something"}"""));

        var check = await NewProvider(handler).CheckTokenAsync(Token, default);

        Assert.Equal("Slack refused the token: ratelimited_or_something", check.Refusal);
    }

    [Fact]
    public async Task ATooManyRequests_IsCouldNotBeReached_NotAnException()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json((HttpStatusCode)429, """{"ok":false,"error":"ratelimited"}"""));

        var check = await NewProvider(handler).CheckTokenAsync(Token, default);

        Assert.Equal("Slack could not be reached; try again", check.Refusal);
        AssertNoToken(check);
    }

    [Fact]
    public async Task ATransportFailure_IsCouldNotBeReached_NotAnException()
    {
        var provider = NewProvider(new ThrowingHandler(new HttpRequestException($"connection refused for {Token}")));

        var check = await provider.CheckTokenAsync(Token, default);

        Assert.Equal("Slack could not be reached; try again", check.Refusal);
        AssertNoToken(check);
    }

    [Fact]
    public async Task ATimeout_IsCouldNotBeReached_NotAnException()
    {
        var provider = NewProvider(new ThrowingHandler(new TaskCanceledException("timed out")));

        var check = await provider.CheckTokenAsync(Token, default);

        Assert.Equal("Slack could not be reached; try again", check.Refusal);
    }

    [Fact]
    public async Task ABodyThatIsNotJson_IsCouldNotBeReached()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(HttpStatusCode.BadGateway, "<html>bad gateway</html>"));

        var check = await NewProvider(handler).CheckTokenAsync(Token, default);

        Assert.Equal("Slack could not be reached; try again", check.Refusal);
    }

    [Theory]
    [InlineData("xoxp-1234-a-user-token")]
    [InlineData("not-a-token")]
    [InlineData("XOXB-upper")]
    public async Task ATokenThatIsNotABotToken_IsRefusedBeforeAnyCall(string token)
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"ok":true,"team_id":"T1","team":"Acme"}"""));

        var check = await NewProvider(handler).CheckTokenAsync(token, default);

        Assert.Empty(handler.Requests);
        Assert.False(check.Accepted);
        Assert.Contains("bot token", check.Refusal);
        Assert.Contains("xoxb-", check.Refusal);
        Assert.DoesNotContain(token, check.Refusal);
    }

    [Fact]
    public async Task ListChannelsAsync_SendsTheTokenAsABearerHeader_AndExcludesArchivedChannels()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"ok":true,"channels":[]}"""));

        await NewProvider(handler).ListChannelsAsync(Token, default);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("GET", request.Method);
        Assert.StartsWith("https://slack.com/api/conversations.list?", request.Url);
        Assert.Contains("types=public_channel,private_channel", request.Url);
        Assert.Contains("exclude_archived=true", request.Url);
        Assert.Equal($"Bearer {Token}", Assert.Single(handler.AuthorizationHeaders));
        Assert.DoesNotContain("secret-value", request.Url);
    }

    [Fact]
    public async Task ListChannelsAsync_ReadsEveryChannelsField()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(HttpStatusCode.OK, """
            {"ok":true,"channels":[
                {"id":"C1","name":"general","is_private":false,"is_member":true},
                {"id":"C2","name":"secret","is_private":true,"is_member":false}
            ]}
            """));

        var list = await NewProvider(handler).ListChannelsAsync(Token, default);

        Assert.Null(list.Refusal);
        Assert.Equal(
            [new ChatChannel("C1", "general", false, true), new ChatChannel("C2", "secret", true, false)],
            list.Channels);
    }

    [Fact]
    public async Task ListChannelsAsync_FollowsTheCursor_UntilItIsEmpty()
    {
        var handler = new StubHttpMessageHandler(
            StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"ok":true,"channels":[{"id":"C1","name":"one"}],"response_metadata":{"next_cursor":"page2"}}"""),
            StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"ok":true,"channels":[{"id":"C2","name":"two"}],"response_metadata":{"next_cursor":""}}"""));

        var list = await NewProvider(handler).ListChannelsAsync(Token, default);

        Assert.Equal(["C1", "C2"], list.Channels!.Select(c => c.Id));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("cursor=page2", handler.Requests[1].Url);
    }

    [Fact]
    public async Task ListChannelsAsync_StopsIfACursorRepeats()
    {
        var handler = new StubHttpMessageHandler(
            StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"ok":true,"channels":[{"id":"C1","name":"one"}],"response_metadata":{"next_cursor":"again"}}"""),
            StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"ok":true,"channels":[{"id":"C2","name":"two"}],"response_metadata":{"next_cursor":"again"}}"""));

        var list = await NewProvider(handler).ListChannelsAsync(Token, default);

        Assert.Equal(["C1", "C2"], list.Channels!.Select(c => c.Id));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData("not_in_channel", "invited")]
    [InlineData("channel_not_found", "could not be found")]
    [InlineData("is_archived", "archived")]
    [InlineData("missing_scope", "permission")]
    [InlineData("ratelimited", "could not be reached")]
    public async Task ListChannelsAsync_EachKnownError_GivesItsOwnPlainSentence(string code, string fragment)
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(HttpStatusCode.OK, $$"""{"ok":false,"error":"{{code}}"}"""));

        var list = await NewProvider(handler).ListChannelsAsync(Token, default);

        Assert.Null(list.Channels);
        Assert.Contains(fragment, list.Refusal);
        Assert.DoesNotContain(code, list.Refusal);
    }

    [Fact]
    public async Task ListChannelsAsync_AnUnknownError_NamesTheCodeWithoutTheWordToken()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"ok":false,"error":"something_else"}"""));

        var list = await NewProvider(handler).ListChannelsAsync(Token, default);

        Assert.Equal("Slack refused: something_else", list.Refusal);
    }

    [Fact]
    public async Task ListChannelsAsync_TooManyRequests_IsCouldNotBeReached_NotAnException()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json((HttpStatusCode)429, """{"ok":false,"error":"ratelimited"}"""));

        var list = await NewProvider(handler).ListChannelsAsync(Token, default);

        Assert.Equal("Slack could not be reached; try again", list.Refusal);
    }

    [Fact]
    public async Task PostMessageAsync_SendsAFormPost_WithTheChannelAndTextAlone_AndTheTokenOnlyAsABearerHeader()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"ok":true}"""));

        var result = await NewProvider(handler).PostMessageAsync(Token, "C1", "hello room", default);

        Assert.True(result.Posted);
        Assert.Null(result.Refusal);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("https://slack.com/api/chat.postMessage", request.Url);
        Assert.Equal($"Bearer {Token}", Assert.Single(handler.AuthorizationHeaders));
        Assert.Equal(new Dictionary<string, string> { ["channel"] = "C1", ["text"] = "hello room" }, handler.LastForm);
        Assert.DoesNotContain("secret-value", request.Url);
        Assert.DoesNotContain("secret-value", request.Body);
    }

    [Fact]
    public async Task PostMessageAsync_ARefusal_AnswersFalse_NotAnException()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"ok":false,"error":"not_in_channel"}"""));

        var result = await NewProvider(handler).PostMessageAsync(Token, "C1", "hello room", default);

        Assert.False(result.Posted);
        Assert.Contains("invited", result.Refusal);
        Assert.DoesNotContain("not_in_channel", result.Refusal);
    }

    private const string AppToken = "xapp-1-A1111-1111-secretAppValue";
    private const string SocketAddress = "wss://socket.example/link/?ticket=one-use-ticket";
    private const string OpenedBody = """{"ok":true,"url":"wss://socket.example/link/?ticket=one-use-ticket"}""";

    [Fact]
    public async Task AnAppToken_IsSentToConnectionsOpen_AsABearerHeader_WithNoBodyAndNothingInTheUrl()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(HttpStatusCode.OK, OpenedBody));

        await NewProvider(handler).CheckAppTokenAsync(AppToken, default);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("https://slack.com/api/apps.connections.open", request.Url);
        Assert.Equal($"Bearer {AppToken}", Assert.Single(handler.AuthorizationHeaders));
        Assert.DoesNotContain("secretAppValue", request.Url);
        Assert.Equal("", request.Body);
    }

    [Fact]
    public async Task AnAcceptedAppToken_IsAccepted_AndTheAddressSlackAnswersWithIsInNothing()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(HttpStatusCode.OK, OpenedBody));
        var logs = new CapturedLogs();

        var check = await NewProvider(handler, logs).CheckAppTokenAsync(AppToken, default);

        Assert.True(check.IsAccepted);
        Assert.Null(check.Refusal);
        Assert.DoesNotContain("one-use-ticket", check.ToString());
        AssertNothingSecret(check, logs);
    }

    [Theory]
    [InlineData("invalid_auth", "did not accept")]
    [InlineData("not_authed", "did not receive")]
    [InlineData("token_revoked", "revoked")]
    [InlineData("token_expired", "expired")]
    [InlineData("missing_scope", "connections:write")]
    public async Task EachKnownAppTokenError_GivesASentenceAboutAnAppLevelToken(string code, string fragment)
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(HttpStatusCode.OK, $$"""{"ok":false,"error":"{{code}}"}"""));
        var logs = new CapturedLogs();

        var check = await NewProvider(handler, logs).CheckAppTokenAsync(AppToken, default);

        Assert.False(check.IsAccepted);
        Assert.Contains(fragment, check.Refusal);
        Assert.Contains("app-level token", check.Refusal);
        Assert.DoesNotContain("install the app again", check.Refusal);
        Assert.DoesNotContain(code, check.Refusal);
        AssertNothingSecret(check, logs);
    }

    [Fact]
    public async Task AnUnknownAppTokenError_NamesTheCode_AndNothingElseFromTheBody()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            """{"ok":false,"error":"something_new","url":"wss://socket.example/link/?ticket=one-use-ticket"}"""));
        var logs = new CapturedLogs();

        var check = await NewProvider(handler, logs).CheckAppTokenAsync(AppToken, default);

        Assert.False(check.IsAccepted);
        Assert.Equal("Slack refused: something_new", check.Refusal);
        AssertNothingSecret(check, logs);
    }

    [Theory]
    [InlineData(429, """{"ok":false,"error":"ratelimited"}""")]
    [InlineData(200, """{"ok":false,"error":"ratelimited"}""")]
    public async Task AThrottledAppTokenCheck_SaysSlackAskedForAPause(int status, string body)
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json((HttpStatusCode)status, body));
        var logs = new CapturedLogs();

        var check = await NewProvider(handler, logs).CheckAppTokenAsync(AppToken, default);

        Assert.False(check.IsAccepted);
        Assert.Contains("Slack asked Hatch to", check.Refusal);
        AssertNothingSecret(check, logs);
    }

    [Fact]
    public async Task AnUnreachableSlack_IsCouldNotBeReached_ForAnAppToken_NotAnException()
    {
        var logs = new CapturedLogs();
        var transport = await NewProvider(new ThrowingHandler(new HttpRequestException($"refused for {AppToken} at {SocketAddress}")), logs)
            .CheckAppTokenAsync(AppToken, default);
        var notJson = await NewProvider(new StubHttpMessageHandler(StubHttpMessageHandler.Json(HttpStatusCode.BadGateway, "<html>bad gateway</html>")), logs)
            .CheckAppTokenAsync(AppToken, default);

        Assert.Equal("Slack could not be reached; try again", transport.Refusal);
        Assert.Equal("Slack could not be reached; try again", notJson.Refusal);
        AssertNothingSecret(transport, logs);
        AssertNothingSecret(notJson, logs);
    }

    [Theory]
    [InlineData("xoxb-1234-a-bot-token")]
    [InlineData("not-a-token")]
    [InlineData("XAPP-upper")]
    public async Task ATokenThatIsNotAnAppToken_IsRefusedBeforeAnyCall(string token)
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(HttpStatusCode.OK, OpenedBody));

        var check = await NewProvider(handler).CheckAppTokenAsync(token, default);

        Assert.Empty(handler.Requests);
        Assert.False(check.IsAccepted);
        Assert.Contains("app-level token", check.Refusal);
        Assert.Contains("xapp-", check.Refusal);
        Assert.DoesNotContain(token, check.Refusal);
    }

    private static void AssertNothingSecret(ChatAppTokenCheck check, CapturedLogs logs)
    {
        var said = new[] { check.Refusal ?? "", check.ToString() }.Concat(logs.Messages).ToList();

        Assert.DoesNotContain(said, l => l.Contains("secretAppValue") || l.Contains("xapp-"));
        Assert.DoesNotContain(said, l => l.Contains("one-use-ticket") || l.Contains("socket.example"));
    }

    private static SlackChatProvider NewProvider(HttpMessageHandler handler, CapturedLogs logs) =>
        new(new StubHttpClientFactory(handler), LoggerFactory.Create(b => b.AddProvider(logs)).CreateLogger<SlackChatProvider>());

    private static SlackChatProvider NewProvider(HttpMessageHandler handler) =>
        new(new StubHttpClientFactory(handler), NullLogger<SlackChatProvider>.Instance);

    private static void AssertNoToken(ChatTokenCheck check)
    {
        Assert.DoesNotContain("secret-value", check.Refusal ?? "");
        Assert.DoesNotContain("xoxb", check.Refusal ?? "");
    }

    private sealed class ThrowingHandler(Exception error) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromException<HttpResponseMessage>(error);
    }
}

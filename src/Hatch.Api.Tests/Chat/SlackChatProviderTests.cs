using System.Net;
using Hatch.Api.Services.Chat;
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

using System.Net;
using System.Text;
using Hatch.Api.Services.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Auth;

/// <summary>
/// Covers the shape of what goes out to Google - the token form - and how the
/// answers come back, against a stubbed transport.
/// </summary>
public class GoogleOAuthServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExchangeCodeAsync_PostsTheVerifierAndClientCredentials()
    {
        var (service, handler) = NewService(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            """{"access_token":"at","refresh_token":"rt","expires_in":3599,"id_token":"it"}"""));

        var result = await service.ExchangeCodeAsync("the-code", "the-verifier", "https://home.example/cb", "client-id.apps.googleusercontent.com", "GOCSPX-secret", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(GoogleOAuthService.TokenEndpoint, handler.Requests[0].Url);
        Assert.Equal("authorization_code", handler.LastForm["grant_type"]);
        Assert.Equal("the-code", handler.LastForm["code"]);
        Assert.Equal("the-verifier", handler.LastForm["code_verifier"]);
        Assert.Equal("https://home.example/cb", handler.LastForm["redirect_uri"]);
        Assert.Equal("client-id.apps.googleusercontent.com", handler.LastForm["client_id"]);
        Assert.Equal("GOCSPX-secret", handler.LastForm["client_secret"]);
    }

    [Fact]
    public async Task ExchangeCodeAsync_ResolvesExpiresInAgainstTheClock()
    {
        var (service, _) = NewService(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            """{"access_token":"at","refresh_token":"rt","expires_in":3599}"""));

        var result = await service.ExchangeCodeAsync("code", "verifier", "https://home.example/cb", "client-id.apps.googleusercontent.com", "GOCSPX-secret", CancellationToken.None);

        Assert.Equal(Now.AddSeconds(3599), result.Tokens!.ExpiresAt);
        Assert.Equal("rt", result.Tokens.RefreshToken);
        Assert.Null(result.Tokens.IdToken);
    }

    [Fact]
    public async Task ExchangeCodeAsync_SurfacesInvalidGrant()
    {
        var (service, _) = NewService(StubHttpMessageHandler.Json(HttpStatusCode.BadRequest,
            """{"error":"invalid_grant","error_description":"Bad Request"}"""));

        var result = await service.ExchangeCodeAsync("code", "verifier", "https://home.example/cb", "client-id.apps.googleusercontent.com", "GOCSPX-secret", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(result.IsInvalidGrant);
    }

    [Fact]
    public async Task ExchangeCodeAsync_FallsBackToTheStatus_WhenTheErrorBodyIsNotJson()
    {
        var (service, _) = NewService(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("<html>upstream is having a day</html>"),
        });

        var result = await service.ExchangeCodeAsync("code", "verifier", "https://home.example/cb", "client-id.apps.googleusercontent.com", "GOCSPX-secret", CancellationToken.None);

        Assert.Equal("http_503", result.Error);
        Assert.False(result.IsInvalidGrant);
    }

    [Fact]
    public async Task ExchangeCodeAsync_ReportsUnreachable_WhenTheTransportFails()
    {
        var service = NewService(new ThrowingHandler());

        var result = await service.ExchangeCodeAsync("code", "verifier", "https://home.example/cb", "client-id.apps.googleusercontent.com", "GOCSPX-secret", CancellationToken.None);

        Assert.Equal("unreachable", result.Error);
    }

    [Fact]
    public void EmailFromIdToken_ReadsTheEmailClaim()
    {
        var idToken = IdToken("""{"iss":"accounts.google.com","email":"family@example.com","email_verified":true}""");

        Assert.Equal("family@example.com", GoogleOAuthService.EmailFromIdToken(idToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-jwt")]
    [InlineData("only.two")]
    [InlineData("a.!!!not-base64!!!.c")]
    public void EmailFromIdToken_ReturnsNull_ForGarbage(string? idToken)
    {
        Assert.Null(GoogleOAuthService.EmailFromIdToken(idToken));
    }

    [Fact]
    public void EmailFromIdToken_ReturnsNull_WhenThePayloadHasNoEmail()
    {
        Assert.Null(GoogleOAuthService.EmailFromIdToken(IdToken("""{"sub":"12345"}""")));
        Assert.Null(GoogleOAuthService.EmailFromIdToken(IdToken("""{"email":""}""")));
        Assert.Null(GoogleOAuthService.EmailFromIdToken(IdToken("""{"email":42}""")));
    }

    /// <summary>An unsigned JWT with the given payload. Enough for EmailFromIdToken, which reads the payload and deliberately doesn't verify the signature.</summary>
    private static string IdToken(string payloadJson) =>
        $"{Base64Url("""{"alg":"none"}""")}.{Base64Url(payloadJson)}.signature";

    private static string Base64Url(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static (GoogleOAuthService Service, StubHttpMessageHandler Handler) NewService(HttpResponseMessage? response = null)
    {
        var handler = new StubHttpMessageHandler(response ?? StubHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        return (NewService(handler), handler);
    }

    private static GoogleOAuthService NewService(HttpMessageHandler handler) =>
        new(new StubHttpClientFactory(handler),
            new FakeTimeProvider(Now),
            NullLogger<GoogleOAuthService>.Instance);

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("no route to host");
    }
}

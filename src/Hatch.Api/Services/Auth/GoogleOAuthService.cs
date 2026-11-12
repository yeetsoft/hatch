using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;

namespace Hatch.Api.Services.Auth;

/// <summary>Tokens as Google's token endpoint returns them, with <c>expires_in</c> already resolved to an instant.</summary>
/// <param name="RefreshToken">Only present when Google chose to issue one - an authorization with <c>access_type=offline</c> and <c>prompt=consent</c>, or the rare rotation on a refresh.</param>
public record GoogleTokens(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt, string? IdToken);

/// <summary>
/// The token endpoint's answer: tokens, or the error code Google named. The
/// code matters because callers branch on it - <c>invalid_grant</c> is a
/// revoked or expired grant that only re-consent fixes, while everything else
/// is worth retrying on the next firing.
/// </summary>
public record GoogleTokenResult(GoogleTokens? Tokens, string? Error)
{
    /// <summary>Google's code for "this grant is dead" - revoked, expired, or (while the consent screen is in Testing) older than seven days.</summary>
    public const string InvalidGrant = "invalid_grant";

    public static GoogleTokenResult Ok(GoogleTokens tokens) => new(tokens, null);

    public static GoogleTokenResult Failed(string error) => new(null, error);

    [MemberNotNullWhen(true, nameof(Tokens))]
    public bool Succeeded => Tokens is not null;

    public bool IsInvalidGrant => Error == InvalidGrant;
}

/// <summary>
/// The claims of a sign-in id token that Hatch reads. Parsed from the payload
/// with no signature check, for the reason
/// <see cref="GoogleOAuthService.EmailFromIdToken"/> gives.
/// </summary>
public record GoogleIdToken(string? Sub, string? Email, bool EmailVerified, string? Name, string? Picture, string? Aud, string? Iss)
{
    public static GoogleIdToken? Parse(string? idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken)) return null;

        var segments = idToken.Split('.');
        if (segments.Length != 3) return null;

        try
        {
            using var payload = JsonDocument.Parse(WebEncoders.Base64UrlDecode(segments[1]));
            var root = payload.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            return new GoogleIdToken(
                Str(root, "sub"), Str(root, "email"), Flag(root, "email_verified"),
                Str(root, "name"), Str(root, "picture"), Str(root, "aud"), Str(root, "iss"));
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or JsonException)
        {
            return null;
        }
    }

    private static string? Str(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    /// <summary>Google sends <c>email_verified</c> as a JSON bool, but has been seen to send the string form; both count, anything else does not.</summary>
    private static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) &&
        (value.ValueKind == JsonValueKind.True ||
         value.ValueKind == JsonValueKind.String && string.Equals(value.GetString(), "true", StringComparison.OrdinalIgnoreCase));
}

public interface IGoogleOAuthService
{
    /// <summary>The sign-in shape: identity scopes only, and the account chooser. No refresh token is asked for because nothing is refreshed later.</summary>
    string BuildSignInUrl(string clientId, string redirectUri, string state, string codeChallenge);

    /// <summary>The sign-in exchange. <paramref name="redirectUri"/> has to be the same string that went out with the authorization request - Google compares them.</summary>
    Task<GoogleTokenResult> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri, string clientId, string clientSecret, CancellationToken ct);
}

/// <summary>
/// Google's OAuth 2.0 endpoints for signing in, called directly rather than
/// through Google.Apis.Auth - an authorization URL and one token exchange,
/// against an SDK whose main value would be a credential store Hatch does not
/// need.
///
/// Fail-soft: the exchange returns a result, never throws a transport failure
/// at its caller. A dead or throttling Google leaves the person at the sign-in
/// page with an error code.
/// </summary>
public class GoogleOAuthService(
    IHttpClientFactory httpClientFactory,
    TimeProvider time,
    ILogger<GoogleOAuthService> logger) : IGoogleOAuthService
{
    /// <summary>The named HttpClient registered in Program.cs. No BaseAddress - every call here is absolute.</summary>
    public const string HttpClientName = "Google";

    public const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    /// <summary>What signing in asks for: who the person is, and nothing else.</summary>
    public const string SignInScope = "openid email profile";

    /// <summary>What to assume when Google omits <c>expires_in</c>. Its access tokens are an hour today; assuming less is harmless.</summary>
    private const int DefaultExpiresInSeconds = 3600;

    public string BuildSignInUrl(string clientId, string redirectUri, string state, string codeChallenge) =>
        QueryHelpers.AddQueryString(AuthorizationEndpoint, new Dictionary<string, string?>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = SignInScope,
            // The chooser, so a browser signed in to several Google accounts is
            // asked which one rather than silently using the first. Neither
            // access_type=offline nor prompt=consent: no refresh token is
            // wanted, and consent on every sign-in would be a screen for nothing.
            ["prompt"] = "select_account",
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
        });

    public Task<GoogleTokenResult> ExchangeCodeAsync(
        string code, string codeVerifier, string redirectUri, string clientId, string clientSecret, CancellationToken ct) =>
        PostTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
            ["redirect_uri"] = redirectUri,
        }, clientId, clientSecret, ct);

    /// <summary>
    /// Reads <c>email</c> out of the id token's payload without validating the
    /// signature. That is deliberate, not an oversight: this JWT arrived in the
    /// body of Hatch's own TLS request to Google's token endpoint, so there is
    /// no untrusted hop between issuer and reader for a signature to defend
    /// against. Validation would earn its keep if the token came from a client.
    /// </summary>
    public static string? EmailFromIdToken(string? idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken)) return null;

        var segments = idToken.Split('.');
        if (segments.Length != 3) return null;

        try
        {
            using var payload = JsonDocument.Parse(WebEncoders.Base64UrlDecode(segments[1]));
            return payload.RootElement.TryGetProperty("email", out var email) && email.ValueKind == JsonValueKind.String
                ? NullIfEmpty(email.GetString())
                : null;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or JsonException)
        {
            return null;
        }
    }

    private async Task<GoogleTokenResult> PostTokenAsync(Dictionary<string, string> form, string clientId, string clientSecret, CancellationToken ct)
    {
        form["client_id"] = clientId;
        form["client_secret"] = clientSecret;

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var content = new FormUrlEncodedContent(form);
            using var response = await client.PostAsync(TokenEndpoint, content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = ErrorCodeFrom(body) ?? $"http_{(int)response.StatusCode}";
                // The code, never the body: an error response echoes back parts
                // of a request that carried the client secret and the grant.
                logger.LogWarning("Google token request rejected with {Status} {Error}", (int)response.StatusCode, error);
                return GoogleTokenResult.Failed(error);
            }

            if (JsonSerializer.Deserialize<TokenResponse>(body) is not { AccessToken: { Length: > 0 } accessToken } payload)
            {
                logger.LogWarning("Google token request succeeded but carried no access token");
                return GoogleTokenResult.Failed("no_access_token");
            }

            return GoogleTokenResult.Ok(new GoogleTokens(
                accessToken,
                NullIfEmpty(payload.RefreshToken),
                time.GetUtcNow().AddSeconds(payload.ExpiresIn ?? DefaultExpiresInSeconds),
                NullIfEmpty(payload.IdToken)));
        }
        catch (Exception ex) when (IsTransport(ex, ct))
        {
            logger.LogWarning(ex, "Google token request could not be completed");
            return GoogleTokenResult.Failed("unreachable");
        }
    }

    /// <summary>Google reports failures as <c>{"error":"invalid_grant", ...}</c>. A body that isn't that shape leaves the caller with the HTTP status instead.</summary>
    private static string? ErrorCodeFrom(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String
                ? NullIfEmpty(error.GetString())
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The failures that mean "Google didn't answer" rather than "the caller
    /// gave up" - HttpClient surfaces its own timeout as a TaskCanceledException,
    /// so the token has to be checked to tell the two apart.
    /// </summary>
    private static bool IsTransport(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested;

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int? ExpiresIn,
        [property: JsonPropertyName("id_token")] string? IdToken);
}

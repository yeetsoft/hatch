using System.Net;
using System.Text;
using System.Text.Json;
using Hatch.Api.Models.People;
using Hatch.Api.Controllers;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Hatch.Api.Services.Calendar;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Auth;

/// <summary>
/// Signing in with Google, end to end against a fake Google: the redirect out,
/// and everything the callback does with what comes back. The fake hands back a
/// hand-built unsigned id token, which is all the real path reads.
/// </summary>
public class GoogleSignInTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
    private const string ClientId = "client-id.apps.example";

    [Fact]
    public async Task StartRedirectsToTheAccountChooserWithPkceAndNoOfflineAccess()
    {
        var (controller, db, _, _) = New();

        var redirect = Assert.IsType<RedirectResult>(await controller.Start("/apps/family/", CancellationToken.None));

        Assert.StartsWith(GoogleOAuthService.AuthorizationEndpoint, redirect.Url);
        var query = QueryHelpers.ParseQuery(new Uri(redirect.Url).Query);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("select_account", query["prompt"]);
        Assert.Equal("openid email profile", query["scope"]);
        Assert.Equal(ClientId, query["client_id"]);
        Assert.False(query.ContainsKey("access_type"));
        var stored = await db.OAuthStates.SingleAsync();
        Assert.Equal(stored.State, query["state"]);
        Assert.Equal("/apps/family/", stored.ReturnTo);
        Assert.Equal("https://home.example.com/api/auth/google/callback", stored.RedirectUri);
    }

    [Fact]
    public async Task StartUsesTheConfiguredRedirectUriOverTheRequests()
    {
        var (controller, db, _, _) = New(o => o.Google.RedirectUri = "https://registered.example/cb");

        await controller.Start(null, CancellationToken.None);

        Assert.Equal("https://registered.example/cb", (await db.OAuthStates.SingleAsync()).RedirectUri);
    }

    [Theory]
    [InlineData("//evil.example.com")]
    [InlineData("https://evil.example.com/")]
    public async Task StartDoesNotStoreAReturnToThatLeavesTheOrigin(string returnTo)
    {
        var (controller, db, _, _) = New();

        await controller.Start(returnTo, CancellationToken.None);

        Assert.Null((await db.OAuthStates.SingleAsync()).ReturnTo);
    }

    [Fact]
    public async Task StartSweepsExpiredStates()
    {
        var (controller, db, _, _) = New();
        await SeedState(db, "old", expiresAt: Now.AddMinutes(-1));

        await controller.Start(null, CancellationToken.None);

        Assert.DoesNotContain(db.OAuthStates, s => s.State == "old");
    }

    [Fact]
    public async Task StartWithNoClientLandsOnTheShellNamingWhy()
    {
        var (controller, db, _, _) = New(o => o.Google = new GoogleAuthOptions());

        var redirect = Assert.IsType<RedirectResult>(await controller.Start(null, CancellationToken.None));

        Assert.Equal("/apps/auth/?error=google_not_configured", redirect.Url);
        Assert.Empty(db.OAuthStates);
    }

    [Fact]
    public async Task ASignInAfterAnAdminExistsCreatesAPendingPersonAnIdentityAndACookie()
    {
        var (controller, db, google, context) = New();
        await SeedPerson(db, PersonRole.Admin);
        await SeedState(db, "s1", returnTo: "/apps/family/");
        google.IdToken = Token(sub: "sub-1", email: "ada@example.com", name: "Ada Lovelace");

        var redirect = Assert.IsType<RedirectResult>(await controller.Callback("c", "s1", null, CancellationToken.None));

        Assert.Equal("/apps/family/", redirect.Url);
        var identity = await db.ExternalIdentities.Include(i => i.Person).SingleAsync();
        Assert.Equal("sub-1", identity.Subject);
        Assert.Equal("ada@example.com", identity.Email);
        Assert.Equal("Ada Lovelace", identity.Person!.Name);
        Assert.Equal(PersonRole.Pending, identity.Person.Role);
        var grant = await db.AuthGrants.SingleAsync();
        Assert.Equal(identity.PersonId, grant.PersonId);
        Assert.Equal(AuthGrantKind.Interactive, grant.Kind);
        Assert.Equal("Chrome on macOS", grant.Label);
        Assert.Equal(Chrome, grant.UserAgent);
        Assert.Equal("10.0.0.7", grant.LastSeenIp);
        Assert.Equal(Now, grant.CookieIssuedAt);
        Assert.Contains("hatch_grant=", context.Response.Headers.SetCookie.ToString());
        // The state is spent.
        Assert.Empty(db.OAuthStates);
    }

    [Fact]
    public async Task ACallbackWithNoReturnToLandsOnTheHatchApp()
    {
        var (controller, db, google, _) = New();
        await SeedState(db, "s1");
        google.IdToken = Token();

        var redirect = Assert.IsType<RedirectResult>(await controller.Callback("c", "s1", null, CancellationToken.None));

        Assert.Equal("/apps/hatch/", redirect.Url);
    }

    [Fact]
    public async Task ThePersonIsNamedFromTheEmailWhenTheTokenHasNoName()
    {
        var (controller, db, google, _) = New();
        await SeedState(db, "s1");
        google.IdToken = Token(email: "grace.hopper@example.com", name: null);

        await controller.Callback("c", "s1", null, CancellationToken.None);

        Assert.Equal("grace.hopper", (await db.People.SingleAsync()).Name);
    }

    [Fact]
    public async Task ASecondSignInFindsTheSamePersonAndTouchesLastSignInAt()
    {
        var (controller, db, google, time, _) = NewWithClock();
        google.IdToken = Token(sub: "sub-1");
        await SeedState(db, "s1");
        await controller.Callback("c", "s1", null, CancellationToken.None);
        var first = await db.ExternalIdentities.AsNoTracking().SingleAsync();

        time.Advance(TimeSpan.FromHours(2));
        await SeedState(db, "s2", createdAt: time.GetUtcNow());
        await controller.Callback("c", "s2", null, CancellationToken.None);

        var second = await db.ExternalIdentities.AsNoTracking().SingleAsync();
        Assert.Equal(first.PersonId, second.PersonId);
        Assert.Equal(Now, second.CreatedAt);
        Assert.Equal(Now.AddHours(2), second.LastSignInAt);
        Assert.Single(db.People);
        Assert.Equal(2, await db.AuthGrants.CountAsync());
    }

    private static async Task<EfPerson> SeedUnclaimed(AppDbContext db, string email, PersonRole role = PersonRole.User)
    {
        var person = new EfPerson { Name = "Ada", Role = role, CreatedAt = Now, UpdatedAt = Now };
        db.ExternalIdentities.Add(new EfExternalIdentity
        {
            Provider = EfExternalIdentity.GoogleProvider, Subject = null, Email = email, Person = person, CreatedAt = Now,
        });
        await db.SaveChangesAsync();
        return person;
    }

    [Fact]
    public async Task AFirstSignInWithAPreApprovedEmailClaimsItAndKeepsTheRole()
    {
        var (controller, db, google, _) = New();
        var seeded = await SeedUnclaimed(db, "ada@example.com");
        // Another Admin does not exist: the first-Admin rule must not apply to a claim.
        await SeedState(db, "s1");
        google.IdToken = Token(sub: "sub-9", email: "Ada@Example.com");

        var redirect = Assert.IsType<RedirectResult>(await controller.Callback("c", "s1", null, CancellationToken.None));

        Assert.Equal("/apps/hatch/", redirect.Url);
        var person = await db.People.SingleAsync();
        Assert.Equal(seeded.Id, person.Id);
        Assert.Equal(PersonRole.User, person.Role);
        var identity = await db.ExternalIdentities.SingleAsync();
        Assert.Equal("sub-9", identity.Subject);
        Assert.Equal(Now, identity.LastSignInAt);
        Assert.Equal("Ada@Example.com", identity.Email);
        Assert.Equal(person.Id, (await db.AuthGrants.SingleAsync()).PersonId);
    }

    [Fact]
    public async Task AnUnverifiedEmailDoesNotClaimAPreApprovedRow()
    {
        var (controller, db, google, _) = New();
        await SeedUnclaimed(db, "ada@example.com");
        await SeedState(db, "s1");
        google.IdToken = Token(sub: "sub-9", email: "ada@example.com", emailVerified: false);

        var redirect = Assert.IsType<RedirectResult>(await controller.Callback("c", "s1", null, CancellationToken.None));

        Assert.Equal("/apps/auth/?error=email_unverified", redirect.Url);
        Assert.Null((await db.ExternalIdentities.SingleAsync()).Subject);
        Assert.Empty(db.AuthGrants);
    }

    [Fact]
    public async Task ABoundIdentityIsNeverRematchedByEmail()
    {
        var (controller, db, google, _) = New();
        await SeedUnclaimed(db, "ada@example.com", PersonRole.Admin); // an Admin exists, so a stranger is Pending
        await SeedState(db, "s1");
        google.IdToken = Token(sub: "sub-9", email: "ada@example.com");
        await controller.Callback("c", "s1", null, CancellationToken.None);

        await SeedState(db, "s2");
        google.IdToken = Token(sub: "sub-other", email: "ada@example.com");
        await controller.Callback("c", "s2", null, CancellationToken.None);

        Assert.Equal(2, await db.People.CountAsync());
        var bound = await db.ExternalIdentities.SingleAsync(i => i.Subject == "sub-9");
        var other = await db.ExternalIdentities.Include(i => i.Person).SingleAsync(i => i.Subject == "sub-other");
        Assert.NotEqual(bound.PersonId, other.PersonId);
        Assert.Equal(PersonRole.Pending, other.Person!.Role);
    }

    [Fact]
    public async Task TheFirstSignInOnAnInstallWithNoAdminBecomesAnAdmin()
    {
        var (controller, db, google, _) = New();
        await SeedState(db, "s1");
        google.IdToken = Token(sub: "sub-1", email: "ada@example.com");

        await controller.Callback("c", "s1", null, CancellationToken.None);

        var person = await db.People.SingleAsync();
        Assert.Equal(PersonRole.Admin, person.Role);
        Assert.Equal("admin", PersonRoles.ToWire(person.Role));
    }

    [Fact]
    public async Task TheSecondNewSignInIsPending()
    {
        var (controller, db, google, _) = New();
        await SeedState(db, "s1");
        google.IdToken = Token(sub: "sub-1", email: "ada@example.com");
        await controller.Callback("c", "s1", null, CancellationToken.None);

        await SeedState(db, "s2");
        google.IdToken = Token(sub: "sub-2", email: "grace@example.com");
        await controller.Callback("c", "s2", null, CancellationToken.None);

        var roles = (await db.ExternalIdentities.Include(i => i.Person).ToListAsync())
            .ToDictionary(i => i.Subject, i => i.Person!.Role);
        Assert.Equal(PersonRole.Admin, roles["sub-1"]);
        Assert.Equal(PersonRole.Pending, roles["sub-2"]);
    }

    [Fact]
    public async Task TheRuleIsNoAdminNotNoPeople()
    {
        var (controller, db, google, _) = New();
        await SeedPerson(db, PersonRole.Pending);
        await SeedPerson(db, PersonRole.User);
        await SeedState(db, "s1");
        google.IdToken = Token(sub: "sub-1");

        await controller.Callback("c", "s1", null, CancellationToken.None);

        var identity = await db.ExternalIdentities.Include(i => i.Person).SingleAsync();
        Assert.Equal(PersonRole.Admin, identity.Person!.Role);
    }

    [Fact]
    public async Task ASignInNeverChangesAKnownPersonsRole()
    {
        var (controller, db, google, _) = New();
        await SeedState(db, "s1");
        google.IdToken = Token(sub: "sub-1");
        await controller.Callback("c", "s1", null, CancellationToken.None);
        var person = await db.People.SingleAsync();

        // Demoted with nobody left to promote: signing in again does not restore it.
        person.Role = PersonRole.Pending;
        await db.SaveChangesAsync();
        await SeedState(db, "s2");
        await controller.Callback("c", "s2", null, CancellationToken.None);
        Assert.Equal(PersonRole.Pending, (await db.People.SingleAsync()).Role);

        person.Role = PersonRole.Admin;
        await db.SaveChangesAsync();
        await SeedState(db, "s3");
        await controller.Callback("c", "s3", null, CancellationToken.None);
        Assert.Equal(PersonRole.Admin, (await db.People.SingleAsync()).Role);
    }

    [Fact]
    public async Task AChangedEmailUpdatesTheRowWithoutChangingThePerson()
    {
        var (controller, db, google, _) = New();
        google.IdToken = Token(sub: "sub-1", email: "old@example.com");
        await SeedState(db, "s1");
        await controller.Callback("c", "s1", null, CancellationToken.None);
        var personId = (await db.People.SingleAsync()).Id;

        google.IdToken = Token(sub: "sub-1", email: "new@example.com");
        await SeedState(db, "s2");
        await controller.Callback("c", "s2", null, CancellationToken.None);

        var identity = await db.ExternalIdentities.SingleAsync();
        Assert.Equal("new@example.com", identity.Email);
        Assert.Equal(personId, identity.PersonId);
        Assert.Single(db.People);
    }

    [Fact]
    public async Task ASameEmailUnderAnotherSubIsAnotherPerson()
    {
        var (controller, db, google, _) = New();
        google.IdToken = Token(sub: "sub-1", email: "shared@example.com");
        await SeedState(db, "s1");
        await controller.Callback("c", "s1", null, CancellationToken.None);

        google.IdToken = Token(sub: "sub-2", email: "shared@example.com");
        await SeedState(db, "s2");
        await controller.Callback("c", "s2", null, CancellationToken.None);

        Assert.Equal(2, await db.People.CountAsync());
    }

    [Fact]
    public async Task ASpentStateIsRefused()
    {
        var (controller, db, google, _) = New();
        await SeedState(db, "s1");
        google.IdToken = Token();
        await controller.Callback("c", "s1", null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(await controller.Callback("c", "s1", null, CancellationToken.None));

        Assert.Equal("/apps/auth/?error=unknown_state", redirect.Url);
        Assert.Single(db.AuthGrants);
    }

    [Fact]
    public async Task AnExpiredStateIsRefusedAndStillSpent()
    {
        var (controller, db, google, context) = New();
        await SeedState(db, "s1", expiresAt: Now.AddSeconds(-1));
        google.IdToken = Token();

        var redirect = Assert.IsType<RedirectResult>(await controller.Callback("c", "s1", null, CancellationToken.None));

        Assert.Equal("/apps/auth/?error=expired_state", redirect.Url);
        Assert.Empty(db.OAuthStates);
        AssertNothingMinted(db, context);
    }

    [Fact]
    public async Task AnUnknownStateIsRefused()
    {
        var (controller, db, _, context) = New();

        var redirect = Assert.IsType<RedirectResult>(await controller.Callback("c", "nope", null, CancellationToken.None));

        Assert.Equal("/apps/auth/?error=unknown_state", redirect.Url);
        AssertNothingMinted(db, context);
    }

    [Theory]
    [InlineData("aud", "someone-elses-client", "wrong_audience")]
    [InlineData("iss", "https://evil.example.com", "wrong_issuer")]
    public async Task ATokenIssuedToOrByTheWrongPartyIsRefused(string claim, string value, string code)
    {
        var (controller, db, google, context) = New();
        await SeedState(db, "s1");
        google.IdToken = claim == "aud" ? Token(aud: value) : Token(iss: value);

        var redirect = Assert.IsType<RedirectResult>(await controller.Callback("c", "s1", null, CancellationToken.None));

        Assert.Equal($"/apps/auth/?error={code}", redirect.Url);
        AssertNothingMinted(db, context);
    }

    [Fact]
    public async Task BothOfGoogleIssuerSpellingsAreAccepted()
    {
        var (controller, db, google, _) = New();
        await SeedState(db, "s1");
        google.IdToken = Token(iss: "accounts.google.com");

        var redirect = Assert.IsType<RedirectResult>(await controller.Callback("c", "s1", null, CancellationToken.None));

        Assert.Equal("/apps/hatch/", redirect.Url);
    }

    [Fact]
    public async Task AnUnverifiedEmailIsRefused()
    {
        var (controller, db, google, context) = New();
        await SeedState(db, "s1");
        google.IdToken = Token(emailVerified: false);

        var redirect = Assert.IsType<RedirectResult>(await controller.Callback("c", "s1", null, CancellationToken.None));

        Assert.Equal("/apps/auth/?error=email_unverified", redirect.Url);
        AssertNothingMinted(db, context);
    }

    [Fact]
    public async Task AnEmptySubIsRefused()
    {
        var (controller, db, google, context) = New();
        await SeedState(db, "s1");
        google.IdToken = Token(sub: "");

        var redirect = Assert.IsType<RedirectResult>(await controller.Callback("c", "s1", null, CancellationToken.None));

        Assert.Equal("/apps/auth/?error=no_subject", redirect.Url);
        AssertNothingMinted(db, context);
    }

    [Fact]
    public async Task ATokenExchangeFailureIsRefusedWithGooglesCode()
    {
        var (controller, db, google, context) = New();
        await SeedState(db, "s1");
        google.Result = GoogleTokenResult.Failed("invalid_grant");

        var redirect = Assert.IsType<RedirectResult>(await controller.Callback("c", "s1", null, CancellationToken.None));

        Assert.Equal("/apps/auth/?error=invalid_grant", redirect.Url);
        AssertNothingMinted(db, context);
    }

    [Fact]
    public async Task DecliningAtGoogleLandsOnTheShellWithThatCode()
    {
        var (controller, db, google, context) = New();

        var redirect = Assert.IsType<RedirectResult>(await controller.Callback(null, null, "access_denied", CancellationToken.None));

        Assert.Equal("/apps/auth/?error=access_denied", redirect.Url);
        Assert.Equal(0, google.Exchanges);
        AssertNothingMinted(db, context);
    }

    [Fact]
    public async Task AnErrorCodeIsClampedBeforeItIsReflected()
    {
        var (controller, _, _, _) = New();

        var redirect = Assert.IsType<RedirectResult>(
            await controller.Callback(null, null, "<script>alert(1)</script>", CancellationToken.None));

        Assert.DoesNotContain("<", redirect.Url);
        Assert.StartsWith("/apps/auth/?error=", redirect.Url);
    }

    [Fact]
    public async Task TheExchangeIsMadeWithTheConfiguredClientAndTheStoredVerifier()
    {
        var (controller, db, google, _) = New();
        await SeedState(db, "s1");
        google.IdToken = Token();

        await controller.Callback("the-code", "s1", null, CancellationToken.None);

        Assert.Equal(("the-code", "verifier", "https://home.example.com/cb", ClientId, "secret"), google.LastExchange);
    }

    [Fact]
    public void AuthOptionsBindsTheGoogleSection()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Google:ClientId"] = "id",
            ["Auth:Google:ClientSecret"] = "secret",
            ["Auth:Google:RedirectUri"] = "https://x.example/cb",
        }).Build();

        var bound = config.GetSection(AuthOptions.SectionName).Get<AuthOptions>()!;

        Assert.Equal("id", bound.Google.ClientId);
        Assert.Equal("secret", bound.Google.ClientSecret);
        Assert.Equal("https://x.example/cb", bound.Google.RedirectUri);
        Assert.True(bound.Google.Configured);
        Assert.False(new AuthOptions().Google.Configured);
    }

    [Theory]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36", "Chrome on macOS")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1", "Safari on iPhone")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36 Edg/126.0.0.0", "Edge on Windows")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64; rv:127.0) Gecko/20100101 Firefox/127.0", "Firefox on Linux")]
    [InlineData("Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Mobile Safari/537.36", "Chrome on Android")]
    [InlineData("curl/8.4.0", "Web browser")]
    [InlineData("", "Web browser")]
    [InlineData(null, "Web browser")]
    public void TheLabelNamesBrowserAndPlatformNotTheRawString(string? userAgent, string expected) =>
        Assert.Equal(expected, UserAgentLabel.From(userAgent));

    [Fact]
    public void GoogleIdTokenReadsTheClaimsAndToleratesGarbage()
    {
        var token = GoogleIdToken.Parse(Token(sub: "s", email: "a@b.c", name: "N", aud: "a", iss: "i", emailVerified: true));

        Assert.Equal(new GoogleIdToken("s", "a@b.c", true, "N", "https://example.com/p.png", "a", "i"), token);
        Assert.Null(GoogleIdToken.Parse("not-a-jwt"));
        Assert.Null(GoogleIdToken.Parse("a.!!!.c"));
        Assert.Null(GoogleIdToken.Parse(null));
    }

    private const string Chrome =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

    private static void AssertNothingMinted(AppDbContext db, HttpContext context)
    {
        Assert.Empty(db.People);
        Assert.Empty(db.ExternalIdentities);
        Assert.Empty(db.AuthGrants);
        Assert.Equal(0, context.Response.Headers.SetCookie.Count);
    }

    private static async Task SeedPerson(AppDbContext db, PersonRole role)
    {
        db.People.Add(new EfPerson { Name = $"seeded {role}", Role = role, CreatedAt = Now, UpdatedAt = Now });
        await db.SaveChangesAsync();
    }

    private static Task SeedState(
        AppDbContext db, string state, string? returnTo = null, DateTimeOffset? createdAt = null, DateTimeOffset? expiresAt = null)
    {
        var created = createdAt ?? Now;
        db.OAuthStates.Add(new EfOAuthState
        {
            State = state,
            CodeVerifier = "verifier",
            RedirectUri = "https://home.example.com/cb",
            ReturnTo = returnTo,
            CreatedAt = created,
            ExpiresAt = expiresAt ?? created.AddMinutes(10),
        });
        return db.SaveChangesAsync();
    }

    /// <summary>An unsigned id token: header and signature are filler, since nothing reads them.</summary>
    private static string Token(
        string sub = "sub-1", string email = "ada@example.com", string? name = "Ada", string aud = ClientId,
        string iss = "https://accounts.google.com", bool emailVerified = true)
    {
        var claims = new Dictionary<string, object?>
        {
            ["sub"] = sub, ["email"] = email, ["email_verified"] = emailVerified, ["name"] = name,
            ["picture"] = "https://example.com/p.png", ["aud"] = aud, ["iss"] = iss,
        };
        var payload = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(claims)));
        return $"e30.{payload}.sig";
    }

    private static (GoogleSignInController Controller, AppDbContext Db, FakeGoogle Google, HttpContext Context) New(
        Action<AuthOptions>? configure = null)
    {
        var (controller, db, google, _, context) = NewWithClock(configure);
        return (controller, db, google, context);
    }

    private static (GoogleSignInController Controller, AppDbContext Db, FakeGoogle Google, FakeTimeProvider Time, HttpContext Context) NewWithClock(
        Action<AuthOptions>? configure = null)
    {
        var auth = new AuthOptions
        {
            Enabled = true,
            CookieName = "hatch_grant",
            Google = new GoogleAuthOptions { ClientId = ClientId, ClientSecret = "secret" },
        };
        configure?.Invoke(auth);

        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var google = new FakeGoogle();
        var time = new FakeTimeProvider(Now);

        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.7");
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("home.example.com");
        context.Request.Headers.UserAgent = Chrome;

        var controller = new GoogleSignInController(db, google, Options.Create(auth), time, NullLogger<GoogleSignInController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
        return (controller, db, google, time, context);
    }

    /// <summary>Google as the callback sees it: the real URL builder, and whatever token the test sets.</summary>
    private sealed class FakeGoogle : IGoogleOAuthService
    {
        private readonly GoogleOAuthService real = new(null!, null!, TimeProvider.System, NullLogger<GoogleOAuthService>.Instance);

        public string? IdToken { get; set; }

        /// <summary>Overrides IdToken when set, to reach the exchange failure.</summary>
        public GoogleTokenResult? Result { get; set; }

        public int Exchanges { get; private set; }

        public (string Code, string Verifier, string RedirectUri, string ClientId, string Secret)? LastExchange { get; private set; }

        public string BuildAuthorizationUrl(string clientId, string redirectUri, string state, string codeChallenge) =>
            real.BuildAuthorizationUrl(clientId, redirectUri, state, codeChallenge);

        public string BuildSignInUrl(string clientId, string redirectUri, string state, string codeChallenge) =>
            real.BuildSignInUrl(clientId, redirectUri, state, codeChallenge);

        public Task<GoogleTokenResult> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri, CancellationToken ct) =>
            throw new NotSupportedException("The sign-in flow must use the explicit-client overload.");

        public Task<GoogleTokenResult> ExchangeCodeAsync(
            string code, string codeVerifier, string redirectUri, string clientId, string clientSecret, CancellationToken ct)
        {
            Exchanges++;
            LastExchange = (code, codeVerifier, redirectUri, clientId, clientSecret);
            return Task.FromResult(Result ?? GoogleTokenResult.Ok(new GoogleTokens("access", null, Now.AddHours(1), IdToken)));
        }

        public Task<GoogleTokenResult> RefreshAsync(string refreshToken, CancellationToken ct) => throw new NotSupportedException();

        public Task<bool> RevokeAsync(string token, CancellationToken ct) => throw new NotSupportedException();
    }
}

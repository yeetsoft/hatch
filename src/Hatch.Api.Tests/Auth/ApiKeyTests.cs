using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Models.Auth;
using Hatch.Api.Services.Auth;
using Hatch.Api.Services.Media;
using Hatch.Api.Tests.Hatch;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Auth;

/// <summary>
/// The credential that is not a browser, from the row up: what the database
/// holds, what the wall accepts, and what a key may reach once it is through.
///
/// The properties worth breaking a build over are the boundaries. A key reaches
/// the routes that accept one and nothing else - most importantly not the
/// endpoints that mint keys, because a key that could mint a key would make the
/// key system decorative. And a revoked key is refused even when the same
/// request is carrying a perfectly good cookie, which is the case revocation
/// exists to close.
/// </summary>
public class ApiKeyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    // ---- What the database holds ----

    [Fact]
    public async Task AMintedKey_IsStoredOnlyAsAHash()
    {
        var (service, db, _) = NewService();

        var created = await service.CreateApiKeyAsync("Claude in VS Code", null, default);

        var row = await db.ApiKeys.AsNoTracking().SingleAsync();
        Assert.StartsWith(AuthTokens.ApiKeyPrefix, created!.Secret);
        Assert.Equal(AuthTokens.ApiKeyPrefix.Length + AuthTokens.ApiKeyBodyLength, created.Secret.Length);
        Assert.Equal(AuthTokens.Hash(created.Secret), row.Hash);
        Assert.Equal(created.Secret[..EfApiKey.PrefixLength], row.Prefix);
        Assert.Null(row.RevokedAt);
    }

    /// <summary>
    /// The whole secret must not be anywhere but the response that minted it.
    /// The prefix is deliberately stored and is deliberately short - enough to
    /// match a row against the value in a config file, useless to anybody
    /// holding it.
    /// </summary>
    [Fact]
    public async Task TheSecretItself_IsNowhereInTheRow()
    {
        var (service, db, _) = NewService();

        var created = await service.CreateApiKeyAsync("Claude", null, default);

        var row = await db.ApiKeys.AsNoTracking().SingleAsync();
        Assert.DoesNotContain(created!.Secret, row.Prefix);
        Assert.Equal(EfApiKey.PrefixLength, row.Prefix.Length);
    }

    [Fact]
    public async Task TwoKeys_CannotShareAName()
    {
        var (service, _, _) = NewService();
        await service.CreateApiKeyAsync("Claude", null, default);

        Assert.Null(await service.CreateApiKeyAsync("Claude", null, default));
    }

    // ---- What the wall accepts ----

    [Fact]
    public async Task ALiveKey_Verifies()
    {
        var (service, _, _) = NewService();
        var created = await service.CreateApiKeyAsync("Claude", null, default);

        var verified = await service.VerifyApiKeyAsync(created!.Secret, default);

        Assert.Equal("Claude", verified?.Name);
    }

    [Fact]
    public async Task AKeyVerifiesWithItsOwner()
    {
        var (service, db, _) = NewService();
        var person = new EfPerson { Name = "Ada", Role = PersonRole.Admin, CreatedAt = Now, UpdatedAt = Now };
        db.People.Add(person);
        await db.SaveChangesAsync();
        var created = await service.CreateApiKeyAsync("Claude", person.Id, default);
        // The service shares this context, so without the clear EF's
        // relationship fix-up would fill Owner whether or not the query asked.
        db.ChangeTracker.Clear();

        var verified = await service.VerifyApiKeyAsync(created!.Secret, default);

        Assert.Equal(PersonRole.Admin, verified?.Owner?.Role);
    }

    [Fact]
    public async Task AKeyWithNoOwner_VerifiesWithNone()
    {
        var (service, db, _) = NewService();
        var created = await service.CreateApiKeyAsync("Claude", null, default);
        db.ChangeTracker.Clear();

        var verified = await service.VerifyApiKeyAsync(created!.Secret, default);

        Assert.NotNull(verified);
        Assert.Null(verified.Owner);
    }

    [Fact]
    public async Task ARevokedKey_StopsVerifying()
    {
        var (service, _, _) = NewService();
        var created = await service.CreateApiKeyAsync("Claude", null, default);

        Assert.True(await service.RevokeApiKeyAsync(created!.Key.Id, default));

        Assert.Null(await service.VerifyApiKeyAsync(created.Secret, default));
    }

    /// <summary>
    /// The column is a timestamp rather than a bool because "when did this stop
    /// working" is the question, and a second revocation must not quietly
    /// rewrite the answer.
    /// </summary>
    [Fact]
    public async Task RevokingTwice_KeepsTheFirstTimestamp()
    {
        var (service, db, time) = NewService();
        var created = await service.CreateApiKeyAsync("Claude", null, default);
        await service.RevokeApiKeyAsync(created!.Key.Id, default);

        time.Advance(TimeSpan.FromHours(3));
        Assert.True(await service.RevokeApiKeyAsync(created.Key.Id, default));

        Assert.Equal(Now, (await db.ApiKeys.AsNoTracking().SingleAsync()).RevokedAt);
    }

    [Theory]
    [InlineData("hatch_ak_nobodyminted00000000000000000000")]
    [InlineData("")]
    [InlineData(null)]
    public async Task AKeyNobodyMinted_DoesNotVerify(string? secret)
    {
        var (service, _, _) = NewService();
        await service.CreateApiKeyAsync("Claude", null, default);

        Assert.Null(await service.VerifyApiKeyAsync(secret, default));
    }

    /// <summary>
    /// The hot path for every request an agent makes. Unthrottled this is a
    /// write per request, which is the same reason a grant's last-seen is
    /// throttled and on the same setting.
    /// </summary>
    [Fact]
    public async Task LastUsed_IsWrittenAtMostOncePerThrottle()
    {
        var (service, db, time) = NewService();
        var created = await service.CreateApiKeyAsync("Claude", null, default);

        await service.VerifyApiKeyAsync(created!.Secret, default);
        time.Advance(TimeSpan.FromSeconds(5));
        await service.VerifyApiKeyAsync(created.Secret, default);

        Assert.Equal(Now, (await db.ApiKeys.AsNoTracking().SingleAsync()).LastUsedAt);

        time.Advance(TimeSpan.FromMinutes(2));
        await service.VerifyApiKeyAsync(created.Secret, default);

        Assert.Equal(Now.AddSeconds(5).AddMinutes(2), (await db.ApiKeys.AsNoTracking().SingleAsync()).LastUsedAt);
    }

    // ---- The gate's second lane ----

    [Fact]
    public async Task ABearerKey_IsAuthenticatedWithoutACookie()
    {
        var auth = new StubAuthService(null) { Key = Key("Claude") };
        var gate = NewGate(auth);

        var decision = await gate.EvaluateAsync("/api/hatch/board", "hatch.example.com", [], "hatch_ak_secret", null, default);

        Assert.Equal(AuthOutcome.Authenticated, decision.Outcome);
        Assert.Equal("Claude", decision.ApiKey?.Name);
        Assert.Equal("hatch_ak_secret", Assert.Single(auth.KeysVerified));
        // No grant and no token: there is no cookie on this lane to re-issue.
        Assert.Null(decision.Grant);
        Assert.Null(decision.Token);
    }

    [Fact]
    public async Task AKeyNoRowAnswersTo_IsChallengedAsUnknownKey()
    {
        var gate = NewGate(new StubAuthService(null));

        var decision = await gate.EvaluateAsync("/api/hatch/board", "hatch.example.com", [], "hatch_ak_gone", null, default);

        Assert.Equal(AuthOutcome.Challenge, decision.Outcome);
        Assert.Equal(AuthDecision.UnknownKey, decision.Reason);
    }

    /// <summary>
    /// The case revocation exists to close. A caller that presents a key meant
    /// to present a key: falling through to whatever cookies the same request
    /// happened to carry would serve a request made with a dead credential.
    /// </summary>
    [Fact]
    public async Task ADeadKey_DoesNotFallThroughToACookieOnTheSameRequest()
    {
        var auth = new StubAuthService(Grant("Nathan's laptop"));
        var gate = NewGate(auth);

        var decision = await gate.EvaluateAsync("/api/hatch/board", "hatch.example.com", ["a-good-token"], "hatch_ak_revoked", null, default);

        Assert.Equal(AuthOutcome.Challenge, decision.Outcome);
        Assert.Empty(auth.Verified);
    }

    // ---- What a key may reach ----

    [Fact]
    public async Task AKeyOnARouteThatAcceptsOne_IsAllowed()
    {
        var gate = NewRoleGate(key: Key("Claude"));

        var decision = await gate.EvaluateAsync("GET", "/api/hatch/board", null, PersonRole.User, true, default);

        Assert.Equal(RoleOutcome.Key, decision.Outcome);
        Assert.True(decision.IsAllowed);
        Assert.Equal("Claude", decision.ApiKey?.Name);
    }

    /// <summary>
    /// A key carries nothing but a name, and that is enough: the route decides.
    /// Accepted where <c>AcceptKey</c> is set, refused <c>key_not_accepted</c>
    /// where it is not.
    /// </summary>
    [Theory]
    [InlineData(true, true, null)]
    [InlineData(false, false, RoleDecision.KeyNotAccepted)]
    public async Task AKeyBuiltWithNoExtraFields_IsDecidedOnTheRouteAlone(bool acceptKey, bool allowed, string? reason)
    {
        var gate = NewRoleGate(key: Key("Claude"));

        var decision = await gate.EvaluateAsync("GET", "/api/hatch/board", null, PersonRole.User, acceptKey, default);

        Assert.Equal(allowed, decision.IsAllowed);
        Assert.Equal(reason, decision.Reason);
    }

    /// <summary>
    /// The boundary the whole scheme rests on. Every route that accepts no
    /// key - minting credentials, revoking sessions, editing the house - stays the
    /// operator's, and a key reaching one is refused rather than treated as an
    /// unnamed administrator.
    /// </summary>
    [Fact]
    public async Task AKeyReachingARouteThatAcceptsNone_IsRefused()
    {
        var gate = NewRoleGate(key: Key("Claude"));

        var decision = await gate.EvaluateAsync("POST", "/api/hatch/runners", null, PersonRole.Admin, acceptKey: false, default);

        Assert.False(decision.IsAllowed);
        Assert.Equal(RoleDecision.KeyNotAccepted, decision.Reason);
    }

    /// <summary>
    /// Accepting a key widens nothing for a person. A person who has not
    /// been let in is refused from a key-accepting route exactly as they are
    /// from every other one.
    /// </summary>
    [Fact]
    public async Task AKeyAcceptingRoute_IsStillClosedToAPendingPerson()
    {
        var person = new EfPerson { Name = "Ada", Role = PersonRole.Pending, CreatedAt = Now, UpdatedAt = Now };
        var gate = NewRoleGate(grant: Grant("Ada's iPhone", person));

        var decision = await gate.EvaluateAsync("GET", "/api/hatch/board", null, PersonRole.User, true, default);

        Assert.False(decision.IsAllowed);
        Assert.Equal(RoleDecision.PendingApproval, decision.Reason);
    }

    // ---- The actor a key writes ----

    [Fact]
    public async Task AKeysName_IsTheActorInAnAuditTrail()
    {
        var caller = NewCaller(key: Key("Claude"));

        Assert.Equal("Claude", await caller.ActorNameAsync(default));
    }

    /// <summary>
    /// An unlinked device and a request carrying nothing at all - the same
    /// honest answer rather than an empty column.
    ///
    /// Under a wall, which is the only place it is still the answer: with the
    /// wall off, whoever is at the machine has a name (see
    /// <see cref="LocalCaller"/>) and this column reads that instead.
    /// </summary>
    [Fact]
    public async Task WithNobodyHoldingThePhone_TheActorIsTheOperator()
    {
        Assert.Equal(CallerIdentity.Unattributed, await NewCaller().ActorNameAsync(default));
    }

    /// <summary>
    /// A bearer key works against `make run`, where Auth:Enabled is false and
    /// no middleware ever looked at a header. An agent developing against a
    /// local API is the first user of that lane.
    /// </summary>
    [Fact]
    public async Task WithTheWallOff_AKeyIsStillReadOffTheHeader()
    {
        var auth = new StubAuthService(null) { Key = Key("Claude") };
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer hatch_ak_secret";

        var caller = new CallerIdentity(Accessor(context), auth, Options.Create(new AuthOptions()), new StubSiteSettings());

        Assert.Equal("Claude", (await caller.ApiKeyAsync(default))?.Name);
        Assert.Equal("hatch_ak_secret", Assert.Single(auth.KeysVerified));
    }

    // ---- Reading the header ----

    [Theory]
    [InlineData("Bearer hatch_ak_secret", "hatch_ak_secret")]
    [InlineData("bearer hatch_ak_secret", "hatch_ak_secret")]
    [InlineData("Bearer   hatch_ak_secret  ", "hatch_ak_secret")]
    // Not a scheme this wall has a table for. Answering it would be inventing a
    // credential format.
    [InlineData("Basic dXNlcjpwYXNz", null)]
    [InlineData("Bearer", null)]
    [InlineData("Bearer ", null)]
    [InlineData("", null)]
    public void TheAuthorizationHeader_IsReadOrIgnored(string header, string? expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = header;

        Assert.Equal(expected, AuthBearer.Read(context.Request));
    }

    // ---- Harness ----

    private static EfApiKey Key(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Prefix = "hatch_ak_abc",
        Hash = AuthTokens.Hash("hatch_ak_secret"),
        CreatedAt = Now,
    };

    private static EfAuthGrant Grant(string label, EfPerson? person = null) => new()
    {
        TokenHash = AuthTokens.Hash("a-token"),
        Label = label,
        PersonId = person?.Id,
        Person = person,
        Kind = AuthGrantKind.Interactive,
        CreatedAt = Now,
        CookieIssuedAt = Now,
    };

    private static AuthGate NewGate(IAuthService auth) =>
        new(auth,
            Options.Create(new AuthOptions { Enabled = true }),
            Options.Create(new MediaLibraryOptions { RequestPath = "/media" }),
            NullLogger<AuthGate>.Instance);

    private static RoleGate NewRoleGate(EfApiKey? key = null, EfAuthGrant? grant = null) =>
        new(NewCaller(key, grant),
            Options.Create(new AuthOptions { Enabled = true }),
            NullLogger<RoleGate>.Instance);

    /// <summary>
    /// A caller identity backed by a real <see cref="CallerIdentity"/> over a
    /// context the middleware has already decided - which is what a request
    /// looks like everywhere the wall is on.
    /// </summary>
    private static CallerIdentity NewCaller(EfApiKey? key = null, EfAuthGrant? grant = null)
    {
        var context = new DefaultHttpContext();
        if (key is not null) context.SetApiKey(key);
        if (grant is not null) context.SetAuthGrant(grant);

        // Enabled, because that is what the summary above says this helper is:
        // a request under a wall. It was left at the default while nothing
        // branched on it; local mode's third lane is the first thing that does,
        // and an unauthenticated caller there is the local person rather than
        // nobody.
        return new CallerIdentity(
            Accessor(context),
            new StubAuthService(null),
            Options.Create(new AuthOptions { Enabled = true }),
            new StubSiteSettings());
    }

    private static IHttpContextAccessor Accessor(HttpContext context) =>
        new HttpContextAccessor { HttpContext = context };

    private static (IAuthService Service, AppDbContext Db, FakeTimeProvider Time) NewService()
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var time = new FakeTimeProvider(Now);

        return (new AuthService(db, Options.Create(new AuthOptions()), time, NullLogger<AuthService>.Instance), db, time);
    }
}

using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Hatch.Api.Services.Media;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;

namespace Hatch.Api.Tests.Auth;

/// <summary>
/// Who gets the board - every document request now that it is the whole
/// origin, rather than one bundle under a prefix. Three things have to hold,
/// and each is a different kind of bug if it does not: the deep links are
/// covered as well as the assets (or /issues/AER-12 serves index.html to
/// anyone), the refusal is a 404 rather than a 403 (or it advertises what it
/// is withholding), and every path the wall itself already lets through -
/// the API, the health probes, the sign-in shell - is untouched.
/// </summary>
public class AdminAppMiddlewareTests
{
    [Theory]
    // The board itself, a deep link a hard refresh produces, and a hashed
    // asset - the first is UseStaticFiles, the second is MapFallbackToFile,
    // and the third would be a hole in either.
    [InlineData("/")]
    [InlineData("/plan")]
    [InlineData("/issues/AER-12")]
    [InlineData("/assets/index-BGJobmXl.js")]
    // Case: a path is not case sensitive to the file system this is served
    // from, and a guard that is would be trivially stepped around.
    [InlineData("/PLAN")]
    public async Task WithholdsTheBoardFromEveryoneElse(string path)
    {
        var (context, served) = await Run(path, isAdmin: false);

        Assert.False(served);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        // A cached refusal outlives the checkbox that fixes it, and "I made her
        // an admin and her phone still says the page isn't there" is not a
        // symptom anyone connects to a cache entry.
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
    }

    /// <summary>
    /// A 404 rather than a 403 on purpose: it is indistinguishable from an
    /// install built without the bundle. The API's refusals go the other way,
    /// and RequireRoleAttribute says why.
    /// </summary>
    [Fact]
    public async Task TheRefusalNeverAdmitsThereIsSomethingThere()
    {
        var (context, _) = await Run("/plan", isAdmin: false);

        Assert.NotEqual(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Empty(context.Response.Headers.Location.ToString());
        Assert.Equal(0, context.Response.ContentLength ?? 0);
    }

    /// <summary>
    /// A person - signed in, but nobody yet, or a device nobody has claimed -
    /// navigating to the board is sent to the sign-in shell, with no ?r=: there
    /// is nowhere for them to return to yet, and the shell can say they are
    /// waiting. Still no-store, for the same reason as the 404.
    /// </summary>
    [Theory]
    [InlineData(RoleDecision.PendingApproval)]
    [InlineData(RoleDecision.NoPerson)]
    public async Task SendsAPersonWhoIsNotInToTheSignInShell_OnANavigation(string reason)
    {
        var gate = new StubRoleGate { Decision = RoleDecision.Refuse(reason) };

        var (context, served) = await Run("/issues/AER-12", gate: gate,
            configure: c => c.Request.Headers["Sec-Fetch-Mode"] = "navigate");

        Assert.False(served);
        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal(new AuthOptions().SignInPath, context.Response.Headers.Location.ToString());
        Assert.DoesNotContain("?r=", context.Response.Headers.Location.ToString());
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
    }

    /// <summary>A fetch cannot follow a 302 usefully; it gets the flat 404 like anyone else.</summary>
    [Fact]
    public async Task ADocumentRedirectIsForNavigationsOnly()
    {
        var gate = new StubRoleGate { Decision = RoleDecision.Refuse(RoleDecision.PendingApproval) };

        var (context, _) = await Run("/assets/index.js", gate: gate,
            configure: c => c.Request.Headers["Sec-Fetch-Mode"] = "cors");

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Empty(context.Response.Headers.Location.ToString());
    }

    /// <summary>An API key has no browser and no sign-in screen: 404, even on something that looks like a navigation.</summary>
    [Fact]
    public async Task AKeyIsRefusedWithTheFlat404_EvenOnANavigation()
    {
        var gate = new StubRoleGate { Decision = RoleDecision.Refuse(RoleDecision.KeyNotAccepted) };

        var (context, served) = await Run("/", gate: gate,
            configure: c => c.Request.Headers["Sec-Fetch-Mode"] = "navigate");

        Assert.False(served);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Empty(context.Response.Headers.Location.ToString());
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/issues/AER-12")]
    [InlineData("/plan")]
    public async Task ServesItToAnAdministrator(string path)
    {
        var (context, served) = await Run(path, isAdmin: true);

        Assert.True(served);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Theory]
    // The API - guarded separately, by an attribute, on the actions that want
    // it - and everything the wall itself already lets through unchallenged.
    [InlineData("/api/zones")]
    [InlineData("/api/hatch/issues")]
    [InlineData("/health/ready")]
    [InlineData("/media/Beatles/Revolver/01.flac")]
    [InlineData("/auth/")]
    [InlineData("/auth")]
    [InlineData("/.well-known/assetlinks.json")]
    // Case, the same rule the exemption list itself matches by.
    [InlineData("/HEALTH/ready")]
    public async Task LeavesEverythingElseAlone(string path)
    {
        var (_, served) = await Run(path, isAdmin: false);

        Assert.True(served);
    }

    /// <summary>
    /// Canonicalised before either check: a dot-segment that collapses onto an
    /// exempt prefix must not read as exempt (or /media/../apps/admin would
    /// have reopened the hole this closes), and the same collapse must not read
    /// an ordinary path as exempt either, which is the case here - Kestrel
    /// hands this middleware an already percent-decoded, not yet
    /// dot-collapsed, Request.Path (a raw `%2e%2e` never reaches here as
    /// such), and /media/../plan collapses to /plan, the board's own.
    /// </summary>
    [Fact]
    public async Task ATraversalOutOfAnExemptPrefixIsGated()
    {
        var gate = new StubRoleGate();

        var (context, served) = await Run("/media/../plan", gate: gate);

        Assert.False(served);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        // Canonicalisation decides only whether to ask at all; the gate itself
        // is still asked about the request exactly as it arrived.
        Assert.Equal(1, gate.Evaluations);
        Assert.Equal("/media/../plan", gate.LastAsked.Path);
    }

    /// <summary>
    /// The rollback, and the whole of local development. Nothing is looked at -
    /// not the path, not the caller - so an install with no wall
    /// pays nothing for this being in the pipeline.
    /// </summary>
    [Fact]
    public async Task NoOpsEntirelyWhileTheWallIsOff()
    {
        var gate = new StubRoleGate { Enabled = false };

        var (_, served) = await Run("/plan", gate: gate);

        Assert.True(served);
        Assert.Equal(0, gate.Evaluations);
    }

    /// <summary>
    /// The gate is asked about the request it is guarding, because the Warning
    /// it logs is what an operator reads when somebody reports the page
    /// missing - and a refusal with the wrong path on it is worse than none.
    /// The original path, unchanged: canonicalisation is only how this
    /// middleware decides whether to ask at all.
    /// </summary>
    [Fact]
    public async Task AsksTheGateWithTheRequestItIsAbout()
    {
        var gate = new StubRoleGate { Decision = RoleDecision.Refuse(RoleDecision.NotAdmin) };

        await Run("/settings", gate: gate);

        // No key: a bundle is for a browser, and an API key has no browser.
        Assert.Equal(("GET", "/settings", "10.0.0.7", PersonRole.User, false), gate.LastAsked);
    }

    private static readonly IAuthGate Wall = new AuthGate(
        new StubAuthService(),
        Options.Create(new AuthOptions()),
        Options.Create(new MediaLibraryOptions { RequestPath = "/media" }),
        NullLogger<AuthGate>.Instance);

    private static async Task<(HttpContext Context, bool Served)> Run(
        string path, bool isAdmin = false, IRoleGate? gate = null, Action<DefaultHttpContext>? configure = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.7");
        context.Request.Method = HttpMethods.Get;
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("home.example.com");
        context.Request.Path = path;
        configure?.Invoke(context);

        gate ??= new StubRoleGate
        {
            Decision = isAdmin
                ? RoleDecision.Allow(new EfPerson
                {
                    Id = Guid.NewGuid(),
                    Name = "Ada",
                    Role = PersonRole.Admin,
                    CreatedAt = DateTimeOffset.UnixEpoch,
                    UpdatedAt = DateTimeOffset.UnixEpoch,
                })
                : RoleDecision.Refuse(RoleDecision.NotAdmin),
        };

        var served = false;
        var middleware = new AdminAppMiddleware(_ =>
        {
            served = true;
            return Task.CompletedTask;
        }, Options.Create(new AuthOptions()));

        await middleware.InvokeAsync(context, gate, Wall);

        return (context, served);
    }
}

/// <summary>
/// Answers however the test says, and records what it was asked - so a test can
/// prove the dormant path never asked at all, which is the property that keeps
/// this middleware free on an install that has not turned enforcement on.
/// </summary>
internal sealed class StubRoleGate : IRoleGate
{
    public bool Enabled { get; set; } = true;

    public RoleDecision Decision { get; set; } = RoleDecision.Refuse(RoleDecision.NotAdmin);

    public int Evaluations { get; private set; }

    public (string? Method, string? Path, string? ClientIp, PersonRole Minimum, bool AcceptKey) LastAsked { get; private set; }

    public Task<RoleDecision> EvaluateAsync(string? method, PathString path, string? clientIp, PersonRole minimum, bool acceptKey, CancellationToken ct)
    {
        Evaluations++;
        LastAsked = (method, path.Value, clientIp, minimum, acceptKey);
        return Task.FromResult(Decision);
    }
}

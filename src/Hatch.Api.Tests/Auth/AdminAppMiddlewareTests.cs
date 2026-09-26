using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Net;

namespace Hatch.Api.Tests.Auth;

/// <summary>
/// Who gets handed an operator app's bundle - admin's, and Hatch's. Three
/// things have to hold, and each is a different kind of bug if it does not: the
/// deep links are covered as well as the assets (or /apps/admin/devices serves
/// index.html to anyone), the refusal is a 404 rather than a 403 (or it
/// advertises what it is withholding), and every other app is untouched (or a
/// family member loses the dashboard to a boundary that was never about them).
/// </summary>
public class AdminAppMiddlewareTests
{
    [Theory]
    // The bundle itself, the deep link a hard refresh produces, and the
    // hashed asset - the first is UseStaticFiles, the second is
    // MapFallbackToFile, and the third would be a hole in either.
    [InlineData("/apps/admin")]
    [InlineData("/apps/admin/")]
    [InlineData("/apps/admin/devices")]
    [InlineData("/apps/admin/assets/index-BGJobmXl.js")]
    // Case, because a path is not case sensitive to the file system this is
    // served from and a guard that is would be trivially stepped around.
    [InlineData("/APPS/Admin/")]
    // Hatch, the second operator app behind the same boundary. Its deep links
    // matter more than most - /apps/hatch/issues/AER-12 is what gets pasted
    // into a chat window - so a gate that covered only the root would be a
    // gate that leaked every ticket in the house.
    [InlineData("/apps/hatch")]
    [InlineData("/apps/hatch/")]
    [InlineData("/apps/hatch/issues/AER-12")]
    [InlineData("/apps/hatch/assets/index-CO5Z7yjM.js")]
    public async Task WithholdsTheBundleFromEveryoneElse(string path)
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
    /// install built without the bundle, which several are - Program.cs mounts
    /// each SPA only if its directory exists. The API's refusals go the other
    /// way, and RequireRoleAttribute says why.
    /// </summary>
    [Fact]
    public async Task TheRefusalNeverAdmitsThereIsSomethingThere()
    {
        var (context, _) = await Run("/apps/admin/", isAdmin: false);

        Assert.NotEqual(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Empty(context.Response.Headers.Location.ToString());
        Assert.Equal(0, context.Response.ContentLength ?? 0);
    }

    /// <summary>
    /// A person - signed in, but nobody yet, or a device nobody has claimed -
    /// navigating to the bundle is sent to the sign-in shell, with no ?r=: there
    /// is nowhere for them to return to yet, and the shell can say they are
    /// waiting. Still no-store, for the same reason as the 404.
    /// </summary>
    [Theory]
    [InlineData(RoleDecision.PendingApproval)]
    [InlineData(RoleDecision.NoPerson)]
    public async Task SendsAPersonWhoIsNotInToTheSignInShell_OnANavigation(string reason)
    {
        var gate = new StubRoleGate { Decision = RoleDecision.Refuse(reason) };

        var (context, served) = await Run("/apps/hatch/issues/AER-12", gate: gate,
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

        var (context, _) = await Run("/apps/hatch/assets/index.js", gate: gate,
            configure: c => c.Request.Headers["Sec-Fetch-Mode"] = "cors");

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Empty(context.Response.Headers.Location.ToString());
    }

    /// <summary>An API key has no browser and no sign-in screen: 404, even on something that looks like a navigation.</summary>
    [Fact]
    public async Task AKeyIsRefusedWithTheFlat404_EvenOnANavigation()
    {
        var gate = new StubRoleGate { Decision = RoleDecision.Refuse(RoleDecision.KeyNotAccepted) };

        var (context, served) = await Run("/apps/hatch/", gate: gate,
            configure: c => c.Request.Headers["Sec-Fetch-Mode"] = "navigate");

        Assert.False(served);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Empty(context.Response.Headers.Location.ToString());
    }

    [Theory]
    [InlineData("/apps/admin/devices")]
    [InlineData("/apps/hatch/issues/AER-12")]
    public async Task ServesItToAnAdministrator(string path)
    {
        var (context, served) = await Run(path, isAdmin: true);

        Assert.True(served);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Theory]
    // Every app that is not this one, plus the API - guarded separately, by an
    // attribute, on the actions that want it.
    [InlineData("/apps/dashboard/")]
    [InlineData("/apps/family/storage")]
    [InlineData("/apps/auth/")]
    [InlineData("/apps/docs/")]
    [InlineData("/api/zones")]
    [InlineData("/media/Beatles/Revolver/01.flac")]
    // Segment matching, the same rule the wall's allow-list uses: a path that
    // merely starts with the same characters is a different app.
    [InlineData("/apps/administration/")]
    [InlineData("/apps/adminfoo")]
    [InlineData("/apps/hatchery/")]
    [InlineData("/apps/hatchfoo")]
    public async Task LeavesEverythingElseAlone(string path)
    {
        var (_, served) = await Run(path, isAdmin: false);

        Assert.True(served);
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

        var (_, served) = await Run("/apps/admin/", gate: gate);

        Assert.True(served);
        Assert.Equal(0, gate.Evaluations);
    }

    /// <summary>
    /// The gate is asked about the request it is guarding, because the Warning
    /// it logs is what an operator reads when somebody reports the page
    /// missing - and a refusal with the wrong path on it is worse than none.
    /// </summary>
    [Fact]
    public async Task AsksTheGateWithTheRequestItIsAbout()
    {
        var gate = new StubRoleGate { Decision = RoleDecision.Refuse(RoleDecision.NotAdmin) };

        await Run("/apps/admin/settings", gate: gate);

        // No scope: a bundle is for a browser, and an API key has no browser.
        Assert.Equal(("GET", "/apps/admin/settings", "10.0.0.7", PersonRole.User, null), gate.LastAsked);
    }

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

        await middleware.InvokeAsync(context, gate);

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

    public (string? Method, string? Path, string? ClientIp, PersonRole Minimum, string? AcceptScope) LastAsked { get; private set; }

    public Task<RoleDecision> EvaluateAsync(string? method, PathString path, string? clientIp, PersonRole minimum, string? acceptScope, CancellationToken ct)
    {
        Evaluations++;
        LastAsked = (method, path.Value, clientIp, minimum, acceptScope);
        return Task.FromResult(Decision);
    }
}

using Hatch.Api.Tests.Hatch;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using QuartzLogProvider = Quartz.Logging.LogProvider;

namespace Hatch.Api.Tests;

/// <summary>
/// HA-2248's criterion: the board answers at the origin's root, every old
/// <c>/apps/hatch/...</c> address redirects there permanently with its query
/// string intact, an unrouted <c>/api/...</c> stays a 404 rather than
/// becoming the board's own HTML, and <c>wwwroot/hatch-runner/*</c> stays
/// unreachable by any route but <c>RunnerController</c>'s own.
///
/// <para>One host for every case here, via <see cref="RootMountFixture"/>
/// (<c>IClassFixture</c>, built once and torn down once) rather than one per
/// case: a temp content root is cheap, but a real host is not, and nothing
/// below mutates what it serves.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class RootMountTests(RootMountFixture fixture) : IClassFixture<RootMountFixture>
{
    [SkippableTheory]
    [InlineData("/")]
    [InlineData("/issues/AER-12")]
    [InlineData("/plan")]
    [InlineData("/docs/getting-started")]
    [InlineData("/runners/x")]
    public async Task EveryBoardRouteServesTheBundlesIndex_NoCache(string path)
    {
        fixture.SkipIfUnavailable();
        var response = await fixture.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("the board's index.html", await response.Content.ReadAsStringAsync());
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
    }

    [SkippableFact]
    public async Task AHashedAssetIsImmutable()
    {
        fixture.SkipIfUnavailable();
        var response = await fixture.Client.GetAsync("/assets/app-ABC123.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("immutable", response.Headers.CacheControl?.ToString());
    }

    [SkippableFact]
    public async Task TheManifestIsTheRightMediaType()
    {
        fixture.SkipIfUnavailable();
        var response = await fixture.Client.GetAsync("/manifest.webmanifest");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/manifest+json", response.Content.Headers.ContentType?.MediaType);
    }

    [SkippableTheory]
    [InlineData("/apps/hatch/issues/AER-12?x=1", "/issues/AER-12?x=1")]
    [InlineData("/apps/hatch", "/")]
    [InlineData("/apps/hatch/", "/")]
    [InlineData("/apps", "/")]
    [InlineData("/apps/", "/")]
    [InlineData("/apps/design/button?x=1", "/design/button?x=1")]
    [InlineData("/apps/design", "/design")]
    [InlineData("/apps/design/", "/design")]
    public async Task AnOldAddressRedirectsPermanently_QueryStringIntact(string from, string to)
    {
        fixture.SkipIfUnavailable();
        var response = await fixture.NoRedirectClient.GetAsync(from);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(to, response.Headers.Location?.OriginalString);
    }

    /// <summary>A `//` in the Location is a protocol-relative URL - the collapsed slashes must not survive into it.</summary>
    [SkippableFact]
    public async Task ATraversalInsideTheOldPrefixNeverEchoesADoubleSlash()
    {
        fixture.SkipIfUnavailable();
        var response = await fixture.NoRedirectClient.GetAsync("/apps/hatch//x.example");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.DoesNotContain("//", response.Headers.Location?.OriginalString);
    }

    /// <summary>The same guarantee, for the gallery's old prefix.</summary>
    [SkippableFact]
    public async Task ATraversalInsideTheOldDesignPrefixNeverEchoesADoubleSlash()
    {
        fixture.SkipIfUnavailable();
        var response = await fixture.NoRedirectClient.GetAsync("/apps/design//x.example");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.DoesNotContain("//", response.Headers.Location?.OriginalString);
    }

    [SkippableFact]
    public async Task TheRunnerBinaryIsUnreachableThroughTheBoardsMount()
    {
        fixture.SkipIfUnavailable();
        var response = await fixture.Client.GetAsync("/hatch-runner/win-x64/hatch.exe");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [SkippableFact]
    public async Task AnUnroutedApiPathIs404_NotTheBoardsHtml()
    {
        fixture.SkipIfUnavailable();
        var response = await fixture.Client.GetAsync("/api/nothing-here");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>A real controller route still wins over both catch-alls, whatever order they were registered in.</summary>
    [SkippableFact]
    public async Task AnExistingApiRouteStillWins()
    {
        fixture.SkipIfUnavailable();
        var response = await fixture.Client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

/// <summary>
/// The scratch pair, the scratch content root and the one real host every
/// case in <see cref="RootMountTests"/> shares - built once, in
/// <see cref="InitializeAsync"/>, and dropped once, in
/// <see cref="DisposeAsync"/>. No npm build needed: a handful of placeholder
/// files under a temp directory are all the mounts in <c>Program.cs</c> ever
/// read from disk.
/// </summary>
public sealed class RootMountFixture : IAsyncLifetime
{
    private string? hatch;
    private string? quartz;
    private string? contentRoot;
    private RootMountFactory? factory;

    /// <summary>Follows redirects, for every assertion that is not about the redirect itself.</summary>
    public HttpClient Client { get; private set; } = null!;

    /// <summary>Stops at the first response, so a redirect's own status and Location are what a case asserts on.</summary>
    public HttpClient NoRedirectClient { get; private set; } = null!;

    public void SkipIfUnavailable() => Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

    public async Task InitializeAsync()
    {
        if (!HatchDatabase.Available) return;

        (hatch, quartz) = await MinimalDatabases.CreateAsync();
        await MinimalDatabases.MigrateAsync(hatch);
        contentRoot = WriteContentRoot();

        // Quartz keeps its logging provider in a static bound to whichever
        // host built a scheduler first - see MinimalStartupTests for why this
        // is cleared on the way in as well as on the way out.
        QuartzLogProvider.SetCurrentLogProvider(null);

        factory = new RootMountFactory(hatch, quartz, contentRoot);
        Client = factory.CreateClient();
        NoRedirectClient = factory.CreateDefaultClient(new NoRedirectHandler());
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        NoRedirectClient?.Dispose();
        if (factory is not null) await factory.DisposeAsync();
        QuartzLogProvider.SetCurrentLogProvider(null);

        if (hatch is not null && quartz is not null) await MinimalDatabases.DropAsync(hatch, quartz);
        if (contentRoot is not null)
        {
            try { Directory.Delete(contentRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    private sealed class NoRedirectHandler() : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false });

    /// <summary>
    /// A fresh scratch content root, named for this run so parallel runs
    /// never collide: a scratch wwwroot/apps/hatch (index.html, a hashed
    /// asset, the manifest) and wwwroot/hatch-runner (the one binary
    /// RunnerController serves).
    /// </summary>
    private static string WriteContentRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"hatch-root-mount-{Guid.NewGuid():N}");
        var board = Path.Combine(root, "wwwroot", "apps", "hatch");
        Directory.CreateDirectory(Path.Combine(board, "assets"));
        Directory.CreateDirectory(Path.Combine(root, "wwwroot", "hatch-runner", "win-x64"));

        File.WriteAllText(Path.Combine(board, "index.html"), "the board's index.html");
        File.WriteAllText(Path.Combine(board, "assets", "app-ABC123.js"), "// hashed");
        File.WriteAllText(
            Path.Combine(board, "manifest.webmanifest"),
            """{"name":"Hatch","id":"/","start_url":"/","scope":"/"}""");
        File.WriteAllText(Path.Combine(root, "wwwroot", "hatch-runner", "win-x64", "hatch.exe"), "not a real binary");

        return root;
    }

    private sealed class RootMountFactory(string hatch, string quartz, string contentRoot) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(contentRoot);

            builder.UseSetting("ConnectionStrings:Hatch", hatch);
            builder.UseSetting("ConnectionStrings:Quartz", quartz);
            builder.UseSetting("Auth:Enabled", "false");
        }
    }
}

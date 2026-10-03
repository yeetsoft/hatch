using Hatch.Api.Tests.Hatch;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;

namespace Hatch.Api.Tests;

/// <summary>
/// HA-269's criterion: the PWA manifest comes back as
/// <c>application/manifest+json</c>, not whatever
/// <see cref="Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider"/>'s
/// defaults would otherwise fall back to for an unrecognised extension.
///
/// <para>Needs the real <c>src/Hatch.Api/wwwroot/apps/hatch</c> on disk - a
/// scratch content root would never have the file to answer with - so it
/// skips out loud rather than failing or silently passing when that build
/// has not run, exactly as it skips out loud when there is no database for
/// the host to start against.</para>
/// </summary>
public class ManifestContentTypeTests
{
    [SkippableFact]
    public async Task GetManifestWebmanifest_ReturnsApplicationManifestJson()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var appsHatch = Path.Combine(RepositoryRoot(), "src", "Hatch.Api", "wwwroot", "apps", "hatch");
        Skip.IfNot(Directory.Exists(appsHatch),
            "apps/hatch has not been built - run npm run build -w apps/hatch (or make test-web) first");

        var (hatch, quartz) = await MinimalDatabases.CreateAsync();

        await using var factory = new ManifestFactory(hatch, quartz);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/apps/hatch/manifest.webmanifest");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/manifest+json", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>Found by walking up for the solution file, same as <c>MinimalStartupTests.RepositoryRoot()</c> - duplicated rather than extracted for a helper this small, used in exactly two places.</summary>
    private static string RepositoryRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "src", "Hatch.slnx")))
                return d.FullName;

        throw new InvalidOperationException("Could not find the repository root (no src/Hatch.slnx above the test binary).");
    }

    /// <summary>
    /// The real <c>src/Hatch.Api</c> content root, unlike
    /// <see cref="MinimalDatabases.ScratchContentRoot"/> - this test is about
    /// whatever is actually built under <c>wwwroot/apps/hatch</c> on this
    /// checkout.
    /// </summary>
    private sealed class ManifestFactory(string hatch, string quartz) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(Path.Combine(RepositoryRoot(), "src", "Hatch.Api"));

            builder.UseSetting("ConnectionStrings:Hatch", hatch);
            builder.UseSetting("ConnectionStrings:Quartz", quartz);
            builder.UseSetting("Auth:Enabled", "false");
        }
    }
}

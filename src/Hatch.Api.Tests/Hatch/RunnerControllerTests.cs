using System.Net;
using System.Security.Cryptography;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What the Runner page is told, and what it is handed.
///
/// Every case turns on what is actually on disk, because that is the one thing
/// this controller decides: an image built without the publish step, a partial
/// build, and a whole one all have to produce a page that is true rather than a
/// list of links that 404.
/// </summary>
[Collection(global::Hatch.Api.Tests.Jobs.QuartzSchedulerCollection.Name)]
public sealed class RunnerControllerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hatch-runner-").FullName;

    private const string Sha = "0123456789abcdef0123456789abcdef01234567";

    /// <summary>Puts a binary where the image would have put it, with bytes worth recognising.</summary>
    private void Publish(string rid, string fileName, string contents = "MZ")
    {
        var directory = Path.Combine(_root, "hatch-runner", rid);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), contents);
    }

    private static string HashOf(string contents) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(contents)));

    private RunnerController Controller() => new(new StubRevision(Sha), new RunnerBuilds(new StubEnvironment(_root)));

    /// <summary>
    /// A `dotnet run` from a checkout, and any image built before the publish
    /// step existed. Nothing is offered, and nothing is claimed.
    /// </summary>
    [Fact]
    public async Task WithNothingPublished_TheListIsEmptyRatherThanFourBrokenLinks()
    {
        var runner = await Controller().Get();

        Assert.Empty(runner.Downloads);
        Assert.Equal(Sha, runner.Revision);
    }

    [Fact]
    public async Task OnlyWhatIsOnDiskIsOffered()
    {
        Publish("win-x64", "hatch.exe");
        Publish("osx-arm64", "hatch");

        var offered = (await Controller().Get()).Downloads;

        Assert.Equal(["win-x64", "osx-arm64"], offered.Select(d => d.Rid));
        Assert.Equal("hatch.exe", offered[0].FileName);
        Assert.Equal("/api/runner/download/win-x64", offered[0].Url);
    }

    /// <summary>
    /// The whole build. Order is the page's order, and every URL is one the
    /// server named rather than one the page assembled.
    /// </summary>
    [Fact]
    public async Task AWholeBuildOffersFourPlatformsInOrder()
    {
        foreach (var rid in new[] { "win-x64", "osx-arm64", "osx-x64", "linux-x64" })
            Publish(rid, rid == "win-x64" ? "hatch.exe" : "hatch");

        var offered = (await Controller().Get()).Downloads;

        Assert.Equal(["win-x64", "osx-arm64", "osx-x64", "linux-x64"], offered.Select(d => d.Rid));
        Assert.All(offered, d => Assert.Equal($"/api/runner/download/{d.Rid}", d.Url));
        Assert.All(offered, d => Assert.False(string.IsNullOrWhiteSpace(d.Platform)));
    }

    /// <summary>
    /// The revision is the image's own, carried on this answer rather than
    /// fetched separately - a page that asked twice could draw a download list
    /// from one build beside a revision from another.
    /// </summary>
    [Fact]
    public async Task TheRevisionIsTheOneTheseBinariesWereBuiltFrom()
    {
        Publish("linux-x64", "hatch");

        Assert.Equal(Sha, (await Controller().Get()).Revision);
    }

    [Fact]
    public async Task ADownloadIsTheBytesAndTheNameItLandsOnDiskAs()
    {
        Publish("osx-arm64", "hatch", "#!/not-really");

        var result = Assert.IsType<PhysicalFileResult>(await Controller().Download("osx-arm64"));

        Assert.Equal("#!/not-really", File.ReadAllText(result.FileName));
        Assert.Equal("hatch", result.FileDownloadName);
        Assert.Equal("application/octet-stream", result.ContentType);
    }

    [Fact]
    public async Task WindowsGetsTheNameWindowsNeeds()
    {
        Publish("win-x64", "hatch.exe");

        var result = Assert.IsType<PhysicalFileResult>(await Controller().Download("win-x64"));

        Assert.Equal("hatch.exe", result.FileDownloadName);
    }

    [Fact]
    public async Task APlatformThisBuildDidNotPublishIsNotFound()
    {
        Publish("win-x64", "hatch.exe");

        Assert.IsType<NotFoundResult>(await Controller().Download("linux-x64"));
    }

    /// <summary>
    /// The guard is the lookup against the fixed list, not a sanitiser: an
    /// argument that is not one of the four RIDs never reaches a file open at
    /// all, so there is no path to traverse out of.
    /// </summary>
    [Theory]
    [InlineData("nonsense")]
    [InlineData("../../../etc/passwd")]
    [InlineData("..")]
    [InlineData("win-x64/../../out/Hatch.Api.dll")]
    [InlineData("")]
    public async Task AnUnrecognisedRidIsNotFound(string rid)
    {
        foreach (var known in new[] { "win-x64", "osx-arm64", "osx-x64", "linux-x64" })
            Publish(known, known == "win-x64" ? "hatch.exe" : "hatch");

        Assert.IsType<NotFoundResult>(await Controller().Download(rid));
    }

    // ---- The sha256 ----

    /// <summary>Each offered build's sha256 is the hex of the hash of the exact bytes its own download serves.</summary>
    [Fact]
    public async Task EachBuildsSha256_IsTheHashOfTheBytesItsDownloadServes()
    {
        Publish("win-x64", "hatch.exe", "windows bytes");
        Publish("osx-arm64", "hatch", "arm bytes");
        Publish("osx-x64", "hatch", "intel bytes");
        Publish("linux-x64", "hatch", "linux bytes");

        var controller = Controller();
        var offered = (await controller.Get()).Downloads;

        foreach (var download in offered)
        {
            var served = Assert.IsType<PhysicalFileResult>(await controller.Download(download.Rid));
            var expected = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(served.FileName)));

            Assert.Equal(expected, download.Sha256);
        }
    }

    [Fact]
    public async Task TheSha256IsSixtyFourLowercaseHexCharacters()
    {
        Publish("win-x64", "hatch.exe");

        var sha256 = (await Controller().Get()).Downloads[0].Sha256;

        Assert.Equal(64, sha256.Length);
        Assert.Matches("^[0-9a-f]{64}$", sha256);
    }

    [Fact]
    public async Task WithNothingPublished_NoSha256Appears()
    {
        var offered = (await Controller().Get()).Downloads;

        Assert.Empty(offered);
    }

    /// <summary>
    /// Hashed once, not per request: a file replaced with other bytes of the
    /// same length and the same mtime still answers the first hash, because
    /// nothing told the cache the file had changed.
    /// </summary>
    [Fact]
    public async Task ABuildIsHashedOnceAndTheAnswerIsKept()
    {
        Publish("win-x64", "hatch.exe", "AAAA");
        var path = Path.Combine(_root, "hatch-runner", "win-x64", "hatch.exe");
        var mtime = File.GetLastWriteTimeUtc(path);

        var builds = new RunnerBuilds(new StubEnvironment(_root));
        var first = (await builds.FindAsync("win-x64"))!.Sha256;

        File.WriteAllText(path, "BBBB");
        File.SetLastWriteTimeUtc(path, mtime);
        var second = (await builds.FindAsync("win-x64"))!.Sha256;

        Assert.Equal(first, second);
        Assert.Equal(HashOf("AAAA"), first);
    }

    /// <summary>
    /// A file replaced under a running process is still rehashed correctly
    /// once its length - part of the cache key alongside the path and the
    /// mtime - changes too, so a redeploy is never described by a stale hash.
    /// </summary>
    [Fact]
    public async Task ABuildWhoseLengthChanges_IsRehashed()
    {
        Publish("win-x64", "hatch.exe", "AAAA");
        var path = Path.Combine(_root, "hatch-runner", "win-x64", "hatch.exe");

        var builds = new RunnerBuilds(new StubEnvironment(_root));
        var first = (await builds.FindAsync("win-x64"))!.Sha256;

        File.WriteAllText(path, "A longer replacement body");
        var second = (await builds.FindAsync("win-x64"))!.Sha256;

        Assert.NotEqual(first, second);
        Assert.Equal(HashOf("A longer replacement body"), second);
    }

    /// <summary>Two callers racing the same miss share one hash rather than each computing their own.</summary>
    [Fact]
    public async Task ConcurrentGets_AgreeOnTheSameHash()
    {
        Publish("win-x64", "hatch.exe", "concurrent bytes");
        var builds = new RunnerBuilds(new StubEnvironment(_root));

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => builds.FindAsync("win-x64")));

        Assert.All(results, r => Assert.Equal(results[0]!.Sha256, r!.Sha256));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temporary directory that will not go is not a failing test.
        }
    }

    private sealed class StubRevision(string revision) : IHatchRevision
    {
        public string Revision { get; } = revision;
        public int Sequence => 1;
        public DateTimeOffset? BuiltAt => null;
        public bool IsDevelopment => Revision == global::Hatch.Api.Services.HatchRevision.Development;
    }

    /// <summary>A web root that is a temp directory, which is the only thing this controller reads out of it.</summary>
    private sealed class StubEnvironment(string webRoot) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = webRoot;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Hatch.Api.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = webRoot;
        public string EnvironmentName { get; set; } = "Testing";
    }

    // ---- The key lane ----

    /// <summary>
    /// A runner's own key reaches both routes, the same as a person at a
    /// browser; an unknown or a revoked key is refused the way every
    /// key-accepting route refuses one - see ApiKeyTests and RoleGateTests for
    /// that refusal itself, which this controller does not own. Needs
    /// Postgres, and is skipped out loud without it. Run it with
    /// `make test-api-db`.
    /// </summary>
    [SkippableFact(Timeout = 120_000)]
    public async Task TheKeyLane_AcceptsALiveKeyAndRefusesAnUnknownOrARevokedOne()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset. Run `make test-api-db`.");

        var logs = new CapturedLogs();
        var deadline = new Deadline(TimeSpan.FromSeconds(90), () => string.Join(Environment.NewLine, logs.Messages));

        await using var host = await WalledHost.StartAsync(deadline, logs);

        var live = await host.MintKeyAsync("runner-key-live", owner: null);
        var revoked = await host.MintKeyAsync("runner-key-revoked", owner: null);
        await host.RevokeAsync(revoked);

        using var liveClient = await host.AsKeyAsync(live);
        using var revokedClient = await host.AsKeyAsync(revoked);
        using var unknownClient = await host.AsBearerAsync("hatch_ak_nobody_minted_this_one_0000000");

        using var liveResponse = await deadline.BoundAsync("a live key", () => liveClient.GetAsync("/api/runner"));
        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);

        using var revokedResponse = await deadline.BoundAsync("a revoked key", () => revokedClient.GetAsync("/api/runner"));
        Assert.Equal(HttpStatusCode.Unauthorized, revokedResponse.StatusCode);

        using var unknownResponse = await deadline.BoundAsync("an unknown key", () => unknownClient.GetAsync("/api/runner"));
        Assert.Equal(HttpStatusCode.Unauthorized, unknownResponse.StatusCode);
    }
}

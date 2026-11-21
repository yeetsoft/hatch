using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Hatch.Api.Services;

/// <summary>One platform's binary, found on disk, with the sha256 of the exact bytes it will be served as.</summary>
public sealed record RunnerBuild(string Rid, string Platform, string FileName, string Path, string Sha256)
{
    /// <summary>The route that serves this build - what the Runner page links to and a heartbeat reply names.</summary>
    public string Url => $"/api/runner/download/{Rid}";
}

/// <summary>
/// The one place that knows which builds are on disk, where, and their sha256
/// - so a controller, and anything else that needs the same answer, reads it
/// from here rather than walking wwwroot/hatch-runner on its own.
/// </summary>
public interface IRunnerBuilds
{
    /// <summary>Every platform actually on disk, in platform order. Empty on an image that published none.</summary>
    Task<IReadOnlyList<RunnerBuild>> ListAsync();

    /// <summary>The one platform named, or null for a RID the table does not have or whose file is not on disk.</summary>
    Task<RunnerBuild?> FindAsync(string rid);
}

/// <summary>
/// Reads the four platform binaries Dockerfile.api places beside wwwroot/apps,
/// hashing each once and keeping the answer.
///
/// The hash is cached by path, length and last-write time: a file replaced
/// under a running process - a redeploy without a restart - changes that key,
/// so the next ask rehashes it rather than handing back a stale answer. A
/// <see cref="Lazy{T}"/> per key means two callers racing the same miss share
/// one hash rather than each computing it.
/// </summary>
public sealed class RunnerBuilds(IWebHostEnvironment env) : IRunnerBuilds
{
    /// <summary>
    /// The platforms the image publishes, in the order the page lists them.
    /// The same RIDs as the Makefile's <c>HATCH_RIDS</c> and Dockerfile.api's
    /// loop; a RID added to those without being added here is simply not
    /// offered, which is a missing download rather than a broken page.
    /// </summary>
    private static readonly (string Rid, string Platform, string FileName)[] Platforms =
    [
        ("win-x64", "Windows", "hatch.exe"),
        ("osx-arm64", "macOS (Apple Silicon)", "hatch"),
        ("osx-x64", "macOS (Intel)", "hatch"),
        ("linux-x64", "Linux", "hatch"),
    ];

    /// <summary>Where the image put them. Not under wwwroot/apps, so no static handler serves them around the controller.</summary>
    private const string RunnerDirectory = "hatch-runner";

    private readonly ConcurrentDictionary<(string Path, long Length, DateTime LastWriteUtc), Lazy<Task<string>>> hashes = new();

    public async Task<IReadOnlyList<RunnerBuild>> ListAsync()
    {
        var builds = new List<RunnerBuild>();
        foreach (var platform in Platforms)
        {
            var build = await FindAsync(platform.Rid);
            if (build is not null) builds.Add(build);
        }

        return builds;
    }

    public async Task<RunnerBuild?> FindAsync(string rid)
    {
        var platform = Platforms.FirstOrDefault(p => p.Rid == rid);
        if (platform.Rid is null) return null;

        var path = PathFor(platform);
        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists) return null;
        }
        catch (IOException)
        {
            return null;
        }

        var key = (path, info.Length, info.LastWriteTimeUtc);
        try
        {
            var sha256 = await HashAsync(key);
            return new RunnerBuild(platform.Rid, platform.Platform, platform.FileName, path, sha256);
        }
        catch (FileNotFoundException)
        {
            // Vanished between the check above and the open below - the same
            // answer as a RID that was never published, not an error about a
            // race nobody caused. Evicted so the miss is not what the next ask
            // remembers.
            hashes.TryRemove(key, out _);
            return null;
        }
    }

    private Task<string> HashAsync((string Path, long Length, DateTime LastWriteUtc) key) =>
        hashes.GetOrAdd(key, k => new Lazy<Task<string>>(() => ComputeAsync(k.Path))).Value;

    private static async Task<string> ComputeAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexStringLower(hash);
    }

    private string PathFor((string Rid, string Platform, string FileName) platform) =>
        Path.Combine(env.WebRootPath, RunnerDirectory, platform.Rid, platform.FileName);
}

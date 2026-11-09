using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Hatch.Cli;

/// <summary>What one probe saw. <c>Completed</c> is false when it timed out or would not start.</summary>
public sealed record ProbeResult(bool Completed, int ExitCode, string Output);

/// <summary>Runs <paramref name="file"/> to its end, or to <paramref name="timeout"/>, whichever is first.</summary>
public delegate Task<ProbeResult> Probe(string file, string[] args, TimeSpan timeout, CancellationToken ct);

/// <summary>
/// One tool, asked about: present with a version, present with none, or absent
/// with one sentence of why. <c>Id</c> is a <see cref="Dependencies"/> id.
/// </summary>
public sealed record DetectorResult(string Id, bool Present, string? Version, string? Why);

/// <summary>
/// Whether <c>make</c>, <c>dotnet</c>, <c>node</c> and <c>pwsh</c> are on
/// <c>PATH</c>, and which version, within a probe that cannot outlast
/// <see cref="ProbeTimeout"/>; whether a Playwright browsers cache exists; and
/// whether Postgres is reachable, as a binary or as a Docker daemon.
/// </summary>
/// <remarks>
/// A detector writes nothing to stderr; the caller does, with the sentence it
/// is given. Every version a parser answers is at most three dotted integers of
/// at most nine digits, so it passes <see cref="DependencyVersion.Problem"/>.
/// </remarks>
public static class Detect
{
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    private const int OutputLimit = 64 * 1024;
    private const int MaxDigits = 9;

    private static readonly Regex LeadingVersion = new(@"[0-9]+(\.[0-9]+){0,2}", RegexOptions.CultureInvariant);

    public static Task<DetectorResult> Make(string? path, Probe probe, CancellationToken ct) =>
        Walk("make", ["--version"], path, probe, ParseMake, ct);

    public static Task<DetectorResult> Dotnet(string? path, Probe probe, CancellationToken ct) =>
        Walk("dotnet", ["--list-sdks"], path, probe, ParseDotnet, ct);

    public static Task<DetectorResult> Node(string? path, Probe probe, CancellationToken ct) =>
        Walk("node", ["--version"], path, probe, ParseNode, ct);

    public static Task<DetectorResult> Pwsh(string? path, Probe probe, CancellationToken ct) =>
        Walk("pwsh", ["--version"], path, probe, ParsePwsh, ct);

    /// <summary>
    /// Whether the Playwright browsers cache holds anything. Reads only
    /// <paramref name="env"/>, so the caller passes the host's variables and a
    /// test passes its own; <paramref name="os"/> is <c>windows</c>,
    /// <c>macos</c> or <c>linux</c>.
    /// </summary>
    public static DetectorResult PlaywrightBrowsers(string os, Func<string, string?> env)
    {
        const string id = "playwright-browsers";
        if (os is not ("windows" or "macos" or "linux"))
            throw new ArgumentOutOfRangeException(nameof(os), os, "expected windows, macos or linux");

        var overridden = env("PLAYWRIGHT_BROWSERS_PATH");
        string dir;
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            if (overridden.Trim() == "0")
                return new DetectorResult(id, false, null,
                    "`PLAYWRIGHT_BROWSERS_PATH=0` keeps browsers inside `node_modules`, where detection cannot look; declare them with `HATCH_HAS=playwright-browsers`");
            dir = overridden;
        }
        else
        {
            string? root;
            string name;
            switch (os)
            {
                case "windows":
                    name = "LOCALAPPDATA";
                    root = env(name);
                    dir = Path.Combine(root ?? "", "ms-playwright");
                    break;
                case "macos":
                    name = "HOME";
                    root = env(name);
                    dir = Path.Combine(root ?? "", "Library", "Caches", "ms-playwright");
                    break;
                default:
                    var xdg = env("XDG_CACHE_HOME");
                    if (!string.IsNullOrWhiteSpace(xdg))
                    {
                        name = "XDG_CACHE_HOME";
                        root = xdg;
                        dir = Path.Combine(xdg, "ms-playwright");
                    }
                    else
                    {
                        name = "HOME";
                        root = env(name);
                        dir = Path.Combine(root ?? "", ".cache", "ms-playwright");
                    }
                    break;
            }

            if (string.IsNullOrWhiteSpace(root))
                return new DetectorResult(id, false, null, $"`{name}` is not set, so there is no Playwright cache to look in");
        }

        return HoldsAnything(dir)
            ? new DetectorResult(id, true, null, null)
            : new DetectorResult(id, false, null, $"Playwright cache `{dir}` is missing or empty");
    }

    /// <summary>
    /// A Postgres server tool on <c>PATH</c> that answers <c>--version</c>, else
    /// a Docker daemon that answers <c>info</c>. A tool that hangs or fails
    /// leaves the next to be asked, so this can wait up to four probe timeouts.
    /// </summary>
    public static async Task<DetectorResult> Postgres(string? path, Probe probe, CancellationToken ct)
    {
        const string id = "postgres";

        foreach (var name in new[] { "postgres", "pg_ctl", "pg_isready" })
        {
            if (Reach.Find(name, path) is not { } file) continue;

            var probed = await probe(file, ["--version"], ProbeTimeout, ct);
            if (probed.Completed && probed.ExitCode == 0)
                return new DetectorResult(id, true, ParsePostgres(probed.Output), null);
        }

        if (Reach.Find("docker", path) is { } docker)
        {
            var probed = await probe(docker, ["info"], ProbeTimeout, ct);
            if (probed.Completed && probed.ExitCode == 0)
                return new DetectorResult(id, true, null, null);
        }

        return new DetectorResult(id, false, null,
            "no `postgres`, `pg_ctl` or `pg_isready` on PATH, and no Docker daemon answered `docker info`");
    }

    /// <summary><c>postgres (PostgreSQL) 17.2 (Debian 17.2-1)</c> is <c>17.2</c>; output without the marker is null.</summary>
    public static string? ParsePostgres(string? output)
    {
        const string marker = "(PostgreSQL) ";

        var at = (output ?? "").IndexOf(marker, StringComparison.Ordinal);
        return at < 0 ? null : Leading(output![(at + marker.Length)..]);
    }

    /// <summary><c>GNU Make 4.3</c> is <c>4.3</c>: the first non-blank line, which is all the copyright lines after it are not.</summary>
    public static string? ParseMake(string? output) => Leading(FirstLine(output));

    /// <summary>The highest SDK in <c>--list-sdks</c>, compared by component: <c>10.0.100</c> beats <c>9.0.304</c>.</summary>
    public static string? ParseDotnet(string? output)
    {
        string? best = null;
        int[]? bestParts = null;

        foreach (var line in (output ?? "").Split('\n'))
        {
            var text = line.Trim();
            if (text.Length == 0 || text[0] is < '0' or > '9') continue;

            var space = text.IndexOf(' ');
            var version = Leading(space < 0 ? text : text[..space]);
            if (version is null) continue;

            var parts = version.Split('.').Select(int.Parse).Concat(Enumerable.Repeat(0, 3)).Take(3).ToArray();
            if (bestParts is null || Compare(parts, bestParts) > 0)
            {
                best = version;
                bestParts = parts;
            }
        }

        return best;
    }

    /// <summary><c>v22.11.0</c> is <c>22.11.0</c>.</summary>
    public static string? ParseNode(string? output) => Leading(FirstLine(output));

    /// <summary><c>PowerShell 7.5.0</c> and <c>7.5.0-preview.2</c> are <c>7.5.0</c>.</summary>
    public static string? ParsePwsh(string? output) => Leading(FirstLine(output));

    /// <summary>
    /// Runs the tool as a child with stdin closed and no window, and answers what
    /// it printed - stdout, then stderr, the first 64 KB of the two. A tool that
    /// will not start, or has not finished by <paramref name="timeout"/>, answers
    /// <c>Completed=false</c> with its whole process tree killed.
    /// </summary>
    public static async Task<ProbeResult> RealProbe(string file, string[] args, TimeSpan timeout, CancellationToken ct)
    {
        var start = new ProcessStartInfo
        {
            FileName = file,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException($"could not start {file}");
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            return new ProbeResult(false, -1, "");
        }

        using (process)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            try
            {
                process.StandardInput.Close();

                // Both at once: a tool with more to say on stderr than a pipe
                // holds would otherwise wait for a reader busy on stdout.
                var stdout = Drain(process.StandardOutput);
                var stderr = Drain(process.StandardError);

                await process.WaitForExitAsync(cts.Token);
                var output = await Task.WhenAll(stdout, stderr).WaitAsync(cts.Token);

                var text = output[0] + output[1];
                return new ProbeResult(true, process.ExitCode, text.Length > OutputLimit ? text[..OutputLimit] : text);
            }
            catch (OperationCanceledException)
            {
                // The readers are not awaited: a grandchild that outlived the
                // kill can hold a pipe open, and the probe must not wait on it.
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                if (ct.IsCancellationRequested) throw;

                return new ProbeResult(false, -1, "");
            }
        }
    }

    /// <summary>
    /// Keeps the first 64 KB of <paramref name="reader"/> and discards the rest,
    /// still reading to the end so the child never blocks on a full pipe.
    /// </summary>
    private static async Task<string> Drain(StreamReader reader)
    {
        var kept = new StringBuilder();
        var buffer = new char[4096];

        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer)) > 0)
            {
                if (kept.Length < OutputLimit) kept.Append(buffer, 0, Math.Min(read, OutputLimit - kept.Length));
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The process was killed under the read; what came before it stands.
        }

        return kept.ToString();
    }

    private static async Task<DetectorResult> Walk(
        string name, string[] args, string? path, Probe probe, Func<string?, string?> parse, CancellationToken ct)
    {
        var command = $"`{name} {string.Join(' ', args)}`";

        if (Reach.Find(name, path) is not { } file)
            return new DetectorResult(name, false, null, $"`{name}` is not on PATH");

        var probed = await probe(file, args, ProbeTimeout, ct);
        if (!probed.Completed)
            return new DetectorResult(name, false, null, $"{command} did not answer within {ProbeTimeout.TotalSeconds:0} seconds");
        if (probed.ExitCode != 0)
            return new DetectorResult(name, false, null, $"{command} exited with code {probed.ExitCode}");

        return new DetectorResult(name, true, parse(probed.Output), null);
    }

    private static bool HoldsAnything(string dir)
    {
        try
        {
            return Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? FirstLine(string? output) =>
        (output ?? "").Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0);

    /// <summary>The first one to three dotted integers in <paramref name="text"/>, each clipped to nine digits.</summary>
    private static string? Leading(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var match = LeadingVersion.Match(text);
        if (!match.Success) return null;

        return string.Join('.', match.Value.Split('.').Select(part => part.Length > MaxDigits ? part[..MaxDigits] : part));
    }

    private static int Compare(int[] left, int[] right)
    {
        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i]) return left[i].CompareTo(right[i]);
        }

        return 0;
    }
}

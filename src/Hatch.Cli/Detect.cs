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
/// <c>Via</c> is null when no login shell was offered, <c>PATH</c> when one
/// was offered but did not answer, or the shell's own invocation (e.g.
/// <c>zsh -l -i +m</c>) when it did.
/// </summary>
public sealed record DetectorResult(string Id, bool Present, string? Version, string? Why, string? Via = null);

/// <summary>
/// What one run of every detector saw, held so a caller can merge it against
/// what a machine declares (<see cref="Settings.Has"/>) without detecting twice.
/// </summary>
public sealed record Detection(IReadOnlyList<DetectorResult> Results)
{
    /// <summary>
    /// What this runner advertises: every present result as <c>id</c> or
    /// <c>id@version</c>, plus every <paramref name="declared"/> entry
    /// detection did not already cover - dropped only where a present result
    /// shares its id and <see cref="DependencyVersion.Matches"/> the
    /// declared version against the detected one, a bare declared id included.
    /// Never removes a detected entry. Ordered ordinal by id, detected before
    /// declared within one id - stable, since <see cref="Enumerable.OrderBy"/>
    /// preserves that - then run through <see cref="DependencySet.Normalise"/>.
    /// </summary>
    public IReadOnlyList<string> Merge(IReadOnlyList<string> declared)
    {
        var detected = Results.Where(result => result.Present).Select(AsEntry);
        var kept = declared.Where(entry => !CoveredByDetection(entry));
        var ordered = detected.Concat(kept).OrderBy(Id, StringComparer.Ordinal);

        DependencySet.Normalise(ordered.ToArray(), out var merged);
        return merged;
    }

    /// <summary>The ids of every absent result <paramref name="declared"/> does not already cover.</summary>
    public IReadOnlyList<string> Missing(IReadOnlyList<string> declared)
    {
        var declaredIds = declared.Select(Id).ToHashSet(StringComparer.Ordinal);
        return Results.Where(result => !result.Present && !declaredIds.Contains(result.Id))
            .Select(result => result.Id).ToArray();
    }

    /// <summary>
    /// One sentence for every present result read from <c>PATH</c> after a
    /// shell was offered and did not answer, in <see cref="Results"/> order.
    /// An absent result carries the same <c>PATH</c> but is already in
    /// <see cref="Missing"/>, so it gets none here.
    /// </summary>
    public IReadOnlyList<string> FallbackNotes() =>
        Results.Where(result => result is { Present: true, Via: "PATH" })
            .Select(result => $"{result.Id} read from PATH, not the shell - {result.Why}").ToArray();

    private bool CoveredByDetection(string declaredEntry)
    {
        var (id, version) = Split(declaredEntry);
        var detected = Results.FirstOrDefault(result => result.Present && result.Id == id);
        return detected is not null && DependencyVersion.Matches(version, detected.Version);
    }

    private static string AsEntry(DetectorResult result) => result.Version is null ? result.Id : $"{result.Id}@{result.Version}";

    private static string Id(string entry) => Split(entry).Id;

    private static (string Id, string? Version) Split(string entry)
    {
        var at = entry.IndexOf('@');
        return at < 0 ? (entry, null) : (entry[..at], entry[(at + 1)..]);
    }
}

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

    /// <summary>How long a login shell gets to answer, longer than <see cref="ProbeTimeout"/> because a profile with a version manager is slower than a bare <c>--version</c>.</summary>
    public static readonly TimeSpan ShellTimeout = TimeSpan.FromSeconds(10);

    private const string Marker = "hatch-probe";

    private const int OutputLimit = 64 * 1024;
    private const int MaxDigits = 9;

    private static readonly Regex LeadingVersion = new(@"[0-9]+(\.[0-9]+){0,2}", RegexOptions.CultureInvariant);

    /// <summary>
    /// Every detector, once, concurrently: <see cref="Make"/>, <see cref="Dotnet"/>,
    /// <see cref="Node"/>, <see cref="Pwsh"/> and <see cref="Postgres"/> alongside
    /// <see cref="PlaywrightBrowsers"/>, which needs no probe. <paramref name="os"/>
    /// is <see cref="CurrentOs"/>'s own answer. <see cref="Make"/>, <see cref="Dotnet"/>,
    /// <see cref="Node"/> and <see cref="Pwsh"/> ask <paramref name="env"/>'s login
    /// shell (<see cref="ShellFor"/>) before the direct PATH probe, so one of them
    /// can now take <see cref="ShellTimeout"/> plus <see cref="ProbeTimeout"/> (15
    /// seconds) rather than <see cref="ProbeTimeout"/> alone; <see cref="Postgres"/>
    /// can take up to four <see cref="ProbeTimeout"/>s, since it tries three
    /// binaries and a Docker daemon in turn before giving up. A probe that throws
    /// is not caught here - the five detectors above already convert a failure
    /// into <c>Present=false</c> rather than throwing, so only
    /// <see cref="OperationCanceledException"/> from <paramref name="ct"/> is ever
    /// seen to propagate.
    /// </summary>
    public static async Task<Detection> RunAsync(string? path, string os, Func<string, string?> env, Probe probe, CancellationToken ct)
    {
        var shell = ShellFor(os, env);

        var make = Make(path, probe, ct, shell);
        var dotnet = Dotnet(path, probe, ct, shell);
        var node = Node(path, probe, ct, shell);
        var pwsh = Pwsh(path, probe, ct, shell);
        var postgres = Postgres(path, probe, ct);
        var playwright = Task.FromResult(PlaywrightBrowsers(os, env));

        var results = await Task.WhenAll(make, dotnet, node, pwsh, postgres, playwright);

        if (shell is not null && !Console.IsInputRedirected && !OperatingSystem.IsWindows())
            Tty.TryReclaimForeground();

        return new Detection(results.OrderBy(result => result.Id, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// <paramref name="env"/>'s <c>SHELL</c>, when it names a file that exists, to
    /// ask first for a tool the direct PATH probe cannot see - a version manager's
    /// shim, installed only by a profile a non-interactive child never reads.
    /// Null on <c>windows</c>, since Git Bash and PowerShell could not be verified
    /// on a real host; null too with <c>SHELL</c> unset, blank, or naming a file
    /// that is not there.
    /// </summary>
    public static string? ShellFor(string os, Func<string, string?> env)
    {
        if (os == "windows") return null;

        var shell = env("SHELL")?.Trim();
        return string.IsNullOrEmpty(shell) || !File.Exists(shell) ? null : shell;
    }

    /// <summary><c>windows</c>, <c>macos</c> or <c>linux</c> - what <see cref="PlaywrightBrowsers"/> and <see cref="RunAsync"/> take as <c>os</c>.</summary>
    public static string CurrentOs() =>
        OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";

    /// <summary>
    /// <paramref name="env"/>'s keys compared case-insensitively on Windows, where
    /// the real PATH key is <c>Path</c>, and ordinally everywhere else - both
    /// <c>Program.cs</c>'s and <see cref="ConfigCommand.Environment"/>'s
    /// dictionaries are case-sensitive, so a straight lookup would miss it.
    /// </summary>
    public static Func<string, string?> Lookup(IDictionary<string, string?> env)
    {
        var map = new Dictionary<string, string?>(
            env, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        return name => map.GetValueOrDefault(name);
    }

    /// <summary>
    /// What <c>.hatch/dependencies.json</c> at <paramref name="path"/> declares, as
    /// <c>id</c> / <c>id@version</c> entries - empty with no file. A parse problem
    /// or an unreadable file answers empty with <paramref name="problem"/> naming
    /// <paramref name="path"/>; nothing calls this yet (HA-1882).
    /// </summary>
    public static IReadOnlyList<string> ReadDeclared(string path, out string? problem)
    {
        problem = null;
        if (!File.Exists(path)) return [];

        byte[] content;
        try
        {
            content = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problem = $"{path}: could not be read";
            return [];
        }

        var (entries, parseProblem) = DependencyDeclaration.Parse(content);
        if (parseProblem is not null)
        {
            problem = $"{path}: {parseProblem}";
            return [];
        }

        return entries!.Select(entry => entry.Version is null ? entry.Id : $"{entry.Id}@{entry.Version}").ToArray();
    }

    public static Task<DetectorResult> Make(string? path, Probe probe, CancellationToken ct, string? shell = null) =>
        Walk("make", ["--version"], path, probe, ParseMake, ct, shell);

    public static Task<DetectorResult> Dotnet(string? path, Probe probe, CancellationToken ct, string? shell = null) =>
        Walk("dotnet", ["--list-sdks"], path, probe, ParseDotnet, ct, shell);

    public static Task<DetectorResult> Node(string? path, Probe probe, CancellationToken ct, string? shell = null) =>
        Walk("node", ["--version"], path, probe, ParseNode, ct, shell);

    public static Task<DetectorResult> Pwsh(string? path, Probe probe, CancellationToken ct, string? shell = null) =>
        Walk("pwsh", ["--version"], path, probe, ParsePwsh, ct, shell);

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

    /// <summary>
    /// <paramref name="shell"/> first, when not null - the incident class is a
    /// tool a version manager's shim puts only where a login shell's profile
    /// looks, not where the loop's own PATH probe can see it - then
    /// <see cref="WalkPath"/> when the shell cannot answer. With <paramref
    /// name="shell"/> null this is exactly <see cref="WalkPath"/>.
    /// </summary>
    private static async Task<DetectorResult> Walk(
        string name, string[] args, string? path, Probe probe, Func<string?, string?> parse, CancellationToken ct, string? shell)
    {
        if (shell is null) return await WalkPath(name, args, path, probe, parse, ct);

        var (answer, failure) = await TryShell(name, args, shell, probe, parse, ct);
        if (answer is not null) return answer;

        var result = await WalkPath(name, args, path, probe, parse, ct);
        return result.Present
            ? result with { Via = "PATH", Why = $"`{Path.GetFileName(shell)} -l -i +m` {failure}" }
            : result with { Via = "PATH" };
    }

    /// <summary>
    /// Asks <paramref name="shell"/> for <paramref name="name"/> with <c>-l -i +m</c>,
    /// a marker printed first so a banner before it and stderr noise after it
    /// (both `RealProbe` keeps) do not reach <paramref name="parse"/>. <c>+m</c>
    /// turns job control off, overriding an interactive shell's default of
    /// turning it on and claiming the terminal. Answers the result when the
    /// shell found the tool, else null with one sentence of why, for
    /// <see cref="Walk"/> to fold into its own PATH-probe <c>Why</c>.
    /// </summary>
    private static async Task<(DetectorResult? Answer, string? Failure)> TryShell(
        string name, string[] args, string shell, Probe probe, Func<string?, string?> parse, CancellationToken ct)
    {
        var script = $"printf '{Marker}\\n'; {name} {string.Join(' ', args)}";
        var probed = await probe(shell, ["-l", "-i", "+m", "-c", script], ShellTimeout, ct);

        if (!probed.Completed)
            return (null, $"did not answer within {ShellTimeout.TotalSeconds:0} seconds");
        if (probed.ExitCode != 0)
            return (null, $"exited with code {probed.ExitCode}");

        if (TextAfterMarker(probed.Output) is not { } text)
            return (null, "printed no marker");

        return (new DetectorResult(name, true, parse(text), null, $"{Path.GetFileName(shell)} -l -i +m"), null);
    }

    /// <summary>The text after the last line that, trimmed, is exactly <see cref="Marker"/> - null with no such line.</summary>
    private static string? TextAfterMarker(string? output)
    {
        var lines = (output ?? "").Split('\n');
        var last = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim() == Marker) last = i;
        }

        return last < 0 ? null : string.Join('\n', lines[(last + 1)..]);
    }

    private static async Task<DetectorResult> WalkPath(
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

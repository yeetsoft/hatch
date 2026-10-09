using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Hatch.Cli;

/// <summary>One check that failed: what it is called, where to read it, and the job behind it where the forge has one.</summary>
/// <param name="Url">The check's own page, or null where it gave none.</param>
/// <param name="JobId">The id <c>gh run view --job</c> reads a log by. Set only for a check run - a commit status has no job.</param>
public sealed record FailingCheck(string Name, string? Url, long? JobId = null);

/// <summary>What the build on one sha came to - see <see cref="BuildVerdicts"/>.</summary>
public sealed record BuildRead(string Verdict, IReadOnlyList<FailingCheck> Failing);

/// <summary>
/// What a forge answered: the build, or why it could not say. The forge does not
/// complain itself - the poll's dedupe lives in its own closure, and a forge that
/// spoke directly would repeat itself every interval.
/// </summary>
/// <param name="Why">One line, for a sentence. Null for a fake that says nothing.</param>
public sealed record ForgeAnswer(BuildRead? Read, string? Why);

/// <summary>
/// What a forge answered about a pull request: the state, or why it could not
/// say. The forge does not complain itself - the caller's dedupe does.
/// </summary>
/// <param name="State">One of <see cref="PullRequestStates"/>.</param>
/// <param name="Why">One line, for a sentence. Null where <paramref name="State"/> was read.</param>
public sealed record PullRequestAnswer(string? State, string? Why);

/// <summary>
/// Where a runner reads the build on a branch's tip from. A seam so a test can
/// say what the build came to without a forge to ask.
/// </summary>
public interface IForge
{
    /// <summary>The build on <paramref name="sha"/>, or why it could not be read.</summary>
    Task<ForgeAnswer> ReadAsync(string sha, CancellationToken ct);

    /// <summary>
    /// The lines of a failing check's log that lead up to its failure, or null
    /// where there is no log to give. See <see cref="GhForge.Excerpt"/>.
    /// </summary>
    Task<string?> LogAsync(FailingCheck check, CancellationToken ct);

    /// <summary>
    /// The pull request's own state - merged, open, or closed without merging.
    /// The only read that tells a merge from a close: see <see cref="PullRequestStates"/>.
    /// </summary>
    Task<PullRequestAnswer> ReadPullRequestAsync(string url, CancellationToken ct);
}

/// <summary>
/// The build on a sha, read by asking the runner's own <c>gh</c> - as whichever
/// account it is signed in as, spending that account's API budget.
/// </summary>
/// <remarks>
/// <para>Nothing here names a forge or inspects a host. <c>gh</c> is handed the
/// board's own identity for the repository where the project binds one
/// (<c>host/owner/repo</c>, which is <c>gh</c>'s own <c>[HOST/]OWNER/REPO</c>
/// form) as <c>GH_REPO</c>, and decides for itself whether it can reach it. A
/// host it cannot reach reads no builds. Setting <c>GH_REPO</c> also stops
/// <c>gh</c> preferring an <c>upstream</c> remote over <c>origin</c>. Where the
/// project binds nothing, <c>gh</c> resolves the repository from the checkout's
/// remotes.</para>
///
/// <para><c>gh api</c> ignores the host in <c>GH_REPO</c> and asks the default
/// host, so the two <c>api</c> calls also pass <c>--hostname</c> with the
/// canonical's first segment. Without it a repository on another host would be
/// read from a same-named repository on the default host, and the verdict would
/// be wrong without saying so. This hands <c>gh</c> an argument taken from the
/// board's own canonical; it names and inspects no host. <c>gh run view</c> does
/// honour the host, and is given none.</para>
///
/// <para>The binary is trimmed, so what <c>gh</c> prints is read with
/// <see cref="JsonDocument"/> and not by reflection.</para>
/// </remarks>
public sealed class GhForge(
    string path, string? canonical,
    Func<string, IReadOnlyDictionary<string, string>, IReadOnlyList<string>, CancellationToken, Task<GhForge.Ran>>? run = null)
    : IForge
{
    /// <summary>What a process said. <c>-1</c> with nothing on stderr is a binary that is not there.</summary>
    public readonly record struct Ran(int Code, string Out, string Err);

    /// <summary>The most lines one failing check's excerpt has, ending at its last error.</summary>
    public const int MaxExcerptLines = 150;

    /// <summary>The most bytes one failing check's excerpt has.</summary>
    public const int MaxExcerptBytes = 12 * 1024;

    private readonly Func<string, IReadOnlyDictionary<string, string>, IReadOnlyList<string>, CancellationToken, Task<Ran>> _run =
        run ?? Execute;

    public async Task<ForgeAnswer> ReadAsync(string sha, CancellationToken ct)
    {
        // A local path has no host, and nothing to ask a forge about.
        if (canonical is { } c && (c.StartsWith('/') || c.StartsWith('.')))
            return new ForgeAnswer(null, "its repository is a local path, so there is no forge to read a build from");

        var hostArgs = HostArgs();

        var runs = await _run(path, Env(), [
            "api", "--paginate", .. hostArgs,
            $"repos/{{owner}}/{{repo}}/commits/{sha}/check-runs?per_page=100",
            "--jq", ".check_runs[] | {id,name,status,conclusion,url:.html_url}",
        ], ct);
        if (runs.Code != 0) return new ForgeAnswer(null, Why(runs));

        var statuses = await _run(path, Env(), [
            "api", "--paginate", .. hostArgs,
            $"repos/{{owner}}/{{repo}}/commits/{sha}/status?per_page=100",
            "--jq", ".statuses[] | {context,state,url:.target_url}",
        ], ct);
        if (statuses.Code != 0) return new ForgeAnswer(null, Why(statuses));

        try
        {
            return new ForgeAnswer(Classify(runs.Out, statuses.Out), null);
        }
        catch (JsonException)
        {
            return new ForgeAnswer(null, "gh answered with something that is not a build");
        }
    }

    public async Task<PullRequestAnswer> ReadPullRequestAsync(string url, CancellationToken ct)
    {
        // A local path has no host, and nothing to ask a forge about.
        if (canonical is { } c && (c.StartsWith('/') || c.StartsWith('.')))
            return new PullRequestAnswer(null, "its repository is a local path, so there is no forge to read a pull request from");

        // The url names the host, the owner and the number, so gh resolves the
        // repository from it without --hostname - unlike gh api, which ignores
        // the host in GH_REPO and always needs it.
        var ran = await _run(path, Env(), ["pr", "view", url, "--json", "state"], ct);
        if (ran.Code != 0) return new PullRequestAnswer(null, Why(ran));

        try
        {
            using var doc = JsonDocument.Parse(ran.Out);
            var state = Text(doc.RootElement, "state");
            return new PullRequestAnswer(ClassifyState(state), null);
        }
        catch (JsonException)
        {
            return new PullRequestAnswer(null, "gh answered with something that is not a pull request");
        }
    }

    /// <summary>One of <see cref="PullRequestStates"/>, matched case-insensitively against what <c>gh</c> prints (<c>OPEN</c>, <c>CLOSED</c>, <c>MERGED</c>).</summary>
    public static string ClassifyState(string? state) => state?.ToUpperInvariant() switch
    {
        "MERGED" => PullRequestStates.Merged,
        "OPEN" => PullRequestStates.Open,
        "CLOSED" => PullRequestStates.Closed,
        _ => PullRequestStates.Unknown,
    };

    public async Task<string?> LogAsync(FailingCheck check, CancellationToken ct)
    {
        // A status has no job to read. Every check run is tried, and a check that
        // is not an Actions job answers with a failure, which is the answer.
        if (check.JobId is not { } job) return null;

        var ran = await _run(path, Env(), ["run", "view", "--job", job.ToString(), "--log-failed"], ct);
        return ran.Code == 0 ? Excerpt(ran.Out) : null;
    }

    private string[] HostArgs() =>
        canonical is { } c && c.Split('/')[0] is { Length: > 0 } host ? ["--hostname", host] : [];

    private IReadOnlyDictionary<string, string> Env()
    {
        var env = new Dictionary<string, string> { ["GH_PROMPT_DISABLED"] = "1", ["NO_COLOR"] = "1" };
        if (canonical is not null) env["GH_REPO"] = canonical;
        return env;
    }

    /// <summary>One line for a sentence: <c>gh</c> is not there, or what it said first.</summary>
    private static string Why(Ran ran)
    {
        if (ran.Code == -1 && ran.Err.Length == 0) return "gh is not installed";

        var first = ran.Err.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);

        return first ?? $"gh exited {ran.Code}";
    }

    /// <summary>
    /// The verdict over what the two calls printed: one JSON object a line, each
    /// check run <c>{id,name,status,conclusion,url}</c> and each status
    /// <c>{context,state,url}</c>.
    /// </summary>
    /// <remarks>
    /// Only the <c>statuses</c> list counts, and never the endpoint's own
    /// <c>state</c>, which says <c>pending</c> for a sha that has no statuses at
    /// all. Pending outranks failed for the verdict - the read waits for every
    /// check to conclude before calling it <c>failed</c>, so one increment sees
    /// every failure - but a pending verdict still carries whatever has already
    /// failed, so a check that fails while others still run is visible before
    /// the last one concludes. No check of either kind is <c>none</c>. A check
    /// counts once, at its latest run: the check-runs endpoint's default and the
    /// status endpoint's latest per context.
    /// </remarks>
    public static BuildRead Classify(string checkRuns, string statuses)
    {
        var any = false;
        var pending = false;
        var failing = new List<FailingCheck>();

        foreach (var line in Lines(checkRuns))
        {
            using var doc = JsonDocument.Parse(line);
            var o = doc.RootElement;
            any = true;

            var status = Text(o, "status");
            var conclusion = Text(o, "conclusion");

            if (status != "completed") pending = true;
            else if (conclusion is "failure" or "timed_out" or "startup_failure")
                failing.Add(new FailingCheck(Text(o, "name") ?? "unnamed check", Text(o, "url"), o.TryGetProperty("id", out var id) && id.TryGetInt64(out var n) ? n : null));
        }

        foreach (var line in Lines(statuses))
        {
            using var doc = JsonDocument.Parse(line);
            var o = doc.RootElement;
            any = true;

            var state = Text(o, "state");
            if (state == "pending") pending = true;
            else if (state is "failure" or "error")
                failing.Add(new FailingCheck(Text(o, "context") ?? "unnamed status", Text(o, "url")));
        }

        // Once a name, sorted: two checks of one name are one failure, and the
        // same failure in another order is the same verdict.
        var sorted = failing.GroupBy(f => f.Name, StringComparer.Ordinal).Select(g => g.First())
            .OrderBy(f => f.Name, StringComparer.Ordinal).ToList();

        if (!any) return new BuildRead(BuildVerdicts.None, []);
        if (pending) return new BuildRead(BuildVerdicts.Pending, sorted);
        if (sorted.Count == 0) return new BuildRead(BuildVerdicts.Passed, []);
        return new BuildRead(BuildVerdicts.Failed, sorted);
    }

    private static IEnumerable<string> Lines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Length > 0);

    private static string? Text(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>
    /// The lines of a job's failed log that lead up to its failure: up to
    /// <see cref="MaxExcerptLines"/>, ending at the log's last <c>##[error]</c>
    /// line or at its end where there is none, each line's
    /// <c>job&lt;TAB&gt;step&lt;TAB&gt;timestamp</c> prefix stripped, and at most
    /// <see cref="MaxExcerptBytes"/> - cut from the front, so what is nearest the
    /// failure is what stays.
    /// </summary>
    /// <remarks>
    /// The last lines of a job's log are the wrong ones: they are the post-job
    /// cleanup, and the test failures sit before the error line. The first line
    /// of the log carries a byte-order mark before its timestamp, and a step's
    /// name can contain spaces, so the prefix is taken apart on its first two
    /// tabs and not on whitespace.
    /// </remarks>
    public static string? Excerpt(string log)
    {
        var lines = log.ReplaceLineEndings("\n").Split('\n').Select(StripPrefix).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        if (lines.Count == 0) return null;

        var end = lines.FindLastIndex(l => l.StartsWith("##[error]", StringComparison.Ordinal));
        end = end < 0 ? lines.Count - 1 : end;

        var from = Math.Max(0, end + 1 - MaxExcerptLines);
        var kept = lines.Skip(from).Take(end + 1 - from).ToList();

        while (kept.Count > 1 && Encoding.UTF8.GetByteCount(string.Join('\n', kept)) > MaxExcerptBytes)
            kept.RemoveAt(0);

        var text = string.Join('\n', kept);
        return text.Trim().Length == 0 ? null : text;
    }

    /// <summary>
    /// <c>job&lt;TAB&gt;step&lt;TAB&gt;timestamp text</c> to <c>text</c>. A line
    /// with fewer than two tabs is kept as it is.
    /// </summary>
    private static string StripPrefix(string line)
    {
        var line1 = line.TrimStart('﻿');

        var first = line1.IndexOf('\t');
        var second = first < 0 ? -1 : line1.IndexOf('\t', first + 1);
        if (second < 0) return line1;

        var rest = line1[(second + 1)..].TrimStart('﻿');
        var space = rest.IndexOf(' ');

        // The timestamp is what is left of the first space, and it is only
        // dropped when it looks like one.
        return space > 0 && rest[..space].Contains('T') && rest[..space].EndsWith('Z') ? rest[(space + 1)..] : rest;
    }

    /// <summary>Runs <c>gh</c> in <paramref name="dir"/>. A missing binary is an answer, not an exception.</summary>
    private static async Task<Ran> Execute(
        string dir, IReadOnlyDictionary<string, string> env, IReadOnlyList<string> args, CancellationToken ct)
    {
        var start = new ProcessStartInfo
        {
            FileName = "gh",
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        foreach (var (name, value) in env) start.Environment[name] = value;

        try
        {
            using var process = Process.Start(start);
            if (process is null) return new Ran(-1, "", "");

            // Both at once: a command with more to say on stderr than a pipe
            // holds would otherwise wait for a reader that is busy on stdout.
            var stderr = process.StandardError.ReadToEndAsync(ct);
            var stdout = process.StandardOutput.ReadToEndAsync(ct);

            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                throw;
            }

            return new Ran(process.ExitCode, await stdout, await stderr);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            return new Ran(-1, "", "");
        }
    }
}

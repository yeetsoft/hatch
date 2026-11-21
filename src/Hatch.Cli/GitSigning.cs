using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Hatch.Cli;

/// <summary>Whether git can sign a commit in a checkout.</summary>
public enum Signing
{
    /// <summary>Git is not set to sign commits here, so a commit is never held up by a signer.</summary>
    Off,

    /// <summary>Git is set to sign, and signed a test object.</summary>
    Works,

    /// <summary>Git is set to sign, and signing failed or did not answer in time.</summary>
    Fails,
}

/// <param name="State">What the check found.</param>
/// <param name="Why">For <see cref="Signing.Fails"/>, the first line of what git complained of; empty otherwise.</param>
/// <param name="OldGit">Git is older than 2.31, which does not read <c>GIT_CONFIG_COUNT</c>.</param>
public sealed record SigningCheck(Signing State, string Why = "", bool OldGit = false)
{
    public bool Fails => State == Signing.Fails;
}

/// <summary>
/// Asking git whether a commit can be signed here, the way the loop would make one.
/// </summary>
/// <remarks>
/// <para>A runner's machine may be set to sign every commit with a key that is
/// locked, a program that is not there, or an agent that is not running. A
/// session on it would find that out at its first commit, with nobody there to
/// say what to do. This asks first, with git itself, so it reads the machine's
/// own key, program and format and not a guess at them.</para>
///
/// <para>The test object is a commit made with <c>commit-tree -S</c>. That
/// writes one unreachable object and nothing else: no ref moves, and neither
/// the index nor the working tree is read or written. It cannot prompt, since
/// stdin is closed and <c>GIT_TERMINAL_PROMPT</c> is off, and a signing program
/// that waits anyway is ended, with everything it started, at the limit.</para>
///
/// <para>No identity is set for it: the default signing key comes from the
/// committer's identity, so setting one would test a key the loop's commits do
/// not use.</para>
/// </remarks>
public static class GitSigning
{
    /// <summary>How long git has to sign before the answer is <see cref="Signing.Fails"/>.</summary>
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);

    /// <summary>The first release that reads <c>GIT_CONFIG_COUNT</c>.</summary>
    private static readonly Version EnvironmentConfig = new(2, 31);

    /// <param name="root">The checkout.</param>
    /// <param name="limit">How long git has to sign.</param>
    /// <param name="environment">Set over the inherited environment for each git call - a test points git at its own settings with it.</param>
    public static async Task<SigningCheck> CheckAsync(
        string root, TimeSpan limit, CancellationToken ct, IReadOnlyDictionary<string, string?>? environment = null)
    {
        var env = new Dictionary<string, string?>(environment ?? new Dictionary<string, string?>())
        {
            ["GIT_TERMINAL_PROMPT"] = "0",
        };

        async Task<ProbeResult> Git(params string[] args) =>
            await Detect.ProbeIn("git", args, limit, ct, root, env);

        var old = Older(await Git("--version"));

        var on = await Git("config", "--type=bool", "commit.gpgsign");
        if (!on.Completed || on.ExitCode != 0 || on.Output.Trim() != "true") return new SigningCheck(Signing.Off, "", old);

        // An unborn branch has no tree to name; the empty one is written, and is
        // what a first commit would sign.
        var head = await Git("rev-parse", "--verify", "--quiet", "HEAD^{tree}");
        var tree = head is { Completed: true, ExitCode: 0 } ? head.Output.Trim() : "";
        if (tree.Length == 0)
        {
            var made = await Git("mktree");
            if (!made.Completed || made.ExitCode != 0)
                return new SigningCheck(Signing.Fails, First(made.Output, "git could not write a test object"), old);

            tree = made.Output.Trim();
        }

        var signed = await Git("commit-tree", "-S", "-m", "probe", tree);
        if (!signed.Completed)
            return new SigningCheck(Signing.Fails, $"git did not answer within {Math.Max(1, (int)Math.Round(limit.TotalSeconds))}s", old);

        return signed.ExitCode == 0
            ? new SigningCheck(Signing.Works, "", old)
            : new SigningCheck(Signing.Fails, First(signed.Output, "git could not sign"), old);
    }

    /// <summary>
    /// The first line git printed. Git opens a signing failure with a line that ends
    /// in a colon and leaves the reason to the next, so a line like that is read
    /// together with the one after it - still one line, and one that says why.
    /// </summary>
    private static string First(string output, string otherwise)
    {
        var lines = output.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0) return otherwise;

        return lines[0].EndsWith(':') && lines.Count > 1 ? $"{lines[0]} {lines[1]}" : lines[0];
    }

    /// <summary><c>git version 2.43.0.windows.1</c> is not older; <c>git version 2.30.2</c> is. A line that cannot be read is not.</summary>
    internal static bool Older(ProbeResult version)
    {
        if (!version.Completed || version.ExitCode != 0) return false;

        var match = Regex.Match(version.Output, @"(\d+)\.(\d+)");
        return match.Success
            && new Version(
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)) < EnvironmentConfig;
    }
}

/// <summary>
/// The checkouts that answered <see cref="Signing.Fails"/> and have not been
/// asked again yet. A reference on <see cref="Runtime"/>, so a copy of it with
/// a field changed still shares what was learned.
/// </summary>
public sealed class SigningCache
{
    /// <summary>How long a failing checkout is left alone: a locked key is usually somebody's to unlock, and asking again every pass would only repeat the wait.</summary>
    public static readonly TimeSpan Keep = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<string, (DateTimeOffset Until, SigningCheck Check)> _failed =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <summary>The failure remembered for <paramref name="root"/>, until it is thirty minutes old.</summary>
    public SigningCheck? Failed(string root, DateTimeOffset now)
    {
        var key = Key(root);
        if (!_failed.TryGetValue(key, out var held)) return null;
        if (now < held.Until) return held.Check;

        _failed.TryRemove(key, out _);
        return null;
    }

    /// <summary>Remembers a failure, and forgets the path on any other answer.</summary>
    public void Record(string root, SigningCheck check, DateTimeOffset now)
    {
        if (check.Fails) _failed[Key(root)] = (now + Keep, check);
        else _failed.TryRemove(Key(root), out _);
    }

    private static string Key(string root) => Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

/// <summary>
/// Whether this increment's git calls, and its session's, are to make commits
/// unsigned. Set by the gate once the check has failed and the project allows
/// it, and cleared when the pass ends, so the next increment starts without.
/// A reference on <see cref="Runtime"/>, shared by every <see cref="Workspace"/> it makes.
/// </summary>
public sealed class UnsignedCommits
{
    public volatile bool On;

    /// <summary>Git is older than 2.31, so the override is passed as <c>-c</c> and the session is told in its prompt.</summary>
    public volatile bool OldGit;

    public void Set(bool oldGit)
    {
        OldGit = oldGit;
        On = true;
    }

    public void Clear()
    {
        On = false;
        OldGit = false;
    }
}

/// <summary>What a session's prompt says about signing a commit.</summary>
/// <param name="State">What the check found in the checkout the session runs in.</param>
/// <param name="Allows">The project allows an unsigned commit - <see cref="Hatch.Contracts.WorkDto.AllowsUnsignedCommits"/>.</param>
/// <param name="OldGit">Git is older than 2.31, so the runner's override does not reach the session and the prompt carries it.</param>
public sealed record CommitSigning(Signing State, bool Allows, bool OldGit = false);

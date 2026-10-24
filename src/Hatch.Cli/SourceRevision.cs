using System.Diagnostics;

namespace Hatch.Cli;

/// <summary>
/// The newest commit under a set of paths in the checkout this process is
/// actually standing in - not the board's own revision (<see
/// cref="ICliRevision"/>), which is stamped once at build time and has no way
/// to answer what the checkout's history looks like right now.
/// </summary>
public sealed record SourceRevisionInfo(string Sha, int Sequence);

/// <summary>
/// Reads the newest commit touching a set of paths out of a git checkout.
/// Replaceable so a test can say what the checkout's history looks like
/// without a real repository to ask.
/// </summary>
public interface ISourceRevision
{
    /// <summary>
    /// The newest commit touching any of <paramref name="paths"/>, or null
    /// when there isn't one - no commit touches them, the root isn't a git
    /// repository, or git isn't available.
    /// </summary>
    SourceRevisionInfo? Newest(IReadOnlyList<string> paths);
}

/// <summary>
/// The real one, shelling out to git over the checkout at <paramref
/// name="root"/>. Best-effort, the same philosophy <see cref="ICliRevision"/>'s
/// <c>BuiltAt</c> uses for its own IO: a checkout with no git history to ask
/// is not an error, it is simply a process with nothing to report.
/// </summary>
public sealed class GitSourceRevision(string root) : ISourceRevision
{
    public SourceRevisionInfo? Newest(IReadOnlyList<string> paths)
    {
        var log = Git(["log", "-1", "--format=%H", "--", .. paths]);
        if (!log.Ok) return null;

        var sha = log.Out.Trim();
        if (sha.Length == 0) return null;

        var count = Git(["rev-list", "--count", sha]);
        if (!count.Ok) return null;
        if (!int.TryParse(count.Out.Trim(), out var sequence) || sequence <= 0) return null;

        return new SourceRevisionInfo(sha, sequence);
    }

    private readonly record struct Ran(int Code, string Out)
    {
        public bool Ok => Code == 0;
    }

    private Ran Git(IReadOnlyList<string> args)
    {
        var start = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(start);
            if (process is null) return new Ran(-1, "");

            var stderr = process.StandardError.ReadToEndAsync();
            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            stderr.GetAwaiter().GetResult();
            return new Ran(process.ExitCode, stdout);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new Ran(-1, "");
        }
    }
}

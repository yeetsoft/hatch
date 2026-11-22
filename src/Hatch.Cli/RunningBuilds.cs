using System.Diagnostics;

namespace Hatch.Cli;

/// <summary>
/// The files the processes on this machine are running, so a sweep leaves a
/// build that something still uses.
/// </summary>
/// <remarks>
/// Windows refuses to open a running file for writing, which the sweep asks of
/// each file, so it needs no scan and answers nothing. macOS and Linux let a
/// running file go, and the next <c>hatch</c> call
/// through a session's <c>PATH</c> or its hooks would find nothing, so there the
/// processes are listed: Linux reads <c>/proc/&lt;pid&gt;/exe</c>, macOS asks
/// <c>ps -axo comm=</c>, which prints full paths there. The macOS one has never
/// been run by the people who wrote it.
/// </remarks>
public static class RunningBuilds
{
    /// <summary>Every path a process runs from that this account may see. Throws where it cannot tell at all.</summary>
    public static IReadOnlyCollection<string> Paths()
    {
        if (OperatingSystem.IsWindows()) return [];

        return Directory.Exists("/proc/self") ? FromProc() : FromPs();
    }

    private static List<string> FromProc()
    {
        var paths = new List<string>();

        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out _)) continue;

            try
            {
                if (new FileInfo(Path.Combine(directory, "exe")).LinkTarget is not { Length: > 0 } target) continue;

                const string Gone = " (deleted)";
                paths.Add(target.EndsWith(Gone, StringComparison.Ordinal) ? target[..^Gone.Length] : target);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A process that ended since, or another account's: not this one's to protect.
            }
        }

        return paths;
    }

    private static List<string> FromPs()
    {
        var start = new ProcessStartInfo("ps", ["-axo", "comm="])
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        using var ps = Process.Start(start) ?? throw new InvalidOperationException("could not run ps");
        var output = ps.StandardOutput.ReadToEndAsync();

        if (!ps.WaitForExit(TimeSpan.FromSeconds(5)))
        {
            try { ps.Kill(); }
            catch (InvalidOperationException) { }
            throw new TimeoutException("ps did not answer");
        }

        return output.GetAwaiter().GetResult()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }
}

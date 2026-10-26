using System.Globalization;
using System.Text.RegularExpressions;

namespace Hatch.Cli;

/// <summary>
/// Switches the log on for one <c>work</c> / <c>go-to-work</c> run: opens the
/// file, wraps the terminal the run was about to use, and says where it is.
/// </summary>
public static partial class RunLog
{
    /// <summary>
    /// The log for this run, wrapped around <paramref name="inner"/>, or null
    /// where <paramref name="directory"/> is null - the log is off, and the run
    /// behaves exactly as it did before there was one. The first line it says,
    /// to the screen and to the file, is where the file is.
    /// </summary>
    public static LogTerminal? Open(
        Terminal inner, string? directory, string command, string runnerName, TimeProvider clock)
    {
        if (directory is null) return null;

        var path = Path.Combine(directory, NameFor(command, runnerName, clock.GetUtcNow()));
        var log = new LogTerminal(inner, path, clock);
        log.Line($"hatch: logging to {path}");
        return log;
    }

    /// <summary>
    /// <c>&lt;command&gt;-&lt;runner&gt;-&lt;yyyyMMdd-HHmmss&gt;.log</c>, the runner's
    /// name reduced to letters, digits and hyphens so no name can reach out of
    /// the folder or break a file system.
    /// </summary>
    public static string NameFor(string command, string runnerName, DateTimeOffset now)
    {
        var runner = Unsafe().Replace(runnerName, "");
        if (runner.Length == 0) runner = "runner";
        return $"{command}-{runner}-{now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.log";
    }

    [GeneratedRegex("[^A-Za-z0-9-]")]
    private static partial Regex Unsafe();
}

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
    /// behaves exactly as it did before there was one - or where no free name
    /// could be had. The first line it says, to the screen and to the file, is
    /// where the file is.
    /// </summary>
    /// <remarks>
    /// A night is one file. Where <paramref name="carried"/> is set, the
    /// incarnation before this one wrote there, and this one appends to the same
    /// file. Otherwise the name is claimed with <see cref="FileMode.CreateNew"/>,
    /// never an existence check, so two runners starting in the same second
    /// cannot both be given it: the loser takes <c>-2</c>, then <c>-3</c>.
    /// </remarks>
    public static LogTerminal? Open(
        Terminal inner, string? directory, string command, string runnerName, TimeProvider clock,
        string? carried = null)
    {
        if (directory is null) return null;

        if (carried is { Length: > 0 })
        {
            var continued = new LogTerminal(inner, carried, clock);
            continued.Line($"hatch: logging to {carried} (continued)");
            return continued;
        }

        var first = Path.Combine(directory, NameFor(command, runnerName, clock.GetUtcNow()));
        var path = Claim(directory, first, out var exhausted);
        if (exhausted)
        {
            inner.Complain($"hatch: cannot write the log at {first}: every name up to {MaxClaims} is taken");
            return null;
        }

        var log = new LogTerminal(inner, path, clock);
        log.Line($"hatch: logging to {path}");
        return log;
    }

    /// <summary>
    /// The first of <paramref name="first"/>, then the same name with <c>-2</c>,
    /// <c>-3</c>... before the extension, that did not exist and now does. Any
    /// other failure to create it answers <paramref name="first"/> unclaimed,
    /// for <see cref="LogTerminal"/> to fail at and complain about once.
    /// </summary>
    private static string Claim(string directory, string first, out bool exhausted)
    {
        exhausted = false;

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return first;
        }

        var stem = Path.Combine(directory, Path.GetFileNameWithoutExtension(first));
        var extension = Path.GetExtension(first);

        for (var attempt = 1; attempt <= MaxClaims; attempt++)
        {
            var candidate = attempt == 1 ? first : $"{stem}-{attempt}{extension}";

            try
            {
                new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite).Dispose();
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate))
            {
                // Taken - by an earlier night, or by another runner this second.
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return first;
            }
        }

        exhausted = true;
        return first;
    }

    private const int MaxClaims = 500;

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

using System.Globalization;
using System.Text.RegularExpressions;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The outcomes a backup attempt is recorded under - see
/// <see cref="BackupAttempt"/>.
/// </summary>
public static class BackupOutcome
{
    public const string Ok = "ok";
    public const string Failed = "failed";
    public const string NotConfigured = "not-configured";
}

/// <summary>
/// One run of the backup process, whatever it did or did not do - see
/// <see cref="BackupStatus"/>.
/// </summary>
public record BackupAttempt(DateTimeOffset At, string Outcome, string? Error, string? File, long? Bytes);

/// <summary>
/// The last backup attempt, held for whoever asks - the status endpoint, and
/// nothing else. A reference swap is the whole of its thread safety: the
/// record it holds is immutable, so nothing ever reads it half-written.
/// </summary>
public class BackupStatus
{
    private BackupAttempt? _last;

    public BackupAttempt? Last => Volatile.Read(ref _last);

    public void Record(BackupAttempt attempt) => Volatile.Write(ref _last, attempt);
}

/// <summary>One finished backup on disk, as <see cref="BackupFolder.List"/> finds it.</summary>
public record BackupCopy(string Name, DateTimeOffset At, long Bytes);

/// <summary>
/// A folder's backups, newest first, or the reason there are none to list.
/// </summary>
public record BackupCopies(IReadOnlyList<BackupCopy> Items, string? Problem);

/// <summary>
/// The one place a backup file's name is built or read back - so a copy on
/// disk and a copy in a listing agree on what counts as Hatch's.
/// </summary>
public static partial class BackupNames
{
    private const string Format = "yyyyMMdd'T'HHmmss'Z'";

    /// <summary>The name a finished backup taken at <paramref name="at"/> is written under.</summary>
    public static string Final(DateTimeOffset at) =>
        $"hatch-{at.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture)}.sql";

    /// <summary>The name a backup taken at <paramref name="at"/> is written under while still in progress.</summary>
    public static string Partial(DateTimeOffset at) => Final(at) + ".partial";

    /// <summary>Whether <paramref name="name"/> is a finished backup's name, and the instant it was taken at.</summary>
    public static bool TryParseCopy(string name, out DateTimeOffset at) => TryParse(FinalPattern(), name, out at);

    /// <summary>Whether <paramref name="name"/> is an in-progress backup's name, and the instant it was taken at.</summary>
    public static bool TryParsePartial(string name, out DateTimeOffset at) => TryParse(PartialPattern(), name, out at);

    private static bool TryParse(Regex pattern, string name, out DateTimeOffset at)
    {
        var match = pattern.Match(name);
        if (match.Success &&
            DateTimeOffset.TryParseExact(
                match.Groups[1].Value, Format, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out at))
        {
            return true;
        }

        at = default;
        return false;
    }

    // [0-9] and \z rather than \d and $: \d matches non-ASCII digits and $
    // matches before a trailing newline, and the point of this pattern is
    // that only exact names are Hatch's.
    [GeneratedRegex(@"^hatch-([0-9]{8}T[0-9]{6}Z)\.sql\z")]
    private static partial Regex FinalPattern();

    [GeneratedRegex(@"^hatch-([0-9]{8}T[0-9]{6}Z)\.sql\.partial\z")]
    private static partial Regex PartialPattern();
}

/// <summary>
/// Reads what backups exist on disk - the one place that lists them, so the
/// status endpoint and the pruning pass agree on what is there.
/// </summary>
public static class BackupFolder
{
    /// <summary>
    /// The folder's finished backups, newest first by the instant in their
    /// name - never by mtime, which a restore or a copy can leave stale.
    /// Never throws: a folder that does not exist or cannot be read comes
    /// back as no copies and a problem naming why.
    /// </summary>
    public static BackupCopies List(string path)
    {
        if (!Directory.Exists(path))
        {
            return new BackupCopies([], $"{path} does not exist");
        }

        try
        {
            var copies = new List<BackupCopy>();
            foreach (var file in new DirectoryInfo(path).EnumerateFiles())
            {
                if (!BackupNames.TryParseCopy(file.Name, out var at))
                {
                    continue;
                }

                try
                {
                    copies.Add(new BackupCopy(file.Name, at, file.Length));
                }
                catch (IOException)
                {
                    // Vanished mid-listing - a pruning race, not a reason to
                    // fail the whole listing.
                }
            }

            return new BackupCopies(copies.OrderByDescending(c => c.At).ToList(), null);
        }
        catch (Exception ex)
        {
            return new BackupCopies([], $"{path} cannot be read: {ex.Message}");
        }
    }
}

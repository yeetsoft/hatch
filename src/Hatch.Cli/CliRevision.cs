using System.Reflection;

namespace Hatch.Cli;

/// <summary>
/// The commit this binary was built from. Mirrors
/// src/Hatch.Api/Services/HatchRevision.cs - see docs/plans/version.md.
/// </summary>
public interface ICliRevision
{
    /// <summary>Full 40-character git sha, or <see cref="CliRevision.Development"/>.</summary>
    string Revision { get; }

    /// <summary>
    /// Commit count at build time - the ordering a sha cannot provide on its
    /// own, and what makes "is this client behind me" answerable. 0 for a
    /// build that wasn't stamped.
    /// </summary>
    int Sequence { get; }

    /// <summary>When this assembly was written, or null if it can't be read.</summary>
    DateTimeOffset? BuiltAt { get; }

    /// <summary>True when nothing stamped this build - a local `dotnet build`.</summary>
    bool IsDevelopment { get; }
}

/// <summary>
/// Reads the build's git identity back out of its own assembly.
///
/// The values are put there by Dockerfile.api at publish time
/// (<c>-p:SourceRevisionId</c>, which the SDK appends to
/// <see cref="AssemblyInformationalVersionAttribute"/> as <c>+&lt;sha&gt;</c>, and
/// an <see cref="AssemblyMetadataAttribute"/> for the sequence) and, for a
/// local build, by `make build-hatch`/`make publish-hatch` computing both from
/// git directly. Deliberately *not* an environment variable on the running
/// process: a revision the caller supplies is a revision the caller can get
/// wrong, and every consumer of this value - a heartbeat the board reads to
/// flag a stale runner - is trusting it to be the truth about the code that is
/// actually running.
///
/// A singleton, resolved once at startup: it is immutable for the whole life
/// of the process, and re-reading it per heartbeat is work that cannot
/// produce a new answer.
/// </summary>
public class CliRevision : ICliRevision
{
    /// <summary>
    /// What an unstamped build reports. Kept identical to
    /// Hatch.Api.Services.HatchRevision.Development so a dev build reads the
    /// same on both sides of the wire.
    /// </summary>
    public const string Development = "dev";

    private const string SequenceKey = "HatchSequence";

    public string Revision { get; }
    public int Sequence { get; }
    public DateTimeOffset? BuiltAt { get; }
    public bool IsDevelopment => Revision == Development;

    public CliRevision() : this(typeof(CliRevision).Assembly) { }

    public CliRevision(Assembly assembly)
    {
        Revision = ParseRevision(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
        Sequence = ParseSequence(assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == SequenceKey)?.Value);
        BuiltAt = ReadBuiltAt(assembly);
    }

    /// <summary>
    /// The sha out of an informational version of the form <c>1.0.0+&lt;sha&gt;</c>.
    ///
    /// Anything that isn't a full sha - absent, empty, truncated, not hex -
    /// reads as a development build rather than throwing. A malformed stamp is
    /// a build-pipeline bug, and failing startup over it would take the whole
    /// CLI down in order to report something a heartbeat can simply say out
    /// loud. Truncation is checked rather than assumed away because a short sha
    /// is what the image tags carried before this existed, and half-migrating
    /// to the full one is a plausible mistake.
    /// </summary>
    public static string ParseRevision(string? informationalVersion)
    {
        if (string.IsNullOrEmpty(informationalVersion)) return Development;

        var plus = informationalVersion.IndexOf('+');
        if (plus < 0 || plus == informationalVersion.Length - 1) return Development;

        var candidate = informationalVersion[(plus + 1)..];
        return IsFullSha(candidate) ? candidate : Development;
    }

    private static bool IsFullSha(string value) =>
        value.Length == 40 && value.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));

    /// <summary>
    /// 0 for anything unusable, which callers read as "no position in the
    /// history" rather than as "the first commit".
    /// </summary>
    public static int ParseSequence(string? value) =>
        int.TryParse(value, out var sequence) && sequence > 0 ? sequence : 0;

    /// <summary>
    /// The assembly file's last-write time, which inside an image is when
    /// `dotnet publish` wrote it. Approximate by construction and reported as
    /// such - it exists so an operator can tell a week-old binary from an
    /// hour-old one at a glance, not as a build record.
    /// </summary>
    private static DateTimeOffset? ReadBuiltAt(Assembly assembly)
    {
        try
        {
            var location = assembly.Location;
            if (string.IsNullOrEmpty(location) || !File.Exists(location)) return null;
            return new DateTimeOffset(File.GetLastWriteTimeUtc(location), TimeSpan.Zero);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}

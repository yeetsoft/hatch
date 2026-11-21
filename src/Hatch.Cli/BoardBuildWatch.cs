using Hatch.Contracts;

namespace Hatch.Cli;

/// <summary>What the loop does about the build its board named on a beat.</summary>
public enum BuildVerdict
{
    /// <summary>Keep the build this process is running.</summary>
    Stay,

    /// <summary>Ask the launcher for the board's build.</summary>
    Switch,
}

/// <summary>
/// Decides, beat by beat, whether a downloaded loop should ask its launcher for
/// the board's build. One lives for one <c>LoopAsync</c>, because what it
/// remembers - which revision the board has been naming, and since when - is
/// only true of one unbroken run of beats.
/// </summary>
/// <remarks>
/// <para>The board decides which side of the comparison a runner is on, and
/// the runner compares nothing. A build the board says is <c>behind</c> is
/// followed at once: it is older, and nothing is lost by moving. Any other word
/// - <c>ahead</c>, <c>diverged</c>, and any a newer board invents - means the
/// board may have been rolled back, or may be one of two builds being deployed
/// in turn, so it is followed only once every answer for
/// <see cref="Hold"/> has named the same revision. A beat with no answer, or
/// with nothing to say about a build, ends that run of answers.</para>
///
/// <para>Pure apart from the one line it may say: the clock is passed in.</para>
/// </remarks>
public sealed class BoardBuildWatch(BuildHold? held = null, Action<string>? say = null)
{
    /// <summary>How long a board must go on naming one build, when that build is not simply newer, before it is followed.</summary>
    public static readonly TimeSpan Hold = TimeSpan.FromMinutes(10);

    private readonly BuildHold held = held ?? new BuildHold();
    private readonly HashSet<string> sayless = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The revision the board has been naming without a break, or null.</summary>
    public string? Revision { get; private set; }

    /// <summary>When it began to be named.</summary>
    public DateTimeOffset Since { get; private set; }

    /// <summary>
    /// Takes one beat's answer. <paramref name="answered"/> is false for a beat
    /// that got no reply; <paramref name="build"/> is null for a reply that
    /// named no build, which is a runner already current or a board too old to say.
    /// </summary>
    public BuildVerdict Judge(RunnerBoardBuildDto? build, bool answered, DateTimeOffset now)
    {
        if (!answered || build is null)
        {
            Revision = null;
            return BuildVerdict.Stay;
        }

        if (build.Download is not { Length: > 0 } || build.Sha256 is not { Length: > 0 })
        {
            // Nothing to fetch, so nothing to wait for either.
            Revision = null;
            if (sayless.Add(build.Revision))
                say?.Invoke($"hatch: the board is on build {Short(build.Revision)} and names no download for this platform - staying on this build");
            return BuildVerdict.Stay;
        }

        if (!string.Equals(Revision, build.Revision, StringComparison.OrdinalIgnoreCase))
        {
            Revision = build.Revision;
            Since = now;
        }

        if (held.Holds(build.Revision, now)) return BuildVerdict.Stay;

        if (string.Equals(build.Compare, RunnerBuildComparisons.Behind, StringComparison.Ordinal))
            return BuildVerdict.Switch;

        return now - Since >= Hold ? BuildVerdict.Switch : BuildVerdict.Stay;
    }

    /// <summary>The short form of a sha, as it reads in a line.</summary>
    public static string Short(string sha) => sha.Length > 8 ? sha[..8] : sha;
}

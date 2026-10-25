namespace Hatch.Cli;

/// <summary>
/// The arithmetic a set of runners shares: pulling <c>--runners</c> out of the
/// command line, and turning one slot number into the workspace, night-state
/// path and arguments that slot's own child gets.
/// </summary>
/// <remarks>
/// Pure, and wired to nothing yet - every method here takes what it needs as
/// an argument rather than reading <see cref="Settings"/> or the environment
/// itself, so a set host (HA-1530) can compute a child's whole command line
/// before it ever spawns one.
/// </remarks>
public static class RunnerSet
{
    /// <summary>The one refusal every bad <c>--runners</c> shares.</summary>
    private const string Refusal = "hatch: --runners takes a count from 1 to 16";

    /// <summary>
    /// Strips <c>--runners &lt;count&gt;</c> out of <paramref name="args"/> wherever it
    /// appears, the same way <c>Program.TryExtractProfile</c> strips
    /// <c>--profile</c> - before, between or after every other flag, last one
    /// standing on repeat.
    /// </summary>
    /// <remarks>
    /// Unlike <c>--profile</c>, a bare trailing flag, a value that will not
    /// parse and a value outside 1..16 all produce the same one refusal rather
    /// than a separate "takes a count" message - a count that is out of range
    /// is no more usable than one that was never there, so there is nothing a
    /// second message would tell the caller that the first does not.
    /// </remarks>
    internal static bool TryExtract(string[] args, out string[] rest, out int runners, out string refusal)
    {
        var kept = new List<string>(args.Length);
        runners = 1;
        refusal = "";

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] != "--runners")
            {
                kept.Add(args[i]);
                continue;
            }

            if (i + 1 >= args.Length || !int.TryParse(args[i + 1].Trim(), out var parsed) || parsed < 1 || parsed > 16)
            {
                rest = args;
                refusal = Refusal;
                return false;
            }

            runners = parsed;
            i++;
        }

        rest = kept.ToArray();
        return true;
    }

    /// <summary>
    /// The workspace one member of the set clones into: <paramref name="baseWorkspace"/>
    /// itself for the first, a sibling of it - not a child - for every other.
    /// </summary>
    /// <remarks>
    /// A sibling rather than a subdirectory because a runner walks the top
    /// level of its own workspace looking for clones, and a subdirectory
    /// holding another member's clones would be served as if it were this
    /// member's own. This method never looks at what <paramref name="baseWorkspace"/>
    /// itself is shaped like - profile-aware or not - it only ever sees the
    /// string <c>Settings.DefaultWorkspacePath</c> or a flag already resolved
    /// to, and appends <c>.n</c> to it the same way regardless.
    /// </remarks>
    internal static string WorkspaceFor(string baseWorkspace, int slot)
    {
        if (slot == 1) return baseWorkspace;

        var trimmed = baseWorkspace.TrimEnd('/', '\\');
        return $"{trimmed}.{slot}";
    }

    /// <summary>
    /// The <c>HATCH_NIGHT_STATE</c> path one member of the set hands its
    /// totals through: <c>null</c> where there is none to begin with, else
    /// <paramref name="path"/> with <paramref name="slot"/> appended -
    /// including slot 1.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="WorkspaceFor"/>, slot 1 is not left verbatim: every
    /// member, the first included, writes to its own <c>HATCH_NIGHT_STATE.&lt;slot&gt;</c>,
    /// because the set host reads every member's file back and a shared path
    /// for slot 1 would be a lone member's totals overwritten by whichever of
    /// the two last wrote them.
    /// </remarks>
    internal static string? NightStateFor(string? path, int slot) =>
        path is null ? null : $"{path}.{slot}";

    /// <summary>
    /// The arguments one member of the set is actually spawned with: the
    /// first keeps whatever <paramref name="rest"/> already says about its
    /// own <c>--repo</c>/<c>--workspace</c> and only gains <c>--no-keys</c>;
    /// every other loses any <c>--repo</c>/<c>--workspace</c> it was given -
    /// those belong to the set host, not to a sibling - and gains its own
    /// <c>--workspace</c> pointed at <paramref name="workspace"/> plus
    /// <c>--no-keys</c>.
    /// </summary>
    /// <remarks>
    /// Every member runs with <c>--no-keys</c> because the set host is the one
    /// that heartbeats and dispatches; a sibling claiming a key of its own
    /// would be a second runner answering to the same name.
    /// </remarks>
    internal static string[] ArgsFor(string[] rest, int slot, string workspace)
    {
        if (slot == 1) return [.. rest, "--no-keys"];

        var kept = new List<string>(rest.Length);
        for (var i = 0; i < rest.Length; i++)
        {
            if ((rest[i] == "--repo" || rest[i] == "--workspace") && i + 1 < rest.Length)
            {
                i++;
                continue;
            }

            kept.Add(rest[i]);
        }

        kept.Add("--workspace");
        kept.Add(workspace);
        kept.Add("--no-keys");
        return kept.ToArray();
    }

    /// <summary>
    /// How the set's children's exit codes become the one code the set host
    /// itself exits with: <c>75</c> (<see cref="GoToWorkCommand.RestartExitCode"/>)
    /// if any child asked for a restart and the host was not itself stopped,
    /// else <c>1</c> if any child refused, else <c>0</c>.
    /// </summary>
    /// <remarks>
    /// <paramref name="stopped"/> - the host's own stop file was seen - takes
    /// a <c>75</c> off the table rather than promoting it to a success: a
    /// child that asked to be restarted into a build that is never coming,
    /// because the host that would have restarted it is itself shutting down,
    /// is a request that goes unanswered, not one that succeeded.
    /// </remarks>
    internal static int Fold(IEnumerable<int> codes, bool stopped)
    {
        var seen = codes.ToList();
        if (!stopped && seen.Contains(GoToWorkCommand.RestartExitCode)) return GoToWorkCommand.RestartExitCode;
        if (seen.Contains(1)) return 1;
        return 0;
    }

    /// <summary>
    /// Set only by the set host on each child's environment, never by hand -
    /// which member of the set a process is, 1-based.
    /// </summary>
    public const string SlotVariable = "HATCH_SET_SLOT";

    /// <summary>
    /// Set only by the set host on each child's environment, never by hand -
    /// the host's own temp directory, where <c>stop</c>/<c>restart</c> files
    /// pass a signal to every member between increments.
    /// </summary>
    public const string DirVariable = "HATCH_SET_DIR";

    /// <summary>The file in <see cref="DirVariable"/>'s directory that tells every member to stop.</summary>
    public const string StopFileName = "stop";

    /// <summary>The file in <see cref="DirVariable"/>'s directory that asks every member to restart.</summary>
    public const string RestartFileName = "restart";

    /// <summary>Whether the set's host has left <paramref name="fileName"/> in <paramref name="setDir"/>.</summary>
    public static bool Signalled(string? setDir, string fileName) =>
        setDir is { Length: > 0 } && Path.Exists(Path.Combine(setDir, fileName));
}

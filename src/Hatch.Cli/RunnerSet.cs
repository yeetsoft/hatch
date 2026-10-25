using System.Diagnostics;
using System.Reflection;

namespace Hatch.Cli;

/// <summary>
/// The arithmetic a set of runners shares: pulling <c>--runners</c> out of the
/// command line, and turning one slot number into the workspace, night-state
/// path and arguments that slot's own child gets.
/// </summary>
/// <remarks>
/// Pure - every method here takes what it needs as an argument rather than
/// reading <see cref="Settings"/> or the environment itself, so a set host can
/// compute a child's whole command line before it ever spawns one, and a
/// member can ask what it is before it has loaded anything.
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
    /// Which member of a set this process is, from <see cref="SlotVariable"/>:
    /// the number where it is 2 or more, else <c>null</c>. Absent, blank,
    /// <c>1</c> and anything that is not a positive integer all read as an
    /// ordinary runner - the first slot is the set host's own checkout, and
    /// serves it exactly as a lone runner would.
    /// </summary>
    public static int? SlotFrom(IReadOnlyDictionary<string, string?> environment) =>
        environment.TryGetValue(SlotVariable, out var value)
        && int.TryParse(value?.Trim(), out var slot)
        && slot >= 2
            ? slot
            : null;

    /// <summary>
    /// <paramref name="settings"/> as a member of a set (slot 2 or more) runs
    /// them: no named checkouts and no pinned name. It is handed one workspace
    /// and serves that alone; a <c>HATCH_REPOS</c> checkout would be locked by
    /// the first runner to reach it and refused to every sibling, and a
    /// <c>HATCH_RUNNER</c> would give every member the same name.
    /// </summary>
    public static Settings ForMember(Settings settings) => settings with { Repos = [], Runner = null };

    /// <summary>
    /// The value after the last <c>--workspace</c> in <paramref name="args"/>,
    /// or <c>null</c> where there is none (or the flag is the final token) -
    /// the same rule <see cref="GoToWorkCommand"/> reads it by, so the answer
    /// here is the workspace the command will end up serving.
    /// </summary>
    internal static string? WorkspaceFrom(string[] args)
    {
        string? found = null;
        for (var i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] != "--workspace") continue;
            found = args[i + 1];
            i++;
        }

        return found;
    }

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

    /// <summary>
    /// Runs <paramref name="runners"/> children of this command at once and
    /// ends them together: the host of a set. Takes no lock, claim or board
    /// client of its own - each child does - so it only starts them, prefixes
    /// what they say, passes a stop or a restart between them through files in
    /// one shared directory, and folds what they exit with.
    /// </summary>
    /// <remarks>
    /// Cancelling <paramref name="ct"/> writes <see cref="StopFileName"/> and
    /// waits; it never reaches a child itself, because a child killed mid-increment
    /// is the one claim nobody released. A child that exits 75 while the set is
    /// not stopped writes <see cref="RestartFileName"/> so the others finish
    /// their increments and follow it; a child that exits 0 or 1 alone signals
    /// nothing.
    /// </remarks>
    public static async Task<int> RunAsync(
        string command, int runners, string[] rest, string? profile, Settings settings,
        IReadOnlyDictionary<string, string?> environment, ISetLauncher launcher, Action<string> say,
        CancellationToken ct)
    {
        var workspace = settings.WorkspaceFor(WorkspaceFrom(rest)).Workspace;
        if (workspace is null)
        {
            say("hatch: --runners needs a workspace to clone into");
            return 1;
        }

        var setDir = Path.Combine(Path.GetTempPath(), "hatch-set-" + Environment.ProcessId);
        if (Directory.Exists(setDir)) Directory.Delete(setDir, recursive: true);
        Directory.CreateDirectory(setDir);

        try
        {
            var nightState = environment.GetValueOrDefault("HATCH_NIGHT_STATE");
            var codes = new int[runners];

            // Registered before the children start, so a cancel that arrived
            // while the set was still being built is written at once.
            using (ct.Register(() => Signal(setDir, StopFileName)))
            {
                var tasks = Enumerable.Range(1, runners).Select(async slot =>
                {
                    var child = ChildFor(command, slot, rest, profile, workspace, nightState, setDir);
                    int code;
                    try
                    {
                        code = await launcher.RunAsync(child, line => say($"[{slot}] {line}"), CancellationToken.None);
                    }
                    catch (Exception e)
                    {
                        say($"[{slot}] hatch: could not run this runner: {e.Message}");
                        code = 1;
                    }

                    if (code == GoToWorkCommand.RestartExitCode && !ct.IsCancellationRequested)
                        Signal(setDir, RestartFileName);

                    codes[slot - 1] = code;
                });

                await Task.WhenAll(tasks);
            }

            return Fold(codes, ct.IsCancellationRequested);
        }
        finally
        {
            try { Directory.Delete(setDir, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Everything one member of the set is started with.</summary>
    private static SetChild ChildFor(
        string command, int slot, string[] rest, string? profile, string baseWorkspace, string? nightState,
        string setDir)
    {
        // Program has already taken --profile out of rest; a pinned profile has
        // to be handed on, or the children resolve a different one.
        string[] pinned = profile is null ? [] : ["--profile", profile];
        string[] args = [command, .. pinned, .. ArgsFor(rest, slot, WorkspaceFor(baseWorkspace, slot))];

        var env = new Dictionary<string, string> { [DirVariable] = setDir };
        if (NightStateFor(nightState, slot) is { } state) env["HATCH_NIGHT_STATE"] = state;

        // Slot 1 is the host's own checkout and serves it as a lone runner would.
        if (slot >= 2) env[SlotVariable] = slot.ToString();

        return new SetChild(slot, args, env);
    }

    private static void Signal(string setDir, string fileName)
    {
        try { File.WriteAllText(Path.Combine(setDir, fileName), ""); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>One member of a set, as the host starts it.</summary>
/// <param name="Slot">Which member, 1-based.</param>
/// <param name="Args">The whole argument list, command word first.</param>
/// <param name="Env">Only what this member adds to the environment it inherits.</param>
public sealed record SetChild(int Slot, string[] Args, IReadOnlyDictionary<string, string> Env);

/// <summary>The seam <see cref="RunnerSet.RunAsync"/> starts a member through.</summary>
public interface ISetLauncher
{
    /// <summary>Runs one member to its end, handing each line of its output to <paramref name="line"/>. Returns its exit code.</summary>
    Task<int> RunAsync(SetChild child, Action<string> line, CancellationToken ct);
}

/// <summary>The real thing: this same program, started again with the member's arguments.</summary>
public sealed class ProcessSetLauncher : ISetLauncher
{
    public async Task<int> RunAsync(SetChild child, Action<string> line, CancellationToken ct)
    {
        var program = Environment.ProcessPath ?? throw new InvalidOperationException("the path of this program is unknown");
        var start = new ProcessStartInfo
        {
            FileName = program,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // A member draws no footer and reads no keys: its input is closed at once.
            RedirectStandardInput = true,
            UseShellExecute = false,
        };

        // `dotnet run` and the like: the process is the host, and the program is its first argument.
        if (Path.GetFileNameWithoutExtension(program).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);

        foreach (var a in child.Args) start.ArgumentList.Add(a);
        foreach (var (name, value) in child.Env) start.Environment[name] = value;

        using var process = Process.Start(start) ?? throw new InvalidOperationException("could not start a runner");
        process.StandardInput.Close();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) line(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) line(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(ct);
        return process.ExitCode;
    }
}

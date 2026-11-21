using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Hatch.Cli;

/// <summary>
/// What a downloaded <c>hatch</c> does to supervise its own loop: runs its own
/// file again as the child, with a state file of its own, and starts it again
/// whenever it exits 75.
/// </summary>
/// <remarks>
/// <para>The same job <c>supervise_go_to_work</c> in <c>scripts/hatch.sh</c> and
/// <c>Invoke-GoToWork</c> in <c>scripts/hatch.ps1</c> do for a checkout, with
/// the rebuild left out: a download has no source beside it to rebuild. Nothing
/// is backgrounded, the child owns the terminal, and the state is removed on
/// every way out that the launcher controls.</para>
///
/// <para>The launcher touches no settings and calls no board. It reads nothing
/// from standard input and writes nothing to the console while a child runs,
/// which is what keeps the footer, the keys and the colour the child's.</para>
/// </remarks>
public static class Launcher
{
    /// <summary>
    /// One whole number, starting at 1. It goes up when the launcher learns to
    /// do something a child or the board should know about, and never when it
    /// merely changes inside.
    /// </summary>
    public const int Version = 1;

    /// <summary>Where the launcher says which version it is. Set by the launcher and by no person.</summary>
    public const string VersionVariable = "HATCH_LAUNCHER_VERSION";

    /// <summary>Where the launcher says which file it is. Set by the launcher and by no person.</summary>
    public const string PathVariable = "HATCH_LAUNCHER_PATH";

    /// <summary>The state file's variable: the one a supervisor sets, and a launcher is one.</summary>
    public const string StateVariable = "HATCH_NIGHT_STATE";

    /// <summary>The assembly metadata a download is stamped with.</summary>
    public const string MetadataKey = "HatchLauncher";

    /// <summary>How long after the first signal a child counts another as the same one, in milliseconds.</summary>
    public const long RepeatWindowMs = 2000;

    /// <summary>
    /// Whether a signal arriving now is a repeat of the one already caught, to be
    /// swallowed: only under a launcher, and only within two seconds. A person
    /// pressing it again later still ends the loop, as without a launcher.
    /// </summary>
    public static bool IsRepeat(LauncherLink? link, long firstAtMs, long nowMs) =>
        link is not null && nowMs - firstAtMs < RepeatWindowMs;

    /// <summary>The file the launcher's state directory holds. A set's members write <c>night.json.&lt;n&gt;</c> beside it.</summary>
    public const string StateFileName = "night.json";

    /// <summary>
    /// Whether a build was stamped as a download: <c>-p:HatchLauncher=true</c>
    /// on a publish, read back the way <see cref="CliRevision"/> reads the
    /// sequence. A build nobody stamped never launches, so a publish route that
    /// forgot the flag fails safe, as today's loop.
    /// </summary>
    public static bool BuildLaunches(Assembly assembly) =>
        assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Any(a => a.Key == MetadataKey && string.Equals(a.Value, "true", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether this process becomes a launcher: only <c>go-to-work</c> and
    /// <c>do-my-work</c>, only when they are not asking for usage, only on a
    /// stamped build, and only with nothing above it - <c>HATCH_NIGHT_STATE</c>
    /// unset or blank. That last check covers <c>scripts/hatch.sh</c>,
    /// <c>scripts/hatch.ps1</c>, <c>HATCH_RUNNER_BIN</c> under them, and every
    /// member of a <c>--runners</c> set.
    /// </summary>
    public static bool Applies(
        string command, string[] rest, IReadOnlyDictionary<string, string?> environment, bool builtAsLauncher)
    {
        if (!builtAsLauncher) return false;
        if (command is not ("go-to-work" or "do-my-work")) return false;
        if (!string.IsNullOrWhiteSpace(environment.GetValueOrDefault(StateVariable))) return false;

        // `--runners 3 -h` asks for usage as much as `-h` does.
        var asked = RunnerSet.TryExtract(rest, out var without, out _, out _) ? without : rest;
        return !Usage.Wanted(asked);
    }

    /// <summary>
    /// Runs the child until it ends for any reason but a restart, and answers
    /// what it exited with.
    /// </summary>
    /// <param name="args">The arguments this process was started with, whole, command word first.</param>
    /// <param name="starter">Starts the child.</param>
    /// <param name="ownPath">This file, handed to the child as <see cref="PathVariable"/>.</param>
    /// <param name="signals">Where an interrupt, a terminate or a hang-up arrives.</param>
    /// <param name="forwards">Whether the launcher passes the first signal on. Where the console reaches the child itself, it does not.</param>
    /// <param name="version">The version told to the child; a test passes an older one.</param>
    /// <param name="tempRoot">Where the state directory is made.</param>
    /// <param name="say">The one line the launcher says when it cannot start its child.</param>
    public static async Task<int> RunAsync(
        string[] args, ILaunchedChild starter, string ownPath, LaunchSignals signals, bool forwards,
        int version, string tempRoot, Action<string> say)
    {
        var directory = Path.Combine(tempRoot, "hatch-night-" + Guid.NewGuid().ToString("N"));

        try
        {
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                say($"hatch: could not make a place for the night's state under {tempRoot}: {e.Message}");
                return 1;
            }

            var state = Path.Combine(directory, StateFileName);

            var environment = new Dictionary<string, string>
            {
                [StateVariable] = state,
                [VersionVariable] = version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                [PathVariable] = ownPath,
            };

            var received = 0;
            var first = PosixSignal.SIGTERM;
            ILaunchedProcess? running = null;
            var gate = new object();

            void OnSignal(PosixSignal signal)
            {
                ILaunchedProcess? target;
                lock (gate)
                {
                    // The first is passed on and the rest are not: a terminal's
                    // Ctrl-C reaches child and launcher together, so a second
                    // copy would end the child that is letting go of a claim.
                    if (Interlocked.Increment(ref received) > 1) return;
                    first = signal;
                    target = running;
                }

                if (target is null) return;

                if (forwards) target.Forward(signal);
                else if (signal == PosixSignal.SIGHUP) target.WaitForExit(TimeSpan.FromSeconds(4));
            }

            signals.Caught += OnSignal;

            try
            {
                while (true)
                {
                    int code;

                    ILaunchedProcess process;
                    try
                    {
                        process = starter.Start(args, environment);
                    }
                    catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
                    {
                        say($"hatch: could not start {ownPath}: {e.Message}");
                        return 1;
                    }

                    using (process)
                    {
                        lock (gate) running = process;

                        // A signal that arrived while there was nothing to pass it to.
                        if (Volatile.Read(ref received) > 0 && forwards) process.Forward(first);

                        code = await process.Exited;

                        lock (gate) running = null;
                    }

                    if (code != GoToWorkCommand.RestartExitCode) return code;

                    // A signal ends the night even where the child asked to come back.
                    if (Volatile.Read(ref received) > 0) return 0;
                }
            }
            finally
            {
                signals.Caught -= OnSignal;
            }
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>The real launcher, over the real process and the real signals.</summary>
    public static async Task<int> RunProcessAsync(string[] args, Action<string> say)
    {
        var path = Environment.ProcessPath ?? throw new InvalidOperationException("the path of this program is unknown");
        using var signals = LaunchSignals.Register();

        return await RunAsync(
            args, new ProcessLaunchedChild(path), path, signals, forwards: !OperatingSystem.IsWindows(), Version,
            Path.GetTempPath(), say);
    }
}

/// <summary>What the launcher starts its child through, so a test can stand in for a process.</summary>
public interface ILaunchedChild
{
    /// <summary>Starts the child with these arguments and, added to its own environment, these variables.</summary>
    ILaunchedProcess Start(string[] args, IReadOnlyDictionary<string, string> environment);
}

/// <summary>One child, started.</summary>
public interface ILaunchedProcess : IDisposable
{
    /// <summary>Completes with the child's exit code.</summary>
    Task<int> Exited { get; }

    /// <summary>Sends the child this signal, where the platform lets one process send it to another. Never ends the child any other way.</summary>
    void Forward(PosixSignal signal);

    /// <summary>Waits a while for the child to end.</summary>
    void WaitForExit(TimeSpan limit);
}

/// <summary>Where the launcher hears a signal. A test raises one; the real one registers the three.</summary>
public sealed class LaunchSignals : IDisposable
{
    private readonly List<PosixSignalRegistration> registrations = [];

    public event Action<PosixSignal>? Caught;

    /// <summary>Hands one to whoever is listening.</summary>
    public void Raise(PosixSignal signal) => Caught?.Invoke(signal);

    /// <summary>
    /// Catches the three that end a night, with <c>Cancel</c> set so the launcher
    /// outlives its child: the child is the one that lets go of a claim, and a
    /// launcher gone first would leave it with no parent and no state file.
    /// </summary>
    public static LaunchSignals Register()
    {
        var signals = new LaunchSignals();

        foreach (var signal in new[] { PosixSignal.SIGINT, PosixSignal.SIGTERM, PosixSignal.SIGHUP })
        {
            try
            {
                signals.registrations.Add(PosixSignalRegistration.Create(signal, context =>
                {
                    context.Cancel = true;
                    signals.Raise(context.Signal);
                }));
            }
            catch (Exception e) when (e is PlatformNotSupportedException or ArgumentOutOfRangeException)
            {
                // Not every signal exists on every platform.
            }
        }

        return signals;
    }

    public void Dispose()
    {
        foreach (var registration in registrations) registration.Dispose();
    }
}

/// <summary>This same file, started again with the terminal inherited.</summary>
public sealed class ProcessLaunchedChild(string program) : ILaunchedChild
{
    public ILaunchedProcess Start(string[] args, IReadOnlyDictionary<string, string> environment)
    {
        // Nothing redirected: the child's keys, footer, colour and width are the
        // terminal's, exactly as for a loop started by hand.
        var start = new ProcessStartInfo { FileName = program, UseShellExecute = false };

        foreach (var a in args) start.ArgumentList.Add(a);
        foreach (var (name, value) in environment) start.Environment[name] = value;

        var process = Process.Start(start) ?? throw new InvalidOperationException("could not start the runner");
        return new LaunchedProcess(process);
    }

    private sealed class LaunchedProcess : ILaunchedProcess
    {
        private readonly Process process;

        public LaunchedProcess(Process process)
        {
            this.process = process;
            Exited = process.WaitForExitAsync().ContinueWith(_ => process.ExitCode, TaskScheduler.Default);
        }

        public Task<int> Exited { get; }

        public void Forward(PosixSignal signal)
        {
            if (OperatingSystem.IsWindows()) return;

            try
            {
                if (!process.HasExited) kill(process.Id, Number(signal));
            }
            catch (Exception e) when (e is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException) { }
        }

        public void WaitForExit(TimeSpan limit)
        {
            try { process.WaitForExit(limit); }
            catch (InvalidOperationException) { }
        }

        public void Dispose() => process.Dispose();

        // The same on Linux and macOS.
        private static int Number(PosixSignal signal) => signal switch
        {
            PosixSignal.SIGHUP => 1,
            PosixSignal.SIGINT => 2,
            _ => 15,
        };

        [DllImport("libc", SetLastError = true)]
        private static extern int kill(int pid, int signal);
    }
}

/// <summary>
/// What a child knows about the launcher above it, read from its environment.
/// Null where there is none: a loop under <c>hatch.sh</c>, or one started by hand.
/// </summary>
public sealed class LauncherLink
{
    private readonly HashSet<string> said = [];

    private LauncherLink(int version, string? path)
    {
        Version = version;
        Path = path;
    }

    /// <summary>The launcher's version, a whole number of 1 or more.</summary>
    public int Version { get; }

    /// <summary>The launcher's own file, when it said.</summary>
    public string? Path { get; }

    /// <summary>
    /// A missing, blank or garbled version is no launcher at all, and says
    /// nothing: that is a loop under a script, not an old launcher.
    /// </summary>
    public static LauncherLink? From(IReadOnlyDictionary<string, string?> environment) =>
        int.TryParse(
            environment.GetValueOrDefault(Launcher.VersionVariable), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var version) && version >= 1
            ? new LauncherLink(version, environment.GetValueOrDefault(Launcher.PathVariable) is { Length: > 0 } path ? path : null)
            : null;

    /// <summary>
    /// Whether the launcher is at least <paramref name="version"/>. Where it is
    /// not, says so once per <paramref name="what"/> and answers false, so the
    /// caller carries on without it. Nothing here exits or throws, whichever
    /// way round the two versions are.
    /// </summary>
    public bool Needs(int version, string what, Action<string> say)
    {
        if (Version >= version) return true;

        if (said.Add(what))
            say($"hatch: the launcher is version {Version}, and {what} needs {version} - carrying on without it");

        return false;
    }
}

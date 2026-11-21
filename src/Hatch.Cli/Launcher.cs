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
    /// merely changes inside. The number itself lives in <c>Hatch.Contracts</c>,
    /// so the board compares against the same one.
    /// </summary>
    public const int Version = Hatch.Contracts.LauncherVersion.Current;

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
    /// Whether this process hands its command on to a stored build: any command
    /// but the loop's, <c>version</c> and <c>inbox</c>, on a stamped build, with
    /// no launcher above it. That last check is what stops a child handing on
    /// again, whichever launcher started it - the loop's or this one - and what
    /// leaves a session's own calls, which already run their loop's build, alone.
    /// The bare <c>hatch</c> and usage never reach here: they are answered first.
    /// </summary>
    public static bool HandsOn(string command, IReadOnlyDictionary<string, string?> environment, bool builtAsLauncher)
    {
        if (!builtAsLauncher) return false;
        if (command is "go-to-work" or "do-my-work" or "version" or "inbox") return false;
        return LauncherLink.From(environment) is null;
    }

    /// <summary>
    /// The stored build a plain command runs on: the one recorded as current for
    /// <paramref name="board"/>, else the newest stored, else null - where the
    /// launcher answers the command itself. Null too where the build chosen is
    /// this launcher's own.
    /// </summary>
    /// <remarks>
    /// Only reads. It never records a build, never installs one and never reaches
    /// the board, so deciding changes nothing on this machine.
    /// <paramref name="note"/> is the one line to say when a build is recorded but
    /// no longer kept; it is null in every other case.
    /// </remarks>
    /// <param name="board">The board the call is aimed at, or null for none.</param>
    /// <param name="store">Where builds are kept.</param>
    /// <param name="ownRevision">The sha this launcher was built from.</param>
    /// <param name="ownPath">This launcher's own file.</param>
    /// <param name="note">What to say on standard error, or null.</param>
    public static string? ChooseBuild(
        LauncherBoard? board, BuildStore store, string ownRevision, string ownPath, out string? note)
    {
        note = null;

        string? chosen;

        if (board is not null && store.Read(board.Origin)?.Current is { } recorded)
        {
            chosen = store.Find(recorded);
            if (chosen is null)
            {
                note = $"hatch: the build recorded for this board ({Short(recorded)}) is not kept; running this build instead";
                return null;
            }
        }
        else
        {
            chosen = store.Newest();
        }

        if (chosen is null) return null;

        var sha = Path.GetFileName(Path.GetDirectoryName(chosen)) ?? "";
        if (string.Equals(sha, ownRevision, StringComparison.OrdinalIgnoreCase)) return null;

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(chosen), Path.GetFullPath(ownPath), comparison) ? null : chosen;
    }

    /// <summary>
    /// Runs one command on <paramref name="program"/>, with the terminal, the
    /// streams and the first signal all the child's, and answers its exit code
    /// unchanged. Answers null where the child could not be started: the caller
    /// answers the command itself, after the one line this said.
    /// </summary>
    /// <remarks>
    /// <para>Nothing is redirected, read or written while the child runs, so a
    /// prompt, a pipe and a footer behave as without a launcher. The child is told
    /// it has a launcher the way the loop's child is - <see cref="VersionVariable"/>
    /// and <see cref="PathVariable"/> - but gets no state file, so nothing takes it
    /// for a night, and it never hands a call on again.</para>
    ///
    /// <para>The launcher never exits before the child: a signal is passed on once
    /// (a second is not, since a terminal's Ctrl-C reaches both), and then waited
    /// for. On Windows the console reaches the child itself, so nothing is passed.
    /// A child a signal ended on Linux or macOS answers 128 plus the signal, the
    /// one thing here that is not passed through exactly.</para>
    ///
    /// <para><paramref name="told"/> replaces the two variables above where a
    /// caller has its own to tell: the launcher that has just replaced itself
    /// starts the new file with <see cref="SelfUpdate.HandoffVariable"/> alone.</para>
    /// </remarks>
    public static async Task<int?> HandOnAsync(
        string[] args, string program, string ownPath, LaunchSignals signals, bool forwards, int version,
        ILaunchedChild starter, Action<string> say, IReadOnlyDictionary<string, string>? told = null)
    {
        var environment = told ?? new Dictionary<string, string>
        {
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
            ILaunchedProcess process;
            try
            {
                process = starter.Start(program, args, environment);
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
            {
                say($"hatch: could not start {program} - {e.Message}; running this build instead");
                return null;
            }

            using (process)
            {
                lock (gate) running = process;

                // A signal that arrived while there was nothing to pass it to.
                if (Volatile.Read(ref received) > 0 && forwards) process.Forward(first);

                var code = await process.Exited;

                lock (gate) running = null;
                return code;
            }
        }
        finally
        {
            signals.Caught -= OnSignal;
        }
    }

    /// <summary>The real hand-off, over the real process and the real signals.</summary>
    public static async Task<int?> HandOnProcessAsync(string[] args, string program, Action<string> say)
    {
        // Not knowing where this file is, there is no launcher to name to the child.
        if (Environment.ProcessPath is not { } path) return null;

        // Disposed on the way out, so a command the launcher answers itself has
        // its own Ctrl-C back.
        using var signals = LaunchSignals.Register();

        return await HandOnAsync(
            args, program, path, signals, forwards: !OperatingSystem.IsWindows(), Version,
            new ProcessLaunchedChild(), say);
    }

    /// <summary>
    /// Runs the child until it ends for any reason but a restart, and answers
    /// what it exited with.
    /// </summary>
    /// <remarks>
    /// <para>With <paramref name="builds"/>, the launcher also moves between
    /// builds. A fresh start runs the build this board's record names, if it is
    /// still kept, and otherwise its own file. When a child exits 75 having left
    /// a request (<see cref="BuildHandoff"/>), the launcher fetches that build,
    /// checks it, and starts the next child from it; once that child's first
    /// beat is answered the build is recorded as current.</para>
    ///
    /// <para>Every way that can go wrong ends the same way: the next child runs
    /// the build the launcher had, with the night's state as it was. Nothing
    /// about a build is ever an exception here, and the launcher says nothing
    /// to the console while a child runs - what it decided goes to the child as
    /// one line, which says it.</para>
    /// </remarks>
    /// <param name="args">The arguments this process was started with, whole, command word first.</param>
    /// <param name="starter">Starts the child.</param>
    /// <param name="ownPath">This file, handed to the child as <see cref="PathVariable"/>.</param>
    /// <param name="signals">Where an interrupt, a terminate or a hang-up arrives.</param>
    /// <param name="forwards">Whether the launcher passes the first signal on. Where the console reaches the child itself, it does not.</param>
    /// <param name="version">The version told to the child; a test passes an older one.</param>
    /// <param name="tempRoot">Where the state directory is made.</param>
    /// <param name="say">The one line the launcher says when it cannot start its child.</param>
    /// <param name="builds">What changing build needs. Null: the launcher only ever runs its own file.</param>
    /// <param name="self">What replacing this launcher's own file needs. Null: the launcher never does.</param>
    public static async Task<int> RunAsync(
        string[] args, ILaunchedChild starter, string ownPath, LaunchSignals signals, bool forwards,
        int version, string tempRoot, Action<string> say, LauncherBuilds? builds = null, SelfUpdate? self = null)
    {
        // First, before anything is made: sweep what an earlier start left
        // aside, and take a stored build that is newer than this file. On
        // Linux and macOS that is this process, in its own place; on Windows
        // it is the new file, started once and waited for.
        LauncherResume? resumed = null;
        string? named = null;

        if (self is not null)
        {
            self.Sweep();

            if (self.Resume is { } resumeDirectory && LauncherResume.Read(resumeDirectory) is { } found)
            {
                resumed = found;
                named = resumeDirectory;
            }
            else if (!self.Handoff && await UpdateAtStartAsync(args, starter, signals, forwards, say, self) is { } code)
            {
                return code;
            }
        }

        var directory = named ?? Path.Combine(tempRoot, "hatch-night-" + Guid.NewGuid().ToString("N"));

        // True once another process has taken this one's place: the night's
        // directory is that one's now, and is not removed.
        var handedOver = false;

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
            using var cancelling = new CancellationTokenSource();

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

                // A download in progress is the only thing this stops.
                try { cancelling.Cancel(); }
                catch (ObjectDisposedException) { }

                if (target is null) return;

                if (forwards) target.Forward(signal);
                else if (signal == PosixSignal.SIGHUP) target.WaitForExit(TimeSpan.FromSeconds(4));
            }

            signals.Caught += OnSignal;

            try
            {
                // The program the next child runs, and the sha it was kept
                // under - null for this launcher's own file.
                var program = ownPath;
                string? programSha = null;

                if (builds is not null
                    && BoardOf(builds) is { } startBoard
                    && builds.Store.Read(startBoard.Origin)?.Current is { } recorded
                    && builds.Store.Find(recorded) is { } kept)
                {
                    program = kept;
                    programSha = recorded;
                }

                // The last program that is known to have worked: where a build
                // that did not goes back to. Where nothing is on trial, it is
                // the program itself.
                var confirmed = (Program: program, Sha: programSha);

                // A build the board has not yet answered the first beat of,
                // the board it is for, and the night's state as it stood
                // before it, because a child that ends without restarting
                // deletes it.
                string? trial = null;
                string? trialOrigin = null;
                Dictionary<string, byte[]>? aside = null;

                var hold = new BuildHold();
                var failed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                string? note = null;

                // A launcher that replaced itself in the middle of a night left
                // all of the above behind: the next child starts as it would have.
                if (resumed is not null)
                {
                    program = resumed.Program;
                    programSha = resumed.ProgramSha;
                    confirmed = (resumed.ConfirmedProgram, resumed.ConfirmedSha);
                    trial = resumed.Trial;
                    trialOrigin = resumed.TrialOrigin;
                    aside = resumed.ReadAside(directory);
                    hold = BuildHold.Parse(resumed.Hold);
                    foreach (var (revision, times) in resumed.ParseFailed()) failed[revision] = times;

                    LauncherResume.Remove(directory);
                }

                while (true)
                {
                    int code;

                    var told = new Dictionary<string, string>(environment);
                    if (builds is not null && hold.Format(builds.Clock.GetUtcNow()) is { } held)
                        told[BuildHandoff.HoldVariable] = held;
                    if (note is not null) told[BuildHandoff.NoteVariable] = note;
                    note = null;

                    BuildHandoff.Clear(directory);

                    ILaunchedProcess process;
                    try
                    {
                        process = starter.Start(program, args, told);
                    }
                    catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
                    {
                        // A build that will not even start is as bad as one that
                        // ends before it is heard from: go back, never ask again.
                        if (builds is not null && program != ownPath)
                        {
                            if (trial is not null)
                            {
                                if (aside is not null) RestoreState(directory, aside);
                                hold.Never(trial);
                                note = $"hatch: build {Short(trial)} could not be started - {e.Message}; carrying on with the build it had";
                                (program, programSha) = confirmed;
                                trial = null;
                                aside = null;
                            }
                            else
                            {
                                note = $"hatch: could not start {program} - {e.Message}; carrying on with the launcher's own build";
                                program = ownPath;
                                programSha = null;
                                confirmed = (program, programSha);
                            }

                            continue;
                        }

                        say($"hatch: could not start {program}: {e.Message}");
                        return 1;
                    }

                    using (process)
                    {
                        lock (gate) running = process;

                        // A signal that arrived while there was nothing to pass it to.
                        if (Volatile.Read(ref received) > 0 && forwards) process.Forward(first);

                        var exited = process.Exited;

                        // The build is current the moment its first beat is
                        // answered, not when the child ends: a night may run
                        // for hours, and a launcher that is stopped by hand
                        // must not forget what it was heard on.
                        bool Heard()
                        {
                            if (trial is null) return true;
                            if (BuildHandoff.Answered(directory) != trial) return false;

                            builds!.Store.Record(trialOrigin!, trial);
                            confirmed = (program, programSha);
                            trial = null;
                            trialOrigin = null;
                            aside = null;
                            return true;
                        }

                        // Beside the child's life, look for that answer.
                        while (trial is not null && builds is not null && !Heard())
                        {
                            using var tick = new CancellationTokenSource();
                            var wait = Task.Delay(builds.Poll, tick.Token);
                            if (await Task.WhenAny(exited, wait) == exited)
                            {
                                tick.Cancel();
                                break;
                            }
                        }

                        code = await exited;

                        lock (gate) running = null;

                        // Once more after it ended: a child that answered and
                        // exited between two looks has still answered.
                        Heard();
                    }

                    // A new build that ended before it was heard from, any way
                    // but a restart, a clean end or a loop that cannot sign (the machine's
                    // answer, which no build changes), is a build that does not work.
                    // A signal's exit is not the build's fault.
                    if (trial is not null && code is not (0 or GoToWorkCommand.RestartExitCode or GoToWorkCommand.CannotSignExitCode) && Volatile.Read(ref received) == 0)
                    {
                        if (aside is not null) RestoreState(directory, aside);
                        hold.Never(trial);
                        note = $"hatch: build {Short(trial)} ended with exit {code} before the board answered it; carrying on with the build it had";
                        (program, programSha) = confirmed;
                        trial = null;
                        aside = null;
                        continue;
                    }

                    if (code != GoToWorkCommand.RestartExitCode) return code;

                    // A signal ends the night even where the child asked to come back.
                    if (Volatile.Read(ref received) > 0) return 0;

                    if (builds is null || BuildHandoff.Take(directory) is not { } request) continue;

                    // Not for this board, held off, or the one already running:
                    // nothing to say, and nothing to do.
                    if (BoardOf(builds) is not { } board
                        || !SameOrigin(request.Origin, board.Origin)
                        || hold.Holds(request.Revision, builds.Clock.GetUtcNow())
                        || string.Equals(request.Revision, programSha, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string? stored = null;
                    string? why;

                    try
                    {
                        Download? download = null;

                        var result = await builds.Store.InstallAsync(
                            request.Revision,
                            async destination =>
                            {
                                download = await builds.Fetch.DownloadAsync(board, request.Download, destination, cancelling.Token);
                                return download.Value.Failure;
                            },
                            async path =>
                                BuildCheck.Header(download?.Revision, request.Revision)
                                ?? BuildCheck.Sha(download?.Sha256 ?? "", request.Sha256)
                                ?? await builds.Verify(path, request.Revision),
                            cancelling.Token);

                        stored = result.Path;
                        why = result.Failure ?? (result.Path is null ? "it could not be kept" : null);
                    }
                    catch (OperationCanceledException) when (cancelling.IsCancellationRequested)
                    {
                        return 0;
                    }
                    catch (Exception e)
                    {
                        why = e.Message;
                    }

                    // A signal during the install ends the night, and no child starts.
                    if (Volatile.Read(ref received) > 0) return 0;

                    if (why is null && stored is not null)
                    {
                        if (trial is null) aside = SetAside(directory);

                        program = stored;
                        programSha = request.Revision;
                        trial = request.Revision;
                        trialOrigin = board.Origin;

                        // A launcher older than the build it has just stored becomes
                        // it. Windows carries on as it was and the next start runs
                        // the new file; elsewhere this process is replaced by it.
                        if (self is not null
                            && await self.JustStoredAsync(stored, request.Revision) is { } newer
                            && Volatile.Read(ref received) == 0
                            && await self.SwapAsync(newer))
                        {
                            self.Updated(newer);

                            if (!self.RenameAside && Volatile.Read(ref received) == 0)
                            {
                                var resume = new LauncherResume(
                                    program, programSha, confirmed.Program, confirmed.Sha, trial, trialOrigin,
                                    hold.Format(builds.Clock.GetUtcNow()), LauncherResume.FormatFailed(failed),
                                    HasAside: false);

                                if (LauncherResume.Write(directory, resume, aside, out var execFailure))
                                {
                                    var added = new Dictionary<string, string>
                                    {
                                        [SelfUpdate.HandoffVariable] = "1",
                                        [SelfUpdate.ResumeVariable] = directory,
                                    };

                                    if (self.Exec.Replace(ownPath, args, added, out execFailure))
                                    {
                                        handedOver = true;
                                        return 0;
                                    }

                                    LauncherResume.Remove(directory);
                                }

                                self.Refuse($"it could not start the new build in its own place: {execFailure}");
                            }
                        }

                        continue;
                    }

                    var times = failed[request.Revision] = failed.GetValueOrDefault(request.Revision) + 1;
                    var backoff = BuildHold.Backoff(times);
                    hold.Until(request.Revision, builds.Clock.GetUtcNow() + backoff);

                    note = $"hatch: could not move to build {Short(request.Revision)} - {Sentence(why)}; "
                        + $"carrying on with the build it has, asking again in {BuildHold.Words(backoff)}";
                }
            }
            finally
            {
                signals.Caught -= OnSignal;
            }
        }
        finally
        {
            if (!handedOver)
            {
                try { Directory.Delete(directory, recursive: true); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>
    /// The start-of-night check: when a stored build is newer than this file,
    /// makes the file that build and carries on as it. Answers the exit code
    /// where Windows started the new file and waited for it, and where Linux or
    /// macOS handed this process over (only a fake's exec says so); null where
    /// this process carries on, having changed nothing or being unable to start
    /// the new file.
    /// </summary>
    private static async Task<int?> UpdateAtStartAsync(
        string[] args, ILaunchedChild starter, LaunchSignals signals, bool forwards, Action<string> say,
        SelfUpdate self)
    {
        if (await self.FindNewerAsync() is not { } newer) return null;
        if (!await self.SwapAsync(newer)) return null;

        self.Updated(newer);

        var handoff = new Dictionary<string, string> { [SelfUpdate.HandoffVariable] = "1" };

        if (!self.RenameAside)
        {
            if (self.Exec.Replace(self.OwnPath, args, handoff, out var why)) return 0;

            self.Refuse($"it could not start the new build in its own place: {why}");
            return null;
        }

        // Windows: this running file was renamed aside, and the new one is at
        // the path. It is started once, and this process only waits for it.
        return await HandOnAsync(
            args, self.OwnPath, self.OwnPath, signals, forwards: false, Version, starter, say, handoff);
    }

    /// <summary>The board this launcher is for just now. Never throws: a resolver that cannot say is no board.</summary>
    private static LauncherBoard? BoardOf(LauncherBuilds builds)
    {
        try { return builds.Board(); }
        catch (Exception e) when (e is not OutOfMemoryException) { return null; }
    }

    private static bool SameOrigin(string a, string b) =>
        string.Equals(a.Trim().TrimEnd('/'), b.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    private static string Short(string sha) => sha.Length > 8 ? sha[..8] : sha;

    /// <summary>A failure's own words, without the <c>hatch: </c> every one of them starts with, since the note brings its own.</summary>
    private static string Sentence(string? failure)
    {
        var text = (failure ?? "").Trim();
        return text.StartsWith("hatch: ", StringComparison.Ordinal) ? text["hatch: ".Length..] : text;
    }

    /// <summary>
    /// The bytes of the night's state and of every member's beside it, held in
    /// memory. Null where they cannot be read, which only means a failed build
    /// cannot put them back.
    /// </summary>
    private static Dictionary<string, byte[]>? SetAside(string directory)
    {
        try
        {
            var kept = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var file in Directory.GetFiles(directory, StateFileName + "*"))
                kept[Path.GetFileName(file)] = File.ReadAllBytes(file);
            return kept;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Puts the state back as <see cref="SetAside"/> found it, whatever the child that ended since did to it.</summary>
    private static void RestoreState(string directory, Dictionary<string, byte[]> aside)
    {
        try
        {
            foreach (var file in Directory.GetFiles(directory, StateFileName + "*")) File.Delete(file);
            foreach (var (name, bytes) in aside) File.WriteAllBytes(Path.Combine(directory, name), bytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The real launcher, over the real process and the real signals.</summary>
    public static async Task<int> RunProcessAsync(string[] args, Action<string> say, Func<LauncherBoard?>? board = null)
    {
        var path = Environment.ProcessPath ?? throw new InvalidOperationException("the path of this program is unknown");
        using var signals = LaunchSignals.Register();

        var builds = board is null ? null : LauncherBuilds.Real(board);
        var self = builds is null ? null : SelfUpdate.Real(new CliRevision(), path, builds.Store, say);

        return await RunAsync(
            args, new ProcessLaunchedChild(), path, signals, forwards: !OperatingSystem.IsWindows(), Version,
            Path.GetTempPath(), say, builds, self);
    }
}

/// <summary>What the launcher starts its child through, so a test can stand in for a process.</summary>
public interface ILaunchedChild
{
    /// <summary>Starts <paramref name="program"/> with these arguments and, added to its own environment, these variables.</summary>
    ILaunchedProcess Start(string program, string[] args, IReadOnlyDictionary<string, string> environment);
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

/// <summary>A program, started with the terminal inherited.</summary>
public sealed class ProcessLaunchedChild : ILaunchedChild
{
    public ILaunchedProcess Start(string program, string[] args, IReadOnlyDictionary<string, string> environment)
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
    /// The one line a child says at the start of a night when its launcher is
    /// older than the child's own build, or null when it is level or newer.
    /// Pure: it says nothing itself, so the caller decides when.
    /// </summary>
    /// <param name="own">The launcher version of this build.</param>
    public string? StaleLine(int own) =>
        Version >= own
            ? null
            : $"hatch: the launcher at {Path ?? "(path unknown)"} is version {Version} and this build is {own} - download hatch again from the Runner page and start it again to bring it up to date";

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

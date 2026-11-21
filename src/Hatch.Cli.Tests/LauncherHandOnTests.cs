using System.Runtime.InteropServices;

namespace Hatch.Cli.Tests;

/// <summary>
/// A plain command run outside a loop: which commands are handed on, which
/// build they go to, and what the launcher does while the child runs.
/// </summary>
public sealed class LauncherHandOnTests : IDisposable
{
    private const string Own = "/somewhere/hatch";
    private const string Origin = "https://board.example";

    private static readonly string A = new('a', 40);
    private static readonly string B = new('b', 40);
    private static readonly string C = new('c', 40);

    private static readonly DateTime Early = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly string root = Directory.CreateTempSubdirectory("hatch-handon-store-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); }
        catch (IOException) { }
    }

    private static IReadOnlyDictionary<string, string?> Env(params (string Name, string? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Name, p => p.Value);

    private static LauncherBoard Board(string origin = Origin) => new(origin, null!);

    private string Keep(string sha, DateTime? written = null)
    {
        var directory = Path.Combine(root, "versions", sha);
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, BuildStore.BinaryName);
        File.WriteAllText(file, sha);
        if (written is { } at) File.SetLastWriteTimeUtc(file, at);
        return file;
    }

    // ---- Which commands are handed on ----

    [Theory]
    [InlineData("board")]
    [InlineData("show")]
    [InlineData("work")]
    [InlineData("answer")]
    [InlineData("config")]
    [InlineData("standby")]
    [InlineData("api")]
    public void A_plain_command_on_a_stamped_build_with_no_launcher_above_it_is_handed_on(string command)
    {
        Assert.True(Launcher.HandsOn(command, Env(), builtAsLauncher: true));
        Assert.True(Launcher.HandsOn(command, Env((Launcher.VersionVariable, "")), builtAsLauncher: true));
        Assert.True(Launcher.HandsOn(command, Env((Launcher.VersionVariable, "nonsense")), builtAsLauncher: true));
    }

    [Theory]
    [InlineData("go-to-work")]
    [InlineData("do-my-work")]
    [InlineData("version")]
    [InlineData("inbox")]
    public void The_loops_commands_version_and_inbox_stay_where_they_are(string command)
    {
        Assert.False(Launcher.HandsOn(command, Env(), builtAsLauncher: true));
    }

    [Fact]
    public void A_build_nobody_stamped_never_hands_on()
    {
        Assert.False(Launcher.HandsOn("board", Env(), builtAsLauncher: false));
    }

    [Fact]
    public void A_child_never_hands_a_call_on_again()
    {
        Assert.False(Launcher.HandsOn("board", Env((Launcher.VersionVariable, "1")), builtAsLauncher: true));
        Assert.False(
            Launcher.HandsOn("work", Env((Launcher.VersionVariable, "1"), (Launcher.PathVariable, Own)), builtAsLauncher: true));
    }

    // ---- Which build ----

    [Fact]
    public void The_build_recorded_for_the_board_runs_even_when_a_newer_one_is_kept()
    {
        var recorded = Keep(A, Early);
        Keep(B, Early.AddDays(5));
        var store = new BuildStore(root);
        Assert.True(store.Record(Origin, A));

        var chosen = Launcher.ChooseBuild(Board(), store, C, Own, out var note);

        Assert.Equal(recorded, chosen);
        Assert.Null(note);
    }

    [Fact]
    public void A_board_with_no_record_and_a_call_with_no_board_run_the_newest_kept_build()
    {
        Keep(A, Early);
        var newest = Keep(B, Early.AddDays(5));
        var store = new BuildStore(root);
        Assert.True(store.Record("https://another.example", A));

        Assert.Equal(newest, Launcher.ChooseBuild(Board(), store, C, Own, out var note));
        Assert.Null(note);
        Assert.Equal(newest, Launcher.ChooseBuild(null, store, C, Own, out note));
        Assert.Null(note);
    }

    [Fact]
    public void A_recorded_build_that_is_not_kept_answers_null_with_one_line()
    {
        Keep(A, Early);
        var store = new BuildStore(root);
        Assert.True(store.Record(Origin, B));

        var chosen = Launcher.ChooseBuild(Board(), store, C, Own, out var note);

        Assert.Null(chosen);
        Assert.NotNull(note);
        Assert.Contains(B[..8], note, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', note);
    }

    [Fact]
    public void Nothing_kept_answers_null_and_says_nothing()
    {
        Assert.Null(Launcher.ChooseBuild(Board(), new BuildStore(root), C, Own, out var note));
        Assert.Null(note);
        Assert.Null(Launcher.ChooseBuild(null, new BuildStore(root), C, Own, out note));
        Assert.Null(note);
    }

    [Fact]
    public void A_build_that_is_the_launchers_own_answers_null_and_says_nothing()
    {
        var kept = Keep(A, Early);
        var store = new BuildStore(root);
        Assert.True(store.Record(Origin, A));

        Assert.Null(Launcher.ChooseBuild(Board(), store, A.ToUpperInvariant(), Own, out var note));
        Assert.Null(note);
        Assert.Null(Launcher.ChooseBuild(null, store, C, kept, out note));
        Assert.Null(note);
    }

    [Fact]
    public void Choosing_changes_nothing_on_disk()
    {
        Keep(A, Early);
        Keep(B, Early.AddDays(2));
        var store = new BuildStore(root);
        Assert.True(store.Record(Origin, A));

        var before = Snapshot();
        Launcher.ChooseBuild(Board(), store, C, Own, out _);
        Launcher.ChooseBuild(null, store, C, Own, out _);
        Launcher.ChooseBuild(Board("https://another.example"), store, C, Own, out _);

        Assert.Equal(before, Snapshot());
    }

    private List<string> Snapshot() =>
        Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(p => File.Exists(p)
                ? $"{p}|{Convert.ToHexString(File.ReadAllBytes(p))}|{File.GetLastWriteTimeUtc(p).Ticks}"
                : p)
            .ToList();

    // ---- A session on a handed-on build ----

    [Fact]
    public void A_work_session_run_on_a_kept_build_puts_that_build_on_its_path_and_in_its_hooks()
    {
        // The child is the kept build itself, so its own process path is the one
        // under versions/: the directory a session's PATH names and the binary
        // its hooks run are that build's, not the launcher's.
        var kept = Path.Combine(root, "versions", A, BuildStore.BinaryName);

        Assert.Equal(Path.GetDirectoryName(kept), Reach.OwnDirectory(kept));
        Assert.Equal(kept, SessionHooks.Binary(kept));
    }

    // ---- The hand-off ----

    private sealed record Began(string Program, string[] Args, IReadOnlyDictionary<string, string> Env);

    private sealed class Running(Task<int> exited) : ILaunchedProcess
    {
        public List<PosixSignal> Forwarded { get; } = [];

        public int Waited;

        public Task<int> Exited => exited;

        public void Forward(PosixSignal signal) => Forwarded.Add(signal);

        public void WaitForExit(TimeSpan limit) => Interlocked.Increment(ref Waited);

        public void Dispose() { }
    }

    private sealed class Child(Func<Task<int>> life) : ILaunchedChild
    {
        public List<Began> Starts { get; } = [];

        public Running? Process { get; private set; }

        public Exception? Throws { get; init; }

        public ILaunchedProcess Start(string program, string[] args, IReadOnlyDictionary<string, string> environment)
        {
            Starts.Add(new Began(program, args, new Dictionary<string, string>(environment)));
            if (Throws is not null) throw Throws;
            return Process = new Running(life());
        }
    }

    private static Child Exits(int code) => new(() => Task.FromResult(code));

    private static Task<int?> HandOn(
        Child child, string[]? args = null, LaunchSignals? signals = null, bool forwards = true,
        Action<string>? say = null) =>
        Launcher.HandOnAsync(
            args ?? ["board", "--profile", "night"], "/kept/hatch", Own, signals ?? new LaunchSignals(), forwards,
            Launcher.Version, child, say ?? (_ => { }));

    [Fact]
    public async Task The_child_gets_the_same_arguments_and_only_the_two_launcher_variables()
    {
        var child = Exits(0);

        await HandOn(child, ["answer", "HA-1", "--profile", "night", "yes"]);

        var started = Assert.Single(child.Starts);
        Assert.Equal("/kept/hatch", started.Program);
        Assert.Equal(["answer", "HA-1", "--profile", "night", "yes"], started.Args);
        Assert.Equal(
            [Launcher.PathVariable, Launcher.VersionVariable],
            started.Env.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(Own, started.Env[Launcher.PathVariable]);
        Assert.Equal(Launcher.Version.ToString(), started.Env[Launcher.VersionVariable]);
        Assert.DoesNotContain(Launcher.StateVariable, started.Env.Keys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(75)]
    [InlineData(130)]
    public async Task The_exit_code_comes_back_unchanged_and_75_is_not_a_restart(int code)
    {
        var child = Exits(code);

        Assert.Equal(code, await HandOn(child));
        Assert.Single(child.Starts);
    }

    [Fact]
    public async Task A_start_that_throws_answers_null_says_one_line_and_the_next_call_still_works()
    {
        var said = new List<string>();
        var broken = new Child(() => Task.FromResult(0))
        {
            Throws = new System.ComponentModel.Win32Exception("not a valid program"),
        };

        Assert.Null(await HandOn(broken, say: said.Add));

        var line = Assert.Single(said);
        Assert.Contains("/kept/hatch", line, StringComparison.Ordinal);
        Assert.Contains("not a valid program", line, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', line);

        Assert.Equal(3, await HandOn(Exits(3)));
    }

    [Fact]
    public async Task A_build_that_starts_and_then_fails_is_the_commands_own_result()
    {
        var said = new List<string>();

        Assert.Equal(1, await HandOn(Exits(1), say: said.Add));
        Assert.Empty(said);
    }

    [Fact]
    public async Task The_first_signal_is_passed_on_once_and_the_launcher_waits_for_the_child()
    {
        var release = new TaskCompletionSource<int>();
        var child = new Child(() => release.Task);
        var signals = new LaunchSignals();

        var run = HandOn(child, signals: signals);
        signals.Raise(PosixSignal.SIGINT);
        signals.Raise(PosixSignal.SIGINT);
        signals.Raise(PosixSignal.SIGTERM);

        Assert.False(run.IsCompleted, "the launcher ended before its child did");
        Assert.Equal([PosixSignal.SIGINT], child.Process!.Forwarded);

        release.SetResult(130);
        Assert.Equal(130, await run);
    }

    [Fact]
    public async Task Where_the_console_reaches_the_child_itself_nothing_is_passed_on_but_a_hangup_is_waited_out()
    {
        var release = new TaskCompletionSource<int>();
        var child = new Child(() => release.Task);
        var signals = new LaunchSignals();

        var run = HandOn(child, signals: signals, forwards: false);
        signals.Raise(PosixSignal.SIGHUP);

        Assert.Empty(child.Process!.Forwarded);
        Assert.Equal(1, child.Process.Waited);

        release.SetResult(0);
        Assert.Equal(0, await run);
    }

    [Fact]
    public async Task The_launcher_stops_listening_for_signals_once_the_child_has_ended()
    {
        var signals = new LaunchSignals();
        var child = Exits(0);

        await HandOn(child, signals: signals);
        signals.Raise(PosixSignal.SIGINT);

        Assert.Empty(child.Process!.Forwarded);
    }
}

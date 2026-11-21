using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace Hatch.Cli.Tests;

/// <summary>
/// What a downloaded build does to supervise its own loop: when it becomes a
/// launcher, what it hands the child, when it starts the child again, and what
/// it leaves behind.
/// </summary>
public sealed class LauncherTests : IDisposable
{
    private const string Own = "/somewhere/hatch";

    private readonly string temp = Directory.CreateTempSubdirectory("hatch-launcher-test-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(temp, recursive: true); }
        catch (IOException) { }
    }

    private string[] Leftovers() => Directory.GetFileSystemEntries(temp);

    private static IReadOnlyDictionary<string, string?> Env(params (string Name, string? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Name, p => p.Value);

    // ---- The stamp ----

    private static Assembly Built(params (string Key, string Value)[] metadata)
    {
        var name = new AssemblyName("Stamped" + Guid.NewGuid().ToString("N"));
        var builder = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
        var constructor = typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!;

        foreach (var (key, value) in metadata)
            builder.SetCustomAttribute(new CustomAttributeBuilder(constructor, [key, value]));

        return builder;
    }

    [Fact]
    public void A_build_stamped_as_a_download_launches()
    {
        Assert.True(Launcher.BuildLaunches(Built(("HatchLauncher", "true"))));
    }

    [Fact]
    public void A_build_nobody_stamped_never_launches()
    {
        Assert.False(Launcher.BuildLaunches(Built()));
        Assert.False(Launcher.BuildLaunches(Built(("HatchSequence", "4127"))));
        Assert.False(Launcher.BuildLaunches(Built(("HatchLauncher", "false"))));
        Assert.False(Launcher.BuildLaunches(typeof(LauncherTests).Assembly));
    }

    // ---- When this process becomes one ----

    [Theory]
    [InlineData("go-to-work", new string[0])]
    [InlineData("do-my-work", new string[0])]
    [InlineData("go-to-work", new[] { "--max-runs", "3", "--until", "08:00" })]
    [InlineData("go-to-work", new[] { "--runners", "3" })]
    [InlineData("do-my-work", new[] { "--runners", "2", "--once" })]
    public void A_stamped_loop_with_nothing_above_it_becomes_a_launcher(string command, string[] rest)
    {
        Assert.True(Launcher.Applies(command, rest, Env(), builtAsLauncher: true));
        Assert.True(Launcher.Applies(command, rest, Env((Launcher.StateVariable, "")), builtAsLauncher: true));
        Assert.True(Launcher.Applies(command, rest, Env((Launcher.StateVariable, "  ")), builtAsLauncher: true));
    }

    [Theory]
    [InlineData("go-to-work", "-h")]
    [InlineData("go-to-work", "--help")]
    [InlineData("do-my-work", "-h")]
    [InlineData("do-my-work", "--help")]
    public void Asking_for_usage_starts_no_launcher(string command, string flag)
    {
        Assert.False(Launcher.Applies(command, [flag], Env(), builtAsLauncher: true));
        Assert.False(Launcher.Applies(command, ["--runners", "2", flag], Env(), builtAsLauncher: true));
    }

    [Theory]
    [InlineData("work")]
    [InlineData("board")]
    [InlineData("use")]
    [InlineData("config")]
    [InlineData("queue")]
    public void Only_the_two_loops_become_a_launcher(string command)
    {
        Assert.False(Launcher.Applies(command, [], Env(), builtAsLauncher: true));
    }

    [Fact]
    public void A_build_nobody_stamped_never_becomes_one()
    {
        Assert.False(Launcher.Applies("go-to-work", [], Env(), builtAsLauncher: false));
    }

    [Fact]
    public void A_loop_already_under_something_never_becomes_one()
    {
        // scripts/hatch.sh, scripts/hatch.ps1, HATCH_RUNNER_BIN under them, and every member of a set.
        Assert.False(Launcher.Applies("go-to-work", [], Env((Launcher.StateVariable, "/tmp/night.json")), builtAsLauncher: true));
        Assert.False(Launcher.Applies("do-my-work", [], Env((Launcher.StateVariable, "/tmp/night.json.2")), builtAsLauncher: true));
    }

    // ---- Running the child ----

    private sealed class FakeProcess(Func<FakeProcess, Task<int>> run) : ILaunchedProcess
    {
        public List<PosixSignal> Forwarded { get; } = [];

        public TaskCompletionSource<int> Ends { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<int> Exited { get; private set; } = Task.FromResult(0);

        public void Begin() => Exited = run(this);

        public void Forward(PosixSignal signal) => Forwarded.Add(signal);

        public void WaitForExit(TimeSpan limit) { }

        public void Dispose() { }
    }

    private sealed class FakeChild : ILaunchedChild
    {
        public List<(string[] Args, IReadOnlyDictionary<string, string> Env)> Starts { get; } = [];

        public List<FakeProcess> Processes { get; } = [];

        /// <summary>What each child does, in order; the last one is repeated.</summary>
        public List<Func<FakeProcess, Task<int>>> Lives { get; } = [];

        public Action<string[], IReadOnlyDictionary<string, string>>? OnStart { get; set; }

        public ILaunchedProcess Start(string[] args, IReadOnlyDictionary<string, string> environment)
        {
            Starts.Add((args, new Dictionary<string, string>(environment)));
            OnStart?.Invoke(args, environment);

            var life = Lives[Math.Min(Starts.Count - 1, Lives.Count - 1)];
            var process = new FakeProcess(life);
            Processes.Add(process);
            process.Begin();
            return process;
        }
    }

    private static Func<FakeProcess, Task<int>> Exits(int code) => _ => Task.FromResult(code);

    private Task<int> Run(
        FakeChild child, LaunchSignals? signals = null, bool forwards = true, int version = Launcher.Version,
        string[]? args = null, Action<string>? say = null) =>
        Launcher.RunAsync(
            args ?? ["go-to-work", "--max-runs", "3"], child, Own, signals ?? new LaunchSignals(), forwards, version,
            temp, say ?? (_ => { }));

    [Fact]
    public async Task A_child_that_exits_75_is_started_again_with_the_same_arguments_and_the_same_state()
    {
        var child = new FakeChild { Lives = { Exits(75), Exits(0) } };

        var code = await Run(child, args: ["do-my-work", "--max-runs", "3", "--profile", "night"]);

        Assert.Equal(0, code);
        Assert.Equal(2, child.Starts.Count);
        Assert.Equal(child.Starts[0].Args, child.Starts[1].Args);
        Assert.Equal(["do-my-work", "--max-runs", "3", "--profile", "night"], child.Starts[1].Args);
        Assert.Equal(child.Starts[0].Env[Launcher.StateVariable], child.Starts[1].Env[Launcher.StateVariable]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(130)]
    public async Task Any_other_exit_code_ends_the_launcher_with_that_code(int exit)
    {
        var child = new FakeChild { Lives = { Exits(exit) } };

        Assert.Equal(exit, await Run(child));
        Assert.Single(child.Starts);
    }

    [Fact]
    public async Task The_child_is_told_exactly_three_things()
    {
        var child = new FakeChild { Lives = { Exits(0) } };

        await Run(child);

        var env = Assert.Single(child.Starts).Env;
        Assert.Equal(3, env.Count);
        Assert.Equal("1", env[Launcher.VersionVariable]);
        Assert.Equal(Own, env[Launcher.PathVariable]);
        Assert.Equal(Launcher.StateFileName, Path.GetFileName(env[Launcher.StateVariable]));
        Assert.StartsWith(temp, env[Launcher.StateVariable], StringComparison.Ordinal);
    }

    [Fact]
    public void The_version_is_one_whole_number_and_starts_at_one()
    {
        Assert.Equal(1, Launcher.Version);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(75)]
    [InlineData(3)]
    public async Task The_state_goes_with_the_launcher_on_every_way_out(int exit)
    {
        var child = new FakeChild { Lives = { Exits(exit), Exits(0) } };

        await Run(child);

        Assert.Empty(Leftovers());
    }

    [Fact]
    public async Task What_a_set_leaves_beside_the_state_goes_too()
    {
        var child = new FakeChild { Lives = { Exits(75), Exits(0) } };
        child.OnStart = (_, env) =>
        {
            var state = env[Launcher.StateVariable];
            File.WriteAllText(state, "{}");
            File.WriteAllText(state + ".1", "{}");
            File.WriteAllText(state + ".2", "{}");
        };

        await Run(child);

        Assert.Equal(2, child.Starts.Count);
        Assert.Empty(Leftovers());
    }

    [Fact]
    public async Task The_state_a_restart_carries_is_still_there_for_the_next_child()
    {
        var child = new FakeChild { Lives = { Exits(75), Exits(0) } };
        string? seen = null;
        child.OnStart = (_, env) =>
        {
            var state = env[Launcher.StateVariable];
            if (child.Starts.Count == 1) File.WriteAllText(state, "carried");
            else seen = File.ReadAllText(state);
        };

        await Run(child);

        Assert.Equal("carried", seen);
    }

    [Fact]
    public async Task A_child_that_cannot_be_started_ends_the_launcher_and_leaves_nothing()
    {
        var said = new List<string>();
        var child = new FakeChild { Lives = { Exits(0) } };
        child.OnStart = (_, _) => throw new InvalidOperationException("no such file");

        Assert.Equal(1, await Run(child, say: said.Add));

        Assert.Empty(Leftovers());
        Assert.Contains(said, l => l.Contains("could not start", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_child_that_throws_leaves_nothing_either()
    {
        var child = new FakeChild { Lives = { _ => Task.FromException<int>(new IOException("gone")) } };

        await Assert.ThrowsAsync<IOException>(() => Run(child));

        Assert.Empty(Leftovers());
    }

    // ---- Signals ----

    [Fact]
    public async Task The_first_signal_reaches_the_child_and_a_second_does_not()
    {
        var child = new FakeChild { Lives = { p => p.Ends.Task } };
        var signals = new LaunchSignals();

        var run = Run(child, signals);
        signals.Raise(PosixSignal.SIGINT);
        signals.Raise(PosixSignal.SIGINT);
        signals.Raise(PosixSignal.SIGTERM);

        // The launcher outlives its child: it has not returned, and has not ended anything.
        Assert.False(run.IsCompleted);
        Assert.Equal([PosixSignal.SIGINT], child.Processes[0].Forwarded);

        child.Processes[0].Ends.SetResult(130);

        Assert.Equal(130, await run);
        Assert.Empty(Leftovers());
    }

    [Theory]
    [InlineData(PosixSignal.SIGINT)]
    [InlineData(PosixSignal.SIGTERM)]
    [InlineData(PosixSignal.SIGHUP)]
    public async Task Each_of_the_three_is_passed_on_as_itself(PosixSignal signal)
    {
        var child = new FakeChild { Lives = { p => p.Ends.Task } };
        var signals = new LaunchSignals();

        var run = Run(child, signals);
        signals.Raise(signal);
        child.Processes[0].Ends.SetResult(0);
        await run;

        Assert.Equal([signal], child.Processes[0].Forwarded);
    }

    [Fact]
    public async Task Where_the_console_reaches_the_child_the_launcher_only_waits()
    {
        var child = new FakeChild { Lives = { p => p.Ends.Task } };
        var signals = new LaunchSignals();

        var run = Run(child, signals, forwards: false);
        signals.Raise(PosixSignal.SIGINT);
        Assert.False(run.IsCompleted);
        child.Processes[0].Ends.SetResult(130);

        Assert.Equal(130, await run);
        Assert.Empty(child.Processes[0].Forwarded);
    }

    [Fact]
    public async Task A_signal_ends_the_night_even_when_the_child_asks_to_come_back()
    {
        var child = new FakeChild { Lives = { p => p.Ends.Task, Exits(0) } };
        var signals = new LaunchSignals();

        var run = Run(child, signals);
        signals.Raise(PosixSignal.SIGTERM);
        child.Processes[0].Ends.SetResult(75);

        Assert.Equal(0, await run);
        Assert.Single(child.Starts);
        Assert.Empty(Leftovers());
    }

    // ---- A repeat of a signal ----

    [Fact]
    public void A_child_swallows_a_repeat_of_a_signal_within_two_seconds_and_only_under_a_launcher()
    {
        var link = LauncherLink.From(Env((Launcher.VersionVariable, "1")));

        Assert.True(Launcher.IsRepeat(link, firstAtMs: 10_000, nowMs: 10_000));
        Assert.True(Launcher.IsRepeat(link, firstAtMs: 10_000, nowMs: 11_999));
        Assert.False(Launcher.IsRepeat(link, firstAtMs: 10_000, nowMs: 12_000));
        Assert.False(Launcher.IsRepeat(link, firstAtMs: 10_000, nowMs: 60_000));
        Assert.False(Launcher.IsRepeat(null, firstAtMs: 10_000, nowMs: 10_000));
    }

    // ---- The handshake ----

    [Fact]
    public void A_child_reads_the_launcher_it_was_started_by()
    {
        var link = LauncherLink.From(Env((Launcher.VersionVariable, "1"), (Launcher.PathVariable, "/opt/hatch")));

        Assert.NotNull(link);
        Assert.Equal(1, link.Version);
        Assert.Equal("/opt/hatch", link.Path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("one")]
    [InlineData("1.5")]
    [InlineData("0")]
    [InlineData("-2")]
    public void No_whole_number_of_one_or_more_is_no_launcher(string? version)
    {
        Assert.Null(LauncherLink.From(Env((Launcher.VersionVariable, version))));
    }

    [Fact]
    public void A_launcher_older_than_the_build_gets_one_line_naming_its_path_and_the_fix()
    {
        var link = LauncherLink.From(Env((Launcher.VersionVariable, "1"), (Launcher.PathVariable, "/opt/hatch")))!;

        var line = link.StaleLine(2);

        Assert.NotNull(line);
        Assert.DoesNotContain("\n", line, StringComparison.Ordinal);
        Assert.Contains("/opt/hatch", line, StringComparison.Ordinal);
        Assert.Contains("version 1", line, StringComparison.Ordinal);
        Assert.Contains("2", line, StringComparison.Ordinal);
        Assert.Contains("download hatch again", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_launcher_that_gave_no_path_is_still_named_as_unknown()
    {
        var link = LauncherLink.From(Env((Launcher.VersionVariable, "1")))!;

        Assert.Contains("(path unknown)", link.StaleLine(2), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2", 2)]
    [InlineData("3", 2)]
    public void A_launcher_level_with_or_ahead_of_the_build_has_nothing_to_say(string version, int own)
    {
        var link = LauncherLink.From(Env((Launcher.VersionVariable, version), (Launcher.PathVariable, "/opt/hatch")))!;

        Assert.Null(link.StaleLine(own));
    }

    [Fact]
    public void The_launchers_version_is_the_one_number_the_board_compares_against()
    {
        Assert.Equal(Hatch.Contracts.LauncherVersion.Current, Launcher.Version);
    }

    [Fact]
    public void A_launcher_at_the_version_a_thing_needs_is_enough()
    {
        var said = new List<string>();
        var link = LauncherLink.From(Env((Launcher.VersionVariable, "2")))!;

        Assert.True(link.Needs(1, "a thing", said.Add));
        Assert.True(link.Needs(2, "a thing", said.Add));
        Assert.Empty(said);
    }

    [Fact]
    public void A_launcher_too_old_for_a_thing_is_said_once_and_the_child_goes_without()
    {
        var said = new List<string>();
        var link = LauncherLink.From(Env((Launcher.VersionVariable, "1")))!;

        Assert.False(link.Needs(2, "swapping builds", said.Add));
        Assert.False(link.Needs(2, "swapping builds", said.Add));
        Assert.False(link.Needs(3, "swapping builds", said.Add));
        Assert.False(link.Needs(2, "reporting itself", said.Add));

        Assert.Equal(2, said.Count);
        Assert.Contains("version 1", said[0], StringComparison.Ordinal);
        Assert.Contains("swapping builds", said[0], StringComparison.Ordinal);
        Assert.Contains("carrying on without it", said[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_child_under_a_launcher_a_version_back_still_runs_to_its_end()
    {
        // The launcher says 1, and this child wants 2 of it: it asks, is told no,
        // says so once, and exits as any other child would.
        var said = new List<string>();
        var child = new FakeChild();
        child.Lives.Add(_ =>
        {
            var env = child.Starts[^1].Env.ToDictionary(e => e.Key, e => (string?)e.Value);
            var link = LauncherLink.From(env)!;
            link.Needs(2, "a newer thing", said.Add);
            link.Needs(2, "a newer thing", said.Add);
            return Task.FromResult(0);
        });

        Assert.Equal(0, await Run(child, version: 1));

        Assert.Single(said);
        Assert.Empty(Leftovers());
    }

    [Fact]
    public async Task A_launcher_newer_than_its_child_is_told_nothing_it_has_to_act_on()
    {
        var child = new FakeChild { Lives = { Exits(0) } };

        Assert.Equal(0, await Run(child, version: 7));

        Assert.Equal("7", child.Starts[0].Env[Launcher.VersionVariable]);
    }
}

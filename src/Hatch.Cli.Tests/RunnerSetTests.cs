namespace Hatch.Cli.Tests;

public sealed class RunnerSetTests
{
    [Fact]
    public void Absent_defaults_to_one_runner_and_leaves_args_unchanged()
    {
        string[] args = ["work", "AER-12"];

        Assert.True(RunnerSet.TryExtract(args, out var rest, out var runners, out _));
        Assert.Equal(args, rest);
        Assert.Equal(1, runners);
    }

    [Fact]
    public void Pulls_the_flag_and_its_value_out_wherever_it_appears()
    {
        Assert.True(RunnerSet.TryExtract(["--runners", "4", "go-to-work"], out var rest1, out var runners1, out _));
        Assert.Equal(["go-to-work"], rest1);
        Assert.Equal(4, runners1);

        Assert.True(RunnerSet.TryExtract(["go-to-work", "--runners", "4", "--once"], out var rest2, out var runners2, out _));
        Assert.Equal(["go-to-work", "--once"], rest2);
        Assert.Equal(4, runners2);

        Assert.True(RunnerSet.TryExtract(["go-to-work", "--once", "--runners", "4"], out var rest3, out var runners3, out _));
        Assert.Equal(["go-to-work", "--once"], rest3);
        Assert.Equal(4, runners3);
    }

    [Fact]
    public void Repeated_flag_leaves_the_last_one_standing()
    {
        Assert.True(RunnerSet.TryExtract(["--runners", "4", "--runners", "8"], out var rest, out var runners, out _));
        Assert.Equal([], rest);
        Assert.Equal(8, runners);
    }

    [Fact]
    public void Trims_the_value()
    {
        Assert.True(RunnerSet.TryExtract(["--runners", "  4  "], out _, out var runners, out _));
        Assert.Equal(4, runners);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    public void Boundary_values_are_accepted(int value)
    {
        Assert.True(RunnerSet.TryExtract(["--runners", value.ToString()], out _, out var runners, out _));
        Assert.Equal(value, runners);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("17")]
    [InlineData("not-a-number")]
    public void Out_of_range_or_unparseable_values_refuse(string value)
    {
        Assert.False(RunnerSet.TryExtract(["--runners", value], out _, out _, out var refusal));
        Assert.Equal("hatch: --runners takes a count from 1 to 16", refusal);
    }

    [Fact]
    public void A_trailing_bare_flag_refuses_with_the_same_message()
    {
        Assert.False(RunnerSet.TryExtract(["go-to-work", "--runners"], out _, out _, out var refusal));
        Assert.Equal("hatch: --runners takes a count from 1 to 16", refusal);
    }

    [Fact]
    public void Workspace_for_slot_one_is_unchanged()
    {
        Assert.Equal("/home/runner/workspace", RunnerSet.WorkspaceFor("/home/runner/workspace", 1));
    }

    [Fact]
    public void Workspace_for_slot_two_with_no_trailing_separator_appends_dot_n()
    {
        Assert.Equal("/home/runner/workspace.2", RunnerSet.WorkspaceFor("/home/runner/workspace", 2));
    }

    [Fact]
    public void Workspace_for_slot_two_trims_a_trailing_separator_first()
    {
        Assert.Equal("/home/runner/workspace.2", RunnerSet.WorkspaceFor("/home/runner/workspace/", 2));
        Assert.Equal("/home/runner/workspace.2", RunnerSet.WorkspaceFor("/home/runner/workspace\\", 2));
    }

    [Fact]
    public void Workspace_for_a_higher_slot_suffixes_correctly()
    {
        Assert.Equal("/home/runner/workspace.16", RunnerSet.WorkspaceFor("/home/runner/workspace", 16));
    }

    [Fact]
    public void Workspace_for_appends_the_same_way_regardless_of_shape()
    {
        Assert.Equal(
            "/home/runner/workspaces/night-shift.2",
            RunnerSet.WorkspaceFor("/home/runner/workspaces/night-shift", 2));
        Assert.Equal(
            "/home/runner/workspace.2",
            RunnerSet.WorkspaceFor("/home/runner/workspace", 2));
    }

    [Fact]
    public void Night_state_null_stays_null()
    {
        Assert.Null(RunnerSet.NightStateFor(null, 1));
        Assert.Null(RunnerSet.NightStateFor(null, 2));
    }

    [Fact]
    public void Night_state_slot_one_still_gets_a_suffix()
    {
        Assert.Equal("/tmp/state.1", RunnerSet.NightStateFor("/tmp/state", 1));
    }

    [Fact]
    public void Night_state_for_a_higher_slot_suffixes_correctly()
    {
        Assert.Equal("/tmp/state.16", RunnerSet.NightStateFor("/tmp/state", 16));
    }

    [Fact]
    public void Args_for_slot_one_with_nothing_already_present_just_appends_no_keys()
    {
        string[] rest = ["go-to-work", "--once"];
        Assert.Equal(["go-to-work", "--once", "--no-keys"], RunnerSet.ArgsFor(rest, 1, "/home/runner/workspace"));
    }

    [Fact]
    public void Args_for_slot_one_leaves_its_own_repo_and_workspace_untouched()
    {
        string[] rest = ["go-to-work", "--repo", "/a", "--workspace", "/b"];
        Assert.Equal(
            ["go-to-work", "--repo", "/a", "--workspace", "/b", "--no-keys"],
            RunnerSet.ArgsFor(rest, 1, "/home/runner/workspace"));
    }

    [Fact]
    public void Args_for_slot_two_strips_repo_and_workspace_and_appends_its_own()
    {
        string[] rest = ["go-to-work", "--repo", "/a", "--workspace", "/b", "--once"];
        Assert.Equal(
            ["go-to-work", "--once", "--workspace", "/home/runner/workspace.2", "--no-keys"],
            RunnerSet.ArgsFor(rest, 2, "/home/runner/workspace.2"));
    }

    [Fact]
    public void Args_for_slot_two_strips_every_repeated_repo_pair()
    {
        string[] rest = ["go-to-work", "--repo", "/a", "--repo", "/b", "--once"];
        Assert.Equal(
            ["go-to-work", "--once", "--workspace", "/home/runner/workspace.2", "--no-keys"],
            RunnerSet.ArgsFor(rest, 2, "/home/runner/workspace.2"));
    }

    [Fact]
    public void Args_for_slot_two_with_neither_flag_present_just_appends()
    {
        string[] rest = ["go-to-work", "--once"];
        Assert.Equal(
            ["go-to-work", "--once", "--workspace", "/home/runner/workspace.2", "--no-keys"],
            RunnerSet.ArgsFor(rest, 2, "/home/runner/workspace.2"));
    }

    [Fact]
    public void Fold_of_nothing_or_all_zero_is_zero()
    {
        Assert.Equal(0, RunnerSet.Fold([], false));
        Assert.Equal(0, RunnerSet.Fold([0, 0], false));
    }

    [Fact]
    public void Fold_with_any_one_is_one()
    {
        Assert.Equal(1, RunnerSet.Fold([0, 1, 0], false));
    }

    [Fact]
    public void Fold_with_a_restart_and_not_stopped_is_the_restart_code()
    {
        Assert.Equal(75, RunnerSet.Fold([0, 75, 0], false));
    }

    [Fact]
    public void Fold_with_a_restart_alone_and_stopped_is_zero()
    {
        Assert.Equal(0, RunnerSet.Fold([0, 75, 0], true));
    }

    [Fact]
    public void Fold_with_a_restart_and_a_one_and_stopped_is_one()
    {
        Assert.Equal(1, RunnerSet.Fold([75, 1], true));
    }

    [Fact]
    public void Fold_with_a_restart_and_a_one_and_not_stopped_is_the_restart_code()
    {
        Assert.Equal(75, RunnerSet.Fold([75, 1], false));
    }

    [Theory]
    [InlineData("2", 2)]
    [InlineData(" 3 ", 3)]
    [InlineData("16", 16)]
    public void A_slot_of_two_or_more_is_a_member(string value, int expected) =>
        Assert.Equal(expected, RunnerSet.SlotFrom(new Dictionary<string, string?> { [RunnerSet.SlotVariable] = value }));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("two")]
    public void Anything_else_is_an_ordinary_runner(string? value) =>
        Assert.Null(RunnerSet.SlotFrom(new Dictionary<string, string?> { [RunnerSet.SlotVariable] = value }));

    [Fact]
    public void No_slot_variable_is_an_ordinary_runner() =>
        Assert.Null(RunnerSet.SlotFrom(new Dictionary<string, string?>()));

    [Fact]
    public void A_member_keeps_its_origin_and_workspace_and_loses_its_checkouts_and_name()
    {
        var settings = new Settings
        {
            Repos = ["/a"], Runner = "Fry", Base = "https://example.test", Key = "k", Workspace = "/w",
        };

        var member = RunnerSet.ForMember(settings);

        Assert.Empty(member.Repos);
        Assert.Null(member.Runner);
        Assert.Equal("https://example.test", member.Base);
        Assert.Equal("/w", member.Workspace);
    }

    [Fact]
    public void The_workspace_handed_over_is_the_last_one_given()
    {
        Assert.Null(RunnerSet.WorkspaceFrom(["--once"]));
        Assert.Equal("/a", RunnerSet.WorkspaceFrom(["--once", "--workspace", "/a"]));
        Assert.Equal("/b", RunnerSet.WorkspaceFrom(["--workspace", "/a", "--once", "--workspace", "/b"]));
        Assert.Null(RunnerSet.WorkspaceFrom(["--once", "--workspace"]));
    }

    // ---- the host --------------------------------------------------------------

    private static readonly Settings HostSettings = new()
    {
        Base = "https://example.test", Key = "k", Workspace = "/w", WorkspaceDefaulted = true,
    };

    private sealed class FakeLauncher(Func<SetChild, Action<string>, Task<int>> run) : ISetLauncher
    {
        public System.Collections.Concurrent.ConcurrentDictionary<int, SetChild> Children { get; } = new();

        public Task<int> RunAsync(SetChild child, Action<string> line, CancellationToken ct)
        {
            Children[child.Slot] = child;
            Assert.Equal(CancellationToken.None, ct);
            return run(child, line);
        }
    }

    private static string SetDirOf(FakeLauncher launcher) => launcher.Children.Values.First().Env[RunnerSet.DirVariable];

    private static async Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    private static Task<int> Host(
        FakeLauncher launcher, int runners = 3, string[]? rest = null, string? profile = null,
        Action<string>? say = null, CancellationToken ct = default, string command = "go-to-work",
        Settings? settings = null, string? nightState = "/tmp/state") =>
        RunnerSet.RunAsync(
            command, runners, rest ?? ["--once"], profile, settings ?? HostSettings,
            new Dictionary<string, string?> { ["HATCH_NIGHT_STATE"] = nightState },
            launcher, say ?? (_ => { }), ct);

    [Fact]
    public async Task Starts_every_child_with_its_own_slot_workspace_and_night_state_and_one_set_dir()
    {
        var launcher = new FakeLauncher((_, _) => Task.FromResult(0));

        Assert.Equal(0, await Host(launcher, rest: ["--workspace", "/w", "--once"]));

        Assert.Equal([1, 2, 3], launcher.Children.Keys.Order());
        Assert.Equal(["/w", "/w.2", "/w.3"], launcher.Children.Values.OrderBy(c => c.Slot).Select(c => WorkspaceFrom(c)));
        Assert.Equal(
            ["/tmp/state.1", "/tmp/state.2", "/tmp/state.3"],
            launcher.Children.Values.OrderBy(c => c.Slot).Select(c => c.Env["HATCH_NIGHT_STATE"]));
        Assert.Single(launcher.Children.Values.Select(c => c.Env[RunnerSet.DirVariable]).Distinct());
        Assert.False(launcher.Children[1].Env.ContainsKey(RunnerSet.SlotVariable));
        Assert.Equal("2", launcher.Children[2].Env[RunnerSet.SlotVariable]);
        Assert.Equal("3", launcher.Children[3].Env[RunnerSet.SlotVariable]);
    }

    private static string? WorkspaceFrom(SetChild child) => RunnerSet.WorkspaceFrom(child.Args);

    [Fact]
    public async Task Without_a_night_state_the_children_get_none()
    {
        var launcher = new FakeLauncher((_, _) => Task.FromResult(0));

        await Host(launcher, runners: 2, nightState: null);

        Assert.All(launcher.Children.Values, c => Assert.False(c.Env.ContainsKey("HATCH_NIGHT_STATE")));
    }

    [Fact]
    public async Task The_default_workspace_is_cloned_beside_for_the_others()
    {
        var launcher = new FakeLauncher((_, _) => Task.FromResult(0));

        await Host(launcher, runners: 2);

        Assert.Null(WorkspaceFrom(launcher.Children[1]));
        Assert.Equal("/w.2", WorkspaceFrom(launcher.Children[2]));
    }

    [Theory]
    [InlineData("go-to-work")]
    [InlineData("do-my-work")]
    public async Task Argv_starts_with_the_command_word_it_was_typed_as(string command)
    {
        var launcher = new FakeLauncher((_, _) => Task.FromResult(0));

        await Host(launcher, runners: 2, command: command);

        Assert.All(launcher.Children.Values, c => Assert.Equal(command, c.Args[0]));
        Assert.All(launcher.Children.Values, c => Assert.Contains("--no-keys", c.Args));
    }

    [Fact]
    public async Task A_pinned_profile_reaches_every_child_and_an_unpinned_one_none()
    {
        var pinned = new FakeLauncher((_, _) => Task.FromResult(0));
        await Host(pinned, runners: 2, profile: "x");
        Assert.All(pinned.Children.Values, c => Assert.Equal(["go-to-work", "--profile", "x"], c.Args[..3]));

        var unpinned = new FakeLauncher((_, _) => Task.FromResult(0));
        await Host(unpinned, runners: 2);
        Assert.All(unpinned.Children.Values, c => Assert.DoesNotContain("--profile", c.Args));
    }

    [Fact]
    public async Task Repo_and_workspace_stay_on_the_first_child_and_go_from_the_rest()
    {
        var launcher = new FakeLauncher((_, _) => Task.FromResult(0));

        await Host(launcher, runners: 2, rest: ["--repo", "/a", "--workspace", "/w", "--once"]);

        Assert.Contains("--repo", launcher.Children[1].Args);
        Assert.DoesNotContain("--repo", launcher.Children[2].Args);
        Assert.Equal("/w.2", WorkspaceFrom(launcher.Children[2]));
    }

    [Fact]
    public async Task A_child_line_reaches_say_prefixed_with_its_slot()
    {
        var said = new System.Collections.Concurrent.ConcurrentBag<string>();
        var launcher = new FakeLauncher((child, line) =>
        {
            if (child.Slot == 2) line("text");
            return Task.FromResult(0);
        });

        await Host(launcher, runners: 2, say: said.Add);

        Assert.Equal(["[2] text"], said);
    }

    [Fact]
    public async Task Cancelling_writes_stop_and_waits_for_the_children()
    {
        var release = new TaskCompletionSource<int>();
        var launcher = new FakeLauncher((_, _) => release.Task);
        using var cancelling = new CancellationTokenSource();

        var host = Host(launcher, runners: 2, ct: cancelling.Token);
        await Eventually(() => launcher.Children.Count == 2);
        var dir = SetDirOf(launcher);
        Assert.False(RunnerSet.Signalled(dir, RunnerSet.StopFileName));

        await cancelling.CancelAsync();
        Assert.True(RunnerSet.Signalled(dir, RunnerSet.StopFileName));
        await Task.Delay(50);
        Assert.False(host.IsCompleted);

        release.SetResult(0);
        Assert.Equal(0, await host);
    }

    [Fact]
    public async Task A_restart_asked_while_stopping_is_not_carried_out()
    {
        var release = new TaskCompletionSource<int>();
        var launcher = new FakeLauncher((_, _) => release.Task);
        using var cancelling = new CancellationTokenSource();

        var host = Host(launcher, runners: 2, ct: cancelling.Token);
        await Eventually(() => launcher.Children.Count == 2);
        var dir = SetDirOf(launcher);

        await cancelling.CancelAsync();
        release.SetResult(75);

        Assert.Equal(0, await host);
        Assert.False(RunnerSet.Signalled(dir, RunnerSet.RestartFileName));
    }

    [Fact]
    public async Task A_child_asking_for_a_restart_writes_restart_and_the_set_returns_it()
    {
        var release = new TaskCompletionSource<int>();
        var launcher = new FakeLauncher((child, _) => child.Slot == 2 ? Task.FromResult(75) : release.Task);

        var host = Host(launcher, runners: 2);
        await Eventually(() => launcher.Children.Count == 2);
        var dir = SetDirOf(launcher);
        await Eventually(() => RunnerSet.Signalled(dir, RunnerSet.RestartFileName));
        Assert.False(RunnerSet.Signalled(dir, RunnerSet.StopFileName));

        release.SetResult(0);
        Assert.Equal(75, await host);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task A_child_alone_exiting_signals_nothing(int code)
    {
        var release = new TaskCompletionSource<int>();
        var done = new TaskCompletionSource();
        var launcher = new FakeLauncher(async (child, _) =>
        {
            if (child.Slot != 2) return await release.Task;
            await Task.Yield();
            done.SetResult();
            return code;
        });

        var host = Host(launcher, runners: 2);
        await done.Task;
        await Task.Delay(50);
        var dir = SetDirOf(launcher);

        Assert.False(RunnerSet.Signalled(dir, RunnerSet.StopFileName));
        Assert.False(RunnerSet.Signalled(dir, RunnerSet.RestartFileName));
        Assert.False(host.IsCompleted);

        release.SetResult(0);
        Assert.Equal(code, await host);
    }

    [Fact]
    public async Task A_launcher_that_throws_counts_as_one_and_does_not_take_the_others_down()
    {
        var said = new System.Collections.Concurrent.ConcurrentBag<string>();
        var launcher = new FakeLauncher((child, _) =>
            child.Slot == 1 ? throw new InvalidOperationException("no such program") : Task.FromResult(0));

        Assert.Equal(1, await Host(launcher, runners: 2, say: said.Add));

        Assert.Contains(said, l => l.StartsWith("[1] ") && l.Contains("no such program"));
        Assert.Equal(2, launcher.Children.Count);
    }

    [Fact]
    public async Task No_workspace_refuses_and_starts_nothing()
    {
        var said = new System.Collections.Concurrent.ConcurrentBag<string>();
        var launcher = new FakeLauncher((_, _) => Task.FromResult(0));

        var code = await Host(launcher, rest: ["--workspace", "off"], say: said.Add);

        Assert.Equal(1, code);
        Assert.Empty(launcher.Children);
        Assert.Equal(["hatch: --runners needs a workspace to clone into"], said);
    }

    [Fact]
    public async Task The_set_dir_is_gone_afterwards()
    {
        var launcher = new FakeLauncher((_, _) => Task.FromResult(0));

        await Host(launcher, runners: 2);

        Assert.False(Directory.Exists(SetDirOf(launcher)));
    }
}

using System.Text;

namespace Hatch.Cli.Tests;

/// <summary>
/// The standby hosts, each built on any OS over a recorded tool: the files they
/// write, the calls they make, and the calls they never make.
/// </summary>
public sealed class StandbyHostTests : IDisposable
{
    private const string Path1 = "/usr/bin:/bin";

    private readonly string _temp = Directory.CreateTempSubdirectory("hatch-standby-host-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_temp, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A temp directory the OS will reap.
        }
    }

    private sealed class Recorder
    {
        public Recorder(Func<string, string[], (int, string)>? script = null)
        {
            Script = script ?? ((_, _) => (0, ""));
            Run = (file, args) =>
            {
                Calls.Add((file, args));
                return Script(file, args);
            };
        }

        public Func<string, string[], (int, string)> Script { get; set; }

        public RunTool Run { get; }

        public List<(string File, string[] Args)> Calls { get; } = [];

        public IEnumerable<string> Lines => Calls.Select(c => c.File + " " + string.Join(' ', c.Args));
    }

    private static RunTool NoTool => (file, _) => throw new InvalidOperationException($"{file} was run");

    private StandbyItem Item(bool windows)
    {
        var root = Path.Combine(_temp, "checkout");
        Directory.CreateDirectory(root);
        var item = StandbyItem.For(root, "Alder", null, "/opt/hatch/hatch", windows, Path.Combine(_temp, "hatch"), out var refusal);
        Assert.Equal("", refusal);
        return Assert.IsType<StandbyItem>(item);
    }

    private string Home => Path.Combine(_temp, "home");

    private string Xdg => Path.Combine(_temp, "xdg");

    private WindowsStandbyHost Windows(Recorder tool) =>
        new(tool.Run, "WORK", "alder", Path.Combine(_temp, "tmp"));

    private LinuxStandbyHost Linux(Recorder tool, string? xdg = null) =>
        new(tool.Run, Home, xdg ?? Xdg, Path1);

    private static void NoStop(Recorder tool)
    {
        foreach (var line in tool.Lines)
        {
            Assert.DoesNotContain("stop", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("--now", line, StringComparison.Ordinal);
            Assert.DoesNotContain("/End", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("bootout", line, StringComparison.Ordinal);
        }
    }

    // --- Windows ---------------------------------------------------------------

    [Fact]
    public void Windows_first_install_writes_the_wrapper_and_creates_the_task_from_a_utf16_file()
    {
        Directory.CreateDirectory(Path.Combine(_temp, "tmp"));
        var item = Item(windows: true);
        var xmlPath = Path.Combine(_temp, "tmp", item.Id + ".xml");
        byte[]? seen = null;
        var tool = new Recorder((_, args) =>
        {
            if (args[0] == "/Create") seen = File.ReadAllBytes(xmlPath);
            return (0, "");
        });

        var result = Windows(tool).Install(item);

        Assert.Equal(StandbyResult.Changed, result);
        var call = Assert.Single(tool.Calls);
        Assert.Equal("schtasks", call.File);
        Assert.Equal(["/Create", "/TN", item.Id, "/XML", xmlPath, "/F"], call.Args);
        Assert.NotNull(seen);
        Assert.Equal([0xFF, 0xFE], seen![..2]);
        Assert.Equal(StandbyItem.TaskXml(item, "WORK", "alder"), new UnicodeEncoding(false, true).GetString(seen, 2, seen.Length - 2));
        Assert.False(File.Exists(xmlPath));
        Assert.Equal(StandbyItem.WindowsWrapper(item), File.ReadAllText(item.Wrapper));
        Assert.True(Directory.Exists(Path.GetDirectoryName(item.Log)));
    }

    [Fact]
    public void Windows_second_install_only_asks_whether_the_task_is_there()
    {
        var item = Item(windows: true);
        Windows(new Recorder()).Install(item);
        var tool = new Recorder();

        var result = Windows(tool).Install(item);

        Assert.Equal(StandbyResult.Unchanged, result);
        Assert.Equal([$"schtasks /Query /TN {item.Id}"], tool.Lines);
    }

    [Fact]
    public void Windows_install_recreates_a_task_that_is_gone_though_the_wrapper_is_there()
    {
        var item = Item(windows: true);
        Windows(new Recorder()).Install(item);
        var tool = new Recorder((_, args) => (args[0] == "/Query" ? 1 : 0, ""));

        var result = Windows(tool).Install(item);

        Assert.Equal(StandbyResult.Changed, result);
        Assert.Equal(["/Query", "/Create"], tool.Calls.Select(c => c.Args[0]));
    }

    [Fact]
    public void Windows_install_never_runs_the_task()
    {
        var item = Item(windows: true);
        var tool = new Recorder();

        Windows(tool).Install(item);

        Assert.DoesNotContain(tool.Calls, c => c.Args.Contains("/Run"));
    }

    [Fact]
    public void Windows_remove_deletes_the_task_and_the_wrapper_and_leaves_the_log()
    {
        var item = Item(windows: true);
        Windows(new Recorder()).Install(item);
        File.WriteAllText(item.Log, "output");
        var tool = new Recorder();

        var result = Windows(tool).Remove(item);

        Assert.Equal(StandbyResult.Changed, result);
        Assert.Equal([$"schtasks /Query /TN {item.Id}", $"schtasks /Delete /TN {item.Id} /F"], tool.Lines);
        Assert.False(File.Exists(item.Wrapper));
        Assert.True(File.Exists(item.Log));
        NoStop(tool);
    }

    [Fact]
    public void Windows_remove_of_nothing_runs_no_delete()
    {
        var item = Item(windows: true);
        var tool = new Recorder((_, _) => (1, "ERROR: The system cannot find the file specified."));

        var result = Windows(tool).Remove(item);

        Assert.Equal(StandbyResult.Unchanged, result);
        Assert.Equal([$"schtasks /Query /TN {item.Id}"], tool.Lines);
        NoStop(tool);
    }

    [Fact]
    public void Windows_remove_keeps_the_wrapper_when_the_task_will_not_delete()
    {
        var item = Item(windows: true);
        Windows(new Recorder()).Install(item);
        var tool = new Recorder((_, args) => args[0] == "/Delete" ? (5, "Access is denied.") : (0, ""));

        var result = Windows(tool).Remove(item);

        Assert.Equal(StandbyOutcome.Refused, result.Outcome);
        Assert.Contains("Access is denied.", result.Refusal);
        Assert.True(File.Exists(item.Wrapper));
    }

    [Fact]
    public void Windows_refuses_with_what_schtasks_said_when_create_fails()
    {
        var item = Item(windows: true);
        var tool = new Recorder((_, _) => (1, "ERROR: Access is denied.\r\n"));

        var result = Windows(tool).Install(item);

        Assert.Equal(StandbyOutcome.Refused, result.Outcome);
        Assert.Equal($"hatch: schtasks /Create /TN {item.Id} /XML {Path.Combine(_temp, "tmp", item.Id + ".xml")} /F exited 1: ERROR: Access is denied.", result.Refusal);
        Assert.False(File.Exists(Path.Combine(_temp, "tmp", item.Id + ".xml")));
    }

    [Fact]
    public void A_tool_that_cannot_start_is_a_refusal_and_not_an_exception()
    {
        var item = Item(windows: true);
        var tool = new Recorder((file, _) => (-1, $"could not start {file}: not found"));

        var result = Windows(tool).Install(item);

        Assert.Equal(StandbyOutcome.Refused, result.Outcome);
        Assert.Contains("could not start schtasks: not found", result.Refusal);
    }

    [Fact]
    public void A_file_that_cannot_be_written_is_a_refusal_naming_it()
    {
        var item = Item(windows: true);
        Directory.CreateDirectory(Path.GetDirectoryName(item.Wrapper)!);
        Directory.CreateDirectory(item.Wrapper);
        File.WriteAllText(Path.Combine(item.Wrapper, "keep"), "");

        var result = Windows(new Recorder()).Install(item);

        Assert.Equal(StandbyOutcome.Refused, result.Outcome);
        Assert.StartsWith($"hatch: {item.Wrapper} - ", result.Refusal);
    }

    // --- macOS -----------------------------------------------------------------

    [Fact]
    public void Mac_install_writes_the_wrapper_and_the_agent_and_runs_no_tool()
    {
        var item = Item(windows: false);
        var host = new MacStandbyHost(Home, Path1);

        var result = host.Install(item);

        Assert.Equal(StandbyResult.Changed, result);
        var agent = Path.Combine(Home, "Library", "LaunchAgents", item.Id + ".plist");
        Assert.Equal(agent, item.LaunchAgentPath(Home));
        Assert.Equal(StandbyItem.LaunchdPlist(item), File.ReadAllText(agent));
        Assert.Equal(StandbyItem.PosixWrapper(item, Path1), File.ReadAllText(item.Wrapper));
        Assert.True(host.Installed(item));
    }

    [Fact]
    public void Mac_install_twice_is_unchanged_and_a_new_path_changes_it()
    {
        var item = Item(windows: false);
        var host = new MacStandbyHost(Home, Path1);
        host.Install(item);

        Assert.Equal(StandbyResult.Unchanged, host.Install(item));
        Assert.Equal(StandbyResult.Changed, new MacStandbyHost(Home, "/opt/bin:" + Path1).Install(item));
        Assert.Contains("/opt/bin", File.ReadAllText(item.Wrapper));
    }

    [Fact]
    public void Mac_remove_deletes_both_files_and_a_second_remove_is_unchanged()
    {
        var item = Item(windows: false);
        var host = new MacStandbyHost(Home, Path1);
        host.Install(item);

        Assert.Equal(StandbyResult.Changed, host.Remove(item));
        Assert.False(File.Exists(item.Wrapper));
        Assert.False(host.Installed(item));
        Assert.Equal(StandbyResult.Unchanged, host.Remove(item));
    }

    // --- Linux -----------------------------------------------------------------

    [Fact]
    public void Linux_with_systemd_writes_the_unit_reloads_and_enables_without_starting()
    {
        var item = Item(windows: false);
        var tool = new Recorder();
        var host = Linux(tool);

        var result = host.Install(item);

        Assert.Equal(StandbyResult.Changed, result);
        Assert.Equal(
        [
            "systemctl --user show-environment",
            "systemctl --user daemon-reload",
            $"systemctl --user enable {item.Id}.service",
        ], tool.Lines);
        var unit = Path.Combine(Xdg, "systemd", "user", item.Id + ".service");
        Assert.Equal(unit, item.SystemdUnitPath(Xdg));
        Assert.Equal(StandbyItem.SystemdUnit(item), File.ReadAllText(unit));
        Assert.Equal(StandbyItem.PosixWrapper(item, Path1), File.ReadAllText(item.Wrapper));
        Assert.True(host.Installed(item));
        NoStop(tool);
    }

    [Fact]
    public void Linux_second_install_runs_only_the_probe()
    {
        var item = Item(windows: false);
        Linux(new Recorder()).Install(item);
        var tool = new Recorder();

        var result = Linux(tool).Install(item);

        Assert.Equal(StandbyResult.Unchanged, result);
        Assert.Equal(["systemctl --user show-environment"], tool.Lines);
    }

    [Fact]
    public void Linux_without_user_systemd_writes_an_autostart_entry_and_enables_nothing()
    {
        var item = Item(windows: false);
        var tool = new Recorder((_, _) => (1, "Failed to connect to bus"));
        var host = Linux(tool);

        var result = host.Install(item);

        Assert.Equal(StandbyResult.Changed, result);
        Assert.Equal(["systemctl --user show-environment"], tool.Lines);
        Assert.Equal(StandbyItem.DesktopEntry(item), File.ReadAllText(Path.Combine(Xdg, "autostart", item.Id + ".desktop")));
        Assert.True(host.Installed(item));
        Assert.Equal(StandbyResult.Unchanged, Linux(tool).Install(item));
    }

    [Fact]
    public void Linux_with_no_systemctl_at_all_falls_back_to_autostart()
    {
        var item = Item(windows: false);
        var tool = new Recorder((file, _) => (-1, $"could not start {file}: not found"));

        var result = Linux(tool).Install(item);

        Assert.Equal(StandbyResult.Changed, result);
        Assert.True(File.Exists(item.AutostartPath(Xdg)));
    }

    [Fact]
    public void Linux_install_under_systemd_removes_a_stale_autostart_entry()
    {
        var item = Item(windows: false);
        Linux(new Recorder((_, _) => (1, ""))).Install(item);

        Linux(new Recorder()).Install(item);

        Assert.False(File.Exists(item.AutostartPath(Xdg)));
        Assert.True(File.Exists(item.SystemdUnitPath(Xdg)));
    }

    [Fact]
    public void Linux_remove_disables_deletes_and_reloads()
    {
        var item = Item(windows: false);
        Linux(new Recorder()).Install(item);
        File.WriteAllText(item.Log, "output");
        var tool = new Recorder();

        var result = Linux(tool).Remove(item);

        Assert.Equal(StandbyResult.Changed, result);
        Assert.Equal([$"systemctl --user disable {item.Id}.service", "systemctl --user daemon-reload"], tool.Lines);
        Assert.False(File.Exists(item.SystemdUnitPath(Xdg)));
        Assert.False(File.Exists(item.Wrapper));
        Assert.True(File.Exists(item.Log));
        NoStop(tool);
    }

    [Fact]
    public void Linux_remove_of_an_autostart_entry_runs_no_tool()
    {
        var item = Item(windows: false);
        Linux(new Recorder((_, _) => (1, ""))).Install(item);

        var result = new LinuxStandbyHost(NoTool, Home, Xdg, Path1).Remove(item);

        Assert.Equal(StandbyResult.Changed, result);
        Assert.False(File.Exists(item.AutostartPath(Xdg)));
        Assert.False(File.Exists(item.Wrapper));
    }

    [Fact]
    public void Linux_remove_of_nothing_is_unchanged_and_runs_no_tool()
    {
        var item = Item(windows: false);

        var result = new LinuxStandbyHost(NoTool, Home, Xdg, Path1).Remove(item);

        Assert.Equal(StandbyResult.Unchanged, result);
    }

    [Fact]
    public void Linux_remove_deletes_nothing_when_disable_fails()
    {
        var item = Item(windows: false);
        Linux(new Recorder()).Install(item);
        var tool = new Recorder((_, args) => args[1] == "disable" ? (1, "Unit not found.") : (0, ""));

        var result = Linux(tool).Remove(item);

        Assert.Equal(StandbyOutcome.Refused, result.Outcome);
        Assert.Contains("Unit not found.", result.Refusal);
        Assert.True(File.Exists(item.SystemdUnitPath(Xdg)));
        Assert.True(File.Exists(item.Wrapper));
        NoStop(tool);
    }

    [Fact]
    public void Linux_refuses_with_what_systemctl_said_when_enable_fails()
    {
        var item = Item(windows: false);
        var tool = new Recorder((_, args) => args[1] == "enable" ? (1, "Failed to enable unit.\n") : (0, ""));

        var result = Linux(tool).Install(item);

        Assert.Equal(StandbyOutcome.Refused, result.Outcome);
        Assert.Equal($"hatch: systemctl --user enable {item.Id}.service exited 1: Failed to enable unit.", result.Refusal);
    }

    [Fact]
    public void Linux_config_home_is_dot_config_when_xdg_is_not_set()
    {
        var item = Item(windows: false);

        new LinuxStandbyHost(new Recorder().Run, Home, null, Path1).Install(item);

        Assert.True(File.Exists(Path.Combine(Home, ".config", "systemd", "user", item.Id + ".service")));
    }

    // --- the real tool -----------------------------------------------------------

    [Fact]
    public void RunProcess_reports_a_program_that_is_not_there_as_minus_one()
    {
        var (exit, output) = StandbyHosts.RunProcess(Path.Combine(_temp, "no-such-program"), []);

        Assert.Equal(-1, exit);
        Assert.StartsWith("could not start ", output);
    }

    [Fact]
    public void ForThisMachine_gives_a_host_for_this_os()
    {
        Assert.NotNull(StandbyHosts.ForThisMachine(NoTool));
    }
}

namespace Hatch.Cli.Tests;

/// <summary>
/// <c>standby</c> - register, show and forget the login item, against a host
/// that records what it was asked and a temp directory standing in for the
/// machine's own.
/// </summary>
public sealed class StandbyCommandTests : IDisposable
{
    private readonly CliHarness _h = new();
    private readonly string _root = Directory.CreateTempSubdirectory("hatch-standby-root-").FullName;
    private readonly string _temp = Directory.CreateTempSubdirectory("hatch-standby-temp-").FullName;
    private readonly FakeStandbyHost _host = new();

    public StandbyCommandTests()
    {
        // A Hatch checkout, so the item names its own script and not the test
        // host's dotnet, which StandbyItem.For refuses.
        Directory.CreateDirectory(Path.Combine(_root, "scripts"));
        File.WriteAllText(Path.Combine(_root, "scripts", "hatch.sh"), "");
        Directory.CreateDirectory(Path.Combine(_root, "src", "Hatch.Cli"));
    }

    public void Dispose()
    {
        _h.Dispose();
        Directory.Delete(_root, recursive: true);
        Directory.Delete(_temp, recursive: true);
    }

    private StandbyCommand Command(bool standing = true, string? profile = null) =>
        new(standing ? _h.Cli with { Checkouts = [new CheckoutEntry(_root, null, Standing: true)] } : _h.Cli, profile, _host)
        {
            TempDirectory = _temp,
        };

    private StandbyItem Item() =>
        StandbyItem.For(
            _root, "test:/checkout", null, Environment.ProcessPath, OperatingSystem.IsWindows(),
            Path.GetDirectoryName(Settings.UserConfigPath())!, out _)!;

    [Fact]
    public async Task Install_fresh_names_the_checkout_the_command_the_log_and_how_it_starts()
    {
        var item = Item();

        Assert.Equal(0, await Command().RunAsync(["install"], default));

        Assert.Equal([item.Id], _host.Installs.Select(i => i.Id));
        Assert.Contains(item.Root, _h.Said);
        Assert.Contains(string.Join(' ', item.Command), _h.Said);
        Assert.Contains(item.Log, _h.Said);
        Assert.Contains("next sign-in", _h.Said);
        Assert.Contains("go-to-work --standby", _h.Said);
        Assert.Empty(_h.Wire.Calls);
    }

    [Fact]
    public async Task Install_with_a_profile_carries_it_in_the_command()
    {
        Assert.Equal(0, await Command(profile: "staging").RunAsync(["install"], default));

        Assert.Contains("--profile staging", _h.Said);
    }

    [Fact]
    public async Task Install_run_twice_says_it_is_already_installed_and_nothing_else()
    {
        _host.InstallResult = StandbyResult.Unchanged;

        Assert.Equal(0, await Command().RunAsync(["install"], default));

        Assert.Contains("already installed", _h.Said);
        Assert.DoesNotContain("next sign-in", _h.Said);
        Assert.DoesNotContain("go-to-work", _h.Said);
        Assert.Single(_h.Say.Said);
    }

    [Fact]
    public async Task Install_refused_by_the_tool_exits_1_naming_why()
    {
        _host.InstallResult = StandbyResult.Refused("boom");

        Assert.Equal(1, await Command().RunAsync(["install"], default));

        Assert.Contains("boom", _h.Complained);
        Assert.Empty(_h.Say.Said);
        Assert.Empty(_host.Removes);
    }

    [Fact]
    public async Task Show_with_nothing_installed_and_no_lock_says_so_and_names_the_command_and_log()
    {
        var item = Item();

        Assert.Equal(0, await Command().RunAsync(["show"], default));

        Assert.Contains("not installed", _h.Said);
        Assert.Contains(string.Join(' ', item.Command), _h.Said);
        Assert.Contains(item.Log, _h.Said);
        Assert.Contains("No loop holds", _h.Said);
        Assert.Empty(_host.Installs);
        Assert.Empty(_host.Removes);
        Assert.Empty(_h.Wire.Calls);
    }

    [Fact]
    public async Task Show_with_it_installed_says_installed()
    {
        _host.IsInstalled = true;

        Assert.Equal(0, await Command().RunAsync(["show"], default));

        Assert.Contains("is installed", _h.Said);
        Assert.DoesNotContain("not installed", _h.Said);
    }

    [Fact]
    public async Task Show_with_a_loop_holding_the_lock_names_its_pid()
    {
        using var held = LoopLock.Take(_root, _temp, out _);
        Assert.NotNull(held);

        Assert.Equal(0, await Command().RunAsync(["show"], default));

        Assert.Contains($"pid {Environment.ProcessId}", _h.Said);
        Assert.DoesNotContain("No loop holds", _h.Said);
    }

    [Fact]
    public async Task Show_with_a_stale_lock_finds_no_holder_and_leaves_the_lock_alone()
    {
        var dir = LoopLock.PathFor(_root, _temp);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "pid"), int.MaxValue.ToString());

        Assert.Equal(0, await Command().RunAsync(["show"], default));

        Assert.Contains("No loop holds", _h.Said);
        Assert.True(Directory.Exists(dir));
    }

    [Fact]
    public async Task Bare_standby_behaves_exactly_like_show()
    {
        Assert.Equal(0, await Command().RunAsync(["show"], default));
        var shown = _h.Said;

        using var again = new CliHarness();
        var bare = new StandbyCommand(
            again.Cli with { Checkouts = [new CheckoutEntry(_root, null, Standing: true)] }, null, _host)
        {
            TempDirectory = _temp,
        };
        Assert.Equal(0, await bare.RunAsync([], default));

        Assert.Equal(shown, again.Said);
    }

    [Fact]
    public async Task Remove_with_nothing_installed_says_so_and_never_installs()
    {
        _host.RemoveResult = StandbyResult.Unchanged;

        Assert.Equal(0, await Command().RunAsync(["remove"], default));

        Assert.Contains("not installed", _h.Said);
        Assert.Single(_h.Say.Said);
        Assert.Empty(_host.Installs);
    }

    [Fact]
    public async Task Remove_that_removes_says_a_running_loop_keeps_running()
    {
        var item = Item();
        using var held = LoopLock.Take(_root, _temp, out _);

        Assert.Equal(0, await Command().RunAsync(["remove"], default));

        Assert.Equal([item.Id], _host.Removes.Select(i => i.Id));
        Assert.Contains("Removed", _h.Said);
        Assert.Contains("not stopped", _h.Said);
        Assert.Empty(_host.Installs);
        Assert.True(Directory.Exists(LoopLock.PathFor(_root, _temp)));
    }

    [Fact]
    public async Task Remove_refused_by_the_tool_exits_1()
    {
        _host.RemoveResult = StandbyResult.Refused("nope");

        Assert.Equal(1, await Command().RunAsync(["remove"], default));

        Assert.Contains("nope", _h.Complained);
    }

    [Fact]
    public async Task No_standing_checkout_exits_1_and_says_to_run_it_inside_one()
    {
        Assert.Equal(1, await Command(standing: false).RunAsync(["install"], default));

        Assert.Contains("run it inside a checkout", _h.Complained);
        Assert.Empty(_host.Installs);
    }

    [Fact]
    public async Task An_unknown_first_word_exits_1_prints_usage_and_asks_nothing_of_the_host()
    {
        Assert.Equal(1, await Command().RunAsync(["start"], default));

        Assert.Contains("standby does not take \"start\"", _h.Complained);
        Assert.Contains("usage: hatch standby", _h.Complained);
        Assert.Empty(_host.Installs);
        Assert.Empty(_host.Removes);
        Assert.Equal(0, _host.InstalledAsked);
    }

    [Fact]
    public async Task Help_prints_the_usage_block_and_asks_nothing_of_the_host()
    {
        Assert.Equal(0, await Command().RunAsync(["-h"], default));

        Assert.Equal(StandbyCommand.StandbyUsage, _h.Say.Said);
        Assert.Empty(_host.Installs);
        Assert.Empty(_host.Removes);
        Assert.Equal(0, _host.InstalledAsked);
    }

    private sealed class FakeStandbyHost : IStandbyHost
    {
        public bool IsInstalled { get; set; }

        public StandbyResult InstallResult { get; set; } = StandbyResult.Changed;

        public StandbyResult RemoveResult { get; set; } = StandbyResult.Changed;

        public List<StandbyItem> Installs { get; } = [];

        public List<StandbyItem> Removes { get; } = [];

        public int InstalledAsked { get; private set; }

        public bool Installed(StandbyItem item)
        {
            InstalledAsked++;
            return IsInstalled;
        }

        public StandbyResult Install(StandbyItem item)
        {
            Installs.Add(item);
            return InstallResult;
        }

        public StandbyResult Remove(StandbyItem item)
        {
            Removes.Add(item);
            return RemoveResult;
        }
    }
}

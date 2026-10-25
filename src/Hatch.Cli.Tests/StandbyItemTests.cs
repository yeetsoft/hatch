namespace Hatch.Cli.Tests;

/// <summary>
/// The standby login item: one name per checkout, and the text each platform
/// registers for it, asserted whole.
/// </summary>
public sealed class StandbyItemTests : IDisposable
{
    private const string Refusal =
        "there is no hatch program to start at login under the dotnet host; run this from a published hatch, or from a Hatch checkout";

    private readonly string _temp = Directory.CreateTempSubdirectory("hatch-standby-").FullName;

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

    private string Tree(string name, bool hatch)
    {
        var path = Path.Combine(_temp, name);
        Directory.CreateDirectory(path);
        if (hatch)
        {
            Directory.CreateDirectory(Path.Combine(path, "scripts"));
            Directory.CreateDirectory(Path.Combine(path, "src", "Hatch.Cli"));
            File.WriteAllText(Path.Combine(path, "scripts", "hatch.sh"), "#!/bin/sh\n");
        }

        return path;
    }

    private StandbyItem Item(string root, string? profile = null, string? processPath = "/opt/hatch/hatch", bool windows = false)
    {
        var item = StandbyItem.For(root, "Alder", profile, processPath, windows, Path.Combine(_temp, "home"), out var refusal);
        Assert.Equal("", refusal);
        return Assert.IsType<StandbyItem>(item);
    }

    private static string Lf(string text) => text.ReplaceLineEndings("\n");

    private static string Crlf(string text) => text.ReplaceLineEndings("\r\n");

    // --- the record and For ---------------------------------------------------

    [Fact]
    public void Two_roots_get_two_ids_and_two_paths_and_each_is_stable()
    {
        var one = Item(Tree("one", hatch: false));
        var two = Item(Tree("two", hatch: false));

        Assert.NotEqual(one.Id, two.Id);
        Assert.NotEqual(one.Log, two.Log);
        Assert.NotEqual(one.Wrapper, two.Wrapper);
        Assert.Equal(one.Id, Item(Tree("one", hatch: false)).Id);
        Assert.Equal("hatch-standby-" + Checkout.Fingerprint(Tree("one", hatch: false)), one.Id);
    }

    [Fact]
    public void The_same_checkout_gives_the_same_item_on_every_call()
    {
        var tree = Tree("one", hatch: true);

        var first = Item(tree, profile: "night");
        var second = Item(tree, profile: "night");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.Description, second.Description);
        Assert.Equal(first.Root, second.Root);
        Assert.Equal(first.Log, second.Log);
        Assert.Equal(first.Wrapper, second.Wrapper);
        Assert.Equal(first.Command, second.Command);
    }

    [Fact]
    public void The_runner_is_in_the_description_and_not_in_the_id()
    {
        var tree = Tree("one", hatch: false);
        var named = Item(tree);
        var renamed = StandbyItem.For(tree, "Birch", null, "/opt/hatch/hatch", false, Path.Combine(_temp, "home"), out _)!;

        Assert.Equal("Hatch standby for Alder", named.Description);
        Assert.Equal("Hatch standby for Birch", renamed.Description);
        Assert.Equal(named.Id, renamed.Id);
        Assert.DoesNotContain("Alder", named.Id + named.Log + named.Wrapper, StringComparison.Ordinal);
    }

    [Fact]
    public void The_log_and_the_wrapper_live_under_the_folder_it_was_given()
    {
        var folder = Path.Combine(_temp, "home");
        var tree = Tree("one", hatch: false);
        var fingerprint = Checkout.Fingerprint(tree);

        var posix = Item(tree);
        var windows = Item(tree, windows: true);

        Assert.Equal(Path.Combine(folder, "logs", $"standby-{fingerprint}.log"), posix.Log);
        Assert.Equal(Path.Combine(folder, "standby", $"hatch-standby-{fingerprint}.sh"), posix.Wrapper);
        Assert.Equal(Path.Combine(folder, "standby", $"hatch-standby-{fingerprint}.cmd"), windows.Wrapper);
        Assert.Equal(Path.GetFullPath(tree), posix.Root);
    }

    // --- the program ---------------------------------------------------------

    [Fact]
    public void A_hatch_checkout_starts_its_own_script()
    {
        var tree = Tree("own", hatch: true);
        var root = Path.GetFullPath(tree);

        Assert.Equal(
            ["bash", Path.Combine(root, "scripts", "hatch.sh"), "go-to-work", "--standby"],
            Item(tree).Command);
        Assert.Equal(
            ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                Path.Combine(root, "scripts", "hatch.ps1"), "go-to-work", "--standby"],
            Item(tree, windows: true).Command);
    }

    [Fact]
    public void Any_other_tree_starts_the_binary_that_is_running()
    {
        var tree = Tree("foreign", hatch: false);

        Assert.Equal(["/opt/hatch/hatch", "go-to-work", "--standby"], Item(tree).Command);
        Assert.Equal(["/opt/hatch/hatch", "go-to-work", "--standby"], Item(tree, windows: true).Command);
    }

    [Fact]
    public void A_script_without_the_source_beside_it_is_not_a_hatch_checkout()
    {
        var tree = Tree("half", hatch: true);
        Directory.Delete(Path.Combine(tree, "src", "Hatch.Cli"));

        Assert.Equal(["/opt/hatch/hatch", "go-to-work", "--standby"], Item(tree).Command);
    }

    [Theory]
    [InlineData("dotnet.exe")]
    [InlineData("dotnet")]
    [InlineData("DOTNET.EXE")]
    [InlineData("/usr/share/dotnet/dotnet")]
    [InlineData(null)]
    [InlineData("")]
    public void Under_the_dotnet_host_there_is_nothing_to_start(string? processPath)
    {
        var item = StandbyItem.For(Tree("foreign", hatch: false), "Alder", null, processPath, false, _temp, out var refusal);

        Assert.Null(item);
        Assert.Contains(Refusal, refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void The_dotnet_host_does_not_matter_inside_a_hatch_checkout()
    {
        var item = StandbyItem.For(Tree("own", hatch: true), "Alder", null, "/usr/share/dotnet/dotnet", false, _temp, out var refusal);

        Assert.NotNull(item);
        Assert.Equal("", refusal);
    }

    [Fact]
    public void A_profile_is_passed_on_only_when_one_was_given()
    {
        var tree = Tree("foreign", hatch: false);

        Assert.Equal(["/opt/hatch/hatch", "go-to-work", "--standby", "--profile", "night-2"], Item(tree, profile: "night-2").Command);
        Assert.Equal(["/opt/hatch/hatch", "go-to-work", "--standby"], Item(tree, profile: null).Command);
        Assert.Equal(["/opt/hatch/hatch", "go-to-work", "--standby"], Item(tree, profile: "").Command);
    }

    // --- paths ---------------------------------------------------------------

    [Fact]
    public void Each_platform_finds_the_item_where_it_looks()
    {
        var item = new StandbyItem("hatch-standby-abc", "d", "/r", "/l", "/w", []);

        Assert.Equal(Path.Combine("/h", "Library", "LaunchAgents", "hatch-standby-abc.plist"), item.LaunchAgentPath("/h"));
        Assert.Equal(Path.Combine("/c", "systemd", "user", "hatch-standby-abc.service"), item.SystemdUnitPath("/c"));
        Assert.Equal(Path.Combine("/c", "autostart", "hatch-standby-abc.desktop"), item.AutostartPath("/c"));
    }

    [Fact]
    public void The_config_home_is_the_xdg_one_only_when_it_is_absolute()
    {
        var absolute = Path.Combine(_temp, "xdg");

        Assert.Equal(absolute, StandbyItem.ConfigHome(absolute, "/h"));
        Assert.Equal(Path.Combine("/h", ".config"), StandbyItem.ConfigHome("relative/xdg", "/h"));
        Assert.Equal(Path.Combine("/h", ".config"), StandbyItem.ConfigHome("", "/h"));
        Assert.Equal(Path.Combine("/h", ".config"), StandbyItem.ConfigHome(null, "/h"));
    }

    // --- the text ------------------------------------------------------------

    private static StandbyItem PosixItem() => new(
        Id: "hatch-standby-0123456789ab",
        Description: "Hatch standby for Al & Bo's <loop> 100%",
        Root: "/srv/a b/100%/it's",
        Log: "/srv/a b/100%/it's/standby.log",
        Wrapper: "/srv/a b/100%/it's/$HOME/\"q\"\\w & <x>.sh",
        Command: ["bash", "/srv/a b/100%/it's/scripts/hatch.sh", "go-to-work", "--standby", "--profile", "night"]);

    private static StandbyItem WindowsItem() => new(
        Id: "hatch-standby-0123456789ab",
        Description: "Hatch standby for Al & Bo's <loop> 100%",
        Root: @"C:\a b\100%\it's",
        Log: @"C:\a b\100%\it's\standby.log",
        Wrapper: @"C:\a b\100%\it's\w & <x>.cmd",
        Command:
        [
            "powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
            @"C:\a b\100%\it's\scripts\hatch.ps1", "go-to-work", "--standby",
        ]);

    [Fact]
    public void The_windows_wrapper_is_crlf_and_quotes_what_a_batch_file_needs_quoted()
    {
        var expected = Crlf("""
            @echo off
            cd /d "C:\a b\100%%\it's"
            powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\a b\100%%\it's\scripts\hatch.ps1" go-to-work --standby > "C:\a b\100%%\it's\standby.log" 2>&1

            """);

        Assert.Equal(expected, StandbyItem.WindowsWrapper(WindowsItem()));
    }

    [Fact]
    public void A_windows_argument_of_safe_characters_is_bare_and_a_percent_is_doubled()
    {
        var item = WindowsItem() with { Command = [@"C:\x\hatch.exe", "go-to-work", "100%", "a&b", ""] };

        Assert.Contains(
            """C:\x\hatch.exe go-to-work "100%%" "a&b" "" > """,
            StandbyItem.WindowsWrapper(item),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_posix_wrapper_is_lf_with_the_path_and_the_checkout_and_the_log()
    {
        var expected = Lf("""
            #!/bin/sh
            PATH='/usr/local/bin:/opt/it'\''s/bin'; export PATH
            cd '/srv/a b/100%/it'\''s' || exit 1
            exec bash '/srv/a b/100%/it'\''s/scripts/hatch.sh' go-to-work --standby --profile night > '/srv/a b/100%/it'\''s/standby.log' 2>&1

            """);

        Assert.Equal(expected, StandbyItem.PosixWrapper(PosixItem(), "/usr/local/bin:/opt/it's/bin"));
    }

    [Fact]
    public void A_posix_argument_of_safe_characters_is_bare()
    {
        var item = PosixItem() with { Command = ["/opt/h/hatch", "a@b+c=d,e:f-g_h.i", "$x", ""] };

        Assert.Contains(
            "exec /opt/h/hatch a@b+c=d,e:f-g_h.i '$x' '' > ",
            StandbyItem.PosixWrapper(item, "/bin"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_task_xml_is_whole_and_escapes_what_xml_needs()
    {
        var expected = Lf("""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Hatch standby for Al &amp; Bo's &lt;loop&gt; 100%</Description></RegistrationInfo>
              <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>WORK&amp;GROUP\pat</UserId></LogonTrigger></Triggers>
              <Principals><Principal id="Author"><UserId>WORK&amp;GROUP\pat</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
              <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><Enabled>true</Enabled><ExecutionTimeLimit>PT0S</ExecutionTimeLimit></Settings>
              <Actions Context="Author"><Exec><Command>conhost.exe</Command><Arguments>--headless cmd.exe /d /c ""C:\a b\100%\it's\w &amp; &lt;x&gt;.cmd""</Arguments></Exec></Actions>
            </Task>

            """);

        Assert.Equal(expected, StandbyItem.TaskXml(WindowsItem(), "WORK&GROUP", "pat"));
    }

    [Fact]
    public void The_launchd_plist_is_whole_and_runs_the_wrapper_at_load_without_keepalive()
    {
        var expected = Lf("""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>Label</key>
              <string>hatch-standby-0123456789ab</string>
              <key>ProgramArguments</key>
              <array>
                <string>/bin/sh</string>
                <string>/srv/a b/100%/it's/$HOME/"q"\w &amp; &lt;x&gt;.sh</string>
              </array>
              <key>RunAtLoad</key>
              <true/>
            </dict>
            </plist>

            """);

        var plist = StandbyItem.LaunchdPlist(PosixItem());

        Assert.Equal(expected, plist);
        Assert.DoesNotContain("KeepAlive", plist, StringComparison.Ordinal);
    }

    [Fact]
    public void The_systemd_unit_is_whole_and_escapes_what_systemd_expands()
    {
        var expected = Lf("""
            [Unit]
            Description=Hatch standby for Al & Bo's <loop> 100%%

            [Service]
            Type=simple
            ExecStart=/bin/sh "/srv/a b/100%%/it's/$$HOME/\"q\"\\w & <x>.sh"

            [Install]
            WantedBy=default.target

            """);

        Assert.Equal(expected, StandbyItem.SystemdUnit(PosixItem()));
    }

    [Fact]
    public void The_desktop_entry_is_whole_and_quotes_exec_twice()
    {
        // \ " ` $ each get a backslash and then every backslash is doubled, so
        // $ is \\$, " is \\" and a backslash is four.
        var expected = Lf("""
            [Desktop Entry]
            Type=Application
            Name=Hatch standby for Al & Bo's <loop> 100%
            Exec=/bin/sh "/srv/a b/100%%/it's/\\$HOME/\\"q\\"\\\\w & <x>.sh"

            """);

        Assert.Equal(expected, StandbyItem.DesktopEntry(PosixItem()));
    }

    [Fact]
    public void The_desktop_entry_quotes_a_backtick()
    {
        var item = PosixItem() with { Wrapper = "/a`b.sh" };

        Assert.Contains("Exec=/bin/sh \"/a\\\\`b.sh\"", StandbyItem.DesktopEntry(item), StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_generated_names_the_account_it_was_not_given()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(profile)) return;

        var posix = PosixItem();
        var windows = WindowsItem();
        string[] outputs =
        [
            StandbyItem.WindowsWrapper(windows),
            StandbyItem.PosixWrapper(posix, "/usr/bin"),
            StandbyItem.TaskXml(windows, "WORK", "pat"),
            StandbyItem.LaunchdPlist(posix),
            StandbyItem.SystemdUnit(posix),
            StandbyItem.DesktopEntry(posix),
        ];

        Assert.All(outputs, text => Assert.DoesNotContain(profile, text, StringComparison.OrdinalIgnoreCase));
    }
}

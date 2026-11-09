using System.Diagnostics;

namespace Hatch.Cli.Tests;

/// <summary>
/// Whether the tools a project may declare are on <c>PATH</c>, and which
/// version - answered within a probe that cannot outlast its timeout.
/// </summary>
public sealed class DetectTests
{
    private const string DebianMake =
        "GNU Make 4.3\n" +
        "Built for x86_64-pc-linux-gnu\n" +
        "Copyright (C) 1988-2020 Free Software Foundation, Inc.\n" +
        "License GPLv3+: GNU GPL version 3 or later <http://gnu.org/licenses/gpl.html>\n" +
        "This is free software: you are free to change and redistribute it.\n" +
        "There is NO WARRANTY, to the extent permitted by law.\n";

    private const string MacMake =
        "GNU Make 3.81\n" +
        "Copyright (C) 2006  Free Software Foundation, Inc.\n" +
        "This is free software; see the source for copying conditions.\n" +
        "There is NO warranty; not even for MERCHANTABILITY or FITNESS FOR A\n" +
        "PARTICULAR PURPOSE.\n" +
        "\n" +
        "This program built for i386-apple-darwin11.3.0\n";

    private const string DotnetSdks =
        "6.0.428 [C:\\Program Files\\dotnet\\sdk]\r\n" +
        "9.0.304 [C:\\Program Files\\dotnet\\sdk]\r\n" +
        "9.0.100-rc.2.24474.11 [C:\\Program Files\\dotnet\\sdk]\r\n" +
        "10.0.100 [C:\\Program Files\\dotnet\\sdk]\r\n";

    private const string Node = "v22.11.0\n";

    private const string Pwsh = "PowerShell 7.5.0\n";

    /// <summary>A directory of empty files standing in for a <c>PATH</c> entry.</summary>
    private sealed class FakePath : IDisposable
    {
        public string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hatch-detect-" + Guid.NewGuid().ToString("N"));

        public FakePath(string fileName, bool executable = true)
        {
            System.IO.Directory.CreateDirectory(Directory);
            File = System.IO.Path.GetFullPath(System.IO.Path.Combine(Directory, fileName));
            System.IO.File.WriteAllText(File, "");

            if (!OperatingSystem.IsWindows() && executable)
                System.IO.File.SetUnixFileMode(File, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        /// <summary>What <see cref="Reach.Find"/> should resolve to.</summary>
        public string File { get; }

        public void Dispose()
        {
            try { System.IO.Directory.Delete(Directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static Probe Answering(string output, int exit = 0, List<(string File, string[] Args)>? calls = null) =>
        (file, args, _, _) =>
        {
            calls?.Add((file, args));
            return Task.FromResult(new ProbeResult(true, exit, output));
        };

    private static readonly Probe TimesOut = (_, _, _, _) => Task.FromResult(new ProbeResult(false, -1, ""));

    private static readonly Probe NeverAsked = (_, _, _, _) => throw new InvalidOperationException("nothing is on PATH to ask");

    private static string[] FileNames(string name) =>
        OperatingSystem.IsWindows() ? [$"{name}.exe", $"{name}.cmd"] : [name];

    // ---- parsers ----------------------------------------------------------

    [Theory]
    [InlineData(DebianMake, "4.3")]
    [InlineData(MacMake, "3.81")]
    public void Make_reads_the_first_line(string output, string expected)
    {
        var version = Detect.ParseMake(output);

        Assert.Equal(expected, version);
        Assert.Null(DependencyVersion.Problem(version));
    }

    [Fact]
    public void Dotnet_reads_the_highest_sdk_by_component_not_by_text()
    {
        var version = Detect.ParseDotnet(DotnetSdks);

        Assert.Equal("10.0.100", version);
        Assert.Null(DependencyVersion.Problem(version));
    }

    [Fact]
    public void Dotnet_reads_a_prerelease_as_its_release()
    {
        Assert.Equal("9.0.100", Detect.ParseDotnet("9.0.100-rc.2.24474.11 [/usr/share/dotnet/sdk]\n"));
    }

    [Fact]
    public void Dotnet_skips_lines_that_are_not_sdks()
    {
        const string output = "\nWarning: something to say first\n8.0.404 [/usr/share/dotnet/sdk]\n";

        Assert.Equal("8.0.404", Detect.ParseDotnet(output));
    }

    [Fact]
    public void Node_drops_the_v()
    {
        var version = Detect.ParseNode(Node);

        Assert.Equal("22.11.0", version);
        Assert.Null(DependencyVersion.Problem(version));
    }

    [Theory]
    [InlineData(Pwsh, "7.5.0")]
    [InlineData("7.5.0-preview.2\n", "7.5.0")]
    public void Pwsh_reads_a_release_or_a_prerelease(string output, string expected)
    {
        var version = Detect.ParsePwsh(output);

        Assert.Equal(expected, version);
        Assert.Null(DependencyVersion.Problem(version));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n\n")]
    [InlineData("no digits here\n")]
    [InlineData(null)]
    public void A_parser_with_nothing_to_read_answers_null_and_never_throws(string? output)
    {
        Assert.Null(Detect.ParseMake(output));
        Assert.Null(Detect.ParseDotnet(output));
        Assert.Null(Detect.ParseNode(output));
        Assert.Null(Detect.ParsePwsh(output));
    }

    [Fact]
    public void A_version_is_clipped_to_three_components_of_nine_digits()
    {
        var version = Detect.ParseNode("v1.2.3.4\n");
        Assert.Equal("1.2.3", version);

        var wide = Detect.ParseNode("v12345678901234.5\n");
        Assert.Equal("123456789.5", wide);
        Assert.Null(DependencyVersion.Problem(wide));
    }

    [Fact]
    public void Only_ascii_digits_are_a_version()
    {
        Assert.Null(Detect.ParseNode("v\u0663.\u0664\n"));
    }

    // ---- detectors --------------------------------------------------------

    [Fact]
    public async Task Make_is_found_and_asked_for_its_version()
    {
        using var fake = new FakePath(FileNames("make")[0]);
        var calls = new List<(string File, string[] Args)>();

        var result = await Detect.Make(fake.Directory, Answering(DebianMake, calls: calls), CancellationToken.None);

        Assert.Equal(new DetectorResult("make", true, "4.3", null), result);
        var call = Assert.Single(calls);
        Assert.Equal(fake.File, call.File);
        Assert.Equal(["--version"], call.Args);
    }

    [Fact]
    public async Task Dotnet_is_found_and_asked_for_its_sdks()
    {
        using var fake = new FakePath(FileNames("dotnet")[0]);
        var calls = new List<(string File, string[] Args)>();

        var result = await Detect.Dotnet(fake.Directory, Answering(DotnetSdks, calls: calls), CancellationToken.None);

        Assert.Equal(new DetectorResult("dotnet", true, "10.0.100", null), result);
        var call = Assert.Single(calls);
        Assert.Equal(fake.File, call.File);
        Assert.Equal(["--list-sdks"], call.Args);
    }

    [Fact]
    public async Task Node_is_found_and_asked_for_its_version()
    {
        using var fake = new FakePath(FileNames("node")[0]);
        var calls = new List<(string File, string[] Args)>();

        var result = await Detect.Node(fake.Directory, Answering(Node, calls: calls), CancellationToken.None);

        Assert.Equal(new DetectorResult("node", true, "22.11.0", null), result);
        var call = Assert.Single(calls);
        Assert.Equal(fake.File, call.File);
        Assert.Equal(["--version"], call.Args);
    }

    [Fact]
    public async Task Pwsh_is_found_and_asked_for_its_version()
    {
        using var fake = new FakePath(FileNames("pwsh")[0]);
        var calls = new List<(string File, string[] Args)>();

        var result = await Detect.Pwsh(fake.Directory, Answering(Pwsh, calls: calls), CancellationToken.None);

        Assert.Equal(new DetectorResult("pwsh", true, "7.5.0", null), result);
        var call = Assert.Single(calls);
        Assert.Equal(fake.File, call.File);
        Assert.Equal(["--version"], call.Args);
    }

    [Fact]
    public async Task On_windows_a_cmd_is_found_too()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var fake = new FakePath("node.cmd");

        var result = await Detect.Node(fake.Directory, Answering(Node), CancellationToken.None);

        Assert.True(result.Present);
    }

    [Fact]
    public async Task On_unix_the_bare_name_is_found()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fake = new FakePath("pwsh");

        var result = await Detect.Pwsh(fake.Directory, Answering(Pwsh), CancellationToken.None);

        Assert.True(result.Present);
    }

    [Fact]
    public async Task On_unix_a_file_that_cannot_be_run_is_absent()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fake = new FakePath("make", executable: false);

        var result = await Detect.Make(fake.Directory, NeverAsked, CancellationToken.None);

        Assert.False(result.Present);
        Assert.Equal("`make` is not on PATH", result.Why);
    }

    [Fact]
    public async Task A_tool_that_is_not_on_path_is_absent_and_never_asked()
    {
        using var fake = new FakePath(FileNames("something-else")[0]);

        var results = new[]
        {
            await Detect.Make(fake.Directory, NeverAsked, CancellationToken.None),
            await Detect.Dotnet(fake.Directory, NeverAsked, CancellationToken.None),
            await Detect.Node(fake.Directory, NeverAsked, CancellationToken.None),
            await Detect.Pwsh(fake.Directory, NeverAsked, CancellationToken.None),
        };

        Assert.Equal(["make", "dotnet", "node", "pwsh"], results.Select(result => result.Id));
        Assert.All(results, result =>
        {
            Assert.False(result.Present);
            Assert.Null(result.Version);
            Assert.Equal($"`{result.Id}` is not on PATH", result.Why);
        });
    }

    [Fact]
    public async Task A_missing_path_is_absent()
    {
        var result = await Detect.Make(null, NeverAsked, CancellationToken.None);

        Assert.False(result.Present);
    }

    [Fact]
    public async Task A_probe_that_did_not_complete_is_absent_and_says_how_long_it_waited()
    {
        using var fake = new FakePath(FileNames("make")[0]);

        var result = await Detect.Make(fake.Directory, TimesOut, CancellationToken.None);

        Assert.False(result.Present);
        Assert.Null(result.Version);
        Assert.Equal("`make --version` did not answer within 5 seconds", result.Why);
    }

    [Fact]
    public async Task The_dotnet_sentence_names_the_flag_it_asked_with()
    {
        using var fake = new FakePath(FileNames("dotnet")[0]);

        var result = await Detect.Dotnet(fake.Directory, TimesOut, CancellationToken.None);

        Assert.Contains("`dotnet --list-sdks`", result.Why);
    }

    [Fact]
    public async Task A_non_zero_exit_is_absent()
    {
        using var fake = new FakePath(FileNames("node")[0]);

        var result = await Detect.Node(fake.Directory, Answering("v22.11.0\n", exit: 127), CancellationToken.None);

        Assert.False(result.Present);
        Assert.Null(result.Version);
        Assert.False(string.IsNullOrWhiteSpace(result.Why));
    }

    [Fact]
    public async Task A_tool_that_answers_with_no_version_is_present_with_none()
    {
        using var fake = new FakePath(FileNames("make")[0]);

        var result = await Detect.Make(fake.Directory, Answering("something unexpected\n"), CancellationToken.None);

        Assert.Equal(new DetectorResult("make", true, null, null), result);
    }

    // ---- playwright / postgres --------------------------------------------

    private static Func<string, string?> Env(params (string Name, string Value)[] pairs)
    {
        var map = pairs.ToDictionary(pair => pair.Name, pair => pair.Value);
        return name => map.TryGetValue(name, out var value) ? value : null;
    }

    /// <summary>Answers by the tool's name without its extension, and records each name it was asked.</summary>
    private static Probe ByTool(Dictionary<string, ProbeResult> answers, List<string>? asked = null, List<string[]>? args = null) =>
        (file, probeArgs, _, _) =>
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(file);
            asked?.Add(name);
            args?.Add(probeArgs);
            return Task.FromResult(answers[name]);
        };

    private static ProbeResult Said(string output, int exit = 0) => new(true, exit, output);

    private static readonly ProbeResult Hung = new(false, -1, "");

    private const string PostgresServer = "postgres (PostgreSQL) 17.2 (Debian 17.2-1.pgdg120+1)\n";

    private const string PgCtl = "pg_ctl (PostgreSQL) 16.4\n";

    private const string PgIsReady = "pg_isready (PostgreSQL) 16.4\n";

    private const string NoPostgres =
        "no `postgres`, `pg_ctl` or `pg_isready` on PATH, and no Docker daemon answered `docker info`";

    private static string Root()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hatch-detect-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(root);
        return root;
    }

    private static void Remove(string root)
    {
        try { System.IO.Directory.Delete(root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Makes <paramref name="dir"/> and puts a file, or only a subdirectory, in it.</summary>
    private static void Populate(string dir, bool subdirectory = false)
    {
        System.IO.Directory.CreateDirectory(dir);
        if (subdirectory) System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dir, "chromium-1148"));
        else System.IO.File.WriteAllText(System.IO.Path.Combine(dir, ".links"), "");
    }

    [Theory]
    [InlineData("windows", "LOCALAPPDATA", new string[] { "ms-playwright" }, false)]
    [InlineData("windows", "LOCALAPPDATA", new string[] { "ms-playwright" }, true)]
    [InlineData("macos", "HOME", new string[] { "Library", "Caches", "ms-playwright" }, false)]
    [InlineData("macos", "HOME", new string[] { "Library", "Caches", "ms-playwright" }, true)]
    [InlineData("linux", "HOME", new string[] { ".cache", "ms-playwright" }, false)]
    [InlineData("linux", "HOME", new string[] { ".cache", "ms-playwright" }, true)]
    public void Playwright_is_found_in_its_default_cache(string os, string variable, string[] below, bool subdirectory)
    {
        var root = Root();
        try
        {
            Populate(System.IO.Path.Combine([root, .. below]), subdirectory);

            var result = Detect.PlaywrightBrowsers(os, Env((variable, root)));

            Assert.Equal(new DetectorResult("playwright-browsers", true, null, null), result);
        }
        finally { Remove(root); }
    }

    [Theory]
    [InlineData("windows", "LOCALAPPDATA", new string[] { "ms-playwright" })]
    [InlineData("macos", "HOME", new string[] { "Library", "Caches", "ms-playwright" })]
    [InlineData("linux", "HOME", new string[] { ".cache", "ms-playwright" })]
    public void Playwright_is_absent_when_the_cache_is_missing_or_empty(string os, string variable, string[] below)
    {
        var root = Root();
        try
        {
            var dir = System.IO.Path.Combine([root, .. below]);

            var missing = Detect.PlaywrightBrowsers(os, Env((variable, root)));
            System.IO.Directory.CreateDirectory(dir);
            var empty = Detect.PlaywrightBrowsers(os, Env((variable, root)));

            foreach (var result in new[] { missing, empty })
            {
                Assert.Equal("playwright-browsers", result.Id);
                Assert.False(result.Present);
                Assert.Null(result.Version);
                Assert.Contains(dir, result.Why);
                Assert.Contains("missing or empty", result.Why);
            }
        }
        finally { Remove(root); }
    }

    [Fact]
    public void Playwright_browsers_path_overrides_a_populated_default()
    {
        var root = Root();
        try
        {
            Populate(System.IO.Path.Combine(root, ".cache", "ms-playwright"));
            var elsewhere = System.IO.Path.Combine(root, "elsewhere");

            var result = Detect.PlaywrightBrowsers("linux", Env(("HOME", root), ("PLAYWRIGHT_BROWSERS_PATH", elsewhere)));

            Assert.False(result.Present);
            Assert.Contains(elsewhere, result.Why);

            Populate(elsewhere);
            Assert.True(Detect.PlaywrightBrowsers("linux", Env(("HOME", root), ("PLAYWRIGHT_BROWSERS_PATH", elsewhere))).Present);
        }
        finally { Remove(root); }
    }

    [Fact]
    public void Playwright_browsers_path_set_without_a_home_is_still_read()
    {
        var root = Root();
        try
        {
            Populate(root);

            Assert.True(Detect.PlaywrightBrowsers("windows", Env(("PLAYWRIGHT_BROWSERS_PATH", root))).Present);
        }
        finally { Remove(root); }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_playwright_browsers_path_is_ignored(string blank)
    {
        var root = Root();
        try
        {
            Populate(System.IO.Path.Combine(root, ".cache", "ms-playwright"));

            var result = Detect.PlaywrightBrowsers("linux", Env(("HOME", root), ("PLAYWRIGHT_BROWSERS_PATH", blank)));

            Assert.True(result.Present);
        }
        finally { Remove(root); }
    }

    [Theory]
    [InlineData("0")]
    [InlineData(" 0 ")]
    public void Playwright_browsers_path_zero_is_absent_even_when_the_default_exists(string zero)
    {
        var root = Root();
        try
        {
            Populate(System.IO.Path.Combine(root, ".cache", "ms-playwright"));

            var result = Detect.PlaywrightBrowsers("linux", Env(("HOME", root), ("PLAYWRIGHT_BROWSERS_PATH", zero)));

            Assert.False(result.Present);
            Assert.Contains("node_modules", result.Why);
            Assert.Contains("HATCH_HAS", result.Why);
        }
        finally { Remove(root); }
    }

    [Fact]
    public void On_linux_xdg_cache_home_is_honoured_over_home()
    {
        var root = Root();
        try
        {
            var xdg = System.IO.Path.Combine(root, "xdg");
            Populate(System.IO.Path.Combine(root, ".cache", "ms-playwright"));

            var missing = Detect.PlaywrightBrowsers("linux", Env(("HOME", root), ("XDG_CACHE_HOME", xdg)));
            Assert.False(missing.Present);
            Assert.Contains(System.IO.Path.Combine(xdg, "ms-playwright"), missing.Why);

            Populate(System.IO.Path.Combine(xdg, "ms-playwright"));
            Assert.True(Detect.PlaywrightBrowsers("linux", Env(("HOME", root), ("XDG_CACHE_HOME", xdg))).Present);
        }
        finally { Remove(root); }
    }

    [Fact]
    public void On_linux_a_blank_xdg_cache_home_falls_back_to_home()
    {
        var root = Root();
        try
        {
            Populate(System.IO.Path.Combine(root, ".cache", "ms-playwright"));

            Assert.True(Detect.PlaywrightBrowsers("linux", Env(("HOME", root), ("XDG_CACHE_HOME", " "))).Present);
        }
        finally { Remove(root); }
    }

    [Theory]
    [InlineData("windows", "LOCALAPPDATA")]
    [InlineData("macos", "HOME")]
    [InlineData("linux", "HOME")]
    public void Playwright_is_absent_when_the_variable_that_locates_the_cache_is_not_set(string os, string variable)
    {
        foreach (var env in new[] { Env(), Env((variable, "  ")) })
        {
            var result = Detect.PlaywrightBrowsers(os, env);

            Assert.False(result.Present);
            Assert.Equal($"`{variable}` is not set, so there is no Playwright cache to look in", result.Why);
        }
    }

    [Fact]
    public void An_unknown_os_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Detect.PlaywrightBrowsers("freebsd", Env()));
    }

    [Theory]
    [InlineData("postgres (PostgreSQL) 17.2 (Debian 17.2-1.pgdg120+1)\n", "17.2")]
    [InlineData("pg_ctl (PostgreSQL) 16.4\n", "16.4")]
    [InlineData("postgres (PostgreSQL) 18devel\n", "18")]
    [InlineData("postgres (PostgreSQL) 17beta1\n", "17")]
    [InlineData("(PostgreSQL) 16.4", "16.4")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("psql 17", null)]
    [InlineData("Client: Docker Engine 27.3.1\n", null)]
    public void Postgres_reads_the_number_after_its_marker(string? output, string? expected)
    {
        var version = Detect.ParsePostgres(output);

        Assert.Equal(expected, version);
        Assert.Null(DependencyVersion.Problem(version));
    }

    [Theory]
    [InlineData("postgres", PostgresServer, "17.2")]
    [InlineData("pg_ctl", PgCtl, "16.4")]
    [InlineData("pg_isready", PgIsReady, "16.4")]
    public async Task Postgres_is_found_by_any_one_of_its_binaries(string name, string output, string expected)
    {
        using var fake = new FakePath(FileNames(name)[0]);
        var asked = new List<string>();
        var args = new List<string[]>();

        var result = await Detect.Postgres(fake.Directory, ByTool(new() { [name] = Said(output) }, asked, args), CancellationToken.None);

        Assert.Equal(new DetectorResult("postgres", true, expected, null), result);
        Assert.Equal([name], asked);
        Assert.Equal(["--version"], Assert.Single(args));
    }

    [Fact]
    public async Task Postgres_asks_postgres_before_pg_ctl()
    {
        using var first = new FakePath(FileNames("postgres")[0]);
        using var second = new FakePath(FileNames("pg_ctl")[0]);
        var asked = new List<string>();
        var answers = new Dictionary<string, ProbeResult> { ["postgres"] = Said(PostgresServer), ["pg_ctl"] = Said(PgCtl) };

        var result = await Detect.Postgres(
            string.Join(System.IO.Path.PathSeparator, first.Directory, second.Directory), ByTool(answers, asked), CancellationToken.None);

        Assert.Equal("17.2", result.Version);
        Assert.Equal(["postgres"], asked);
    }

    [Fact]
    public async Task A_binary_beats_docker_and_docker_is_never_asked()
    {
        using var binary = new FakePath(FileNames("pg_isready")[0]);
        using var docker = new FakePath(FileNames("docker")[0]);
        var asked = new List<string>();
        var answers = new Dictionary<string, ProbeResult> { ["pg_isready"] = Said(PgIsReady), ["docker"] = Said("") };

        var result = await Detect.Postgres(
            string.Join(System.IO.Path.PathSeparator, binary.Directory, docker.Directory), ByTool(answers, asked), CancellationToken.None);

        Assert.True(result.Present);
        Assert.DoesNotContain("docker", asked);
    }

    [Fact]
    public async Task A_binary_that_prints_no_version_is_present_with_none()
    {
        using var fake = new FakePath(FileNames("postgres")[0]);

        var result = await Detect.Postgres(fake.Directory, ByTool(new() { ["postgres"] = Said("something unexpected\n") }), CancellationToken.None);

        Assert.Equal(new DetectorResult("postgres", true, null, null), result);
    }

    [Fact]
    public async Task With_no_binary_a_docker_daemon_that_answers_is_present_with_no_version()
    {
        using var fake = new FakePath(FileNames("docker")[0]);
        var args = new List<string[]>();

        var result = await Detect.Postgres(fake.Directory, ByTool(new() { ["docker"] = Said("Client: Docker Engine 27.3.1\n") }, args: args), CancellationToken.None);

        Assert.Equal(new DetectorResult("postgres", true, null, null), result);
        Assert.Equal(["info"], Assert.Single(args));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_docker_that_fails_or_hangs_is_absent_with_both_routes_named(bool hangs)
    {
        using var fake = new FakePath(FileNames("docker")[0]);

        var result = await Detect.Postgres(fake.Directory, ByTool(new() { ["docker"] = hangs ? Hung : Said("", exit: 1) }), CancellationToken.None);

        Assert.Equal(new DetectorResult("postgres", false, null, NoPostgres), result);
    }

    [Fact]
    public async Task With_nothing_on_path_postgres_is_absent_and_nothing_is_asked()
    {
        using var fake = new FakePath(FileNames("something-else")[0]);

        var result = await Detect.Postgres(fake.Directory, NeverAsked, CancellationToken.None);
        var none = await Detect.Postgres(null, NeverAsked, CancellationToken.None);

        Assert.Equal(new DetectorResult("postgres", false, null, NoPostgres), result);
        Assert.Equal(result, none);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_binary_that_hangs_or_fails_leaves_the_next_to_answer(bool hangs)
    {
        using var first = new FakePath(FileNames("postgres")[0]);
        using var second = new FakePath(FileNames("pg_ctl")[0]);
        var asked = new List<string>();
        var answers = new Dictionary<string, ProbeResult>
        {
            ["postgres"] = hangs ? Hung : Said("postgres: error\n", exit: 1),
            ["pg_ctl"] = Said(PgCtl),
        };

        var result = await Detect.Postgres(
            string.Join(System.IO.Path.PathSeparator, first.Directory, second.Directory), ByTool(answers, asked), CancellationToken.None);

        Assert.Equal(new DetectorResult("postgres", true, "16.4", null), result);
        Assert.Equal(["postgres", "pg_ctl"], asked);
    }

    [Fact]
    public async Task A_binary_that_hangs_leaves_docker_to_answer()
    {
        using var binary = new FakePath(FileNames("postgres")[0]);
        using var docker = new FakePath(FileNames("docker")[0]);
        var answers = new Dictionary<string, ProbeResult> { ["postgres"] = Hung, ["docker"] = Said("") };

        var result = await Detect.Postgres(
            string.Join(System.IO.Path.PathSeparator, binary.Directory, docker.Directory), ByTool(answers), CancellationToken.None);

        Assert.Equal(new DetectorResult("postgres", true, null, null), result);
    }

    [Fact]
    public async Task A_cancelled_postgres_probe_propagates()
    {
        using var fake = new FakePath(FileNames("postgres")[0]);
        Probe cancelled = (_, _, _, _) => throw new OperationCanceledException();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Detect.Postgres(fake.Directory, cancelled, CancellationToken.None));
    }

    // ---- the real probe ---------------------------------------------------

    [Fact]
    public async Task A_real_probe_answers_what_the_tool_printed()
    {
        if (OperatingSystem.IsWindows()) return;

        var result = await Detect.RealProbe("/bin/sh", ["-c", "echo out; echo err >&2; exit 3"], TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal(3, result.ExitCode);
        Assert.Equal("out\nerr\n", result.Output);
    }

    [Fact]
    public async Task A_real_probe_keeps_the_first_64_kilobytes_and_does_not_block_on_the_rest()
    {
        if (OperatingSystem.IsWindows()) return;

        var result = await Detect.RealProbe("/bin/sh", ["-c", "head -c 300000 /dev/zero | tr '\\0' x"], TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal(64 * 1024, result.Output.Length);
    }

    [Fact]
    public async Task A_real_probe_of_a_file_that_will_not_start_did_not_complete()
    {
        var missing = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hatch-detect-" + Guid.NewGuid().ToString("N"), "nothing");

        var result = await Detect.RealProbe(missing, ["--version"], TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.False(result.Completed);
    }

    [Fact]
    public async Task A_real_probe_cannot_outlast_its_timeout()
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hatch-detect-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);

        try
        {
            string file;
            string[] args;
            if (OperatingSystem.IsWindows())
            {
                file = System.IO.Path.Combine(directory, "sleeper.cmd");
                System.IO.File.WriteAllText(file, "@echo off\r\nping -n 30 127.0.0.1 >nul\r\n");
                args = [];
            }
            else
            {
                // Handed to sh rather than executed, so the script needs no execute bit.
                var script = System.IO.Path.Combine(directory, "sleeper.sh");
                System.IO.File.WriteAllText(script, "sleep 30\n");
                file = "/bin/sh";
                args = [script];
            }

            var watch = Stopwatch.StartNew();
            var result = await Detect.RealProbe(file, args, TimeSpan.FromMilliseconds(300), CancellationToken.None);
            watch.Stop();

            Assert.False(result.Completed);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"the probe took {watch.Elapsed}");
        }
        finally
        {
            try { System.IO.Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task A_real_probe_stops_when_its_caller_does()
    {
        if (OperatingSystem.IsWindows()) return;

        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Detect.RealProbe("/bin/sh", ["-c", "sleep 30"], TimeSpan.FromSeconds(30), cancel.Token));
    }
}

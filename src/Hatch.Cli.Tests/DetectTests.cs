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

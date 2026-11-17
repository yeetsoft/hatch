using System.Reflection;

namespace Hatch.Cli.Tests;

/// <summary>
/// The ten machine facts a beat carries: read once, each null where it
/// could not be read.
/// </summary>
public sealed class MachineFactsTests
{
    private static Probe Answering(string output, int exit = 0, List<(string File, string[] Args)>? calls = null) =>
        (file, args, _, _) =>
        {
            calls?.Add((file, args));
            return Task.FromResult(new ProbeResult(true, exit, output));
        };

    // ---- ShortOs ------------------------------------------------------

    [Theory]
    [InlineData(
        "Darwin 23.6.0 Darwin Kernel Version 23.6.0: Mon Jul 29 21:16:46 PDT 2024; root:xnu-10063~1/RELEASE_ARM64_ARM64",
        "Darwin 23.6.0")]
    [InlineData(
        "Linux 6.5.0-1018-azure #18~22.04.1-Ubuntu SMP Mon Apr 22 12:43:22 UTC 2024",
        "Linux 6.5.0-1018-azure")]
    [InlineData("Microsoft Windows 10.0.22631", "Microsoft Windows 10.0.22631")]
    public void ShortOs_keeps_the_name_and_version(string description, string expected)
    {
        Assert.Equal(expected, MachineFacts.ShortOs(description));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ShortOs_is_null_for_blank_or_null(string? description)
    {
        Assert.Null(MachineFacts.ShortOs(description));
    }

    [Fact]
    public void ShortOs_truncates_to_64()
    {
        var token = new string('a', 200);

        Assert.Equal(token[..64], MachineFacts.ShortOs(token));
    }

    // ---- InContainer ----------------------------------------------------

    [Fact]
    public void InContainer_is_true_for_dockerenv_alone()
    {
        Assert.True(MachineFacts.InContainer(path => path == "/.dockerenv", _ => null));
    }

    [Theory]
    [InlineData("0::/system.slice/docker-abc.scope")]
    [InlineData("0::/kubepods/pod123")]
    public void InContainer_is_true_for_a_matching_cgroup(string cgroup)
    {
        Assert.True(MachineFacts.InContainer(_ => false, _ => cgroup));
    }

    [Fact]
    public void InContainer_is_false_for_an_unrelated_cgroup()
    {
        Assert.False(MachineFacts.InContainer(_ => false, _ => "0::/user.slice/user-1000.slice"));
    }

    [Fact]
    public void InContainer_is_false_for_a_throwing_reader()
    {
        Assert.False(MachineFacts.InContainer(_ => false, _ => throw new IOException("no such file")));
    }

    // ---- AgentVersionAsync ------------------------------------------------

    [Fact]
    public async Task AgentVersionAsync_reads_the_first_line_on_a_clean_exit()
    {
        var calls = new List<(string File, string[] Args)>();

        var version = await MachineFacts.AgentVersionAsync("claude", Answering("2.1.5 (Claude Code)\n", calls: calls), CancellationToken.None);

        Assert.Equal("2.1.5 (Claude Code)", version);
        var call = Assert.Single(calls);
        Assert.Equal(["--version"], call.Args);
    }

    [Fact]
    public async Task AgentVersionAsync_is_null_for_a_nonzero_exit()
    {
        var version = await MachineFacts.AgentVersionAsync("claude", Answering("2.1.5\n", exit: 1), CancellationToken.None);

        Assert.Null(version);
    }

    [Fact]
    public async Task AgentVersionAsync_is_null_when_the_probe_did_not_complete()
    {
        Probe timesOut = (_, _, _, _) => Task.FromResult(new ProbeResult(false, -1, ""));

        var version = await MachineFacts.AgentVersionAsync("claude", timesOut, CancellationToken.None);

        Assert.Null(version);
    }

    [Fact]
    public async Task AgentVersionAsync_is_null_for_a_throwing_probe()
    {
        Probe throws = (_, _, _, _) => throw new InvalidOperationException("boom");

        var version = await MachineFacts.AgentVersionAsync("claude", throws, CancellationToken.None);

        Assert.Null(version);
    }

    [Fact]
    public async Task AgentVersionAsync_is_null_for_a_null_file()
    {
        var version = await MachineFacts.AgentVersionAsync(null, (_, _, _, _) => throw new InvalidOperationException("never asked"), CancellationToken.None);

        Assert.Null(version);
    }

    // ---- ReadAcceleratorAsync ---------------------------------------------

    private const string NvidiaFile = "nvidia-smi";
    private const string RocmFile = "rocm-smi";
    private const string SysctlFile = "sysctl";

    private static string Card(long bytes) =>
        $$"""{"VRAM Total Memory (B)": "{{bytes}}", "VRAM Total Used Memory (B)": "0"}""";

    /// <summary>A probe answering per executable; one not named did not complete, as a missing one does.</summary>
    private static Probe Tools(Dictionary<string, ProbeResult> answers, List<string>? calls = null) =>
        (file, _, _, _) =>
        {
            calls?.Add(file);
            return Task.FromResult(answers.TryGetValue(file, out var result) ? result : new ProbeResult(false, -1, ""));
        };

    private static Dictionary<string, ProbeResult> Only(string file, string output, int exit = 0) =>
        new() { [file] = new ProbeResult(true, exit, output) };

    [Fact]
    public async Task ReadAcceleratorAsync_reads_apple_silicon_memory()
    {
        var accelerator = await MachineFacts.ReadAcceleratorAsync(
            true, Tools(Only(SysctlFile, "38654705664\n")), CancellationToken.None);

        Assert.Equal((38654705664L, 1, "metal"), accelerator);
    }

    [Theory]
    [InlineData("not a number", 0, true)]
    [InlineData("38654705664", 1, true)]
    [InlineData("", 0, false)]
    public async Task ReadAcceleratorAsync_keeps_metal_when_the_memory_reading_fails(string output, int exit, bool completed)
    {
        Probe probe = (_, _, _, _) => Task.FromResult(new ProbeResult(completed, exit, output));

        var accelerator = await MachineFacts.ReadAcceleratorAsync(true, probe, CancellationToken.None);

        Assert.Equal((null, 1, "metal"), accelerator);
    }

    [Fact]
    public async Task ReadAcceleratorAsync_keeps_metal_when_the_probe_throws()
    {
        Probe throws = (_, _, _, _) => throw new InvalidOperationException("boom");

        var accelerator = await MachineFacts.ReadAcceleratorAsync(true, throws, CancellationToken.None);

        Assert.Equal((null, 1, "metal"), accelerator);
    }

    [Fact]
    public async Task ReadAcceleratorAsync_on_apple_silicon_never_asks_nvidia_smi()
    {
        var calls = new List<string>();

        await MachineFacts.ReadAcceleratorAsync(true, Tools(Only(SysctlFile, "1\n"), calls), CancellationToken.None);

        Assert.Equal([SysctlFile], calls);
    }

    [Fact]
    public async Task ReadAcceleratorAsync_reads_the_nvidia_query_in_mebibytes()
    {
        var calls = new List<(string File, string[] Args)>();

        var accelerator = await MachineFacts.ReadAcceleratorAsync(
            false, Answering("24564\n", calls: calls), CancellationToken.None);

        Assert.Equal((24564L * 1024 * 1024, 1, "cuda"), accelerator);
        var call = Assert.Single(calls);
        Assert.Equal(NvidiaFile, call.File);
        Assert.Equal(["--query-gpu=memory.total", "--format=csv,noheader,nounits"], call.Args);
    }

    [Fact]
    public async Task ReadAcceleratorAsync_sums_two_nvidia_gpus()
    {
        var accelerator = await MachineFacts.ReadAcceleratorAsync(
            false, Tools(Only(NvidiaFile, "24564\n24564\n")), CancellationToken.None);

        Assert.Equal((2 * 24564L * 1024 * 1024, 2, "cuda"), accelerator);
    }

    [Fact]
    public async Task ReadAcceleratorAsync_reads_nvidia_windows_line_endings()
    {
        var accelerator = await MachineFacts.ReadAcceleratorAsync(
            false, Tools(Only(NvidiaFile, "12282\r\n")), CancellationToken.None);

        Assert.Equal((12282L * 1024 * 1024, 1, "cuda"), accelerator);
    }

    [Theory]
    [InlineData(false, -1, "")]
    [InlineData(true, 9, "12282\n")]
    public async Task ReadAcceleratorAsync_falls_from_nvidia_through_to_rocm(bool completed, int exit, string output)
    {
        var answers = new Dictionary<string, ProbeResult>
        {
            [NvidiaFile] = new ProbeResult(completed, exit, output),
            [RocmFile] = new ProbeResult(true, 0, $$"""{"card0": {{Card(17163091968)}}}"""),
        };

        var accelerator = await MachineFacts.ReadAcceleratorAsync(false, Tools(answers), CancellationToken.None);

        Assert.Equal((17163091968L, 1, "rocm"), accelerator);
    }

    [Fact]
    public async Task ReadAcceleratorAsync_reads_one_rocm_card()
    {
        var calls = new List<(string File, string[] Args)>();
        Probe probe = (file, args, _, _) =>
        {
            calls.Add((file, args));
            return Task.FromResult(file == RocmFile
                ? new ProbeResult(true, 0, $$"""{"card0": {{Card(17163091968)}}}""")
                : new ProbeResult(false, -1, ""));
        };

        var accelerator = await MachineFacts.ReadAcceleratorAsync(false, probe, CancellationToken.None);

        Assert.Equal((17163091968L, 1, "rocm"), accelerator);
        Assert.Equal(["--showmeminfo", "vram", "--json"], calls.Single(c => c.File == RocmFile).Args);
    }

    [Fact]
    public async Task ReadAcceleratorAsync_sums_two_rocm_cards()
    {
        var json = $$"""{"card0": {{Card(1000)}}, "card1": {{Card(2000)}}}""";

        var accelerator = await MachineFacts.ReadAcceleratorAsync(false, Tools(Only(RocmFile, json)), CancellationToken.None);

        Assert.Equal((3000L, 2, "rocm"), accelerator);
    }

    [Fact]
    public async Task ReadAcceleratorAsync_ignores_a_rocm_key_that_is_not_a_card()
    {
        var json = $$"""{"card0": {{Card(1000)}}, "system": {"Driver version": "6.1.0"}, "cardinal": 5}""";

        var accelerator = await MachineFacts.ReadAcceleratorAsync(false, Tools(Only(RocmFile, json)), CancellationToken.None);

        Assert.Equal((1000L, 1, "rocm"), accelerator);
    }

    [Theory]
    [InlineData("""{"system": {"Driver version": "6.1.0"}}""")]
    [InlineData("""{"card0": {"VRAM Total Memory (B)": "lots"}}""")]
    [InlineData("""{"card0": {"VRAM Total Memory (B)": "1000"}, "card1": {}}""")]
    [InlineData("[]")]
    [InlineData("not json at all")]
    [InlineData("")]
    public async Task ReadAcceleratorAsync_treats_unusable_rocm_output_as_absent(string output)
    {
        var accelerator = await MachineFacts.ReadAcceleratorAsync(false, Tools(Only(RocmFile, output)), CancellationToken.None);

        Assert.Equal((null, null, "none"), accelerator);
    }

    [Theory]
    [InlineData("No devices were found\n")]
    [InlineData("24564\nN/A\n")]
    [InlineData("\n")]
    public async Task ReadAcceleratorAsync_treats_unusable_nvidia_output_as_absent(string output)
    {
        var accelerator = await MachineFacts.ReadAcceleratorAsync(false, Tools(Only(NvidiaFile, output)), CancellationToken.None);

        Assert.Equal((null, null, "none"), accelerator);
    }

    [Fact]
    public async Task ReadAcceleratorAsync_is_none_when_nothing_answers()
    {
        var accelerator = await MachineFacts.ReadAcceleratorAsync(false, Tools([]), CancellationToken.None);

        Assert.Equal((null, null, "none"), accelerator);
    }

    [Fact]
    public async Task ReadAcceleratorAsync_is_none_for_a_throwing_probe()
    {
        Probe throws = (_, _, _, _) => throw new InvalidOperationException("boom");

        var accelerator = await MachineFacts.ReadAcceleratorAsync(false, throws, CancellationToken.None);

        Assert.Equal((null, null, "none"), accelerator);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadAcceleratorAsync_lets_cancellation_propagate(bool appleSilicon)
    {
        Probe cancelled = (_, _, _, _) => throw new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => MachineFacts.ReadAcceleratorAsync(appleSilicon, cancelled, CancellationToken.None));
    }

    // ---- shape ------------------------------------------------------------

    [Fact]
    public void MachineFacts_has_exactly_the_ten_properties()
    {
        var names = typeof(MachineFacts)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["AcceleratorBackend", "AcceleratorDeviceCount", "AcceleratorMemoryBytes", "AgentVersion", "Arch", "Container", "Image", "Os", "ProcessStartedAt", "Username"], names);
    }

    // ---- Stamp --------------------------------------------------------

    [Fact]
    public void Stamp_fills_the_ten_and_leaves_everything_else_alone()
    {
        var facts = new MachineFacts("linux", "x64", "2.1.5", true, "runner", DateTimeOffset.UnixEpoch, "ghcr.io/o/r:1", 24L << 30, 2, "cuda");
        var request = new RunnerHeartbeatRequest(Kind: "loop", Line: "working");

        var stamped = facts.Stamp(request);

        var expected = request with
        {
            Os = "linux",
            Arch = "x64",
            AgentVersion = "2.1.5",
            Container = true,
            Username = "runner",
            ProcessStartedAt = DateTimeOffset.UnixEpoch,
            Image = "ghcr.io/o/r:1",
            AcceleratorMemoryBytes = 24L << 30,
            AcceleratorDeviceCount = 2,
            AcceleratorBackend = "cuda",
        };
        Assert.Equal(expected, stamped);
    }

    [Fact]
    public void None_stamp_leaves_the_request_unchanged()
    {
        var request = new RunnerHeartbeatRequest(Kind: "loop", Line: "working");

        Assert.Equal(request, MachineFacts.None.Stamp(request));
    }

    // ---- ImageFrom --------------------------------------------------------

    [Fact]
    public void ImageFrom_reads_HATCH_IMAGE_trimmed()
    {
        Assert.Equal("ghcr.io/o/r:1", MachineFacts.ImageFrom(name => name == "HATCH_IMAGE" ? "  ghcr.io/o/r:1  " : null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ImageFrom_is_null_for_unset_blank_or_whitespace(string? value)
    {
        Assert.Null(MachineFacts.ImageFrom(name => name == "HATCH_IMAGE" ? value : null));
    }

    [Fact]
    public void ImageFrom_asks_only_HATCH_IMAGE()
    {
        var asked = new List<string>();

        MachineFacts.ImageFrom(name =>
        {
            asked.Add(name);
            return null;
        });

        Assert.Equal(["HATCH_IMAGE"], asked);
    }
}

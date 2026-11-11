using System.Reflection;

namespace Hatch.Cli.Tests;

/// <summary>
/// The seven machine facts a beat carries: read once, each null where it
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

    // ---- shape ------------------------------------------------------------

    [Fact]
    public void MachineFacts_has_exactly_the_seven_properties()
    {
        var names = typeof(MachineFacts)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["AgentVersion", "Arch", "Container", "Image", "Os", "ProcessStartedAt", "Username"], names);
    }

    // ---- Stamp --------------------------------------------------------

    [Fact]
    public void Stamp_fills_the_seven_and_leaves_everything_else_alone()
    {
        var facts = new MachineFacts("linux", "x64", "2.1.5", true, "runner", DateTimeOffset.UnixEpoch, "ghcr.io/o/r:1");
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

namespace Hatch.Cli.Tests;

/// <summary>
/// <see cref="ClaudeSessionRunner"/>'s own argv, reached directly through the
/// <c>InternalsVisibleTo</c> grant rather than through a spawned process - the
/// <c>--disallowedTools</c> flags <see cref="ClaudeSessionRunner.Base"/> builds
/// from a <see cref="SessionRequest"/>'s <see cref="SessionRequest.DeniedTools"/>,
/// HA-453.
/// </summary>
public sealed class ClaudeSessionRunnerTests : IDisposable
{
    private readonly string _bin = System.IO.Path.GetTempFileName();

    public void Dispose() => File.Delete(_bin);

    private ClaudeSessionRunner Runner()
    {
        var runner = new ClaudeSessionRunner(_bin);
        Assert.True(runner.CanSpawn(out _));
        return runner;
    }

    [Fact]
    public void One_disallowedTools_flag_is_appended_per_rule_in_order()
    {
        var request = new SessionRequest("/root", "sonnet", "high", "prompt", Quiet: true, DeniedTools: ["Bash", "WebFetch"]);

        var args = Runner().Base(request).ArgumentList;

        var at = args.IndexOf("--disallowedTools");
        Assert.True(at >= 0, "--disallowedTools was not in the argument list");
        Assert.Equal(
            ["--disallowedTools", "Bash", "--disallowedTools", "WebFetch"],
            args.Skip(at).Take(4));
    }

    [Fact]
    public void No_flag_at_all_when_the_playbook_denies_nothing()
    {
        var request = new SessionRequest("/root", "sonnet", "high", "prompt", Quiet: true);

        var args = Runner().Base(request).ArgumentList;

        Assert.DoesNotContain("--disallowedTools", args);
    }
}

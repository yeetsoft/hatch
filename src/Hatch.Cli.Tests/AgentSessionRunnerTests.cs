using System.Collections.Concurrent;
using System.Diagnostics;

namespace Hatch.Cli.Tests;

/// <summary>
/// The spawn itself, against a real child: <c>git stripspace</c> reads stdin and
/// echoes it, which is the one child that proves whether a prompt arrived.
/// </summary>
public sealed class AgentSessionRunnerTests
{
    private static SessionRequest Request(string prompt = "hello") =>
        new("/checkout", "opus", "high", prompt, Quiet: true);

    private static FakeAdapter Echo(bool promptOnStdin) => new()
    {
        PromptOnStdin = promptOnStdin,
        Spawn = _ =>
        {
            var start = new ProcessStartInfo
            {
                FileName = "git",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
            };
            start.ArgumentList.Add("stripspace");
            return start;
        },
    };

    [Fact]
    public async Task A_prompt_on_stdin_reaches_the_child_and_the_spawn_is_released_once()
    {
        var adapter = Echo(promptOnStdin: true);
        var request = Request();

        var result = await new AgentSessionRunner(adapter).RunAsync(request, null, default);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello", result.Output);
        Assert.Same(request, Assert.Single(adapter.Released));
    }

    [Fact]
    public async Task An_adapter_that_delivers_the_prompt_itself_leaves_stdin_at_end_of_file()
    {
        var adapter = Echo(promptOnStdin: false);

        var result = await new AgentSessionRunner(adapter).RunAsync(Request(), null, default);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.Output.Trim());
        Assert.Single(adapter.Released);
    }

    /// <summary>Redirects stdin and stdout but not stderr, so the runner is what has to.</summary>
    private static FakeAdapter UsageOnStderr() => new()
    {
        PromptOnStdin = false,
        Spawn = _ =>
        {
            var start = new ProcessStartInfo
            {
                FileName = "git",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
            };
            start.ArgumentList.Add("--no-such-option");
            return start;
        },
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_childs_standard_error_arrives_line_by_line_through_the_callback(bool streamed)
    {
        var errors = new ConcurrentQueue<string>();
        var lines = new ConcurrentQueue<string>();

        var result = await new AgentSessionRunner(UsageOnStderr(), errors.Enqueue)
            .RunAsync(Request(), streamed ? lines.Enqueue : null, default);

        Assert.NotEqual(0, result.ExitCode);
        Assert.NotEmpty(errors);
        Assert.Contains(errors, line => line.Contains("usage", StringComparison.OrdinalIgnoreCase)
            || line.Contains("unknown option", StringComparison.OrdinalIgnoreCase));
        Assert.All(errors, line => Assert.DoesNotContain('\n', line));
        Assert.Equal("", result.Output);
        Assert.Empty(lines);
    }

    [Fact]
    public async Task A_childs_standard_error_is_dropped_without_a_callback_and_stdout_is_untouched()
    {
        var adapter = Echo(promptOnStdin: true);
        var lines = new ConcurrentQueue<string>();

        var result = await new AgentSessionRunner(adapter).RunAsync(Request(), lines.Enqueue, default);
        var quiet = await new AgentSessionRunner(UsageOnStderr()).RunAsync(Request(), null, default);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello", lines);
        Assert.NotEqual(0, quiet.ExitCode);
        Assert.Equal("", quiet.Output);
    }

    [Fact]
    public async Task A_cancelled_run_still_releases_the_spawn()
    {
        var adapter = Echo(promptOnStdin: true);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await new AgentSessionRunner(adapter).RunAsync(Request(), null, cts.Token);

        Assert.Single(adapter.Released);
    }

    [Fact]
    public async Task A_child_that_cannot_start_still_releases_what_the_build_wrote()
    {
        var adapter = new FakeAdapter
        {
            Spawn = _ => new ProcessStartInfo { FileName = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), UseShellExecute = false },
        };

        await Assert.ThrowsAnyAsync<Exception>(() => new AgentSessionRunner(adapter).RunAsync(Request(), null, default));

        Assert.Single(adapter.Released);
    }

    [Fact]
    public async Task An_attached_spawn_is_released_after_it_ends()
    {
        var adapter = new FakeAdapter
        {
            Spawn = _ =>
            {
                var start = new ProcessStartInfo { FileName = "git", UseShellExecute = false, RedirectStandardOutput = true };
                start.ArgumentList.Add("--version");
                return start;
            },
        };

        var code = await new AgentSessionRunner(adapter).AttachAsync(Request(), default);

        Assert.Equal(0, code);
        Assert.Single(adapter.Released);
    }
}

namespace Hatch.Cli.Tests;

/// <summary>HA-2289: the cheap session a runner runs before it claims again, judged on its own.</summary>
public sealed class AgentCheckTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), $"hatch-agent-check-tests-{Guid.NewGuid():N}");
    private readonly Settings _settings = new() { Base = "https://hatch.example", Key = "hatch_ak_test" };

    public AgentCheckTests() => Directory.CreateDirectory(_temp);

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    private static IAgentAdapter Adapter() => new ClaudeAdapter(Path.GetTempFileName(), null, null, _ => null);

    private async Task<(AgentCheck.Verdict Verdict, FakeSessions Sessions)> RunAsync(
        Func<SessionRequest, Action<string>?, CancellationToken, Task<SessionResult>> behaviour, TimeSpan? ceiling = null)
    {
        var sessions = new FakeSessions { Behaviour = behaviour };
        var verdict = await AgentCheck.RunAsync(sessions, Adapter(), _settings, _temp, default, ceiling);
        return (verdict, sessions);
    }

    [Fact]
    public async Task A_session_that_answers_passes_and_reports_what_it_cost()
    {
        var (verdict, sessions) = await RunAsync((_, _, _) => Task.FromResult(new SessionResult(0, Fixtures.Result(cost: 0.02m))));

        Assert.True(verdict.Passed);
        Assert.Equal("", verdict.Why);
        Assert.Equal(0.02m, verdict.Cost);
        Assert.Equal(1_000, verdict.Tokens);

        var request = Assert.Single(sessions.Spawned);
        Assert.Equal(AgentCheck.Prompt, request.Prompt);
        Assert.Equal("haiku", request.Model);
        Assert.Equal("low", request.Effort);
        Assert.True(request.Quiet);
        Assert.Empty(request.AddDirs);
        Assert.Null(request.HookSettings);
        Assert.Null(request.SessionId);
        Assert.Same(_settings, request.Settings);
    }

    [Fact]
    public async Task A_session_that_started_and_ended_with_an_error_fails_with_what_it_said()
    {
        var (verdict, _) = await RunAsync((_, _, _) =>
            Task.FromResult(new SessionResult(1, Fixtures.Result(error: true, said: "Credit balance is too low"))));

        Assert.False(verdict.Passed);
        Assert.Equal("Credit balance is too low", verdict.Why);
    }

    [Fact]
    public async Task A_session_with_no_first_event_fails_with_the_last_thing_the_cli_wrote()
    {
        var (verdict, _) = await RunAsync((_, _, _) =>
            Task.FromResult(new SessionResult(1, "", Stderr: ["warming up", "not logged in"])));

        Assert.False(verdict.Passed);
        Assert.Equal("the agent never started - the CLI said \"not logged in\"", verdict.Why);
    }

    [Fact]
    public async Task A_session_with_no_first_event_and_nothing_said_still_fails()
    {
        var (verdict, _) = await RunAsync((_, _, _) => Task.FromResult(new SessionResult(0, "")));

        Assert.False(verdict.Passed);
        Assert.Equal("the agent never started", verdict.Why);
    }

    [Fact]
    public async Task A_session_that_does_not_answer_inside_the_ceiling_fails_and_is_killed()
    {
        var (verdict, _) = await RunAsync(
            async (_, _, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return new SessionResult(0, "");
            },
            ceiling: TimeSpan.FromMilliseconds(50));

        Assert.False(verdict.Passed);
        Assert.StartsWith("no answer in ", verdict.Why, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_machine_with_no_cli_to_spawn_fails_with_the_sentence_saying_so_and_spawns_nothing()
    {
        var sessions = new FakeSessions { Missing = "no claude on PATH" };

        var verdict = await AgentCheck.RunAsync(sessions, Adapter(), _settings, _temp, default);

        Assert.False(verdict.Passed);
        Assert.Equal("no claude on PATH", verdict.Why);
        Assert.Empty(sessions.Spawned);
    }

    [Fact]
    public async Task A_runner_that_throws_fails_the_check_and_does_not_throw()
    {
        var (verdict, _) = await RunAsync((_, _, _) => throw new InvalidOperationException("no such file"));

        Assert.False(verdict.Passed);
        Assert.Equal("no such file", verdict.Why);
    }

    [Fact]
    public async Task A_loop_that_is_stopped_meanwhile_stops_the_check_and_does_not_judge_it()
    {
        using var stop = new CancellationTokenSource();
        var sessions = new FakeSessions
        {
            Behaviour = async (_, _, ct) =>
            {
                await stop.CancelAsync();
                await Task.Delay(Timeout.Infinite, ct);
                return new SessionResult(0, "");
            },
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AgentCheck.RunAsync(sessions, Adapter(), _settings, _temp, stop.Token));
    }

    [Fact]
    public async Task The_directory_the_check_ran_in_is_gone_afterwards_on_every_outcome()
    {
        var roots = new List<string>();
        Func<SessionRequest, Action<string>?, CancellationToken, Task<SessionResult>>[] behaviours =
        [
            (_, _, _) => Task.FromResult(new SessionResult(0, Fixtures.Result())),
            (_, _, _) => Task.FromResult(new SessionResult(1, "")),
            (_, _, _) => throw new InvalidOperationException("boom"),
        ];

        foreach (var behaviour in behaviours)
        {
            var (_, sessions) = await RunAsync((request, onLine, ct) =>
            {
                Assert.True(Directory.Exists(request.Root), "the check's directory should exist while it runs");
                return behaviour(request, onLine, ct);
            });
            roots.Add(Assert.Single(sessions.Spawned).Root);
        }

        Assert.All(roots, root =>
        {
            Assert.False(Directory.Exists(root));
            Assert.Equal(_temp, Path.GetDirectoryName(root));
            Assert.StartsWith("hatch-agent-check-", Path.GetFileName(root), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Claudes_cheapest_model_is_haiku()
    {
        Assert.Equal("haiku", Adapter().CheapestModel);
        Assert.Equal("", ((IAgentAdapter)new FakeAdapter { Models = [] }).CheapestModel);
    }
}

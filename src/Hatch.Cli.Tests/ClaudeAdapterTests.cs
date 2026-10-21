namespace Hatch.Cli.Tests;

/// <summary>
/// <see cref="ClaudeAdapter"/>'s own argv, reached directly through the
/// <c>InternalsVisibleTo</c> grant rather than through a spawned process - the
/// same reach <see cref="ClaudeSessionRunnerTests"/> uses for the runner this
/// adapter stands beside, HA-848.
/// </summary>
public sealed class ClaudeAdapterTests : IDisposable
{
    private readonly string _bin = Path.GetTempFileName();

    public void Dispose() => File.Delete(_bin);

    private ClaudeAdapter Adapter()
    {
        var adapter = new ClaudeAdapter(_bin);
        Assert.True(adapter.CanSpawn(out _));
        return adapter;
    }

    [Fact]
    public void CanServe_answers_true_for_every_model()
    {
        Assert.True(((IAgentAdapter)Adapter()).CanServe("anything:7b", out var refusal));
        Assert.Equal("", refusal);
    }

    [Fact]
    public void BuildHeadless_quiet_with_no_hooks_and_no_denied_tools()
    {
        var request = new SessionRequest("/root", "sonnet", "high", "prompt", Quiet: true);

        var args = Adapter().BuildHeadless(request).ArgumentList;

        Assert.Equal(
            ["--model", "sonnet", "--effort", "high", "--add-dir", "/root",
                "-p", "--permission-mode", "bypassPermissions",
                "--output-format", "json"],
            args);
    }

    [Fact]
    public void BuildHeadless_streamed_with_hooks_and_denied_tools()
    {
        var request = new SessionRequest(
            "/root", "sonnet", "high", "prompt", Quiet: false,
            HookSettings: "/tmp/settings.json", DeniedTools: ["Bash", "WebFetch"]);

        var args = Adapter().BuildHeadless(request).ArgumentList;

        Assert.Equal(
            ["--model", "sonnet", "--effort", "high", "--add-dir", "/root",
                "--disallowedTools", "Bash", "--disallowedTools", "WebFetch",
                "-p", "--permission-mode", "bypassPermissions",
                "--settings", "/tmp/settings.json",
                "--output-format", "stream-json", "--verbose"],
            args);
    }

    [Fact]
    public void BuildHeadless_redirects_standard_input_and_output()
    {
        var request = new SessionRequest("/root", "sonnet", "high", "prompt", Quiet: true);

        var start = Adapter().BuildHeadless(request);

        Assert.True(start.RedirectStandardInput);
        Assert.True(start.RedirectStandardOutput);
    }

    [Fact]
    public void BuildAttached_is_the_base_argv_with_the_bare_prompt_appended()
    {
        var request = new SessionRequest("/root", "sonnet", "high", "say hello", Quiet: false);

        var args = Adapter().BuildAttached(request).ArgumentList;

        Assert.Equal(
            ["--model", "sonnet", "--effort", "high", "--add-dir", "/root", "say hello"],
            args);
    }

    [Fact]
    public void The_probes_argv_asks_usage_as_one_turn_over_stream_json()
    {
        var args = Adapter().ProbeStart("/tmp").ArgumentList;

        Assert.Equal(
            ["-p", "/usage", "--output-format", "stream-json", "--verbose", "--max-turns", "1"],
            args);
    }

    [Fact]
    public void CanSpawn_refuses_with_the_same_sentence_as_today_when_nothing_is_configured_or_on_path()
    {
        var adapter = new ClaudeAdapter(configured: "/does/not/exist");

        Assert.False(adapter.CanSpawn(out var refusal));
        Assert.Equal("hatch: HATCH_CLAUDE_BIN is not there: /does/not/exist", refusal);
    }

    [Fact]
    public void The_spawned_session_names_the_runner_that_dispatched_it()
    {
        var request = new SessionRequest("/root", "sonnet", "high", "prompt", Quiet: true, RunnerName: "test:/checkout");

        Assert.Equal("test:/checkout", Adapter().BuildHeadless(request).Environment["HATCH_RUNNER"]);
        Assert.Equal("test:/checkout", Adapter().BuildAttached(request).Environment["HATCH_RUNNER"]);
    }

    [Fact]
    public void A_session_with_no_runner_name_leaves_HATCH_RUNNER_as_it_inherited_it()
    {
        var request = new SessionRequest("/root", "sonnet", "high", "prompt", Quiet: true, RunnerName: "");

        Adapter().BuildHeadless(request).Environment.TryGetValue("HATCH_RUNNER", out var inherited);

        Assert.Equal(Environment.GetEnvironmentVariable("HATCH_RUNNER"), inherited);
    }}

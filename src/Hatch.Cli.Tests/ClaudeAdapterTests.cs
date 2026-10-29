using System.Reflection;

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

    /// <summary>What a current CLI's <c>--help</c> lists, in the shape it lists it.</summary>
    private const string CurrentHelp = """
        Options:
          --effort <level>                      Effort level
          --exclude-dynamic-system-prompt-sections
                                                Move per-machine sections out of the system prompt.
          -p, --print                           Print response and exit
          --tools <tools...>                    Specify the list of available tools from
                                                the built-in set. Use "default" for all.
        """;

    private ClaudeAdapter Adapter(string? tools = null, string help = CurrentHelp, Action<string>? warn = null)
    {
        var adapter = new ClaudeAdapter(_bin, warn, tools, _ => help);
        Assert.True(adapter.CanSpawn(out _));
        return adapter;
    }

    private const string Tools = ClaudeAdapter.DefaultTools;

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
                "--tools", Tools, "--exclude-dynamic-system-prompt-sections",
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
                "--tools", Tools, "--exclude-dynamic-system-prompt-sections",
                "--settings", "/tmp/settings.json",
                "--output-format", "stream-json", "--verbose"],
            args);
    }

    [Fact]
    public void BuildHeadless_passes_the_default_tools_as_one_argument()
    {
        var request = new SessionRequest("/root", "sonnet", "high", "prompt", Quiet: true);

        var args = Adapter().BuildHeadless(request).ArgumentList;

        Assert.Equal("Bash,Read,Edit,Write,Grep,Glob,Monitor,TaskStop", args[args.IndexOf("--tools") + 1]);
        Assert.Single(args, "--tools");
        Assert.Contains("--exclude-dynamic-system-prompt-sections", args);
    }

    [Fact]
    public void Attached_and_probe_argv_carry_neither_new_flag()
    {
        var request = new SessionRequest("/root", "sonnet", "high", "say hello", Quiet: false);
        var adapter = Adapter();

        foreach (var args in new[] { adapter.BuildAttached(request).ArgumentList, adapter.ProbeStart("/tmp").ArgumentList })
        {
            Assert.DoesNotContain("--tools", args);
            Assert.DoesNotContain("--exclude-dynamic-system-prompt-sections", args);
        }
    }

    [Fact]
    public void A_configured_list_replaces_the_default()
    {
        var request = new SessionRequest("/root", "sonnet", "high", "prompt", Quiet: true);

        var args = Adapter("Bash,Read").BuildHeadless(request).ArgumentList;

        Assert.Equal("Bash,Read", args[args.IndexOf("--tools") + 1]);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("Default")]
    public void The_word_default_passes_no_tools_flag_and_keeps_the_other(string word)
    {
        var request = new SessionRequest("/root", "sonnet", "high", "prompt", Quiet: true);

        var args = Adapter(word).BuildHeadless(request).ArgumentList;

        Assert.DoesNotContain("--tools", args);
        Assert.Contains("--exclude-dynamic-system-prompt-sections", args);
    }

    [Fact]
    public void Denied_tools_are_passed_alongside_the_tools_list()
    {
        var request = new SessionRequest("/root", "sonnet", "high", "prompt", Quiet: true, DeniedTools: ["Bash(git push:*)"]);

        var args = Adapter().BuildHeadless(request).ArgumentList;

        Assert.Equal("Bash(git push:*)", args[args.IndexOf("--disallowedTools") + 1]);
        Assert.Contains("--tools", args);
    }

    [Fact]
    public void A_cli_that_lists_neither_flag_gets_neither_and_is_warned_about_once()
    {
        var said = new List<string>();
        var adapter = Adapter(help: "Options:\n  -p, --print   Print\n  --effort <level>   Effort\n", warn: said.Add);
        var request = new SessionRequest("/root", "sonnet", "high", "prompt", Quiet: true);

        adapter.BuildHeadless(request);
        var args = adapter.BuildHeadless(request).ArgumentList;

        Assert.DoesNotContain("--tools", args);
        Assert.DoesNotContain("--exclude-dynamic-system-prompt-sections", args);
        var warning = Assert.Single(said);
        Assert.Contains("--tools", warning);
        Assert.Contains("--exclude-dynamic-system-prompt-sections", warning);
    }

    [Fact]
    public void A_flag_named_only_inside_a_description_is_not_one_the_cli_lists()
    {
        var help = "Options:\n  --allowedTools <tools>   Unlike --tools, this does not restrict the set\n";
        var request = new SessionRequest("/root", "sonnet", "high", "prompt", Quiet: true);

        var args = Adapter(help: help).BuildHeadless(request).ArgumentList;

        Assert.DoesNotContain("--tools", args);
    }

    [Fact]
    public void A_help_that_could_not_be_read_leaves_both_flags_off()
    {
        var adapter = new ClaudeAdapter(_bin, null, null, _ => null);
        Assert.True(adapter.CanSpawn(out _));
        var request = new SessionRequest("/root", "sonnet", "high", "prompt", Quiet: true);

        var args = adapter.BuildHeadless(request).ArgumentList;

        Assert.DoesNotContain("--tools", args);
        Assert.DoesNotContain("--exclude-dynamic-system-prompt-sections", args);
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
    public void The_probe_redirects_its_standard_error_so_it_cannot_write_under_the_readout()
    {
        Assert.True(Adapter().ProbeStart("/tmp").RedirectStandardError);
    }

    /// <summary>HA-1457: a probe already known to cost a turn says so and asks nothing, rather than spawning again in silence.</summary>
    [Fact]
    public async Task ProbeAsync_says_so_and_asks_nothing_once_already_known_costly()
    {
        var logged = new List<string>();
        var adapter = new ClaudeAdapter(_bin, warn: logged.Add);
        Assert.True(adapter.CanSpawn(out _));

        // Same instant the real trip happens in (a probe that came back having
        // cost a turn) - reached directly rather than by actually spending one,
        // the same reach this file's own doc comment already claims.
        typeof(ClaudeAdapter).GetField("_probeCostly", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(adapter, true);

        var result = await ((IUsageProbe)adapter).ProbeAsync(Path.GetTempPath(), default);

        Assert.Null(result);
        Assert.Contains(logged, l => l.Contains("probe skipped", StringComparison.Ordinal));
    }

    /// <summary>HA-1457: once the process exits, the probe names its exit code and how many lines it read - here, a real spawn of a binary that is not the claude CLI, so the exit is a predictable failure with nothing on stdout.</summary>
    [Fact]
    public async Task ProbeAsync_logs_the_exit_code_and_line_count_once_the_process_exits()
    {
        var git = Reach.Find("git", Environment.GetEnvironmentVariable("PATH"));
        Assert.NotNull(git);

        var logged = new List<string>();
        var adapter = new ClaudeAdapter(git, warn: logged.Add);
        Assert.True(adapter.CanSpawn(out _));

        var result = await ((IUsageProbe)adapter).ProbeAsync(Path.GetTempPath(), default);

        Assert.NotNull(result);
        Assert.Empty(result);
        Assert.Contains(logged, l => l.Contains("/usage probe exited", StringComparison.Ordinal) && l.Contains("0 line(s)", StringComparison.Ordinal));
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
    public void The_spawned_session_carries_the_runners_own_HATCH_BASE_and_HATCH_KEY()
    {
        var request = new SessionRequest("/root", "sonnet", "high", "prompt", Quiet: true,
            Settings: new Settings { Base = "https://example.hatch", Key = "hatch_ak_test" });

        Assert.Equal("https://example.hatch", Adapter().BuildHeadless(request).Environment["HATCH_BASE"]);
        Assert.Equal("hatch_ak_test", Adapter().BuildHeadless(request).Environment["HATCH_KEY"]);
        Assert.Equal("https://example.hatch", Adapter().BuildAttached(request).Environment["HATCH_BASE"]);
        Assert.Equal("hatch_ak_test", Adapter().BuildAttached(request).Environment["HATCH_KEY"]);
    }

    [Fact]
    public void A_session_with_no_runner_name_leaves_HATCH_RUNNER_as_it_inherited_it()
    {
        var request = new SessionRequest("/root", "sonnet", "high", "prompt", Quiet: true, RunnerName: "");

        Adapter().BuildHeadless(request).Environment.TryGetValue("HATCH_RUNNER", out var inherited);

        Assert.Equal(Environment.GetEnvironmentVariable("HATCH_RUNNER"), inherited);
    }}

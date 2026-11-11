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

    /// <summary>A script that prints <paramref name="json"/> to stdout and exits, standing in for a real claude CLI spawn.</summary>
    private static string ScriptThatPrints(string directory, string json)
    {
        if (OperatingSystem.IsWindows())
        {
            var file = Path.Combine(directory, "fake-claude.cmd");
            File.WriteAllText(file, $"@echo off\r\necho {json}\r\n");
            return file;
        }

        var script = Path.Combine(directory, "fake-claude.sh");
        File.WriteAllText(script, $"#!/bin/sh\necho '{json}'\n");
        File.SetUnixFileMode(script,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return script;
    }

    /// <summary>HA-2205: a verdict file naming the current CLI version pre-sets the latch, so the second process never spawns the probe at all.</summary>
    [Fact]
    public async Task NoteCliVersion_pre_sets_the_latch_from_a_verdict_naming_the_current_version()
    {
        var directory = Directory.CreateTempSubdirectory("hatch-usage-verdict-").FullName;
        try
        {
            var verdictPath = Path.Combine(directory, "verdict.json");
            Assert.True(new UsageProbeVerdict("2.1.268 (Claude Code)", 0.38m).Write(verdictPath));

            var logged = new List<string>();
            // _bin is a plain temp file, not an executable - spawning it would
            // fail loudly, so a clean null with no such failure says the probe
            // was never run.
            var adapter = new ClaudeAdapter(_bin, warn: logged.Add, verdictPath: verdictPath);
            Assert.True(adapter.CanSpawn(out _));

            adapter.NoteCliVersion("2.1.268 (Claude Code)");
            var result = await ((IUsageProbe)adapter).ProbeAsync(Path.GetTempPath(), default);

            Assert.Null(result);
            Assert.Single(logged, l => l.Contains("already known to cost a turn", StringComparison.Ordinal) && l.Contains("$0.38", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>HA-2205: a verdict naming a different version is not this process's own, so it is ignored and a probe runs as normal.</summary>
    [Fact]
    public async Task NoteCliVersion_ignores_a_verdict_naming_a_different_version()
    {
        var directory = Directory.CreateTempSubdirectory("hatch-usage-verdict-").FullName;
        try
        {
            var verdictPath = Path.Combine(directory, "verdict.json");
            Assert.True(new UsageProbeVerdict("2.1.200 (Claude Code)", 0.38m).Write(verdictPath));

            var git = Reach.Find("git", Environment.GetEnvironmentVariable("PATH"));
            Assert.NotNull(git);

            var logged = new List<string>();
            var adapter = new ClaudeAdapter(git, warn: logged.Add, verdictPath: verdictPath);
            Assert.True(adapter.CanSpawn(out _));

            adapter.NoteCliVersion("2.1.268 (Claude Code)");
            var result = await ((IUsageProbe)adapter).ProbeAsync(Path.GetTempPath(), default);

            Assert.NotNull(result);
            Assert.DoesNotContain(logged, l => l.Contains("already known to cost a turn", StringComparison.Ordinal));
            Assert.Contains(logged, l => l.Contains("/usage probe exited", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>HA-2205: "probe skipped" is said once per process, not once per due cycle.</summary>
    [Fact]
    public async Task ProbeAsync_says_probe_skipped_only_once_across_consecutive_calls()
    {
        var logged = new List<string>();
        var adapter = new ClaudeAdapter(_bin, warn: logged.Add);
        Assert.True(adapter.CanSpawn(out _));

        typeof(ClaudeAdapter).GetField("_probeCostly", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(adapter, true);

        await ((IUsageProbe)adapter).ProbeAsync(Path.GetTempPath(), default);
        await ((IUsageProbe)adapter).ProbeAsync(Path.GetTempPath(), default);

        Assert.Single(logged, l => l.Contains("probe skipped", StringComparison.Ordinal));
    }

    /// <summary>HA-2205: a probe that discovers the CLI took /usage as a turn writes a verdict file, so the next process can skip its own probe.</summary>
    [Fact]
    public async Task ProbeAsync_writes_a_verdict_file_once_it_discovers_the_probe_is_costly()
    {
        var directory = Directory.CreateTempSubdirectory("hatch-usage-verdict-").FullName;
        try
        {
            var script = ScriptThatPrints(directory, """{"type":"result","num_turns":1,"total_cost_usd":0.38}""");
            var verdictPath = Path.Combine(directory, "verdict.json");

            var adapter = new ClaudeAdapter(script, verdictPath: verdictPath);
            Assert.True(adapter.CanSpawn(out _));
            adapter.NoteCliVersion("2.1.268 (Claude Code)");

            var result = await ((IUsageProbe)adapter).ProbeAsync(Path.GetTempPath(), default);

            Assert.Null(result);
            var verdict = UsageProbeVerdict.Read(verdictPath);
            Assert.NotNull(verdict);
            Assert.Equal("2.1.268 (Claude Code)", verdict.Version);
            Assert.Equal(0.38m, verdict.CostUsd);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CanSpawn_refuses_with_the_same_sentence_as_today_when_nothing_is_configured_or_on_path()
    {
        var adapter = new ClaudeAdapter(configured: "/does/not/exist");

        Assert.False(adapter.CanSpawn(out var refusal));
        Assert.Equal("hatch: HATCH_CLAUDE_BIN is not there: /does/not/exist", refusal);
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
        Assert.DoesNotContain("HATCH_RUNNER", Adapter().BuildHeadless(request).Environment.Keys);
    }

    [Fact]
    public void RecogniseModelRefusal_reads_the_credits_sentence()
    {
        var endedAt = DateTimeOffset.Parse("2026-09-28T22:00:00+00:00");

        var refusal = Adapter().RecogniseModelRefusal(
            "Fable 5.1 requires usage credits. Switch to another model to continue.", endedAt);

        Assert.NotNull(refusal);
        Assert.Equal("Fable 5.1", refusal!.Value.Model);
        Assert.Null(refusal.Value.ResetAt);
    }

    [Fact]
    public void Executable_is_the_configured_binary_when_it_exists()
    {
        Assert.Equal(_bin, new ClaudeAdapter(_bin).Executable);
    }

    [Fact]
    public void Executable_is_null_for_a_configured_path_that_does_not_exist()
    {
        Assert.Null(new ClaudeAdapter("/does/not/exist").Executable);
    }
}

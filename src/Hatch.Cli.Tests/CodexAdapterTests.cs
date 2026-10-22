namespace Hatch.Cli.Tests;

/// <summary>
/// <see cref="CodexAdapter"/>'s own argv, reached directly the way
/// <see cref="ClaudeAdapterTests"/> reaches Claude's.
/// </summary>
public sealed class CodexAdapterTests : IDisposable
{
    private readonly string _bin = Path.GetTempFileName();

    public void Dispose() => File.Delete(_bin);

    private CodexAdapter Adapter()
    {
        var adapter = new CodexAdapter(_bin);
        Assert.True(adapter.CanSpawn(out _));
        return adapter;
    }

    private static SessionRequest Request(
        string model = "gpt-6-luna", string effort = "high", bool quiet = false,
        string? hooks = null, string[]? denied = null) =>
        new("/root", model, effort, "prompt", quiet, HookSettings: hooks, DeniedTools: denied);

    [Fact]
    public void Identity()
    {
        var adapter = new CodexAdapter();

        Assert.Equal("codex", adapter.Id);
        Assert.Equal("Codex", adapter.DisplayName);
        Assert.Null(adapter.Probe);
    }

    [Fact]
    public void CanServe_answers_true_for_every_model()
    {
        Assert.True(((IAgentAdapter)Adapter()).CanServe("anything:7b", out var refusal));
        Assert.Equal("", refusal);
    }

    [Fact]
    public void BuildHeadless_with_no_hooks_and_no_denied_tools_grants_approval_alone()
    {
        var args = Adapter().BuildHeadless(Request()).ArgumentList;

        Assert.Equal(
            ["exec", "-m", "gpt-6-luna", "-C", "/root",
                "--json", "-c", "model_reasoning_effort=high", "--approve-for-me"],
            args);
    }

    [Fact]
    public void BuildHeadless_with_hooks_sets_codex_home_and_the_last_message_file()
    {
        var home = Path.Combine(Path.GetTempPath(), "codex-home");

        var start = Adapter().BuildHeadless(Request(hooks: home));

        Assert.Equal(home, start.Environment["CODEX_HOME"]);
        Assert.Equal(
            ["exec", "-m", "gpt-6-luna", "-C", "/root",
                "--json", "-c", "model_reasoning_effort=high", "--approve-for-me",
                "-o", Path.Combine(home, "last-message.txt")],
            start.ArgumentList);
    }

    [Fact]
    public void OutputLastMessagePath_is_the_file_BuildHeadless_asked_for_and_null_without_hooks()
    {
        var home = Path.Combine(Path.GetTempPath(), "codex-home");

        Assert.Equal(Path.Combine(home, "last-message.txt"), Adapter().OutputLastMessagePath(Request(hooks: home)));
        Assert.Null(Adapter().OutputLastMessagePath(Request()));
    }

    [Fact]
    public void BuildHeadless_without_hooks_adds_no_output_file_and_sets_no_codex_home()
    {
        var start = Adapter().BuildHeadless(Request());

        Assert.DoesNotContain("-o", start.ArgumentList);
        start.Environment.TryGetValue("CODEX_HOME", out var inherited);
        Assert.Equal(Environment.GetEnvironmentVariable("CODEX_HOME"), inherited);
    }

    [Fact]
    public void BuildHeadless_carries_each_add_dir()
    {
        var request = Request() with { AddDirs = ["/a", "/b"] };

        var args = Adapter().BuildHeadless(request).ArgumentList;

        Assert.Equal(
            ["exec", "-m", "gpt-6-luna", "-C", "/root", "--add-dir", "/a", "--add-dir", "/b",
                "--json", "-c", "model_reasoning_effort=high", "--approve-for-me"],
            args);
    }

    [Theory]
    [InlineData("Edit")]
    [InlineData("Write")]
    [InlineData("NotebookEdit")]
    public void BuildHeadless_with_a_write_tool_denied_goes_read_only_with_no_grant(string tool)
    {
        var args = Adapter().BuildHeadless(Request(denied: [tool])).ArgumentList;

        Assert.Equal(
            ["exec", "-m", "gpt-6-luna", "-C", "/root",
                "--json", "-c", "model_reasoning_effort=high", "-s", "read-only"],
            args);
        Assert.DoesNotContain("--approve-for-me", args);
    }

    [Fact]
    public void BuildHeadless_with_all_three_write_tools_denied_still_carries_one_sandbox_flag()
    {
        var args = Adapter().BuildHeadless(Request(denied: ["Edit", "Write", "NotebookEdit"])).ArgumentList;

        Assert.Single(args, "-s");
        Assert.DoesNotContain("--approve-for-me", args);
        Assert.DoesNotContain("--dangerously-bypass-approvals-and-sandbox", args);
    }

    [Theory]
    [InlineData("haiku", "gpt-6-luna")]
    [InlineData("sonnet", "gpt-5.6-terra")]
    [InlineData("opus", "gpt-5.6-terra")]
    [InlineData("fable", "gpt-5.6-terra")]
    [InlineData("gpt-7-pinned", "gpt-7-pinned")]
    public void Models_map_aliases_and_pass_pinned_ids_through(string model, string expected)
    {
        var args = Adapter().BuildHeadless(Request(model: model)).ArgumentList;

        Assert.Equal(expected, args[args.IndexOf("-m") + 1]);
    }

    [Theory]
    [InlineData("low", "low")]
    [InlineData("medium", "medium")]
    [InlineData("high", "high")]
    [InlineData("xhigh", "xhigh")]
    [InlineData("max", "xhigh")]
    public void Efforts_map_max_to_xhigh_and_pass_the_rest_through(string effort, string expected)
    {
        var args = Adapter().BuildHeadless(Request(effort: effort)).ArgumentList;

        Assert.Equal($"model_reasoning_effort={expected}", args[args.IndexOf("-c") + 1]);
    }

    [Fact]
    public void BuildHeadless_argv_is_the_same_quiet_or_streamed()
    {
        var quiet = Adapter().BuildHeadless(Request(quiet: true)).ArgumentList;
        var streamed = Adapter().BuildHeadless(Request(quiet: false)).ArgumentList;

        Assert.Equal(streamed, quiet);
    }

    [Fact]
    public void BuildHeadless_redirects_standard_input_and_output()
    {
        var start = Adapter().BuildHeadless(Request());

        Assert.True(start.RedirectStandardInput);
        Assert.True(start.RedirectStandardOutput);
    }

    [Fact]
    public void BuildAttached_is_the_base_argv_with_the_bare_prompt_appended()
    {
        var args = Adapter().BuildAttached(Request() with { Prompt = "say hello" }).ArgumentList;

        Assert.Equal(["-m", "gpt-6-luna", "-C", "/root", "say hello"], args);
    }

    [Fact]
    public void BuildAttached_never_carries_a_sandbox_or_a_grant_even_with_a_write_tool_denied()
    {
        var args = Adapter().BuildAttached(Request(denied: ["Edit"])).ArgumentList;

        Assert.Equal(["-m", "gpt-6-luna", "-C", "/root", "prompt"], args);
    }

    [Theory]
    [InlineData("Bash")]
    [InlineData("Bash(rm:*)")]
    [InlineData("Edit(src/**)")]
    [InlineData("mcp__server__tool")]
    [InlineData("*")]
    [InlineData("WebFetch")]
    public void An_untranslatable_rule_throws_in_both_builds_and_names_the_rule(string rule)
    {
        var adapter = Adapter();

        var headless = Assert.Throws<InvalidOperationException>(() => adapter.BuildHeadless(Request(denied: [rule])));
        var attached = Assert.Throws<InvalidOperationException>(() => adapter.BuildAttached(Request(denied: [rule])));

        Assert.Contains($"\"{rule}\"", headless.Message);
        Assert.Contains($"\"{rule}\"", attached.Message);
    }

    [Fact]
    public void A_translatable_rule_does_not_excuse_an_untranslatable_one()
    {
        var adapter = Adapter();

        Assert.Throws<InvalidOperationException>(() => adapter.BuildHeadless(Request(denied: ["Edit", "Bash"])));
        Assert.Throws<InvalidOperationException>(() => adapter.BuildHeadless(Request(denied: ["Bash", "Edit"])));
    }

    [Fact]
    public void Building_before_can_spawn_is_a_bug_and_throws()
    {
        Assert.Throws<InvalidOperationException>(() => new CodexAdapter(_bin).BuildHeadless(Request()));
    }

    [Fact]
    public void CanSpawn_refuses_when_the_configured_binary_is_not_there()
    {
        var adapter = new CodexAdapter(configured: "/does/not/exist");

        Assert.False(adapter.CanSpawn(out var refusal));
        Assert.Equal("hatch: HATCH_CODEX_BIN is not there: /does/not/exist", refusal);
    }

    [Fact]
    public void CanSpawn_names_the_variable_when_nothing_is_on_path()
    {
        var adapter = new CodexAdapter(path: Path.Combine(Path.GetTempPath(), "no-such-dir"));

        Assert.False(adapter.CanSpawn(out var refusal));
        Assert.Equal(
            "hatch: no codex CLI on PATH.\n\n  Install it, or point at one you have:\n      export HATCH_CODEX_BIN=/path/to/codex",
            refusal);
    }

    [Fact]
    public void ResumeCommand_rejoins_through_exec_resume()
    {
        Assert.Equal("codex exec resume abc", new CodexAdapter().ResumeCommand("abc"));
    }

    [Fact]
    public void WriteHooks_answers_null_because_exec_skips_an_overlay_without_the_bypass_flag()
    {
        Assert.Null(new CodexAdapter().WriteHooks(Path.GetTempPath(), "AER-1", "hatch", null));
    }

    [Fact]
    public void CreateReader_hands_the_stream_reader_the_mapped_model()
    {
        var facts = new RunFacts();
        var reader = Adapter().CreateReader("/root", facts, "sonnet");

        _ = reader.Read("""{"type":"turn.completed","usage":{"input_tokens":1,"output_tokens":1}}""").ToList();

        Assert.Equal("gpt-5.6-terra", facts.Result!.Models![0].Model);
    }

    [Fact]
    public void ReadClosing_keeps_the_last_agent_message()
    {
        var blob = string.Join('\n',
            """{"type":"thread.started","thread_id":"t"}""",
            """{"type":"item.completed","item":{"type":"agent_message","text":"first"}}""",
            """{"type":"item.completed","item":{"type":"command_execution","aggregated_output":"x","status":"completed"}}""",
            """{"type":"item.completed","item":{"type":"agent_message","text":"second"}}""",
            """{"type":"turn.completed","usage":{}}""");

        Assert.Equal("second", Adapter().ReadClosing(blob));
    }

    [Fact]
    public void ReadClosing_ends_on_turn_failed_when_that_is_last()
    {
        var blob = string.Join('\n',
            """{"type":"item.completed","item":{"type":"agent_message","text":"working"}}""",
            """{"type":"turn.failed","error":{"message":"usage limit"}}""");

        Assert.Equal("usage limit", Adapter().ReadClosing(blob));
    }

    [Fact]
    public void ReadClosing_ends_on_a_bare_error_when_that_is_last()
    {
        var blob = string.Join('\n',
            """{"type":"item.completed","item":{"type":"agent_message","text":"working"}}""",
            "not json at all",
            """{"type":"error","message":"stream dropped"}""");

        Assert.Equal("stream dropped", Adapter().ReadClosing(blob));
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("{broken")]
    [InlineData("""{"type":"turn.completed","usage":{}}""")]
    public void ReadClosing_is_null_on_empty_or_unparseable_input(string blob)
    {
        Assert.Null(Adapter().ReadClosing(blob));
    }

    [Fact]
    public void The_spawned_session_names_the_runner_that_dispatched_it()
    {
        var request = Request() with { RunnerName = "test:/checkout" };

        Assert.Equal("test:/checkout", Adapter().BuildHeadless(request).Environment["HATCH_RUNNER"]);
        Assert.Equal("test:/checkout", Adapter().BuildAttached(request).Environment["HATCH_RUNNER"]);
    }

    [Fact]
    public void The_spawned_session_carries_the_runners_own_HATCH_BASE_and_HATCH_KEY()
    {
        var request = Request() with { Settings = new Settings { Base = "https://example.hatch", Key = "hatch_ak_test" } };

        Assert.Equal("https://example.hatch", Adapter().BuildHeadless(request).Environment["HATCH_BASE"]);
        Assert.Equal("hatch_ak_test", Adapter().BuildHeadless(request).Environment["HATCH_KEY"]);
        Assert.Equal("https://example.hatch", Adapter().BuildAttached(request).Environment["HATCH_BASE"]);
        Assert.Equal("hatch_ak_test", Adapter().BuildAttached(request).Environment["HATCH_KEY"]);
    }}

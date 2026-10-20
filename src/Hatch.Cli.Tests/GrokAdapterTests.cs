namespace Hatch.Cli.Tests;

/// <summary>
/// <see cref="GrokAdapter"/>'s own argv, reached directly through the
/// <c>InternalsVisibleTo</c> grant the same way <see cref="ClaudeAdapterTests"/>
/// reaches <see cref="ClaudeAdapter"/>'s.
/// </summary>
public sealed class GrokAdapterTests : IDisposable
{
    private readonly string _bin = Path.GetTempFileName();
    private readonly List<string> _warnings = [];

    public void Dispose() => File.Delete(_bin);

    private GrokAdapter Adapter(IReadOnlyDictionary<string, string?>? tiers = null)
    {
        var adapter = new GrokAdapter(_bin, _warnings.Add, tiers);
        Assert.True(adapter.CanSpawn(out _));
        return adapter;
    }

    [Fact]
    public void BuildHeadless_quiet_with_no_session_id()
    {
        var request = new SessionRequest("/root", "sonnet", "high", "prompt", Quiet: true);
        var adapter = Adapter();

        var args = adapter.BuildHeadless(request).ArgumentList;

        Assert.Equal(
            ["--effort", "high", "--cwd", "/root",
                "--prompt-file", PromptFile(request), "--verbatim",
                "--output-format", "json",
                "--always-approve", "--trust", "--no-auto-update"],
            args);

        adapter.Release(request);
    }

    [Fact]
    public void BuildHeadless_streamed_carries_the_session_id()
    {
        var request = new SessionRequest("/root", "sonnet", "high", "prompt", Quiet: false, SessionId: "11111111-1111-1111-1111-111111111111");
        var adapter = Adapter();

        var args = adapter.BuildHeadless(request).ArgumentList;

        Assert.Equal(
            ["--effort", "high", "--cwd", "/root",
                "-s", "11111111-1111-1111-1111-111111111111",
                "--prompt-file", PromptFile(request), "--verbatim",
                "--output-format", "streaming-json",
                "--always-approve", "--trust", "--no-auto-update"],
            args);

        adapter.Release(request);
    }

    [Fact]
    public void BuildHeadless_redirects_standard_input_and_output()
    {
        var request = new SessionRequest("/root", "sonnet", "high", "prompt", Quiet: true);
        var adapter = Adapter();

        var start = adapter.BuildHeadless(request);

        Assert.True(start.RedirectStandardInput);
        Assert.True(start.RedirectStandardOutput);

        adapter.Release(request);
    }

    [Fact]
    public void BuildHeadless_writes_the_exact_prompt_to_the_prompt_file_and_release_deletes_it()
    {
        var request = new SessionRequest("/root", "sonnet", "high", "the exact prompt text", Quiet: true);
        var adapter = Adapter();

        adapter.BuildHeadless(request);

        var file = PromptFile(request);
        Assert.True(File.Exists(file));
        Assert.Equal("the exact prompt text", File.ReadAllText(file));

        adapter.Release(request);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void BuildAttached_is_the_base_argv_with_the_bare_prompt_appended_and_none_of_the_headless_only_flags()
    {
        var request = new SessionRequest("/root", "sonnet", "high", "say hello", Quiet: false, SessionId: "s-1");

        var args = Adapter().BuildAttached(request).ArgumentList;

        Assert.Equal(["--effort", "high", "--cwd", "/root", "say hello"], args);
    }

    [Theory]
    [InlineData("Bash")]
    [InlineData("Bash(git push:*)")]
    [InlineData("Read")]
    [InlineData("Read(secrets/**)")]
    [InlineData("Edit")]
    [InlineData("Edit(src/**)")]
    [InlineData("Write")]
    [InlineData("Write(out/**)")]
    [InlineData("Grep")]
    [InlineData("Grep(TODO)")]
    [InlineData("Glob")]
    [InlineData("Glob(**/*.cs)")]
    [InlineData("WebFetch")]
    [InlineData("WebFetch(example.com)")]
    [InlineData("WebSearch")]
    [InlineData("WebSearch(example)")]
    public void A_vendor_recognised_tool_rule_is_allowed_and_translates_to_deny(string rule)
    {
        var adapter = Adapter();
        var request = new SessionRequest("/root", "sonnet", "high", "say hello", Quiet: false, DeniedTools: [rule]);

        Assert.True(adapter.CanDeny([rule], out _));

        var args = adapter.BuildAttached(request).ArgumentList;

        Assert.Equal(["--effort", "high", "--cwd", "/root", "--deny", rule, "say hello"], args);
    }

    [Fact]
    public void Bare_agent_is_allowed_and_translates_to_disallowed_tools_not_deny()
    {
        var adapter = Adapter();
        var request = new SessionRequest("/root", "sonnet", "high", "say hello", Quiet: false, DeniedTools: ["Agent"]);

        Assert.True(adapter.CanDeny(["Agent"], out _));

        var args = adapter.BuildAttached(request).ArgumentList;

        Assert.Equal(["--effort", "high", "--cwd", "/root", "--disallowed-tools", "Agent", "say hello"], args);
        Assert.DoesNotContain("--deny", args);
    }

    [Fact]
    public void Agent_and_a_deny_rule_together_emit_disallowed_tools_before_deny()
    {
        var request = new SessionRequest("/root", "sonnet", "high", "say hello", Quiet: false, DeniedTools: ["Agent", "Bash"]);

        var args = Adapter().BuildAttached(request).ArgumentList;

        Assert.Equal(["--effort", "high", "--cwd", "/root", "--disallowed-tools", "Agent", "--deny", "Bash", "say hello"], args);
    }

    [Theory]
    [InlineData("NotebookEdit")]
    [InlineData("Agent(explore)")]
    [InlineData("mcp__x__y")]
    [InlineData("PowerShell")]
    public void An_unrecognised_rule_is_refused_with_the_exact_sentence(string rule)
    {
        Assert.False(Adapter().CanDeny([rule], out var refusal));
        Assert.Equal(
            $"hatch: grok cannot honour the playbook's denied-tools rule \"{rule}\" - it has no equivalent, so this increment is not spawned under grok",
            refusal);
    }

    [Theory]
    [InlineData("NotebookEdit")]
    [InlineData("Agent(explore)")]
    [InlineData("mcp__x__y")]
    [InlineData("PowerShell")]
    public void A_refused_rule_throws_from_both_builds_with_the_exact_sentence(string rule)
    {
        var request = new SessionRequest("/root", "sonnet", "high", "say hello", Quiet: true, DeniedTools: [rule]);
        var expected = $"hatch: grok cannot honour the playbook's denied-tools rule \"{rule}\" - it has no equivalent, so this increment is not spawned under grok";

        var headless = Assert.Throws<InvalidOperationException>(() => Adapter().BuildHeadless(request));
        var attached = Assert.Throws<InvalidOperationException>(() => Adapter().BuildAttached(request));

        Assert.Equal(expected, headless.Message);
        Assert.Equal(expected, attached.Message);
    }

    [Fact]
    public void A_translatable_rule_does_not_excuse_a_refused_one()
    {
        var adapter = Adapter();

        Assert.Throws<InvalidOperationException>(() => adapter.BuildHeadless(new SessionRequest("/root", "sonnet", "high", "p", Quiet: true, DeniedTools: ["Bash", "NotebookEdit"])));
        Assert.Throws<InvalidOperationException>(() => adapter.BuildHeadless(new SessionRequest("/root", "sonnet", "high", "p", Quiet: true, DeniedTools: ["NotebookEdit", "Bash"])));
    }

    [Fact]
    public void A_pinned_model_passes_through_m()
    {
        var request = new SessionRequest("/root", "grok-4.7", "high", "prompt", Quiet: true);

        var args = Adapter().BuildAttached(request).ArgumentList;

        Assert.Equal(["-m", "grok-4.7", "--effort", "high", "--cwd", "/root", "prompt"], args);
    }

    [Theory]
    [InlineData("haiku")]
    [InlineData("opus")]
    [InlineData("fable")]
    [InlineData("sonnet")]
    public void Every_tier_omits_m_when_shipped_null(string tier)
    {
        var request = new SessionRequest("/root", tier, "high", "prompt", Quiet: true);

        var args = Adapter().BuildAttached(request).ArgumentList;

        Assert.DoesNotContain("-m", args);
    }

    [Fact]
    public void An_overridden_tier_passes_its_pinned_id_through_m()
    {
        var tiers = new Dictionary<string, string?> { ["haiku"] = "grok-4-fast", ["opus"] = "grok-4.7" };
        var request = new SessionRequest("/root", "opus", "high", "prompt", Quiet: true);

        var args = Adapter(tiers).BuildAttached(request).ArgumentList;

        Assert.Equal(["-m", "grok-4.7", "--effort", "high", "--cwd", "/root", "prompt"], args);
    }

    [Fact]
    public void An_arbitrary_model_string_not_in_the_tiers_table_passes_through_unchanged()
    {
        var request = new SessionRequest("/root", "some-other-model", "high", "prompt", Quiet: true);

        var args = Adapter().BuildAttached(request).ArgumentList;

        Assert.Equal(["-m", "some-other-model", "--effort", "high", "--cwd", "/root", "prompt"], args);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("minimal")]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    [InlineData("xhigh")]
    public void Effort_passes_straight_through(string effort)
    {
        var request = new SessionRequest("/root", "sonnet", effort, "prompt", Quiet: true);

        var args = Adapter().BuildAttached(request).ArgumentList;

        Assert.Contains("--effort", args);
        Assert.Contains(effort, args);
    }

    [Theory]
    [InlineData("low", "low")]
    [InlineData("medium", "medium")]
    [InlineData("high", "high")]
    [InlineData("xhigh", "xhigh")]
    [InlineData("max", "xhigh")]
    public void Efforts_map_max_to_xhigh_and_pass_the_rest_through(string effort, string expected)
    {
        var request = new SessionRequest("/root", "sonnet", effort, "prompt", Quiet: true);

        var args = Adapter().BuildAttached(request).ArgumentList;

        Assert.Equal(expected, args[args.IndexOf("--effort") + 1]);
    }

    [Fact]
    public void Collapsing_more_than_one_tier_onto_the_default_warns_once_per_adapter()
    {
        var adapter = Adapter();

        adapter.BuildAttached(new SessionRequest("/root", "haiku", "high", "p", Quiet: true));
        adapter.BuildAttached(new SessionRequest("/root", "opus", "high", "p", Quiet: true));

        Assert.Single(_warnings, w => w.Contains("haiku, opus and fable run on the CLI's default model here"));
    }

    [Fact]
    public void An_override_table_that_leaves_only_one_tier_on_the_default_does_not_warn()
    {
        var tiers = new Dictionary<string, string?> { ["haiku"] = "grok-4-fast", ["opus"] = "grok-4.7" };
        _ = Adapter(tiers);

        Assert.DoesNotContain(_warnings, w => w.Contains("run on the CLI's default model here"));
    }

    [Fact]
    public void PromptOnStdin_and_CanHook_are_false()
    {
        var adapter = Adapter();

        Assert.False(adapter.PromptOnStdin);
        Assert.False(adapter.CanHook);
    }

    [Fact]
    public void WriteHooks_returns_null_and_warns_once_across_two_calls()
    {
        var adapter = Adapter();

        Assert.Null(adapter.WriteHooks("/tmp", "AER-1", "/path/to/hatch", null));
        Assert.Null(adapter.WriteHooks("/tmp", "AER-1", "/path/to/hatch", null));

        Assert.Single(_warnings, w => w.Contains("grok has no hooks Hatch can lend a single session"));
    }

    [Fact]
    public void AddDirs_warns_once_across_headless_and_attached()
    {
        var adapter = Adapter();
        var request = new SessionRequest("/root", "sonnet", "high", "prompt", Quiet: true, AddDirs: ["/other"]);

        adapter.BuildHeadless(request);
        adapter.Release(request);
        adapter.BuildAttached(request);

        Assert.Single(_warnings, w => w.Contains("grok has no add-dir flag"));
        Assert.DoesNotContain("--add-dir", adapter.BuildAttached(request).ArgumentList);
    }

    [Fact]
    public void BuildHeadless_and_BuildAttached_redirect_stdio_only_on_the_headless_spawn()
    {
        var request = new SessionRequest("/root", "sonnet", "high", "prompt", Quiet: true);
        var adapter = Adapter();

        var attached = adapter.BuildAttached(request);
        Assert.False(attached.RedirectStandardInput);
        Assert.False(attached.RedirectStandardOutput);
    }

    [Fact]
    public void CanSpawn_refuses_when_HATCH_GROK_BIN_is_not_there()
    {
        var adapter = new GrokAdapter(configured: "/does/not/exist");

        Assert.False(adapter.CanSpawn(out var refusal));
        Assert.Equal("hatch: HATCH_GROK_BIN is not there: /does/not/exist", refusal);
    }

    [Fact]
    public void CanSpawn_names_the_variable_and_the_auth_env_when_nothing_is_on_path()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", Path.Combine(Path.GetTempPath(), "no-such-dir"));
        try
        {
            Assert.False(new GrokAdapter().CanSpawn(out var refusal));
            Assert.Equal(
                "hatch: no grok CLI on PATH.\n\n" +
                "  Install it (https://docs.x.ai/build/overview), or point at one you have:\n" +
                "      export HATCH_GROK_BIN=/path/to/grok\n\n" +
                "  Headless auth is XAI_API_KEY or a cached `grok login` token.",
                refusal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", path);
        }
    }

    [Fact]
    public void ResumeCommand_is_grok_dash_r()
    {
        Assert.Equal("grok -r abc123", Adapter().ResumeCommand("abc123"));
    }

    [Fact]
    public void Identity_and_probe()
    {
        var adapter = Adapter();

        Assert.Equal("grok", adapter.Id);
        Assert.Equal("Grok", adapter.DisplayName);
        Assert.Null(adapter.Probe);
    }

    [Fact]
    public void HookOutput_matches_claudes_own_writer_byte_for_byte()
    {
        var grok = Adapter().HookOutput;
        var claude = new ClaudeAdapter(_bin).HookOutput;

        Assert.Equal(claude.WriteStop("text"), grok.WriteStop("text"));
        Assert.Equal(claude.WritePostToolUse("text"), grok.WritePostToolUse("text"));
    }

    private static string PromptFile(SessionRequest request) =>
        Path.Combine(Path.GetTempPath(), $"hatch-grok-{request.SessionId ?? Guid.Empty.ToString()}.md");

    [Fact]
    public void The_spawned_session_names_the_runner_that_dispatched_it()
    {
        var request = new SessionRequest("/root", "sonnet", "high", "prompt", Quiet: true, RunnerName: "test:/checkout");
        var adapter = Adapter();

        Assert.Equal("test:/checkout", adapter.BuildHeadless(request).Environment["HATCH_RUNNER"]);

        adapter.Release(request);
    }}

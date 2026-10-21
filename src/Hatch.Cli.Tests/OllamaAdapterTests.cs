namespace Hatch.Cli.Tests;

/// <summary>
/// <see cref="OllamaAdapter"/>'s own argv, reached directly the way
/// <see cref="CodexAdapterTests"/> reaches Codex's.
/// </summary>
public sealed class OllamaAdapterTests : IDisposable
{
    private const string Pinned = "qwen3-coder:30b";

    private readonly string _bin = Path.GetTempFileName();
    private readonly List<string> _warnings = [];

    public void Dispose() => File.Delete(_bin);

    private OllamaAdapter Adapter(OllamaHost? host = null, IReadOnlyDictionary<string, string?>? tiers = null)
    {
        var adapter = new OllamaAdapter(host, tiers, _warnings.Add, _bin);
        Assert.True(adapter.CanSpawn(out _));
        return adapter;
    }

    private static SessionRequest Request(
        string model = Pinned, string effort = "high", bool quiet = false,
        string? hooks = null, string[]? denied = null) =>
        new("/root", model, effort, "prompt", quiet, HookSettings: hooks, DeniedTools: denied);

    [Fact]
    public void Identity()
    {
        var adapter = new OllamaAdapter();

        Assert.Equal("ollama", adapter.Id);
        Assert.Equal("Ollama", adapter.DisplayName);
        Assert.Null(adapter.Probe);
    }

    [Fact]
    public void BuildHeadless_with_no_hooks_and_no_denied_tools_is_codexs_argv_with_the_provider_flags()
    {
        var args = Adapter().BuildHeadless(Request()).ArgumentList;

        Assert.Equal(
            ["exec", "--oss", "--local-provider", "ollama", "-m", "qwen3-coder:30b", "-C", "/root",
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
            ["exec", "--oss", "--local-provider", "ollama", "-m", Pinned, "-C", "/root",
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
            ["exec", "--oss", "--local-provider", "ollama", "-m", Pinned, "-C", "/root",
                "--add-dir", "/a", "--add-dir", "/b",
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
            ["exec", "--oss", "--local-provider", "ollama", "-m", Pinned, "-C", "/root",
                "--json", "-c", "model_reasoning_effort=high", "-s", "read-only"],
            args);
        Assert.DoesNotContain("--approve-for-me", args);
    }

    [Theory]
    [InlineData("Bash")]
    [InlineData("Bash(rm:*)")]
    [InlineData("Edit(src/**)")]
    [InlineData("mcp__server__tool")]
    [InlineData("*")]
    public void An_untranslatable_rule_throws_in_both_builds_and_names_the_rule(string rule)
    {
        var adapter = Adapter();

        var headless = Assert.Throws<InvalidOperationException>(() => adapter.BuildHeadless(Request(denied: [rule])));
        var attached = Assert.Throws<InvalidOperationException>(() => adapter.BuildAttached(Request(denied: [rule])));

        Assert.Contains($"\"{rule}\"", headless.Message);
        Assert.Contains($"\"{rule}\"", attached.Message);
    }

    [Fact]
    public void Release_does_not_throw()
    {
        var exception = Record.Exception(() => ((IAgentAdapter)Adapter()).Release(Request()));

        Assert.Null(exception);
    }

    [Fact]
    public void A_mapped_alias_sends_its_id()
    {
        var adapter = Adapter(tiers: new Dictionary<string, string?> { ["sonnet"] = Pinned });

        var headless = adapter.BuildHeadless(Request(model: "sonnet")).ArgumentList;
        var attached = adapter.BuildAttached(Request(model: "sonnet")).ArgumentList;

        Assert.Equal(Pinned, headless[headless.IndexOf("-m") + 1]);
        Assert.Equal(Pinned, attached[attached.IndexOf("-m") + 1]);
    }

    [Theory]
    [InlineData("haiku")]
    [InlineData("sonnet")]
    [InlineData("opus")]
    [InlineData("fable")]
    public void An_unmapped_alias_sends_no_model_flag_at_all(string alias)
    {
        var adapter = Adapter();

        var headless = adapter.BuildHeadless(Request(model: alias)).ArgumentList;
        var attached = adapter.BuildAttached(Request(model: alias)).ArgumentList;

        Assert.DoesNotContain("-m", headless);
        Assert.DoesNotContain(alias, headless);
        Assert.DoesNotContain("-m", attached);
        Assert.DoesNotContain(alias, attached);
        Assert.Equal(["--oss", "--local-provider", "ollama", "-C", "/root", "prompt"], attached);
    }

    [Fact]
    public void A_pinned_id_passes_through_even_beside_mapped_tiers()
    {
        var adapter = Adapter(tiers: new Dictionary<string, string?> { ["sonnet"] = Pinned });

        var args = adapter.BuildHeadless(Request(model: "llama3.3:70b")).ArgumentList;

        Assert.Equal("llama3.3:70b", args[args.IndexOf("-m") + 1]);
    }

    [Fact]
    public void An_explicit_host_is_carried_with_the_openai_path_headless_and_attached()
    {
        var adapter = Adapter(new OllamaHost("http://h:1234", true));

        Assert.Equal("http://h:1234/v1", adapter.BuildHeadless(Request()).Environment["CODEX_OSS_BASE_URL"]);
        Assert.Equal("http://h:1234/v1", adapter.BuildAttached(Request()).Environment["CODEX_OSS_BASE_URL"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_default_or_absent_host_leaves_the_inherited_value_alone(bool withDefault)
    {
        var adapter = Adapter(withDefault ? OllamaHost.Default : null);
        var inherited = Environment.GetEnvironmentVariable("CODEX_OSS_BASE_URL");

        foreach (var start in new[] { adapter.BuildHeadless(Request()), adapter.BuildAttached(Request()) })
        {
            start.Environment.TryGetValue("CODEX_OSS_BASE_URL", out var carried);
            Assert.Equal(inherited, carried);
        }
    }

    [Fact]
    public void BuildAttached_is_the_provider_flags_the_model_the_root_and_the_bare_prompt()
    {
        var args = Adapter().BuildAttached(Request() with { Prompt = "prompt" }).ArgumentList;

        Assert.Equal(["--oss", "--local-provider", "ollama", "-m", Pinned, "-C", "/root", "prompt"], args);
    }

    [Fact]
    public void BuildAttached_never_carries_a_sandbox_or_a_grant_even_with_a_write_tool_denied()
    {
        var args = Adapter().BuildAttached(Request(denied: ["Edit"])).ArgumentList;

        Assert.Equal(["--oss", "--local-provider", "ollama", "-m", Pinned, "-C", "/root", "prompt"], args);
    }

    [Fact]
    public void ResumeCommand_rejoins_through_exec_resume_with_both_provider_flags()
    {
        Assert.Equal("codex exec resume --oss --local-provider ollama abc", new OllamaAdapter().ResumeCommand("abc"));
    }

    [Theory]
    [InlineData("You've hit your session limit · resets 7:40pm (America/New_York)")]
    [InlineData("You've hit your usage limit. Try again in 4 days")]
    [InlineData("rate limit exceeded")]
    [InlineData("usage_limit_reached")]
    [InlineData("rate_limit_exceeded")]
    [InlineData("weekly usage limit reached")]
    public void RecogniseLimit_is_null_for_every_sentence_codex_would_recognise(string said)
    {
        var endedAt = DateTimeOffset.Parse("2026-09-28T12:00:00+00:00");

        Assert.NotNull(UsageLimit.RecogniseCodex(said, endedAt));
        Assert.Null(new OllamaAdapter().RecogniseLimit(said, endedAt));
    }

    [Fact]
    public void WriteHooks_answers_null_and_no_codex_home_is_set()
    {
        Assert.Null(new OllamaAdapter().WriteHooks(Path.GetTempPath(), "AER-1", "hatch", null));

        Adapter().BuildHeadless(Request()).Environment.TryGetValue("CODEX_HOME", out var home);
        Assert.Equal(Environment.GetEnvironmentVariable("CODEX_HOME"), home);
    }

    [Fact]
    public void CreateReader_hands_the_stream_reader_the_id_that_was_sent()
    {
        var facts = new RunFacts();
        var adapter = Adapter(tiers: new Dictionary<string, string?> { ["sonnet"] = Pinned });

        _ = adapter.CreateReader("/root", facts, "sonnet")
            .Read("""{"type":"turn.completed","usage":{"input_tokens":1,"output_tokens":1}}""").ToList();

        Assert.Equal(Pinned, facts.Result!.Models![0].Model);
    }

    [Fact]
    public void CreateReader_hands_the_stream_reader_the_alias_when_no_id_was_sent()
    {
        var facts = new RunFacts();

        _ = Adapter().CreateReader("/root", facts, "sonnet")
            .Read("""{"type":"turn.completed","usage":{"input_tokens":1,"output_tokens":1}}""").ToList();

        Assert.Equal("sonnet", facts.Result!.Models![0].Model);
    }

    [Fact]
    public void CanSpawn_refuses_when_the_configured_binary_is_not_there()
    {
        var adapter = new OllamaAdapter(configured: "/does/not/exist");

        Assert.False(adapter.CanSpawn(out var refusal));
        Assert.Equal("hatch: HATCH_CODEX_BIN is not there: /does/not/exist", refusal);
    }

    [Fact]
    public void CanSpawn_names_the_variable_when_nothing_is_on_path()
    {
        var adapter = new OllamaAdapter(path: Path.Combine(Path.GetTempPath(), "no-such-dir"));

        Assert.False(adapter.CanSpawn(out var refusal));
        Assert.Equal(
            "hatch: no codex CLI on PATH.\n\n  Install it, or point at one you have:\n      export HATCH_CODEX_BIN=/path/to/codex",
            refusal);
    }

    [Fact]
    public void Building_before_can_spawn_is_a_bug_and_throws()
    {
        Assert.Throws<InvalidOperationException>(() => new OllamaAdapter(configured: _bin).BuildHeadless(Request()));
    }

    [Fact]
    public void The_warning_names_all_four_aliases_when_none_is_mapped()
    {
        _ = Adapter();

        Assert.Equal(["hatch: ollama - haiku, sonnet, opus and fable run on the CLI's default model"], _warnings);
    }

    [Fact]
    public void The_warning_names_only_the_unmapped_aliases()
    {
        _ = Adapter(tiers: new Dictionary<string, string?> { ["sonnet"] = Pinned, ["fable"] = Pinned });

        Assert.Equal(["hatch: ollama - haiku and opus run on the CLI's default model"], _warnings);
    }

    [Fact]
    public void The_warning_names_a_single_alias_bare()
    {
        _ = Adapter(tiers: new Dictionary<string, string?>
        {
            ["haiku"] = Pinned, ["sonnet"] = Pinned, ["fable"] = Pinned,
        });

        Assert.Equal(["hatch: ollama - opus run on the CLI's default model"], _warnings);
    }

    [Fact]
    public void There_is_no_warning_when_every_alias_is_mapped()
    {
        _ = Adapter(tiers: new Dictionary<string, string?>
        {
            ["haiku"] = Pinned, ["sonnet"] = Pinned, ["opus"] = Pinned, ["fable"] = Pinned,
        });

        Assert.Empty(_warnings);
    }

    [Fact]
    public void The_warning_fires_once_per_adapter_however_many_spawns_are_built()
    {
        var adapter = Adapter();

        _ = adapter.BuildHeadless(Request(model: "haiku"));
        _ = adapter.BuildAttached(Request(model: "haiku"));
        _ = adapter.BuildHeadless(Request(model: "opus"));

        Assert.Single(_warnings);
    }
}

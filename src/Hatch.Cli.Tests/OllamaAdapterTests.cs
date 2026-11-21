using System.Diagnostics;
using System.Net;

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

    private OllamaAdapter Adapter(
        OllamaHost? host = null, IReadOnlyDictionary<string, string?>? tiers = null, HttpMessageHandler? wire = null)
    {
        var adapter = new OllamaAdapter(host, tiers, _warnings.Add, _bin, handler: wire ?? new TagsWire());
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
    public void RecogniseModelRefusal_recognises_its_own_sentence_and_not_codexs()
    {
        var endedAt = DateTimeOffset.Parse("2026-09-28T12:00:00+00:00");

        Assert.Null(new OllamaAdapter().RecogniseModelRefusal(
            "the 'gpt-7-nova' model is not supported when using Codex with a ChatGPT account", endedAt));

        var refusal = new OllamaAdapter().RecogniseModelRefusal(
            "model \"llama3:70b\" not found, try pulling it first", endedAt);

        Assert.NotNull(refusal);
        Assert.Equal("llama3:70b", refusal!.Value.Model);
        Assert.Null(refusal.Value.ResetAt);
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
    public void CanSpawn_with_models_in_the_body_answers_true()
    {
        var adapter = new OllamaAdapter(
            configured: _bin, handler: new TagsWire(body: """{"models":[{"name":"qwen3-coder:30b"}]}"""));

        Assert.True(adapter.CanSpawn(out var refusal));
        Assert.Equal("", refusal);
    }

    [Fact]
    public void CanSpawn_refuses_when_the_wire_cannot_be_reached()
    {
        var adapter = new OllamaAdapter(configured: _bin, handler: new ThrowingWire(new HttpRequestException("refused")));

        Assert.False(adapter.CanSpawn(out var refusal));
        Assert.Equal(
            $"hatch: ollama is not answering at {OllamaHost.Default.Url} - start it, or point HATCH_OLLAMA_HOST at one that is",
            refusal);
    }

    [Fact]
    public void CanSpawn_refuses_when_the_wire_times_out()
    {
        var adapter = new OllamaAdapter(
            configured: _bin,
            handler: new ThrowingWire(new TaskCanceledException("the request was canceled due to the configured HttpClient.Timeout")));

        Assert.False(adapter.CanSpawn(out var refusal));
        Assert.Equal(
            $"hatch: ollama is not answering at {OllamaHost.Default.Url} - start it, or point HATCH_OLLAMA_HOST at one that is",
            refusal);
    }

    [Fact]
    public void CanSpawn_refuses_on_a_non_2xx_answer()
    {
        var adapter = new OllamaAdapter(configured: _bin, handler: new TagsWire(status: HttpStatusCode.ServiceUnavailable));

        Assert.False(adapter.CanSpawn(out var refusal));
        Assert.Equal(
            $"hatch: ollama is not answering at {OllamaHost.Default.Url} - start it, or point HATCH_OLLAMA_HOST at one that is",
            refusal);
    }

    [Fact]
    public void CanSpawn_names_an_explicit_host_in_the_refusal()
    {
        var host = new OllamaHost("http://h:1234", true);
        var adapter = new OllamaAdapter(host, configured: _bin, handler: new ThrowingWire(new HttpRequestException("refused")));

        Assert.False(adapter.CanSpawn(out var refusal));
        Assert.Equal(
            "hatch: ollama is not answering at http://h:1234 - start it, or point HATCH_OLLAMA_HOST at one that is",
            refusal);
    }

    [Fact]
    public void CanSpawn_asks_the_wire_only_once_across_three_calls()
    {
        var wire = new TagsWire();
        var adapter = new OllamaAdapter(configured: _bin, handler: wire);

        Assert.True(adapter.CanSpawn(out _));
        Assert.True(adapter.CanSpawn(out _));
        Assert.True(adapter.CanSpawn(out _));

        Assert.Equal(1, wire.Calls);
    }

    [Fact]
    public void CanSpawn_with_a_missing_configured_binary_never_asks_the_wire()
    {
        var adapter = new OllamaAdapter(configured: "/does/not/exist", handler: new ThrowingWire(new HttpRequestException("refused")));

        Assert.False(adapter.CanSpawn(out var refusal));
        Assert.Equal("hatch: HATCH_CODEX_BIN is not there: /does/not/exist", refusal);
    }

    private const string Unpulled =
        "pinned to qwen3-coder:30b, which this runner's ollama has not pulled - ollama pull qwen3-coder:30b would";

    private static TagsWire Pulled(string name = Pinned) => new(body: $$"""{"models":[{"name":"{{name}}"}]}""");

    [Fact]
    public void Models_are_the_mapped_ids_in_alias_order_then_the_pulled_ones_sorted()
    {
        // Through the interface, so the override is proven to be the one an
        // IAgentAdapter caller reaches rather than the aliases default.
        IAgentAdapter adapter = Adapter(
            tiers: new Dictionary<string, string?> { ["opus"] = "big:70b", ["haiku"] = "tiny:1b" },
            wire: new TagsWire(body: """{"models":[{"name":"zeta:1b"},{"name":"alpha:1b"},{"name":"tiny:1b"}]}"""));

        Assert.Equal(["tiny:1b", "big:70b", "alpha:1b", "zeta:1b"], adapter.Models);
    }

    [Fact]
    public void Models_drop_a_pulled_name_the_API_would_refuse()
    {
        IAgentAdapter adapter = Adapter(wire: new TagsWire(body: """{"models":[{"name":"has space:7b"},{"name":"llama3:8b"}]}"""));

        Assert.Equal(["llama3:8b"], adapter.Models);
    }

    [Fact]
    public void Models_before_the_list_was_read_are_only_the_mapped_ids()
    {
        IAgentAdapter adapter = new OllamaAdapter(
            tiers: new Dictionary<string, string?> { ["sonnet"] = Pinned }, handler: new TagsWire());

        Assert.Equal([Pinned], adapter.Models);
    }

    [Fact]
    public void Models_with_nothing_mapped_and_nothing_pulled_are_empty()
    {
        IAgentAdapter adapter = Adapter();

        Assert.Empty(adapter.Models);
    }

    [Fact]
    public void CanServe_a_pinned_id_in_the_pulled_list_is_true()
    {
        IAgentAdapter adapter = Adapter(wire: Pulled());

        Assert.True(adapter.CanServe(Pinned, out var refusal));
        Assert.Equal("", refusal);
    }

    [Fact]
    public void CanServe_a_pinned_id_not_in_the_pulled_list_refuses_with_the_sentence()
    {
        IAgentAdapter adapter = Adapter(wire: Pulled("llama3:8b"));

        Assert.False(adapter.CanServe(Pinned, out var refusal));
        Assert.Equal(Unpulled, refusal);
    }

    [Fact]
    public void CanServe_an_alias_mapped_to_a_pulled_id_is_true()
    {
        IAgentAdapter adapter = Adapter(tiers: new Dictionary<string, string?> { ["sonnet"] = Pinned }, wire: Pulled());

        Assert.True(adapter.CanServe("sonnet", out var refusal));
        Assert.Equal("", refusal);
    }

    [Fact]
    public void CanServe_an_alias_mapped_to_an_unpulled_id_refuses_naming_the_id_and_not_the_alias()
    {
        IAgentAdapter adapter = Adapter(
            tiers: new Dictionary<string, string?> { ["sonnet"] = Pinned }, wire: Pulled("llama3:8b"));

        Assert.False(adapter.CanServe("sonnet", out var refusal));
        Assert.Equal(Unpulled, refusal);
    }

    [Fact]
    public void CanServe_an_unmapped_alias_is_true_even_against_an_empty_list()
    {
        IAgentAdapter adapter = Adapter();

        Assert.True(adapter.CanServe("opus", out var refusal));
        Assert.Equal("", refusal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void CanServe_no_model_is_true(string? model)
    {
        IAgentAdapter adapter = Adapter(wire: Pulled("llama3:8b"));

        Assert.True(adapter.CanServe(model, out var refusal));
        Assert.Equal("", refusal);
    }

    [Fact]
    public void CanServe_before_the_list_has_been_read_is_true_for_a_pinned_id()
    {
        IAgentAdapter adapter = new OllamaAdapter(configured: _bin);

        Assert.True(adapter.CanServe(Pinned, out var refusal));
        Assert.Equal("", refusal);
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

    // ParseTiers

    private static void Tiers(string? raw, params (string Alias, string Id)[] expected)
    {
        Assert.Null(OllamaAdapter.ParseTiers(raw, out var tiers));
        Assert.Equal(expected.Length, tiers.Count);
        foreach (var (alias, id) in expected) Assert.Equal(id, tiers[alias]);
    }

    [Fact]
    public void ParseTiers_reads_all_four_aliases()
    {
        Tiers("haiku=a:b,sonnet=c:d,opus=e:f,fable=qwen3-coder:30b",
            ("haiku", "a:b"), ("sonnet", "c:d"), ("opus", "e:f"), ("fable", Pinned));
    }

    [Fact]
    public void ParseTiers_reads_a_subset()
    {
        Tiers($"opus={Pinned}", ("opus", Pinned));
    }

    [Fact]
    public void ParseTiers_reads_any_order()
    {
        Tiers("fable=e:f,opus=c:d,haiku=a:b", ("haiku", "a:b"), ("opus", "c:d"), ("fable", "e:f"));
    }

    [Fact]
    public void ParseTiers_trims_spaces_around_entries_commas_and_the_equals_sign()
    {
        Tiers($"  haiku = {Pinned} ,  sonnet=a:b  ", ("haiku", Pinned), ("sonnet", "a:b"));
    }

    [Fact]
    public void ParseTiers_keeps_a_colon_and_a_slash_in_the_id()
    {
        Tiers("sonnet=hf.co/user/repo:Q4_K_M", ("sonnet", "hf.co/user/repo:Q4_K_M"));
    }

    [Fact]
    public void ParseTiers_stores_the_alias_lower_case()
    {
        Assert.Null(OllamaAdapter.ParseTiers($"HAIKU={Pinned}", out var tiers));

        Assert.Equal(Pinned, tiers["haiku"]);
        Assert.False(tiers.ContainsKey("HAIKU"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",,")]
    [InlineData(" , ,")]
    public void ParseTiers_answers_empty_tiers_for_nothing(string? raw)
    {
        Tiers(raw);
    }

    [Theory]
    [InlineData("haiku", "entry \"haiku\" is not alias=id")]
    [InlineData("haiku=", "entry \"haiku=\" is not alias=id")]
    [InlineData("=qwen3-coder:30b", "entry \"=qwen3-coder:30b\" is not alias=id")]
    [InlineData("fast=", "entry \"fast=\" is not alias=id")]
    [InlineData("fast=qwen3-coder:30b", "\"fast\" is not an alias (haiku, sonnet, opus, fable)")]
    [InlineData("haiku=a:b,haiku=c:d", "\"haiku\" is given twice")]
    [InlineData("haiku=a:b,HAIKU=c:d", "\"HAIKU\" is given twice")]
    [InlineData("haiku=llama3.3", "\"llama3.3\" is not an Ollama model id (name:tag, e.g. qwen3-coder:30b)")]
    [InlineData("haiku=gpt-6-luna", "\"gpt-6-luna\" is not an Ollama model id (name:tag, e.g. qwen3-coder:30b)")]
    [InlineData("haiku=a=b", "\"a=b\" is not an Ollama model id (name:tag, e.g. qwen3-coder:30b)")]
    [InlineData("sonnet=a:b,fast=c:d,opus=llama3.3", "\"fast\" is not an alias (haiku, sonnet, opus, fable)")]
    public void ParseTiers_refuses_a_malformed_entry_in_a_sentence_and_leaves_tiers_empty(string raw, string sentence)
    {
        Assert.Equal(sentence, OllamaAdapter.ParseTiers(raw, out var tiers));
        Assert.Empty(tiers);
    }

    /// <summary>A wire that answers <c>GET /api/tags</c> with a scripted status and body.</summary>
    private sealed class TagsWire(HttpStatusCode status = HttpStatusCode.OK, string body = """{"models":[]}""") : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body),
            });
        }
    }

    private static string Tags(params string[] names) =>
        "{\"models\":[" + string.Join(",", names.Select(n => $"{{\"name\":\"{n}\"}}")) + "]}";

    private static string TagsWithDigests(params (string Name, string? Digest)[] models) =>
        "{\"models\":[" + string.Join(",", models.Select(m =>
            m.Digest is null ? $"{{\"name\":\"{m.Name}\"}}" : $"{{\"name\":\"{m.Name}\",\"digest\":\"{m.Digest}\"}}")) + "]}";

    [Fact]
    public void Digest_is_the_one_the_list_reported_for_a_pulled_tag()
    {
        var adapter = Adapter(wire: new TagsWire(body: TagsWithDigests((Pinned, "abc123"), ("a:1b", "def456"))));

        Assert.Equal("abc123", adapter.Digest(Pinned));
        Assert.Equal("def456", adapter.Digest("a:1b"));
    }

    [Fact]
    public void Digest_follows_an_alias_through_the_tiers_as_CanServe_does()
    {
        var tiers = new Dictionary<string, string?> { ["sonnet"] = Pinned };
        var adapter = Adapter(tiers: tiers, wire: new TagsWire(body: TagsWithDigests((Pinned, "abc123"))));

        Assert.Equal("abc123", adapter.Digest("sonnet"));
    }

    [Fact]
    public void Digest_is_null_for_a_tag_not_in_the_list_an_unmapped_alias_and_no_model()
    {
        var adapter = Adapter(wire: new TagsWire(body: TagsWithDigests(("a:1b", "def456"))));

        Assert.Null(adapter.Digest(Pinned));
        Assert.Null(adapter.Digest("opus"));
        Assert.Null(adapter.Digest(null));
        Assert.Null(adapter.Digest(""));
    }

    [Fact]
    public void Digest_is_null_when_the_list_gave_none_for_that_entry()
    {
        var adapter = Adapter(wire: new TagsWire(body: TagsWithDigests((Pinned, null))));

        Assert.True(adapter.CanServe(Pinned, out _));
        Assert.Null(adapter.Digest(Pinned));
    }

    [Fact]
    public void Refresh_replaces_the_digest_when_a_tag_is_pushed_again()
    {
        var adapter = Adapter(wire: new SequenceWire(
            TagsWithDigests((Pinned, "old")), TagsWithDigests((Pinned, "new"))));
        Assert.Equal("old", adapter.Digest(Pinned));

        // Only a refresh reads the server: the digest in hand is the one the session started under.
        Assert.Equal("old", adapter.Digest(Pinned));
        adapter.Refresh();

        Assert.Equal("new", adapter.Digest(Pinned));
    }

    [Fact]
    public void Refresh_makes_Models_follow_the_second_list()
    {
        IAgentAdapter adapter = Adapter(wire: new SequenceWire(Tags("a:1b"), Tags("a:1b", "b:1b")));
        Assert.Equal(["a:1b"], adapter.Models);

        adapter.Refresh();

        Assert.Equal(["a:1b", "b:1b"], adapter.Models);
    }

    [Fact]
    public void Refresh_makes_CanServe_follow_a_pull()
    {
        IAgentAdapter adapter = Adapter(wire: new SequenceWire(Tags("a:1b"), Tags("a:1b", Pinned)));
        Assert.False(adapter.CanServe(Pinned, out _));

        adapter.Refresh();

        Assert.True(adapter.CanServe(Pinned, out _));
    }

    [Fact]
    public void Refresh_makes_CanServe_follow_a_removal()
    {
        IAgentAdapter adapter = Adapter(wire: new SequenceWire(Tags(Pinned), Tags("a:1b")));
        Assert.True(adapter.CanServe(Pinned, out _));

        adapter.Refresh();

        Assert.False(adapter.CanServe(Pinned, out var refusal));
        Assert.Equal(Unpulled, refusal);
    }

    [Fact]
    public void Refresh_keeps_the_last_list_and_says_so_once_when_the_read_fails()
    {
        IAgentAdapter adapter = Adapter(wire: new SequenceWire(Tags("a:1b"), null, null));

        adapter.Refresh();
        adapter.Refresh();

        Assert.Equal(["a:1b"], adapter.Models);
        Assert.Equal(
            [$"hatch: ollama is not answering at {OllamaHost.Default.Url} - keeping the model list from the last read"],
            _warnings.Where(w => w.Contains("not answering")));
    }

    [Fact]
    public void Refresh_says_it_again_when_a_failure_follows_a_recovery()
    {
        IAgentAdapter adapter = Adapter(wire: new SequenceWire(Tags("a:1b"), null, Tags("b:1b"), null));

        adapter.Refresh();
        adapter.Refresh();
        Assert.Equal(["b:1b"], adapter.Models);
        adapter.Refresh();

        Assert.Equal(2, _warnings.Count(w => w.Contains("not answering")));
    }

    /// <summary>A wire that answers each read with the next body, the last repeating; null is a refused read.</summary>
    private sealed class SequenceWire(params string?[] bodies) : HttpMessageHandler
    {
        private int _next;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = bodies[Math.Min(_next++, bodies.Length - 1)];
            return body is null
                ? throw new HttpRequestException("refused")
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    /// <summary>A wire that fails to answer at all, the way a dead or slow Ollama would.</summary>
    private sealed class ThrowingWire(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw exception;
    }

    [Fact]
    public void Executable_is_null_even_with_a_configured_binary()
    {
        Assert.Null(new OllamaAdapter(configured: _bin).Executable);
    }

    // ---- The loop pulls what the board asks it to hold ----

    /// <summary>A wire whose <c>/api/pull</c> answers as told, and which keeps what it was asked.</summary>
    private sealed class PullWire(HttpStatusCode status = HttpStatusCode.OK, string body = """{"status":"success"}""") : HttpMessageHandler
    {
        public List<(string Path, string Body)> Pulls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (request.RequestUri!.AbsolutePath == "/api/pull")
            {
                Pulls.Add((request.RequestUri.AbsolutePath, await request.Content!.ReadAsStringAsync(ct)));
                return new HttpResponseMessage(status) { Content = new StringContent(body) };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"models":[]}""") };
        }
    }

    [Fact]
    public async Task PullAsync_asks_the_server_to_pull_the_model_without_streaming_and_answers_null_on_success()
    {
        var wire = new PullWire();

        var reason = await Adapter(wire: wire).PullAsync(Pinned, default);

        Assert.Null(reason);
        var pull = Assert.Single(wire.Pulls);
        using var sent = System.Text.Json.JsonDocument.Parse(pull.Body);
        Assert.Equal(Pinned, sent.RootElement.GetProperty("model").GetString());
        Assert.False(sent.RootElement.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task PullAsync_leaves_the_list_alone_until_Refresh()
    {
        IAgentAdapter adapter = Adapter(wire: new PullWire());

        await adapter.PullAsync(Pinned, default);

        Assert.DoesNotContain(Pinned, adapter.Models);
    }

    [Fact]
    public async Task PullAsync_answers_an_error_body_on_a_2xx_as_the_reason()
    {
        var reason = await Adapter(wire: new PullWire(body: """{"error":"pull model manifest: file does not exist"}""")).PullAsync(Pinned, default);

        Assert.Equal("pull model manifest: file does not exist", reason);
    }

    [Fact]
    public async Task PullAsync_answers_a_2xx_that_never_says_success_as_not_finished()
    {
        var reason = await Adapter(wire: new PullWire(body: """{"status":"pulling manifest"}""")).PullAsync(Pinned, default);

        Assert.Equal("the server did not say the pull finished", reason);
    }

    [Fact]
    public async Task PullAsync_answers_the_error_of_a_non_2xx_else_its_code()
    {
        Assert.Equal("model not found",
            await Adapter(wire: new PullWire(HttpStatusCode.NotFound, """{"error":"model not found"}""")).PullAsync(Pinned, default));
        Assert.Equal("HTTP 500",
            await Adapter(wire: new PullWire(HttpStatusCode.InternalServerError, "oops")).PullAsync(Pinned, default));
    }

    [Fact]
    public async Task PullAsync_answers_not_JSON_for_a_2xx_that_is_not()
    {
        var reason = await Adapter(wire: new PullWire(body: "<html>")).PullAsync(Pinned, default);

        Assert.Equal("the server's answer was not JSON", reason);
    }

    [Fact]
    public async Task PullAsync_says_ollama_is_not_answering_where_the_server_is_unreachable()
    {
        var adapter = new OllamaAdapter(configured: _bin, handler: new ThrowingWire(new HttpRequestException("refused")));

        var reason = await adapter.PullAsync(Pinned, default);

        Assert.StartsWith("ollama is not answering at ", reason, StringComparison.Ordinal);
    }

    // ---- A model that does not fit is refused ----

    /// <summary>A wire whose <c>/api/ps</c> answers as told, and whose <c>/api/tags</c> lists nothing.</summary>
    private sealed class PsWire(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Asked { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath != "/api/ps")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"models":[]}""") });

            Asked++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private static string Loaded(string name, string sizes, string field = "name") =>
        $$"""{"models":[{"{{field}}":"{{name}}",{{sizes}}}]}""";

    [Fact]
    public async Task SpilledAsync_answers_null_for_a_model_wholly_on_the_accelerator()
    {
        var wire = new PsWire(Loaded(Pinned, "\"size\":1000,\"size_vram\":1000"));

        Assert.Null(await Adapter(wire: wire).SpilledAsync(Pinned, default));
        Assert.Equal(1, wire.Asked);
    }

    [Fact]
    public async Task SpilledAsync_answers_the_id_where_less_of_the_model_is_in_video_memory_than_it_takes()
    {
        var wire = new PsWire(Loaded(Pinned, "\"size\":1000,\"size_vram\":600"));

        Assert.Equal(Pinned, await Adapter(wire: wire).SpilledAsync(Pinned, default));
    }

    [Fact]
    public async Task SpilledAsync_matches_the_entry_on_its_model_field_as_well_as_its_name()
    {
        var wire = new PsWire(Loaded(Pinned, "\"size\":1000,\"size_vram\":0", field: "model"));

        Assert.Equal(Pinned, await Adapter(wire: wire).SpilledAsync(Pinned, default));
    }

    [Fact]
    public async Task SpilledAsync_answers_null_for_a_model_that_is_no_longer_loaded()
    {
        Assert.Null(await Adapter(wire: new PsWire("""{"models":[]}""")).SpilledAsync(Pinned, default));
    }

    [Fact]
    public async Task SpilledAsync_does_not_judge_by_another_models_entry()
    {
        var wire = new PsWire(Loaded("llama3:8b", "\"size\":1000,\"size_vram\":100"));

        Assert.Null(await Adapter(wire: wire).SpilledAsync(Pinned, default));
    }

    [Fact]
    public async Task SpilledAsync_goes_through_the_tiers_for_an_alias()
    {
        var tiers = new Dictionary<string, string?> { ["sonnet"] = Pinned };
        var wire = new PsWire(Loaded(Pinned, "\"size\":1000,\"size_vram\":600"));

        Assert.Equal(Pinned, await Adapter(tiers: tiers, wire: wire).SpilledAsync("sonnet", default));
    }

    [Fact]
    public async Task SpilledAsync_answers_null_without_asking_for_an_unmapped_alias_or_no_model()
    {
        var wire = new PsWire(Loaded(Pinned, "\"size\":1000,\"size_vram\":600"));
        var adapter = Adapter(wire: wire);

        Assert.Null(await adapter.SpilledAsync("sonnet", default));
        Assert.Null(await adapter.SpilledAsync(null, default));
        Assert.Equal(0, wire.Asked);
    }

    [Fact]
    public async Task SpilledAsync_answers_null_where_the_server_errors_or_answers_what_is_not_JSON()
    {
        Assert.Null(await Adapter(wire: new PsWire("oops", HttpStatusCode.InternalServerError)).SpilledAsync(Pinned, default));
        Assert.Null(await Adapter(wire: new PsWire("<html>")).SpilledAsync(Pinned, default));
    }

    [Fact]
    public async Task SpilledAsync_answers_null_where_the_server_is_unreachable()
    {
        var adapter = new OllamaAdapter(configured: _bin, handler: new ThrowingWire(new HttpRequestException("refused")));

        Assert.Null(await adapter.SpilledAsync(Pinned, default));
    }

    [Fact]
    public async Task SpilledAsync_does_not_judge_an_entry_with_no_size_vram_or_a_zero_size()
    {
        Assert.Null(await Adapter(wire: new PsWire(Loaded(Pinned, "\"size\":1000"))).SpilledAsync(Pinned, default));
        Assert.Null(await Adapter(wire: new PsWire(Loaded(Pinned, "\"size\":0,\"size_vram\":0"))).SpilledAsync(Pinned, default));
    }

    [Fact]
    public async Task SpilledAsync_leaves_the_list_alone()
    {
        IAgentAdapter adapter = Adapter(wire: new PsWire(Loaded(Pinned, "\"size\":1000,\"size_vram\":600")));

        await adapter.SpilledAsync(Pinned, default);

        Assert.DoesNotContain(Pinned, adapter.Models);
    }

    [Fact]
    public async Task PullAsync_cuts_a_long_reason_to_two_hundred_characters()
    {
        var long_ = new string('x', 500);

        var reason = await Adapter(wire: new PullWire(body: $$"""{"error":"{{long_}}"}""")).PullAsync(Pinned, default);

        Assert.Equal(200, reason!.Length);
    }

    [Fact]
    public async Task PullAsync_rethrows_a_cancelled_token()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Adapter(wire: new PullWire()).PullAsync(Pinned, cts.Token));
    }

    [Fact]
    public async Task An_adapter_with_no_pull_of_its_own_says_it_has_nothing_to_pull()
    {
        IAgentAdapter adapter = new FakeAdapterWithoutPull();

        Assert.Equal("Plain has no models to pull", await adapter.PullAsync(Pinned, default));
    }

    private sealed class FakeAdapterWithoutPull : IAgentAdapter
    {
        public string Id => "plain";
        public string DisplayName => "Plain";
        public bool CanSpawn(out string refusal) { refusal = ""; return true; }
        public ProcessStartInfo BuildHeadless(SessionRequest request) => throw new NotSupportedException();
        public ProcessStartInfo BuildAttached(SessionRequest request) => throw new NotSupportedException();
        public IAgentStreamReader CreateReader(string root, RunFacts facts, string model) => throw new NotSupportedException();
        public string? ReadClosing(string json) => null;
        public string ResumeCommand(string sessionId) => sessionId;
        public IAgentHooks? WriteHooks(string temp, string key, string hatch, string? rtk) => null;
        public IUsageProbe? Probe => null;
        public IHookOutputWriter HookOutput => throw new NotSupportedException();
        public UsageLimit.Recognised? RecogniseLimit(string? said, DateTimeOffset endedAt, TimeZoneInfo? runnerZone = null) => null;
        public ModelRefusal.Recognised? RecogniseModelRefusal(string? said, DateTimeOffset endedAt, TimeZoneInfo? runnerZone = null) => null;
        public AuthFailure.Recognised? RecogniseAuthFailure(string? said) => null;
    }

    [Fact]
    public void The_unsigned_override_reaches_a_headless_and_an_attached_session()
    {
        var request = Request() with { Unsigned = true };

        ReachTests.AssertUnsigned(Adapter().BuildHeadless(request).Environment);
        ReachTests.AssertUnsigned(Adapter().BuildAttached(request).Environment);
    }
}

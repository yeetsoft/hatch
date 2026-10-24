namespace Hatch.Cli.Tests;

/// <summary>
/// The three layers, and the two things about them that would be silent
/// failures: which one wins, and that a missing key is a mode rather than a
/// refusal.
/// </summary>
public sealed class SettingsTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("hatch-settings-").FullName;

    private string Write(string name, params string[] lines)
    {
        var path = Path.Combine(_temp, name);
        File.WriteAllLines(path, lines);
        return path;
    }

    private static Dictionary<string, string?> Env(params (string Name, string? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);

    // ---- precedence ----

    /// <summary>
    /// What makes a one-off origin a prefix on a command line rather than an
    /// edit to a file.
    /// </summary>
    [Fact]
    public void An_exported_value_beats_both_files()
    {
        var checkout = Write("checkout.env", "HATCH_BASE=https://from-the-checkout");
        var user = Write("user.config", "HATCH_BASE=https://from-the-user-file");

        Assert.True(Settings.TryLoad(
            checkout, Env(("HATCH_BASE", "https://exported")), out var settings, out _, user));

        Assert.Equal("https://exported", settings.Base);
        Assert.Equal(Settings.Layer.Environment, settings.SourceOf("HATCH_BASE"));
    }

    /// <summary>
    /// A repository that pins its own origin keeps it, which is what the
    /// checkout layer is for.
    /// </summary>
    [Fact]
    public void This_checkouts_env_beats_the_per_user_file()
    {
        var checkout = Write("checkout.env", "HATCH_BASE=https://from-the-checkout");
        var user = Write("user.config", "HATCH_BASE=https://from-the-user-file");

        Assert.True(Settings.TryLoad(checkout, Env(), out var settings, out _, user));

        Assert.Equal("https://from-the-checkout", settings.Base);
        Assert.Equal(Settings.Layer.Checkout, settings.SourceOf("HATCH_BASE"));
    }

    /// <summary>
    /// The whole reason the per-user file exists: `hatch board` from a directory
    /// that has never been a repository still knows where Hatch is.
    /// </summary>
    [Fact]
    public void The_per_user_file_answers_when_there_is_no_checkout_at_all()
    {
        var user = Write("user.config", "HATCH_BASE=https://from-the-user-file", "HATCH_KEY=hatch_ak_mine");

        Assert.True(Settings.TryLoad(null, Env(), out var settings, out _, user));

        Assert.Equal("https://from-the-user-file", settings.Base);
        Assert.Equal("hatch_ak_mine", settings.Key);
        Assert.Equal(Settings.Layer.User, settings.SourceOf("HATCH_KEY"));
    }

    /// <summary>
    /// Layered a value at a time, not a file at a time - so a checkout may pin
    /// the origin while the key still comes from the person.
    /// </summary>
    [Fact]
    public void Each_value_is_layered_on_its_own()
    {
        var checkout = Write("checkout.env", "HATCH_BASE=https://this-repository", "HATCH_KEY=hatch_ak_here");
        var user = Write("user.config", "HATCH_BASE=https://mine", "HATCH_KEY=hatch_ak_mine");

        Assert.True(Settings.TryLoad(
            checkout, Env(("HATCH_CLAUDE_BIN", "/opt/claude")), out var settings, out _, user));

        Assert.Equal("https://this-repository", settings.Base);
        Assert.Equal("hatch_ak_here", settings.Key);
        Assert.Equal("/opt/claude", settings.ClaudeBin);

        Assert.Equal(Settings.Layer.Checkout, settings.SourceOf("HATCH_BASE"));
        Assert.Equal(Settings.Layer.Checkout, settings.SourceOf("HATCH_KEY"));
        Assert.Equal(Settings.Layer.Environment, settings.SourceOf("HATCH_CLAUDE_BIN"));
    }

    /// <summary>The same three layers <c>HATCH_CLAUDE_BIN</c> reads through, for the other known agent.</summary>
    [Fact]
    public void Grok_bin_reads_from_the_same_three_layers()
    {
        var checkout = Write("checkout.env", "HATCH_BASE=https://this-repository");
        var user = Write("user.config", "HATCH_BASE=https://mine", "HATCH_KEY=hatch_ak_mine");

        Assert.True(Settings.TryLoad(
            checkout, Env(("HATCH_GROK_BIN", "/opt/grok")), out var settings, out _, user));

        Assert.Equal("/opt/grok", settings.GrokBin);
        Assert.Equal(Settings.Layer.Environment, settings.SourceOf("HATCH_GROK_BIN"));
    }

    [Fact]
    public void Known_agent_ids_include_grok_and_codex()
    {
        Assert.Contains("grok", Settings.KnownAgentIds);
        Assert.Contains("codex", Settings.KnownAgentIds);
    }

    /// <summary>The same three layers the other agent bins read through, for the Ollama host.</summary>
    [Fact]
    public void Ollama_host_reads_from_HATCH_OLLAMA_HOST_through_the_three_layers()
    {
        var checkout = Write("checkout.env", "HATCH_BASE=https://this-repository");
        var user = Write("user.config", "HATCH_BASE=https://mine", "HATCH_KEY=hatch_ak_mine");

        Assert.True(Settings.TryLoad(
            checkout, Env(("HATCH_OLLAMA_HOST", "http://box:1234")), out var settings, out _, user));

        Assert.Equal("http://box:1234", settings.OllamaHost.Url);
        Assert.True(settings.OllamaHost.Explicit);
    }

    /// <summary>
    /// Ollama's own variable is read straight out of the process environment,
    /// never through the checkout or per-user file.
    /// </summary>
    [Fact]
    public void Ollama_host_falls_back_to_OLLAMA_HOST_when_HATCH_OLLAMA_HOST_is_unset()
    {
        var user = Write("user.config", "HATCH_BASE=https://mine");

        Assert.True(Settings.TryLoad(null, Env(("OLLAMA_HOST", "http://box:4321")), out var settings, out _, user));

        Assert.Equal("http://box:4321", settings.OllamaHost.Url);
        Assert.True(settings.OllamaHost.Explicit);
    }

    /// <summary>
    /// `OLLAMA_HOST` is not on the allowlist, so a settings file naming it is
    /// ignored the same way any other unknown name is - it never reaches
    /// <see cref="Settings.OllamaHost"/>.
    /// </summary>
    [Fact]
    public void A_checkout_file_naming_OLLAMA_HOST_is_ignored()
    {
        var checkout = Write("checkout.env", "HATCH_BASE=https://this-repository", "OLLAMA_HOST=http://box:9999");
        var user = Write("user.config", "HATCH_BASE=https://mine");

        Assert.True(Settings.TryLoad(checkout, Env(), out var settings, out _, user));

        Assert.Equal(OllamaHost.Default, settings.OllamaHost);
    }

    [Fact]
    public void Known_agent_ids_include_ollama()
    {
        Assert.Contains("ollama", Settings.KnownAgentIds);
    }

    [Fact]
    public void A_bad_HATCH_OLLAMA_TIERS_refuses_the_load()
    {
        var user = Write("user.config", "HATCH_BASE=https://mine");

        Assert.False(Settings.TryLoad(
            null, Env(("HATCH_OLLAMA_TIERS", "nope")), out _, out var refusal, user));

        Assert.Contains("HATCH_OLLAMA_TIERS", refusal);
        Assert.Contains(OllamaAdapter.ParseTiers("nope", out _)!, refusal);
    }

    [Fact]
    public void A_good_HATCH_OLLAMA_TIERS_parses_into_OllamaTiers()
    {
        var user = Write("user.config", "HATCH_BASE=https://mine");

        Assert.True(Settings.TryLoad(
            null, Env(("HATCH_OLLAMA_TIERS", "sonnet=qwen3-coder:30b")), out var settings, out _, user));

        Assert.Equal("qwen3-coder:30b", settings.OllamaTiers["sonnet"]);
    }

    [Fact]
    public void Codex_bin_is_read_from_the_environment()
    {
        var user = Write("user.config", "HATCH_BASE=https://mine");

        Assert.True(Settings.TryLoad(null, Env(("HATCH_CODEX_BIN", "/opt/codex")), out var settings, out _, user));

        Assert.Equal("/opt/codex", settings.CodexBin);
        Assert.Equal(Settings.Layer.Environment, settings.SourceOf("HATCH_CODEX_BIN"));
    }

    [Fact]
    public void Codex_bin_is_read_from_the_checkout_file()
    {
        var checkout = Write("checkout.env", "HATCH_BASE=https://mine", "HATCH_CODEX_BIN=/opt/codex");

        Assert.True(Settings.TryLoad(checkout, Env(), out var settings, out _, Path.Combine(_temp, "does-not-exist")));

        Assert.Equal("/opt/codex", settings.CodexBin);
        Assert.Equal(Settings.Layer.Checkout, settings.SourceOf("HATCH_CODEX_BIN"));
    }

    [Fact]
    public void Codex_bin_is_read_from_the_per_user_file()
    {
        var user = Write("user.config", "HATCH_BASE=https://mine", "HATCH_CODEX_BIN=/opt/codex");

        Assert.True(Settings.TryLoad(null, Env(), out var settings, out _, user));

        Assert.Equal("/opt/codex", settings.CodexBin);
        Assert.Equal(Settings.Layer.User, settings.SourceOf("HATCH_CODEX_BIN"));
    }

    [Fact]
    public void Codex_bin_is_null_and_unset_when_nothing_set_it()
    {
        var user = Write("user.config", "HATCH_BASE=https://mine");

        Assert.True(Settings.TryLoad(null, Env(), out var settings, out _, user));

        Assert.Null(settings.CodexBin);
        Assert.Equal(Settings.Layer.Unset, settings.SourceOf("HATCH_CODEX_BIN"));
    }

    /// <summary>An exported variable that is there but empty is not a value.</summary>
    [Fact]
    public void An_empty_exported_value_falls_through_to_the_file_below_it()
    {
        var user = Write("user.config", "HATCH_BASE=https://mine");

        Assert.True(Settings.TryLoad(null, Env(("HATCH_BASE", "")), out var settings, out _, user));

        Assert.Equal("https://mine", settings.Base);
        Assert.Equal(Settings.Layer.User, settings.SourceOf("HATCH_BASE"));
    }

    [Fact]
    public void Nothing_anywhere_is_unset_and_not_a_guess()
    {
        Assert.True(Settings.TryLoad(
            null, Env(("HATCH_BASE", "https://somewhere")), out var settings, out _,
            Path.Combine(_temp, "does-not-exist")));

        Assert.Equal(Settings.Layer.Unset, settings.SourceOf("HATCH_BASE_BRANCH"));
        Assert.Null(settings.BaseBranch);
    }

    // ---- HATCH_REPOS ----

    [Fact]
    public void HATCH_REPOS_splits_on_the_platforms_path_separator()
    {
        var user = Write("user.config",
            "HATCH_BASE=https://mine", $"HATCH_REPOS=/repos/one{Path.PathSeparator}/repos/two");

        Assert.True(Settings.TryLoad(null, Env(), out var settings, out _, user));

        Assert.Equal(["/repos/one", "/repos/two"], settings.Repos);
    }

    [Fact]
    public void HATCH_REPOS_unset_is_empty_and_not_null()
    {
        Assert.True(Settings.TryLoad(
            null, Env(("HATCH_BASE", "https://somewhere")), out var settings, out _,
            Path.Combine(_temp, "does-not-exist")));

        Assert.Empty(settings.Repos);
    }

    [Fact]
    public void An_exported_HATCH_REPOS_beats_the_checkout_file_which_beats_the_per_user_file()
    {
        var checkout = Write("checkout.env", "HATCH_BASE=https://mine", "HATCH_REPOS=/from-the-checkout");
        var user = Write("user.config", "HATCH_REPOS=/from-the-user-file");

        Assert.True(Settings.TryLoad(
            checkout, Env(("HATCH_BASE", "https://mine"), ("HATCH_REPOS", "/exported")),
            out var exported, out _, user));
        Assert.Equal(["/exported"], exported.Repos);
        Assert.Equal(Settings.Layer.Environment, exported.SourceOf("HATCH_REPOS"));

        Assert.True(Settings.TryLoad(checkout, Env(("HATCH_BASE", "https://mine")), out var checkoutWins, out _, user));
        Assert.Equal(["/from-the-checkout"], checkoutWins.Repos);
        Assert.Equal(Settings.Layer.Checkout, checkoutWins.SourceOf("HATCH_REPOS"));

        Assert.True(Settings.TryLoad(null, Env(("HATCH_BASE", "https://mine")), out var userWins, out _, user));
        Assert.Equal(["/from-the-user-file"], userWins.Repos);
        Assert.Equal(Settings.Layer.User, userWins.SourceOf("HATCH_REPOS"));
    }

    // ---- HATCH_TAGS ----

    [Fact]
    public void HATCH_TAGS_splits_on_commas_trims_and_drops_blanks()
    {
        var user = Write("user.config", "HATCH_BASE=https://mine", "HATCH_TAGS=macos, xcode");

        Assert.True(Settings.TryLoad(null, Env(), out var settings, out _, user));

        Assert.Equal(["macos", "xcode"], settings.Tags);
    }

    [Fact]
    public void HATCH_TAGS_lowercases_and_drops_blank_entries()
    {
        var user = Write("user.config", "HATCH_BASE=https://mine", "HATCH_TAGS=MacOS,,xcode, ");

        Assert.True(Settings.TryLoad(null, Env(), out var settings, out _, user));

        Assert.Equal(["macos", "xcode"], settings.Tags);
    }

    [Fact]
    public void HATCH_TAGS_deduplicates_case_insensitively()
    {
        var user = Write("user.config", "HATCH_BASE=https://mine", "HATCH_TAGS=gpu,GPU");

        Assert.True(Settings.TryLoad(null, Env(), out var settings, out _, user));

        Assert.Equal(["gpu"], settings.Tags);
    }

    [Fact]
    public void HATCH_TAGS_unset_is_empty_and_not_null()
    {
        Assert.True(Settings.TryLoad(
            null, Env(("HATCH_BASE", "https://somewhere")), out var settings, out _,
            Path.Combine(_temp, "does-not-exist")));

        Assert.Empty(settings.Tags);
        Assert.Equal(Settings.Layer.Unset, settings.SourceOf("HATCH_TAGS"));
    }

    [Fact]
    public void An_exported_HATCH_TAGS_beats_the_checkout_file_which_beats_the_per_user_file()
    {
        var checkout = Write("checkout.env", "HATCH_BASE=https://mine", "HATCH_TAGS=checkout-tag");
        var user = Write("user.config", "HATCH_TAGS=user-tag");

        Assert.True(Settings.TryLoad(
            checkout, Env(("HATCH_BASE", "https://mine"), ("HATCH_TAGS", "exported-tag")),
            out var exported, out _, user));
        Assert.Equal(["exported-tag"], exported.Tags);
        Assert.Equal(Settings.Layer.Environment, exported.SourceOf("HATCH_TAGS"));

        Assert.True(Settings.TryLoad(checkout, Env(("HATCH_BASE", "https://mine")), out var checkoutWins, out _, user));
        Assert.Equal(["checkout-tag"], checkoutWins.Tags);
        Assert.Equal(Settings.Layer.Checkout, checkoutWins.SourceOf("HATCH_TAGS"));

        Assert.True(Settings.TryLoad(null, Env(("HATCH_BASE", "https://mine")), out var userWins, out _, user));
        Assert.Equal(["user-tag"], userWins.Tags);
        Assert.Equal(Settings.Layer.User, userWins.SourceOf("HATCH_TAGS"));
    }

    [Fact]
    public void HATCH_TAGS_with_a_space_inside_a_tag_refuses_to_load()
    {
        Assert.False(Settings.TryLoad(
            null, Env(("HATCH_BASE", "https://mine"), ("HATCH_TAGS", "gpu big")), out _, out var refusal,
            Path.Combine(_temp, "does-not-exist")));

        Assert.Contains("no space or comma", refusal);
    }

    [Fact]
    public void HATCH_TAGS_with_a_tag_over_32_characters_refuses_to_load()
    {
        var tooLong = new string('a', 33);

        Assert.False(Settings.TryLoad(
            null, Env(("HATCH_BASE", "https://mine"), ("HATCH_TAGS", tooLong)), out _, out var refusal,
            Path.Combine(_temp, "does-not-exist")));

        Assert.Contains("at most 32", refusal);
    }

    [Fact]
    public void HATCH_TAGS_with_nine_tags_refuses_to_load()
    {
        var nine = string.Join(',', Enumerable.Range(1, 9).Select(n => $"tag{n}"));

        Assert.False(Settings.TryLoad(
            null, Env(("HATCH_BASE", "https://mine"), ("HATCH_TAGS", nine)), out _, out var refusal,
            Path.Combine(_temp, "does-not-exist")));

        Assert.Contains("at most 8", refusal);
    }

    // ---- HATCH_AGENT ----

    [Fact]
    public void HATCH_AGENT_is_read_from_the_environment()
    {
        Assert.True(Settings.TryLoad(
            null, Env(("HATCH_BASE", "https://mine"), ("HATCH_AGENT", "claude")), out var settings, out _,
            Path.Combine(_temp, "does-not-exist")));

        Assert.Equal("claude", settings.Agent);
        Assert.Equal(Settings.Layer.Environment, settings.SourceOf("HATCH_AGENT"));
    }

    [Fact]
    public void HATCH_AGENT_is_read_from_the_checkout_file()
    {
        var checkout = Write("checkout.env", "HATCH_BASE=https://mine", "HATCH_AGENT=claude");

        Assert.True(Settings.TryLoad(checkout, Env(), out var settings, out _, Path.Combine(_temp, "does-not-exist")));

        Assert.Equal("claude", settings.Agent);
        Assert.Equal(Settings.Layer.Checkout, settings.SourceOf("HATCH_AGENT"));
    }

    [Fact]
    public void HATCH_AGENT_is_read_from_the_per_user_file()
    {
        var user = Write("user.config", "HATCH_BASE=https://mine", "HATCH_AGENT=claude");

        Assert.True(Settings.TryLoad(null, Env(), out var settings, out _, user));

        Assert.Equal("claude", settings.Agent);
        Assert.Equal(Settings.Layer.User, settings.SourceOf("HATCH_AGENT"));
    }

    [Fact]
    public void HATCH_AGENT_defaults_to_claude_when_unset()
    {
        Assert.True(Settings.TryLoad(
            null, Env(("HATCH_BASE", "https://mine")), out var settings, out _,
            Path.Combine(_temp, "does-not-exist")));

        Assert.Equal("claude", settings.Agent);
        Assert.Equal(Settings.Layer.Unset, settings.SourceOf("HATCH_AGENT"));
    }

    // ---- HATCH_WORKSPACE ----

    [Fact]
    public void HATCH_WORKSPACE_unset_falls_back_to_the_per_user_default()
    {
        Assert.True(Settings.TryLoad(
            null, Env(("HATCH_BASE", "https://somewhere")), out var settings, out _,
            Path.Combine(_temp, "does-not-exist")));

        Assert.Equal(Settings.DefaultWorkspacePath(), settings.Workspace);
        Assert.True(settings.WorkspaceDefaulted);
        Assert.Equal(Settings.Layer.Unset, settings.SourceOf("HATCH_WORKSPACE"));
    }

    [Fact]
    public void The_default_workspace_sits_beside_the_per_user_config_and_names_no_one()
    {
        Assert.Equal(
            Path.Combine(Path.GetDirectoryName(Settings.UserConfigPath())!, "workspace"),
            Settings.DefaultWorkspacePath());
    }

    /// <summary>Regression guard: a null or absent profile keeps today's path, unchanged by HA-1484.</summary>
    [Fact]
    public void DefaultWorkspacePath_with_no_profile_is_todays_path()
    {
        Assert.Equal(
            Path.Combine(Path.GetDirectoryName(Settings.UserConfigPath())!, "workspace"),
            Settings.DefaultWorkspacePath());
        Assert.Equal(Settings.DefaultWorkspacePath(), Settings.DefaultWorkspacePath(null));
        Assert.Equal(Settings.DefaultWorkspacePath(), Settings.DefaultWorkspacePath(""));
    }

    [Fact]
    public void DefaultWorkspacePath_is_per_profile()
    {
        var a = Settings.DefaultWorkspacePath("a");
        var b = Settings.DefaultWorkspacePath("b");
        var none = Settings.DefaultWorkspacePath();
        var workspaces = Path.Combine(Path.GetDirectoryName(Settings.UserConfigPath())!, "workspaces");

        Assert.NotEqual(a, b);
        Assert.NotEqual(a, none);
        Assert.NotEqual(b, none);
        Assert.StartsWith(workspaces, a);
        Assert.StartsWith(workspaces, b);
    }

    [Fact]
    public void A_profile_with_no_workspace_set_gets_its_own_default()
    {
        var profile = Write("myprofile", "HATCH_BASE=https://from-the-profile");

        Assert.True(Settings.TryLoad(
            null, Env(), out var settings, out _, Path.Combine(_temp, "does-not-exist"), profile));

        Assert.Equal(Settings.DefaultWorkspacePath("myprofile"), settings.Workspace);
        Assert.True(settings.WorkspaceDefaulted);
    }

    [Theory]
    [InlineData("off")]
    [InlineData("OFF")]
    [InlineData(" Off ")]
    public void HATCH_WORKSPACE_off_turns_the_default_off_and_is_not_itself_a_directory(string word)
    {
        Assert.True(Settings.TryLoad(
            null, Env(("HATCH_BASE", "https://somewhere"), ("HATCH_WORKSPACE", word)), out var settings, out _,
            Path.Combine(_temp, "does-not-exist")));

        Assert.Null(settings.Workspace);
        Assert.False(settings.WorkspaceDefaulted);
        Assert.Equal(Settings.Layer.Environment, settings.SourceOf("HATCH_WORKSPACE"));
    }

    [Fact]
    public void A_workspace_flag_is_the_whole_answer_for_a_run_and_off_means_none()
    {
        var settings = new Settings { Base = "https://x", Key = "", Workspace = "/set", WorkspaceDefaulted = true };

        Assert.Equal(("/set", true), settings.WorkspaceFor(null));
        Assert.Equal(("/flag", false), settings.WorkspaceFor("/flag"));
        Assert.Equal((null, false), settings.WorkspaceFor("off"));
    }

    [Fact]
    public void WithWorkspace_null_is_never_defaulted()
    {
        var settings = new Settings { Base = "https://x", Key = "", Workspace = "/set", WorkspaceDefaulted = true };

        var gaveWay = settings.WithWorkspace(null, defaulted: true);

        Assert.Null(gaveWay.Workspace);
        Assert.False(gaveWay.WorkspaceDefaulted);
    }

    [Fact]
    public void A_workspace_that_is_set_is_not_defaulted()
    {
        Assert.True(Settings.TryLoad(
            null, Env(("HATCH_BASE", "https://somewhere"), ("HATCH_WORKSPACE", "/mine")), out var settings, out _,
            Path.Combine(_temp, "does-not-exist")));

        Assert.Equal("/mine", settings.Workspace);
        Assert.False(settings.WorkspaceDefaulted);
    }

    [Fact]
    public void An_exported_HATCH_WORKSPACE_beats_the_checkout_file_which_beats_the_per_user_file()
    {
        var checkout = Write("checkout.env", "HATCH_BASE=https://mine", "HATCH_WORKSPACE=/from-the-checkout");
        var user = Write("user.config", "HATCH_WORKSPACE=/from-the-user-file");

        Assert.True(Settings.TryLoad(
            checkout, Env(("HATCH_BASE", "https://mine"), ("HATCH_WORKSPACE", "/exported")),
            out var exported, out _, user));
        Assert.Equal("/exported", exported.Workspace);
        Assert.Equal(Settings.Layer.Environment, exported.SourceOf("HATCH_WORKSPACE"));

        Assert.True(Settings.TryLoad(checkout, Env(("HATCH_BASE", "https://mine")), out var checkoutWins, out _, user));
        Assert.Equal("/from-the-checkout", checkoutWins.Workspace);
        Assert.Equal(Settings.Layer.Checkout, checkoutWins.SourceOf("HATCH_WORKSPACE"));

        Assert.True(Settings.TryLoad(null, Env(("HATCH_BASE", "https://mine")), out var userWins, out _, user));
        Assert.Equal("/from-the-user-file", userWins.Workspace);
        Assert.Equal(Settings.Layer.User, userWins.SourceOf("HATCH_WORKSPACE"));
    }

    // ---- the key is optional ----

    /// <summary>
    /// A Hatch with its wall off has no credential to present, so a missing key
    /// is a mode and not a failure to load. A load that refused without one
    /// would make local mode unreachable from the command that configures it.
    /// </summary>
    [Fact]
    public void An_origin_with_no_key_loads_and_the_key_is_empty()
    {
        Assert.True(Settings.TryLoad(
            null, Env(("HATCH_BASE", "http://localhost:5227")), out var settings, out var refusal,
            Path.Combine(_temp, "does-not-exist")));

        Assert.Equal("", settings.Key);
        Assert.Equal("", refusal);
    }

    [Fact]
    public void No_origin_anywhere_is_the_one_refusal_and_it_names_config()
    {
        Assert.False(Settings.TryLoad(
            null, Env(("HATCH_KEY", "hatch_ak_x")), out _, out var refusal,
            Path.Combine(_temp, "does-not-exist")));

        Assert.Contains("not configured - it needs an origin", refusal);
        Assert.Contains("hatch config", refusal);
        Assert.DoesNotContain("./scripts/hatch.sh", refusal);
    }

    // ---- the file itself ----

    [Fact]
    public void A_trailing_slash_on_the_origin_is_dropped_so_no_path_ever_doubles_up()
    {
        Assert.True(Settings.TryLoad(
            null, Env(("HATCH_BASE", "https://hatch.example/")), out var settings, out _,
            Path.Combine(_temp, "none")));

        Assert.Equal("https://hatch.example", settings.Base);
    }

    [Fact]
    public void One_layer_of_surrounding_quotes_is_stripped()
    {
        var user = Write("user.config", """HATCH_BASE="https://quoted" """.TrimEnd(), "HATCH_KEY='hatch_ak_q'");

        Assert.True(Settings.TryLoad(null, Env(), out var settings, out _, user));

        Assert.Equal("https://quoted", settings.Base);
        Assert.Equal("hatch_ak_q", settings.Key);
    }

    [Fact]
    public void Blank_lines_and_comments_are_skipped()
    {
        var user = Write("user.config", "# written by config", "", "HATCH_BASE=https://mine", "   ");

        Assert.True(Settings.TryLoad(null, Env(), out var settings, out _, user));
        Assert.Equal("https://mine", settings.Base);
    }

    /// <summary>
    /// Parsed against an allowlist rather than sourced, because a credential
    /// file that is also a program is a larger promise than "a few values".
    /// </summary>
    [Fact]
    public void A_name_that_is_not_on_the_allowlist_is_ignored()
    {
        var user = Write("user.config", "HATCH_BASE=https://mine", "PATH=/tmp/evil");

        Assert.True(Settings.TryLoad(null, Env(), out var settings, out _, user));

        Assert.Equal("https://mine", settings.Base);
        Assert.DoesNotContain("PATH", settings.Sources.Keys);
    }

    /// <summary>
    /// RunnerNames.Record has its own reader, keyed on a checkout path rather
    /// than on one of these names - so the runners file next to `config` is
    /// never read through here, and a checkout path on the left of `=` never
    /// trips the allowlist warning below.
    /// </summary>
    [Fact]
    public void A_runners_record_line_is_not_shaped_like_a_config_line()
    {
        var runners = Write("runners", "/Users/x/code/hatch=Jeff Winger");

        var values = Settings.ReadFile(runners);

        // Read as config, the left of "=" is the path - not one of
        // Settings.FileNames - so the line is dropped rather than parsed.
        Assert.Empty(values);
    }

    [Fact]
    public void The_runners_file_is_beside_config_and_not_the_same_path()
    {
        Assert.NotEqual(RunnerNames.Record.DefaultPath(), Settings.UserConfigPath());
        Assert.Equal(
            Path.GetDirectoryName(Settings.UserConfigPath()), Path.GetDirectoryName(RunnerNames.Record.DefaultPath()));
        Assert.Equal("runners", Path.GetFileName(RunnerNames.Record.DefaultPath()));
    }

    [Fact]
    public void The_heartbeat_defaults_to_twenty_and_takes_a_number_and_refuses_a_negative_one()
    {
        var origin = Env(("HATCH_BASE", "https://mine"));
        var none = Path.Combine(_temp, "none");

        Assert.True(Settings.TryLoad(null, origin, out var byDefault, out _, none));
        Assert.Equal(20, byDefault.HeartbeatSeconds);

        Assert.True(Settings.TryLoad(
            null, Env(("HATCH_BASE", "https://mine"), ("HATCH_HEARTBEAT", "0")), out var off, out _, none));
        Assert.Equal(0, off.HeartbeatSeconds);

        Assert.True(Settings.TryLoad(
            null, Env(("HATCH_BASE", "https://mine"), ("HATCH_HEARTBEAT", "-5")), out var bad, out _, none));
        Assert.Equal(20, bad.HeartbeatSeconds);
    }

    /// <summary>
    /// Read and layered exactly as <c>HATCH_HEARTBEAT</c> is - environment-only,
    /// same default-on-nonsense rule, and <c>0</c> is a valid window rather than
    /// nonsense: it means exactly one attempt, not "no window at all".
    /// </summary>
    [Fact]
    public void The_retry_window_defaults_to_ninety_and_takes_a_number_and_refuses_a_negative_one()
    {
        var origin = Env(("HATCH_BASE", "https://mine"));
        var none = Path.Combine(_temp, "none");

        Assert.True(Settings.TryLoad(null, origin, out var byDefault, out _, none));
        Assert.Equal(90, byDefault.RetrySeconds);

        Assert.True(Settings.TryLoad(
            null, Env(("HATCH_BASE", "https://mine"), ("HATCH_RETRY_SECONDS", "30")), out var overridden, out _, none));
        Assert.Equal(30, overridden.RetrySeconds);

        Assert.True(Settings.TryLoad(
            null, Env(("HATCH_BASE", "https://mine"), ("HATCH_RETRY_SECONDS", "0")), out var off, out _, none));
        Assert.Equal(0, off.RetrySeconds);

        Assert.True(Settings.TryLoad(
            null, Env(("HATCH_BASE", "https://mine"), ("HATCH_RETRY_SECONDS", "-5")), out var bad, out _, none));
        Assert.Equal(90, bad.RetrySeconds);
    }

    /// <summary>
    /// Not settable from a file - the same reason <c>HATCH_HEARTBEAT</c> is not:
    /// a line naming it in <c>scripts/.env</c> or the per-user file is an
    /// unrecognised name and is skipped, loudly, rather than read.
    /// </summary>
    [Fact]
    public void HATCH_RETRY_SECONDS_is_not_one_of_the_names_a_file_may_set()
    {
        Assert.DoesNotContain("HATCH_RETRY_SECONDS", Settings.FileNames);
    }

    /// <summary>
    /// A settings file that follows the person rather than the checkout is the
    /// whole point, so it must not be inside one.
    /// </summary>
    [Fact]
    public void The_per_user_path_is_under_the_platforms_application_data_and_is_called_config()
    {
        var path = Settings.UserConfigPath();

        Assert.Equal("config", Path.GetFileName(path));
        Assert.Equal("hatch", Path.GetFileName(Path.GetDirectoryName(path)));
        Assert.StartsWith(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            path);
    }

    // ---- profiles ----

    /// <summary>
    /// The new fourth layer, between the checkout file and the per-user file -
    /// matching HA-1448's decision 2.
    /// </summary>
    [Fact]
    public void A_profile_value_beats_the_per_user_file()
    {
        var profile = Write("profile.config", "HATCH_BASE=https://from-the-profile");
        var user = Write("user.config", "HATCH_BASE=https://from-the-user-file");

        Assert.True(Settings.TryLoad(null, Env(), out var settings, out _, user, profile));

        Assert.Equal("https://from-the-profile", settings.Base);
        Assert.Equal(Settings.Layer.Profile, settings.SourceOf("HATCH_BASE"));
    }

    [Fact]
    public void The_checkouts_env_beats_a_profile_value_and_an_export_beats_both()
    {
        var checkout = Write("checkout.env", "HATCH_BASE=https://from-the-checkout");
        var profile = Write("profile.config", "HATCH_BASE=https://from-the-profile");
        var user = Write("user.config", "HATCH_BASE=https://from-the-user-file");

        Assert.True(Settings.TryLoad(
            checkout, Env(("HATCH_BASE", "https://exported")), out var exported, out _, user, profile));
        Assert.Equal("https://exported", exported.Base);
        Assert.Equal(Settings.Layer.Environment, exported.SourceOf("HATCH_BASE"));

        Assert.True(Settings.TryLoad(checkout, Env(), out var checkoutWins, out _, user, profile));
        Assert.Equal("https://from-the-checkout", checkoutWins.Base);
        Assert.Equal(Settings.Layer.Checkout, checkoutWins.SourceOf("HATCH_BASE"));
    }

    /// <summary>
    /// Layered a value at a time, not a file at a time - the same guarantee
    /// <see cref="Each_value_is_layered_on_its_own"/> makes for the other three.
    /// </summary>
    [Fact]
    public void Each_value_is_layered_on_its_own_across_profile_and_user()
    {
        var profile = Write("profile.config", "HATCH_BASE=https://from-the-profile");
        var user = Write("user.config", "HATCH_BASE=https://mine", "HATCH_KEY=hatch_ak_mine");

        Assert.True(Settings.TryLoad(null, Env(), out var settings, out _, user, profile));

        Assert.Equal("https://from-the-profile", settings.Base);
        Assert.Equal("", settings.Key);
        Assert.NotNull(settings.KeyNote);

        Assert.Equal(Settings.Layer.Profile, settings.SourceOf("HATCH_BASE"));
        Assert.Equal(Settings.Layer.Unset, settings.SourceOf("HATCH_KEY"));
    }

    [Fact]
    public void A_profile_skips_every_board_bound_name_in_the_per_user_file_but_not_the_machine_bound_ones()
    {
        var user = Write("user.config",
            "HATCH_BASE=https://mine", "HATCH_KEY=hatch_ak_mine", "HATCH_RUNNER=mine",
            "HATCH_TAGS=gpu", "HATCH_WORKSPACE=/mine/workspace", "HATCH_REPOS=/mine/repo",
            "HATCH_CLAUDE_BIN=/mine/claude", "HATCH_AGENT=codex");
        var profile = Write("profile.config", "HATCH_BASE=https://from-the-profile");

        Assert.True(Settings.TryLoad(null, Env(), out var settings, out _, user, profile));

        Assert.Null(settings.Runner);
        Assert.Empty(settings.Tags);
        Assert.Empty(settings.Repos);
        Assert.True(settings.WorkspaceDefaulted);
        Assert.Equal("", settings.Key);
        Assert.Equal("/mine/claude", settings.ClaudeBin);
        Assert.Equal("codex", settings.Agent);
    }

    /// <summary>
    /// Everything but <c>HATCH_RUNNER</c> still reads from the per-user file
    /// with no profile active, exactly as before HA-1484 - a name belongs to a
    /// checkout, never a person, with or without a profile.
    /// </summary>
    [Fact]
    public void With_no_profile_the_same_user_file_reads_every_value_as_today_except_HATCH_RUNNER()
    {
        var user = Write("user.config", "HATCH_BASE=https://mine", "HATCH_RUNNER=mine", "HATCH_TAGS=gpu");

        Assert.True(Settings.TryLoad(null, Env(), out var settings, out _, user));

        Assert.Null(settings.Runner);
        Assert.Contains($"HATCH_RUNNER in {user} is ignored", settings.RunnerNote);
        Assert.Contains("gpu", settings.Tags);
    }

    [Fact]
    public void A_named_profile_with_no_file_behind_it_still_skips_the_user_file_for_board_bound_names()
    {
        var missingProfile = Path.Combine(_temp, "no-such-profile.config"); // never written
        var user = Write("user.config", "HATCH_RUNNER=mine", "HATCH_CLAUDE_BIN=/mine/claude");

        Assert.True(Settings.TryLoad(
            null, Env(("HATCH_BASE", "https://exported")), out var settings, out _, user, missingProfile));

        Assert.Null(settings.Runner);
        Assert.Equal("/mine/claude", settings.ClaudeBin);
    }

    [Fact]
    public void The_exported_layer_and_the_checkouts_env_still_supply_board_bound_names_under_a_profile()
    {
        var checkout = Write("checkout.env", "HATCH_WORKSPACE=/checkout/workspace");
        var profile = Write("profile.config", "HATCH_BASE=https://from-the-profile");
        var user = Write("user.config", "HATCH_RUNNER=mine");

        Assert.True(Settings.TryLoad(
            checkout, Env(("HATCH_TAGS", "gpu")), out var settings, out _, user, profile));

        Assert.Equal("/checkout/workspace", settings.Workspace);
        Assert.False(settings.WorkspaceDefaulted);
        Assert.Contains("gpu", settings.Tags);
        Assert.Null(settings.Runner);
    }

    // ---- the key goes with its origin ----

    [Fact]
    public void An_exported_key_over_an_origin_from_a_file_is_ignored_and_the_note_names_the_environment()
    {
        var checkout = Write("checkout.env", "HATCH_BASE=https://this-repository");

        Assert.True(Settings.TryLoad(
            checkout, Env(("HATCH_KEY", "hatch_ak_exported")), out var settings, out _, Write("user.config")));

        Assert.Equal("", settings.Key);
        Assert.Equal(Settings.Layer.Unset, settings.SourceOf("HATCH_KEY"));
        Assert.NotNull(settings.KeyNote);
        Assert.Contains("HATCH_KEY in the environment is ignored", settings.KeyNote);
        Assert.Contains($"HATCH_BASE comes from {checkout}", settings.KeyNote);
        Assert.EndsWith("Export both, or neither.", settings.KeyNote);
    }

    [Fact]
    public void An_exported_origin_and_an_exported_key_pair_and_say_nothing()
    {
        var user = Write("user.config", "HATCH_BASE=https://mine");

        Assert.True(Settings.TryLoad(
            null, Env(("HATCH_BASE", "https://exported"), ("HATCH_KEY", "hatch_ak_exported")),
            out var settings, out _, user));

        Assert.Equal("hatch_ak_exported", settings.Key);
        Assert.Equal(Settings.Layer.Environment, settings.SourceOf("HATCH_KEY"));
        Assert.Null(settings.KeyNote);
    }

    [Fact]
    public void A_checkout_origin_and_key_beat_a_per_user_key_and_the_user_key_is_noted()
    {
        var checkout = Write("checkout.env", "HATCH_BASE=https://this-repository", "HATCH_KEY=hatch_ak_here");
        var user = Write("user.config", "HATCH_KEY=hatch_ak_mine");

        Assert.True(Settings.TryLoad(checkout, Env(), out var settings, out _, user));

        Assert.Equal("hatch_ak_here", settings.Key);
        Assert.Equal(Settings.Layer.Checkout, settings.SourceOf("HATCH_KEY"));
        Assert.Contains($"HATCH_KEY in {user} is ignored", settings.KeyNote);
        Assert.EndsWith($"Set both in {user}, or neither.", settings.KeyNote);
    }

    [Fact]
    public void With_no_profile_an_origin_and_key_in_the_user_file_load_as_before()
    {
        var user = Write("user.config", "HATCH_BASE=https://mine", "HATCH_KEY=hatch_ak_mine");

        Assert.True(Settings.TryLoad(null, Env(), out var settings, out _, user));

        Assert.Equal("hatch_ak_mine", settings.Key);
        Assert.Equal(Settings.Layer.User, settings.SourceOf("HATCH_KEY"));
        Assert.Null(settings.KeyNote);
    }

    [Fact]
    public void A_key_alone_in_the_checkout_is_ignored_when_the_origin_comes_from_the_user_file()
    {
        var checkout = Write("checkout.env", "HATCH_KEY=hatch_ak_here");
        var user = Write("user.config", "HATCH_BASE=https://mine");

        Assert.True(Settings.TryLoad(checkout, Env(), out var settings, out _, user));

        Assert.Equal("", settings.Key);
        Assert.Contains($"HATCH_KEY in {checkout} is ignored", settings.KeyNote);
        Assert.Contains($"HATCH_BASE comes from {user}", settings.KeyNote);
    }

    [Fact]
    public void A_profile_origin_with_no_key_does_not_inherit_the_per_user_key()
    {
        var profile = Write("profile.config", "HATCH_BASE=https://from-the-profile");
        var user = Write("user.config", "HATCH_KEY=hatch_ak_mine");

        Assert.True(Settings.TryLoad(null, Env(), out var settings, out _, user, profile));

        Assert.Equal("", settings.Key);
        Assert.Contains($"HATCH_KEY in {user} is ignored", settings.KeyNote);
    }

    /// <summary>
    /// Neither candidate has a file behind it on this machine, so the call
    /// refuses either way - what this proves is which name it tried, which is
    /// what precedence actually means here.
    /// </summary>
    [Fact]
    public void TryResolveProfile_an_explicit_name_wins_over_HATCH_PROFILE()
    {
        Assert.False(Settings.TryResolveProfile(
            "explicit", Env(("HATCH_PROFILE", "from-env")), out _, out _, out var refusal));

        Assert.Contains("\"explicit\"", refusal);
    }

    [Fact]
    public void TryResolveProfile_HATCH_PROFILE_wins_over_the_active_pointer_when_no_name_is_given()
    {
        Assert.False(Settings.TryResolveProfile(
            null, Env(("HATCH_PROFILE", "from-env")), out _, out _, out var refusal));

        Assert.Contains("\"from-env\"", refusal);
    }

    [Fact]
    public void TryResolveProfile_an_unknown_name_refuses_and_names_ProfilesDirectory()
    {
        Assert.False(Settings.TryResolveProfile(
            "no-such-profile", Env(), out var name, out var file, out var refusal));

        // Named even on refusal - config --show's "active but missing" line
        // (HA-1464) needs the candidate, not just that resolution failed.
        Assert.Equal("no-such-profile", name);
        Assert.Null(file);
        Assert.Contains("no such profile", refusal);
        Assert.Contains(Settings.ProfilesDirectory(), refusal);
    }

    [Fact]
    public void TryResolveProfile_nothing_named_anywhere_resolves_to_null_and_leaves_todays_fold_unchanged()
    {
        Assert.True(Settings.TryResolveProfile(null, Env(), out var name, out var file, out var refusal));

        Assert.Null(name);
        Assert.Null(file);
        Assert.Equal("", refusal);
    }

    // ---- a runner name belongs to a checkout ----

    [Fact]
    public void HATCH_RUNNER_in_the_per_user_file_is_ignored_with_a_note()
    {
        var user = Write("user.config", "HATCH_RUNNER=from-the-user-file");

        Assert.True(Settings.TryLoad(null, Env(("HATCH_BASE", "https://somewhere")), out var settings, out _, user));

        Assert.Null(settings.Runner);
        Assert.Contains($"HATCH_RUNNER in {user} is ignored", settings.RunnerNote);
    }

    [Fact]
    public void HATCH_RUNNER_in_a_profile_is_ignored_with_a_note()
    {
        var profile = Write("profile.config", "HATCH_BASE=https://mine", "HATCH_RUNNER=from-the-profile");
        var user = Path.Combine(_temp, "does-not-exist");

        Assert.True(Settings.TryLoad(null, Env(), out var settings, out _, user, profile));

        Assert.Null(settings.Runner);
        Assert.Contains($"HATCH_RUNNER in {profile} is ignored", settings.RunnerNote);
    }

    [Fact]
    public void An_exported_HATCH_RUNNER_still_wins_over_a_profile()
    {
        var profile = Write("profile.config", "HATCH_BASE=https://from-the-profile", "HATCH_RUNNER=from-the-profile");
        var user = Path.Combine(_temp, "does-not-exist");

        Assert.True(Settings.TryLoad(
            null, Env(("HATCH_RUNNER", "from-the-environment")), out var settings, out _, user, profile));

        Assert.Equal("from-the-environment", settings.Runner);
        Assert.Null(settings.RunnerNote);
    }

    [Fact]
    public void A_checkouts_own_scripts_env_HATCH_RUNNER_still_wins()
    {
        var checkout = Write("checkout.env", "HATCH_BASE=https://from-the-checkout", "HATCH_RUNNER=from-the-checkout");
        var profile = Write("profile.config", "HATCH_RUNNER=from-the-profile");
        var user = Path.Combine(_temp, "does-not-exist");

        Assert.True(Settings.TryLoad(checkout, Env(), out var settings, out _, user, profile));

        Assert.Equal("from-the-checkout", settings.Runner);
        Assert.Null(settings.RunnerNote);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_temp, recursive: true);
        }
        catch (IOException)
        {
            // A temporary directory that will not go is not a failing test.
        }
    }
}

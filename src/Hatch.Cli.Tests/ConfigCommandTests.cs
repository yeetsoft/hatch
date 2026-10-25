namespace Hatch.Cli.Tests;

/// <summary>
/// <c>config</c> - the one command that has to work before anything is
/// configured, and the only one that writes a settings file.
/// </summary>
public sealed class ConfigCommandTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("hatch-config-").FullName;
    private readonly Transcript _say = new();

    private string ConfigPath => Path.Combine(_temp, "hatch", "config");

    private string RunnersPath => Path.Combine(_temp, "hatch", "runners");

    private ConfigCommand Command(
        Replies input,
        IDictionary<string, string?>? environment = null,
        string? checkoutEnv = null,
        Func<Settings, string, CancellationToken, Task<string?>>? probe = null,
        string? root = null,
        string runnerName = "test:/checkout",
        Func<Settings, CancellationToken, Task<IReadOnlyList<string>>>? liveRunners = null,
        Func<string, bool>? checkoutIgnoresEnv = null,
        Func<Settings, string, CancellationToken, Task<MeDto?>>? meProbe = null,
        string? profileName = null,
        string? profilePath = null,
        ConfigCommand.ProfileResolver? resolveProfile = null) =>
        new(_say, input, environment ?? new Dictionary<string, string?>(StringComparer.Ordinal),
            checkoutEnv, runnerName, root)
        {
            ConfigPath = ConfigPath,
            RunnersPath = RunnersPath,
            Probe = probe ?? ((_, _, _) => Task.FromResult<string?>("To Do, In Progress, Done")),
            LiveRunners = liveRunners ?? ((_, _) => Task.FromResult<IReadOnlyList<string>>([])),
            CheckoutIgnoresEnv = checkoutIgnoresEnv ?? (_ => true),
            MeProbe = meProbe ?? ((_, _, _) => Task.FromResult<MeDto?>(new MeDto("key", "stub", null, true, false, false))),
            ProfileName = profileName,
            ProfilePath = profilePath,
            // No active profile unless a test says otherwise: the real one reads
            // the machine's own pointer, which no test may depend on.
            ResolveProfile = resolveProfile ?? NoActiveProfile,
        };

    private static bool NoActiveProfile(
        string? requested, IDictionary<string, string?> environment,
        out string? name, out string? file, out string refusal)
    {
        name = null;
        file = null;
        refusal = "";
        return true;
    }

    private string Said => string.Join("\n", _say.Said);

    private string Complained => string.Join("\n", _say.Complained);

    private string WriteCheckoutEnv(params string[] lines)
    {
        var path = Path.Combine(_temp, "scripts.env");
        File.WriteAllLines(path, lines);
        return path;
    }

    /// <summary>
    /// A checkout root and its own <c>scripts/.env</c> - unlike
    /// <see cref="WriteCheckoutEnv"/>, which puts the checkout file somewhere
    /// unrelated to any <c>root</c>, this is the shape <c>--here</c> needs: a
    /// root and the file it writes under it, the same path <c>Program.cs</c>
    /// computes from one.
    /// </summary>
    private (string Root, string CheckoutEnv) HereCheckout(params string[] lines)
    {
        var root = Directory.CreateTempSubdirectory("hatch-config-here-").FullName;
        var scripts = Path.Combine(root, "scripts");
        Directory.CreateDirectory(scripts);
        var checkoutEnv = Path.Combine(scripts, ".env");
        if (lines.Length > 0) File.WriteAllLines(checkoutEnv, lines);
        return (root, checkoutEnv);
    }

    /// <summary>
    /// A named profile's own file, under <c>_temp</c> rather than the
    /// machine's own profiles directory - the path a test passes as
    /// <c>profilePath</c>, the same shape <see cref="Settings.ProfilePath"/>
    /// would hand <c>Program.cs</c> for a resolved <c>--profile</c>.
    /// </summary>
    private string Profile(params string[] lines)
    {
        var path = Path.Combine(_temp, "profiles", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (lines.Length > 0) File.WriteAllLines(path, lines);
        return path;
    }

    // ---- writing ----

    [Fact]
    public async Task It_asks_for_the_origin_the_key_and_the_cli_and_writes_all_three()
    {
        var input = new Replies(
            "https://hatch.example", "hatch_ak_written", "claude", "/opt/claude");

        Assert.Equal(0, await Command(input).RunAsync([], default));

        var written = File.ReadAllLines(ConfigPath);
        Assert.Contains("HATCH_BASE=https://hatch.example", written);
        Assert.Contains("HATCH_KEY=hatch_ak_written", written);
        Assert.Contains("HATCH_CLAUDE_BIN=/opt/claude", written);
        Assert.Contains("HATCH_AGENT=claude", written);
        Assert.DoesNotContain(written, l => l.StartsWith("HATCH_CODEX_BIN=", StringComparison.Ordinal));
        Assert.DoesNotContain(written, l => l.StartsWith("HATCH_GROK_BIN=", StringComparison.Ordinal));

        Assert.Contains("Hatch origin", input.Asked[0]);
        Assert.Contains("API key", input.Asked[1]);
        Assert.Contains("agent", input.Asked[2]);
        Assert.Contains("claude CLI path", input.Asked[3]);
    }

    /// <summary>Only the chosen agent's binary is asked for, and the prompt names it.</summary>
    [Fact]
    public async Task Choosing_codex_asks_for_the_codex_CLI_and_writes_HATCH_CODEX_BIN_alone()
    {
        var input = new Replies("https://hatch.example", "hatch_ak_written", "codex", "/opt/codex");

        Assert.Equal(0, await Command(input).RunAsync([], default));

        var written = File.ReadAllLines(ConfigPath);
        Assert.Contains("HATCH_AGENT=codex", written);
        Assert.Contains("HATCH_CODEX_BIN=/opt/codex", written);
        Assert.DoesNotContain(written, l => l.StartsWith("HATCH_CLAUDE_BIN=", StringComparison.Ordinal));
        Assert.Contains("codex CLI path", input.Asked[3]);
        Assert.DoesNotContain(input.Asked, a => a.Contains("claude CLI path", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Choosing_grok_asks_for_the_grok_CLI_alone()
    {
        var input = new Replies("https://hatch.example", "hatch_ak_written", "grok", "/opt/grok");

        Assert.Equal(0, await Command(input).RunAsync([], default));

        Assert.Contains("HATCH_GROK_BIN=/opt/grok", File.ReadAllLines(ConfigPath));
        Assert.Contains("grok CLI path", input.Asked[3]);
    }

    [Fact]
    public async Task Choosing_ollama_asks_for_the_codex_CLI_then_the_Ollama_host()
    {
        var input = new Replies("https://hatch.example", "hatch_ak_written", "ollama", "/opt/codex", "http://box:11434");

        Assert.Equal(0, await Command(input).RunAsync([], default));

        var written = File.ReadAllLines(ConfigPath);
        Assert.Contains("HATCH_CODEX_BIN=/opt/codex", written);
        Assert.Contains("HATCH_OLLAMA_HOST=http://box:11434", written);
        Assert.Contains("codex CLI path", input.Asked[3]);
        Assert.Contains("Ollama host", input.Asked[4]);
    }

    [Fact]
    public async Task Choosing_an_agent_keeps_the_Ollama_host_already_written()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllLines(ConfigPath, ["HATCH_BASE=https://kept", "HATCH_OLLAMA_HOST=http://kept:11434"]);

        Assert.Equal(0, await Command(new Replies("", "", "codex", "/opt/codex")).RunAsync([], default));

        Assert.Contains("HATCH_OLLAMA_HOST=http://kept:11434", File.ReadAllLines(ConfigPath));
    }

    /// <summary>Switching agent is not a reason to forget the other one's path.</summary>
    [Fact]
    public async Task Choosing_an_agent_keeps_the_other_agents_binary_already_written()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllLines(ConfigPath, ["HATCH_BASE=https://kept", "HATCH_CLAUDE_BIN=/opt/claude"]);

        Assert.Equal(0, await Command(new Replies("", "", "codex", "/opt/codex")).RunAsync([], default));

        var written = File.ReadAllLines(ConfigPath);
        Assert.Contains("HATCH_CLAUDE_BIN=/opt/claude", written);
        Assert.Contains("HATCH_CODEX_BIN=/opt/codex", written);
    }

    /// <summary>
    /// The point of the story: it writes where the person is, not where the
    /// checkout is - and it no longer writes a repository's scripts/.env at all.
    /// </summary>
    [Fact]
    public async Task It_writes_the_per_user_file_and_never_the_checkouts_own()
    {
        var checkoutEnv = WriteCheckoutEnv("HATCH_BASE=https://this-repository");

        await Command(new Replies("https://elsewhere", "", ""), checkoutEnv: checkoutEnv).RunAsync([], default);

        Assert.True(File.Exists(ConfigPath));
        Assert.Equal(["HATCH_BASE=https://this-repository"], File.ReadAllLines(checkoutEnv));
    }

    /// <summary>
    /// Whatever is loaded already is the default, so changing one setting is not
    /// an excuse to retype the others.
    /// </summary>
    [Fact]
    public async Task Enter_alone_keeps_what_is_already_there()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllLines(ConfigPath, ["HATCH_BASE=https://kept", "HATCH_KEY=hatch_ak_alreadykept"]);

        var input = new Replies("", "", "");
        Assert.Equal(0, await Command(input).RunAsync([], default));

        var written = File.ReadAllLines(ConfigPath);
        Assert.Contains("HATCH_BASE=https://kept", written);
        Assert.Contains("HATCH_KEY=hatch_ak_alreadykept", written);

        // Shown back masked, and never whole.
        Assert.Contains("hatch_ak_alr...kept", input.Asked[1]);
        Assert.DoesNotContain("hatch_ak_alreadykept", input.Asked[1]);
    }

    /// <summary>
    /// Written even when empty, so the file records that the omission was
    /// deliberate rather than looking like a half-finished config.
    /// </summary>
    [Fact]
    public async Task An_empty_key_is_written_and_said_out_loud()
    {
        Assert.Equal(0, await Command(new Replies("https://hatch.example", "", "")).RunAsync([], default));

        Assert.Contains("HATCH_KEY=", File.ReadAllLines(ConfigPath));
        Assert.Contains("no key - calls will name themselves \"test:/checkout\"", Complained);
        Assert.Contains("only a Hatch with its wall off reads", Complained);
    }

    [Fact]
    public async Task A_key_with_the_wrong_prefix_is_a_warning_and_not_a_refusal()
    {
        Assert.Equal(0, await Command(new Replies("https://hatch.example", "hunter2", "")).RunAsync([], default));

        Assert.Contains("does not start with hatch_ak_", Complained);
        Assert.Contains("HATCH_KEY=hunter2", File.ReadAllLines(ConfigPath));
    }

    [Fact]
    public async Task An_origin_with_no_scheme_is_refused_and_nothing_is_written()
    {
        Assert.Equal(1, await Command(new Replies("hatch.example", "", "")).RunAsync([], default));

        Assert.Contains("has no scheme - every call will fail", Complained);
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public async Task No_origin_at_all_is_refused_and_nothing_is_written()
    {
        Assert.Equal(1, await Command(new Replies("", "", "")).RunAsync([], default));

        Assert.Contains("an origin is required", Complained);
        Assert.False(File.Exists(ConfigPath));
    }

    /// <summary>
    /// Mode 600, because the point of the file is that a credential need not be
    /// exported into every shell the day starts with.
    /// </summary>
    [Fact]
    public async Task The_file_is_readable_by_its_owner_and_by_nobody_else()
    {
        Skip.If(OperatingSystem.IsWindows(), "Windows has no unix file mode; %APPDATA% is already per-user.");

        await Command(new Replies("https://hatch.example", "hatch_ak_secret", "")).RunAsync([], default);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(ConfigPath));
    }

    /// <summary>
    /// Temp file then rename, so a half-written file is never read as a whole
    /// one - and so a second run leaves exactly one file behind.
    /// </summary>
    [Fact]
    public async Task Writing_it_twice_leaves_one_file_and_no_temporary_beside_it()
    {
        await Command(new Replies("https://first", "", "")).RunAsync([], default);
        await Command(new Replies("https://second", "", "")).RunAsync([], default);

        Assert.Equal(["config"], Directory.GetFiles(Path.GetDirectoryName(ConfigPath)!)
            .Select(Path.GetFileName).Order());
        Assert.Contains("HATCH_BASE=https://second", File.ReadAllLines(ConfigPath));
    }

    /// <summary>
    /// Written before it is proven, on purpose: a key that is refused is worth
    /// keeping on disk to fix, and the message says what to fix.
    /// </summary>
    [Fact]
    public async Task A_call_that_does_not_go_through_still_leaves_the_file_behind()
    {
        var code = await Command(
                new Replies("https://hatch.example", "hatch_ak_wrong", ""),
                probe: (_, _, _) => throw new HatchException("hatch: 401 - the key was not accepted."))
            .RunAsync([], default);

        Assert.Equal(1, code);
        Assert.True(File.Exists(ConfigPath));
        Assert.Contains("the file is written but that call did not go through", Complained);
    }

    [Fact]
    public async Task A_call_that_goes_through_names_the_columns_it_found()
    {
        Assert.Equal(0, await Command(new Replies("https://hatch.example", "hatch_ak_ok", "")).RunAsync([], default));

        Assert.Contains("reached https://hatch.example - columns: To Do, In Progress, Done", Said);
    }

    [Fact]
    public async Task Config_without_a_terminal_is_refused_before_it_asks_anything()
    {
        var input = new Replies { AtATerminal = false };

        Assert.Equal(1, await Command(input).RunAsync([], default));
        Assert.Contains("config asks questions and needs a terminal", Complained);
        Assert.Empty(input.Asked);
        Assert.False(File.Exists(ConfigPath));
    }

    // ---- the runner name prompt ----

    [Fact]
    public async Task Outside_a_checkout_the_runner_is_never_asked_about()
    {
        var input = new Replies("https://hatch.example", "", "");
        Assert.Equal(0, await Command(input).RunAsync([], default));

        Assert.DoesNotContain(input.Asked, l => l.Contains("board call this runner", StringComparison.Ordinal));
    }

    [Fact]
    public async Task In_a_checkout_it_offers_the_runner_already_named_and_Enter_keeps_it()
    {
        var root = Directory.CreateTempSubdirectory("hatch-config-root-").FullName;
        var input = new Replies("https://hatch.example", "", "", "", "");

        Assert.Equal(
            0, await Command(input, root: root, runnerName: "Jeff Winger").RunAsync([], default));

        Assert.Contains("What should the board call this runner? [Jeff Winger]:", input.Asked[4]);
        Assert.Empty(RunnerNames.Record.Read(RunnersPath));
    }

    [Fact]
    public async Task A_typed_name_is_recorded()
    {
        var root = Directory.CreateTempSubdirectory("hatch-config-root-").FullName;
        var input = new Replies("https://hatch.example", "", "", "", "Liz Lemon");

        Assert.Equal(
            0, await Command(input, root: root, runnerName: "Jeff Winger").RunAsync([], default));

        Assert.Equal("Liz Lemon", RunnerNames.Record.Read(RunnersPath)[Checkout.Canonical(root)]);
    }

    [Fact]
    public async Task An_empty_typed_name_is_refused_with_nothing_written()
    {
        var root = Directory.CreateTempSubdirectory("hatch-config-root-").FullName;
        var input = new Replies("https://hatch.example", "", "", "", new string(' ', 3));

        var code = await Command(input, root: root, runnerName: "Jeff Winger").RunAsync([], default);

        Assert.Equal(1, code);
        Assert.Contains("a name is required", Complained);
        Assert.False(File.Exists(ConfigPath));
        Assert.Empty(RunnerNames.Record.Read(RunnersPath));
    }

    [Fact]
    public async Task A_name_that_is_not_plain_ASCII_is_refused_with_nothing_written()
    {
        var root = Directory.CreateTempSubdirectory("hatch-config-root-").FullName;
        var input = new Replies("https://hatch.example", "", "", "", "Jeff Wïnger");

        var code = await Command(input, root: root, runnerName: "Jeff Winger").RunAsync([], default);

        Assert.Equal(1, code);
        Assert.Contains("a name is plain ASCII", Complained);
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public async Task A_name_over_the_limit_is_refused_with_nothing_written()
    {
        var root = Directory.CreateTempSubdirectory("hatch-config-root-").FullName;
        var input = new Replies(
            "https://hatch.example", "", "", "", new string('x', ClaimRequest.MaxRunnerLength + 1));

        var code = await Command(input, root: root, runnerName: "Jeff Winger").RunAsync([], default);

        Assert.Equal(1, code);
        Assert.Contains($"a name is at most {ClaimRequest.MaxRunnerLength} characters", Complained);
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public async Task A_name_held_by_a_live_runner_is_refused_with_nothing_written()
    {
        var root = Directory.CreateTempSubdirectory("hatch-config-root-").FullName;
        var input = new Replies("https://hatch.example", "hatch_ak_ok", "", "", "Liz Lemon");

        var code = await Command(
                input, root: root, runnerName: "Jeff Winger",
                liveRunners: (_, _) => Task.FromResult<IReadOnlyList<string>>(["Liz Lemon"]))
            .RunAsync([], default);

        Assert.Equal(1, code);
        Assert.Contains("Liz Lemon is already a live runner on this board", Complained);
        Assert.False(File.Exists(ConfigPath));
        Assert.Empty(RunnerNames.Record.Read(RunnersPath));
    }

    // ---- --runner ----

    [Fact]
    public async Task Runner_flag_writes_the_name_without_asking_anything()
    {
        var root = Directory.CreateTempSubdirectory("hatch-config-root-").FullName;

        Assert.Equal(0, await Command(new Replies(), root: root).RunAsync(["--runner", "Liz Lemon"], default));

        Assert.Equal("Liz Lemon", RunnerNames.Record.Read(RunnersPath)[Checkout.Canonical(root)]);
    }

    [Fact]
    public async Task Runner_with_profile_writes_that_profiles_record_entry()
    {
        var root = Directory.CreateTempSubdirectory("hatch-config-root-").FullName;
        var canonical = Checkout.Canonical(root);
        ConfigCommand.ProfileResolver neverResolves = (string? _, IDictionary<string, string?> _, out string? _, out string? _, out string _) =>
            throw new InvalidOperationException("an explicit --profile never resolves");

        Assert.Equal(0, await Command(new Replies(), root: root, profileName: "a", profilePath: Profile(), resolveProfile: neverResolves)
            .RunAsync(["--runner", "Toby"], default));
        Assert.Equal(0, await Command(new Replies(), root: root, profileName: "b", profilePath: Profile(), resolveProfile: neverResolves)
            .RunAsync(["--runner", "Troy"], default));

        var record = RunnerNames.Record.Read(RunnersPath);
        Assert.Equal("Toby", record[RunnerNames.Record.Key("a", canonical)]);
        Assert.Equal("Troy", record[RunnerNames.Record.Key("b", canonical)]);
        Assert.False(record.ContainsKey(canonical));
    }

    [Fact]
    public async Task Runner_with_no_profile_flag_uses_the_active_one()
    {
        var root = Directory.CreateTempSubdirectory("hatch-config-root-").FullName;
        var canonical = Checkout.Canonical(root);
        var profilePath = Profile();
        ConfigCommand.ProfileResolver active = (string? _, IDictionary<string, string?> _, out string? name, out string? file, out string refusal) =>
        {
            name = "a";
            file = profilePath;
            refusal = "";
            return true;
        };

        Assert.Equal(0, await Command(new Replies(), root: root, resolveProfile: active)
            .RunAsync(["--runner", "Troy"], default));

        var record = RunnerNames.Record.Read(RunnersPath);
        Assert.Equal("Troy", record[RunnerNames.Record.Key("a", canonical)]);
        Assert.False(record.ContainsKey(canonical));
    }

    [Fact]
    public async Task Runner_refuses_a_dangling_active_profile()
    {
        var root = Directory.CreateTempSubdirectory("hatch-config-root-").FullName;
        ConfigCommand.ProfileResolver dangling = (string? _, IDictionary<string, string?> _, out string? name, out string? file, out string refusal) =>
        {
            name = "ghost";
            file = null;
            refusal = "hatch: no such profile \"ghost\"";
            return false;
        };

        var code = await Command(new Replies(), root: root, resolveProfile: dangling)
            .RunAsync(["--runner", "Troy"], default);

        Assert.Equal(1, code);
        Assert.Contains("no such profile \"ghost\"", Complained);
        Assert.Empty(RunnerNames.Record.Read(RunnersPath));
    }

    [Fact]
    public async Task Runner_with_profile_asks_that_profiles_board()
    {
        var root = Directory.CreateTempSubdirectory("hatch-config-root-").FullName;
        string? asked = null;

        var code = await Command(
                new Replies(),
                root: root,
                profileName: "a",
                profilePath: Profile("HATCH_BASE=https://profile-board"),
                liveRunners: (settings, _) =>
                {
                    asked = settings.Base;
                    return Task.FromResult<IReadOnlyList<string>>([]);
                })
            .RunAsync(["--runner", "Troy"], default);

        Assert.Equal(0, code);
        Assert.Equal("https://profile-board", asked);
    }

    [Fact]
    public async Task Runner_flag_outside_a_checkout_is_refused()
    {
        var code = await Command(new Replies()).RunAsync(["--runner", "Liz Lemon"], default);

        Assert.Equal(1, code);
        Assert.Contains("--runner names this checkout - run it inside one", Complained);
    }

    [Fact]
    public async Task Runner_flag_held_by_a_live_runner_is_refused_with_nothing_written()
    {
        var root = Directory.CreateTempSubdirectory("hatch-config-root-").FullName;

        var code = await Command(
                new Replies(),
                environment: new Dictionary<string, string?>(StringComparer.Ordinal) { ["HATCH_BASE"] = "https://hatch.example" },
                root: root,
                liveRunners: (_, _) => Task.FromResult<IReadOnlyList<string>>(["Liz Lemon"]))
            .RunAsync(["--runner", "Liz Lemon"], default);

        Assert.Equal(1, code);
        Assert.Contains("Liz Lemon is already a live runner on this board", Complained);
        Assert.Empty(RunnerNames.Record.Read(RunnersPath));
    }

    [Fact]
    public async Task Runner_flag_with_this_checkouts_own_name_is_not_refused()
    {
        var root = Directory.CreateTempSubdirectory("hatch-config-root-").FullName;

        var code = await Command(
                new Replies(),
                environment: new Dictionary<string, string?>(StringComparer.Ordinal) { ["HATCH_BASE"] = "https://hatch.example" },
                root: root,
                runnerName: "Liz Lemon",
                liveRunners: (_, _) => Task.FromResult<IReadOnlyList<string>>(["Liz Lemon"]))
            .RunAsync(["--runner", "Liz Lemon"], default);

        Assert.Equal(0, code);
        Assert.Equal("Liz Lemon", RunnerNames.Record.Read(RunnersPath)[Checkout.Canonical(root)]);
    }

    // ---- --origin ----

    /// <summary>
    /// The point of the mode: what Hatch's own Runner page prints, pasted on a
    /// machine that has just downloaded the binary.
    /// </summary>
    [Fact]
    public async Task Origin_writes_the_origin_without_asking_anything()
    {
        var input = new Replies();

        Assert.Equal(0, await Command(input).RunAsync(["--origin", "https://hatch.example"], default));

        Assert.Contains("HATCH_BASE=https://hatch.example", File.ReadAllLines(ConfigPath));
        Assert.Empty(input.Asked);
        Assert.Contains("reached https://hatch.example - columns: To Do, In Progress, Done", Said);
    }

    /// <summary>
    /// The sharp one. A command that silently blanked a credential would be a
    /// worse way to lose one than forgetting it.
    /// </summary>
    [Fact]
    public async Task Origin_leaves_the_key_and_the_cli_exactly_as_they_were()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllLines(ConfigPath, [
            "HATCH_BASE=https://old",
            "HATCH_KEY=hatch_ak_alreadykept",
            "HATCH_CLAUDE_BIN=/opt/claude",
        ]);

        Assert.Equal(0, await Command(new Replies()).RunAsync(["--origin", "https://new"], default));

        var written = File.ReadAllLines(ConfigPath);
        Assert.Contains("HATCH_BASE=https://new", written);
        Assert.Contains("HATCH_KEY=hatch_ak_alreadykept", written);
        Assert.Contains("HATCH_CLAUDE_BIN=/opt/claude", written);
    }

    /// <summary>Never on the screen, even in the mode that never asks for it.</summary>
    [Fact]
    public async Task Origin_never_prints_the_key_it_carried_forward()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllLines(ConfigPath, ["HATCH_KEY=hatch_ak_abcdefghijklmnop"]);

        await Command(new Replies()).RunAsync(["--origin", "https://hatch.example"], default);

        Assert.DoesNotContain("hatch_ak_abcdefghijklmnop", $"{Said}\n{Complained}");
    }

    /// <summary>
    /// The same sentence the interactive path says, because it is the same
    /// helper - a friend who paired the page's command with a wall-off Hatch
    /// should be told what their calls will name themselves.
    /// </summary>
    [Fact]
    public async Task Origin_with_no_key_anywhere_says_what_calls_will_name_themselves()
    {
        Assert.Equal(0, await Command(new Replies()).RunAsync(["--origin", "https://hatch.example"], default));

        Assert.Contains("HATCH_KEY=", File.ReadAllLines(ConfigPath));
        Assert.Contains("no key - calls will name themselves \"test:/checkout\"", Complained);
    }

    [Fact]
    public async Task Origin_with_no_scheme_is_refused_with_the_same_sentence_and_nothing_is_written()
    {
        Assert.Equal(1, await Command(new Replies()).RunAsync(["--origin", "hatch.example"], default));

        Assert.Contains("has no scheme - every call will fail", Complained);
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public async Task An_empty_origin_is_refused_and_nothing_is_written()
    {
        Assert.Equal(1, await Command(new Replies()).RunAsync(["--origin", "   "], default));

        Assert.Contains("an origin is required", Complained);
        Assert.False(File.Exists(ConfigPath));
    }

    /// <summary>Written before it is proven, exactly as the interactive path writes it.</summary>
    [Fact]
    public async Task Origin_that_cannot_be_reached_still_leaves_the_file_behind()
    {
        var code = await Command(
                new Replies(),
                probe: (_, _, _) => throw new HatchException("hatch: 401 - the key was not accepted."))
            .RunAsync(["--origin", "https://hatch.example"], default);

        Assert.Equal(1, code);
        Assert.Contains("HATCH_BASE=https://hatch.example", File.ReadAllLines(ConfigPath));
        Assert.Contains("the file is written but that call did not go through", Complained);
    }

    /// <summary>
    /// It needs no terminal - which is the difference from `config` and the
    /// reason a setup script may use it.
    /// </summary>
    [Fact]
    public async Task Origin_works_where_there_is_no_terminal_at_all()
    {
        var input = new Replies { AtATerminal = false };

        Assert.Equal(0, await Command(input).RunAsync(["--origin", "https://hatch.example"], default));
        Assert.Contains("HATCH_BASE=https://hatch.example", File.ReadAllLines(ConfigPath));
    }

    // ---- --show ----

    [Fact]
    public async Task Show_says_which_of_the_three_layers_each_value_came_from()
    {
        // A key goes with its origin (HA-1479), so it sits beside HATCH_BASE -
        // and a user-file key that goes unread because of it (HA-1481) is
        // called out rather than left silently dropped.
        var checkoutEnv = WriteCheckoutEnv("HATCH_BASE=https://this-repository", "HATCH_KEY=hatch_ak_from_the_checkout_file");
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllLines(ConfigPath, ["HATCH_KEY=hatch_ak_from_the_user_file_abc"]);

        var code = await Command(
                new Replies(),
                environment: new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["HATCH_CLAUDE_BIN"] = "/opt/claude",
                },
                checkoutEnv: checkoutEnv)
            .RunAsync(["--show"], default);

        Assert.Equal(0, code);
        Assert.Contains($"HATCH_BASE:        https://this-repository  ({checkoutEnv})", Said);
        Assert.Contains(
            $"HATCH_KEY:         hatch_ak_fro...file  ({checkoutEnv})" +
            $"  (ignored: {ConfigPath} holds a key, but HATCH_BASE comes from {checkoutEnv} - set both there, or neither)",
            Said);
        Assert.Contains("HATCH_CLAUDE_BIN:  /opt/claude  (exported)", Said);
        Assert.Contains("HATCH_BASE_BRANCH: <unset>", Said);
        Assert.DoesNotContain("hatch_ak_from_the_user_file_abc", Said);
    }

    [Fact]
    public async Task Show_prints_the_agent_and_its_layer()
    {
        await Command(
                new Replies(),
                environment: new Dictionary<string, string?>(StringComparer.Ordinal) { ["HATCH_AGENT"] = "claude" })
            .RunAsync(["--show"], default);

        Assert.Contains("HATCH_AGENT:       claude  (exported)", Said);
    }

    [Fact]
    public async Task Show_prints_HATCH_CODEX_BIN_and_codex_as_the_agent_with_their_layers()
    {
        await Command(
                new Replies(),
                environment: new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["HATCH_CODEX_BIN"] = "/opt/codex",
                    ["HATCH_AGENT"] = "codex",
                })
            .RunAsync(["--show"], default);

        Assert.Contains("HATCH_CODEX_BIN:   /opt/codex  (exported)", Said);
        Assert.Contains("HATCH_AGENT:       codex  (exported)", Said);
    }

    [Fact]
    public async Task Show_prints_HATCH_OLLAMA_HOST_and_HATCH_OLLAMA_TIERS_with_their_layers()
    {
        await Command(
                new Replies(),
                environment: new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["HATCH_OLLAMA_HOST"] = "http://box:11434",
                    ["HATCH_OLLAMA_TIERS"] = "sonnet=qwen3-coder:30b",
                })
            .RunAsync(["--show"], default);

        Assert.Contains("HATCH_OLLAMA_HOST: http://box:11434  (exported)", Said);
        Assert.Contains("HATCH_OLLAMA_TIERS:  sonnet=qwen3-coder:30b  (exported)", Said);
    }

    [Fact]
    public async Task Show_says_the_agent_is_unset_when_nothing_set_it()
    {
        await Command(new Replies()).RunAsync(["--show"], default);

        Assert.Contains("HATCH_AGENT:       <unset>", Said);
    }

    /// <summary>Enough of a key to recognise which one it is, and not enough to use.</summary>
    [Fact]
    public async Task Show_masks_the_key_and_never_prints_it_whole()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllLines(ConfigPath, ["HATCH_BASE=https://this-repository", "HATCH_KEY=hatch_ak_abcdefghijklmnop"]);

        await Command(new Replies()).RunAsync(["--show"], default);

        Assert.DoesNotContain("hatch_ak_abcdefghijklmnop", Said);
        Assert.Contains("hatch_ak_abc...mnop", Said);
    }

    [Fact]
    public void A_key_too_short_to_mask_usefully_is_only_reported_as_set()
    {
        Assert.Equal("(set)", ConfigCommand.Mask("short"));
        Assert.Equal("hatch_ak_abc...mnop", ConfigCommand.Mask("hatch_ak_abcdefghijklmnop"));
    }

    /// <summary>
    /// The pairing rule (HA-1479) drops this key before `--show` ever masks
    /// it - so the line says why it is unset rather than reading like nothing
    /// was ever exported.
    /// </summary>
    [Fact]
    public async Task Show_with_an_exported_key_and_a_file_origin_says_it_is_ignored_and_never_prints_it()
    {
        var checkoutEnv = WriteCheckoutEnv("HATCH_BASE=https://this-repository");
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["HATCH_KEY"] = "hatch_ak_exported_but_ignored",
        };

        var code = await Command(new Replies(), environment: environment, checkoutEnv: checkoutEnv).RunAsync(["--show"], default);

        Assert.Equal(0, code);
        Assert.Contains(
            $"{"HATCH_KEY:".PadRight(19)}<unset - calls go out as \"test:/checkout\", which only a wall-off Hatch reads>" +
            $"  (ignored: exported holds a key, but HATCH_BASE comes from {checkoutEnv} - set both there, or neither)",
            Said);
        Assert.DoesNotContain("hatch_ak_exported_but_ignored", Said);
    }

    /// <summary>
    /// With no key, what a call will name itself is the thing worth printing -
    /// only a wall-off Hatch reads it, and knowing that is half of diagnosing a
    /// 401.
    /// </summary>
    [Fact]
    public async Task Show_with_no_key_says_what_calls_will_name_themselves_instead()
    {
        await Command(new Replies()).RunAsync(["--show"], default);

        Assert.Contains("<unset - calls go out as \"test:/checkout\"", Said);
        Assert.Contains("(does not exist yet)", Said);
    }

    /// <summary>
    /// HA-116: not settable from a file, like HATCH_HEARTBEAT beside it, but
    /// still worth seeing - the ticket asks for it explicitly.
    /// </summary>
    [Fact]
    public async Task Show_lists_the_retry_window_even_though_it_is_not_settable_from_a_file()
    {
        await Command(new Replies()).RunAsync(["--show"], default);

        Assert.Contains("HATCH_RETRY_SECONDS:  90  (default)", Said);
    }

    [Fact]
    public async Task Show_reports_an_overridden_retry_window_and_its_layer()
    {
        await Command(
                new Replies(),
                environment: new Dictionary<string, string?>(StringComparer.Ordinal) { ["HATCH_RETRY_SECONDS"] = "30" })
            .RunAsync(["--show"], default);

        Assert.Contains("HATCH_RETRY_SECONDS:  30  (exported)", Said);
    }

    [Fact]
    public async Task Show_says_it_is_not_in_a_checkout_when_it_is_not()
    {
        await Command(new Replies()).RunAsync(["--show"], default);
        Assert.Contains("checkout:         <not in one>", Said);
    }

    [Fact]
    public async Task Show_outside_a_checkout_says_so_for_the_runner_too()
    {
        await Command(new Replies()).RunAsync(["--show"], default);
        Assert.Contains("runner:           <not in a checkout>", Said);
    }

    [Fact]
    public async Task Show_in_a_checkout_with_no_recorded_name_says_so()
    {
        var root = Directory.CreateTempSubdirectory("hatch-config-root-").FullName;
        await Command(new Replies(), root: root).RunAsync(["--show"], default);

        Assert.Contains(
            _say.Said, l => l.StartsWith("runner:", StringComparison.Ordinal) && l.Contains("none yet", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Show_in_a_checkout_prints_the_recorded_name_and_the_runners_file()
    {
        var root = Directory.CreateTempSubdirectory("hatch-config-root-").FullName;
        RunnerNames.Record.Set(RunnersPath, null, Checkout.Canonical(root), "Jeff Winger");

        await Command(new Replies(), root: root).RunAsync(["--show"], default);

        Assert.Contains($"runner:           Jeff Winger  ({RunnersPath})", Said);
    }

    [Fact]
    public async Task Show_says_HATCH_RUNNER_wins_when_it_is_set()
    {
        var root = Directory.CreateTempSubdirectory("hatch-config-root-").FullName;
        RunnerNames.Record.Set(RunnersPath, null, Checkout.Canonical(root), "Jeff Winger");

        await Command(
                new Replies(),
                environment: new Dictionary<string, string?>(StringComparer.Ordinal) { ["HATCH_RUNNER"] = "the-box" },
                root: root)
            .RunAsync(["--show"], default);

        Assert.Contains("runner:           the-box  (HATCH_RUNNER, exported)", Said);
    }

    [Fact]
    public async Task Show_prints_a_keys_identity_with_its_owner_and_scopes()
    {
        await Command(
                new Replies(),
                environment: new Dictionary<string, string?>(StringComparer.Ordinal) { ["HATCH_BASE"] = "https://hatch.example" },
                meProbe: (_, _, _) => Task.FromResult<MeDto?>(
                    new MeDto("key", "ada-key", "admin", true, false, false, "Ada", ["hatch"])))
            .RunAsync(["--show"], default);

        Assert.Contains("identity:         key \"ada-key\", owned by Ada, scopes: hatch", Said);
    }

    [Fact]
    public async Task Show_prints_a_runners_identity_by_its_name()
    {
        await Command(
                new Replies(),
                environment: new Dictionary<string, string?>(StringComparer.Ordinal) { ["HATCH_BASE"] = "https://hatch.example" },
                meProbe: (_, _, _) => Task.FromResult<MeDto?>(new MeDto("runner", "Morty", null, true, false, false)))
            .RunAsync(["--show"], default);

        Assert.Contains("identity:         calls go out as \"Morty\"", Said);
    }

    [Fact]
    public async Task Show_prints_unreachable_when_meProbe_throws_and_still_exits_zero()
    {
        var code = await Command(
                new Replies(),
                environment: new Dictionary<string, string?>(StringComparer.Ordinal) { ["HATCH_BASE"] = "https://hatch.example" },
                meProbe: (_, _, _) => throw new HatchException("boom"))
            .RunAsync(["--show"], default);

        Assert.Equal(0, code);
        Assert.Contains("identity:         <unreachable>", Said);
    }

    // ---- --key ----

    [Fact]
    public async Task Key_writes_the_key_and_keeps_the_origin_without_asking_anything()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllLines(ConfigPath, ["HATCH_BASE=https://kept", "HATCH_CLAUDE_BIN=/opt/claude"]);
        var input = new Replies();

        Assert.Equal(0, await Command(input).RunAsync(["--key", "hatch_ak_thesecret"], default));

        var written = File.ReadAllLines(ConfigPath);
        Assert.Contains("HATCH_BASE=https://kept", written);
        Assert.Contains("HATCH_KEY=hatch_ak_thesecret", written);
        Assert.Contains("HATCH_CLAUDE_BIN=/opt/claude", written);
        Assert.Empty(input.Asked);
        Assert.DoesNotContain("hatch_ak_thesecret", Said + Complained);
    }

    /// <summary>The same round trip, for the other known agent's own bin setting.</summary>
    [Fact]
    public async Task Key_keeps_HATCH_GROK_BIN_and_HATCH_AGENT_grok_without_asking_anything()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllLines(ConfigPath, [
            "HATCH_BASE=https://kept", "HATCH_GROK_BIN=/opt/grok", "HATCH_AGENT=grok",
        ]);

        Assert.Equal(0, await Command(new Replies()).RunAsync(["--key", "hatch_ak_thesecret"], default));

        var written = File.ReadAllLines(ConfigPath);
        Assert.Contains("HATCH_BASE=https://kept", written);
        Assert.Contains("HATCH_KEY=hatch_ak_thesecret", written);
        Assert.Contains("HATCH_GROK_BIN=/opt/grok", written);
        Assert.Contains("HATCH_AGENT=grok", written);
    }

    [Fact]
    public async Task Key_keeps_HATCH_CODEX_BIN_and_HATCH_AGENT_codex_without_asking_anything()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllLines(ConfigPath, [
            "HATCH_BASE=https://kept", "HATCH_CODEX_BIN=/opt/codex", "HATCH_AGENT=codex",
        ]);
        var input = new Replies();

        Assert.Equal(0, await Command(input).RunAsync(["--key", "hatch_ak_thesecret"], default));

        var written = File.ReadAllLines(ConfigPath);
        Assert.Contains("HATCH_CODEX_BIN=/opt/codex", written);
        Assert.Contains("HATCH_AGENT=codex", written);
        Assert.Empty(input.Asked);
    }

    [Fact]
    public async Task Origin_keeps_HATCH_CODEX_BIN()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllLines(ConfigPath, ["HATCH_BASE=https://old", "HATCH_CODEX_BIN=/opt/codex"]);

        Assert.Equal(0, await Command(new Replies()).RunAsync(["--origin", "https://new"], default));

        Assert.Contains("HATCH_CODEX_BIN=/opt/codex", File.ReadAllLines(ConfigPath));
    }

    [Fact]
    public async Task Origin_keeps_the_Ollama_host_and_tiers()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllLines(
            ConfigPath,
            ["HATCH_BASE=https://old", "HATCH_OLLAMA_HOST=http://kept:11434", "HATCH_OLLAMA_TIERS=sonnet=qwen3-coder:30b"]);

        Assert.Equal(0, await Command(new Replies()).RunAsync(["--origin", "https://new"], default));

        var written = File.ReadAllLines(ConfigPath);
        Assert.Contains("HATCH_OLLAMA_HOST=http://kept:11434", written);
        Assert.Contains("HATCH_OLLAMA_TIERS=sonnet=qwen3-coder:30b", written);
    }

    [Fact]
    public async Task Tag_keeps_the_Ollama_host_and_tiers()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllLines(
            ConfigPath,
            ["HATCH_BASE=https://kept", "HATCH_OLLAMA_HOST=http://kept:11434", "HATCH_OLLAMA_TIERS=sonnet=qwen3-coder:30b"]);

        Assert.Equal(0, await Command(new Replies()).RunAsync(["--tag", "gpu"], default));

        var written = File.ReadAllLines(ConfigPath);
        Assert.Contains("HATCH_OLLAMA_HOST=http://kept:11434", written);
        Assert.Contains("HATCH_OLLAMA_TIERS=sonnet=qwen3-coder:30b", written);
    }

    [Fact]
    public async Task Key_with_no_origin_yet_is_refused_and_nothing_is_written()
    {
        Assert.Equal(1, await Command(new Replies()).RunAsync(["--key", "hatch_ak_thesecret"], default));

        Assert.Contains("no origin is set yet", Complained);
        Assert.False(File.Exists(ConfigPath));
    }

    /// <summary>
    /// Plain <c>--key</c> writes the per-user file, but the origin here is
    /// pinned in the checkout's own <c>scripts/.env</c> - a different layer,
    /// so the pairing rule (HA-1479) will never present this key. Written
    /// anyway, and still probed with the key typed, as the sentence says.
    /// </summary>
    [Fact]
    public async Task Key_written_to_the_user_file_while_the_origin_sits_in_the_checkout_warns_it_will_not_be_sent()
    {
        var checkoutEnv = WriteCheckoutEnv("HATCH_BASE=https://this-repository");

        var code = await Command(new Replies(), checkoutEnv: checkoutEnv).RunAsync(["--key", "hatch_ak_thesecret"], default);

        Assert.Equal(0, code);
        Assert.Contains(
            $"hatch: that key will not be sent - HATCH_BASE comes from {checkoutEnv}. Set both there, or neither.",
            Complained);
        Assert.Contains("HATCH_KEY=hatch_ak_thesecret", File.ReadAllLines(ConfigPath));
    }

    [Fact]
    public async Task An_empty_key_is_refused_by_key_and_the_old_one_stays()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllLines(ConfigPath, ["HATCH_BASE=https://kept", "HATCH_KEY=hatch_ak_old"]);

        Assert.Equal(1, await Command(new Replies()).RunAsync(["--key", "  "], default));

        Assert.Contains("a key is required", Complained);
        Assert.Contains("HATCH_KEY=hatch_ak_old", File.ReadAllLines(ConfigPath));
    }

    [Fact]
    public async Task Key_with_the_wrong_prefix_is_a_warning_and_not_a_refusal()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllLines(ConfigPath, ["HATCH_BASE=https://kept"]);

        Assert.Equal(0, await Command(new Replies()).RunAsync(["--key", "hunter2"], default));

        Assert.Contains("does not start with hatch_ak_", Complained);
    }

    [Fact]
    public async Task Key_that_cannot_be_reached_still_leaves_the_file_behind()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllLines(ConfigPath, ["HATCH_BASE=https://kept"]);

        var code = await Command(new Replies(), probe: (_, _, _) => throw new HatchException("hatch: 401"))
            .RunAsync(["--key", "hatch_ak_refused"], default);

        Assert.Equal(1, code);
        Assert.Contains("HATCH_KEY=hatch_ak_refused", File.ReadAllLines(ConfigPath));
    }

    /// <summary>A key is one argument, and a bare `--key` is a mistake rather than a prompt.</summary>
    [Fact]
    public async Task Key_with_nothing_after_it_is_the_usage_block_and_not_a_question()
    {
        var input = new Replies();

        Assert.Equal(1, await Command(input).RunAsync(["--key"], default));
        Assert.Contains("usage: hatch config", Complained);
        Assert.Empty(input.Asked);
    }

    // ---- usage ----

    [Fact]
    public async Task Anything_other_than_the_three_modes_is_the_mistake_and_then_the_usage_block()
    {
        Assert.Equal(1, await Command(new Replies()).RunAsync(["--everything"], default));
        Assert.Contains("config takes nothing, --show, --origin <origin>, or --key <key>", Complained);
        Assert.Contains("usage: hatch config", Complained);
    }

    /// <summary>An origin is one argument, and a bare `--origin` is a mistake rather than a prompt.</summary>
    [Fact]
    public async Task Origin_with_nothing_after_it_is_the_usage_block_and_not_a_question()
    {
        var input = new Replies();

        Assert.Equal(1, await Command(input).RunAsync(["--origin"], default));
        Assert.Contains("config takes nothing, --show, --origin <origin>, or --key <key>", Complained);
        Assert.Empty(input.Asked);
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public async Task Dash_h_prints_its_own_usage_and_writes_nothing()
    {
        Assert.Equal(0, await Command(new Replies()).RunAsync(["-h"], default));
        Assert.StartsWith("usage: hatch config", Said);
        Assert.False(File.Exists(ConfigPath));
    }

    // ---- --repo ----

    private string Tree(string name)
    {
        var path = Path.Combine(_temp, name);
        Directory.CreateDirectory(Path.Combine(path, ".git"));
        return path;
    }

    [Fact]
    public async Task Repo_writes_the_joined_list_and_touches_nothing_else()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllLines(ConfigPath, ["HATCH_BASE=https://kept", "HATCH_KEY=hatch_ak_kept"]);

        var a = Tree("a");
        var b = Tree("b");

        Assert.Equal(0, await Command(new Replies()).RunAsync(["--repo", a, "--repo", b], default));

        var written = File.ReadAllLines(ConfigPath);
        Assert.Contains($"HATCH_REPOS={a}{Path.PathSeparator}{b}", written);
        Assert.Contains("HATCH_BASE=https://kept", written);
        Assert.Contains("HATCH_KEY=hatch_ak_kept", written);
    }

    [Fact]
    public async Task A_path_that_is_not_a_checkout_is_refused_naming_it_and_nothing_is_written()
    {
        var notACheckout = Path.Combine(_temp, "plain-directory");
        Directory.CreateDirectory(notACheckout);

        Assert.Equal(1, await Command(new Replies()).RunAsync(["--repo", notACheckout], default));

        Assert.Contains(notACheckout, Complained, StringComparison.Ordinal);
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public async Task A_missing_path_is_refused_naming_it_and_nothing_is_written()
    {
        var missing = Path.Combine(_temp, "does-not-exist");

        Assert.Equal(1, await Command(new Replies()).RunAsync(["--repo", missing], default));

        Assert.Contains(missing, Complained, StringComparison.Ordinal);
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public async Task Repo_alone_clears_what_was_there()
    {
        var a = Tree("a");
        await Command(new Replies()).RunAsync(["--repo", a], default);
        Assert.Contains($"HATCH_REPOS={a}", File.ReadAllLines(ConfigPath));

        Assert.Equal(0, await Command(new Replies()).RunAsync(["--repo"], default));

        Assert.DoesNotContain(File.ReadAllLines(ConfigPath), l => l.StartsWith("HATCH_REPOS=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Repo_shaped_wrong_is_refused_and_nothing_is_written()
    {
        var a = Tree("a");
        var b = Tree("b");

        // A trailing bare --repo.
        Assert.Equal(1, await Command(new Replies()).RunAsync(["--repo", a, "--repo"], default));
        Assert.False(File.Exists(ConfigPath));

        // Two paths with no --repo between them.
        Assert.Equal(1, await Command(new Replies()).RunAsync(["--repo", a, b], default));
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public async Task Show_lists_the_repos_one_per_line_with_the_layer()
    {
        var a = Tree("a");
        var b = Tree("b");
        await Command(new Replies()).RunAsync(["--repo", a, "--repo", b], default);

        await Command(new Replies()).RunAsync(["--show"], default);

        Assert.Contains($"HATCH_REPOS:       {a}  ({ConfigPath})", Said);
        Assert.Contains($"HATCH_REPOS:       {b}  ({ConfigPath})", Said);
    }

    [Fact]
    public async Task Show_with_no_repos_says_unset()
    {
        await Command(new Replies()).RunAsync(["--show"], default);
        Assert.Contains("HATCH_REPOS:       <unset>", Said);
    }

    /// <summary>
    /// The regression the fourth <c>Write</c> parameter exists to prevent: a
    /// `--repo` set earlier must survive an unrelated `--origin`/`--key` write.
    /// </summary>
    [Fact]
    public async Task Origin_after_repo_leaves_the_repos_in_the_file()
    {
        var a = Tree("a");
        await Command(new Replies()).RunAsync(["--repo", a], default);

        await Command(new Replies()).RunAsync(["--origin", "https://new"], default);

        Assert.Contains($"HATCH_REPOS={a}", File.ReadAllLines(ConfigPath));
    }

    [Fact]
    public async Task Key_after_repo_leaves_the_repos_in_the_file()
    {
        var a = Tree("a");
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllLines(ConfigPath, ["HATCH_BASE=https://kept"]);
        await Command(new Replies()).RunAsync(["--repo", a], default);

        await Command(new Replies()).RunAsync(["--key", "hatch_ak_new"], default);

        Assert.Contains($"HATCH_REPOS={a}", File.ReadAllLines(ConfigPath));
    }

    // ---- --workspace ----

    [Fact]
    public async Task Workspace_writes_HATCH_WORKSPACE_and_touches_nothing_else()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllLines(ConfigPath, ["HATCH_BASE=https://kept", "HATCH_KEY=hatch_ak_kept"]);

        var dir = Path.Combine(_temp, "clones");

        Assert.Equal(0, await Command(new Replies()).RunAsync(["--workspace", dir], default));

        var written = File.ReadAllLines(ConfigPath);
        Assert.Contains($"HATCH_WORKSPACE={dir}", written);
        Assert.Contains("HATCH_BASE=https://kept", written);
        Assert.Contains("HATCH_KEY=hatch_ak_kept", written);
        Assert.True(Directory.Exists(dir));
    }

    [Fact]
    public async Task A_workspace_inside_a_named_checkout_is_refused_naming_both()
    {
        var checkout = Tree("checkout");
        var dir = Path.Combine(checkout, "clones");

        await Command(new Replies()).RunAsync(["--repo", checkout], default);

        Assert.Equal(1, await Command(new Replies()).RunAsync(["--workspace", dir], default));
        Assert.Contains(checkout, Complained, StringComparison.Ordinal);
        Assert.Contains(dir, Complained, StringComparison.Ordinal);
        Assert.DoesNotContain(File.ReadAllLines(ConfigPath), l => l.StartsWith("HATCH_WORKSPACE=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_workspace_inside_the_standing_checkout_is_refused_naming_both()
    {
        var standing = Tree("standing");
        var dir = Path.Combine(standing, "clones");

        var cmd = Command(new Replies()) with { Root = standing };

        Assert.Equal(1, await cmd.RunAsync(["--workspace", dir], default));
        Assert.Contains(standing, Complained, StringComparison.Ordinal);
        Assert.Contains(dir, Complained, StringComparison.Ordinal);
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public async Task A_named_checkout_inside_the_workspace_is_refused_the_same_way()
    {
        var workspace = Path.Combine(_temp, "clones");
        Directory.CreateDirectory(workspace);
        var checkout = Tree(Path.Combine("clones", "a-checkout"));

        Assert.Equal(0, await Command(new Replies()).RunAsync(["--workspace", workspace], default));

        Assert.Equal(1, await Command(new Replies()).RunAsync(["--repo", checkout], default));
        Assert.Contains(workspace, Complained, StringComparison.Ordinal);
        Assert.Contains(checkout, Complained, StringComparison.Ordinal);
        Assert.DoesNotContain(File.ReadAllLines(ConfigPath), l => l.StartsWith("HATCH_REPOS=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Workspace_shaped_wrong_is_refused()
    {
        Assert.Equal(1, await Command(new Replies()).RunAsync(["--workspace"], default));
        Assert.False(File.Exists(ConfigPath));

        Assert.Equal(1, await Command(new Replies()).RunAsync(["--workspace", "a", "b"], default));
    }

    [Fact]
    public async Task Show_lists_the_workspace_with_its_layer()
    {
        var dir = Path.Combine(_temp, "clones");
        await Command(new Replies()).RunAsync(["--workspace", dir], default);

        await Command(new Replies()).RunAsync(["--show"], default);

        Assert.Contains($"HATCH_WORKSPACE:   {dir}  ({ConfigPath})", Said);
    }

    [Fact]
    public async Task Workspace_off_is_written_as_a_word_and_makes_no_directory()
    {
        Assert.Equal(0, await Command(new Replies()).RunAsync(["--workspace", "off"], default));

        Assert.Contains("HATCH_WORKSPACE=off", File.ReadAllLines(ConfigPath));
        Assert.False(Directory.Exists("off"));

        await Command(new Replies()).RunAsync(["--show"], default);
        Assert.Contains("off - this runner never clones", Said, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Workspace_off_does_not_trip_the_overlap_check_when_a_repo_is_added_after()
    {
        var checkout = Tree("checkout");
        await Command(new Replies()).RunAsync(["--workspace", "off"], default);

        Assert.Equal(0, await Command(new Replies()).RunAsync(["--repo", checkout], default));

        Assert.Contains("HATCH_WORKSPACE=off", File.ReadAllLines(ConfigPath));
    }

    [Fact]
    public async Task Show_with_no_workspace_says_unset_and_where_clones_go_instead()
    {
        await Command(new Replies()).RunAsync(["--show"], default);
        Assert.Contains($"HATCH_WORKSPACE:   <unset - clones go to {Settings.DefaultWorkspacePath()}>", Said);
    }

    // ---- --tag ----

    [Fact]
    public async Task Tag_writes_the_joined_list_and_touches_nothing_else()
    {
        await Command(new Replies()).RunAsync(["--origin", "https://kept"], default);

        Assert.Equal(0, await Command(new Replies()).RunAsync(["--tag", "gpu", "--tag", "linux"], default));

        var written = File.ReadAllLines(ConfigPath);
        Assert.Contains("HATCH_TAGS=gpu,linux", written);
        Assert.Contains("HATCH_BASE=https://kept", written);
    }

    [Fact]
    public async Task Tag_is_normalised_lowercased_and_deduplicated()
    {
        Assert.Equal(0, await Command(new Replies()).RunAsync(["--tag", "GPU", "--tag", "gpu"], default));

        Assert.Contains("HATCH_TAGS=gpu", File.ReadAllLines(ConfigPath));
    }

    [Fact]
    public async Task Tag_alone_clears_what_was_there()
    {
        await Command(new Replies()).RunAsync(["--tag", "gpu"], default);
        Assert.Contains("HATCH_TAGS=gpu", File.ReadAllLines(ConfigPath));

        Assert.Equal(0, await Command(new Replies()).RunAsync(["--tag"], default));

        Assert.DoesNotContain(File.ReadAllLines(ConfigPath), l => l.StartsWith("HATCH_TAGS=", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("gpu big")]
    [InlineData("a,b")]
    [InlineData("123456789012345678901234567890123")]
    public async Task A_tag_that_breaks_RunnerTags_is_refused_with_nothing_written(string badTag)
    {
        Assert.Equal(1, await Command(new Replies()).RunAsync(["--tag", badTag], default));

        Assert.Contains("a tag", Complained, StringComparison.Ordinal);
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public async Task Nine_tags_are_refused_with_nothing_written()
    {
        var args = new List<string>();
        for (var i = 0; i < 9; i++)
        {
            args.Add("--tag");
            args.Add($"tag{i}");
        }

        Assert.Equal(1, await Command(new Replies()).RunAsync([.. args], default));

        Assert.Contains("at most 8 tags", Complained, StringComparison.Ordinal);
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public async Task A_refused_tag_leaves_an_existing_file_untouched()
    {
        await Command(new Replies()).RunAsync(["--tag", "gpu"], default);
        var before = File.ReadAllBytes(ConfigPath);

        Assert.Equal(1, await Command(new Replies()).RunAsync(["--tag", "gpu big"], default));

        Assert.Equal(before, File.ReadAllBytes(ConfigPath));
    }

    [Fact]
    public async Task Tag_shaped_wrong_is_refused_and_nothing_is_written()
    {
        // A trailing bare --tag.
        Assert.Equal(1, await Command(new Replies()).RunAsync(["--tag", "gpu", "--tag"], default));
        Assert.False(File.Exists(ConfigPath));

        // Two values with no --tag between them.
        Assert.Equal(1, await Command(new Replies()).RunAsync(["--tag", "gpu", "linux"], default));
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public async Task Tag_needs_no_checkout_and_no_reachable_board()
    {
        var probed = false;
        var cmd = Command(new Replies(), root: null, probe: (_, _, _) =>
        {
            probed = true;
            return Task.FromResult<string?>(null);
        });

        Assert.Equal(0, await cmd.RunAsync(["--tag", "gpu"], default));
        Assert.False(probed);
    }

    [Fact]
    public async Task Show_lists_the_tags_with_the_layer()
    {
        await Command(new Replies()).RunAsync(["--tag", "gpu", "--tag", "linux"], default);

        await Command(new Replies()).RunAsync(["--show"], default);

        Assert.Contains($"HATCH_TAGS:        gpu, linux  ({ConfigPath})", Said);
    }

    [Fact]
    public async Task Show_with_no_tags_says_unset()
    {
        await Command(new Replies()).RunAsync(["--show"], default);
        Assert.Contains("HATCH_TAGS:        <unset>", Said);
    }

    [Fact]
    public async Task Origin_after_tag_leaves_the_tags_in_the_file()
    {
        await Command(new Replies()).RunAsync(["--tag", "gpu"], default);

        await Command(new Replies()).RunAsync(["--origin", "https://new"], default);

        Assert.Contains("HATCH_TAGS=gpu", File.ReadAllLines(ConfigPath));
    }

    [Fact]
    public async Task Key_after_tag_leaves_the_tags_in_the_file()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllLines(ConfigPath, ["HATCH_BASE=https://kept"]);
        await Command(new Replies()).RunAsync(["--tag", "gpu"], default);

        await Command(new Replies()).RunAsync(["--key", "hatch_ak_new"], default);

        Assert.Contains("HATCH_TAGS=gpu", File.ReadAllLines(ConfigPath));
    }

    [Fact]
    public async Task Repo_after_tag_leaves_the_tags_in_the_file()
    {
        var a = Tree("a");
        await Command(new Replies()).RunAsync(["--tag", "gpu"], default);

        await Command(new Replies()).RunAsync(["--repo", a], default);

        Assert.Contains("HATCH_TAGS=gpu", File.ReadAllLines(ConfigPath));
    }

    [Fact]
    public async Task Workspace_after_tag_leaves_the_tags_in_the_file()
    {
        var dir = Path.Combine(_temp, "clones");
        await Command(new Replies()).RunAsync(["--tag", "gpu"], default);

        await Command(new Replies()).RunAsync(["--workspace", dir], default);

        Assert.Contains("HATCH_TAGS=gpu", File.ReadAllLines(ConfigPath));
    }

    // ---- --log ----

    [Fact]
    public async Task Log_dir_writes_HATCH_LOG_makes_the_directory_and_touches_nothing_else()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllLines(ConfigPath, ["HATCH_BASE=https://kept", "HATCH_KEY=hatch_ak_kept"]);
        var dir = Path.Combine(_temp, "logs-here");

        Assert.Equal(0, await Command(new Replies()).RunAsync(["--log", dir], default));

        var written = File.ReadAllLines(ConfigPath);
        Assert.Contains($"HATCH_LOG={dir}", written);
        Assert.Contains("HATCH_BASE=https://kept", written);
        Assert.Contains("HATCH_KEY=hatch_ak_kept", written);
        Assert.True(Directory.Exists(dir));
    }

    [Fact]
    public async Task Log_on_writes_the_word_and_makes_the_default_folder()
    {
        Assert.Equal(0, await Command(new Replies()).RunAsync(["--log", "on"], default));

        Assert.Contains("HATCH_LOG=on", File.ReadAllLines(ConfigPath));
        Assert.True(Directory.Exists(Settings.DefaultLogPath()));
    }

    [Fact]
    public async Task Log_off_writes_the_word_and_makes_no_directory()
    {
        Assert.Equal(0, await Command(new Replies()).RunAsync(["--log", "off"], default));

        Assert.Contains("HATCH_LOG=off", File.ReadAllLines(ConfigPath));
        Assert.False(Directory.Exists("off"));
    }

    [Fact]
    public async Task Log_shaped_wrong_is_refused()
    {
        Assert.Equal(1, await Command(new Replies()).RunAsync(["--log"], default));
        Assert.Equal(1, await Command(new Replies()).RunAsync(["--log", "a", "b"], default));
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public async Task A_log_directory_that_cannot_be_made_is_refused_and_the_file_is_untouched()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllLines(ConfigPath, ["HATCH_BASE=https://kept"]);
        var file = Path.Combine(_temp, "a-file");
        File.WriteAllText(file, "");
        var dir = Path.Combine(file, "logs");

        Assert.Equal(1, await Command(new Replies()).RunAsync(["--log", dir], default));

        Assert.Contains(dir, Complained, StringComparison.Ordinal);
        Assert.Equal(["HATCH_BASE=https://kept"], File.ReadAllLines(ConfigPath));
    }

    [Fact]
    public async Task Log_is_left_in_the_file_by_every_other_write()
    {
        var logs = Path.Combine(_temp, "logs-kept");
        var checkout = Tree("checkout");
        var clones = Path.Combine(_temp, "clones");
        await Command(new Replies()).RunAsync(["--origin", "https://kept"], default);
        await Command(new Replies()).RunAsync(["--log", logs], default);

        await Command(new Replies()).RunAsync(["--repo", checkout], default);
        Assert.Contains($"HATCH_LOG={logs}", File.ReadAllLines(ConfigPath));

        await Command(new Replies()).RunAsync(["--workspace", clones], default);
        Assert.Contains($"HATCH_LOG={logs}", File.ReadAllLines(ConfigPath));

        await Command(new Replies()).RunAsync(["--workspace", "off"], default);
        Assert.Contains($"HATCH_LOG={logs}", File.ReadAllLines(ConfigPath));

        await Command(new Replies()).RunAsync(["--tag", "gpu"], default);
        Assert.Contains($"HATCH_LOG={logs}", File.ReadAllLines(ConfigPath));

        await Command(new Replies()).RunAsync(["--origin", "https://other"], default);
        Assert.Contains($"HATCH_LOG={logs}", File.ReadAllLines(ConfigPath));

        await Command(new Replies()).RunAsync(["--key", "hatch_ak_new"], default);
        Assert.Contains($"HATCH_LOG={logs}", File.ReadAllLines(ConfigPath));

        var input = new Replies("https://hatch.example", "hatch_ak_written", "claude", "/opt/claude");
        Assert.Equal(0, await Command(input).RunAsync([], default));
        Assert.Contains($"HATCH_LOG={logs}", File.ReadAllLines(ConfigPath));
    }

    [Fact]
    public async Task Tag_after_log_and_log_after_tag_keep_both()
    {
        var logs = Path.Combine(_temp, "logs-both");
        await Command(new Replies()).RunAsync(["--tag", "gpu"], default);

        await Command(new Replies()).RunAsync(["--log", logs], default);

        var written = File.ReadAllLines(ConfigPath);
        Assert.Contains("HATCH_TAGS=gpu", written);
        Assert.Contains($"HATCH_LOG={logs}", written);
    }

    [Fact]
    public async Task Here_log_writes_the_checkout_file_and_leaves_the_per_user_file_untouched()
    {
        var (root, checkoutEnv) = HereCheckout();
        var dir = Path.Combine(_temp, "logs-here");

        Assert.Equal(
            0, await Command(new Replies(), root: root, checkoutEnv: checkoutEnv).RunAsync(["--here", "--log", dir], default));

        Assert.Contains($"HATCH_LOG={dir}", File.ReadAllLines(checkoutEnv));
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public async Task Profile_log_writes_the_profile_file_and_leaves_the_per_user_file_untouched()
    {
        var profilePath = Profile();

        Assert.Equal(
            0,
            await Command(new Replies(), profileName: "work", profilePath: profilePath)
                .RunAsync(["--log", "off"], default));

        Assert.Contains("HATCH_LOG=off", File.ReadAllLines(profilePath));
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public async Task Show_with_no_log_says_unset()
    {
        await Command(new Replies()).RunAsync(["--show"], default);

        Assert.Contains("HATCH_LOG:         <unset - no log>", Said);
    }

    [Fact]
    public async Task Show_says_off_for_an_off_log()
    {
        await Command(new Replies()).RunAsync(["--log", "off"], default);

        await Command(new Replies()).RunAsync(["--show"], default);

        Assert.Contains($"HATCH_LOG:         off - no log  ({ConfigPath})", Said);
    }

    [Fact]
    public async Task Show_names_the_default_folder_for_an_on_log()
    {
        await Command(new Replies()).RunAsync(["--log", "on"], default);

        await Command(new Replies()).RunAsync(["--show"], default);

        Assert.Contains($"HATCH_LOG:         {Settings.DefaultLogPath()}  ({ConfigPath})", Said);
    }

    [Fact]
    public async Task Show_names_the_folder_of_a_directory_log()
    {
        var dir = Path.Combine(_temp, "logs-shown");
        await Command(new Replies()).RunAsync(["--log", dir], default);

        await Command(new Replies()).RunAsync(["--show"], default);

        Assert.Contains($"HATCH_LOG:         {dir}  ({ConfigPath})", Said);
    }

    // ---- --here ----

    [Fact]
    public async Task Here_tag_writes_the_checkout_file_and_leaves_the_per_user_file_untouched()
    {
        var (root, checkoutEnv) = HereCheckout();

        Assert.Equal(
            0, await Command(new Replies(), root: root, checkoutEnv: checkoutEnv).RunAsync(["--here", "--tag", "gpu"], default));

        Assert.Contains("HATCH_TAGS=gpu", File.ReadAllLines(checkoutEnv));
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public async Task Here_origin_writes_the_checkout_file_and_leaves_the_per_user_file_untouched()
    {
        var (root, checkoutEnv) = HereCheckout();

        Assert.Equal(
            0,
            await Command(new Replies(), root: root, checkoutEnv: checkoutEnv)
                .RunAsync(["--here", "--origin", "https://hatch.example"], default));

        Assert.Contains("HATCH_BASE=https://hatch.example", File.ReadAllLines(checkoutEnv));
        Assert.False(File.Exists(ConfigPath));
    }

    /// <summary>
    /// The stale-fold bug the ticket named: once <c>--here --origin</c> writes
    /// this checkout's own line, the origin's layer is this checkout's file,
    /// not the per-user file a key happens to sit in - so the pairing rule
    /// (HA-1479) drops that key, correctly, and the probe is made with none.
    /// </summary>
    [Fact]
    public async Task Here_origin_over_a_per_user_key_says_no_key_and_probes_with_none()
    {
        var (root, checkoutEnv) = HereCheckout();
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllLines(ConfigPath, ["HATCH_KEY=hatch_ak_from_the_user_file"]);

        string? probedKey = null;
        var code = await Command(
                new Replies(), root: root, checkoutEnv: checkoutEnv,
                probe: (settings, _, _) =>
                {
                    probedKey = settings.Key;
                    return Task.FromResult<string?>("To Do");
                })
            .RunAsync(["--here", "--origin", "https://hatch.example"], default);

        Assert.Equal(0, code);
        Assert.Equal("", probedKey);
        Assert.Contains("no key - calls will name themselves", Complained);
    }

    [Fact]
    public async Task Here_key_writes_the_checkout_file_and_leaves_the_per_user_file_untouched()
    {
        var (root, checkoutEnv) = HereCheckout();
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["HATCH_BASE"] = "https://hatch.example",
        };

        Assert.Equal(
            0,
            await Command(new Replies(), environment: environment, root: root, checkoutEnv: checkoutEnv)
                .RunAsync(["--here", "--key", "hatch_ak_thesecret"], default));

        Assert.Contains("HATCH_KEY=hatch_ak_thesecret", File.ReadAllLines(checkoutEnv));
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public async Task Here_repo_writes_the_checkout_file_and_leaves_the_per_user_file_untouched()
    {
        var (root, checkoutEnv) = HereCheckout();
        var a = Tree("a");

        Assert.Equal(
            0, await Command(new Replies(), root: root, checkoutEnv: checkoutEnv).RunAsync(["--here", "--repo", a], default));

        Assert.Contains($"HATCH_REPOS={a}", File.ReadAllLines(checkoutEnv));
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public async Task Here_workspace_writes_the_checkout_file_and_leaves_the_per_user_file_untouched()
    {
        var (root, checkoutEnv) = HereCheckout();
        var dir = Path.Combine(_temp, "clones");

        Assert.Equal(
            0,
            await Command(new Replies(), root: root, checkoutEnv: checkoutEnv)
                .RunAsync(["--here", "--workspace", dir], default));

        Assert.Contains($"HATCH_WORKSPACE={dir}", File.ReadAllLines(checkoutEnv));
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public async Task Here_interactive_writes_the_checkout_file_and_leaves_the_per_user_file_untouched()
    {
        var (root, checkoutEnv) = HereCheckout();
        var input = new Replies("https://hatch.example", "hatch_ak_written", "claude", "/opt/claude", "");

        Assert.Equal(0, await Command(input, root: root, checkoutEnv: checkoutEnv).RunAsync(["--here"], default));

        Assert.Contains("HATCH_BASE=https://hatch.example", File.ReadAllLines(checkoutEnv));
        Assert.False(File.Exists(ConfigPath));
    }

    /// <summary>
    /// The written header's own promise: an empty untouched field is never a
    /// line, so a file that started with nothing holds only what was asked
    /// for - never an empty <c>HATCH_BASE=</c>/<c>HATCH_KEY=</c> pair the way
    /// the per-user file always carries.
    /// </summary>
    [Fact]
    public async Task Here_tag_with_no_prior_checkout_file_writes_only_the_tag()
    {
        var (root, checkoutEnv) = HereCheckout();

        Assert.Equal(
            0,
            await Command(new Replies(), root: root, checkoutEnv: checkoutEnv)
                .RunAsync(["--here", "--tag", "breakdown"], default));

        var written = File.ReadAllLines(checkoutEnv).Where(l => l.Length > 0 && l[0] != '#').ToArray();
        Assert.Equal(["HATCH_TAGS=breakdown"], written);
    }

    /// <summary>
    /// The point of <c>Carry</c>: an exported value and a value in the
    /// per-user file are both real settings, but neither is this checkout's
    /// own - so neither leaks into the file <c>--here</c> writes.
    /// </summary>
    [Fact]
    public async Task Here_tag_carries_forward_only_the_checkout_files_own_lines()
    {
        var (root, checkoutEnv) = HereCheckout("HATCH_BASE=https://this-checkout");
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllLines(ConfigPath, ["HATCH_KEY=hatch_ak_fromuser"]);
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["HATCH_CLAUDE_BIN"] = "/opt/exported",
        };

        Assert.Equal(
            0,
            await Command(new Replies(), environment: environment, root: root, checkoutEnv: checkoutEnv)
                .RunAsync(["--here", "--tag", "gpu"], default));

        var written = File.ReadAllLines(checkoutEnv);
        Assert.Contains("HATCH_BASE=https://this-checkout", written);
        Assert.Contains("HATCH_TAGS=gpu", written);
        Assert.DoesNotContain(written, l => l.StartsWith("HATCH_KEY=", StringComparison.Ordinal));
        Assert.DoesNotContain(written, l => l.StartsWith("HATCH_CLAUDE_BIN=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Here_runner_is_refused_with_the_exact_sentence()
    {
        var (root, checkoutEnv) = HereCheckout();

        var code = await Command(new Replies(), root: root, checkoutEnv: checkoutEnv)
            .RunAsync(["--here", "--runner", "Liz Lemon"], default);

        Assert.Equal(1, code);
        Assert.Contains("hatch: a runner's name is already this checkout's - --runner needs no --here", Complained);
        Assert.False(File.Exists(checkoutEnv));
    }

    [Fact]
    public async Task Here_show_is_refused_as_a_flag_show_does_not_take()
    {
        var (root, checkoutEnv) = HereCheckout();

        var code = await Command(new Replies(), root: root, checkoutEnv: checkoutEnv).RunAsync(["--here", "--show"], default);

        Assert.Equal(1, code);
        Assert.Contains("config --show takes no --here", Complained);
    }

    [Fact]
    public async Task Here_outside_a_checkout_is_refused_with_the_exact_sentence()
    {
        var code = await Command(new Replies(), root: null).RunAsync(["--here", "--tag", "gpu"], default);

        Assert.Equal(1, code);
        Assert.Contains("hatch: --here writes this checkout's scripts/.env - run it inside one", Complained);
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public async Task Here_refused_when_the_checkout_does_not_ignore_its_env_file()
    {
        var (root, checkoutEnv) = HereCheckout();

        var code = await Command(new Replies(), root: root, checkoutEnv: checkoutEnv, checkoutIgnoresEnv: _ => false)
            .RunAsync(["--here", "--tag", "gpu"], default);

        Assert.Equal(1, code);
        Assert.Contains("is not ignored by this checkout's git", Complained);
        Assert.False(File.Exists(checkoutEnv));
    }

    /// <summary>
    /// The split the ticket calls for: what is written under <c>--here</c> is
    /// this checkout's own line (or nothing, carrying none), but whether an
    /// origin exists at all - and what the probe proves against - is the real,
    /// merged answer, exactly what a subsequent load would resolve to.
    /// </summary>
    [Fact]
    public async Task Here_key_validates_against_folds_origin_even_when_the_checkout_carries_none()
    {
        var (root, checkoutEnv) = HereCheckout();
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllLines(ConfigPath, ["HATCH_BASE=https://from-the-user-file"]);

        string? probedBase = null;
        var code = await Command(
                new Replies(), root: root, checkoutEnv: checkoutEnv,
                probe: (settings, _, _) =>
                {
                    probedBase = settings.Base;
                    return Task.FromResult<string?>("To Do");
                })
            .RunAsync(["--here", "--key", "hatch_ak_thesecret"], default);

        Assert.Equal(0, code);
        Assert.Equal("https://from-the-user-file", probedBase);

        var written = File.ReadAllLines(checkoutEnv);
        Assert.Contains("HATCH_KEY=hatch_ak_thesecret", written);
        Assert.DoesNotContain(written, l => l.StartsWith("HATCH_BASE=", StringComparison.Ordinal));
    }

    /// <summary>
    /// The one check the ticket calls out by name: the overlap guard must see
    /// this checkout's own <c>HATCH_REPOS</c>, not whatever an export happens
    /// to carry - an exported list that does not overlap must not wave through
    /// a workspace that does overlap the checkout file's own repo.
    /// </summary>
    [Fact]
    public async Task Here_workspace_overlap_checks_against_the_checkout_files_own_repos_not_folds()
    {
        var checkoutRepo = Tree("checkout-repo");
        var (root, checkoutEnv) = HereCheckout($"HATCH_REPOS={checkoutRepo}");
        var dir = Path.Combine(checkoutRepo, "clones");

        var exportedRepo = Tree("exported-repo");
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal) { ["HATCH_REPOS"] = exportedRepo };

        var code = await Command(new Replies(), environment: environment, root: root, checkoutEnv: checkoutEnv)
            .RunAsync(["--here", "--workspace", dir], default);

        Assert.Equal(1, code);
        Assert.Contains(checkoutRepo, Complained, StringComparison.Ordinal);
        Assert.DoesNotContain(File.ReadAllLines(checkoutEnv), l => l.StartsWith("HATCH_WORKSPACE=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Here_before_or_after_the_flag_it_follows_writes_the_same_file()
    {
        var (rootA, checkoutEnvA) = HereCheckout();
        Assert.Equal(
            0, await Command(new Replies(), root: rootA, checkoutEnv: checkoutEnvA).RunAsync(["--here", "--tag", "x"], default));

        var (rootB, checkoutEnvB) = HereCheckout();
        Assert.Equal(
            0, await Command(new Replies(), root: rootB, checkoutEnv: checkoutEnvB).RunAsync(["--tag", "x", "--here"], default));

        var linesA = File.ReadAllLines(checkoutEnvA).Where(l => l.Length > 0 && l[0] != '#');
        var linesB = File.ReadAllLines(checkoutEnvB).Where(l => l.Length > 0 && l[0] != '#');
        Assert.Equal(linesA, linesB);
    }

    // ---- config --show says what --here would write to ----

    [Fact]
    public async Task Show_names_what_here_would_write_to_when_in_a_checkout()
    {
        var (root, checkoutEnv) = HereCheckout();

        await Command(new Replies(), root: root, checkoutEnv: checkoutEnv).RunAsync(["--show"], default);

        Assert.Contains($"checkout:         {checkoutEnv}  (written by --here)", Said);
    }

    // ---- --profile ----

    [Fact]
    public async Task Profile_tag_writes_the_profile_file_and_leaves_the_per_user_file_untouched()
    {
        var profilePath = Profile();

        Assert.Equal(
            0,
            await Command(new Replies(), profileName: "work", profilePath: profilePath)
                .RunAsync(["--tag", "gpu"], default));

        Assert.Contains("HATCH_TAGS=gpu", File.ReadAllLines(profilePath));
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public async Task Profile_origin_writes_the_profile_file_and_leaves_the_per_user_file_untouched()
    {
        var profilePath = Profile();

        Assert.Equal(
            0,
            await Command(new Replies(), profileName: "work", profilePath: profilePath)
                .RunAsync(["--origin", "https://hatch.example"], default));

        Assert.Contains("HATCH_BASE=https://hatch.example", File.ReadAllLines(profilePath));
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public async Task Profile_key_writes_the_profile_file_and_leaves_the_per_user_file_untouched()
    {
        var profilePath = Profile();
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["HATCH_BASE"] = "https://hatch.example",
        };

        Assert.Equal(
            0,
            await Command(new Replies(), environment: environment, profileName: "work", profilePath: profilePath)
                .RunAsync(["--key", "hatch_ak_thesecret"], default));

        Assert.Contains("HATCH_KEY=hatch_ak_thesecret", File.ReadAllLines(profilePath));
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public async Task Profile_repo_writes_the_profile_file_and_leaves_the_per_user_file_untouched()
    {
        var profilePath = Profile();
        var a = Tree("profile-a");

        Assert.Equal(
            0,
            await Command(new Replies(), profileName: "work", profilePath: profilePath)
                .RunAsync(["--repo", a], default));

        Assert.Contains($"HATCH_REPOS={a}", File.ReadAllLines(profilePath));
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public async Task Profile_workspace_writes_the_profile_file_and_leaves_the_per_user_file_untouched()
    {
        var profilePath = Profile();
        var dir = Path.Combine(_temp, "profile-clones");

        Assert.Equal(
            0,
            await Command(new Replies(), profileName: "work", profilePath: profilePath)
                .RunAsync(["--workspace", dir], default));

        Assert.Contains($"HATCH_WORKSPACE={dir}", File.ReadAllLines(profilePath));
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public async Task Profile_interactive_writes_the_profile_file_and_leaves_the_per_user_file_untouched()
    {
        var profilePath = Profile();
        var input = new Replies("https://hatch.example", "hatch_ak_written", "claude", "/opt/claude");

        Assert.Equal(0, await Command(input, profileName: "work", profilePath: profilePath).RunAsync([], default));

        Assert.Contains("HATCH_BASE=https://hatch.example", File.ReadAllLines(profilePath));
        Assert.False(File.Exists(ConfigPath));
    }

    /// <summary>
    /// The same promise <c>--here</c> makes: an empty untouched field is never
    /// a line, so a file that started with nothing holds only what was asked
    /// for.
    /// </summary>
    [Fact]
    public async Task Profile_tag_with_no_prior_profile_file_writes_only_the_tag()
    {
        var profilePath = Profile();

        Assert.Equal(
            0,
            await Command(new Replies(), profileName: "work", profilePath: profilePath)
                .RunAsync(["--tag", "breakdown"], default));

        var written = File.ReadAllLines(profilePath).Where(l => l.Length > 0 && l[0] != '#').ToArray();
        Assert.Equal(["HATCH_TAGS=breakdown"], written);
    }

    /// <summary>
    /// The point of the profile's own <c>overlayOnly</c> read: an exported
    /// value and a value in the per-user file are both real settings, but
    /// neither is this profile's own - so neither leaks into the file
    /// <c>--profile</c> writes.
    /// </summary>
    [Fact]
    public async Task Profile_tag_carries_forward_only_the_profiles_own_lines()
    {
        var profilePath = Profile("HATCH_BASE=https://this-profile");
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllLines(ConfigPath, ["HATCH_KEY=hatch_ak_fromuser"]);
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["HATCH_CLAUDE_BIN"] = "/opt/exported",
        };

        Assert.Equal(
            0,
            await Command(new Replies(), environment: environment, profileName: "work", profilePath: profilePath)
                .RunAsync(["--tag", "gpu"], default));

        var written = File.ReadAllLines(profilePath);
        Assert.Contains("HATCH_BASE=https://this-profile", written);
        Assert.Contains("HATCH_TAGS=gpu", written);
        Assert.DoesNotContain(written, l => l.StartsWith("HATCH_KEY=", StringComparison.Ordinal));
        Assert.DoesNotContain(written, l => l.StartsWith("HATCH_CLAUDE_BIN=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Show_names_the_profile_on_the_runner_line()
    {
        var root = Directory.CreateTempSubdirectory("hatch-config-root-").FullName;
        var canonical = Checkout.Canonical(root);
        RunnerNames.Record.Set(RunnersPath, "a", canonical, "Toby");
        RunnerNames.Record.Set(RunnersPath, null, canonical, "Bare");

        await Command(new Replies(), root: root, profileName: "a", profilePath: Profile())
            .RunAsync(["--show"], default);

        Assert.Contains($"runner:           Toby  ({RunnersPath})  (profile: a)", Said);
        Assert.DoesNotContain("Bare", Said);
    }

    [Fact]
    public async Task Show_names_the_profile_on_the_workspace_line()
    {
        var root = Directory.CreateTempSubdirectory("hatch-config-root-").FullName;

        await Command(new Replies(), root: root, profileName: "a", profilePath: Profile())
            .RunAsync(["--show"], default);

        Assert.Contains(
            $"HATCH_WORKSPACE:   <unset - clones go to {Settings.DefaultWorkspacePath("a")}>  (profile: a)", Said);
    }

    [Fact]
    public async Task Profile_and_here_is_refused()
    {
        var (root, checkoutEnv) = HereCheckout();
        var profilePath = Profile();

        var code = await Command(new Replies(), root: root, checkoutEnv: checkoutEnv, profileName: "work", profilePath: profilePath)
            .RunAsync(["--here", "--tag", "gpu"], default);

        Assert.Equal(1, code);
        Assert.Contains("hatch: --here and --profile write to two different places - pick one", Complained);
        Assert.False(File.Exists(checkoutEnv));
        Assert.False(File.Exists(profilePath));
    }

    /// <summary>
    /// Program.cs leaves <c>ProfilePath</c> null for a shape-invalid name but
    /// passes the raw string through as <c>ProfileName</c> unchanged -
    /// <c>ConfigCommand</c> is where that is caught, not silently dropped.
    /// </summary>
    [Fact]
    public async Task Invalid_profile_name_is_refused_before_anything_is_written()
    {
        var code = await Command(new Replies(), profileName: "li z", profilePath: null)
            .RunAsync(["--tag", "gpu"], default);

        Assert.Equal(1, code);
        Assert.Contains("is not a valid profile name", Complained);
        Assert.False(File.Exists(ConfigPath));
    }

    // ---- config --show with --profile ----

    [Fact]
    public async Task Show_with_an_explicit_profile_names_Layer_Profile_for_every_value_it_supplies()
    {
        var profilePath = Profile("HATCH_BASE=https://from-profile", "HATCH_TAGS=gpu");

        var code = await Command(new Replies(), profileName: "work", profilePath: profilePath).RunAsync(["--show"], default);

        Assert.Equal(0, code);
        Assert.Contains($"profile:          work  ({profilePath})", Said);
        Assert.Contains($"HATCH_BASE:        https://from-profile  ({profilePath})", Said);
        Assert.Contains($"HATCH_TAGS:        gpu  ({profilePath})", Said);
    }

    [Fact]
    public async Task Show_with_an_explicit_profile_reads_it_without_switching_the_active_one()
    {
        var profilePath = Profile("HATCH_BASE=https://from-profile");

        var code = await Command(new Replies(), profileName: "work", profilePath: profilePath).RunAsync(["--show"], default);

        Assert.Equal(0, code);
        Assert.False(File.Exists(Settings.ActiveProfilePath()));
    }

    [Fact]
    public async Task Show_with_no_profile_given_and_HATCH_PROFILE_exported_but_missing_says_it_does_not_exist()
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["HATCH_PROFILE"] = "hatch-config-tests-no-such-profile",
        };

        var code = await Command(new Replies(), environment: environment, resolveProfile: Settings.TryResolveProfile)
            .RunAsync(["--show"], default);

        Assert.Equal(0, code);
        Assert.Contains("profile:          hatch-config-tests-no-such-profile  (does not exist)", Said);
    }

    [Fact]
    public async Task Show_with_no_profile_anywhere_prints_none()
    {
        var code = await Command(new Replies()).RunAsync(["--show"], default);

        Assert.Equal(0, code);
        Assert.Contains("profile:          <none>", Said);
    }

    /// <summary>
    /// HA-1480's rule, said out loud: a board-bound name folds past the
    /// per-user file while a profile is active, and the line says so rather
    /// than reading like the file was simply empty. A machine-bound name
    /// beside it still reads the per-user file, and carries no such note.
    /// </summary>
    [Fact]
    public async Task Show_under_a_profile_flags_board_bound_lines_but_not_HATCH_CLAUDE_BIN()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllLines(ConfigPath, ["HATCH_WORKSPACE=/wherever", "HATCH_CLAUDE_BIN=/opt/claude-user"]);
        var profilePath = Profile("HATCH_BASE=https://from-profile");

        var code = await Command(new Replies(), profileName: "work", profilePath: profilePath).RunAsync(["--show"], default);

        Assert.Equal(0, code);
        Assert.Contains(
            $"HATCH_WORKSPACE:   <unset - clones go to {Settings.DefaultWorkspacePath("work")}>" +
            "  (profile: work)  (profile active: the per-user file was not consulted)",
            Said);
        Assert.Contains($"HATCH_CLAUDE_BIN:  /opt/claude-user  ({ConfigPath})", Said);
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

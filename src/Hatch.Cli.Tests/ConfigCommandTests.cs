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
        Func<Settings, CancellationToken, Task<IReadOnlyList<RunnerDto>>>? liveRunners = null) =>
        new(_say, input, environment ?? new Dictionary<string, string?>(StringComparer.Ordinal),
            checkoutEnv, runnerName, root)
        {
            ConfigPath = ConfigPath,
            RunnersPath = RunnersPath,
            Probe = probe ?? ((_, _, _) => Task.FromResult<string?>("To Do, In Progress, Done")),
            LiveRunners = liveRunners ?? ((_, _) => Task.FromResult<IReadOnlyList<RunnerDto>>([])),
        };

    private string Said => string.Join("\n", _say.Said);

    private string Complained => string.Join("\n", _say.Complained);

    private string WriteCheckoutEnv(params string[] lines)
    {
        var path = Path.Combine(_temp, "scripts.env");
        File.WriteAllLines(path, lines);
        return path;
    }

    // ---- writing ----

    [Fact]
    public async Task It_asks_for_the_origin_the_key_and_the_cli_and_writes_all_three()
    {
        var input = new Replies("https://hatch.example", "hatch_ak_written", "/opt/claude");

        Assert.Equal(0, await Command(input).RunAsync([], default));

        var written = File.ReadAllLines(ConfigPath);
        Assert.Contains("HATCH_BASE=https://hatch.example", written);
        Assert.Contains("HATCH_KEY=hatch_ak_written", written);
        Assert.Contains("HATCH_CLAUDE_BIN=/opt/claude", written);

        Assert.Contains("Hatch origin", input.Asked[0]);
        Assert.Contains("API key", input.Asked[1]);
        Assert.Contains("claude CLI path", input.Asked[2]);
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
        var input = new Replies("https://hatch.example", "", "", "");

        Assert.Equal(
            0, await Command(input, root: root, runnerName: "Jeff Winger").RunAsync([], default));

        Assert.Contains("What should the board call this runner? [Jeff Winger]:", input.Asked[3]);
        Assert.Empty(RunnerNames.Record.Read(RunnersPath));
    }

    [Fact]
    public async Task A_typed_name_is_recorded()
    {
        var root = Directory.CreateTempSubdirectory("hatch-config-root-").FullName;
        var input = new Replies("https://hatch.example", "", "", "Liz Lemon");

        Assert.Equal(
            0, await Command(input, root: root, runnerName: "Jeff Winger").RunAsync([], default));

        Assert.Equal("Liz Lemon", RunnerNames.Record.Read(RunnersPath)[Checkout.Canonical(root)]);
    }

    [Fact]
    public async Task An_empty_typed_name_is_refused_with_nothing_written()
    {
        var root = Directory.CreateTempSubdirectory("hatch-config-root-").FullName;
        var input = new Replies("https://hatch.example", "", "", new string(' ', 3));

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
        var input = new Replies("https://hatch.example", "", "", "Jeff Wïnger");

        var code = await Command(input, root: root, runnerName: "Jeff Winger").RunAsync([], default);

        Assert.Equal(1, code);
        Assert.Contains("a name is plain ASCII", Complained);
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public async Task A_name_over_the_limit_is_refused_with_nothing_written()
    {
        var root = Directory.CreateTempSubdirectory("hatch-config-root-").FullName;
        var input = new Replies("https://hatch.example", "", "", new string('x', ClaimRequest.MaxRunnerLength + 1));

        var code = await Command(input, root: root, runnerName: "Jeff Winger").RunAsync([], default);

        Assert.Equal(1, code);
        Assert.Contains($"a name is at most {ClaimRequest.MaxRunnerLength} characters", Complained);
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public async Task A_name_held_by_a_live_runner_elsewhere_is_refused_with_nothing_written()
    {
        var root = Directory.CreateTempSubdirectory("hatch-config-root-").FullName;
        var input = new Replies("https://hatch.example", "hatch_ak_ok", "", "Liz Lemon");

        var code = await Command(
                input, root: root, runnerName: "Jeff Winger",
                liveRunners: (_, _) => Task.FromResult<IReadOnlyList<RunnerDto>>(
                [
                    new RunnerDto(
                        "Liz Lemon", "loop", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, null,
                        "running", null, null, null, null, 90, [], null, null, "elsewhere:/some/tree"),
                ]))
            .RunAsync([], default);

        Assert.Equal(1, code);
        Assert.Contains("Liz Lemon is already the runner on elsewhere:/some/tree", Complained);
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
    public async Task Runner_flag_outside_a_checkout_is_refused()
    {
        var code = await Command(new Replies()).RunAsync(["--runner", "Liz Lemon"], default);

        Assert.Equal(1, code);
        Assert.Contains("--runner names this checkout - run it inside one", Complained);
    }

    [Fact]
    public async Task Runner_flag_held_elsewhere_is_refused_with_nothing_written()
    {
        var root = Directory.CreateTempSubdirectory("hatch-config-root-").FullName;

        var code = await Command(
                new Replies(),
                environment: new Dictionary<string, string?>(StringComparer.Ordinal) { ["HATCH_BASE"] = "https://hatch.example" },
                root: root,
                liveRunners: (_, _) => Task.FromResult<IReadOnlyList<RunnerDto>>(
                [
                    new RunnerDto(
                        "Liz Lemon", "loop", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, null,
                        "running", null, null, null, null, 90, [], null, null, "elsewhere:/some/tree"),
                ]))
            .RunAsync(["--runner", "Liz Lemon"], default);

        Assert.Equal(1, code);
        Assert.Contains("Liz Lemon is already the runner on elsewhere:/some/tree", Complained);
        Assert.Empty(RunnerNames.Record.Read(RunnersPath));
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
        var checkoutEnv = WriteCheckoutEnv("HATCH_BASE=https://this-repository");
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllLines(ConfigPath, ["HATCH_KEY=hatch_ak_from_the_user_file"]);

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
        Assert.Contains($"HATCH_KEY:         hatch_ak_fro...file  ({ConfigPath})", Said);
        Assert.Contains("HATCH_CLAUDE_BIN:  /opt/claude  (exported)", Said);
        Assert.Contains("HATCH_BASE_BRANCH: <unset>", Said);
    }

    /// <summary>Enough of a key to recognise which one it is, and not enough to use.</summary>
    [Fact]
    public async Task Show_masks_the_key_and_never_prints_it_whole()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllLines(ConfigPath, ["HATCH_KEY=hatch_ak_abcdefghijklmnop"]);

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
        RunnerNames.Record.Set(RunnersPath, Checkout.Canonical(root), "Jeff Winger");

        await Command(new Replies(), root: root).RunAsync(["--show"], default);

        Assert.Contains($"runner:           Jeff Winger  ({RunnersPath})", Said);
    }

    [Fact]
    public async Task Show_says_HATCH_RUNNER_wins_when_it_is_set()
    {
        var root = Directory.CreateTempSubdirectory("hatch-config-root-").FullName;
        RunnerNames.Record.Set(RunnersPath, Checkout.Canonical(root), "Jeff Winger");

        await Command(
                new Replies(),
                environment: new Dictionary<string, string?>(StringComparer.Ordinal) { ["HATCH_RUNNER"] = "the-box" },
                root: root)
            .RunAsync(["--show"], default);

        Assert.Contains("runner:           the-box  (HATCH_RUNNER, exported)", Said);
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

    [Fact]
    public async Task Key_with_no_origin_yet_is_refused_and_nothing_is_written()
    {
        Assert.Equal(1, await Command(new Replies()).RunAsync(["--key", "hatch_ak_thesecret"], default));

        Assert.Contains("no origin is set yet", Complained);
        Assert.False(File.Exists(ConfigPath));
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
    public async Task Show_with_no_workspace_says_unset()
    {
        await Command(new Replies()).RunAsync(["--show"], default);
        Assert.Contains("HATCH_WORKSPACE:   <unset>", Said);
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

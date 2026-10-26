using System.Diagnostics;

namespace Hatch.Cli;

/// <summary>
/// Where Hatch is and how this machine reaches it, asked for once and written
/// where it follows the person rather than the checkout.
/// </summary>
/// <remarks>
/// <para>The one command that has to work before anything is configured, which
/// is why it is the one command <see cref="Settings.TryLoad"/> is not applied
/// to. It reads whatever is already there as its defaults, so changing one
/// setting is not an excuse to retype the others.</para>
///
/// <para>It writes <see cref="Settings.UserConfigPath"/> and nothing else. A
/// checkout's <c>scripts/.env</c> is still read, at higher precedence, for the
/// repository that wants to pin its own origin - but it is no longer written
/// from here, because a CLI installed once and run everywhere cannot keep its
/// settings inside one clone.</para>
/// </remarks>
/// <param name="Environment">
/// What is exported, so the write can leave the process holding the values it
/// just took - a <c>config</c> that could not then reach the board would be a
/// command that verifies nothing.
/// </param>
public sealed record ConfigCommand(
    Terminal Say,
    Input In,
    IDictionary<string, string?> Environment,
    string? CheckoutEnvFile,
    string RunnerName,
    string? Root = null,
    string? ProfileName = null,
    string? ProfilePath = null)
{
    public static readonly string[] ConfigUsage =
    [
        "usage: hatch config [--show | --origin <origin> | --key <key> | --runner <name>",
        "                     | --repo <path>... | --workspace <dir> | --tag <tag>...",
        "                     | --log on|<dir>|off | --dangerous on|off]",
        "                     [--here | --profile <name>]",
        "",
        "  hatch config                    asks for the origin, the key and the runner",
        "                                     name, and writes them",
        "  hatch config --show             says what is set, and which layer it came from",
        "  hatch config --origin <origin>  writes the origin alone, asking nothing",
        "  hatch config --key <key>        writes the key alone, asking nothing",
        "  hatch config --runner <name>    what the board calls this checkout, asking nothing",
        "  hatch config --repo <path>      also serve this checkout, repeatable",
        "  hatch config --repo             ...alone, clears the checkouts a loop with no",
        "                                     checkout of its own would otherwise serve",
        "  hatch config --workspace <dir>  clone every repository the board binds that",
        "                                     this runner has no checkout of, into <dir>",
        "  hatch config --workspace off    ...never clone: serve only the checkouts it has",
        "  hatch config --log on           keep a log, in the `logs` folder beside the per-user file",
        "  hatch config --log <dir>        ...keep it in <dir>, made if it is not there",
        "  hatch config --log off          ...keep none",
        "  hatch config --dangerous off    refuse work flagged dangerous",
        "  hatch config --dangerous on     ...take it, which is the default",
        "  hatch config --tag <tag>        advertise this tag, repeatable",
        "  hatch config --tag              ...alone, clears them",
        "",
        "  --origin is what Hatch's own Runner page hands a new machine to paste. It",
        "  leaves the key and everything else exactly as they were, so it is also how",
        "  an origin is changed without retyping a credential.",
        "",
        "  --key is what the API keys page's mint panel is for: paste the key you",
        "  just minted. It leaves the origin as it was, and needs one already set.",
        "  A key on a command line stays in shell history - the prompt above does",
        "  not, so prefer it on a machine other people can read.",
        "",
        "  --runner sets the name this checkout calls itself on the board - a",
        "  character from the cast list by default, chosen once and kept from then",
        "  on. Refused, with nothing written, if it is empty, too long, not plain",
        "  ASCII, or already the name of a live runner. Needs a checkout, and is",
        "  recorded for the active profile (or the one --profile names): a runner's",
        "  name is still a fact about one checkout, not a setting that follows the",
        "  person.",
        "",
        "  --repo writes HATCH_REPOS: the checkouts a loop with no checkout of its own",
        "  serves, beside whatever it is standing in. Each path is checked before the",
        "  write - it exists and carries a .git - and refused naming the path otherwise,",
        "  with nothing written. Leaves the origin, the key and everything else as they",
        "  were.",
        "",
        "  --workspace writes HATCH_WORKSPACE: a directory this runner owns entirely,",
        "  where it clones what the board binds and it has no checkout of. Refused when",
        "  it sits inside a named checkout or the checkout it contains one, in either",
        "  direction - a clone under a tree being reset (or a tree under a directory of",
        "  clones) is two owners of one path. With none written, the default is a",
        "  `workspace` directory beside the per-user file; `--workspace off` writes",
        "  `off`, which keeps this runner from cloning at all.",
        "",
        "  --log writes HATCH_LOG: where this runner's log goes. `on` is the default",
        "  folder, `off` (or nothing written) keeps no log, and a directory is made if",
        "  it does not exist, and refused with nothing written if it cannot be. The",
        "  setting is only recorded here; `on` is the `logs` folder beside the per-user",
        "  file.",
        "",
        "  --dangerous writes HATCH_DANGEROUS: `off` makes this runner refuse work flagged",
        "  dangerous - security testing, load testing, anything that can destabilise a",
        "  machine - and `on`, or nothing written, takes it. It seeds this runner's row",
        "  on the board once; after that the row is the board's to change, so writing it",
        "  at a checkout whose runner already has a row changes nothing there.",
        "",
        "  --tag writes HATCH_TAGS: the tags this runner advertises, so a project's",
        "  own runner tags can ask for one and only a runner carrying it is matched.",
        "  A tag is a word a project's runner tags ask for - macos, gpu - at most 8",
        "  on a runner, each at most 32 characters, lowercased, with no space or",
        "  comma. A tag that breaks that rule is refused, naming it, with nothing",
        "  written. Bare --tag clears the tags this runner advertises.",
        "",
        "  --here writes this checkout's scripts/.env instead of the per-user file -",
        "  combine it with any write above. Refused outside a checkout, with --runner",
        "  (a runner's name is already this checkout's), and with --show.",
        "",
        "  --profile <name> writes that profile's own file instead of the per-user",
        "  file - combine it with any write above. The file need not exist yet.",
        "  Refused with --here (two different places to write) and with an",
        "  invalid name.",
        "  `--show --profile <name>` reads the named profile as if it were active,",
        "  without switching to it.",
        "",
        "  Written to the per-user file, unless --here or --profile <name> says to",
        "  write that file instead - mode 600 where the platform has modes.",
        "  Read back highest-first: an exported variable, then scripts/.env in the",
        "  checkout you are standing in, then the active profile, then that file.",
        "",
        "  The key may be left empty for a Hatch running with its wall off, where",
        "  calls name themselves with a runner header instead. Never in a",
        "  repository, either way.",
    ];

    /// <summary>The per-user file. Named so a test can put one somewhere that is not the machine's own.</summary>
    public string ConfigPath { get; init; } = Settings.UserConfigPath();

    /// <summary>
    /// <see cref="RunnerNames.Record"/>'s file. Named so a test can put one
    /// somewhere that is not the machine's own.
    /// </summary>
    public string RunnersPath { get; init; } = RunnerNames.Record.DefaultPath();

    /// <summary>
    /// How the written settings are proved. Replaced by a test, which has no
    /// origin to reach.
    /// </summary>
    public Func<Settings, string, CancellationToken, Task<string?>> Probe { get; init; } =
        async (settings, runnerName, ct) =>
        {
            using var client = new HatchClient(settings, runnerName);
            var board = await new Board(client).BoardAsync(ct);
            return board is null ? null : Columns.Named(board.Statuses);
        };

    /// <summary>
    /// The name of every runner live on the board, so a typed name already
    /// spoken for is refused rather than silently colliding. Names alone: the
    /// names route answers any key, where the full listing answers only the
    /// scope it was given. Replaced by a test, which has no origin to reach.
    /// </summary>
    public Func<Settings, CancellationToken, Task<IReadOnlyList<string>>> LiveRunners { get; init; } =
        async (settings, ct) =>
        {
            using var client = new HatchClient(settings, "hatch-config");
            return await new Board(client).RunnerNamesAsync(ct);
        };

    /// <summary>
    /// Who <c>--show</c>'s own identity line names - a key or a runner, per
    /// <c>/api/hatch/me</c>. Replaced by a test, which has no origin to reach.
    /// </summary>
    public Func<Settings, string, CancellationToken, Task<MeDto?>> MeProbe { get; init; } =
        (settings, runnerName, ct) => new Board(new HatchClient(settings, runnerName)).MeAsync(ct);

    /// <summary>
    /// Whether <c>scripts/.env</c> is ignored by this checkout's own git - the
    /// one check <c>--here</c> makes before it ever writes there, so a file
    /// that is not ignored is never one <c>git add</c> could pick up by
    /// accident. Replaced by a test, which has no git to ask.
    /// </summary>
    public Func<string, bool> CheckoutIgnoresEnv { get; init; } = DefaultCheckoutIgnoresEnv;

    /// <summary>
    /// Resolves the active profile when no <c>--profile</c> names one -
    /// <see cref="Settings.TryResolveProfile"/> reads the machine's real
    /// pointer and files, so a test cannot stage them. Replaced by a test.
    /// </summary>
    public delegate bool ProfileResolver(
        string? requested, IDictionary<string, string?> environment,
        out string? name, out string? file, out string refusal);

    public ProfileResolver ResolveProfile { get; init; } = Settings.TryResolveProfile;

    private static bool DefaultCheckoutIgnoresEnv(string root)
    {
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add("check-ignore");
            start.ArgumentList.Add("-q");
            start.ArgumentList.Add(Path.Combine("scripts", ".env"));

            using var proc = Process.Start(start);
            if (proc is null) return false;

            proc.WaitForExit();
            return proc.ExitCode == 0;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            return false;
        }
    }

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        // Anywhere in the argument list, and stripped before anything below
        // ever sees it - so `--here --tag x` and `--tag x --here` both leave
        // the same `args` for the dispatch that follows.
        var here = args.Contains("--here");
        if (here) args = [.. args.Where(a => a != "--here")];

        if (ProfileName is not null && !Settings.ValidProfileName(ProfileName))
        {
            Say.Complain($"""hatch: "{ProfileName}" is not a valid profile name - letters, digits and hyphens only""");
            return 1;
        }

        if (here && ProfileName is not null)
        {
            Say.Complain("hatch: --here and --profile write to two different places - pick one");
            return 1;
        }

        if (Usage.Wanted(args)) return Usage.Print(Say, ConfigUsage);

        if (here && args is ["--show"])
            return Usage.Refuse(Say, "config --show takes no --here", ConfigUsage);

        if (here && args.Length > 0 && args[0] == "--runner")
        {
            Say.Complain("hatch: a runner's name is already this checkout's - --runner needs no --here");
            return 1;
        }

        if (here && Root is null)
        {
            Say.Complain("hatch: --here writes this checkout's scripts/.env - run it inside one");
            return 1;
        }

        if (here && !CheckoutIgnoresEnv(Root!))
        {
            Say.Complain(
                $"hatch: {CheckoutEnvFile} is not ignored by this checkout's git - add .env to " +
                ".gitignore before --here can write it");
            return 1;
        }

        if (args is ["--show"]) return await ShowAsync(ct);

        // Before the refusal below, and the one mode that asks nothing: it is
        // what the Runner page prints for a machine being set up, where the
        // origin is already known and typing it back in by hand is the step
        // that gets it wrong.
        if (args is ["--origin", var given]) return await OriginAsync(given, here, ct);

        if (args is ["--key", var givenKey]) return await KeyAsync(givenKey, here, ct);

        if (args.Length > 0 && args[0] == "--runner") return await RunnerAsync(args, ct);

        if (args.Length > 0 && args[0] == "--repo") return Repo(args, here);

        if (args.Length > 0 && args[0] == "--workspace") return Workspace(args, here);

        if (args.Length > 0 && args[0] == "--log") return Log(args, here);

        if (args.Length > 0 && args[0] == "--dangerous") return Dangerous(args, here);

        if (args.Length > 0 && args[0] == "--tag") return Tag(args, here);

        if (args.Length > 0)
            return Usage.Refuse(Say, "config takes nothing, --show, --origin <origin>, or --key <key>", ConfigUsage);

        if (!In.Interactive)
        {
            Say.Complain("hatch: config asks questions and needs a terminal");
            return 1;
        }

        var (overlayPath, overlay) = here ? (CheckoutEnvFile, true)
            : ProfileName is not null ? (ProfilePath, true)
            : ((string?)null, false);
        var fold = Settings.Layers(CheckoutEnvFile, Environment, ConfigPath, ProfilePath);
        var overlayOnly = Settings.ReadFile(overlayPath);
        string Carry(string name) => overlayOnly.GetValueOrDefault(name, "");

        // Whatever is loaded already is the default, so changing one setting is
        // not an excuse to retype the other. Under --here/--profile the bracket
        // still shows what is actually active (fold), but Enter alone keeps
        // only what this overlay's own file already carries - never a value
        // leaked in from an export or the per-user file.
        var originShown = fold("HATCH_BASE").Value ?? "";
        var origin = overlay ? Carry("HATCH_BASE") : originShown;
        var keyShown = fold("HATCH_KEY").Value ?? "";
        var key = overlay ? Carry("HATCH_KEY") : keyShown;
        var claudeBinShown = fold("HATCH_CLAUDE_BIN").Value ?? "";
        var claudeBin = overlay ? Carry("HATCH_CLAUDE_BIN") : claudeBinShown;
        var codexBinShown = fold("HATCH_CODEX_BIN").Value ?? "";
        var codexBin = overlay ? Carry("HATCH_CODEX_BIN") : codexBinShown;
        var grokBinShown = fold("HATCH_GROK_BIN").Value ?? "";
        var grokBin = overlay ? Carry("HATCH_GROK_BIN") : grokBinShown;
        var ollamaHostShown = fold("HATCH_OLLAMA_HOST").Value ?? "";
        var ollamaHost = overlay ? Carry("HATCH_OLLAMA_HOST") : ollamaHostShown;
        var ollamaTiers = overlay ? Carry("HATCH_OLLAMA_TIERS") : fold("HATCH_OLLAMA_TIERS").Value ?? "";
        var agentShown = fold("HATCH_AGENT").Value ?? "";
        var agent = overlay ? Carry("HATCH_AGENT") : agentShown;
        var repos = overlay ? Carry("HATCH_REPOS") : fold("HATCH_REPOS").Value ?? "";
        var workspace = overlay ? Carry("HATCH_WORKSPACE") : fold("HATCH_WORKSPACE").Value ?? "";
        var tags = overlay ? Carry("HATCH_TAGS") : fold("HATCH_TAGS").Value ?? "";
        var log = overlay ? Carry("HATCH_LOG") : fold("HATCH_LOG").Value ?? "";
        var dangerous = overlay ? Carry("HATCH_DANGEROUS") : fold("HATCH_DANGEROUS").Value ?? "";

        var target = overlayPath ?? ConfigPath;
        Say.Line($"Writing {target}. Enter keeps what is shown in brackets.");
        Say.Line("");

        In.Prompt($"Hatch origin [{(originShown.Length > 0 ? originShown : "https://hatch.<your domain>")}]: ");
        if (In.Line() is { Length: > 0 } typedOrigin) origin = typedOrigin;

        if (origin.Length == 0)
        {
            Say.Complain("hatch: an origin is required");
            return 1;
        }

        if (!HasScheme(origin)) return NoScheme(origin);

        // Read without echo: this is the one value on the screen that a
        // screenshot, a shoulder or a scrollback should not be able to keep.
        In.Prompt($"API key [{(keyShown.Length > 0 ? Mask(keyShown) : "hatch_ak_...")}]: ");
        if (In.Secret() is { Length: > 0 } typedKey) key = typedKey;

        if (key.Length == 0)
            Say.Complain(
                $"hatch: no key - calls will name themselves \"{RunnerName}\", " +
                "which only a Hatch with its wall off reads.");
        else if (!key.StartsWith("hatch_ak_", StringComparison.Ordinal))
            Say.Complain("hatch: warning - that does not start with hatch_ak_. Carrying on; the call below will say.");

        In.Prompt($"agent ({string.Join(", ", Settings.KnownAgentIds)}), for `work` [{(agentShown.Length > 0 ? agentShown : "claude")}]: ");
        if (In.Line() is { Length: > 0 } typedAgent) agent = typedAgent;

        // Only the binary the chosen agent needs: asking for all of them every
        // time would be a question per agent installed nowhere. The others keep
        // whatever they already held, and are written back as they were.
        switch (agent.Length > 0 ? agent : "claude")
        {
            case "codex":
                In.Prompt($"codex CLI path, for `work` [{(codexBinShown.Length > 0 ? codexBinShown : "on PATH")}]: ");
                if (In.Line() is { Length: > 0 } typedCodexBin) codexBin = typedCodexBin;
                break;
            case "ollama":
                In.Prompt($"codex CLI path, for `work` [{(codexBinShown.Length > 0 ? codexBinShown : "on PATH")}]: ");
                if (In.Line() is { Length: > 0 } typedOllamaCodexBin) codexBin = typedOllamaCodexBin;

                In.Prompt($"Ollama host [{(ollamaHostShown.Length > 0 ? ollamaHostShown : OllamaHost.Default.Url)}]: ");
                if (In.Line() is { Length: > 0 } typedOllamaHost) ollamaHost = typedOllamaHost;
                break;
            case "grok":
                In.Prompt($"grok CLI path, for `work` [{(grokBinShown.Length > 0 ? grokBinShown : "on PATH")}]: ");
                if (In.Line() is { Length: > 0 } typedGrokBin) grokBin = typedGrokBin;
                break;
            default:
                In.Prompt($"claude CLI path, for `work` [{(claudeBinShown.Length > 0 ? claudeBinShown : "on PATH")}]: ");
                if (In.Line() is { Length: > 0 } typedBin) claudeBin = typedBin;
                break;
        }

        // Only in a checkout - a runner's name is a fact about one of them, and
        // there is nothing here to name outside of one. RunnerName is already
        // this checkout's chosen or recorded name (or HATCH_RUNNER's override),
        // so Enter alone changes nothing and writes nothing new.
        string? chosenRunner = null;
        if (Root is { Length: > 0 })
        {
            In.Prompt($"What should the board call this runner? [{RunnerName}]: ");
            var typedRunner = In.Line();

            if (typedRunner is { Length: > 0 })
            {
                var candidate = typedRunner.Trim();
                if (RunnerProblem(candidate) is { } problem)
                {
                    Say.Complain($"hatch: {problem}");
                    return 1;
                }

                if (await IsLiveAsync(candidate, origin, key, ct))
                {
                    Say.Complain($"hatch: {candidate} is already a live runner on this board - pick another name");
                    return 1;
                }

                chosenRunner = candidate;
            }
        }

        Write(
            origin, key, claudeBin, codexBin, repos, workspace, agent, grokBin, tags,
            ollamaHost: ollamaHost, ollamaTiers: ollamaTiers, log: log, dangerous: dangerous,
            path: overlayPath, overlay: overlay, mode: "");

        if (chosenRunner is not null) RunnerNames.Record.Set(RunnersPath, null, Checkout.Canonical(Root!), chosenRunner);

        // Exported, not just set: this process is about to make the call below,
        // and a `work` spawned from here should not have to find the file again.
        Environment["HATCH_BASE"] = origin;
        Environment["HATCH_KEY"] = key;
        if (claudeBin.Length > 0) Environment["HATCH_CLAUDE_BIN"] = claudeBin;
        if (codexBin.Length > 0) Environment["HATCH_CODEX_BIN"] = codexBin;
        if (grokBin.Length > 0) Environment["HATCH_GROK_BIN"] = grokBin;
        if (ollamaHost.Length > 0) Environment["HATCH_OLLAMA_HOST"] = ollamaHost;
        if (ollamaTiers.Length > 0) Environment["HATCH_OLLAMA_TIERS"] = ollamaTiers;
        if (agent.Length > 0) Environment["HATCH_AGENT"] = agent;

        Say.Line("");
        Say.Line($"wrote {target}");

        return await ProveAsync(new Settings { Base = origin.TrimEnd('/'), Key = key }, ct);
    }

    /// <summary>
    /// The origin alone, taken from the command line and written without a
    /// question being asked.
    /// </summary>
    /// <remarks>
    /// <para>The one mode a script - or a person pasting what the Runner page
    /// printed - can use, and the reason it exists: the origin is the one
    /// setting Hatch already knows about itself, so a new machine should not
    /// have to be told it by hand.</para>
    ///
    /// <para>Everything else on disk is read back through the same three layers
    /// and written out again unchanged, so this changes an origin rather than
    /// replacing a configuration. In particular a key that is already set stays
    /// set - a command that silently blanked a credential would be a worse way
    /// to lose one than forgetting it.</para>
    ///
    /// <para>Same refusals and same success sentence as the interactive path,
    /// because they are the same two helpers.</para>
    /// </remarks>
    private async Task<int> OriginAsync(string origin, bool here, CancellationToken ct)
    {
        origin = origin.Trim();

        if (origin.Length == 0)
        {
            Say.Complain("hatch: an origin is required");
            return 1;
        }

        if (!HasScheme(origin)) return NoScheme(origin);

        var (overlayPath, overlay) = here ? (CheckoutEnvFile, true)
            : ProfileName is not null ? (ProfilePath, true)
            : ((string?)null, false);
        var fold = Settings.Layers(CheckoutEnvFile, Environment, ConfigPath, ProfilePath);
        var overlayOnly = Settings.ReadFile(overlayPath);
        string Field(string name) => overlay ? overlayOnly.GetValueOrDefault(name, "") : fold(name).Value ?? "";

        // The key is a field this call does not touch - carried forward as
        // this overlay's own line under --here/--profile, never one leaked in
        // from an export or the per-user file.
        var key = Field("HATCH_KEY");
        var claudeBin = Field("HATCH_CLAUDE_BIN");
        var codexBin = Field("HATCH_CODEX_BIN");
        var grokBin = Field("HATCH_GROK_BIN");
        var ollamaHost = Field("HATCH_OLLAMA_HOST");
        var ollamaTiers = Field("HATCH_OLLAMA_TIERS");
        var agent = Field("HATCH_AGENT");
        var repos = Field("HATCH_REPOS");
        var workspace = Field("HATCH_WORKSPACE");
        var tags = Field("HATCH_TAGS");
        var log = Field("HATCH_LOG");
        var dangerous = Field("HATCH_DANGEROUS");

        var target = overlayPath ?? ConfigPath;
        Write(
            origin, key, claudeBin, codexBin, repos, workspace, agent, grokBin, tags,
            ollamaHost: ollamaHost, ollamaTiers: ollamaTiers, log: log, dangerous: dangerous,
            path: overlayPath, overlay: overlay, mode: "--origin");

        // Rebuilt after the write and before HATCH_BASE is exported: `fold`
        // above still reads the file as it stood before Write, and exporting
        // the origin first would make Environment the origin's own layer,
        // dropping every file key the pairing rule (HA-1479) would otherwise
        // honour.
        var freshFold = Settings.Layers(CheckoutEnvFile, Environment, ConfigPath, ProfilePath);

        // What a real subsequent load resolves the key to - not just this
        // checkout's own line, so a key already set at the user layer is not
        // reported missing just because --here carries nothing of its own.
        var keyForProbe = freshFold("HATCH_KEY").Value ?? "";

        Environment["HATCH_BASE"] = origin;

        Say.Line($"wrote {target}");

        if (keyForProbe.Length == 0)
            Say.Complain(
                $"hatch: no key - calls will name themselves \"{RunnerName}\", " +
                "which only a Hatch with its wall off reads.");

        return await ProveAsync(new Settings { Base = origin.TrimEnd('/'), Key = keyForProbe }, ct);
    }

    /// <summary>
    /// The key alone, taken from the command line and written without a
    /// question being asked. The mirror of <see cref="OriginAsync"/>: the
    /// origin and everything else are carried forward unchanged.
    /// </summary>
    /// <remarks>
    /// Needs an origin already, because a file with a key and no origin proves
    /// nothing and every later call would fail on the missing half. An empty
    /// key is refused rather than written - blanking a credential is what the
    /// interactive prompt is for, where it is deliberate.
    /// </remarks>
    private async Task<int> KeyAsync(string key, bool here, CancellationToken ct)
    {
        key = key.Trim();

        if (key.Length == 0)
        {
            Say.Complain("hatch: a key is required");
            return 1;
        }

        var (overlayPath, overlay) = here ? (CheckoutEnvFile, true)
            : ProfileName is not null ? (ProfilePath, true)
            : ((string?)null, false);
        var fold = Settings.Layers(CheckoutEnvFile, Environment, ConfigPath, ProfilePath);
        var overlayOnly = Settings.ReadFile(overlayPath);
        string Field(string name) => overlay ? overlayOnly.GetValueOrDefault(name, "") : fold(name).Value ?? "";

        // What a real subsequent load resolves the origin to - checked and
        // proved against this, even under --here/--profile, where the overlay
        // file may carry none of its own and fall through to the user layer.
        var (originValueForProbe, originLayer) = fold("HATCH_BASE");
        var originForProbe = originValueForProbe ?? "";
        if (originForProbe.Length == 0)
        {
            Say.Complain("hatch: no origin is set yet - run `hatch config --origin <origin>` first");
            return 1;
        }

        // The layer this write is about to land in - read before the write, so
        // the warning below describes the file as it stood when the key was
        // typed. A key that lands anywhere else is one the pairing rule (HA-1479)
        // will not send: it only reads HATCH_KEY from the layer HATCH_BASE came
        // from.
        var writingLayer = here ? Settings.Layer.Checkout
            : ProfileName is not null ? Settings.Layer.Profile
            : Settings.Layer.User;
        if (originLayer != writingLayer)
            Say.Complain($"hatch: that key will not be sent - HATCH_BASE comes from {Where(originLayer)}. Set both there, or neither.");

        var origin = Field("HATCH_BASE");
        var claudeBin = Field("HATCH_CLAUDE_BIN");
        var codexBin = Field("HATCH_CODEX_BIN");
        var grokBin = Field("HATCH_GROK_BIN");
        var ollamaHost = Field("HATCH_OLLAMA_HOST");
        var ollamaTiers = Field("HATCH_OLLAMA_TIERS");
        var agent = Field("HATCH_AGENT");
        var repos = Field("HATCH_REPOS");
        var workspace = Field("HATCH_WORKSPACE");
        var tags = Field("HATCH_TAGS");
        var log = Field("HATCH_LOG");
        var dangerous = Field("HATCH_DANGEROUS");

        if (!key.StartsWith("hatch_ak_", StringComparison.Ordinal))
            Say.Complain("hatch: warning - that does not start with hatch_ak_. Carrying on; the call below will say.");

        var target = overlayPath ?? ConfigPath;
        Write(
            origin, key, claudeBin, codexBin, repos, workspace, agent, grokBin, tags,
            ollamaHost: ollamaHost, ollamaTiers: ollamaTiers, log: log, dangerous: dangerous,
            path: overlayPath, overlay: overlay, mode: "--key");

        Environment["HATCH_KEY"] = key;

        Say.Line($"wrote {target}");

        return await ProveAsync(new Settings { Base = originForProbe.TrimEnd('/'), Key = key }, ct);
    }

    /// <summary>
    /// This checkout's name, taken from the command line and written without a
    /// question being asked - the mirror of <see cref="OriginAsync"/> and
    /// <see cref="KeyAsync"/>, except that there is nothing else to carry
    /// forward: a runner's name is the one thing this writes.
    /// </summary>
    /// <remarks>
    /// A purely local filesystem fact once it is chosen, like <see cref="Repo"/> -
    /// but choosing it needs the board, to skip a name already live,
    /// so this is async where <see cref="Repo"/> is not.
    /// </remarks>
    private async Task<int> RunnerAsync(string[] args, CancellationToken ct)
    {
        if (args is not ["--runner", var name])
            return Usage.Refuse(Say, "config --runner takes one name", ConfigUsage);

        if (Root is not { Length: > 0 } root)
        {
            Say.Complain("hatch: --runner names this checkout - run it inside one");
            return 1;
        }

        var candidate = name.Trim();
        if (RunnerProblem(candidate) is { } problem)
        {
            Say.Complain($"hatch: {problem}");
            return 1;
        }

        // An explicit --profile names the profile outright; otherwise it is the
        // active one. A dangling pointer is refused rather than falling through
        // to the bare key, which Checkout.RunnerAsync would not read under it.
        string? profile;
        string? profileFile;
        if (ProfileName is not null)
        {
            profile = ProfileName;
            profileFile = ProfilePath;
        }
        else if (!ResolveProfile(null, Environment, out profile, out profileFile, out var refusal))
        {
            Say.Complain(refusal);
            return 1;
        }

        var fold = Settings.Layers(CheckoutEnvFile, Environment, ConfigPath, profileFile);
        var origin = fold("HATCH_BASE").Value ?? "";
        var key = fold("HATCH_KEY").Value ?? "";

        if (await IsLiveAsync(candidate, origin, key, ct))
        {
            Say.Complain($"hatch: {candidate} is already a live runner on this board - pick another name");
            return 1;
        }

        RunnerNames.Record.Set(RunnersPath, profile, Checkout.Canonical(root), candidate);

        Say.Line($"wrote {RunnersPath}");
        return 0;
    }

    /// <summary>
    /// What is wrong with a typed name, or null. The same four bounds the
    /// server would eventually judge a heartbeat by - checked here so a bad
    /// name is a sentence at the prompt rather than a 409 at the next beat.
    /// </summary>
    private static string? RunnerProblem(string name) => name switch
    {
        { Length: 0 } => "a name is required",
        _ when name.Length > ClaimRequest.MaxRunnerLength =>
            $"a name is at most {ClaimRequest.MaxRunnerLength} characters",
        _ when !name.All(c => c < 128) => "a name is plain ASCII",
        _ => null,
    };

    /// <summary>
    /// Whether the board already has a live runner by this name. This checkout's
    /// own current name never counts - it is already that runner, and the
    /// server's 409 stays the backstop - and neither does a board that cannot
    /// be reached, the same read <see cref="Checkout.RunnerAsync"/> makes of one.
    /// </summary>
    private async Task<bool> IsLiveAsync(string name, string origin, string key, CancellationToken ct)
    {
        if (origin.Length == 0) return false;
        if (string.Equals(name, RunnerName, StringComparison.OrdinalIgnoreCase)) return false;

        try
        {
            var live = await LiveRunners(new Settings { Base = origin.TrimEnd('/'), Key = key }, ct);
            return live.Contains(name, StringComparer.OrdinalIgnoreCase);
        }
        catch (HatchException)
        {
            return false;
        }
    }

    /// <summary>
    /// The checkouts a loop with no checkout of its own serves, taken from the
    /// command line and written without a question being asked - the mirror of
    /// <see cref="OriginAsync"/> and <see cref="KeyAsync"/>: everything else is
    /// carried forward unchanged.
    /// </summary>
    /// <remarks>
    /// A purely local filesystem fact, unlike the origin and the key - so there
    /// is no <see cref="ProveAsync"/> call here, and no requirement that an
    /// origin already be set.
    /// </remarks>
    private int Repo(string[] args, bool here)
    {
        var paths = new List<string>();

        // A bare --repo is the one shape with an odd count: it clears rather
        // than pairing with a path. Anything else - a trailing bare --repo, two
        // paths with no --repo between them - is shaped wrong and refused.
        if (args is not ["--repo"])
        {
            for (var i = 0; i < args.Length; i += 2)
            {
                if (args[i] != "--repo" || i + 1 >= args.Length)
                    return Usage.Refuse(
                        Say, "config --repo takes one or more paths, each after its own --repo", ConfigUsage);

                paths.Add(args[i + 1]);
            }
        }

        // Checked before anything is written - the first bad one refuses
        // naming it, and nothing changes on disk.
        foreach (var path in paths)
        {
            if (!Checkouts.IsCheckout(path, out var refusal))
            {
                Say.Complain(refusal);
                return 1;
            }
        }

        var (overlayPath, overlay) = here ? (CheckoutEnvFile, true)
            : ProfileName is not null ? (ProfilePath, true)
            : ((string?)null, false);
        var fold = Settings.Layers(CheckoutEnvFile, Environment, ConfigPath, ProfilePath);
        var overlayOnly = Settings.ReadFile(overlayPath);
        string Field(string name) => overlay ? overlayOnly.GetValueOrDefault(name, "") : fold(name).Value ?? "";

        var origin = Field("HATCH_BASE");
        var key = Field("HATCH_KEY");
        var claudeBin = Field("HATCH_CLAUDE_BIN");
        var codexBin = Field("HATCH_CODEX_BIN");
        var grokBin = Field("HATCH_GROK_BIN");
        var ollamaHost = Field("HATCH_OLLAMA_HOST");
        var ollamaTiers = Field("HATCH_OLLAMA_TIERS");
        var agent = Field("HATCH_AGENT");
        var workspace = Field("HATCH_WORKSPACE");
        var tags = Field("HATCH_TAGS");
        var log = Field("HATCH_LOG");
        var dangerous = Field("HATCH_DANGEROUS");

        // The reverse of the check `--workspace` makes: a checkout named here
        // that sits inside the workspace already configured is the same two
        // owners of one path, the other way round.
        if (workspace.Length > 0 && !Settings.IsWorkspaceOff(workspace)
            && !Checkouts.OverlapsCheckout(workspace, paths, out var workspaceOverlap))
        {
            Say.Complain(workspaceOverlap);
            return 1;
        }

        var target = overlayPath ?? ConfigPath;
        Write(
            origin, key, claudeBin, codexBin, string.Join(Path.PathSeparator, paths), workspace, agent, grokBin, tags,
            ollamaHost: ollamaHost, ollamaTiers: ollamaTiers, log: log, dangerous: dangerous,
            path: overlayPath, overlay: overlay, mode: "--repo");

        Environment["HATCH_REPOS"] = paths.Count > 0 ? string.Join(Path.PathSeparator, paths) : null;

        Say.Line($"wrote {target}");
        return 0;
    }

    /// <summary>
    /// The directory this runner clones what the board binds into, taken from
    /// the command line and written without a question being asked - the
    /// mirror of <see cref="Repo"/>: everything else is carried forward
    /// unchanged.
    /// </summary>
    /// <remarks>
    /// A single value rather than a list, so there is no bare-flag-clears shape
    /// here the way there is for <c>--repo</c> - the ticket asks for a
    /// directory set, not one that can also be taken off from the command
    /// line.
    /// </remarks>
    private int Workspace(string[] args, bool here)
    {
        if (args is not ["--workspace", var dir])
            return Usage.Refuse(Say, "config --workspace takes one directory", ConfigUsage);

        var (overlayPath, overlay) = here ? (CheckoutEnvFile, true)
            : ProfileName is not null ? (ProfilePath, true)
            : ((string?)null, false);
        var fold = Settings.Layers(CheckoutEnvFile, Environment, ConfigPath, ProfilePath);
        var overlayOnly = Settings.ReadFile(overlayPath);
        string Field(string name) => overlay ? overlayOnly.GetValueOrDefault(name, "") : fold(name).Value ?? "";

        var origin = Field("HATCH_BASE");
        var key = Field("HATCH_KEY");
        var claudeBin = Field("HATCH_CLAUDE_BIN");
        var codexBin = Field("HATCH_CODEX_BIN");
        var grokBin = Field("HATCH_GROK_BIN");
        var ollamaHost = Field("HATCH_OLLAMA_HOST");
        var ollamaTiers = Field("HATCH_OLLAMA_TIERS");
        var agent = Field("HATCH_AGENT");
        var reposRaw = Field("HATCH_REPOS");
        var tags = Field("HATCH_TAGS");
        var log = Field("HATCH_LOG");
        var dangerous = Field("HATCH_DANGEROUS");
        var repos = reposRaw is { Length: > 0 }
            ? reposRaw.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

        var target = overlayPath ?? ConfigPath;

        if (Settings.IsWorkspaceOff(dir))
        {
            // A word and not a directory: nothing to overlap and nothing to make.
            Write(
                origin, key, claudeBin, codexBin, reposRaw, Settings.WorkspaceOff, agent, grokBin, tags,
                ollamaHost: ollamaHost, ollamaTiers: ollamaTiers, log: log, dangerous: dangerous,
                path: overlayPath, overlay: overlay, mode: "--workspace");
            Environment["HATCH_WORKSPACE"] = Settings.WorkspaceOff;
            Say.Line($"wrote {target}");
            return 0;
        }

        var others = Root is null ? repos : (IReadOnlyList<string>)[Root, .. repos];
        if (!Checkouts.OverlapsCheckout(dir, others, out var overlapRefusal))
        {
            Say.Complain(overlapRefusal);
            return 1;
        }

        if (!Checkouts.EnsureWorkspace(dir, out var refusal))
        {
            Say.Complain(refusal);
            return 1;
        }

        Write(
            origin, key, claudeBin, codexBin, reposRaw, dir, agent, grokBin, tags,
            ollamaHost: ollamaHost, ollamaTiers: ollamaTiers, log: log, dangerous: dangerous,
            path: overlayPath, overlay: overlay, mode: "--workspace");

        Environment["HATCH_WORKSPACE"] = dir;

        Say.Line($"wrote {target}");
        return 0;
    }

    /// <summary>
    /// Where this runner's log goes, taken from the command line and written
    /// without a question being asked - the mirror of <see cref="Workspace"/>:
    /// everything else is carried forward unchanged. Only the setting is
    /// recorded; nothing reads it yet.
    /// </summary>
    private int Log(string[] args, bool here)
    {
        if (args is not ["--log", var value])
            return Usage.Refuse(Say, "config --log takes on, off, or one directory", ConfigUsage);

        var (overlayPath, overlay) = here ? (CheckoutEnvFile, true)
            : ProfileName is not null ? (ProfilePath, true)
            : ((string?)null, false);
        var fold = Settings.Layers(CheckoutEnvFile, Environment, ConfigPath, ProfilePath);
        var overlayOnly = Settings.ReadFile(overlayPath);
        string Field(string name) => overlay ? overlayOnly.GetValueOrDefault(name, "") : fold(name).Value ?? "";

        var origin = Field("HATCH_BASE");
        var key = Field("HATCH_KEY");
        var claudeBin = Field("HATCH_CLAUDE_BIN");
        var codexBin = Field("HATCH_CODEX_BIN");
        var grokBin = Field("HATCH_GROK_BIN");
        var ollamaHost = Field("HATCH_OLLAMA_HOST");
        var ollamaTiers = Field("HATCH_OLLAMA_TIERS");
        var agent = Field("HATCH_AGENT");
        var repos = Field("HATCH_REPOS");
        var workspace = Field("HATCH_WORKSPACE");
        var tags = Field("HATCH_TAGS");
        var dangerous = Field("HATCH_DANGEROUS");

        var target = overlayPath ?? ConfigPath;
        var written = value.Trim();

        // A word and not a directory: nothing to make. A folder is made before
        // anything is written, so a path that cannot be one leaves the file as it was.
        if (Settings.IsLogOff(written)) written = Settings.LogOff;
        else
        {
            var dir = Settings.IsLogOn(written) ? Settings.DefaultLogPath() : written;
            if (dir.Length == 0)
                return Usage.Refuse(Say, "config --log takes on, off, or one directory", ConfigUsage);

            if (Settings.IsLogOn(written)) written = Settings.LogOn;

            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException
                or ArgumentException)
            {
                Say.Complain($"hatch: cannot make the log folder {dir} - {e.Message}");
                return 1;
            }
        }

        Write(
            origin, key, claudeBin, codexBin, repos, workspace, agent, grokBin, tags,
            ollamaHost: ollamaHost, ollamaTiers: ollamaTiers, log: written, dangerous: dangerous,
            path: overlayPath, overlay: overlay, mode: "--log");

        Environment["HATCH_LOG"] = written;

        Say.Line($"wrote {target}");
        return 0;
    }

    /// <summary>
    /// Whether this runner refuses work flagged dangerous, taken from the command
    /// line and written without a question being asked - the mirror of
    /// <see cref="Log"/>: everything else is carried forward unchanged. Only the
    /// setting is recorded; a runner whose row already exists keeps the board's
    /// answer, and the setting seeds a row the board has not got yet.
    /// </summary>
    private int Dangerous(string[] args, bool here)
    {
        const string Refusal = "config --dangerous takes on or off";
        if (args is not ["--dangerous", var value])
            return Usage.Refuse(Say, Refusal, ConfigUsage);

        var written = value.Trim().ToLowerInvariant();
        if (written != Settings.DangerousOff && written != Settings.DangerousOn)
            return Usage.Refuse(Say, Refusal, ConfigUsage);

        var (overlayPath, overlay) = here ? (CheckoutEnvFile, true)
            : ProfileName is not null ? (ProfilePath, true)
            : ((string?)null, false);
        var fold = Settings.Layers(CheckoutEnvFile, Environment, ConfigPath, ProfilePath);
        var overlayOnly = Settings.ReadFile(overlayPath);
        string Field(string name) => overlay ? overlayOnly.GetValueOrDefault(name, "") : fold(name).Value ?? "";

        var origin = Field("HATCH_BASE");
        var key = Field("HATCH_KEY");
        var claudeBin = Field("HATCH_CLAUDE_BIN");
        var codexBin = Field("HATCH_CODEX_BIN");
        var grokBin = Field("HATCH_GROK_BIN");
        var ollamaHost = Field("HATCH_OLLAMA_HOST");
        var ollamaTiers = Field("HATCH_OLLAMA_TIERS");
        var agent = Field("HATCH_AGENT");
        var repos = Field("HATCH_REPOS");
        var workspace = Field("HATCH_WORKSPACE");
        var tags = Field("HATCH_TAGS");
        var log = Field("HATCH_LOG");

        var target = overlayPath ?? ConfigPath;
        Write(
            origin, key, claudeBin, codexBin, repos, workspace, agent, grokBin, tags,
            ollamaHost: ollamaHost, ollamaTiers: ollamaTiers, log: log, dangerous: written,
            path: overlayPath, overlay: overlay, mode: "--dangerous");

        Environment["HATCH_DANGEROUS"] = written;

        Say.Line($"wrote {target}");
        return 0;
    }

    /// <summary>
    /// The tags this runner advertises, taken from the command line and
    /// written without a question being asked - the mirror of <see cref="Repo"/>:
    /// everything else is carried forward unchanged.
    /// </summary>
    /// <remarks>
    /// Needs no <see cref="Root"/> of its own - only --here asks for one - and
    /// unlike <see cref="OriginAsync"/> it needs no origin and makes no call -
    /// there is nothing here a board could refuse.
    /// </remarks>
    private int Tag(string[] args, bool here)
    {
        var values = new List<string>();

        // A bare --tag is the one shape with an odd count: it clears rather
        // than pairing with a value. Anything else - a trailing bare --tag, two
        // values with no --tag between them - is shaped wrong and refused.
        if (args is not ["--tag"])
        {
            for (var i = 0; i < args.Length; i += 2)
            {
                if (args[i] != "--tag" || i + 1 >= args.Length)
                    return Usage.Refuse(
                        Say, "config --tag takes one or more tags, each after its own --tag", ConfigUsage);

                values.Add(args[i + 1]);
            }
        }

        // Checked before anything is written, and against the typed values
        // rather than a comma-split of them - a `--tag a,b` is refused by the
        // same rule that refuses a comma inside one tag, not split into two.
        if (RunnerTags.Normalise(values, out var tags) is { } sentence)
        {
            Say.Complain($"hatch: {sentence}");
            return 1;
        }

        var (overlayPath, overlay) = here ? (CheckoutEnvFile, true)
            : ProfileName is not null ? (ProfilePath, true)
            : ((string?)null, false);
        var fold = Settings.Layers(CheckoutEnvFile, Environment, ConfigPath, ProfilePath);
        var overlayOnly = Settings.ReadFile(overlayPath);
        string Field(string name) => overlay ? overlayOnly.GetValueOrDefault(name, "") : fold(name).Value ?? "";

        var origin = Field("HATCH_BASE");
        var key = Field("HATCH_KEY");
        var claudeBin = Field("HATCH_CLAUDE_BIN");
        var codexBin = Field("HATCH_CODEX_BIN");
        var grokBin = Field("HATCH_GROK_BIN");
        var ollamaHost = Field("HATCH_OLLAMA_HOST");
        var ollamaTiers = Field("HATCH_OLLAMA_TIERS");
        var agent = Field("HATCH_AGENT");
        var reposRaw = Field("HATCH_REPOS");
        var workspace = Field("HATCH_WORKSPACE");
        var log = Field("HATCH_LOG");
        var dangerous = Field("HATCH_DANGEROUS");

        var target = overlayPath ?? ConfigPath;
        Write(
            origin, key, claudeBin, codexBin, reposRaw, workspace, agent, grokBin, string.Join(',', tags),
            ollamaHost: ollamaHost, ollamaTiers: ollamaTiers, log: log, dangerous: dangerous,
            path: overlayPath, overlay: overlay, mode: "--tag");

        Environment["HATCH_TAGS"] = tags.Count > 0 ? string.Join(',', tags) : null;

        Say.Line($"wrote {target}");
        return 0;
    }

    /// <summary>An origin with no scheme is an origin every call will fail on, so it is caught here rather than at the first request.</summary>
    private static bool HasScheme(string origin) =>
        origin.StartsWith("http://", StringComparison.Ordinal) ||
        origin.StartsWith("https://", StringComparison.Ordinal);

    private int NoScheme(string origin)
    {
        Say.Complain($"hatch: \"{origin}\" has no scheme - every call will fail. Write it as https://...");
        return 1;
    }

    /// <summary>
    /// The written settings, proved by using them.
    /// </summary>
    /// <remarks>
    /// Called after the file is written, on purpose: a key that is refused is
    /// worth keeping on disk to fix, and the message below says what to fix.
    /// </remarks>
    private async Task<int> ProveAsync(Settings settings, CancellationToken ct)
    {
        try
        {
            var columns = await Probe(settings, RunnerName, ct);
            Say.Line($"reached {settings.Base} - columns: {columns}");
            return 0;
        }
        catch (HatchException e)
        {
            Say.Complain(e.Message);
            Say.Complain("hatch: the file is written but that call did not go through - fix it and run config again.");
            return 1;
        }
    }

    /// <summary>Formats <see cref="MeProbe"/>'s answer for the <c>identity:</c> line - the key itself is never printed, only its name.</summary>
    private static string Identity(MeDto? me) => me switch
    {
        { Kind: "runner" } => $"calls go out as \"{me.Name}\"",
        { Kind: "key", Owner: null } => $"key \"{me.Name}\"",
        { Kind: "key" } => $"key \"{me.Name}\", owned by {me.Owner}, scopes: {string.Join(", ", me.Scopes ?? [])}",
        _ => "<unreachable>",
    };

    /// <summary>What is set, and which of the three layers it came from.</summary>
    private async Task<int> ShowAsync(CancellationToken ct)
    {
        var (fold, ignoredKeyLayer, ignoredRunnerLayer) = Settings.LayersWithNotes(CheckoutEnvFile, Environment, ConfigPath, ProfilePath);
        var exists = File.Exists(ConfigPath) ? "" : " (does not exist yet)";

        Say.Line($"file:             {ConfigPath}{exists}");
        Say.Line(CheckoutEnvFile is { Length: > 0 } checkoutEnvFile
            ? $"checkout:         {checkoutEnvFile}  (written by --here)"
            : "checkout:         <not in one>");

        // The profile the runner: and HATCH_WORKSPACE: lines below belong to -
        // the same name the profile: line prints, null for "none".
        string? profileName;
        if (ProfileName is not null)
        {
            profileName = ProfileName;
            Say.Line($"profile:          {ProfileName}  ({ProfilePath})");
        }
        else if (ResolveProfile(null, Environment, out var activeName, out var activeFile, out _))
        {
            profileName = activeName is { Length: > 0 } ? activeName : null;
            Say.Line(activeName is { Length: > 0 }
                ? $"profile:          {activeName}  ({activeFile})"
                : "profile:          <none>");
        }
        else
        {
            profileName = activeName;
            Say.Line($"profile:          {activeName}  (does not exist)");
        }

        var profileSuffix = $"  (profile: {profileName ?? "<none>"})";

        var runnerFold = fold("HATCH_RUNNER");
        if (runnerFold.Value is { Length: > 0 } explicitRunner)
            Say.Line($"runner:           {explicitRunner}  (HATCH_RUNNER, {Where(runnerFold.From)})");
        else if (Root is { Length: > 0 } root)
        {
            var recorded = RunnerNames.Record.Read(RunnersPath)
                .GetValueOrDefault(RunnerNames.Record.Key(profileName, Checkout.Canonical(root)));
            Say.Line(recorded is { Length: > 0 }
                ? $"runner:           {recorded}  ({RunnersPath}){profileSuffix}"
                : $"runner:           <none yet - chosen the next time this checkout speaks to a board>{profileSuffix}");
        }
        else
            Say.Line("runner:           <not in a checkout>");

        var origin = fold("HATCH_BASE").Value;
        var key = fold("HATCH_KEY").Value ?? "";

        if (string.IsNullOrEmpty(origin))
            Say.Line("identity:         <unreachable>");
        else
        {
            try
            {
                var me = await MeProbe(new Settings { Base = origin.TrimEnd('/'), Key = key }, RunnerName, ct);
                Say.Line($"identity:         {Identity(me)}");
            }
            catch (HatchException)
            {
                Say.Line("identity:         <unreachable>");
            }
        }

        // Environment-only, like HATCH_HEARTBEAT beside it - never a line in
        // FileNames, and so never a candidate for the loop below - but worth
        // seeing here regardless, since it is the number a deploy is measured
        // against.
        var retryFold = fold("HATCH_RETRY_SECONDS");
        var retryWhere = retryFold.From == Settings.Layer.Unset ? "  (default)" : $"  ({Where(retryFold.From)})";
        var retrySeconds = retryFold.Value is { Length: > 0 } retryRaw && int.TryParse(retryRaw, out var parsedRetry) && parsedRetry >= 0
            ? parsedRetry
            : 90;

        // Its own name is already longer than the column every other line
        // pads to, so this is set apart with two spaces rather than forced
        // into alignment with the rest.
        Say.Line($"HATCH_RETRY_SECONDS:  {retrySeconds}{retryWhere}");

        foreach (var name in Settings.FileNames)
        {
            var (value, from) = fold(name);
            var where = from == Settings.Layer.Unset ? "" : $"  ({Where(from)})";

            // Two notes, independent of each other and of `where`: a key the
            // pairing rule (HA-1479) drops regardless of what this layer's own
            // line shows, and a board-bound name (HA-1480) the active profile
            // kept the per-user file out of reach of.
            var note = "";
            if (name == "HATCH_KEY" && ignoredKeyLayer() is { } ignored)
                note += $"  (ignored: {Where(ignored)} holds a key, but HATCH_BASE comes from " +
                         $"{Where(fold("HATCH_BASE").From)} - set both there, or neither)";
            if (name == "HATCH_RUNNER" && ignoredRunnerLayer() is { } ignoredRunner)
                note += $"  (ignored: {Where(ignoredRunner)} holds a name, but a runner's name belongs to a checkout - " +
                         "export it, or put it in scripts/.env)";
            if (Settings.IsBoardBound(name) && ProfilePath is not null)
                note += "  (profile active: the per-user file was not consulted)";

            // One path per line rather than the raw joined value - a person
            // reading this is asking which checkouts, not which separator.
            if (name == "HATCH_REPOS")
            {
                var paths = value is { Length: > 0 }
                    ? value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    : [];

                if (paths.Length == 0)
                    Say.Line($"{(name + ":").PadRight(19)}<unset>{note}");
                else
                    foreach (var path in paths) Say.Line($"{(name + ":").PadRight(19)}{path}{where}{note}");

                continue;
            }

            if (name == "HATCH_TAGS")
            {
                if (value is not { Length: > 0 })
                {
                    Say.Line($"{(name + ":").PadRight(19)}<unset>{note}");
                    continue;
                }

                var sentence = Settings.ParseTags(value, out var parsed);
                Say.Line(sentence is null
                    ? $"{(name + ":").PadRight(19)}{string.Join(", ", parsed)}{where}{note}"
                    : $"{(name + ":").PadRight(19)}{value}  (refused: {sentence}){where}{note}");

                continue;
            }

            if (name == "HATCH_DANGEROUS")
            {
                var sentence = Settings.ParseDangerous(value, out var refuses);
                var word = value is not { Length: > 0 }
                    ? "<unset - takes dangerous work>"
                    : sentence is not null
                        ? $"{value}  (refused: {sentence})"
                        : refuses
                            ? "off - this runner refuses dangerous work"
                            : "on - takes dangerous work";

                Say.Line($"{(name + ":").PadRight(19)}{word}{where}{note}");
                continue;
            }

            var shown = value is not { Length: > 0 }
                ? name == "HATCH_KEY"
                    ? $"<unset - calls go out as \"{RunnerName}\", which only a wall-off Hatch reads>"
                    : name == "HATCH_WORKSPACE"
                        ? $"<unset - clones go to {Settings.DefaultWorkspacePath(profileName)}>{profileSuffix}"
                        : name == "HATCH_LOG"
                            ? "<unset - no log>"
                            : "<unset>"
                : name == "HATCH_KEY"
                    ? Mask(value)
                    : name == "HATCH_WORKSPACE" && Settings.IsWorkspaceOff(value)
                        ? "off - this runner never clones"
                        : name == "HATCH_LOG" && Settings.IsLogOff(value)
                            ? "off - no log"
                            : name == "HATCH_LOG" && Settings.IsLogOn(value)
                                ? Settings.DefaultLogPath()
                                : value;

            // A name as wide as the column would print its value glued to the colon.
            var label = name.Length + 1 < 19 ? (name + ":").PadRight(19) : name + ":  ";
            Say.Line($"{label}{shown}{where}{note}");
        }

        return 0;
    }

    /// <summary>Which layer, as a person would say it.</summary>
    private string Where(Settings.Layer layer) => layer switch
    {
        Settings.Layer.Environment => "exported",
        Settings.Layer.Checkout => CheckoutEnvFile ?? "this checkout",
        Settings.Layer.Profile => ProfilePath ?? "a profile",
        Settings.Layer.User => ConfigPath,
        _ => "unset",
    };

    /// <summary>
    /// The file, written whole or not at all.
    /// </summary>
    /// <remarks>See <see cref="AtomicFile.Write"/> for how it is written whole.</remarks>
    private void Write(
        string origin, string key, string claudeBin, string codexBin, string repos, string workspace = "",
        string agent = "", string grokBin = "", string tags = "", string ollamaHost = "", string ollamaTiers = "",
        string log = "", string dangerous = "", string? path = null, bool overlay = false, string mode = "")
    {
        var target = path ?? ConfigPath;

        var lines = !overlay
            ? new List<string>
              {
                  "# Hatch's settings, written by `hatch config`.",
                  "#",
                  "# Mode 600, and outside every repository. The key does not belong in a",
                  "# commit, a plan, an issue or a paste - Hatch ships to other operators, and",
                  "# the hatch_ak_ prefix exists so that one which slips into a diff is",
                  "# recognisable on sight. Re-run `config` to change any of this.",
                  "",
              }
            : ProfileName is not null
                ? new List<string>
                  {
                      $"# This profile's own overrides, written by `hatch config --profile {ProfileName}" +
                      $"{(mode.Length > 0 ? " " + mode : "")}`.",
                      "# Read when this profile is named explicitly, or made active with `hatch use`.",
                      "",
                  }
                : new List<string>
                  {
                      $"# This checkout's own overrides, written by `hatch config --here{(mode.Length > 0 ? " " + mode : "")}`.",
                      "# Read at higher precedence than the per-user file `hatch config` writes.",
                      "",
                  };

        // Written even when empty for the per-user file, so it records that the
        // omission was deliberate rather than looking like a half-finished
        // config - but never under an overlay (--here or --profile), which
        // would otherwise leak an empty override on top of a real value the
        // per-user file still carries.
        if (!overlay || origin.Length > 0) lines.Add($"HATCH_BASE={origin}");
        if (!overlay || key.Length > 0) lines.Add($"HATCH_KEY={key}");

        if (claudeBin.Length > 0) lines.Add($"HATCH_CLAUDE_BIN={claudeBin}");
        if (codexBin.Length > 0) lines.Add($"HATCH_CODEX_BIN={codexBin}");
        if (grokBin.Length > 0) lines.Add($"HATCH_GROK_BIN={grokBin}");
        if (ollamaHost.Length > 0) lines.Add($"HATCH_OLLAMA_HOST={ollamaHost}");
        if (ollamaTiers.Length > 0) lines.Add($"HATCH_OLLAMA_TIERS={ollamaTiers}");
        if (agent.Length > 0) lines.Add($"HATCH_AGENT={agent}");
        if (repos.Length > 0) lines.Add($"HATCH_REPOS={repos}");
        if (workspace.Length > 0) lines.Add($"HATCH_WORKSPACE={workspace}");
        if (log.Length > 0) lines.Add($"HATCH_LOG={log}");
        if (dangerous.Length > 0) lines.Add($"HATCH_DANGEROUS={dangerous}");
        if (tags.Length > 0) lines.Add($"HATCH_TAGS={tags}");

        AtomicFile.Write(target, lines);
    }

    /// <summary>Enough of a key to recognise which one it is, and not enough to use.</summary>
    public static string Mask(string key) => key.Length <= 16 ? "(set)" : $"{key[..12]}...{key[^4..]}";
}

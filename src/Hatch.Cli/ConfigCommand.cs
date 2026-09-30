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
    string? Root = null)
{
    public static readonly string[] ConfigUsage =
    [
        "usage: hatch config [--show | --origin <origin> | --key <key> | --runner <name>",
        "                     | --repo <path>... | --workspace <dir>]",
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
        "  ASCII, or already the name of a live runner elsewhere. Needs a checkout:",
        "  a runner's name is a fact about one of them, not a setting that follows",
        "  the person.",
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
        "  clones) is two owners of one path.",
        "",
        "  Written to the per-user file, mode 600 where the platform has modes.",
        "  Read back highest-first: an exported variable, then scripts/.env in the",
        "  checkout you are standing in, then that file.",
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
    /// Every runner live on the board, so a typed name already spoken for is
    /// refused rather than silently colliding. Replaced by a test, which has no
    /// origin to reach.
    /// </summary>
    public Func<Settings, CancellationToken, Task<IReadOnlyList<RunnerDto>>> LiveRunners { get; init; } =
        async (settings, ct) =>
        {
            using var client = new HatchClient(settings, "hatch-config");
            return await new Board(client).RunnersAsync(ct);
        };

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (Usage.Wanted(args)) return Usage.Print(Say, ConfigUsage);
        if (args is ["--show"]) return Show();

        // Before the refusal below, and the one mode that asks nothing: it is
        // what the Runner page prints for a machine being set up, where the
        // origin is already known and typing it back in by hand is the step
        // that gets it wrong.
        if (args is ["--origin", var given]) return await OriginAsync(given, ct);

        if (args is ["--key", var givenKey]) return await KeyAsync(givenKey, ct);

        if (args.Length > 0 && args[0] == "--runner") return await RunnerAsync(args, ct);

        if (args.Length > 0 && args[0] == "--repo") return Repo(args);

        if (args.Length > 0 && args[0] == "--workspace") return Workspace(args);

        if (args.Length > 0)
            return Usage.Refuse(Say, "config takes nothing, --show, --origin <origin>, or --key <key>", ConfigUsage);

        if (!In.Interactive)
        {
            Say.Complain("hatch: config asks questions and needs a terminal");
            return 1;
        }

        var fold = Settings.Layers(CheckoutEnvFile, Environment, ConfigPath);

        // Whatever is loaded already is the default, so changing one setting is
        // not an excuse to retype the other.
        var origin = fold("HATCH_BASE").Value ?? "";
        var key = fold("HATCH_KEY").Value ?? "";
        var claudeBin = fold("HATCH_CLAUDE_BIN").Value ?? "";
        var repos = fold("HATCH_REPOS").Value ?? "";
        var workspace = fold("HATCH_WORKSPACE").Value ?? "";

        Say.Line($"Writing {ConfigPath}. Enter keeps what is shown in brackets.");
        Say.Line("");

        In.Prompt($"Hatch origin [{(origin.Length > 0 ? origin : "https://hatch.<your domain>")}]: ");
        if (In.Line() is { Length: > 0 } typedOrigin) origin = typedOrigin;

        if (origin.Length == 0)
        {
            Say.Complain("hatch: an origin is required");
            return 1;
        }

        if (!HasScheme(origin)) return NoScheme(origin);

        // Read without echo: this is the one value on the screen that a
        // screenshot, a shoulder or a scrollback should not be able to keep.
        In.Prompt($"API key [{(key.Length > 0 ? Mask(key) : "hatch_ak_...")}]: ");
        if (In.Secret() is { Length: > 0 } typedKey) key = typedKey;

        if (key.Length == 0)
            Say.Complain(
                $"hatch: no key - calls will name themselves \"{RunnerName}\", " +
                "which only a Hatch with its wall off reads.");
        else if (!key.StartsWith("hatch_ak_", StringComparison.Ordinal))
            Say.Complain("hatch: warning - that does not start with hatch_ak_. Carrying on; the call below will say.");

        In.Prompt($"claude CLI path, for `work` [{(claudeBin.Length > 0 ? claudeBin : "on PATH")}]: ");
        if (In.Line() is { Length: > 0 } typedBin) claudeBin = typedBin;

        // Only in a checkout - a runner's name is a fact about one of them, and
        // there is nothing here to name outside of one. RunnerName is already
        // this checkout's chosen or recorded name (or HATCH_RUNNER's override),
        // so Enter alone changes nothing and writes nothing new.
        string? chosenRunner = null;
        if (Root is { Length: > 0 } root)
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

                if (await HeldElsewhereAsync(candidate, origin, key, root, ct) is { } holder)
                {
                    Say.Complain($"hatch: {candidate} is already the runner on {holder} - pick another name");
                    return 1;
                }

                chosenRunner = candidate;
            }
        }

        Write(origin, key, claudeBin, repos, workspace);

        if (chosenRunner is not null) RunnerNames.Record.Set(RunnersPath, Checkout.Canonical(Root!), chosenRunner);

        // Exported, not just set: this process is about to make the call below,
        // and a `work` spawned from here should not have to find the file again.
        Environment["HATCH_BASE"] = origin;
        Environment["HATCH_KEY"] = key;
        if (claudeBin.Length > 0) Environment["HATCH_CLAUDE_BIN"] = claudeBin;

        Say.Line("");
        Say.Line($"wrote {ConfigPath}");

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
    private async Task<int> OriginAsync(string origin, CancellationToken ct)
    {
        origin = origin.Trim();

        if (origin.Length == 0)
        {
            Say.Complain("hatch: an origin is required");
            return 1;
        }

        if (!HasScheme(origin)) return NoScheme(origin);

        var fold = Settings.Layers(CheckoutEnvFile, Environment, ConfigPath);
        var key = fold("HATCH_KEY").Value ?? "";
        var claudeBin = fold("HATCH_CLAUDE_BIN").Value ?? "";
        var repos = fold("HATCH_REPOS").Value ?? "";
        var workspace = fold("HATCH_WORKSPACE").Value ?? "";

        Write(origin, key, claudeBin, repos, workspace);

        Environment["HATCH_BASE"] = origin;

        Say.Line($"wrote {ConfigPath}");

        if (key.Length == 0)
            Say.Complain(
                $"hatch: no key - calls will name themselves \"{RunnerName}\", " +
                "which only a Hatch with its wall off reads.");

        return await ProveAsync(new Settings { Base = origin.TrimEnd('/'), Key = key }, ct);
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
    private async Task<int> KeyAsync(string key, CancellationToken ct)
    {
        key = key.Trim();

        if (key.Length == 0)
        {
            Say.Complain("hatch: a key is required");
            return 1;
        }

        var fold = Settings.Layers(CheckoutEnvFile, Environment, ConfigPath);
        var origin = fold("HATCH_BASE").Value ?? "";
        var claudeBin = fold("HATCH_CLAUDE_BIN").Value ?? "";
        var repos = fold("HATCH_REPOS").Value ?? "";
        var workspace = fold("HATCH_WORKSPACE").Value ?? "";

        if (origin.Length == 0)
        {
            Say.Complain("hatch: no origin is set yet - run `hatch config --origin <origin>` first");
            return 1;
        }

        if (!key.StartsWith("hatch_ak_", StringComparison.Ordinal))
            Say.Complain("hatch: warning - that does not start with hatch_ak_. Carrying on; the call below will say.");

        Write(origin, key, claudeBin, repos, workspace);

        Environment["HATCH_KEY"] = key;

        Say.Line($"wrote {ConfigPath}");

        return await ProveAsync(new Settings { Base = origin.TrimEnd('/'), Key = key }, ct);
    }

    /// <summary>
    /// This checkout's name, taken from the command line and written without a
    /// question being asked - the mirror of <see cref="OriginAsync"/> and
    /// <see cref="KeyAsync"/>, except that there is nothing else to carry
    /// forward: a runner's name is the one thing this writes.
    /// </summary>
    /// <remarks>
    /// A purely local filesystem fact once it is chosen, like <see cref="Repo"/> -
    /// but choosing it needs the board, to skip a name already live elsewhere,
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

        var fold = Settings.Layers(CheckoutEnvFile, Environment, ConfigPath);
        var origin = fold("HATCH_BASE").Value ?? "";
        var key = fold("HATCH_KEY").Value ?? "";

        if (await HeldElsewhereAsync(candidate, origin, key, root, ct) is { } holder)
        {
            Say.Complain($"hatch: {candidate} is already the runner on {holder} - pick another name");
            return 1;
        }

        RunnerNames.Record.Set(RunnersPath, Checkout.Canonical(root), candidate);

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
    /// The <c>where</c> of the live runner already holding this name elsewhere,
    /// or null - this checkout's own row does not count, and neither does a
    /// board that cannot be reached, the same read <see cref="Checkout.RunnerAsync"/>
    /// makes of one.
    /// </summary>
    private async Task<string?> HeldElsewhereAsync(string name, string origin, string key, string root, CancellationToken ct)
    {
        if (origin.Length == 0) return null;

        try
        {
            var where = Checkout.Where(Checkout.Host(), root);
            var live = await LiveRunners(new Settings { Base = origin.TrimEnd('/'), Key = key }, ct);
            return live.FirstOrDefault(r =>
                    string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(r.Where, where, StringComparison.Ordinal))
                ?.Where;
        }
        catch (HatchException)
        {
            return null;
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
    private int Repo(string[] args)
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

        var fold = Settings.Layers(CheckoutEnvFile, Environment, ConfigPath);
        var origin = fold("HATCH_BASE").Value ?? "";
        var key = fold("HATCH_KEY").Value ?? "";
        var claudeBin = fold("HATCH_CLAUDE_BIN").Value ?? "";
        var workspace = fold("HATCH_WORKSPACE").Value ?? "";

        // The reverse of the check `--workspace` makes: a checkout named here
        // that sits inside the workspace already configured is the same two
        // owners of one path, the other way round.
        if (workspace.Length > 0 && !Checkouts.OverlapsCheckout(workspace, paths, out var workspaceOverlap))
        {
            Say.Complain(workspaceOverlap);
            return 1;
        }

        Write(origin, key, claudeBin, string.Join(Path.PathSeparator, paths), workspace);

        Environment["HATCH_REPOS"] = paths.Count > 0 ? string.Join(Path.PathSeparator, paths) : null;

        Say.Line($"wrote {ConfigPath}");
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
    private int Workspace(string[] args)
    {
        if (args is not ["--workspace", var dir])
            return Usage.Refuse(Say, "config --workspace takes one directory", ConfigUsage);

        var fold = Settings.Layers(CheckoutEnvFile, Environment, ConfigPath);
        var origin = fold("HATCH_BASE").Value ?? "";
        var key = fold("HATCH_KEY").Value ?? "";
        var claudeBin = fold("HATCH_CLAUDE_BIN").Value ?? "";
        var reposRaw = fold("HATCH_REPOS").Value ?? "";
        var repos = reposRaw is { Length: > 0 }
            ? reposRaw.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

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

        Write(origin, key, claudeBin, reposRaw, dir);

        Environment["HATCH_WORKSPACE"] = dir;

        Say.Line($"wrote {ConfigPath}");
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

    /// <summary>What is set, and which of the three layers it came from.</summary>
    private int Show()
    {
        var fold = Settings.Layers(CheckoutEnvFile, Environment, ConfigPath);
        var exists = File.Exists(ConfigPath) ? "" : " (does not exist yet)";

        Say.Line($"file:             {ConfigPath}{exists}");
        Say.Line($"checkout:         {CheckoutEnvFile ?? "<not in one>"}");

        var runnerFold = fold("HATCH_RUNNER");
        if (runnerFold.Value is { Length: > 0 } explicitRunner)
            Say.Line($"runner:           {explicitRunner}  (HATCH_RUNNER, {Where(runnerFold.From)})");
        else if (Root is { Length: > 0 } root)
        {
            var recorded = RunnerNames.Record.Read(RunnersPath).GetValueOrDefault(Checkout.Canonical(root));
            Say.Line(recorded is { Length: > 0 }
                ? $"runner:           {recorded}  ({RunnersPath})"
                : "runner:           <none yet - chosen the next time this checkout speaks to a board>");
        }
        else
            Say.Line("runner:           <not in a checkout>");

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

            // One path per line rather than the raw joined value - a person
            // reading this is asking which checkouts, not which separator.
            if (name == "HATCH_REPOS")
            {
                var paths = value is { Length: > 0 }
                    ? value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    : [];

                if (paths.Length == 0)
                    Say.Line($"{(name + ":").PadRight(19)}<unset>");
                else
                    foreach (var path in paths) Say.Line($"{(name + ":").PadRight(19)}{path}{where}");

                continue;
            }

            var shown = value is not { Length: > 0 }
                ? name == "HATCH_KEY"
                    ? $"<unset - calls go out as \"{RunnerName}\", which only a wall-off Hatch reads>"
                    : "<unset>"
                : name == "HATCH_KEY"
                    ? Mask(value)
                    : value;

            Say.Line($"{(name + ":").PadRight(19)}{shown}{where}");
        }

        return 0;
    }

    /// <summary>Which layer, as a person would say it.</summary>
    private string Where(Settings.Layer layer) => layer switch
    {
        Settings.Layer.Environment => "exported",
        Settings.Layer.Checkout => CheckoutEnvFile ?? "this checkout",
        Settings.Layer.User => ConfigPath,
        _ => "unset",
    };

    /// <summary>
    /// The file, written whole or not at all.
    /// </summary>
    /// <remarks>
    /// Temp file, then mode, then rename - the rename is what makes a
    /// half-written file impossible to read as a whole one, and the mode is set
    /// before there is anything in it to read. <c>File.SetUnixFileMode</c> is a
    /// no-op concept on Windows, where the file inherits the directory's ACL
    /// and <c>%APPDATA%</c> is already per-user.
    /// </remarks>
    private void Write(string origin, string key, string claudeBin, string repos, string workspace = "")
    {
        var directory = Path.GetDirectoryName(ConfigPath);
        if (directory is { Length: > 0 }) Directory.CreateDirectory(directory);

        var temp = $"{ConfigPath}.{System.Environment.ProcessId}";

        var lines = new List<string>
        {
            "# Hatch's settings, written by `hatch config`.",
            "#",
            "# Mode 600, and outside every repository. The key does not belong in a",
            "# commit, a plan, an issue or a paste - Hatch ships to other operators, and",
            "# the hatch_ak_ prefix exists so that one which slips into a diff is",
            "# recognisable on sight. Re-run `config` to change any of this.",
            "",
            $"HATCH_BASE={origin}",

            // Written even when empty, so the file records that the omission was
            // deliberate rather than looking like a half-finished config.
            $"HATCH_KEY={key}",
        };

        if (claudeBin.Length > 0) lines.Add($"HATCH_CLAUDE_BIN={claudeBin}");
        if (repos.Length > 0) lines.Add($"HATCH_REPOS={repos}");
        if (workspace.Length > 0) lines.Add($"HATCH_WORKSPACE={workspace}");

        File.WriteAllLines(temp, lines);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        File.Move(temp, ConfigPath, overwrite: true);
    }

    /// <summary>Enough of a key to recognise which one it is, and not enough to use.</summary>
    public static string Mask(string key) => key.Length <= 16 ? "(set)" : $"{key[..12]}...{key[^4..]}";
}

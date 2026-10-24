using System.Text.RegularExpressions;

namespace Hatch.Cli;

/// <summary>
/// Where to reach Hatch, and the handful of things about this machine that
/// cannot be written down in the repository.
/// </summary>
/// <remarks>
/// <para>Three sources, highest first: anything already exported, then
/// <c>scripts/.env</c> in the checkout the command was run from, then the
/// per-user file at <see cref="UserConfigPath"/>. That order is what makes a
/// one-off origin a prefix on a command line rather than an edit to a file -
/// and what lets one person's settings follow them into every checkout while a
/// single repository can still pin its own.</para>
///
/// <para>Each layer is parsed against an allowlist rather than sourced, since a
/// credential file that is also a program is a larger promise than "a few
/// values".</para>
///
/// <para>The key is optional. A Hatch started with its wall off has no
/// credential to present and names its callers by the runner header instead
/// (docs/auth-architecture.md, "Local mode"), so a missing key is a mode and
/// not a failure to load; <see cref="HatchClient"/> is where the two diverge.</para>
/// </remarks>
public sealed partial record Settings
{
    /// <summary>Everything a settings file may carry. A line naming anything else is skipped, loudly.</summary>
    public static readonly string[] FileNames =
    [
        "HATCH_BASE", "HATCH_KEY", "HATCH_CLAUDE_BIN", "HATCH_CODEX_BIN", "HATCH_GROK_BIN", "HATCH_OLLAMA_HOST",
        "HATCH_OLLAMA_TIERS", "HATCH_AGENT", "HATCH_BASE_BRANCH", "HATCH_RUNNER", "HATCH_REPOS",
        "HATCH_WORKSPACE", "HATCH_TAGS",
    ];

    /// <summary>The names that belong to a board rather than a machine - decision 4. Everything else in FileNames is machine-bound and still folds from the per-user file under a profile.</summary>
    public static readonly string[] BoardBoundNames =
        ["HATCH_BASE", "HATCH_KEY", "HATCH_RUNNER", "HATCH_TAGS", "HATCH_WORKSPACE", "HATCH_REPOS"];

    /// <summary>Whether <paramref name="name"/> is board-bound.</summary>
    public static bool IsBoardBound(string name) => BoardBoundNames.Contains(name, StringComparer.Ordinal);

    /// <summary>The agent ids <see cref="Agents"/> knows how to resolve.</summary>
    public static readonly string[] KnownAgentIds = ["claude", "codex", "grok", "ollama"];

    /// <summary>Which of the three layers a value came from, for <c>config --show</c>.</summary>
    public enum Layer
    {
        /// <summary>Nowhere. Nothing set it.</summary>
        Unset,

        /// <summary>Exported into this process. Wins over both files.</summary>
        Environment,

        /// <summary>This checkout's <c>scripts/.env</c>.</summary>
        Checkout,

        /// <summary>The named profile file, when one resolved.</summary>
        Profile,

        /// <summary>The per-user file, which follows the person between checkouts.</summary>
        User,
    }

    /// <summary>The origin, with no trailing slash.</summary>
    public required string Base { get; init; }

    /// <summary>
    /// The <c>hatch_ak_…</c> key, or empty against a Hatch with its wall off.
    /// Never printed, never logged, never written down here.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>The claude CLI, if it is not simply on PATH.</summary>
    public string? ClaudeBin { get; init; }

    /// <summary>The codex CLI, if it is not simply on PATH.</summary>
    public string? CodexBin { get; init; }

    /// <summary>The grok CLI, if it is not simply on PATH.</summary>
    public string? GrokBin { get; init; }

    /// <summary>Where the Ollama server is - HATCH_OLLAMA_HOST, falling back to OLLAMA_HOST read straight from the process environment, then OllamaHost.Default.</summary>
    public OllamaHost OllamaHost { get; init; } = OllamaHost.Default;

    /// <summary>HATCH_OLLAMA_TIERS, parsed: alias onto a model id, hand-edited in a settings file - no `hatch config` flag sets it.</summary>
    public IReadOnlyDictionary<string, string?> OllamaTiers { get; init; } = new Dictionary<string, string?>();

    /// <summary>Which agent spawns a session - one of <see cref="KnownAgentIds"/>, <c>claude</c> when unset.</summary>
    public string Agent { get; init; } = "claude";

    /// <summary>The trunk the loop resets to, when it is not the one origin calls its default.</summary>
    public string? BaseBranch { get; init; }

    /// <summary>What the board calls this runner, overriding <c>host:/path/to/checkout</c>.</summary>
    public string? Runner { get; init; }

    /// <summary>
    /// The checkouts a loop with no checkout of its own serves, beside the
    /// standing one - empty where nothing is named.
    /// </summary>
    public IReadOnlyList<string> Repos { get; init; } = [];

    /// <summary>
    /// What this machine can do that a project's runner tags ask for - said
    /// on every heartbeat and dispatch read, so the board can match work to a
    /// runner that is actually able to do it.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// A directory this runner owns entirely, where it clones every repository
    /// the board binds that it has no checkout of - null for a runner with
    /// none, which is every runner before HA-19.
    /// </summary>
    public string? Workspace { get; init; }

    /// <summary>
    /// True where <see cref="Workspace"/> is <see cref="DefaultWorkspacePath"/>
    /// because nothing set one. Such a workspace is held lightly: it is not
    /// created until a clone needs it, and it gives way rather than refusing
    /// where it conflicts with the checkout the command stands in.
    /// </summary>
    public bool WorkspaceDefaulted { get; init; }

    /// <summary>
    /// What <c>HATCH_WORKSPACE</c> (or <c>--workspace</c>) says to turn the
    /// default off: this runner serves the checkouts it has and never clones.
    /// A word rather than a blank because a blank value reads as unset in every
    /// layer, which is how the default gets applied at all.
    /// </summary>
    public const string WorkspaceOff = "off";

    /// <summary>Whether a workspace value is <see cref="WorkspaceOff"/>.</summary>
    public static bool IsWorkspaceOff(string? value) =>
        string.Equals(value?.Trim(), WorkspaceOff, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The workspace one run serves, and whether it is the default: the
    /// command line's <paramref name="flag"/> if there is one, as that run's
    /// whole answer, and these settings' own otherwise.
    /// </summary>
    public (string? Workspace, bool Defaulted) WorkspaceFor(string? flag) =>
        flag is null ? (Workspace, WorkspaceDefaulted) : (IsWorkspaceOff(flag) ? null : flag, false);

    /// <summary>These settings with the workspace a run actually ended up serving.</summary>
    public Settings WithWorkspace(string? workspace, bool defaulted) =>
        this with { Workspace = workspace, WorkspaceDefaulted = defaulted && workspace is not null };

    /// <summary>
    /// How long a spawned session may say nothing before the renderer says what
    /// it is still waiting on. Zero turns the pulse off.
    /// </summary>
    public int HeartbeatSeconds { get; init; } = 20;

    /// <summary>
    /// How long <see cref="HatchClient.Send"/> keeps retrying a call Hatch has
    /// not answered before it gives up - ninety seconds, which is meant to be
    /// longer than a deploy with no health check ever leaves an operator
    /// staring at nothing. Zero means exactly one attempt: retries switched
    /// off, not a wait of no time at all.
    /// </summary>
    public int RetrySeconds { get; init; } = 90;

    /// <summary>
    /// One line saying a <c>HATCH_KEY</c> was set in a layer other than the one
    /// <c>HATCH_BASE</c> came from, and so not read - null where none was. Data
    /// rather than a write to standard error, so a caller whose output is a
    /// document can choose when to say it.
    /// </summary>
    public string? KeyNote { get; init; }

    /// <summary>Where each value came from, so <c>config --show</c> can say.</summary>
    public IReadOnlyDictionary<string, Layer> Sources { get; init; } =
        new Dictionary<string, Layer>(StringComparer.Ordinal);

    /// <summary>
    /// The settings file that belongs to the person rather than to a checkout:
    /// <c>ApplicationData/hatch/config</c>, which is <c>~/.config/hatch/config</c>
    /// on Unix and <c>%APPDATA%\hatch\config</c> on Windows.
    /// </summary>
    /// <remarks>
    /// The whole reason this story exists: an installed <c>hatch</c> is run from
    /// wherever somebody happens to be standing, and settings that lived in one
    /// repository's <c>scripts/</c> directory were settings that only worked in
    /// one repository.
    /// </remarks>
    public static string UserConfigPath() => Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "hatch",
        "config");

    /// <summary>
    /// Where a runner clones what the board binds when nobody named a place:
    /// <c>ApplicationData/hatch/workspace</c>, beside <see cref="UserConfigPath"/>.
    /// Built from the operating system's per-user folder, so it is a convention
    /// every installation shares and not a path that is true of only one.
    /// </summary>
    public static string DefaultWorkspacePath() => Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "hatch",
        "workspace");

    /// <summary>Where every profile file lives, named for the person rather than a checkout.</summary>
    public static string ProfilesDirectory() => Path.Combine(ApplicationData, "hatch", "profiles");

    /// <summary>A named profile's own file, inside <see cref="ProfilesDirectory"/>.</summary>
    public static string ProfilePath(string name) => Path.Combine(ProfilesDirectory(), name);

    /// <summary>The one line naming which profile is active when nothing names one explicitly.</summary>
    public static string ActiveProfilePath() => Path.Combine(ApplicationData, "hatch", "profile");

    /// <summary>The operating system's per-user folder, shared by every path above it.</summary>
    private static string ApplicationData => Environment.GetFolderPath(
        Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify);

    /// <summary>A profile name safe to use as a file name: letters, digits and hyphens, and not blank.</summary>
    public static bool ValidProfileName(string name) => !string.IsNullOrEmpty(name) && ProfileNamePattern().IsMatch(name);

    [GeneratedRegex(@"^[A-Za-z0-9-]+$")]
    private static partial Regex ProfileNamePattern();

    /// <summary>
    /// Which profile applies, highest first: <paramref name="requested"/> (already
    /// trimmed by the caller), then <c>HATCH_PROFILE</c>, then the one line of
    /// <see cref="ActiveProfilePath"/>. Nothing named anywhere is not a refusal -
    /// it is today's fold, unchanged, with no profile in it at all. A name that
    /// was actually named and has no file behind it is the only refusal.
    /// </summary>
    public static bool TryResolveProfile(
        string? requested, IDictionary<string, string?> environment,
        out string? name, out string? file, out string refusal)
    {
        name = null;
        file = null;
        refusal = "";

        var candidate = requested;

        if (string.IsNullOrEmpty(candidate) &&
            environment.TryGetValue("HATCH_PROFILE", out var exported) && !string.IsNullOrWhiteSpace(exported))
            candidate = exported.Trim();

        if (string.IsNullOrEmpty(candidate))
        {
            var pointer = ActiveProfilePath();
            if (File.Exists(pointer))
            {
                var line = File.ReadLines(pointer).FirstOrDefault()?.Trim();
                if (!string.IsNullOrEmpty(line)) candidate = line;
            }
        }

        if (string.IsNullOrEmpty(candidate)) return true;

        if (!ValidProfileName(candidate) || !File.Exists(ProfilePath(candidate)))
        {
            // Named even on refusal - a caller showing what is active wants to
            // say which name does not exist, not just that resolution failed.
            name = candidate;
            refusal = $"""hatch: no such profile "{candidate}" - looked in {ProfilesDirectory()}""";
            return false;
        }

        name = candidate;
        file = ProfilePath(candidate);
        return true;
    }

    /// <summary>
    /// The three layers, folded. Answers the settings, or the sentence saying
    /// what is missing - which is the origin alone, since the key is optional.
    /// </summary>
    /// <param name="checkoutEnvFile">
    /// This checkout's <c>scripts/.env</c>, or null where the command was not
    /// run inside one. Fourteen of the sixteen commands do not need a checkout
    /// and pass null here.
    /// </param>
    /// <param name="userConfigFile">
    /// The per-user file. Named rather than looked up so a test can put one
    /// somewhere that is not the machine's own.
    /// </param>
    /// <param name="profileFile">
    /// The resolved profile's own file, if one resolved - see
    /// <see cref="TryResolveProfile"/>. Null is "no profile", which folds
    /// exactly as it did before this layer existed.
    /// </param>
    public static bool TryLoad(
        string? checkoutEnvFile,
        IDictionary<string, string?> environment,
        out Settings settings,
        out string refusal,
        string? userConfigFile = null,
        string? profileFile = null)
    {
        var sources = new Dictionary<string, Layer>(StringComparer.Ordinal);
        var (fold, ignoredKeyLayer) = LayersWithNotes(checkoutEnvFile, environment, userConfigFile, profileFile);

        string? Read(string name)
        {
            var (value, from) = fold(name);
            sources[name] = from;
            return value;
        }

        settings = null!;
        refusal = "";

        var origin = Read("HATCH_BASE");
        var key = Read("HATCH_KEY");
        var claudeBin = Read("HATCH_CLAUDE_BIN");
        var codexBin = Read("HATCH_CODEX_BIN");
        var grokBin = Read("HATCH_GROK_BIN");
        var ollamaHostRaw = Read("HATCH_OLLAMA_HOST");
        var ollamaTiersRaw = Read("HATCH_OLLAMA_TIERS");
        var agentRaw = Read("HATCH_AGENT");
        var baseBranch = Read("HATCH_BASE_BRANCH");
        var runner = Read("HATCH_RUNNER");
        var heartbeat = Read("HATCH_HEARTBEAT");
        var retry = Read("HATCH_RETRY_SECONDS");
        var reposRaw = Read("HATCH_REPOS");
        var workspace = Read("HATCH_WORKSPACE");
        var tagsRaw = Read("HATCH_TAGS");

        // The origin alone. A key is not required, because a Hatch with its wall
        // off has no credential to present - and a load that refused without one
        // would make local mode unreachable from the command that configures it.
        if (string.IsNullOrWhiteSpace(origin))
        {
            refusal = string.Join('\n',
                "hatch: not configured - it needs an origin.",
                "",
                "    hatch config          asks for it, and writes it where it follows you",
                "    hatch config --show   says what is set, and which layer it came from",
                "",
                "  Or export HATCH_BASE. The key may be left empty for a Hatch running with",
                "  its wall off. Never in a repository, either way: Hatch ships to other",
                "  operators, and a key in the artifact is one operator's key inherited by",
                "  everybody who clones it.");
            return false;
        }

        var pulse = 20;
        if (heartbeat is { } beat && int.TryParse(beat, out var parsed) && parsed >= 0) pulse = parsed;

        var retrySeconds = 90;
        if (retry is { } window && int.TryParse(window, out var parsedRetry) && parsedRetry >= 0)
            retrySeconds = parsedRetry;

        var repos = reposRaw is { Length: > 0 }
            ? reposRaw.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

        if (ParseTags(tagsRaw, out var tags) is { } sentence)
        {
            refusal = $"""
                hatch: HATCH_TAGS - {sentence}

                  Fix it with `hatch config --tag <tag>` (bare `--tag` clears it), or change the exported variable.
                """;
            return false;
        }

        if (OllamaAdapter.ParseTiers(ollamaTiersRaw, out var ollamaTiers) is { } tiersSentence)
        {
            refusal = $"""
                hatch: HATCH_OLLAMA_TIERS - {tiersSentence}

                  Fix it by editing the setting file directly - there is no `hatch config` flag for it - or change the exported variable.
                """;
            return false;
        }

        settings = new Settings
        {
            Base = origin.TrimEnd('/'),
            Key = key?.Trim() ?? "",
            ClaudeBin = claudeBin,
            CodexBin = codexBin,
            GrokBin = grokBin,
            OllamaHost = Hatch.Cli.OllamaHost.Resolve(
                ollamaHostRaw, environment.TryGetValue("OLLAMA_HOST", out var rawOllamaHost) ? rawOllamaHost : null),
            OllamaTiers = ollamaTiers,
            Agent = string.IsNullOrWhiteSpace(agentRaw) ? "claude" : agentRaw,
            BaseBranch = baseBranch,
            Runner = runner,
            HeartbeatSeconds = pulse,
            RetrySeconds = retrySeconds,
            Repos = repos,
            Workspace = IsWorkspaceOff(workspace) ? null : workspace ?? DefaultWorkspacePath(),
            WorkspaceDefaulted = workspace is null,
            Tags = tags,
            Sources = sources,
            KeyNote = ignoredKeyLayer() is { } ignored
                ? IgnoredKeySentence(
                    ignored, sources["HATCH_BASE"], checkoutEnvFile, profileFile, userConfigFile ?? UserConfigPath())
                : null,
        };
        return true;
    }

    /// <summary>The one line saying which layer held a key that was not read, and what to do.</summary>
    private static string IgnoredKeySentence(
        Layer ignored, Layer origin, string? checkoutEnvFile, string? profileFile, string userFile)
    {
        string Where(Layer layer) => layer switch
        {
            Layer.Environment => "the environment",
            Layer.Checkout => checkoutEnvFile ?? "the checkout's scripts/.env",
            Layer.Profile => profileFile ?? "the profile",
            _ => userFile,
        };

        var fix = ignored == Layer.Environment ? "Export both, or neither." : $"Set both in {Where(ignored)}, or neither.";
        return $"hatch: HATCH_KEY in {Where(ignored)} is ignored - HATCH_BASE comes from {Where(origin)}, " +
               $"and a key goes with its origin. {fix}";
    }

    /// <summary>
    /// <paramref name="raw"/> split on commas, trimmed and emptied of blanks,
    /// then run through <see cref="RunnerTags.Normalise"/> - null or blank
    /// answers an empty <paramref name="tags"/> with no refusal. Answers the
    /// refusal sentence, or null once <paramref name="tags"/> is the trimmed,
    /// lowercased, deduplicated list. Public because <c>ConfigCommand.Show</c>
    /// reuses it to validate <c>--tag</c> the same way.
    /// </summary>
    public static string? ParseTags(string? raw, out IReadOnlyList<string> tags)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            tags = [];
            return null;
        }

        var list = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return RunnerTags.Normalise(list, out tags);
    }

    /// <summary>Where a value came from, or <see cref="Layer.Unset"/> if nothing set it.</summary>
    public Layer SourceOf(string name) => Sources.GetValueOrDefault(name, Layer.Unset);

    /// <summary>
    /// The four layers as one lookup: the value, and which layer it came from.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="TryLoad"/> because <c>config --show</c> has to
    /// answer on a machine that has nothing configured yet, and a load that
    /// refuses for want of an origin cannot say where the origin is missing
    /// from.
    /// </remarks>
    public static Func<string, (string? Value, Layer From)> Layers(
        string? checkoutEnvFile, IDictionary<string, string?> environment,
        string? userConfigFile = null, string? profileFile = null) =>
        LayersWithNotes(checkoutEnvFile, environment, userConfigFile, profileFile).Fold;

    /// <summary>
    /// <see cref="Layers"/>, and with it which layer held a <c>HATCH_KEY</c>
    /// the fold ignored - read from the same files, so nothing is read twice.
    /// </summary>
    /// <remarks>
    /// A key goes with its origin: <c>HATCH_KEY</c> answers only from the layer
    /// <c>HATCH_BASE</c> came from, and is unset where that layer carries none.
    /// A key in a lower or higher layer would be presented to a board it was
    /// never issued for.
    /// </remarks>
    public static (Func<string, (string? Value, Layer From)> Fold, Func<Layer?> IgnoredKeyLayer) LayersWithNotes(
        string? checkoutEnvFile, IDictionary<string, string?> environment,
        string? userConfigFile = null, string? profileFile = null)
    {
        var checkout = ReadFile(checkoutEnvFile);
        var profile = ReadFile(profileFile);
        var user = ReadFile(userConfigFile ?? UserConfigPath());

        string? In(Layer layer, string name)
        {
            var found = layer switch
            {
                Layer.Environment => environment.TryGetValue(name, out var exported) ? exported : null,
                Layer.Checkout => checkout.GetValueOrDefault(name),
                Layer.Profile => profile.GetValueOrDefault(name),
                Layer.User => user.GetValueOrDefault(name),
                _ => null,
            };
            return string.IsNullOrWhiteSpace(found) ? null : found;
        }

        (string? Value, Layer From) Lookup(string name)
        {
            foreach (var layer in Order)
            {
                if (layer == Layer.User && profileFile is not null && IsBoardBound(name)) continue;
                if (In(layer, name) is { } value) return (value, layer);
            }

            return (null, Layer.Unset);
        }

        (string? Value, Layer From) Fold(string name)
        {
            if (name != "HATCH_KEY") return Lookup(name);

            var origin = Lookup("HATCH_BASE").From;
            if (origin == Layer.Unset) return (null, Layer.Unset);

            return In(origin, name) is { } key ? (key, origin) : (null, Layer.Unset);
        }

        Layer? IgnoredKeyLayer()
        {
            var origin = Lookup("HATCH_BASE").From;
            if (origin == Layer.Unset) return null;

            foreach (var layer in Order)
                if (layer != origin && In(layer, "HATCH_KEY") is not null) return layer;

            return null;
        }

        return (Fold, IgnoredKeyLayer);
    }

    private static readonly Layer[] Order = [Layer.Environment, Layer.Checkout, Layer.Profile, Layer.User];

    /// <summary>
    /// <c>KEY=value</c> a line, blanks and <c>#</c> comments skipped, one layer
    /// of surrounding quotes stripped - and a name that is not on the allowlist
    /// ignored with a sentence saying so, rather than silently.
    /// </summary>
    internal static Dictionary<string, string> ReadFile(string? path)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return values;

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            var split = line.IndexOf('=');
            if (split <= 0) continue;

            var name = line[..split].Trim();
            var value = line[(split + 1)..].Trim();

            if (!FileNames.Contains(name, StringComparer.Ordinal))
            {
                Console.Error.WriteLine(
                    $"hatch: ignoring \"{name}\" in {path} - not one of: {string.Join(' ', FileNames)}");
                continue;
            }

            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
                value = value[1..^1];

            values[name] = value;
        }

        return values;
    }
}

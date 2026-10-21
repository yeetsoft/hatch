namespace Hatch.Cli;

/// <summary>
/// The codex CLI pointed at an Ollama server instead of the vendor's cloud:
/// <see cref="CodexAdapter"/>'s spawn, stream and closing, plus the provider
/// flags, the operator's host and whose model table it is.
/// </summary>
/// <remarks>
/// Probe, hooks, the stream reader, <c>CanSpawn</c> and the binary lookup
/// (<c>HATCH_CODEX_BIN</c>, else <c>codex</c> on <c>PATH</c>) are all
/// <see cref="CodexAdapter"/>'s, untouched. <c>CanDeny</c> is not overridden
/// either: Codex has none and takes the interface default, and a denied rule
/// its read-only sandbox cannot carry is an <see cref="InvalidOperationException"/>
/// from both builds, inherited as it stands.
/// </remarks>
public sealed class OllamaAdapter : CodexAdapter
{
    /// <summary>The playbook's aliases in the order the warning names them.</summary>
    private static readonly string[] Aliases = ["haiku", "sonnet", "opus", "fable"];

    /// <summary>Every alias null: no model id is shipped here, the operator names theirs.</summary>
    private static readonly IReadOnlyDictionary<string, string?> DefaultTiers = new Dictionary<string, string?>
    {
        ["haiku"] = null,
        ["sonnet"] = null,
        ["opus"] = null,
        ["fable"] = null,
    };

    private readonly OllamaHost _host;
    private readonly IReadOnlyDictionary<string, string?> _tiers;

    /// <param name="host">Where the server is; <see cref="OllamaHost.Default"/> when none.</param>
    /// <param name="tiers">
    /// Overrides onto four null defaults, so an alias nobody mapped sends no
    /// <c>-m</c> at all and the CLI's own default applies.
    /// </param>
    /// <param name="warn">Told once, from the constructor, which aliases will run on the CLI's default.</param>
    public OllamaAdapter(
        OllamaHost? host = null,
        IReadOnlyDictionary<string, string?>? tiers = null,
        Action<string>? warn = null,
        string? configured = null,
        string? path = null)
        : base(configured, path)
    {
        _host = host ?? OllamaHost.Default;

        var merged = new Dictionary<string, string?>(DefaultTiers);
        if (tiers is not null)
            foreach (var (alias, id) in tiers)
                merged[alias] = id;
        _tiers = merged;

        var unmapped = Aliases.Where(alias => merged[alias] is null).ToArray();
        if (unmapped.Length > 0)
            warn?.Invoke($"hatch: ollama - {Join(unmapped)} run on the CLI's default model");
    }

    public override string Id => "ollama";

    public override string DisplayName => "Ollama";

    /// <remarks>
    /// codex-cli 0.163.0, 2026-10-21: <c>--oss</c> and
    /// <c>--local-provider &lt;lmstudio|ollama&gt;</c> are in both
    /// <c>codex exec --help</c> and <c>codex exec resume --help</c>. The names
    /// are in <c>--help</c>; what each does against a real Ollama is HA-1257's
    /// to prove.
    /// </remarks>
    protected override IReadOnlyList<string> ProviderArgs { get; } = ["--oss", "--local-provider", "ollama"];

    /// <summary>
    /// Only an operator-named host is carried. Otherwise nothing is touched -
    /// not even an inherited value is removed - so an <c>oss</c> entry in the
    /// operator's own <c>~/.codex/config.toml</c> still wins as it does from
    /// their terminal.
    /// </summary>
    /// <remarks>
    /// codex-cli 0.163.0, 2026-10-21: <c>CODEX_OSS_BASE_URL</c> is among the
    /// binary's strings. The value, the host with Ollama's OpenAI-compatible
    /// <c>/v1</c>, is per Ollama's Codex page and the vendor README; what the
    /// variable does is HA-1257's to prove, and to correct here if it is wrong.
    /// </remarks>
    protected override void ConfigureEnvironment(IDictionary<string, string?> environment)
    {
        if (_host.Explicit) environment["CODEX_OSS_BASE_URL"] = _host.Url + "/v1";
    }

    /// <summary>
    /// A mapped alias is the id to send, a pinned id passes through, and an
    /// unmapped alias is null: send no <c>-m</c>, let the CLI choose.
    /// </summary>
    protected override string? ModelFlag(string model) => _tiers.TryGetValue(model, out var id) ? id : model;

    /// <summary>
    /// <c>exec resume</c> takes both provider flags; interactive <c>resume</c>
    /// takes neither (codex-cli 0.163.0, 2026-10-21, per <c>--help</c>). The host
    /// is not on the line: it is the operator's environment.
    /// </summary>
    public override string ResumeCommand(string sessionId) =>
        $"codex exec resume --oss --local-provider ollama {sessionId}";

    /// <summary>A local server has no usage window to hit.</summary>
    public override UsageLimit.Recognised? RecogniseLimit(string? said, DateTimeOffset endedAt, TimeZoneInfo? runnerZone = null) => null;

    /// <summary>"a", "a and b", "a, b and c".</summary>
    private static string Join(string[] names) =>
        names.Length == 1 ? names[0] : string.Join(", ", names[..^1]) + " and " + names[^1];
}

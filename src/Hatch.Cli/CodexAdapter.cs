using System.Diagnostics;
using System.Text.Json;

namespace Hatch.Cli;

/// <summary>The codex CLI, behind <see cref="IAgentAdapter"/>.</summary>
public sealed class CodexAdapter(string? configured = null) : IAgentAdapter
{
    /// <summary>The binary <see cref="CanSpawn"/> found, and null until it has.</summary>
    private string? _bin;

    public string Id => "codex";

    public string DisplayName => "Codex";

    public IUsageProbe? Probe => null;

    public IHookOutputWriter HookOutput { get; } = new CodexHookOutputWriter();

    public bool CanSpawn(out string refusal)
    {
        if (_bin is not null)
        {
            refusal = "";
            return true;
        }

        if (!TryFind(configured, out var found, out refusal)) return false;

        _bin = found;
        return true;
    }

    public ProcessStartInfo BuildHeadless(SessionRequest request)
    {
        var readOnly = EnsureDenyTranslatable(request.DeniedTools);

        var start = Base(request, exec: true);
        start.ArgumentList.Add("--json");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add($"model_reasoning_effort={MapEffort(request.Effort)}");

        // One flag, never two: under `exec`, --approve-for-me refuses to sit
        // beside -s, and it implies workspace-write on its own. A denied write
        // tool downgrades to read-only instead, which is the only way this CLI
        // has of saying "do not edit". Never --dangerously-bypass-approvals-and-sandbox,
        // which would remove the very sandbox the downgrade relies on.
        if (readOnly)
        {
            start.ArgumentList.Add("-s");
            start.ArgumentList.Add("read-only");
        }
        else
        {
            start.ArgumentList.Add("--approve-for-me");
        }

        // The overlay directory the hooks were written into is this spawn's
        // whole Codex home, and the last message is written beside it, outside
        // the checkout, for the same reason the hooks are.
        if (request.HookSettings is { Length: > 0 } codexHome)
        {
            start.Environment["CODEX_HOME"] = codexHome;
            start.ArgumentList.Add("-o");
            start.ArgumentList.Add(LastMessageIn(codexHome));
        }

        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;

        return start;
    }

    public string? OutputLastMessagePath(SessionRequest request) =>
        request.HookSettings is { Length: > 0 } codexHome ? LastMessageIn(codexHome) : null;

    private static string LastMessageIn(string codexHome) => Path.Combine(codexHome, "last-message.txt");

    public ProcessStartInfo BuildAttached(SessionRequest request)
    {
        // A rule that cannot be honoured unattended cannot be honoured any
        // better with somebody at the keyboard, so the refusal is the same. But
        // the read-only downgrade is an unattended measure: here the operator
        // answers the prompts, so no sandbox flag and no approval flag is
        // carried at all.
        EnsureDenyTranslatable(request.DeniedTools);

        var start = Base(request, exec: false);
        start.ArgumentList.Add(request.Prompt);
        return start;
    }

    public IAgentStreamReader CreateReader(string root, RunFacts facts, string model) =>
        new CodexStreamReader(root, facts, MapModel(model), ResumeCommand);

    /// <summary>
    /// The last thing a run said, out of the NDJSON a quiet run wrote: an
    /// <c>agent_message</c>, else the failure that ended it - the same "last one
    /// wins" rule <see cref="CodexStreamReader"/> applies live.
    /// </summary>
    public string? ReadClosing(string json)
    {
        string? closing = null;
        foreach (var raw in json.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith('{')) continue;

            JsonElement e;
            try
            {
                e = JsonDocument.Parse(line).RootElement;
            }
            catch (JsonException)
            {
                continue;
            }

            var said = Text(e, "type") switch
            {
                "item.completed" when e.TryGetProperty("item", out var item) && Text(item, "type") == "agent_message" =>
                    Text(item, "text"),
                "turn.failed" when e.TryGetProperty("error", out var error) => Text(error, "message"),
                "error" => Text(e, "message"),
                _ => null,
            };

            if (said is not null) closing = said;
        }

        return closing;
    }

    /// <summary>The line printed so a person can join the session after the increment.</summary>
    /// <remarks>
    /// Only honest because no <c>CODEX_HOME</c> overlay is built, so the rollout
    /// lands in the operator's real home and outlives the run. An overlay would
    /// take the rollout with it when deleted; see <see cref="CodexHooks"/>.
    /// </remarks>
    public string ResumeCommand(string sessionId) => $"codex exec resume {sessionId}";

    /// <summary>
    /// No door: confirmed against a real, logged-in CLI on 2026-10-20 (HA-910's
    /// proof) that <c>codex exec</c> silently skips every hook in a
    /// <c>CODEX_HOME</c> overlay's <c>hooks.json</c> unless the spawn also
    /// carries <c>--dangerously-bypass-hook-trust</c> - a flag this adapter will
    /// not send, the same stance <see cref="BuildHeadless"/> already takes on
    /// <c>--dangerously-bypass-approvals-and-sandbox</c>. <see cref="CodexHooks"/>
    /// still builds a correct overlay (proven by the same session, with the
    /// bypass flag, in isolation) - it is simply never reached from here, so a
    /// session runs with no hooks and no <c>CODEX_HOME</c>, which
    /// <see cref="BuildHeadless"/> already degrades to cleanly.
    /// </summary>
    public IAgentHooks? WriteHooks(string temp, string key, string hatch, string? rtk) => null;

    public UsageLimit.Recognised? RecogniseLimit(string? said, DateTimeOffset endedAt, TimeZoneInfo? runnerZone = null) =>
        UsageLimit.RecogniseCodex(said, endedAt, runnerZone);

    /// <summary>
    /// The binary to spawn, which is only known once <see cref="CanSpawn"/> has
    /// said there is one. A caller that skipped the question is a bug, and
    /// spawning whatever <c>PATH</c> happens to offer would hide it.
    /// </summary>
    private string Bin => _bin
        ?? throw new InvalidOperationException("CanSpawn was never asked, so there is no codex CLI to spawn");

    /// <summary>
    /// Whether a denied-tools list asks for a read-only session, and a refusal
    /// of any rule this CLI has no way to carry. Codex has a sandbox mode and
    /// nothing finer: a bare edit tool is a write the sandbox can take away,
    /// but a command, a pattern or an MCP tool is not, and quietly running
    /// without the restriction would be a deny list in name only.
    /// </summary>
    private static bool EnsureDenyTranslatable(IReadOnlyList<string> tools)
    {
        var readOnly = false;
        foreach (var rule in tools)
        {
            if (rule is "Edit" or "Write" or "NotebookEdit")
            {
                readOnly = true;
                continue;
            }

            throw new InvalidOperationException(
                $"the denied tool \"{rule}\" has no equivalent in codex, whose only restriction is a read-only sandbox " +
                "that stands for Edit, Write and NotebookEdit alone - drop it from the playbook, or run it on claude");
        }

        return readOnly;
    }

    private ProcessStartInfo Base(SessionRequest request, bool exec)
    {
        var start = new ProcessStartInfo
        {
            FileName = Bin,
            WorkingDirectory = request.Root,
            UseShellExecute = false,
        };

        // The prompt this session is about to be handed names `hatch` commands,
        // so the word has to resolve inside it. See Reach. Its calls name the
        // runner that dispatched it, not the person the checkout belongs to.
        Reach.OnPath(start.Environment, Reach.OwnDirectory(Environment.ProcessPath));
        Reach.AsRunner(start.Environment, request.RunnerName);

        if (exec) start.ArgumentList.Add("exec");

        start.ArgumentList.Add("-m");
        start.ArgumentList.Add(MapModel(request.Model));
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(request.Root);
        foreach (var dir in request.AddDirs)
        {
            start.ArgumentList.Add("--add-dir");
            start.ArgumentList.Add(dir);
        }

        return start;
    }

    /// <summary>
    /// The playbook's aliases onto this vendor's own ids; a pinned id passes
    /// through. Re-pull from the vendor's current model list when it changes -
    /// the same caveat <c>ModelFamilies.Default</c>'s codex row carries.
    /// </summary>
    /// <remarks>
    /// Confirmed against a real, logged-in CLI on 2026-10-20 (HA-910's proof):
    /// a ChatGPT-account login's own catalog is <c>gpt-reserve</c>,
    /// <c>gpt-6-luna</c>, <c>gpt-5.6-terra</c>, <c>gpt-5.6-luna</c> and
    /// <c>codex-auto-review</c> - every other id tried, including the ones this
    /// table named before (<c>gpt-6.1-sol</c>, <c>gpt-6-astra</c>, and even
    /// guesses like bare <c>gpt-6</c>), answers the same hard refusal: "the
    /// '&lt;id&gt;' model is not supported when using Codex with a ChatGPT
    /// account." No id in that catalog reads as a tier above <c>sonnet</c>'s, so
    /// <c>opus</c>/<c>fable</c> collapse onto it here rather than naming an id
    /// this login cannot reach; API-key auth was not tried and may unlock more.
    /// </remarks>
    private static string MapModel(string model) => model switch
    {
        "haiku" => "gpt-6-luna",
        "sonnet" or "opus" or "fable" => "gpt-5.6-terra",
        _ => model,
    };

    /// <summary>Codex's top effort is spelled <c>xhigh</c>; the rest of the playbook's words are its own.</summary>
    private static string MapEffort(string effort) => effort == "max" ? "xhigh" : effort;

    private static string? Text(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryFind(string? configured, out string bin, out string refusal)
    {
        refusal = "";

        if (!string.IsNullOrWhiteSpace(configured))
        {
            bin = configured;
            if (File.Exists(bin)) return true;

            refusal = $"hatch: HATCH_CODEX_BIN is not there: {bin}";
            return false;
        }

        bin = "codex";
        if (Reach.Find("codex", Environment.GetEnvironmentVariable("PATH")) is { } found)
        {
            bin = found;
            return true;
        }

        refusal = string.Join('\n',
            "hatch: no codex CLI on PATH.",
            "",
            "  Install it, or point at one you have:",
            "      export HATCH_CODEX_BIN=/path/to/codex");
        return false;
    }
}

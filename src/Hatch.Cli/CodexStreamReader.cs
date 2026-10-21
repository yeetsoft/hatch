using System.Text.Json;

namespace Hatch.Cli;

/// <summary>
/// Codex's <c>--json</c> event vocabulary, read back the way <see
/// cref="StreamRender"/> reads Claude's <c>stream-json</c> - its own file,
/// because the two CLIs' events share no shape, consumed by (but not part
/// of) an adapter's own <c>CreateReader</c>.
/// </summary>
/// <remarks>
/// Codex's <c>turn.completed.usage</c> names no model anywhere, unlike
/// Claude's <c>modelUsage</c> dict - so <paramref name="model"/> below is
/// explicit, the same way <see cref="ClaudeAdapter"/> already threads its own
/// <c>ResumeCommand</c> through rather than this reader guessing one.
/// </remarks>
internal sealed class CodexStreamReader(string root, RunFacts facts, string model, Func<string, string>? resumeCommand = null)
    : IAgentStreamReader
{
    private readonly Func<string, string> _resumeCommand = resumeCommand ?? (id => $"codex resume {id}");

    private long _inputTokens;
    private long _outputTokens;
    private long _cacheCreationTokens;
    private long _cacheReadTokens;
    private long _peakContext;

    public long TokensSoFar { get; private set; }

    public int Requests { get; private set; }

    public long PeakContextTokens => _peakContext;

    // A stub for the interface; the Codex reads task (HA-1266) replaces it.
    public IReadOnlyList<WorkLogReadDto> Reads => [];

    /// <summary>
    /// Always empty - Codex's <c>ThreadEvent</c> has no <c>rate_limit_event</c>
    /// equivalent, confirmed from its own source rather than from documentation
    /// saying nothing. An absent battery is already a supported state.
    /// </summary>
    public IReadOnlyList<UsageWindow> Usage { get; } = [];

    public DateTimeOffset? UsageReadAt => null;

    public IEnumerable<string> Read(string raw)
    {
        var line = raw.TrimStart();
        if (!line.StartsWith('{')) return [];

        JsonElement e;
        try
        {
            e = JsonDocument.Parse(line).RootElement;
        }
        catch (JsonException)
        {
            return [];
        }

        return Text(e, "type") switch
        {
            "thread.started" => ThreadStarted(e),
            "item.started" => ItemStarted(e),
            "item.completed" => ItemCompleted(e),
            "turn.completed" => TurnCompleted(e),
            "turn.failed" => TurnFailed(e),
            "error" => Error(e),
            _ => [],
        };
    }

    private IEnumerable<string> ThreadStarted(JsonElement e)
    {
        var id = Text(e, "thread_id");
        if (id is null) yield break;

        facts.SessionId = id;
        yield return $"hatch: session {id}";
        yield return $"hatch:   join it with  {_resumeCommand(id)}";
    }

    /// <summary>
    /// One line every time, like Claude's tool-use line - <c>file_change</c>
    /// never arrives here (the vendor's own doc comment: "emitted only as a
    /// completed event"), so it needs no case of its own.
    /// </summary>
    private IEnumerable<string> ItemStarted(JsonElement e)
    {
        if (!e.TryGetProperty("item", out var item)) yield break;

        var type = Text(item, "type");
        if (type is null) yield break;

        var field = type switch
        {
            "command_execution" => Text(item, "command"),
            "mcp_tool_call" or "collab_tool_call" => Text(item, "tool"),
            "web_search" => Text(item, "query"),
            _ => null,
        };

        yield return field is { Length: > 0 }
            ? $"  ⏺ {type}  {StreamRender.Clip(StreamRender.Flat(StreamRender.Here(root, field)))}"
            : $"  ⏺ {type}";
    }

    /// <summary>
    /// Draws nothing unless the item failed - an <c>error</c>-typed item, or a
    /// status neither <c>completed</c> nor <c>in_progress</c>: one rule that
    /// covers every vendor status enum, present and future.
    /// </summary>
    private IEnumerable<string> ItemCompleted(JsonElement e)
    {
        if (!e.TryGetProperty("item", out var item)) yield break;

        var type = Text(item, "type");

        // Separately from whether this item failed: the last agent_message
        // wins, for a run that ends before a result event could say so itself.
        if (type == "agent_message" && Text(item, "text") is { } said)
        {
            facts.LastAssistantText = said;
            facts.Scan(said);
        }

        var status = Text(item, "status");
        var failed = type == "error" || (status != "completed" && status != "in_progress");
        if (!failed) yield break;

        var field = type switch
        {
            "command_execution" => Text(item, "aggregated_output"),
            "file_change" => Paths(item),
            "mcp_tool_call" => item.TryGetProperty("error", out var err) && Text(err, "message") is { Length: > 0 } msg
                ? msg
                : type,
            "collab_tool_call" => type,
            "error" => Text(item, "message"),
            _ => type,
        };

        if (field is { Length: > 0 })
            yield return $"  ✗ {StreamRender.Clip(StreamRender.Flat(field))}";
    }

    private static string? Paths(JsonElement item) =>
        item.TryGetProperty("changes", out var changes) && changes.ValueKind == JsonValueKind.Array
            ? string.Join(", ", changes.EnumerateArray().Select(c => Text(c, "path") ?? "").Where(p => p.Length > 0))
            : null;

    /// <summary>
    /// The four Hatch token kinds and the three live running totals, over
    /// however many <c>turn.completed</c> events arrive - ordinarily exactly
    /// one per session, but the schema does not promise it.
    /// </summary>
    private IEnumerable<string> TurnCompleted(JsonElement e)
    {
        if (e.TryGetProperty("usage", out var usage))
        {
            var cachedInput = Long(usage, "cached_input_tokens");
            var inputTokens = Long(usage, "input_tokens") - cachedInput;
            var cacheCreationTokens = Long(usage, "cache_write_input_tokens");
            var outputTokens = Long(usage, "output_tokens") + Long(usage, "reasoning_output_tokens");

            _inputTokens += inputTokens;
            _cacheReadTokens += cachedInput;
            _cacheCreationTokens += cacheCreationTokens;
            _outputTokens += outputTokens;

            TokensSoFar += inputTokens + outputTokens + cacheCreationTokens + cachedInput;

            var context = inputTokens + cacheCreationTokens + cachedInput;
            if (context > _peakContext) _peakContext = context;

            Requests += 1;
        }

        var (title, summary) = facts.WorkLogFrom(facts.LastAssistantText ?? "");
        facts.ReleaseNote = StreamRender.ReleaseNote(facts.LastAssistantText ?? "");
        facts.Result = new WorkLogEntryRequest(
            SessionId: facts.SessionId ?? "",
            StartedAt: default,
            EndedAt: default,
            DurationMs: 0,
            Title: title,
            Summary: summary,
            IsError: false,
            Turns: Requests,
            CostUsd: null,
            Models: [new WorkLogModelUseDto(model, _inputTokens, _outputTokens, _cacheCreationTokens, _cacheReadTokens, null)]);

        return [];
    }

    /// <summary>
    /// Nests one level deeper than the standalone <see cref="Error"/> event -
    /// confirmed live against the installed CLI. Leaves <see
    /// cref="RunFacts.Result"/> untouched: a null <c>Result</c> is exactly what
    /// routes <c>Increment</c>'s crashed-session branch to read these two
    /// fields for a usage-limit sentence, the same path a crashed Claude
    /// session already takes.
    /// </summary>
    private IEnumerable<string> TurnFailed(JsonElement e)
    {
        if (e.TryGetProperty("error", out var err) && Text(err, "message") is { } message)
        {
            facts.ResultText = message;
            facts.LastAssistantText = message;
        }

        return [];
    }

    /// <summary>
    /// A real failing session emits several of these before its terminal
    /// <c>turn.failed</c> - each simply overwrites the last.
    /// </summary>
    private IEnumerable<string> Error(JsonElement e)
    {
        if (Text(e, "message") is { } message)
        {
            facts.ResultText = message;
            facts.LastAssistantText = message;
        }

        return [];
    }

    private static string? Text(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long Long(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : 0;
}

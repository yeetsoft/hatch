using System.Diagnostics;
using System.Text.Json;

namespace Hatch.Cli;

/// <summary>
/// Grok Build's own event vocabulary, read back the way <see
/// cref="StreamRender"/> reads Claude's <c>stream-json</c> and <see
/// cref="CodexStreamReader"/> reads Codex's <c>--json</c> - its own file,
/// standalone from any adapter (none exists yet). First read against the
/// vendor's own headless-mode guide, 2026-10-19; every shape below is now
/// confirmed against a real 1.0.62 binary's own streaming-json and json
/// output on HA-918, the same day.
/// </summary>
/// <remarks>
/// <para><b>Session id.</b> Grok's <c>-s</c> demands a UUID before the stream
/// has said anything, so the constructor mints one eagerly (lower-case
/// hyphenated, <see cref="Guid.NewGuid"/>'s own format) rather than waiting
/// for an event the way <see cref="CodexStreamReader"/> waits for
/// <c>thread.started</c>. <c>end.sessionId</c>, or the quiet <c>json</c>
/// object's own, replaces it when it differs.</para>
///
/// <para><b>Tokens.</b> Only a dedicated <c>usage</c> event (or the quiet
/// <c>json</c> object's own top-level <c>usage</c>) drives <see
/// cref="TokensSoFar"/>, <see cref="Requests"/> and <see
/// cref="PeakContextTokens"/> - <c>end</c>'s own <c>usage</c> field is read
/// only for the work-log fallback row below, never added to these running
/// totals, since a real run's <c>usage</c> events already carried it. Total is
/// <c>input + output + cache_creation + cache_read</c>; peak context is the
/// same less output. A <c>usage</c> event is deduped by <c>messageId</c> -
/// confirmed on HA-918 that no real <c>usage</c> event carries one (only
/// <c>usage</c> and a <c>signature</c>), so every real line is counted
/// unconditionally today; the dedup stays as a defence against a future
/// vendor version that starts sending one, not a path this run ever took.
/// <c>reasoning_tokens</c> is read as already inside <c>output_tokens</c> -
/// confirmed by a real turn's own arithmetic (<c>input + cache_read +
/// output</c> alone matched the vendor's own <c>total_tokens</c>, with
/// <c>reasoning_tokens</c> adding nothing on top) - so it is never added
/// again here.</para>
///
/// <para><b>The work-log row</b>, built on <c>end</c> or the quiet object: one
/// row per <c>modelUsage</c> key, or - missing or empty - one row keyed
/// <c>"grok"</c> carrying the top-level <c>usage</c>'s counts instead.</para>
///
/// <para><b>Cost.</b> <c>total_cost_usd_ticks / 10_000_000_000m</c> when
/// present (exact), else <c>total_cost_usd</c>, else null - unpriced, never
/// zero (HA-707). Never <c>?? 0m</c>, unlike <see cref="StreamRender"/>'s own
/// Claude reading. Confirmed exact on a real turn: <c>total_cost_usd_ticks</c>
/// was precisely <c>total_cost_usd * 10_000_000_000</c>.</para>
///
/// <para><b>The final message.</b> <c>text.data</c> accumulates into a buffer
/// cleared on every <c>tool_call</c> and <c>tool_call_update</c>, so what is
/// left at <c>end</c> is the text after the last tool - <see
/// cref="RunFacts.ResultText"/>, fed to <see cref="StreamRender.WorkLog"/> for
/// the fence. Confirmed on a real multi-turn run: a streamed <c>end</c> event
/// carries no <c>text</c> field at all (only the quiet single-object
/// <c>json</c> output does), so <c>Finish</c>'s own fallback to the buffer is
/// not a fallback in practice against a stream - it is the only source. <see
/// cref="RunFacts.LastAssistantText"/> is simply the latest non-empty
/// <c>text.data</c>, overwritten rather than accumulated.</para>
///
/// <para><b>Errors.</b> An <c>error</c> event's <c>message</c> becomes both
/// <see cref="RunFacts.ResultText"/> and <see
/// cref="RunFacts.LastAssistantText"/> directly - what <c>Increment</c>'s
/// usage-limit read looks at on a run that ends without <c>end</c>, which
/// leaves <see cref="RunFacts.Result"/> null. <c>IsError</c> is true once an
/// <c>error</c> was seen, or <c>stopReason</c> is anything but
/// <c>end_turn</c>. Confirmed live on HA-918, twice: an invalid <c>-m</c>
/// produces <c>{"type":"error","message":"Couldn't set model '...': Invalid
/// params: \"unknown model id\"...","cwd":"..."}</c>, and the account's own
/// free-tier usage limit, hit mid-investigation, produced <c>{"type":"error",
/// "message":"You've reached your free Grok Build usage limit for now. Get
/// SuperGrok for much higher limits, or try again later: ...","cwd":"..."}</c>
/// - see <see cref="UsageLimit.RecogniseGrok"/>, whose one-hour backstop this
/// confirms rather than replaces: the real sentence names no reset time at
/// all.</para>
///
/// <para>This vocabulary has no rate-limit event, so <see cref="Usage"/> is
/// always empty and <see cref="UsageReadAt"/> always null - an absent battery
/// is already a supported state.</para>
/// </remarks>
internal sealed class GrokStreamReader : IAgentStreamReader
{
    private const int ThinkingStep = 3_000;

    private readonly string _root;
    private readonly RunFacts _facts;
    private readonly Func<string, string> _resumeCommand;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly HashSet<string> _counted = [];

    private string _buffer = "";
    private string _textBuffer = "";
    private bool _sawError;
    private bool _bannerPrinted;
    private long _peakContext;
    private long _thoughtChars;
    private long _thoughtsSaid;

    internal GrokStreamReader(string root, RunFacts facts, Func<string, string>? resumeCommand = null)
    {
        _root = root;
        _facts = facts;
        _resumeCommand = resumeCommand ?? (id => $"grok --resume {id}");
        _facts.SessionId = Guid.NewGuid().ToString();
    }

    public long TokensSoFar { get; private set; }

    public int Requests { get; private set; }

    public long PeakContextTokens => _peakContext;

    /// <summary>
    /// Always empty - this vocabulary carries no rate-limit event. See the
    /// class remarks.
    /// </summary>
    public IReadOnlyList<UsageWindow> Usage { get; } = [];

    public DateTimeOffset? UsageReadAt => null;

    /// <summary>
    /// Wraps <see cref="ReadEvent"/> with a one-time banner, printed off the
    /// very first call regardless of what that line parses to - the same way
    /// <see cref="RunFacts.SessionId"/> is minted before any line arrives.
    /// </summary>
    public IEnumerable<string> Read(string raw)
    {
        var lines = ReadEvent(raw);
        if (_bannerPrinted) return lines;

        _bannerPrinted = true;
        return Banner().Concat(lines);
    }

    private IEnumerable<string> Banner()
    {
        yield return $"hatch: session {_facts.SessionId}";
        yield return $"hatch:   join it with  {_resumeCommand(_facts.SessionId!)}";
    }

    private IEnumerable<string> ReadEvent(string raw)
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

        var type = Text(e, "type");
        if (type is null)
            return e.TryGetProperty("sessionId", out _) ? QuietJson(e) : [];

        return type switch
        {
            "tool_call" => ToolCall(e),
            "tool_call_update" => ToolCallUpdate(e),
            "text" => TextEvent(e),
            "thought" => Thought(e),
            "usage" => UsageEvent(e),
            "end" => Finish(e),
            "error" => Error(e),
            _ => [],
        };
    }

    private IEnumerable<string> ToolCall(JsonElement e)
    {
        _buffer = "";
        foreach (var said in FlushText()) yield return said;
        yield return $"  ⏺ {Text(e, "toolName")}  {ToolField(e)}";
    }

    /// <summary>
    /// <see cref="StreamRender.Summarise"/>'s own field-order loop, duplicated
    /// here over <c>rawInput</c> in place of <c>input</c> - that method is
    /// private and tied to <see cref="StreamRender"/>'s own <c>_root</c>.
    /// <c>target_file</c> is grok's own name for the field Claude calls
    /// <c>file_path</c> - confirmed on HA-918's real <c>read_file</c> tool
    /// call, whose <c>rawInput</c> carried <c>target_file</c> and nothing
    /// named <c>file_path</c> or <c>path</c> at all.
    /// </summary>
    private string ToolField(JsonElement e)
    {
        if (!e.TryGetProperty("rawInput", out var rawInput) || rawInput.ValueKind != JsonValueKind.Object)
            return "";

        foreach (var field in (string[])["command", "target_file", "file_path", "pattern", "description", "url", "path", "key"])
            if (Text(rawInput, field) is { Length: > 0 } value)
                return StreamRender.Clip(StreamRender.Flat(StreamRender.Here(_root, value)));

        return StreamRender.Clip(StreamRender.Flat(StreamRender.Here(_root, rawInput.ToString())));
    }

    private IEnumerable<string> ToolCallUpdate(JsonElement e)
    {
        _buffer = "";
        foreach (var said in FlushText()) yield return said;

        if (Text(e, "status") != "failed") yield break;

        var text = ToolUpdateText(e);
        var firstLine = text.ReplaceLineEndings("\n").Split('\n')[0];
        var clipped = StreamRender.Clip(StreamRender.Flat(firstLine));
        if (clipped.Length > 0) yield return $"  ✗ {clipped}";
    }

    /// <summary>
    /// <c>content</c>'s first element, which may be a flat <c>{type:"text",
    /// text:"…"}</c> block or ACP's own nested <c>{type:"content",
    /// content:{type:"text",text:"…"}}</c> wrapper - tried in that order, then
    /// the element's raw JSON - else <c>rawOutput</c>, its own string or its
    /// raw JSON text otherwise.
    /// </summary>
    private static string ToolUpdateText(JsonElement e)
    {
        if (e.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array &&
            content.EnumerateArray().FirstOrDefault() is { ValueKind: JsonValueKind.Object } first)
        {
            return Text(first, "text")
                ?? (first.TryGetProperty("content", out var nested) ? Text(nested, "text") : null)
                ?? first.ToString();
        }

        if (!e.TryGetProperty("rawOutput", out var rawOutput)) return "";

        return rawOutput.ValueKind == JsonValueKind.String ? rawOutput.GetString() ?? "" : rawOutput.ToString();
    }

    private IEnumerable<string> TextEvent(JsonElement e)
    {
        if (Text(e, "data") is { Length: > 0 } data)
        {
            _buffer += data;
            _textBuffer += data;
            _facts.LastAssistantText = data;
        }

        return [];
    }

    /// <summary>
    /// The thinking pulse. <see cref="StreamRender"/>'s own <c>ThinkingStep</c>
    /// is private, so the literal is deliberately duplicated here rather than
    /// shared.
    /// </summary>
    private IEnumerable<string> Thought(JsonElement e)
    {
        if (Text(e, "data") is { Length: > 0 } data) _thoughtChars += data.Length;

        if (_thoughtChars < _thoughtsSaid + ThinkingStep) return [];

        _thoughtsSaid = _thoughtChars;
        return ["  ✻ thinking…"];
    }

    /// <summary>
    /// Text buffered since the last flush, drawn as a blank line then its own
    /// lines - mirroring <see cref="StreamRender.Assistant"/>'s own shape.
    /// Never called from <see cref="TextEvent"/> itself, so a line is never
    /// drawn split mid-word.
    /// </summary>
    private IEnumerable<string> FlushText()
    {
        var text = _textBuffer;
        _textBuffer = "";

        if (StreamRender.Flat(text).Length == 0) return [];

        return new[] { "" }.Concat(text.ReplaceLineEndings("\n").Split('\n'));
    }

    private IEnumerable<string> UsageEvent(JsonElement e)
    {
        var flushed = FlushText().ToList();

        var messageId = Text(e, "messageId");
        if (messageId is not null && !_counted.Add(messageId)) return flushed;

        CountTokens(e);
        return flushed;
    }

    private void CountTokens(JsonElement e)
    {
        if (!e.TryGetProperty("usage", out var usage)) return;

        var input = Long(usage, "input_tokens");
        var output = Long(usage, "output_tokens");
        var cacheCreation = Long(usage, "cache_creation_input_tokens");
        var cacheRead = Long(usage, "cache_read_input_tokens");

        TokensSoFar += input + output + cacheCreation + cacheRead;
        Requests += 1;

        var context = input + cacheCreation + cacheRead;
        if (context > _peakContext) _peakContext = context;
    }

    private IEnumerable<string> Error(JsonElement e)
    {
        if (Text(e, "message") is { } message)
        {
            _facts.ResultText = message;
            _facts.LastAssistantText = message;
        }

        _sawError = true;
        return [];
    }

    /// <summary>
    /// The quiet run's single <c>json</c> object counts as both the one
    /// <c>usage</c> event a stream would have sent and the <c>end</c> that
    /// finishes it - its own top-level <c>usage</c> is read for the running
    /// totals here, since no separate <c>usage</c> event will ever arrive to
    /// do it.
    /// </summary>
    private IEnumerable<string> QuietJson(JsonElement e)
    {
        CountTokens(e);
        return Finish(e);
    }

    private IEnumerable<string> Finish(JsonElement e)
    {
        if (Text(e, "sessionId") is { Length: > 0 } id) _facts.SessionId = id;

        var resultText = Text(e, "text") ?? _buffer;
        _facts.ResultText = resultText;

        var stopReason = Text(e, "stopReason");
        var failed = _sawError || (stopReason is { Length: > 0 } && stopReason != "end_turn");

        var cost = Cost(e);
        if (cost is { } spent) _facts.CostUsd = spent;

        var (title, summary) = StreamRender.WorkLog(resultText);
        _facts.ReleaseNote = StreamRender.ReleaseNote(resultText);

        _facts.Result = new WorkLogEntryRequest(
            SessionId: _facts.SessionId ?? "",
            StartedAt: default,
            EndedAt: default,
            DurationMs: _stopwatch.ElapsedMilliseconds,
            Title: title,
            Summary: summary,
            IsError: failed,
            Turns: (int)Long(e, "num_turns"),
            CostUsd: cost,
            Models: Models(e));

        return [];
    }

    private static decimal? Cost(JsonElement e) =>
        Decimal(e, "total_cost_usd_ticks") is { } ticks ? ticks / 10_000_000_000m : Decimal(e, "total_cost_usd");

    /// <summary>
    /// One row per <c>modelUsage</c> key, Hatch's names rather than the
    /// vendor's - or, missing or empty, one row keyed <c>"grok"</c> carrying
    /// the top-level <c>usage</c>'s counts, so a run that never named a model
    /// still gets a work-log entry.
    /// </summary>
    private static List<WorkLogModelUseDto> Models(JsonElement e)
    {
        var models = new List<WorkLogModelUseDto>();

        if (e.TryGetProperty("modelUsage", out var modelUsage) && modelUsage.ValueKind == JsonValueKind.Object &&
            modelUsage.EnumerateObject().Any())
        {
            foreach (var model in modelUsage.EnumerateObject())
                models.Add(new WorkLogModelUseDto(
                    Model: model.Name,
                    InputTokens: Long(model.Value, "inputTokens"),
                    OutputTokens: Long(model.Value, "outputTokens"),
                    CacheCreationTokens: Long(model.Value, "cacheCreationInputTokens"),
                    CacheReadTokens: Long(model.Value, "cacheReadInputTokens"),
                    CostUsd: Decimal(model.Value, "costUSD")));

            return models;
        }

        if (e.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            models.Add(new WorkLogModelUseDto(
                Model: "grok",
                InputTokens: Long(usage, "input_tokens"),
                OutputTokens: Long(usage, "output_tokens"),
                CacheCreationTokens: Long(usage, "cache_creation_input_tokens"),
                CacheReadTokens: Long(usage, "cache_read_input_tokens"),
                CostUsd: Cost(e)));

        return models;
    }

    /// <summary>The prose a quiet run ended with, for the adapter's <c>ReadClosing</c>.</summary>
    internal static string? Closing(string json)
    {
        try
        {
            return JsonDocument.Parse(json).RootElement is { ValueKind: JsonValueKind.Object } e
                ? Text(e, "text")
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
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

    private static decimal? Decimal(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
            ? number
            : null;
}

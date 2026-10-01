using System.Text.Json;

namespace Hatch.Cli;

/// <summary>
/// What a probe's stream carries, out of the CLI's own <c>/usage</c> - the
/// account's windows between sessions, rather than the session stream's own
/// <c>rate_limit_event</c>. See docs/hatch.md and the decisions on HA-173.
/// </summary>
/// <remarks>
/// The CLI's own schema calls this shape experimental, so every way it can
/// fail - no <c>usage_report</c>, a null <c>rate_limits</c>, a field that does
/// not deserialize - is read as "nothing to report" rather than an error.
/// </remarks>
public static class UsageReport
{
    /// <summary>
    /// The account's windows, out of the probe's raw stream-json lines - null
    /// where nothing usable arrived at all. A limit whose <c>kind</c> this does
    /// not know is skipped rather than guessed at.
    /// </summary>
    public static IReadOnlyList<UsageWindow>? Parse(IEnumerable<string> lines)
    {
        foreach (var e in Objects(lines))
        {
            if (Text(e, "type") != "assistant") continue;
            if (Object(e, "message") is not { } message) continue;
            if (Object(message, "usage_report") is not { } report) continue;
            if (Object(report, "rate_limits") is not { } limits) continue;

            var windows = new List<UsageWindow>();

            if (limits.TryGetProperty("limits", out var array) && array.ValueKind == JsonValueKind.Array)
                foreach (var limit in array.EnumerateArray())
                    if (Limit(limit) is { } window)
                        windows.Add(window);

            if (Object(limits, "extra_usage") is { } extra && Extra(extra) is { } extraWindow)
                windows.Add(extraWindow);

            return windows;
        }

        return null;
    }

    /// <summary>
    /// The result line's own turn count, out of the same lines - above zero
    /// says the CLI sent <c>/usage</c> to a model rather than taking it as a
    /// local command, which is the one outcome a probe must never repeat.
    /// Zero where there is no result line at all: a probe that never finished
    /// cost nothing either.
    /// </summary>
    public static int Turns(IEnumerable<string> lines)
    {
        foreach (var e in Objects(lines))
        {
            if (Text(e, "type") != "result") continue;
            return (int)Long(e, "num_turns");
        }

        return 0;
    }

    private static UsageWindow? Limit(JsonElement limit)
    {
        var (window, fallback) = Text(limit, "kind") switch
        {
            "session" => ("session", "Session"),
            "weekly_all" => ("weekly", "Weekly"),
            "weekly_scoped" => ("weeklyModel", "Weekly (model)"),
            _ => ((string?)null, (string?)null),
        };
        if (window is null) return null;

        var label = window == "weeklyModel" && Name(limit) is { Length: > 0 } named
            ? $"Weekly ({named})"
            : fallback!;

        var resetsAt = Text(limit, "resets_at") is { } iso && DateTimeOffset.TryParse(iso, out var parsed)
            ? parsed
            : (DateTimeOffset?)null;

        return new UsageWindow(window, label, (Double(limit, "percent") ?? 0) / 100, resetsAt);
    }

    /// <summary>The account's own name for a scoped weekly window - never written down here.</summary>
    private static string? Name(JsonElement limit) =>
        Object(limit, "scope") is { } scope && Object(scope, "model") is { } model
            ? Text(model, "display_name")
            : null;

    /// <summary>
    /// A property that is there and is an object - never a bare <c>TryGetProperty</c>,
    /// which throws asking a JSON <c>null</c> for a property of its own rather
    /// than saying "not there".
    /// </summary>
    private static JsonElement? Object(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    private static UsageWindow? Extra(JsonElement extra)
    {
        if (!extra.TryGetProperty("is_enabled", out var enabled) || enabled.ValueKind != JsonValueKind.True)
            return null;

        return Double(extra, "utilization") is { } utilization
            ? new UsageWindow("extra", "Extra usage", utilization / 100, null)
            : null;
    }

    private static IEnumerable<JsonElement> Objects(IEnumerable<string> lines)
    {
        foreach (var raw in lines)
        {
            var line = raw.TrimStart();
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

            yield return e;
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

    private static double? Double(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            ? number
            : null;
}

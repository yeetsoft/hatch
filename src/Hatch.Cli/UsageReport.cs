using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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
/// Newer CLIs send no <c>usage_report</c> at all, only the <c>/usage</c> text
/// in the assistant line's content, which is read where the structure is absent.
/// </remarks>
public static partial class UsageReport
{
    /// <summary>
    /// The account's windows, out of the probe's raw stream-json lines - null
    /// where nothing usable arrived at all. A limit whose <c>kind</c> this does
    /// not know is skipped rather than guessed at. A structured
    /// <c>usage_report</c> wins; failing that, the <c>Current …</c> lines of the
    /// assistant's text are read, their resets relative to <paramref name="at"/>
    /// (now, when not given).
    /// </summary>
    public static IReadOnlyList<UsageWindow>? Parse(
        IEnumerable<string> lines, DateTimeOffset? at = null, TimeZoneInfo? runnerZone = null)
    {
        var all = lines as IReadOnlyList<string> ?? lines.ToList();
        return Structured(all) ?? FromText(all, at ?? DateTimeOffset.UtcNow, runnerZone);
    }

    /// <summary>
    /// What the CLI said, for a diagnostic: the assistant's text, else the
    /// result line's, else every line that is not <c>system</c> chatter - never
    /// the <c>init</c> line, which says nothing about the reading.
    /// </summary>
    public static string Said(IEnumerable<string> lines)
    {
        var all = lines as IReadOnlyList<string> ?? lines.ToList();
        if (AssistantText(all) is { Length: > 0 } text) return text;

        foreach (var e in Objects(all))
            if (Text(e, "type") == "result" && Text(e, "result") is { Length: > 0 } result)
                return result;

        var kept = all.Where(l => !Objects([l]).Any(e => Text(e, "type") == "system"));
        return string.Join('\n', kept);
    }

    private static IReadOnlyList<UsageWindow>? Structured(IReadOnlyList<string> lines)
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

    private static IReadOnlyList<UsageWindow>? FromText(
        IReadOnlyList<string> lines, DateTimeOffset at, TimeZoneInfo? runnerZone)
    {
        if (AssistantText(lines) is not { Length: > 0 } text) return null;

        var windows = new List<UsageWindow>();
        foreach (var line in text.Split('\n'))
        {
            var match = CurrentLine().Match(line);
            if (!match.Success) continue;

            var scope = match.Groups["scope"].Value.Trim();
            var (window, label) = match.Groups["kind"].Value.ToLowerInvariant() switch
            {
                "session" => ("session", "Session"),
                // A week that names no scope is the account's whole week, not a model's.
                _ when scope.Length == 0 || scope.Equals("all models", StringComparison.OrdinalIgnoreCase) => ("weekly", "Weekly"),
                _ => ("weeklyModel", $"Weekly ({scope})"),
            };

            var percent = double.Parse(match.Groups["percent"].Value, CultureInfo.InvariantCulture);
            windows.Add(new UsageWindow(
                window, label, percent / 100, UsageLimit.ResetNamed(match.Groups["rest"].Value, at, runnerZone)));
        }

        return windows.Count > 0 ? windows : null;
    }

    /// <summary>The text blocks of the first assistant line that has any, joined by newlines.</summary>
    private static string? AssistantText(IEnumerable<string> lines)
    {
        foreach (var e in Objects(lines))
        {
            if (Text(e, "type") != "assistant") continue;
            if (Object(e, "message") is not { } message) continue;
            if (!message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;

            var text = new StringBuilder();
            foreach (var block in content.EnumerateArray())
                if (Text(block, "type") == "text" && Text(block, "text") is { } t)
                    text.Append(t).Append('\n');

            if (text.Length > 0) return text.ToString();
        }

        return null;
    }

    [GeneratedRegex(
        @"^\s*Current\s+(?<kind>session|week)(?:\s*\((?<scope>[^)]+)\))?\s*:\s*(?<percent>\d+(?:\.\d+)?)\s*%\s*used(?<rest>.*)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex CurrentLine();

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

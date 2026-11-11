using System.Globalization;
using System.Text;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// <see cref="ConsumptionReport.Row"/>s as a CSV body - the spreadsheet-facing
/// sibling of the JSON route, sharing its rows rather than a second fold.
///
/// Pure, the way <see cref="ConsumptionReport"/> itself is: no database, no
/// clock, no BOM - a byte-order mark is a byte concern the controller adds,
/// while this returns a string a test reads directly.
/// </summary>
public static class ConsumptionCsv
{
    /// <summary>The eighteen columns, in the order every row lists them.</summary>
    public static readonly string[] Header =
    [
        "key", "title", "type", "project", "statusId", "parentKey",
        "sessions", "errors", "inputTokens", "outputTokens", "cacheCreationTokens", "cacheReadTokens",
        "totalTokens", "costUsd", "unpricedSessions", "runnerMs", "firstSessionAt", "lastSessionAt",
    ];

    /// <summary>The header record, then one per row, each ending <c>\r\n</c> per RFC 4180.</summary>
    public static string Write(IReadOnlyList<ConsumptionReport.Row> rows)
    {
        var text = new StringBuilder();

        text.Append(Record(Header));
        foreach (var row in rows) text.Append(Record(Fields(row)));

        return text.ToString();
    }

    private static IEnumerable<string> Fields(ConsumptionReport.Row row)
    {
        var totals = row.Totals;

        yield return row.Key;
        yield return row.Title;
        yield return row.Type;
        yield return row.ProjectKey;
        yield return row.StatusId.ToString(CultureInfo.InvariantCulture);
        yield return row.ParentKey ?? "";
        yield return totals.Sessions.ToString(CultureInfo.InvariantCulture);
        yield return totals.Errors.ToString(CultureInfo.InvariantCulture);
        yield return totals.InputTokens.ToString(CultureInfo.InvariantCulture);
        yield return totals.OutputTokens.ToString(CultureInfo.InvariantCulture);
        yield return totals.CacheCreationTokens.ToString(CultureInfo.InvariantCulture);
        yield return totals.CacheReadTokens.ToString(CultureInfo.InvariantCulture);
        yield return totals.TotalTokens.ToString(CultureInfo.InvariantCulture);
        // The cost-sort sentinel, read off the same fold rather than a second
        // rule - every session unpriced means nothing to show, not $0.
        yield return totals.UnpricedSessions == totals.Sessions
            ? ""
            : totals.CostUsd.ToString(CultureInfo.InvariantCulture);
        yield return totals.UnpricedSessions.ToString(CultureInfo.InvariantCulture);
        yield return totals.DurationMs.ToString(CultureInfo.InvariantCulture);
        yield return Instant(row.FirstSessionAt);
        yield return Instant(row.LastSessionAt);
    }

    /// <summary><see cref="IssueMoment"/>'s own instant format, copied rather than reached into - this never carries a has-time flag.</summary>
    private static string Instant(DateTimeOffset at) =>
        at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Record(IEnumerable<string> fields) => string.Join(',', fields.Select(Quote)) + "\r\n";

    private static string Quote(string field) =>
        field.IndexOfAny(['"', ',', '\r', '\n']) < 0 ? field : $"\"{field.Replace("\"", "\"\"")}\"";
}

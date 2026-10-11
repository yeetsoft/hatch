using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Whether this issue's most recent move was a send-back for an over-long
/// brief, and if so, the two numbers that said so - see
/// <see cref="WorkDto.SentBack"/> for what a session is told about it. A
/// later ordinary move is itself the newest <c>status_changed</c>, is not a
/// send-back, and so correctly reads back as cleared.
/// </summary>
public static class SentBack
{
    public readonly record struct Info(int Length, int Limit);

    /// <summary>
    /// The newest <see cref="EfHatchIssueEvent.StatusChanged"/> event, read
    /// back for a send-back - or null where there is none, or the newest one
    /// was not a send-back.
    /// </summary>
    public static Info? From(IEnumerable<(string Kind, string? Payload)> eventsNewestFirst)
    {
        foreach (var e in eventsNewestFirst)
        {
            if (e.Kind != EfHatchIssueEvent.StatusChanged) continue;
            return Parse(e.Payload);
        }

        return null;
    }

    /// <summary>The single-issue query <see cref="From"/> is run against, for a named dispatch.</summary>
    public static async Task<Info?> ForIssueAsync(HatchContext db, long issueId, CancellationToken ct)
    {
        var events = await db.IssueEvents.AsNoTracking()
            .Where(e => e.IssueId == issueId)
            .OrderByDescending(e => e.At).ThenByDescending(e => e.Id)
            .Select(e => new { e.Kind, e.Payload })
            .ToListAsync(ct);

        return From(events.Select(e => (e.Kind, e.Payload)));
    }

    private static Info? Parse(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (!root.TryGetProperty("sendback", out var sendback) || sendback.ValueKind != JsonValueKind.True)
                return null;

            if (!root.TryGetProperty("length", out var lengthEl) || !lengthEl.TryGetInt32(out var length))
                return null;
            if (!root.TryGetProperty("limit", out var limitEl) || !limitEl.TryGetInt32(out var limit))
                return null;

            return new Info(length, limit);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

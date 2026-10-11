using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// How many times in a row a send-back hop has bounced this issue between the
/// implementation column and its feeder, uncounted by <see cref="LetGo"/>
/// because each bounce is a <c>status_changed</c>, not a release - see
/// <see cref="Dispatch.Blocked"/>'s own fold for what the count is for.
/// </summary>
public static class SendBackStreak
{
    /// <summary>
    /// A round trip writes two events, newest first: the send-back itself
    /// (<c>sendback: true</c>), and - further back - the ordinary advance that
    /// returned the issue to <paramref name="implementationName"/>, carrying no
    /// <c>sendback</c> flag. The walk skips that return trip rather than
    /// breaking on it - counted as neither a hit nor a stop - because breaking
    /// on any non-send-back <c>status_changed</c> would cap the streak at 1 and
    /// the condition this exists for could never fire. Any other
    /// <c>status_changed</c> - a real advance, not a round trip - breaks.
    /// </summary>
    public static int Count(IEnumerable<(string Kind, string? Payload)> eventsNewestFirst, string implementationName)
    {
        var streak = 0;
        foreach (var e in eventsNewestFirst)
        {
            if (e.Kind != EfHatchIssueEvent.StatusChanged) continue;

            if (IsSendBack(e.Payload)) { streak++; continue; }
            if (IsReturnToImplementation(e.Payload, implementationName)) continue;

            break;
        }

        return streak;
    }

    /// <summary>The single-issue query <see cref="Count"/> is run against, for a named dispatch.</summary>
    public static async Task<int> ForIssueAsync(HatchContext db, long issueId, string implementationName, CancellationToken ct)
    {
        var events = await db.IssueEvents.AsNoTracking()
            .Where(e => e.IssueId == issueId)
            .OrderByDescending(e => e.At).ThenByDescending(e => e.Id)
            .Select(e => new { e.Kind, e.Payload })
            .ToListAsync(ct);

        return Count(events.Select(e => (e.Kind, e.Payload)), implementationName);
    }

    /// <summary>
    /// The same count for however many issues a scan is judging at once - one
    /// query, grouped, with <see cref="Count"/> run once a group. Ungated,
    /// unlike <see cref="LetGo.CountsAsync"/>: no <c>StalledAt</c>-shaped
    /// pre-filter exists for a send-back streak, so this runs over every id
    /// it is handed.
    /// </summary>
    public static async Task<Dictionary<long, int>> CountsAsync(
        HatchContext db, IReadOnlyList<long> issueIds, string implementationName, CancellationToken ct)
    {
        var events = await db.IssueEvents.AsNoTracking()
            .Where(e => issueIds.Contains(e.IssueId))
            .OrderByDescending(e => e.At).ThenByDescending(e => e.Id)
            .Select(e => new { e.IssueId, e.Kind, e.Payload })
            .ToListAsync(ct);

        return events
            .GroupBy(e => e.IssueId)
            .ToDictionary(g => g.Key, g => Count(g.Select(e => (e.Kind, e.Payload)), implementationName));
    }

    /// <summary>Whether a <c>status_changed</c> event's payload marks it as a send-back hop.</summary>
    private static bool IsSendBack(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return false;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.TryGetProperty("sendback", out var value) && value.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Whether a <c>status_changed</c> event's payload names the implementation column as its destination.</summary>
    private static bool IsReturnToImplementation(string? payload, string implementationName)
    {
        if (string.IsNullOrWhiteSpace(payload)) return false;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.TryGetProperty("to", out var value) && value.ValueKind == JsonValueKind.String
                && value.GetString() == implementationName;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// One definition of "let go", used by everything that asks - see
/// <see cref="WorkDto.LetGo"/> for what the count means. Two callers ask
/// the same thing for different reasons: <see cref="WorkController"/> for a
/// single named issue, and <see cref="Dispatch.ScanAsync"/> for however many a
/// pass is judging at once - the same bulk/single split
/// <see cref="Questions.DispatchCountsAsync"/> and
/// <see cref="WorkController"/>'s own <c>UnlapsedWaitingAsync</c> already use
/// for an open question's own lapse rule.
/// </summary>
public static class LetGo
{
    /// <summary>
    /// How many increments in a row let this ticket go without moving it: its
    /// releases, newest first, whose outcome was <see cref="ClaimOutcomes.Dropped"/>,
    /// counted back to the first release whose outcome was
    /// <see cref="ClaimOutcomes.Worked"/>, a status change, or an answer a
    /// person wrote - whichever comes first. A release with no outcome, and
    /// an answer written by a lapse, are skipped rather than counted or
    /// stopped at.
    /// </summary>
    public static int Count(IEnumerable<(string Kind, string? Payload)> eventsNewestFirst)
    {
        var letGo = 0;
        foreach (var e in eventsNewestFirst)
        {
            if (e.Kind == EfHatchIssueEvent.StatusChanged) break;

            if (e.Kind == EfHatchIssueEvent.ClaimReleased)
            {
                var outcome = ReleaseOutcome(e.Payload);
                if (outcome == ClaimOutcomes.Dropped) { letGo++; continue; }
                if (outcome == ClaimOutcomes.Worked) break;
                continue; // no outcome: skipped over, neither counted nor stopped at
            }

            if (e.Kind == EfHatchIssueEvent.Answered)
            {
                if (!IsLapsedAnswer(e.Payload)) break;
                continue; // an answer written by a lapse: skipped over, the same as a bare release
            }
        }

        return letGo;
    }

    /// <summary>The single-issue query <see cref="Count"/> is run against, for a named dispatch.</summary>
    public static async Task<int> ForIssueAsync(HatchContext db, long issueId, CancellationToken ct)
    {
        var events = await db.IssueEvents.AsNoTracking()
            .Where(e => e.IssueId == issueId)
            .OrderByDescending(e => e.At).ThenByDescending(e => e.Id)
            .Select(e => new { e.Kind, e.Payload })
            .ToListAsync(ct);

        return Count(events.Select(e => (e.Kind, e.Payload)));
    }

    /// <summary>
    /// The same count for however many issues a scan is judging at once - one
    /// query over the marked issues' events, grouped, with <see cref="Count"/>
    /// run once a group. Issues absent from <paramref name="issueIds"/> cost
    /// nothing; a scan asks only for the ones it marked.
    /// </summary>
    public static async Task<Dictionary<long, int>> CountsAsync(
        HatchContext db, IReadOnlyList<long> issueIds, CancellationToken ct)
    {
        var events = await db.IssueEvents.AsNoTracking()
            .Where(e => issueIds.Contains(e.IssueId))
            .OrderByDescending(e => e.At).ThenByDescending(e => e.Id)
            .Select(e => new { e.IssueId, e.Kind, e.Payload })
            .ToListAsync(ct);

        return events
            .GroupBy(e => e.IssueId)
            .ToDictionary(g => g.Key, g => Count(g.Select(e => (e.Kind, e.Payload))));
    }

    /// <summary>A release's own outcome, or null where it did not say - see <see cref="ClaimOutcomes"/>.</summary>
    private static string? ReleaseOutcome(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.TryGetProperty("outcome", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Whether an <c>answered</c> event's payload marks it as written by a lapse rather than by a person.</summary>
    private static bool IsLapsedAnswer(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return false;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.TryGetProperty("lapsed", out var value) && value.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

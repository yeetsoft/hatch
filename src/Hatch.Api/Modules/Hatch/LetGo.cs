using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>The runner that let an issue go last, and when - see <see cref="LetGo.By"/>.</summary>
public sealed record LetGoBy(string Runner, DateTimeOffset At);

/// <summary>
/// One definition of "let go", used by everything that asks - see
/// <see cref="WorkDto.LetGo"/> for what the count means. Two callers ask
/// the same thing for different reasons: <see cref="WorkController"/> for a
/// single named issue, and <see cref="Dispatch.ScanAsync"/> for however many a
/// pass is judging at once.
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

    /// <summary>
    /// Who let this ticket go most recently, if nothing has happened to it
    /// since that would make the drop stale. Walks the trail newest first: a
    /// release with outcome <see cref="ClaimOutcomes.Dropped"/> that names its
    /// runner is the record; the first release that is anything else (worked,
    /// preempted, no outcome), the first status change, or the first answer a
    /// person wrote means there is none. Takes, lapses, clears and comments
    /// are stepped over. A drop with no runner - one written before the
    /// payload carried it - records nothing.
    /// </summary>
    public static LetGoBy? By(IEnumerable<(string Kind, string? Payload, DateTimeOffset At)> eventsNewestFirst)
    {
        foreach (var e in eventsNewestFirst)
        {
            if (e.Kind == EfHatchIssueEvent.StatusChanged) return null;

            if (e.Kind == EfHatchIssueEvent.ClaimReleased)
            {
                if (ReleaseOutcome(e.Payload) == ClaimOutcomes.Dropped && ReleaseRunner(e.Payload) is { } runner)
                    return new LetGoBy(runner, e.At);
                return null;
            }

            if (e.Kind == EfHatchIssueEvent.Answered && !IsLapsedAnswer(e.Payload)) return null;
        }

        return null;
    }

    /// <summary>
    /// <see cref="By"/> for every issue <paramref name="runner"/> let go of and
    /// has not been followed on since. One prefilter query finds the issues
    /// whose trail holds a drop by that runner at all; only their trails are
    /// loaded.
    /// </summary>
    public static async Task<Dictionary<long, LetGoBy>> ByAsync(HatchContext db, string runner, CancellationToken ct)
    {
        var named = "\"runner\":" + JsonSerializer.Serialize(runner);

        var ids = await db.IssueEvents.AsNoTracking()
            .Where(e => e.Kind == EfHatchIssueEvent.ClaimReleased
                && e.Payload!.Contains("\"outcome\":\"dropped\"")
                && e.Payload.Contains(named))
            .Select(e => e.IssueId)
            .Distinct()
            .ToListAsync(ct);

        var by = new Dictionary<long, LetGoBy>();
        if (ids.Count == 0) return by;

        var events = await db.IssueEvents.AsNoTracking()
            .Where(e => ids.Contains(e.IssueId))
            .OrderByDescending(e => e.At).ThenByDescending(e => e.Id)
            .Select(e => new { e.IssueId, e.Kind, e.Payload, e.At })
            .ToListAsync(ct);

        foreach (var group in events.GroupBy(e => e.IssueId))
        {
            // The prefilter matched on the runner's name anywhere in a drop's
            // payload; the newest drop may be another runner's, which moves
            // the record to them.
            if (By(group.Select(e => (e.Kind, e.Payload, e.At))) is { } found
                && string.Equals(found.Runner, runner, StringComparison.Ordinal))
                by[group.Key] = found;
        }

        return by;
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

    /// <summary>The runner a release names, or null where it did not - older releases never did.</summary>
    private static string? ReleaseRunner(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.TryGetProperty("runner", out var value)
                && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrEmpty(value.GetString())
                    ? value.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Whether an <c>answered</c> event's payload marks it as written by a lapse rather than by a person.</summary>
    internal static bool IsLapsedAnswer(string? payload)
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

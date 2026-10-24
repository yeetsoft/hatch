using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// How many claims in a row have lapsed on one ticket - each taken over after
/// going quiet or expiring - with nothing between them that says the ticket is
/// getting anywhere. The counterpart of <see cref="LetGo"/> for the one way an
/// increment can end that writes no release outcome at all: the lease is lost,
/// so the session is stopped and nothing further is written, and
/// <see cref="LetGo.Count"/> skips such a release rather than counting it. A
/// ticket whose work can never finish inside its quiet window therefore never
/// looked like a stall, and was taken again for as long as the board ran.
/// </summary>
public static class LapseStreak
{
    /// <summary>
    /// The <c>claim_lapsed</c> events newest first, counted back to the first of:
    /// a release whose outcome was <see cref="ClaimOutcomes.Worked"/>, a status
    /// change, or an answer a person wrote. A release with no outcome or a dropped
    /// one is stepped over - what a lapse says is that the session was stopped,
    /// not what it did - and so is an answer written by a lapse.
    /// </summary>
    public static int Count(IEnumerable<(string Kind, string? Payload)> eventsNewestFirst)
    {
        var lapses = 0;
        foreach (var e in eventsNewestFirst)
        {
            if (e.Kind == EfHatchIssueEvent.StatusChanged) break;
            if (e.Kind == EfHatchIssueEvent.ClaimLapsed) { lapses++; continue; }
            if (e.Kind == EfHatchIssueEvent.ClaimReleased && LetGo.ReleaseOutcome(e.Payload) == ClaimOutcomes.Worked) break;
            if (e.Kind == EfHatchIssueEvent.Answered && !LetGo.IsLapsedAnswer(e.Payload)) break;
        }

        return lapses;
    }

    /// <summary>The count over a single ticket's saved events.</summary>
    public static async Task<int> ForIssueAsync(HatchContext db, long issueId, CancellationToken ct)
    {
        var events = await db.IssueEvents.AsNoTracking()
            .Where(e => e.IssueId == issueId)
            .OrderByDescending(e => e.At).ThenByDescending(e => e.Id)
            .Select(e => new { e.Kind, e.Payload })
            .ToListAsync(ct);

        return Count(events.Select(e => (e.Kind, e.Payload)));
    }
}

using Hatch.Contracts;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// What landed in the newest night, out of every landing in the house.
///
/// Pure and synchronous - no <see cref="HatchContext"/>, no query - because
/// reading "an issue landed" off the database is a different job
/// (<c>OvernightController</c>'s, wiring this to <c>GET /api/hatch/overnight</c>)
/// and this fold is tested without one.
/// </summary>
public static class Overnight
{
    /// <summary>
    /// How long a silence has to run before one night ends and the next
    /// begins. Four hours: a working loop lands something far more often
    /// than that, and a silence this long is a loop that has stopped or a
    /// person who has gone to bed.
    /// </summary>
    public static readonly TimeSpan QuietGap = TimeSpan.FromHours(4);

    /// <summary>Epic, reflection, bug, story, task - deliberately not
    /// <see cref="EfHatchIssue.Types"/>, which is the order a picker offers them in.</summary>
    private static readonly string[] TypeOrder = ["epic", "reflection", "bug", "story", "task"];

    /// <summary>One landed issue, as the fold reads it.</summary>
    public readonly record struct Landing(
        long IssueId, string Key, string Type, string Title, string ProjectKey,
        DateTimeOffset At, int DescendantCount);

    /// <summary>
    /// The newest night: the run of landings nearest <paramref name="landings"/>'s
    /// own newest, split off by a gap of more than <see cref="QuietGap"/> on
    /// either side. Empty input answers an empty night - no <c>From</c>/<c>To</c>,
    /// zero rows - never a refusal.
    /// </summary>
    /// <param name="seenAt">
    /// This reader's own mark, or null for a reader who has never read the
    /// page - in which case every row in the night is unseen.
    /// </param>
    public static OvernightDto NewestNight(IReadOnlyList<Landing> landings, DateTimeOffset? seenAt = null)
    {
        if (landings.Count == 0) return new OvernightDto(null, null, 0, []);

        var byTime = landings
            .OrderByDescending(l => l.At)
            .ThenByDescending(l => l.IssueId)
            .ToList();

        var night = new List<Landing> { byTime[0] };
        for (var i = 1; i < byTime.Count; i++)
        {
            if (byTime[i - 1].At - byTime[i].At > QuietGap) break;
            night.Add(byTime[i]);
        }

        var rows = night
            .OrderBy(TypeRank)
            .ThenByDescending(l => l.DescendantCount)
            .ThenByDescending(l => l.At)
            .ThenByDescending(l => l.IssueId)
            .Select(l => new OvernightRowDto(l.Key, l.Type, l.Title, l.ProjectKey, l.At, l.DescendantCount))
            .ToList();

        // Strictly later than the mark: a landing exactly at it is the mark,
        // not after it.
        var unseen = night.Count(l => seenAt is null || l.At > seenAt.Value);

        return new OvernightDto(night.Min(l => l.At), night.Max(l => l.At), night.Count, rows, unseen);
    }

    private static int TypeRank(Landing l) => Array.IndexOf(TypeOrder, l.Type) is var i and >= 0 ? i : TypeOrder.Length;
}

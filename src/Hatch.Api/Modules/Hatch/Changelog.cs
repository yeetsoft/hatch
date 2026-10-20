using Hatch.Contracts;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// What shipped, day by day, out of every close in the house.
///
/// Pure and synchronous - no <see cref="HatchContext"/>, no query - because
/// reading "an issue closed" off the database is a different job
/// (<c>ChangelogController</c>'s, wiring this to <c>GET /api/hatch/changelog</c>)
/// and this fold is tested without one.
/// </summary>
public static class Changelog
{
    /// <summary>Story, bug, task - the brief's reading order for a day's fixes.
    /// Deliberately not <see cref="EfHatchIssue.Types"/>, which is a picker's order.</summary>
    private static readonly string[] FixTypeOrder = ["story", "bug", "task"];

    /// <summary>One closed, parentless issue, as the fold reads it.</summary>
    public readonly record struct Entry(
        long IssueId, string Key, string Title, string Type, string ProjectKey,
        string? ReleaseNote, DateTimeOffset ClosedAt, int DescendantCount);

    /// <summary>
    /// The window a page asking for <paramref name="days"/> days before
    /// <paramref name="before"/> lands on, in UTC day terms.
    /// </summary>
    /// <param name="OldestDayStart">That day's own UTC midnight - the lower bound
    /// query 2 fetches rows with; null when nothing is eligible.</param>
    /// <param name="Oldest">The same day, as the wire's <c>yyyy-MM-dd</c>; null with <see cref="OldestDayStart"/>.</param>
    /// <param name="HasOlder">Whether an eligible close exists on a day older than <see cref="Oldest"/>.</param>
    public readonly record struct Window(DateTimeOffset? OldestDayStart, string? Oldest, bool HasOlder);

    /// <summary>
    /// Walks <paramref name="closes"/> newest first, collecting distinct UTC
    /// days until <paramref name="days"/> of them have shipped something - a
    /// quiet stretch between two days that did is never counted, so it never
    /// has to be skipped.
    /// </summary>
    public static Window ResolveWindow(IReadOnlyList<DateTimeOffset> closes, DateTimeOffset before, int days)
    {
        var eligible = closes.Where(c => c < before).OrderByDescending(c => c).ToList();
        if (eligible.Count == 0) return new Window(null, null, false);

        var dayKeys = new List<DateOnly>();
        foreach (var close in eligible)
        {
            var key = DateOnly.FromDateTime(close.UtcDateTime);
            if (dayKeys.Count > 0 && dayKeys[^1] == key) continue;
            if (dayKeys.Count == days) break;
            dayKeys.Add(key);
        }

        var oldestDay = dayKeys[^1];
        var oldestStart = new DateTimeOffset(oldestDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var hasOlder = eligible.Any(c => DateOnly.FromDateTime(c.UtcDateTime) < oldestDay);

        return new Window(oldestStart, oldestDay.ToString("yyyy-MM-dd"), hasOlder);
    }

    /// <summary>
    /// Every closed month <paramref name="closes"/> touches, newest first -
    /// unbounded by any window, which is what makes this safe to compute off
    /// the same unfiltered-by-window read <see cref="ResolveWindow"/> uses.
    /// </summary>
    public static IReadOnlyList<ChangelogMonthDto> Months(IReadOnlyList<DateTimeOffset> closes) =>
        closes
            .GroupBy(c => new DateOnly(c.UtcDateTime.Year, c.UtcDateTime.Month, 1))
            .OrderByDescending(g => g.Key)
            .Select(g => new ChangelogMonthDto(g.Key.ToString("yyyy-MM"), g.Count()))
            .ToList();

    /// <summary>
    /// <paramref name="windowEntries"/>, grouped into days and ordered the way
    /// the brief reads a day: features (epics) before fixes, and within each,
    /// later close first - fixes break a tie on type first, story then bug
    /// then task.
    /// </summary>
    public static IReadOnlyList<ChangelogDayDto> Days(IReadOnlyList<Entry> windowEntries) =>
        windowEntries
            .GroupBy(e => DateOnly.FromDateTime(e.ClosedAt.UtcDateTime))
            .OrderByDescending(g => g.Key)
            .Select(g => new ChangelogDayDto(
                g.Key.ToString("yyyy-MM-dd"),
                Ordered(g.Where(e => e.Type == "epic")),
                Ordered(g.Where(e => e.Type != "epic"))))
            .ToList();

    private static IReadOnlyList<ChangelogEntryDto> Ordered(IEnumerable<Entry> entries) =>
        entries
            .OrderBy(FixRank)
            .ThenByDescending(e => e.ClosedAt)
            .ThenByDescending(e => e.IssueId)
            .Select(e => new ChangelogEntryDto(e.Key, e.Title, e.Type, e.ProjectKey, e.ReleaseNote, e.ClosedAt, e.DescendantCount))
            .ToList();

    private static int FixRank(Entry e) => Array.IndexOf(FixTypeOrder, e.Type) is var i and >= 0 ? i : FixTypeOrder.Length;
}

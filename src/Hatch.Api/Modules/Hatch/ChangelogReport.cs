namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Closed top-level epics, grouped into the buckets a changelog draws them in.
///
/// Pure in the way <see cref="TimelineReport"/> and <see cref="WorkLogRollup"/>
/// are - rows and a range in, buckets out, with no database and no clock, so
/// the rules can be pinned by tests that build the rows in memory.
/// </summary>
public static class ChangelogReport
{
    public enum ChangelogBucketSize { Hour, Day, Week, Month }

    /// <summary>The wire form of a bucket size, which is its lowercase name.</summary>
    public static string Name(ChangelogBucketSize bucket) => bucket switch
    {
        ChangelogBucketSize.Hour => "hour",
        ChangelogBucketSize.Day => "day",
        ChangelogBucketSize.Week => "week",
        _ => "month",
    };

    /// <summary>
    /// One closed issue, as the fold needs it. <see cref="Type"/> is the
    /// issue's own type; <see cref="ParentType"/> is its parent's, or null with
    /// no parent. Nothing about "closed" is checked here - every row the fold
    /// receives is assumed already closed, since that is the caller's job.
    /// </summary>
    public readonly record struct Row(long Id, string? ParentType, string Type, DateTimeOffset ClosedAt);

    /// <summary>One bucket: the window it covers, and the epics closed inside it.</summary>
    public sealed record Bucket(DateTimeOffset Start, DateTimeOffset End, IReadOnlyList<long> EpicIds);

    private const string EpicType = "epic";

    /// <summary>
    /// Top-level epics closed in <c>[from, to)</c>, bucketed and ordered newest
    /// first. A bucket with nothing in it is never emitted - no dense grid, no
    /// point cap, unlike <see cref="CumulativeFlowReport"/>'s.
    /// </summary>
    public static IReadOnlyList<Bucket> Fold(
        IReadOnlyList<Row> rows, DateTimeOffset from, DateTimeOffset to, ChangelogBucketSize bucket, int offsetMinutes)
    {
        var kept = rows.Where(r =>
            r.Type == EpicType && r.ParentType != EpicType && r.ClosedAt >= from && r.ClosedAt < to);

        return kept
            .Select(r => (Row: r, Bounds: BoundsOf(r.ClosedAt, bucket, offsetMinutes)))
            .GroupBy(x => x.Bounds)
            .OrderByDescending(g => g.Key.Start)
            .Select(g => new Bucket(
                g.Key.Start,
                g.Key.End,
                g.OrderByDescending(x => x.Row.ClosedAt).ThenByDescending(x => x.Row.Id).Select(x => x.Row.Id).ToList()))
            .ToList();
    }

    /// <summary>One bucket: the window it covers, and the loose issues closed inside it.</summary>
    public sealed record LooseBucket(DateTimeOffset Start, DateTimeOffset End, IReadOnlyList<long> IssueIds);

    /// <summary>
    /// Closed bugs and parentless stories in <c>[from, to)</c>, bucketed and
    /// ordered newest first - the same shape as <see cref="Fold"/>, but for the
    /// issues that ride a changelog's loose list rather than its epic groups.
    /// A bug lands here whatever its parent is; a story only with no parent at
    /// all. A bucket with nothing in it is never emitted.
    /// </summary>
    public static IReadOnlyList<LooseBucket> FoldLoose(
        IReadOnlyList<Row> rows, DateTimeOffset from, DateTimeOffset to, ChangelogBucketSize bucket, int offsetMinutes)
    {
        var kept = rows.Where(r =>
            (r.Type == "bug" || (r.Type == "story" && r.ParentType == null)) && r.ClosedAt >= from && r.ClosedAt < to);

        return kept
            .Select(r => (Row: r, Bounds: BoundsOf(r.ClosedAt, bucket, offsetMinutes)))
            .GroupBy(x => x.Bounds)
            .OrderByDescending(g => g.Key.Start)
            .Select(g => new LooseBucket(
                g.Key.Start,
                g.Key.End,
                g.OrderByDescending(x => x.Row.ClosedAt).ThenByDescending(x => x.Row.Id).Select(x => x.Row.Id).ToList()))
            .ToList();
    }

    /// <summary>
    /// One closed story, as <see cref="FoldStories"/> needs it. <see cref="EpicId"/>
    /// is the nearest epic above it, already walked by the caller, or null with
    /// no epic above it at all.
    /// </summary>
    public readonly record struct StoryRow(long Id, long? EpicId, DateTimeOffset ClosedAt);

    /// <summary>
    /// One epic that might anchor a group in <see cref="FoldStories"/>.
    /// <see cref="ClosedAt"/> is its own close, or null while it is still open;
    /// which epics are worth passing in at all is the caller's call, not this
    /// fold's - it only reads <see cref="ClosedAt"/> against the window.
    /// </summary>
    public readonly record struct EpicCandidate(long Id, DateTimeOffset? ClosedAt);

    /// <summary>One epic's stories closed in a bucket, or its own bare close, or both.</summary>
    public sealed record StoryGroup(long EpicId, bool ClosedHere, IReadOnlyList<long> StoryIds);

    /// <summary>One bucket: the window it covers, and the story groups inside it.</summary>
    public sealed record StoryBucket(DateTimeOffset Start, DateTimeOffset End, IReadOnlyList<StoryGroup> Groups);

    /// <summary>
    /// Closed stories in <c>[from, to)</c>, grouped under their epic and bucketed
    /// alongside any of <paramref name="epics"/> whose own close also falls in the
    /// window - a bare milestone where an epic closed with no story of its own
    /// closing in that same bucket. A story with no epic above it makes no group
    /// here; it is left for the caller to list elsewhere.
    /// </summary>
    public static IReadOnlyList<StoryBucket> FoldStories(
        IReadOnlyList<EpicCandidate> epics, IReadOnlyList<StoryRow> stories,
        DateTimeOffset from, DateTimeOffset to, ChangelogBucketSize bucket, int offsetMinutes)
    {
        var groups = new Dictionary<
            (DateTimeOffset Start, DateTimeOffset End, long EpicId),
            (List<long> StoryIds, bool ClosedHere, DateTimeOffset SortAt)>();

        foreach (var g in stories
            .Where(s => s.EpicId != null && s.ClosedAt >= from && s.ClosedAt < to)
            .Select(s => (Story: s, Bounds: BoundsOf(s.ClosedAt, bucket, offsetMinutes)))
            .GroupBy(x => (x.Bounds.Start, x.Bounds.End, EpicId: x.Story.EpicId!.Value)))
        {
            var ordered = g.OrderByDescending(x => x.Story.ClosedAt).ThenByDescending(x => x.Story.Id).ToList();
            groups[g.Key] = (ordered.Select(x => x.Story.Id).ToList(), false, ordered[0].Story.ClosedAt);
        }

        foreach (var epic in epics)
        {
            if (epic.ClosedAt is not { } closedAt || closedAt < from || closedAt >= to)
            {
                continue;
            }

            var bounds = BoundsOf(closedAt, bucket, offsetMinutes);
            var key = (bounds.Start, bounds.End, epic.Id);
            var storyIds = groups.TryGetValue(key, out var existing) ? existing.StoryIds : [];
            groups[key] = (storyIds, true, closedAt);
        }

        return groups
            .GroupBy(kv => (kv.Key.Start, kv.Key.End))
            .OrderByDescending(g => g.Key.Start)
            .Select(g => new StoryBucket(
                g.Key.Start,
                g.Key.End,
                g.OrderByDescending(kv => kv.Value.SortAt)
                    .ThenByDescending(kv => kv.Key.EpicId)
                    .Select(kv => new StoryGroup(kv.Key.EpicId, kv.Value.ClosedHere, kv.Value.StoryIds))
                    .ToList()))
            .ToList();
    }

    /// <summary>
    /// The bucket <paramref name="at"/> falls into on a reader whose wall clock
    /// is <paramref name="offsetMinutes"/> away from UTC: floored onto that
    /// reader's hour, day, Monday-to-Monday week, or calendar month, then
    /// shifted back out to the UTC instants the wire carries as <c>start</c>
    /// and <c>end</c>.
    /// </summary>
    internal static (DateTimeOffset Start, DateTimeOffset End) BoundsOf(
        DateTimeOffset at, ChangelogBucketSize bucket, int offsetMinutes)
    {
        var local = at.UtcDateTime.AddMinutes(offsetMinutes);

        DateTime startLocal, endLocal;
        switch (bucket)
        {
            case ChangelogBucketSize.Hour:
                startLocal = new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0);
                endLocal = startLocal.AddHours(1);
                break;
            case ChangelogBucketSize.Day:
                startLocal = local.Date;
                endLocal = startLocal.AddDays(1);
                break;
            case ChangelogBucketSize.Week:
                // Monday is day 0.
                startLocal = local.Date.AddDays(-(((int)local.DayOfWeek + 6) % 7));
                endLocal = startLocal.AddDays(7);
                break;
            default:
                startLocal = new DateTime(local.Year, local.Month, 1);
                endLocal = startLocal.AddMonths(1);
                break;
        }

        return (
            new DateTimeOffset(startLocal, TimeSpan.Zero).AddMinutes(-offsetMinutes),
            new DateTimeOffset(endLocal, TimeSpan.Zero).AddMinutes(-offsetMinutes));
    }
}

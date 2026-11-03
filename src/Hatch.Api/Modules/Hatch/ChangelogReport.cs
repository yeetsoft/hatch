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

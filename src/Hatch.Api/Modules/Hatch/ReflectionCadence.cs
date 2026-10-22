namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// When a project's next reflection falls due, out of its cadence and the
/// last period it reflected on.
///
/// Pure and synchronous - no <see cref="HatchContext"/>, no clock read -
/// because reading a project's own cadence off the database is a different
/// job and this rule is tested without one.
/// </summary>
public static class ReflectionCadence
{
    /// <summary>
    /// The start of the last period it reflected on plus the cadence, or
    /// <paramref name="now"/> where it never has - null where reflection is
    /// off.
    /// </summary>
    public static DateTimeOffset? NextDue(int? cadenceDays, DateTimeOffset? lastPeriodStart, DateTimeOffset now) =>
        cadenceDays is not { } days ? null : lastPeriodStart is { } start ? start.AddDays(days) : now;

    /// <summary>Whether reflection is due now - false where <see cref="NextDue"/> is null.</summary>
    public static bool IsDue(int? cadenceDays, DateTimeOffset? lastPeriodStart, DateTimeOffset now) =>
        NextDue(cadenceDays, lastPeriodStart, now) is { } due && due <= now;

    /// <summary>
    /// The title and description a reflection is filed with, covering the
    /// <paramref name="days"/> before <paramref name="windowEnd"/> - the shape
    /// <see cref="ReflectionCadenceJob"/> and a pressed reflect-now both use, so
    /// a filed issue reads the same whichever path filed it.
    /// </summary>
    public static (string Title, string Description, DateTimeOffset WindowStart) Describe(string projectKey, int days, DateTimeOffset windowEnd)
    {
        var windowStart = windowEnd.AddDays(-days);
        var title = $"Reflection: {windowStart:yyyy-MM-dd} to {windowEnd:yyyy-MM-dd}";
        var description = $"The reflection for {projectKey} covering {windowStart:yyyy-MM-dd} to {windowEnd:yyyy-MM-dd}.";
        return (title, description, windowStart);
    }
}

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
}

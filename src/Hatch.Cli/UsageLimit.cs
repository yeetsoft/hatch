using System.Globalization;
using System.Text.RegularExpressions;

namespace Hatch.Cli;

/// <summary>
/// Whether a session ended because the Claude account it ran under hit its
/// usage limit, read from whatever the session said on its way out - never
/// from an API somebody else's runner would have to authenticate to reach.
/// </summary>
/// <remarks>
/// <para>The one sample this is built from is the sentence a runner's terminal
/// showed: <c>You've hit your session limit · resets 7:40pm (America/New_York)</c>.
/// A time with no date names the next occurrence of that clock time after the
/// session ended, in the named zone - so an hour already past today means
/// tomorrow. A weekly limit is the same sentence with a date in front of the
/// time, and that date is taken as given rather than rolled forward.</para>
///
/// <para>An unknown zone id degrades to UTC rather than giving up - a wrong
/// offset on a reset time nobody would have read exactly anyway is better than
/// throwing the sentence away. A sentence that names a limit but whose time
/// cannot be parsed at all is <see cref="Recognised.ResetKnown"/> false, and
/// callers are the ones who turn that into the one-hour backstop.</para>
/// </remarks>
public static partial class UsageLimit
{
    /// <summary>What a session that hit its limit said, read out of its own words.</summary>
    /// <param name="ResetAt">When the account resets - parsed from the sentence, or the one-hour backstop.</param>
    /// <param name="ResetKnown">Whether <see cref="ResetAt"/> came from the sentence, or is the backstop.</param>
    public readonly record struct Recognised(DateTimeOffset ResetAt, bool ResetKnown);

    /// <summary>The backstop for a limit whose reset time could not be read.</summary>
    public static readonly TimeSpan UnknownWindow = TimeSpan.FromHours(1);

    /// <summary>
    /// Whether <paramref name="said"/> - the CLI's own result text, or failing
    /// that the last thing the assistant said - reports a usage limit. Null for
    /// anything else, including an ordinary error.
    /// </summary>
    public static Recognised? Recognise(string? said, DateTimeOffset endedAt)
    {
        if (string.IsNullOrWhiteSpace(said)) return null;
        if (!Mention().IsMatch(said)) return null;

        var match = ClockPattern().Match(said);
        if (!match.Success) return new Recognised(endedAt + UnknownWindow, ResetKnown: false);

        var zone = ResolveZone(match.Groups["zone"].Value.Trim());

        if (!TryHour(match, out var hour24, out var minute))
            return new Recognised(endedAt + UnknownWindow, ResetKnown: false);

        var local = TimeZoneInfo.ConvertTime(endedAt, zone);

        DateOnly date;
        if (match.Groups["month"].Success && TryDate(match, local, out var named))
        {
            date = named;
        }
        else
        {
            date = DateOnly.FromDateTime(local.DateTime);
            var todayAt = new DateTime(date.Year, date.Month, date.Day, hour24, minute, 0);
            if (todayAt <= local.DateTime) date = date.AddDays(1);
        }

        var wall = new DateTime(date.Year, date.Month, date.Day, hour24, minute, 0, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(wall, zone);
        var offset = zone.GetUtcOffset(utc);
        return new Recognised(new DateTimeOffset(utc, TimeSpan.Zero).ToOffset(offset), ResetKnown: true);
    }

    /// <summary>
    /// 7:40pm, lowercase, the way a terminal or a ticket says a reset time - in
    /// the zone the session itself reported, carried on <paramref name="at"/>'s
    /// own offset rather than whatever zone the runner's machine happens to sit
    /// in, so the same sentence comes back regardless of where this runs.
    /// </summary>
    public static string Clock(DateTimeOffset at) =>
        at.ToString("h:mmtt", CultureInfo.InvariantCulture).ToLowerInvariant();

    private static bool TryHour(Match match, out int hour24, out int minute)
    {
        hour24 = 0;
        minute = 0;
        if (!int.TryParse(match.Groups["hour"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var hour)) return false;
        if (hour is < 1 or > 12) return false;

        minute = match.Groups["minute"].Success
            ? int.Parse(match.Groups["minute"].Value, CultureInfo.InvariantCulture)
            : 0;

        var pm = match.Groups["meridiem"].Value.Equals("pm", StringComparison.OrdinalIgnoreCase);
        hour24 = (hour % 12) + (pm ? 12 : 0);
        return true;
    }

    private static bool TryDate(Match match, DateTimeOffset local, out DateOnly date)
    {
        date = default;
        var month = Month(match.Groups["month"].Value);
        if (month is null || !int.TryParse(match.Groups["day"].Value, CultureInfo.InvariantCulture, out var day)) return false;

        var y = 0;
        var namedYear = match.Groups["year"].Success && int.TryParse(match.Groups["year"].Value, CultureInfo.InvariantCulture, out y);
        var year = namedYear ? y : local.Year;

        try
        {
            date = new DateOnly(year, month.Value, day);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        // No year was named, and the guessed one reads as already past: a
        // weekly limit whose sentence crosses a new year - ended late
        // December, resets in January - means next year, not a year ago.
        if (!namedYear && date < DateOnly.FromDateTime(local.DateTime))
        {
            try
            {
                date = date.AddYears(1);
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        return true;
    }

    private static int? Month(string name)
    {
        for (var m = 1; m <= 12; m++)
        {
            var full = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(m);
            var abbrev = CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(m);
            if (name.Equals(full, StringComparison.OrdinalIgnoreCase) || name.Equals(abbrev, StringComparison.OrdinalIgnoreCase))
                return m;
        }

        return null;
    }

    /// <summary>
    /// An IANA id resolved the way a wrong one is met everywhere else in this
    /// repository: a fallback rather than an exception, because a reset time is
    /// worth having even under the wrong offset.
    /// </summary>
    private static TimeZoneInfo ResolveZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    [GeneratedRegex(@"hit your\b.*\blimit\b", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Mention();

    [GeneratedRegex(
        @"resets\s+(?:(?<month>[A-Za-z]{3,9})\.?\s+(?<day>\d{1,2})(?:,?\s*(?<year>\d{4}))?\s*(?:at\s+)?)?" +
        @"(?<hour>\d{1,2})(?::(?<minute>\d{2}))?\s*(?<meridiem>am|pm)\s*\(\s*(?<zone>[^)]+?)\s*\)",
        RegexOptions.IgnoreCase)]
    private static partial Regex ClockPattern();
}

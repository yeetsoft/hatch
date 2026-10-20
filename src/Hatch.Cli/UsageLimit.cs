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
/// <para>A session limit is never more than its own window away, so an
/// undated session time that rolls further than the window and an hour of
/// slack is not tomorrow's: it is a reset that has just passed, read at a
/// minute after the account's own reset trailed it. That is
/// <see cref="Recognised.Passed"/>, and the answer is a short retry instead
/// of a wait the length of a day.</para>
///
/// <para>An unknown zone id degrades to the runner's own zone rather than
/// giving up - a wrong offset on a reset time nobody would have read exactly
/// anyway is better than throwing the sentence away. The shipped binary is
/// invariant-globalization, and Windows cannot read an IANA id without ICU, so
/// there every named zone degrades this way. A sentence that names a limit but
/// whose time cannot be parsed at all is <see cref="Recognised.ResetKnown"/> false, and
/// callers are the ones who turn that into the one-hour backstop.</para>
/// </remarks>
public static partial class UsageLimit
{
    /// <summary>What a session that hit its limit said, read out of its own words.</summary>
    /// <param name="ResetAt">When the account resets - parsed from the sentence, or the one-hour backstop.</param>
    /// <param name="ResetKnown">Whether <see cref="ResetAt"/> came from the sentence, or is the backstop.</param>
    /// <param name="Window">"session" or "weekly", read off the sentence itself.</param>
    /// <param name="Passed">
    /// Whether the reset the sentence named had already passed, so
    /// <see cref="ResetAt"/> is <see cref="PassedRetry"/> after the session
    /// ended rather than the named time.
    /// </param>
    public readonly record struct Recognised(DateTimeOffset ResetAt, bool ResetKnown, string Window, bool Passed = false);

    /// <summary>The backstop for a limit whose reset time could not be read.</summary>
    public static readonly TimeSpan UnknownWindow = TimeSpan.FromHours(1);

    /// <summary>How long after the session ended a reset that has already passed is tried again.</summary>
    public static readonly TimeSpan PassedRetry = TimeSpan.FromMinutes(5);

    /// <summary>The slack past a session window that still reads as that window, for a sentence that rounds to the hour.</summary>
    public static readonly TimeSpan WindowSlack = TimeSpan.FromHours(1);

    /// <summary>
    /// How long a limit's own window runs - the CLI's own copy, since it does
    /// not reference <c>Hatch.Api</c>'s <c>Utilization.WindowLengths</c>. Keys
    /// match the literal strings already used elsewhere: <see cref="Recognised.Window"/>,
    /// and <c>UsageReport.Limit</c>'s own "session"/"weekly_all"/"weekly_scoped" mapping.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, TimeSpan> WindowLengths = new Dictionary<string, TimeSpan>
    {
        ["session"] = TimeSpan.FromHours(5),
        ["weekly"] = TimeSpan.FromDays(7),
    };

    /// <summary>
    /// Whether <paramref name="said"/> - the CLI's own result text, or failing
    /// that the last thing the assistant said - reports a usage limit. Null for
    /// anything else, including an ordinary error.
    /// </summary>
    public static Recognised? Recognise(string? said, DateTimeOffset endedAt, TimeZoneInfo? runnerZone = null)
    {
        if (string.IsNullOrWhiteSpace(said)) return null;
        if (!Mention().IsMatch(said)) return null;

        var window = said.Contains("weekly", StringComparison.OrdinalIgnoreCase) ? "weekly" : "session";

        var match = ClockPattern().Match(said);
        if (!match.Success) return new Recognised(endedAt + UnknownWindow, ResetKnown: false, window);

        var runner = runnerZone ?? TimeZoneInfo.Local;
        var zone = match.Groups["zone"].Success
            ? ResolveZone(match.Groups["zone"].Value.Trim(), runner)
            : runner;

        if (!TryHour(match, out var hour24, out var minute))
            return new Recognised(endedAt + UnknownWindow, ResetKnown: false, window);

        var local = TimeZoneInfo.ConvertTime(endedAt, zone);

        DateOnly date;
        var rolled = false;
        if (match.Groups["month"].Success && TryDate(match, local, out var named))
        {
            date = named;
        }
        else
        {
            date = DateOnly.FromDateTime(local.DateTime);
            var todayAt = new DateTime(date.Year, date.Month, date.Day, hour24, minute, 0);
            if (todayAt <= local.DateTime) date = date.AddDays(1);
            rolled = true;
        }

        var wall = new DateTime(date.Year, date.Month, date.Day, hour24, minute, 0, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(wall, zone);
        var offset = zone.GetUtcOffset(utc);

        if (rolled && window == "session" && new DateTimeOffset(utc, TimeSpan.Zero) - endedAt > WindowLengths["session"] + WindowSlack)
            return new Recognised(TimeZoneInfo.ConvertTime(endedAt + PassedRetry, zone), ResetKnown: true, window, Passed: true);

        return new Recognised(new DateTimeOffset(utc, TimeSpan.Zero).ToOffset(offset), ResetKnown: true, window);
    }

    /// <summary>
    /// Whether <paramref name="said"/> - the Codex CLI's own result text or its
    /// error - reports a usage limit. A sentence in <see cref="Recognise"/>'s
    /// clock form is read by it; any other limit report is the one-hour
    /// backstop, <see cref="Recognised.ResetKnown"/> false. Null for anything
    /// else, including an ordinary error.
    /// </summary>
    public static Recognised? RecogniseCodex(string? said, DateTimeOffset endedAt, TimeZoneInfo? runnerZone = null)
    {
        if (string.IsNullOrWhiteSpace(said)) return null;

        var claude = Recognise(said, endedAt, runnerZone);
        if (claude is not null) return claude;

        if (!CodexMention().IsMatch(said)) return null;

        // No real Codex limit sentence has been seen yet. When HA-910's proof
        // (or any run) sees one, its reset-time regex goes here, ahead of this
        // backstop; until then every Codex limit report rests an hour.
        var window = said.Contains("weekly", StringComparison.OrdinalIgnoreCase) ? "weekly" : "session";
        return new Recognised(endedAt + UnknownWindow, ResetKnown: false, window);
    }

    /// <summary>
    /// Whether <paramref name="said"/> - the Grok CLI's own result text or its
    /// error - reports a usage limit: the one-hour backstop,
    /// <see cref="Recognised.ResetKnown"/> false, window <c>session</c>. Null
    /// for anything else, including an ordinary error and the per-minute
    /// <c>429</c> / <c>rate limit exceeded</c> throttle, which Grok retries
    /// itself and which is not an account running out.
    /// </summary>
    /// <remarks>
    /// The sentences, read from the vendor's open-source repository on
    /// 2026-10-19 (<c>xai-grok-shell/src/sampling/error.rs</c> and
    /// <c>session/compaction.rs</c>), none naming a reset time:
    /// <list type="bullet">
    /// <item>You’ve reached your free Grok Build usage limit for now. Get SuperGrok for much higher limits, or try again later: ...</item>
    /// <item>Grok Build usage limit reached</item>
    /// <item>You hit your free usage limit.</item>
    /// </list>
    /// </remarks>
    public static Recognised? RecogniseGrok(string? said, DateTimeOffset endedAt)
    {
        if (string.IsNullOrWhiteSpace(said)) return null;
        if (!GrokMention().IsMatch(said)) return null;

        // HA-918's own investigation hit the account's free-tier limit for
        // real, mid-run: a headless error event carrying exactly the first
        // sentence above, word for word, and naming no reset time - the same
        // backstop this already falls back to. No run has yet seen either of
        // the other two sentences, or one naming a time; this stays the
        // regex's place to land if one ever does.
        return new Recognised(endedAt + UnknownWindow, ResetKnown: false, "session");
    }

    /// <summary>
    /// 7:40pm, lowercase, the way a terminal or a ticket says a reset time.
    /// When the sentence itself named a zone (<paramref name="known"/>), that
    /// is read off <paramref name="at"/>'s own carried offset, so the same
    /// sentence comes back regardless of where this runs. Otherwise
    /// <paramref name="at"/> is the one-hour backstop - built from
    /// <see cref="DateTimeOffset.UtcNow"/> and so carrying a <c>+00:00</c> that
    /// means nothing - and is converted to <paramref name="clock"/>'s own local
    /// zone first, the house way of reading a local now.
    /// </summary>
    public static string Clock(DateTimeOffset at, bool known, TimeProvider? clock = null)
    {
        var shown = known ? at : TimeZoneInfo.ConvertTime(at, (clock ?? TimeProvider.System).LocalTimeZone);
        return shown.ToString("h:mmtt", CultureInfo.InvariantCulture).ToLowerInvariant();
    }

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
    /// worth having even under the wrong offset. The fallback is the runner's
    /// own zone, not UTC: the shipped binary is invariant-globalization, and
    /// Windows cannot read an IANA id without ICU, so on a Windows runner every
    /// id lands here, and the runner's zone is the one a Claude account most
    /// likely resets in.
    /// </summary>
    private static TimeZoneInfo ResolveZone(string id, TimeZoneInfo fallback)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return fallback;
        }
    }

    [GeneratedRegex(@"hit your\b.*\blimit\b|out of usage credits\b",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Mention();

    [GeneratedRegex(@"usage[ _]limit|rate[ _]limit|usage_limit_reached|rate_limit_exceeded|hit your\b.*\blimit\b",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CodexMention();

    [GeneratedRegex(@"usage limit", RegexOptions.IgnoreCase)]
    private static partial Regex GrokMention();

    [GeneratedRegex(
        @"resets\s+(?:(?<month>[A-Za-z]{3,9})\.?\s+(?<day>\d{1,2})(?:,\s*(?<year>\d{4}))?" +
        @"\s*(?:,|at)?\s*)?(?<hour>\d{1,2})(?::(?<minute>\d{2}))?\s*(?<meridiem>am|pm)" +
        @"(?:\s*\(\s*(?<zone>[^)]+?)\s*\))?",
        RegexOptions.IgnoreCase)]
    private static partial Regex ClockPattern();
}

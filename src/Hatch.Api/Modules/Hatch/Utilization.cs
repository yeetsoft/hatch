using System.Text.Json;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// One limit window, in Hatch's own vocabulary rather than the account's - the
/// same four fields a runner's heartbeat carries.
///
/// <paramref name="ResetsAt"/> is nullable because a model-scoped weekly window
/// that has not been touched arrives with no reset instant at all. Anything
/// that subtracts two instants has to survive that - a ring with nothing to run
/// down to is drawn as unknown, not as full and not as empty.
/// </summary>
public record UtilizationLimit(string Window, string Label, int Percent, DateTimeOffset? ResetsAt);

/// <summary>A whole reading: how good it is, when it was taken, and what it said.</summary>
/// <remarks>
/// <paramref name="State"/> is the whole of the degraded story, so a client
/// reads one field rather than inferring staleness from a timestamp:
/// <see cref="UtilizationStates.Ok"/> read within <see cref="Utilization.FreshFor"/>
/// of <paramref name="ReadAt"/>; <see cref="UtilizationStates.Stale"/> outside
/// it - still the last thing this account is known to have reported, whose age
/// <paramref name="ReadAt"/> gives. There is no third state: a reading is kept,
/// not swept, and the endpoint answers <c>204</c> rather than a value where no
/// runner of the caller's has ever reported one.
/// </remarks>
public record UtilizationReading(string State, DateTimeOffset ReadAt, IReadOnlyList<UtilizationLimit> Limits);

public static class UtilizationStates
{
    public const string Ok = "ok";
    public const string Stale = "stale";
}

/// <summary>
/// One pass's answer about an account's room to spend on economy work -
/// mutually exclusive with itself: exactly one of the two is non-null.
/// </summary>
public sealed record EconomyPace(string? Fold, string? ClearNote)
{
    public static EconomyPace Behind(string fold) => new(fold, null);
    public static EconomyPace Clear(string? note) => new(null, note);
}

/// <summary>
/// One read of an account's usage, judged twice: once at economy's stricter
/// arithmetic, once at low's laxer one. See <see cref="Utilization.Pace"/> and
/// <see cref="Utilization.SessionPace"/>.
/// </summary>
public sealed record PaceReadings(EconomyPace Economy, EconomyPace Low);

/// <summary>The windows a runner's heartbeat reports - see <see cref="RunnerUsageWindowDto"/>.</summary>
public static class UtilizationWindows
{
    public const string Session = "session";
    public const string Weekly = "weekly";
    public const string WeeklyModel = "weeklyModel";

    /// <summary>
    /// The account's own extra-usage balance, off the CLI's <c>/usage</c>
    /// probe between sessions - never in a session's own stream. See HA-173.
    /// </summary>
    public const string Extra = "extra";
}

/// <summary>
/// The one judgement the endpoint makes: which of a person's runners holds the
/// freshest reading, and whether that reading still reads as current.
///
/// The freshest <em>whole</em> reading wins where somebody has two runners, not
/// a per-window merge - see HA-134's decisions. One person has one Claude
/// login, so two runners are two readings of one account, and stitching windows
/// out of two of them could put a session percentage beside a weekly one taken
/// an hour apart.
/// </summary>
public static class Utilization
{
    /// <summary>A reading younger than this reads as current. Older is stale, not hidden and not drawn as though it were current.</summary>
    public static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How many points of an economy issue's windows are kept in hand - the
    /// account projects to reset with at least this much of each window
    /// unspent before a pass will touch one. See <see cref="Pace"/>.
    /// </summary>
    public const int Reserve = 10;

    /// <summary>
    /// How long each window Hatch knows the length of - the only two an
    /// economy pass can project a pace for. A window with no length here (an
    /// account vocabulary Hatch has never seen) is skipped, the same way one
    /// with no <see cref="UtilizationLimit.ResetsAt"/> is.
    /// </summary>
    private static readonly Dictionary<string, TimeSpan> WindowLengths = new()
    {
        [UtilizationWindows.Session] = TimeSpan.FromHours(5),
        [UtilizationWindows.Weekly] = TimeSpan.FromDays(7),
        [UtilizationWindows.WeeklyModel] = TimeSpan.FromDays(7),
    };

    /// <summary>
    /// The caller's own reading, or null where none of <paramref name="runners"/>
    /// has ever reported one - the endpoint's <c>204</c>.
    /// </summary>
    public static UtilizationReading? Of(IEnumerable<EfHatchRunner> runners, DateTimeOffset now)
    {
        EfHatchRunner? freshest = null;
        foreach (var runner in runners)
        {
            if (runner.Usage is null || runner.UsageReadAt is not { } at) continue;
            if (freshest is null || at > freshest.UsageReadAt) freshest = runner;
        }

        if (freshest is null) return null;

        var readAt = freshest.UsageReadAt!.Value;
        var state = now - readAt < FreshFor ? UtilizationStates.Ok : UtilizationStates.Stale;
        var windows = JsonSerializer.Deserialize<List<RunnerUsageWindowDto>>(freshest.Usage!) ?? [];

        return new UtilizationReading(
            state, readAt, windows.Select(w => new UtilizationLimit(w.Window, w.Label, w.Percent, w.ResetsAt)).ToList());
    }

    /// <summary>
    /// Whether an account's reading has a gap to spend an economy issue into -
    /// every window ahead of its own pace, with <see cref="Reserve"/> points
    /// held back - and the sentence for whichever reading it is not: the
    /// clearest of several windows that qualifies, if ahead, or the worst
    /// offender, if behind. Decided here rather than in <c>Dispatch</c> because
    /// a provider's window length is written down nowhere else - see the
    /// remarks on <see cref="UtilizationLimit"/> for why a window may carry no
    /// <see cref="UtilizationLimit.ResetsAt"/> at all, which this skips rather
    /// than guesses at.
    /// </summary>
    public static EconomyPace Pace(UtilizationReading reading, DateTimeOffset now) =>
        PaceFor(reading, now, Reserve, "economy", "a gap", _ => true);

    /// <summary>
    /// Low's own pace, over the session window alone and with nothing held in
    /// reserve - see "What 'green' is" on HA-226. Strictly laxer than
    /// <see cref="Pace"/>: every reading that clears economy's gate clears
    /// this one too.
    /// </summary>
    public static EconomyPace SessionPace(UtilizationReading reading, DateTimeOffset now) =>
        PaceFor(reading, now, reserve: 0, "low", "the window to catch up",
            window => window == UtilizationWindows.Session);

    private static EconomyPace PaceFor(
        UtilizationReading reading, DateTimeOffset now, int reserve, string label, string waitsFor,
        Func<string, bool> includeWindow)
    {
        double? worstMargin = null;
        UtilizationLimit? worstLimit = null;

        foreach (var limit in reading.Limits)
        {
            if (!includeWindow(limit.Window)) continue; // not one of the windows this judgement reads
            if (limit.ResetsAt is not { } resetsAt) continue; // no reset instant - skipped, not guessed at
            if (!WindowLengths.TryGetValue(limit.Window, out var length)) continue; // a window Hatch has no length for

            if (resetsAt <= now) continue; // already reset since the reading - unconditionally ahead, never the worst

            var elapsedPercent = 100 * (1 - (resetsAt - now) / length);
            var margin = elapsedPercent - limit.Percent; // >= reserve is ahead; the display number either way

            if (worstMargin is null || margin < worstMargin)
            {
                worstMargin = margin;
                worstLimit = limit;
            }
        }

        if (worstLimit is null || worstMargin is null) return EconomyPace.Clear(null); // nothing to be behind on - vacuously ahead

        var phrase = WindowPhrase(worstLimit.Window);

        if (worstMargin < reserve)
        {
            var elapsedPercent = Math.Round(worstMargin.Value + worstLimit.Percent);
            return EconomyPace.Behind(
                $"{label} - the account has spent {worstLimit.Percent}% of its {phrase} with {elapsedPercent}% of it elapsed; this waits for {waitsFor}");
        }

        return EconomyPace.Clear($"{label}: {Math.Round(worstMargin.Value)} points ahead of pace on the {phrase}");
    }

    private static string WindowPhrase(string window) => window switch
    {
        UtilizationWindows.Session => "session window",
        UtilizationWindows.Weekly => "weekly window",
        UtilizationWindows.WeeklyModel => "weekly model window",
        _ => $"{window} window",
    };
}

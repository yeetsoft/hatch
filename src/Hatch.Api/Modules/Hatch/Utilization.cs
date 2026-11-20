using System.Text.Json;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.EntityFrameworkCore;

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

/// <summary>
/// One runner of the caller's, behind the headline: whether it is live, and
/// its own reading on the same terms the headline is judged on.
///
/// <paramref name="State"/>, <paramref name="ReadAt"/>, <paramref name="ReceivedAt"/>
/// and <paramref name="Limits"/> are all null together where this runner has
/// never reported a reading of its own - up, or just linked, but silent so
/// far - so a client can tell "no reading yet" from "not linked" without a
/// second field.
///
/// <paramref name="Source"/> and <paramref name="ProbeOff"/> are what the
/// runner last said about where that reading came from, and why its probe is
/// off, read off the same reading as the rest: null where it said nothing, and
/// so null whenever there is no reading.
/// </summary>
public record UtilizationRunner(
    string Name, bool Live, string? State, DateTimeOffset? ReadAt, DateTimeOffset? ReceivedAt,
    IReadOnlyList<UtilizationLimit>? Limits, string? Source = null, string? ProbeOff = null);

/// <summary>A whole reading: how good it is, when it was taken, and what it said.</summary>
/// <remarks>
/// <paramref name="State"/> is the whole of the degraded story, so a client
/// reads one field rather than inferring staleness from a timestamp:
/// <see cref="UtilizationStates.Ok"/> read within <see cref="Utilization.FreshFor"/>
/// of <paramref name="ReceivedAt"/>; <see cref="UtilizationStates.Stale"/> outside
/// it - still the last thing this account is known to have reported, whose age
/// <paramref name="ReceivedAt"/> gives. There is no third state: a reading is kept,
/// not swept, and the endpoint answers <c>204</c> rather than a value where no
/// runner of the caller's has ever reported one.
///
/// <paramref name="ReadAt"/> stays the runner's own word for when it read the
/// account - a clock that can run ahead or behind Hatch's own. Freshness is
/// judged on <paramref name="ReceivedAt"/>, Hatch's own clock at the moment
/// this reading actually changed.
///
/// <paramref name="Runners"/> is the panel behind the headline: one entry per
/// live runner of the caller's, plus the runner that holds the headline even
/// once it is no longer live, so the headline is never unexplained. See
/// <see cref="Utilization.PanelAsync"/>.
/// </remarks>
public record UtilizationReading(
    string State, DateTimeOffset ReadAt, DateTimeOffset ReceivedAt, IReadOnlyList<UtilizationLimit> Limits,
    IReadOnlyList<UtilizationRunner> Runners, string? Source = null, string? ProbeOff = null);

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
    /// The windows off a non-empty reading, as JSON, percent clamped 0..100 -
    /// it is drawn as a fill, and a fill is a fraction of something. Null where
    /// the request carries none, so the caller can tell "nothing to write" from
    /// "write this".
    /// </summary>
    public static string? Serialise(IReadOnlyList<RunnerUsageWindowDto>? usage) =>
        usage is not { Count: > 0 } windows
            ? null
            : JsonSerializer.Serialize(windows.Select(w => w with { Percent = Math.Clamp(w.Percent, 0, 100) }));

    /// <summary>
    /// The caller's own reading, or null where none of <paramref name="runners"/>
    /// has ever reported one - the endpoint's <c>204</c>.
    /// </summary>
    public static UtilizationReading? Of(IEnumerable<EfHatchRunner> runners, DateTimeOffset now) =>
        Of(runners.Select(r => (r.Usage, r.UsageReadAt, r.UsageReceivedAt, r.UsageSource, r.UsageProbeOff)), now);

    /// <summary>
    /// The same judgement over bare <c>(usage, read time, received time, source,
    /// probe-off reason)</c> tuples, the last two carried onto whichever
    /// reading is freshest, so a runner row and a <see cref="EfHatchRunnerSession"/> - both
    /// of which carry the account's last reading - are weighed together.
    /// Ordered, and judged <c>ok</c>/<c>stale</c>, on <c>ReceivedAt ?? ReadAt</c>:
    /// a row written before <see cref="EfHatchRunner.UsageReceivedAt"/> existed
    /// carries no received time and falls back to its own read time, so nothing
    /// goes dark on upgrade.
    /// </summary>
    public static UtilizationReading? Of(
        IEnumerable<(string? Usage, DateTimeOffset? ReadAt, DateTimeOffset? ReceivedAt, string? Source, string? ProbeOff)> readings,
        DateTimeOffset now)
    {
        (string Usage, DateTimeOffset ReadAt, DateTimeOffset ReceivedAt, string? Source, string? ProbeOff)? freshest = null;
        foreach (var (usage, readAt, receivedAt, source, probeOff) in readings)
        {
            if (usage is null || readAt is not { } at) continue;
            var receivedOrRead = receivedAt ?? at;
            if (freshest is null || receivedOrRead > freshest.Value.ReceivedAt)
                freshest = (usage, at, receivedOrRead, source, probeOff);
        }

        if (freshest is not { } best) return null;

        var state = now - best.ReceivedAt < FreshFor ? UtilizationStates.Ok : UtilizationStates.Stale;
        var windows = JsonSerializer.Deserialize<List<RunnerUsageWindowDto>>(best.Usage) ?? [];

        return new UtilizationReading(
            state, best.ReadAt, best.ReceivedAt,
            windows.Select(w => new UtilizationLimit(w.Window, w.Label, w.Percent, w.ResetsAt)).ToList(), [],
            best.Source, best.ProbeOff);
    }

    /// <summary>
    /// A person's reading from everything that carries one: the runner rows
    /// beating for them, and the sessions of the keys they own. A current CLI
    /// beats a session and writes no runner row under its own name, so a read
    /// of rows alone would leave its account with no reading at all. With no
    /// <paramref name="keys"/> to say whose a session is, only rows are read.
    /// </summary>
    public static async Task<UtilizationReading?> ReadAsync(
        HatchContext db, IAuthService? keys, Guid personId, DateTimeOffset now, CancellationToken ct)
    {
        var readings = (await db.LegacyRunners.AsNoTracking()
                .Where(r => r.ForPersonId == personId && r.Usage != null)
                .Select(r => new { r.Usage, r.UsageReadAt, r.UsageReceivedAt, r.UsageSource, r.UsageProbeOff })
                .ToListAsync(ct))
            .Select(r => (r.Usage, r.UsageReadAt, r.UsageReceivedAt, r.UsageSource, r.UsageProbeOff))
            .ToList();

        if (keys is not null)
        {
            var owned = (await keys.ListApiKeysAsync(ct))
                .Where(k => k.OwnerPersonId == personId)
                .Select(k => k.Id)
                .ToList();

            if (owned.Count > 0)
                readings.AddRange((await db.RunnerSessions.AsNoTracking()
                        .Where(s => owned.Contains(s.ApiKeyId) && s.Usage != null)
                        .Select(s => new { s.Usage, s.UsageReadAt, s.UsageReceivedAt, s.UsageSource, s.UsageProbeOff })
                        .ToListAsync(ct))
                    .Select(s => (s.Usage, s.UsageReadAt, s.UsageReceivedAt, s.UsageSource, s.UsageProbeOff)));
        }

        return Of(readings, now);
    }

    /// <summary>
    /// The headline, plus the panel behind it: one entry per runner of
    /// <paramref name="personId"/>'s that is live now, or that holds the
    /// headline reading even though it has gone quiet - see
    /// <see cref="RunnerEntries"/>. Null stays null, the endpoint's own
    /// <c>204</c>, since a caller with no reading has no panel either.
    /// </summary>
    public static async Task<UtilizationReading?> PanelAsync(
        HatchContext db, IAuthService keys, Runners runnerRules, RunnerSessions sessionRules, Guid personId,
        DateTimeOffset now, CancellationToken ct)
    {
        var headline = await ReadAsync(db, keys, personId, now, ct);
        if (headline is null) return null;

        var mine = (await Runner.JoinAsync(db, keys, ct)).Where(r => r.OwnerPersonId == personId).ToList();
        var entries = RunnerEntries(mine, runnerRules, sessionRules, headline, now);

        return headline with { Runners = entries };
    }

    /// <summary>
    /// The panel's own rows: a live runner of <paramref name="mine"/>'s, or
    /// the one runner whose own reading matches <paramref name="headline"/>'s
    /// exactly - the account-wide freshest is always the best triple of
    /// exactly one runner's own set, so that match is exactly one entry in
    /// the ordinary case. Pure, so a test builds <see cref="Runner"/> records
    /// by hand rather than seeding a database.
    ///
    /// A beat and a key sharing one name under two different people - already
    /// unresolved anywhere else in this area - is out of scope here: this does
    /// not re-derive the headline from this same join, it only matches against
    /// the one already read.
    /// </summary>
    public static IReadOnlyList<UtilizationRunner> RunnerEntries(
        IReadOnlyList<Runner> mine, Runners runnerRules, RunnerSessions sessionRules, UtilizationReading headline,
        DateTimeOffset now)
    {
        var entries = new List<UtilizationRunner>();

        foreach (var r in mine)
        {
            var triples = new List<(string? Usage, DateTimeOffset? ReadAt, DateTimeOffset? ReceivedAt, string? Source, string? ProbeOff)>();
            if (r.Beat is { } beat) triples.Add((beat.Usage, beat.UsageReadAt, beat.UsageReceivedAt, beat.UsageSource, beat.UsageProbeOff));
            if (r.Sessions is { } sessions)
                triples.AddRange(sessions.Select(s => (s.Usage, s.UsageReadAt, s.UsageReceivedAt, s.UsageSource, s.UsageProbeOff)));

            var own = Of(triples, now);
            var live = r.IsLive(runnerRules, sessionRules, now);
            var isHeadline = own is not null && own.ReadAt == headline.ReadAt && own.ReceivedAt == headline.ReceivedAt;

            if (!live && !isHeadline) continue;

            entries.Add(new UtilizationRunner(r.Name, live, own?.State, own?.ReadAt, own?.ReceivedAt, own?.Limits, own?.Source, own?.ProbeOff));
        }

        return entries;
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

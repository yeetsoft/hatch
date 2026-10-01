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
}

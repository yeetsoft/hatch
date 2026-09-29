namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Deploy-time config for Hatch (the "Hatch" appsettings section), shaped like
/// <see cref="Hatch.Api.Services.Auth.AuthOptions"/> and bound by
/// <see cref="HatchModule"/> - a module owns its own registrations
/// (Modules/README.md).
/// </summary>
/// <remarks>
/// Nothing here is written into appsettings.json. The default below is the
/// value, and a section that only ever restated it would be a second place for
/// it to drift.
/// </remarks>
public class HatchOptions
{
    public const string SectionName = "Hatch";

    /// <summary>
    /// How long a claim survives without a heartbeat - see
    /// <see cref="EfHatchIssue.ClaimToken"/>. Long enough that an increment
    /// pausing on a slow build does not lose its lease, short enough that a
    /// runner killed at the wall clock frees its ticket before anybody is
    /// waiting on it.
    /// </summary>
    /// <remarks>
    /// Read through <see cref="IssueClaims.TtlSeconds"/> and nowhere else, so
    /// the guard against a value that would make every claim dead on arrival
    /// lives in one place. Returned to the client in the claim response rather
    /// than configured on both sides: the server honours it, so the server says
    /// what it is.
    /// </remarks>
    public int ClaimTtlSeconds { get; set; } = 300;

    /// <summary>
    /// How long a runner may go without a heartbeat before its row reads
    /// <em>gone</em> - see <see cref="EfHatchRunner.LastSeenAt"/>. Shorter than
    /// a claim's TTL on purpose: a claim is a lease that must outlast a slow
    /// build, and this is a control surface, where a runner that stopped
    /// answering a minute ago is worth saying so about.
    /// </summary>
    /// <remarks>
    /// <para>Read through <see cref="Runners.GoneAfterSeconds"/> and nowhere
    /// else, guarded there the way the TTL above is.</para>
    ///
    /// <para>The second horizon - the one past which a row is not returned at
    /// all - is deliberately not a setting. It is a fixed multiple of this
    /// (<see cref="Runners.DropMultiple"/>), because the two are not
    /// independent judgements: one says "this runner is not answering" and the
    /// other says "this runner is not coming back", and an installation that
    /// could set them apart could set the second shorter than the first and
    /// have rows vanish before they were ever gone.</para>
    /// </remarks>
    public int RunnerGoneAfterSeconds { get; set; } = 90;

    /// <summary>
    /// How long a stall question or a claim may go untouched before it lapses -
    /// see <see cref="IssueClaims.StallLapseSeconds"/> and
    /// <see cref="Questions.IsLapsed"/>. Named in minutes rather than hours or
    /// seconds: the whole point of this setting is that it is small enough an
    /// operator will actually type it in.
    /// </summary>
    /// <remarks>
    /// Read through <see cref="IssueClaims.StallLapseSeconds"/> and nowhere
    /// else, with the same three-way guard <see cref="ClaimTtlSeconds"/> has
    /// with one difference: zero turns lapsing off rather than falling back,
    /// because "never lapse" is a setting an operator may actually want, and a
    /// negative value is the misconfiguration the fallback is for.
    /// </remarks>
    public int StallLapseMinutes { get; set; } = 5;
}

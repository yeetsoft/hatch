using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Hatch.Contracts;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Which runners a caller may see: the one rule the Runners page and the lanes
/// on the board health read both keep, so the two cannot disagree about whose
/// runner is whose.
/// </summary>
public static class RunnerReach
{
    /// <summary>
    /// Whether every runner may be shown, or only the ones <c>OwnerPersonId</c>
    /// ties to <c>PersonId</c>: the wall off, a person (cookie) calling, or a
    /// key whose owner is an admin all see everything; anyone else sees only
    /// their own.
    /// </summary>
    /// <remarks>
    /// A positive "is a person" test, not <c>!IsProgramAsync</c>, so a caller
    /// the wall somehow let through with no identity at all falls to the
    /// narrow arm, never the wide one. <c>PersonAsync</c> and <c>ApiKeyAsync</c>
    /// are memoized on <see cref="ICallerIdentity"/>, so asking again costs no
    /// extra query.
    /// </remarks>
    public static async Task<(bool All, Guid? PersonId)> OfAsync(
        bool wallEnabled, ICallerIdentity caller, IActorDirectory actors, CancellationToken ct)
    {
        if (!wallEnabled) return (true, null);
        if (await caller.PersonAsync(ct) is not null) return (true, null);
        if (await caller.ApiKeyAsync(ct) is { Owner.Role: >= PersonRole.Admin }) return (true, null);

        return (false, (await actors.PrincipalAsync(ct))?.Id);
    }
}

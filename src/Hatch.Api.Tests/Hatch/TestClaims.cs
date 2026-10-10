using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The claim rule, for a test that only needs one to exist. Most of these
/// harnesses have nothing to say about leases and want the default TTL; the
/// ones that do say something take it as an argument.
/// </summary>
internal static class TestClaims
{
    /// <summary>The TTL every harness here uses unless it is testing expiry - five minutes, the shipped default.</summary>
    public const int Ttl = 300;

    /// <summary>The stall lapse window every harness here uses unless it is testing the lapse itself - five minutes, the shipped default.</summary>
    public const int StallLapseMinutes = 5;

    /// <summary>The stall resume window every harness here uses unless it is testing the resume itself - fifteen minutes, the shipped default.</summary>
    public const int StallResumeMinutes = 15;

    /// <summary>The stall resume limit every harness here uses unless it is testing the limit itself - three, the shipped default.</summary>
    public const int StallResumeLimit = 3;

    public static IssueClaims With(
        int ttlSeconds = Ttl, int stallLapseMinutes = StallLapseMinutes,
        int stallResumeMinutes = StallResumeMinutes, int stallResumeLimit = StallResumeLimit) =>
        new(Options.Create(new HatchOptions
        {
            ClaimTtlSeconds = ttlSeconds, StallLapseMinutes = stallLapseMinutes,
            StallResumeMinutes = stallResumeMinutes, StallResumeLimit = stallResumeLimit,
        }));

    /// <summary>
    /// A <see cref="Preemption"/> for a harness with nothing to say about it -
    /// <see cref="IssueClaimController"/> now takes one, and most call sites
    /// here are testing something else entirely. Built on <paramref name="db"/>
    /// itself rather than a connection of its own, the way DI hands a request
    /// the same scoped context for both.
    /// </summary>
    public static Preemption Preemption(
        HatchContext db, TimeProvider time, IssueClaims? claims = null, IActorDirectory? actors = null,
        Runners? runners = null)
    {
        var rule = claims ?? With();
        return new Preemption(
            db,
            new Dispatch(db, actors ?? new StubActorDirectory(), rule, time),
            rule,
            runners ?? new Runners(Options.Create(new HatchOptions())),
            time);
    }
}

using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The claim's rule, with no database under it: when a lease is alive, what it
/// looks like on the wire, and the one sentence every fold prints.
///
/// Worth a file of its own because these three are asked from four places - the
/// claim endpoints, the dispatcher, the board and the issue read - and a rule
/// that three of them agreed on would still be a bug.
/// </summary>
public class IssueClaimsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    // ---- Alive, and not ----

    [Fact]
    public void AClaimHeardFromJustNow_IsLive()
    {
        Assert.True(TestClaims.With().IsLive(Held(Now), Now));
    }

    [Fact]
    public void AClaimExactlyAtTheCutoff_IsStillLive()
    {
        var claims = TestClaims.With(ttlSeconds: 60);

        // The boundary is inclusive, and that is the side to be on: a heartbeat
        // that lands exactly on the TTL is a runner that answered in time.
        Assert.True(claims.IsLive(Held(Now.AddSeconds(-60)), Now));
    }

    [Fact]
    public void AClaimOneTickPastTheCutoff_IsOver()
    {
        var claims = TestClaims.With(ttlSeconds: 60);

        Assert.False(claims.IsLive(Held(Now.AddSeconds(-60).AddTicks(-1)), Now));
    }

    [Fact]
    public void NoTokenIsNoClaim_HoweverRecentTheHeartbeat()
    {
        var orphaned = new ClaimSnapshot(null, "Nathan", "here", Now, Now, null, null);

        Assert.False(TestClaims.With().IsLive(orphaned, Now));
        Assert.Null(TestClaims.With().Project(orphaned, Now));
    }

    [Fact]
    public void ATtlOfZero_FallsBackRatherThanKillingEveryClaim()
    {
        // A misconfigured TTL should cost a fallback, not a board nobody may
        // claim. Anything at or below zero would make every claim dead the
        // instant it was taken.
        Assert.Equal(new HatchOptions().ClaimTtlSeconds, TestClaims.With(ttlSeconds: 0).TtlSeconds);
        Assert.Equal(new HatchOptions().ClaimTtlSeconds, TestClaims.With(ttlSeconds: -5).TtlSeconds);
    }

    // ---- Gone quiet ----

    [Fact]
    public void AClaimWithNoWordSinceItWasTaken_GoesQuietMeasuredFromTheTake()
    {
        var claims = TestClaims.With(stallLapseMinutes: 5);

        // ChatterAt is null throughout - a --quiet session sends no chatter -
        // so the clock the quiet check reads runs from ClaimedAt. The
        // heartbeat itself stays fresh, as an ongoing --quiet session's would,
        // so only the quiet clause is under test here and not the TTL.
        ClaimSnapshot Held(DateTimeOffset now) =>
            new(Guid.NewGuid(), "hatch", "somewhere:/checkouts/one", Now, now, null, null);

        Assert.True(claims.IsLive(Held(Now.AddMinutes(4).AddSeconds(59)), Now.AddMinutes(4).AddSeconds(59)));
        Assert.False(claims.IsLive(Held(Now.AddMinutes(5).AddSeconds(1)), Now.AddMinutes(5).AddSeconds(1)));
    }

    [Fact]
    public void AWordSaidRecently_KeepsAHeardFromClaimAlive_HoweverOldTheTake()
    {
        var claims = TestClaims.With(stallLapseMinutes: 5);

        var claim = new ClaimSnapshot(
            Guid.NewGuid(), "hatch", "somewhere:/checkouts/one",
            Now.AddHours(-2), Now, "still here", Now.AddMinutes(-1));

        Assert.True(claims.IsLive(claim, Now));
    }

    [Fact]
    public void AWordSaidLongAgo_GoesQuiet_EvenWithARecentHeartbeat()
    {
        var claims = TestClaims.With(stallLapseMinutes: 5);

        // The heartbeat alone says a process is alive; the chatter is the last
        // time anybody looked, and that is what quiet measures.
        var claim = new ClaimSnapshot(
            Guid.NewGuid(), "hatch", "somewhere:/checkouts/one",
            Now.AddHours(-2), Now, "still here", Now.AddMinutes(-6));

        Assert.False(claims.IsLive(claim, Now));
    }

    [Fact]
    public void StallLapseMinutesOfZero_TurnsQuietOffEntirely()
    {
        var claims = TestClaims.With(stallLapseMinutes: 0);

        // The heartbeat stays fresh at the instant checked, so only the quiet
        // clause is under test - the claim was taken a day ago and has never
        // said a word since.
        var later = Now.AddDays(1);
        var claim = new ClaimSnapshot(Guid.NewGuid(), "hatch", "somewhere:/checkouts/one", Now, later, null, null);

        Assert.True(claims.IsLive(claim, later));
        Assert.Equal(0, claims.StallLapseSeconds);
    }

    [Fact]
    public void ANegativeStallLapseMinutes_FallsBackToTheDefault()
    {
        Assert.Equal(new HatchOptions().StallLapseMinutes * 60, TestClaims.With(stallLapseMinutes: -5).StallLapseSeconds);
    }

    [Fact]
    public void APositiveStallLapseMinutes_IsHonouredInSeconds()
    {
        Assert.Equal(600, TestClaims.With(stallLapseMinutes: 10).StallLapseSeconds);
    }

    // ---- What a client is handed ----

    [Fact]
    public void ALiveClaim_ProjectsWhoAndFromWhereAndWhatItSaid()
    {
        var claim = new ClaimSnapshot(
            Guid.NewGuid(), "hatch", "somewhere:/checkouts/one",
            Now.AddMinutes(-3), Now.AddSeconds(-20), "running the tests", Now.AddSeconds(-20));

        var drawn = TestClaims.With().Project(claim, Now);

        Assert.NotNull(drawn);
        Assert.Equal("hatch", drawn.ClaimedBy);
        Assert.Equal("somewhere:/checkouts/one", drawn.Runner);
        Assert.Equal(Now.AddMinutes(-3), drawn.ClaimedAt);
        Assert.Equal(Now.AddSeconds(-20), drawn.HeartbeatAt);
        Assert.Equal("running the tests", drawn.Chatter);
    }

    [Fact]
    public void AnExpiredClaim_ProjectsAsNothingAtAll()
    {
        var claims = TestClaims.With(ttlSeconds: 60);

        // Not a claim with an old heartbeat for the client to judge: the
        // arithmetic is the server's, and a card drawing a holder that stopped
        // existing four hours ago is worse than a card drawing nothing.
        Assert.Null(claims.Project(Held(Now.AddMinutes(-5)), Now));
    }

    // ---- The one sentence ----

    [Fact]
    public void TheSentence_NamesWhoAndFromWhereAndHowLongAgo()
    {
        var claims = TestClaims.With();
        var claim = new ClaimSnapshot(
            Guid.NewGuid(), "hatch", "somewhere:/checkouts/one", Now.AddMinutes(-4), Now, null, null);

        Assert.Equal(
            "hatch is working this from somewhere:/checkouts/one, last heard from just now",
            claims.Sentence(claim, Now));
    }

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(9, "just now")]
    [InlineData(10, "10 seconds ago")]
    [InlineData(59, "59 seconds ago")]
    [InlineData(60, "1 minute ago")]
    [InlineData(240, "4 minutes ago")]
    public void HowLongAgo_ReadsAtTheResolutionSomebodyCaresAbout(int secondsAgo, string expected)
    {
        // Seconds under a minute because the interesting case is a lease taken
        // moments ago by the runner in the next window; minutes after that,
        // because past a minute nobody is counting.
        Assert.EndsWith($"last heard from {expected}", TestClaims.With().Sentence(Held(Now.AddSeconds(-secondsAgo)), Now));
    }

    private static ClaimSnapshot Held(DateTimeOffset heartbeatAt) => new(
        Guid.NewGuid(), "hatch", "somewhere:/checkouts/one", heartbeatAt, heartbeatAt, null, null);
}

using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Microsoft.EntityFrameworkCore;

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

    // ---- A claim's own run cap ----

    [Fact]
    public void AClaimOverItsRunCap_IsLiveAtTheCapAndNotOneTickPast_HoweverFreshItsHeartbeatAndChatter()
    {
        var claims = TestClaims.With(stallLapseMinutes: 5);

        ClaimSnapshot Held(DateTimeOffset now) =>
            new(Guid.NewGuid(), "hatch", "somewhere:/checkouts/one", Now, now, "working", now, MaxRunSeconds: 240);

        var atCap = Now.AddSeconds(240);
        Assert.True(claims.IsLive(Held(atCap), atCap));
        Assert.False(claims.IsOverRun(Held(atCap), atCap));

        var past = atCap.AddTicks(1);
        Assert.False(claims.IsLive(Held(past), past));
        Assert.True(claims.IsOverRun(Held(past), past));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-5)]
    public void ANullZeroOrNegativeRunCap_IsNoCap(int? cap)
    {
        var claims = TestClaims.With(stallLapseMinutes: 0);
        var later = Now.AddDays(1);
        var claim = new ClaimSnapshot(
            Guid.NewGuid(), "hatch", "somewhere:/checkouts/one", Now, later, null, null, MaxRunSeconds: cap);

        Assert.False(claims.IsOverRun(claim, later));
        Assert.True(claims.IsLive(claim, later));
    }

    [Fact]
    public void AClaimWithNoClaimedAt_IsNeverOverRun()
    {
        var claims = TestClaims.With(stallLapseMinutes: 0);
        var claim = new ClaimSnapshot(
            Guid.NewGuid(), "hatch", "somewhere:/checkouts/one", null, Now, null, null, MaxRunSeconds: 60);

        Assert.False(claims.IsOverRun(claim, Now.AddDays(1)));
    }

    // ---- A claim's own quiet window ----

    [Fact]
    public void AClaimsOwnQuietWindow_IsLiveAt20MinutesAndNotLiveAt26_OverAFiveMinuteGlobal()
    {
        var claims = TestClaims.With(stallLapseMinutes: 5);

        ClaimSnapshot Held(DateTimeOffset now) =>
            new(Guid.NewGuid(), "hatch", "somewhere:/checkouts/one", Now, now, null, null, QuietLimitSeconds: 1500);

        Assert.True(claims.IsLive(Held(Now.AddMinutes(20)), Now.AddMinutes(20)));
        Assert.False(claims.IsLive(Held(Now.AddMinutes(26)), Now.AddMinutes(26)));
    }

    [Fact]
    public void AClaimsOwnQuietWindow_TheBoundaryIsInclusive()
    {
        var claims = TestClaims.With(stallLapseMinutes: 5);

        ClaimSnapshot Held(DateTimeOffset now) =>
            new(Guid.NewGuid(), "hatch", "somewhere:/checkouts/one", Now, now, null, null, QuietLimitSeconds: 1500);

        Assert.True(claims.IsLive(Held(Now.AddSeconds(1500)), Now.AddSeconds(1500)));
        Assert.False(claims.IsLive(Held(Now.AddSeconds(1500).AddTicks(1)), Now.AddSeconds(1500).AddTicks(1)));
    }

    [Fact]
    public void AProjectedClaim_CarriesItsOwnWindowOrTheGlobalOne()
    {
        var claims = TestClaims.With(stallLapseMinutes: 10);
        ClaimSnapshot Held(int? limit) =>
            new(Guid.NewGuid(), "hatch", "somewhere:/checkouts/one", Now, Now, null, null, QuietLimitSeconds: limit);

        Assert.Equal(1500, claims.Project(Held(1500), Now)!.StallLapseSeconds);
        Assert.Equal(600, claims.Project(Held(null), Now)!.StallLapseSeconds);
        Assert.Equal(0, TestClaims.With(stallLapseMinutes: 0).Project(Held(null), Now)!.StallLapseSeconds);
    }

    [Fact]
    public void AQuietLimitOfZeroOrNegative_FallsBackToTheGlobalWindow()
    {
        var claims = TestClaims.With(stallLapseMinutes: 5);

        Assert.Equal(claims.StallLapseSeconds, claims.WindowFor(WithQuietLimit(0)));
        Assert.Equal(claims.StallLapseSeconds, claims.WindowFor(WithQuietLimit(-5)));
    }

    [Fact]
    public void ANullQuietLimit_IsJudgedAtTheGlobalWindow_IncludingAGlobalOfZero()
    {
        Assert.Equal(TestClaims.With(stallLapseMinutes: 5).StallLapseSeconds, TestClaims.With(stallLapseMinutes: 5).WindowFor(WithQuietLimit(null)));

        // A global of zero is "never quiet", not "falls back further" - a null
        // column inherits that exactly as it inherits any other global value.
        var neverQuiet = TestClaims.With(stallLapseMinutes: 0);
        var claim = new ClaimSnapshot(
            Guid.NewGuid(), "hatch", "somewhere:/checkouts/one", Now, Now.AddDays(1), null, null, QuietLimitSeconds: null);

        Assert.True(neverQuiet.IsLive(claim, Now.AddDays(1)));
    }

    [Fact]
    public void APositiveQuietLimit_AppliesEvenWhenTheGlobalIsOff()
    {
        var claims = TestClaims.With(stallLapseMinutes: 0);

        ClaimSnapshot Held(DateTimeOffset now) =>
            new(Guid.NewGuid(), "hatch", "somewhere:/checkouts/one", Now, now, null, null, QuietLimitSeconds: 1500);

        Assert.True(claims.IsLive(Held(Now.AddMinutes(20)), Now.AddMinutes(20)));
        Assert.False(claims.IsLive(Held(Now.AddMinutes(26)), Now.AddMinutes(26)));
    }

    private static ClaimSnapshot WithQuietLimit(int? quietLimitSeconds) => new(
        Guid.NewGuid(), "hatch", "somewhere:/checkouts/one", Now, Now, null, null, quietLimitSeconds);

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

    // ---- IsLive against the SQL shape ----

    /// <summary>
    /// <see cref="IssueClaims.IsLive"/> answers the same as the compiled
    /// <see cref="IssueClaims.Live"/> expression - the predicate
    /// <see cref="IssueClaims.LineageAsync"/> once ran as a second query and now
    /// judges off a row lifted from a priority tree. A drift between the two
    /// would show nobody a claim the take-path still honours, or the reverse.
    /// </summary>
    [Theory]
    [MemberData(nameof(ClaimCases))]
    public void IsLive_AgreesWithTheCompiledSqlPredicate(string name, EfHatchIssue issue)
    {
        var claims = TestClaims.With();
        var compiled = claims.Live(Now).Compile();

        Assert.Equal(compiled(issue), claims.IsLive(ClaimSnapshot.Of(issue), Now));
    }

    public static IEnumerable<object[]> ClaimCases()
    {
        yield return ["fresh", Issue(c =>
        {
            c.ClaimToken = Guid.NewGuid(); c.ClaimedBy = "ann"; c.ClaimRunner = "box-0";
            c.ClaimedAt = Now; c.ClaimHeartbeatAt = Now;
        })];

        // No word since the take, past TestClaims.StallLapseMinutes: quiet,
        // whatever the heartbeat says.
        yield return ["quiet", Issue(c =>
        {
            c.ClaimToken = Guid.NewGuid(); c.ClaimedBy = "ann"; c.ClaimRunner = "box-0";
            c.ClaimedAt = Now.AddMinutes(-(TestClaims.StallLapseMinutes + 1)); c.ClaimHeartbeatAt = Now;
        })];

        // Past its own run cap, however recently it spoke.
        yield return ["over-run", Issue(c =>
        {
            c.ClaimToken = Guid.NewGuid(); c.ClaimedBy = "ann"; c.ClaimRunner = "box-0";
            c.ClaimedAt = Now.AddSeconds(-120); c.ClaimHeartbeatAt = Now; c.ClaimMaxRunSeconds = 60;
        })];

        yield return ["no claim", Issue(_ => { })];
    }

    /// <summary>A bare issue with every required column filled, for a test that cares only about its claim.</summary>
    private static EfHatchIssue Issue(Action<EfHatchIssue> set)
    {
        var issue = new EfHatchIssue
        {
            ProjectId = 1, Number = 1, Type = "task", Title = "t", StatusId = 1, Rank = 1,
            CreatedBy = "operator", CreatedAt = Now, UpdatedAt = Now,
        };
        set(issue);
        return issue;
    }

    // ---- Scope ----

    /// <summary>
    /// A caller's own scope still bounds the lineage, even though it is read
    /// off a house-wide tree rather than a scoped query of its own: a claim on
    /// a parent in a project this caller cannot see does not hold its visible
    /// child, whether the tree is handed in or <see cref="IssueClaims.LineageAsync"/>
    /// builds its own.
    /// </summary>
    [Fact]
    public async Task LineageAsync_StaysInsideTheCallersOwnScope()
    {
        var options = new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var seedDb = new HatchContext(options);

        var hidden = new EfHatchProject { Key = "HID", Name = "Hidden", CreatedAt = Now };
        var visible = new EfHatchProject { Key = "VIS", Name = "Visible", CreatedAt = Now };
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        seedDb.AddRange(hidden, visible, todo);
        await seedDb.SaveChangesAsync();

        var parent = new EfHatchIssue
        {
            ProjectId = hidden.Id, Number = 1, Type = "story", Title = "parent", StatusId = todo.Id, Rank = 1,
            CreatedBy = "operator", CreatedAt = Now, UpdatedAt = Now,
            ClaimToken = Guid.NewGuid(), ClaimedBy = "ann", ClaimRunner = "box-0", ClaimedAt = Now, ClaimHeartbeatAt = Now,
        };
        seedDb.Issues.Add(parent);
        await seedDb.SaveChangesAsync();

        var child = new EfHatchIssue
        {
            ProjectId = visible.Id, ParentId = parent.Id, Number = 1, Type = "task", Title = "child", StatusId = todo.Id, Rank = 2,
            CreatedBy = "operator", CreatedAt = Now, UpdatedAt = Now,
        };
        seedDb.Issues.Add(child);
        await seedDb.SaveChangesAsync();

        var actors = new StubActorDirectory();
        var claims = TestClaims.With();
        var scopedDb = ScopeSeed.Request(options, ScopeSeed.Only(visible.Id));

        var withoutTree = await claims.LineageAsync(scopedDb, actors, Now, default);
        Assert.Null(withoutTree.Holder(child.Id, null));

        var tree = await PriorityTree.ForAsync(scopedDb, actors, default, ignoreFilters: true);
        var withTree = await claims.LineageAsync(scopedDb, actors, Now, default, tree);
        Assert.Null(withTree.Holder(child.Id, null));
    }
}

using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The lease on an issue: taking one, refreshing it, letting go of it, and
/// every way each of those is refused.
///
/// <para><b>These run against a real Postgres, and skip without one.</b> Every
/// write here is a single conditional <c>UPDATE</c> - the guarantee is the
/// <c>WHERE</c> clause, not anything C# does around it - and EF's in-memory
/// provider refuses <c>ExecuteUpdateAsync</c> outright. A harness that could
/// run these would be a harness testing something other than what ships, which
/// is the one failure mode this lane exists to prevent. Point
/// <c>HATCH_TEST_DATABASE_URL</c> at a scratch database, or run
/// <c>make test-api-db</c>, which does it for you.</para>
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class IssueClaimTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    // ---- Taking one ----

    [SkippableFact]
    public async Task AnUnclaimedIssue_IsClaimed()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();

        var taken = Value(await h.Claims.TakeClaim(issue, new ClaimRequest("somewhere:/checkouts/one"), default));

        Assert.NotEqual(Guid.Empty, taken.Token);
        Assert.Equal("Nathan", taken.ClaimedBy);
        Assert.Equal(Now, taken.ClaimedAt);

        // The TTL rides back rather than being configured on both sides: the
        // server is what honours it, so the server is what says what it is.
        Assert.Equal(TestClaims.Ttl, taken.TtlSeconds);
    }

    [SkippableFact]
    public async Task AClaimedIssue_RefusesASecondClaimAndSaysWhoHasIt()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.TakeAsync(issue, "somewhere:/checkouts/one");

        h.Time.Advance(TimeSpan.FromSeconds(30));
        var refusal = Conflict(await h.Claims.TakeClaim(issue, new ClaimRequest("elsewhere:/checkouts/two"), default));

        Assert.Equal("Nathan is working this from somewhere:/checkouts/one, last heard from 30 seconds ago", refusal);
    }

    [SkippableFact]
    public async Task TheSameRunnerAskingTwice_IsRefusedToo()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.TakeAsync(issue, "somewhere:/checkouts/one");

        // Re-claiming is not a thing a runner does - it heartbeats. A take that
        // quietly succeeded for the same runner would hide the case the whole
        // feature exists to catch, which is two checkouts on one box under one
        // credential.
        Assert.NotNull(Conflict(await h.Claims.TakeClaim(issue, new ClaimRequest("somewhere:/checkouts/one"), default)));
    }

    [SkippableFact]
    public async Task AClaimOlderThanTheTtl_IsClaimableAgain()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();
        var first = await h.TakeAsync(issue, "somewhere:/checkouts/one");

        // No job ran and nobody cleared anything. The clock moved.
        h.Time.Advance(TimeSpan.FromSeconds(TestClaims.Ttl + 1));

        var second = Value(await h.Claims.TakeClaim(issue, new ClaimRequest("elsewhere:/checkouts/two"), default));
        Assert.NotEqual(first, second.Token);
        Assert.Equal("elsewhere:/checkouts/two", (await h.RowAsync(issue)).ClaimRunner);
    }

    [SkippableFact]
    public async Task ATakeoverOfALapsedLease_WritesWhoStoppedAnsweringBeforeWhoHoldsItNow()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.TakeAsync(issue, "somewhere:/checkouts/one");

        h.Time.Advance(TimeSpan.FromSeconds(TestClaims.Ttl + 1));
        await h.TakeAsync(issue, "elsewhere:/checkouts/two");

        Assert.Equal(
            [EfHatchIssueEvent.ClaimTaken, EfHatchIssueEvent.ClaimLapsed, EfHatchIssueEvent.ClaimTaken],
            await h.EventKindsAsync(issue));

        var lapsed = (await h.EventsAsync(issue))[1];
        Assert.Equal("Nathan on somewhere:/checkouts/one", lapsed.Payload!.Value.GetProperty("from").GetString());
        Assert.Equal(Now, lapsed.Payload!.Value.GetProperty("heardAt").GetDateTimeOffset());
    }

    [SkippableFact]
    public async Task AFreshTake_WritesNoLapse()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.TakeAsync(issue, "somewhere:/checkouts/one");

        Assert.Equal([EfHatchIssueEvent.ClaimTaken], await h.EventKindsAsync(issue));
    }

    [SkippableFact]
    public async Task ATakeAfterAnOrdinaryRelease_WritesNoLapse()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();
        var token = await h.TakeAsync(issue, "somewhere:/checkouts/one");
        await h.Claims.ReleaseClaim(issue, token, default);

        await h.TakeAsync(issue, "elsewhere:/checkouts/two");

        Assert.Equal(
            [EfHatchIssueEvent.ClaimTaken, EfHatchIssueEvent.ClaimReleased, EfHatchIssueEvent.ClaimTaken],
            await h.EventKindsAsync(issue));
    }

    [SkippableFact]
    public async Task TwoTakesAgainstOnePreClaimState_ResolveToOneClaim()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();
        var id = (await h.RowAsync(issue)).Id;

        // Two connections, and both read the row before either wrote - which is
        // the interleaving the whole design is about. Nothing in the read
        // decides this; the predicate on the write does.
        var one = h.Connect();
        var two = h.Connect();

        var claims = TestClaims.With();
        Assert.False(claims.IsLive(ClaimSnapshot.Of(await one.Issues.FirstAsync(i => i.Id == id)), Now));
        Assert.False(claims.IsLive(ClaimSnapshot.Of(await two.Issues.FirstAsync(i => i.Id == id)), Now));

        var mine = Guid.NewGuid();
        Assert.True(await claims.TryTakeAsync(one, id, mine, "Nathan", "somewhere:/checkouts/one", Now, default));
        Assert.False(await claims.TryTakeAsync(two, id, Guid.NewGuid(), "Nathan", "elsewhere:/checkouts/two", Now, default));

        // Exactly one claim, and it is the first one's - the loser wrote
        // nothing at all rather than overwriting a lease it lost.
        var row = await h.RowAsync(issue);
        Assert.Equal(mine, row.ClaimToken);
        Assert.Equal("somewhere:/checkouts/one", row.ClaimRunner);
    }

    // ---- One runner per line of the tree ----

    [SkippableFact]
    public async Task ATakeOnTheChildOfAClaimedIssue_IsRefusedNamingTheParent_AndWritesNothing()
    {
        await using var h = await NewAsync();
        var parent = await h.FileAsync();
        var child = await h.FileAsync(parent);
        await h.TakeAsync(parent, "somewhere:/checkouts/one");

        var refusal = Conflict(await h.Claims.TakeClaim(child, new ClaimRequest("elsewhere:/checkouts/two"), default));

        Assert.Equal(
            $"Nathan is working {parent}, above this, from somewhere:/checkouts/one, last heard from just now",
            refusal);
        Assert.Null((await h.RowAsync(child)).ClaimToken);
        Assert.Empty(await h.EventKindsAsync(child));
    }

    [SkippableFact]
    public async Task ATakeOnTheParentOfAClaimedChild_IsRefused()
    {
        await using var h = await NewAsync();
        var parent = await h.FileAsync();
        var child = await h.FileAsync(parent);
        await h.TakeAsync(child, "somewhere:/checkouts/one");

        var refusal = Conflict(await h.Claims.TakeClaim(parent, new ClaimRequest("elsewhere:/checkouts/two"), default));

        Assert.Equal(
            $"Nathan is working {child}, below this, from somewhere:/checkouts/one, last heard from just now",
            refusal);
        Assert.Null((await h.RowAsync(parent)).ClaimToken);
        Assert.Empty(await h.EventKindsAsync(parent));
    }

    [SkippableFact]
    public async Task ATakeOnASibling_Succeeds()
    {
        await using var h = await NewAsync();
        var parent = await h.FileAsync();
        var one = await h.FileAsync(parent);
        var two = await h.FileAsync(parent);
        await h.TakeAsync(one, "somewhere:/checkouts/one");

        Value(await h.Claims.TakeClaim(two, new ClaimRequest("elsewhere:/checkouts/two"), default));

        Assert.NotNull((await h.RowAsync(two)).ClaimToken);
    }

    [SkippableFact]
    public async Task ARelativesExpiredClaim_DoesNotRefuseATake()
    {
        await using var h = await NewAsync();
        var parent = await h.FileAsync();
        var child = await h.FileAsync(parent);
        await h.TakeAsync(parent, "somewhere:/checkouts/one");

        h.Time.Advance(TimeSpan.FromSeconds(TestClaims.Ttl + 1));

        Value(await h.Claims.TakeClaim(child, new ClaimRequest("elsewhere:/checkouts/two"), default));
        Assert.NotNull((await h.RowAsync(child)).ClaimToken);
    }

    [SkippableFact]
    public async Task TwoTakesOnAParentAndItsChild_NeverBothKeepAClaim()
    {
        await using var h = await NewAsync();
        var parent = await h.FileAsync();
        var child = await h.FileAsync(parent);
        var parentId = (await h.RowAsync(parent)).Id;
        var childId = (await h.RowAsync(child)).Id;

        // Both takes decided against the pre-claim state, so each one's write
        // lands on a row of its own and neither WHERE can see the other. This
        // is what the check after the write is for.
        var one = h.Connect();
        var two = h.Connect();
        var claims = TestClaims.With();

        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        Assert.True(await claims.TryTakeAsync(one, parentId, mine, "Nathan", "somewhere:/checkouts/one", Now, default));
        Assert.True(await claims.TryTakeAsync(two, childId, theirs, "Nathan", "elsewhere:/checkouts/two", Now, default));

        // One thread cannot make both look before either lets go, so this
        // ordering ends with exactly one claim: the first check sees the
        // second's claim and releases its own, and the second then sees
        // nothing and keeps its own. The assertion is the guarantee - at most
        // one, never both - not the ordering.
        var first = await claims.ConfirmLineAsync(one, parentId, mine, Now, default);
        var second = await claims.ConfirmLineAsync(two, childId, theirs, Now, default);

        Assert.NotNull(first);
        Assert.Null(second);

        var held = new[] { await h.RowAsync(parent), await h.RowAsync(child) }.Count(r => r.ClaimToken is not null);
        Assert.Equal(1, held);
    }

    [SkippableFact]
    public async Task AClaimWithoutARunner_IsRefused()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();

        Assert.Equal(
            "a claim names the runner holding it",
            BadRequest(await h.Claims.TakeClaim(issue, new ClaimRequest("   "), default)));
    }

    [SkippableFact]
    public async Task AClaimOnNothing_IsNotFound()
    {
        await using var h = await NewAsync();

        Assert.IsType<NotFoundResult>(
            (await h.Claims.TakeClaim("AER-404", new ClaimRequest("somewhere:/checkouts/one"), default)).Result);
    }

    // ---- Heartbeats ----

    [SkippableFact]
    public async Task AHeartbeatWithTheCurrentToken_RefreshesTheLease()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();
        var token = await h.TakeAsync(issue);

        h.Time.Advance(TimeSpan.FromSeconds(TestClaims.Ttl - 1));
        Assert.IsType<NoContentResult>((await h.Claims.Heartbeat(issue, new ClaimHeartbeatRequest(token, null), default)).Result);

        // Refreshed, so the lease outlives the TTL it was taken under - and
        // ClaimedAt stays where it was, because "how long has this been
        // running" is a question about the take.
        var row = await h.RowAsync(issue);
        Assert.Equal(Now.AddSeconds(TestClaims.Ttl - 1), row.ClaimHeartbeatAt);
        Assert.Equal(Now, row.ClaimedAt);
    }

    [SkippableFact]
    public async Task AHeartbeatOnAnIssuePausedMidIncrement_StillRefreshesTheLease()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();
        var token = await h.TakeAsync(issue);

        await h.PauseAsync(issue);

        h.Time.Advance(TimeSpan.FromSeconds(TestClaims.Ttl - 1));
        Assert.IsType<NoContentResult>((await h.Claims.Heartbeat(issue, new ClaimHeartbeatRequest(token, null), default)).Result);

        // A pause takes effect between increments, not mid-one: the heartbeat
        // answers exactly as it would for an issue nobody touched, and the
        // runner is never told to stand down.
        var row = await h.RowAsync(issue);
        Assert.Equal(Now.AddSeconds(TestClaims.Ttl - 1), row.ClaimHeartbeatAt);
    }

    [SkippableFact]
    public async Task AHeartbeatWithSomebodyElsesToken_IsRefused()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.TakeAsync(issue, "somewhere:/checkouts/one");

        h.Time.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(
            "Nathan is working this from somewhere:/checkouts/one, last heard from 30 seconds ago",
            Conflict(await h.Claims.Heartbeat(issue, new ClaimHeartbeatRequest(Guid.NewGuid(), null), default)));
    }

    [SkippableFact]
    public async Task AHeartbeatAfterAPersonClearedTheClaim_IsRefused()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();
        var token = await h.TakeAsync(issue);

        Assert.IsType<NoContentResult>(await h.Claims.ReleaseClaim(issue, null, default));

        Assert.Equal(
            "this claim was cleared",
            Conflict(await h.Claims.Heartbeat(issue, new ClaimHeartbeatRequest(token, null), default)));
    }

    [SkippableFact]
    public async Task AHeartbeatAgainstAnExpiredClaimOfOnesOwn_IsRefused()
    {
        await using var h = await NewAsync();

        // Lapsing off, so this is purely the TTL's own expiry and not the
        // quiet clause landing on the same claim by coincidence of two
        // windows that default to the same five minutes - see
        // AQuietClaim_IsRefusedOnHeartbeatWithTheQuietReason for that one.
        h.Rule = TestClaims.With(stallLapseMinutes: 0);
        var issue = await h.FileAsync();
        var token = await h.TakeAsync(issue);

        // The token is still on the row - nobody took it over - and the lease is
        // over anyway. A late heartbeat does not resurrect one.
        h.Time.Advance(TimeSpan.FromSeconds(TestClaims.Ttl + 1));

        Assert.Equal(
            "this claim has expired",
            Conflict(await h.Claims.Heartbeat(issue, new ClaimHeartbeatRequest(token, null), default)));
    }

    [SkippableFact]
    public async Task AHeartbeat_CarriesReplacesAndClearsALine()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();
        var token = await h.TakeAsync(issue);

        // A new lease carries nothing: it does not inherit the last one's last
        // words.
        Assert.Null((await h.RowAsync(issue)).ClaimChatter);

        h.Time.Advance(TimeSpan.FromSeconds(10));
        await h.Claims.Heartbeat(issue, new ClaimHeartbeatRequest(token, "running the tests"), default);
        Assert.Equal("running the tests", (await h.RowAsync(issue)).ClaimChatter);
        Assert.Equal(Now.AddSeconds(10), (await h.RowAsync(issue)).ClaimChatterAt);

        // Absent leaves it alone.
        await h.Claims.Heartbeat(issue, new ClaimHeartbeatRequest(token, null), default);
        Assert.Equal("running the tests", (await h.RowAsync(issue)).ClaimChatter);

        // And "" clears it.
        await h.Claims.Heartbeat(issue, new ClaimHeartbeatRequest(token, ""), default);
        Assert.Null((await h.RowAsync(issue)).ClaimChatter);
        Assert.Null((await h.RowAsync(issue)).ClaimChatterAt);
    }

    [SkippableFact]
    public async Task AnOverlongLine_IsTruncatedRatherThanRefused()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();
        var token = await h.TakeAsync(issue);

        // Killing a live lease because a terminal printed something wide would
        // be the wrong trade, and the field is cosmetic. The first line only,
        // for the same reason.
        var wide = new string('x', EfHatchIssue.MaxClaimChatterLength + 40) + "\nand a second line";
        Assert.IsType<NoContentResult>((await h.Claims.Heartbeat(issue, new ClaimHeartbeatRequest(token, wide), default)).Result);

        Assert.Equal(
            new string('x', EfHatchIssue.MaxClaimChatterLength),
            (await h.RowAsync(issue)).ClaimChatter);
    }

    [SkippableFact]
    public async Task AHeartbeat_WritesNoEvent()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();
        var token = await h.TakeAsync(issue);

        await h.Claims.Heartbeat(issue, new ClaimHeartbeatRequest(token, "still here"), default);

        // A heartbeat is not a decision, and the trail would be a row a minute
        // for every running increment.
        Assert.Equal([EfHatchIssueEvent.ClaimTaken], await h.EventKindsAsync(issue));
    }

    // ---- Letting go ----

    [SkippableFact]
    public async Task AReleaseWithTheCurrentToken_ClearsTheClaim()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();
        var token = await h.TakeAsync(issue);

        Assert.IsType<NoContentResult>(await h.Claims.ReleaseClaim(issue, token, default));

        var row = await h.RowAsync(issue);
        Assert.Null(row.ClaimToken);
        Assert.Null(row.ClaimedBy);
        Assert.Null(row.ClaimRunner);
        Assert.Null(row.ClaimHeartbeatAt);
    }

    [SkippableFact]
    public async Task AReleaseWithAStaleToken_IsRefusedAndClearsNothing()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();
        var held = await h.TakeAsync(issue, "somewhere:/checkouts/one");

        h.Time.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(
            "Nathan is working this from somewhere:/checkouts/one, last heard from 30 seconds ago",
            Conflict(await h.Claims.ReleaseClaim(issue, Guid.NewGuid(), default)));

        Assert.Equal(held, (await h.RowAsync(issue)).ClaimToken);
    }

    [SkippableFact]
    public async Task AReleaseWithAMatchingTokenAfterExpiry_StillTidiesUp()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();
        var token = await h.TakeAsync(issue);

        // The row is still the holder's - nobody took it over - so letting go
        // of a lease that expired underneath you is not an error.
        h.Time.Advance(TimeSpan.FromSeconds(TestClaims.Ttl + 1));

        Assert.IsType<NoContentResult>(await h.Claims.ReleaseClaim(issue, token, default));
        Assert.Null((await h.RowAsync(issue)).ClaimToken);
    }

    [SkippableFact]
    public async Task ReleasingTwice_IsNotAnError()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();
        var token = await h.TakeAsync(issue);

        await h.Claims.ReleaseClaim(issue, token, default);

        // A DELETE says what should not exist afterwards, and it does not.
        Assert.IsType<NoContentResult>(await h.Claims.ReleaseClaim(issue, token, default));
        Assert.Equal(
            [EfHatchIssueEvent.ClaimTaken, EfHatchIssueEvent.ClaimReleased],
            await h.EventKindsAsync(issue));
    }

    [SkippableFact]
    public async Task ATokenlessRelease_IsThePersonsAndNotTheKeys()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.TakeAsync(issue);

        // An agent that could clear another runner's claim could take a ticket
        // off it mid-increment, which is the exact failure the claim exists to
        // prevent, reintroduced through its own back door.
        h.Caller.Key = AKey();

        var refused = Assert.IsType<ObjectResult>(await h.Claims.ReleaseClaim(issue, null, default));
        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Equal("clearing another runner's claim is the operator's, not an agent's", refused.Value);

        // And the person may.
        h.Caller.Key = null;
        Assert.IsType<NoContentResult>(await h.Claims.ReleaseClaim(issue, null, default));
        Assert.Null((await h.RowAsync(issue)).ClaimToken);
    }

    [SkippableFact]
    public async Task AKeyMayStillReleaseItsOwnClaim()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();
        var token = await h.TakeAsync(issue);

        h.Caller.Key = AKey();

        // The narrowing is the tokenless lane and only it: a mutex only an
        // operator could operate would mutex nothing.
        Assert.IsType<NoContentResult>(await h.Claims.ReleaseClaim(issue, token, default));
    }

    /// <summary>
    /// The same narrowing where the wall is off. An agent without a credential
    /// can take a ticket off another agent just as well as one with, so the
    /// question is "is this a program" rather than "does it hold a key" - and
    /// the sentence is the one a key is refused with, because it is the truth
    /// about which lane the caller is in.
    /// </summary>
    [SkippableFact]
    public async Task ATokenlessRelease_IsRefusedToAKeylessRunnerToo()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.TakeAsync(issue);

        h.Caller.Local = new Actor(ActorKind.Key, LocalCaller.RunnerIdFor("host:/src"), "host:/src");

        var refused = Assert.IsType<ObjectResult>(await h.Claims.ReleaseClaim(issue, null, default));
        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Equal("clearing another runner's claim is the operator's, not an agent's", refused.Value);

        // And the local person may - which is the operator's press on the
        // issue page working exactly as it does under a wall.
        h.Caller.Local = new Actor(ActorKind.Person, LocalCaller.PersonId, "Ada");
        Assert.IsType<NoContentResult>(await h.Claims.ReleaseClaim(issue, null, default));
        Assert.Null((await h.RowAsync(issue)).ClaimToken);
    }

    /// <summary>The narrowing is the tokenless lane and only it, for a runner as for a key.</summary>
    [SkippableFact]
    public async Task AKeylessRunnerMayStillReleaseItsOwnClaim()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();
        var token = await h.TakeAsync(issue);

        h.Caller.Local = new Actor(ActorKind.Key, LocalCaller.RunnerIdFor("host:/src"), "host:/src");

        Assert.IsType<NoContentResult>(await h.Claims.ReleaseClaim(issue, token, default));
    }

    [SkippableFact]
    public async Task ReleasingAnUnclaimedIssue_WritesNothing()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();

        Assert.IsType<NoContentResult>(await h.Claims.ReleaseClaim(issue, Guid.NewGuid(), default));
        Assert.IsType<NoContentResult>(await h.Claims.ReleaseClaim(issue, null, default));
        Assert.Empty(await h.EventKindsAsync(issue));
    }

    // ---- The trail ----

    [SkippableFact]
    public async Task TakingReleasingAndClearing_EachWriteAnEventNamingTheActor()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();

        var token = await h.TakeAsync(issue, "somewhere:/checkouts/one");
        await h.Claims.ReleaseClaim(issue, token, default);
        await h.TakeAsync(issue, "elsewhere:/checkouts/two");
        await h.Claims.ReleaseClaim(issue, null, default);

        var events = await h.EventsAsync(issue);
        Assert.Equal(
            [
                EfHatchIssueEvent.ClaimTaken, EfHatchIssueEvent.ClaimReleased,
                EfHatchIssueEvent.ClaimTaken, EfHatchIssueEvent.ClaimCleared,
            ],
            events.Select(e => e.Kind));

        Assert.All(events, e => Assert.Equal("Nathan", e.Actor));

        // The from/to shape ParentChanged and DependencyAdded take, so the
        // page's event line needs no new case. A runner letting go and a person
        // prising a ticket loose are told apart by the kind.
        Assert.Equal((null, "Nathan on somewhere:/checkouts/one"), Payload(events[0]));
        Assert.Equal(("Nathan on somewhere:/checkouts/one", null), Payload(events[1]));
        Assert.Equal(("Nathan on elsewhere:/checkouts/two", null), Payload(events[3]));
    }

    /// <summary>
    /// The trail names the runner rather than "operator" on both ends of a
    /// lease taken with no key - criterion 4's "its events name the runner",
    /// which is the whole reason the lane has a name at all.
    /// </summary>
    [SkippableFact]
    public async Task AKeylessRunnersTakeAndRelease_AreNamedAfterIt()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();

        h.Caller.Person = null;
        h.Caller.Local = new Actor(ActorKind.Key, LocalCaller.RunnerIdFor("host:/src"), "host:/src");

        var token = await h.TakeAsync(issue, "host:/src");
        await h.Claims.ReleaseClaim(issue, token, default);

        var events = await h.EventsAsync(issue);
        Assert.All(events, e => Assert.Equal("host:/src", e.Actor));
    }

    // ---- The outcome on a release ----

    [SkippableFact]
    public async Task AReleaseWithAnOutcome_RecordsItOnTheEvent()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();
        var token = await h.TakeAsync(issue);

        Assert.IsType<NoContentResult>(await h.Claims.ReleaseClaim(issue, token, ClaimOutcomes.Dropped, default));

        var released = (await h.EventsAsync(issue)).Single(e => e.Kind == EfHatchIssueEvent.ClaimReleased);
        Assert.Equal(ClaimOutcomes.Dropped, released.Payload!.Value.GetProperty("outcome").GetString());
    }

    [SkippableFact]
    public async Task AReleaseWithPreemptedOutcome_RecordsItOnTheEvent()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();
        var token = await h.TakeAsync(issue);

        // The board's own decision, not a choice the increment made - see
        // WorkController.LetGoAsync, which skips over this outcome rather
        // than counting or resetting on it.
        Assert.IsType<NoContentResult>(await h.Claims.ReleaseClaim(issue, token, ClaimOutcomes.Preempted, default));

        var released = (await h.EventsAsync(issue)).Single(e => e.Kind == EfHatchIssueEvent.ClaimReleased);
        Assert.Equal(ClaimOutcomes.Preempted, released.Payload!.Value.GetProperty("outcome").GetString());
    }

    [SkippableFact]
    public async Task AReleaseWithNoOutcome_WritesThePayloadItAlwaysHas()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();
        var token = await h.TakeAsync(issue);

        await h.Claims.ReleaseClaim(issue, token, null, default);

        var released = (await h.EventsAsync(issue)).Single(e => e.Kind == EfHatchIssueEvent.ClaimReleased);
        Assert.False(released.Payload!.Value.TryGetProperty("outcome", out _));
    }

    [SkippableFact]
    public async Task AnOutcomeThatIsNeitherValue_Is400()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();
        var token = await h.TakeAsync(issue);

        Assert.Equal(
            "\"quit\" is not an outcome - it is \"dropped\", \"worked\", \"preempted\", or nothing at all",
            BadRequest(await h.Claims.ReleaseClaim(issue, token, "quit", default)));

        // Refused before anything was touched.
        Assert.NotNull((await h.RowAsync(issue)).ClaimToken);
    }

    [SkippableFact]
    public async Task AnOperatorsTokenlessClear_TakesNoOutcome()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.TakeAsync(issue);

        // Named on a tokenless clear, and not refused for it - it is simply
        // not read, the way an operator's clobber names no increment at all.
        Assert.IsType<NoContentResult>(await h.Claims.ReleaseClaim(issue, null, "dropped", default));

        var cleared = (await h.EventsAsync(issue)).Single(e => e.Kind == EfHatchIssueEvent.ClaimCleared);
        Assert.False(cleared.Payload!.Value.TryGetProperty("outcome", out _));
    }

    // ---- A silent claim ----

    [SkippableFact]
    public async Task AQuietClaim_IsRefusedOnHeartbeatWithTheQuietReason()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();
        var token = await h.TakeAsync(issue);

        // Never a word - a --quiet session - so the clock runs from the take.
        h.Time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

        Assert.Equal(
            "this claim has gone quiet for 5 minutes",
            Conflict(await h.Claims.Heartbeat(issue, new ClaimHeartbeatRequest(token, null), default)));
    }

    [SkippableFact]
    public async Task AQuietClaim_IsTakeableByAnotherRunner()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.TakeAsync(issue, "somewhere:/checkouts/one");

        h.Time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

        var second = Value(await h.Claims.TakeClaim(issue, new ClaimRequest("elsewhere:/checkouts/two"), default));
        Assert.Equal("elsewhere:/checkouts/two", (await h.RowAsync(issue)).ClaimRunner);
        Assert.NotEqual(Guid.Empty, second.Token);
    }

    [SkippableFact]
    public async Task AWordSaidJustBeforeTheWindow_KeepsTheClaimAlive()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();
        var token = await h.TakeAsync(issue);

        h.Time.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        Assert.IsType<NoContentResult>((await h.Claims.Heartbeat(issue, new ClaimHeartbeatRequest(token, "still going"), default)).Result);

        h.Time.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        Assert.IsType<NoContentResult>((await h.Claims.Heartbeat(issue, new ClaimHeartbeatRequest(token, null), default)).Result);
    }

    // ---- Taking a claim answers no question ----

    [SkippableFact]
    public async Task ATake_AnswersNoQuestionAtAll_HoweverOldOrShapedLikeTheFormerStallPair()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();

        // Written by hand, carrying exactly the labels the old stall question used to -
        // the type that named them is gone, so this proves the controller never
        // special-cased them, not merely that it stopped calling the thing that used
        // to write them.
        var db = h.Connect();
        var formerStallShape = new EfHatchComment
        {
            IssueId = (await h.RowAsync(issue)).Id,
            Author = "hatch-agent",
            Body = "an increment did nothing - what next?",
            Kind = EfHatchComment.Question,
            Options = Questions.WriteOptions([new QuestionOptionDto("leave it", ""), new QuestionOptionDto("try again", "")]),
            CreatedAt = Now,
        };
        db.Comments.Add(formerStallShape);
        await db.SaveChangesAsync();

        var prose = await h.AskProseAsync(issue, Now, "what should this be called?");

        h.Time.Advance(TimeSpan.FromDays(1));

        Value(await h.Claims.TakeClaim(issue, new ClaimRequest("somewhere:/checkouts/one"), default));

        Assert.DoesNotContain(await h.CommentsAsync(issue), c => c.Kind == EfHatchComment.Answer);
    }

    [SkippableFact]
    public async Task StallLapseMinutesOfZero_NeverRefusesAHeartbeatAsQuiet()
    {
        await using var h = await NewAsync();
        h.Rule = TestClaims.With(stallLapseMinutes: 0);
        var issue = await h.FileAsync();
        var token = await h.TakeAsync(issue);

        // Heartbeat well past what the default five-minute window would be,
        // each beat inside the TTL and none of them carrying a word - only the
        // TTL bounds how long between beats once lapsing is off.
        for (var i = 0; i < 5; i++)
        {
            h.Time.Advance(TimeSpan.FromMinutes(4));
            Assert.IsType<NoContentResult>((await h.Claims.Heartbeat(issue, new ClaimHeartbeatRequest(token, null), default)).Result);
        }
    }

    // ---- The window on the take ----

    [SkippableFact]
    public async Task TheTakesResponse_CarriesStallLapseSecondsAsMinutesTimesSixty()
    {
        await using var h = await NewAsync();
        h.Rule = TestClaims.With(stallLapseMinutes: 10);
        var issue = await h.FileAsync();

        var taken = Value(await h.Claims.TakeClaim(issue, new ClaimRequest("somewhere:/checkouts/one"), default));
        Assert.Equal(600, taken.StallLapseSeconds);
    }

    [SkippableFact]
    public async Task TheTakesResponse_CarriesZeroWhenLapsingIsOff()
    {
        await using var h = await NewAsync();
        h.Rule = TestClaims.With(stallLapseMinutes: 0);
        var issue = await h.FileAsync();

        var taken = Value(await h.Claims.TakeClaim(issue, new ClaimRequest("somewhere:/checkouts/one"), default));
        Assert.Equal(0, taken.StallLapseSeconds);
    }

    // ---- Preemption ----
    //
    // The five rules a claim heartbeat decides against, lazily and from the
    // board as it stands - see Preemption.cs and docs/hatch.md, "The
    // dispatcher", "Preemption".

    [SkippableFact]
    public async Task AnActionableEmergencyIssue_TellsTheSoleHeldRunnerItIsPreempted()
    {
        await using var h = await NewAsync();
        var emergency = await h.FileAsync(priority: PriorityLevels.Emergency, title: "put out this fire");
        var victim = await h.FileAsync();
        var token = await h.TakeAsync(victim);

        var told = Value(await h.Claims.Heartbeat(victim, new ClaimHeartbeatRequest(token, null), default));

        Assert.Equal(emergency, told.Key);
        Assert.Equal("put out this fire", told.Title);
    }

    [SkippableFact]
    public async Task EmergencyWorkItself_IsNeverPreempted()
    {
        await using var h = await NewAsync();
        await h.FileAsync(priority: PriorityLevels.Emergency); // actionable and unclaimed - rule 1 holds
        var heartbeating = await h.FileAsync(priority: PriorityLevels.Emergency);
        var token = await h.TakeAsync(heartbeating);

        // Rule 2: emergency work is never preempted, however the rest of the
        // board looks.
        Assert.IsType<NoContentResult>(
            (await h.Claims.Heartbeat(heartbeating, new ClaimHeartbeatRequest(token, null), default)).Result);
    }

    [SkippableFact]
    public async Task AnActionableIssueInheritingEmergency_IsNeverPreempted()
    {
        await using var h = await NewAsync();
        await h.FileAsync(priority: PriorityLevels.Emergency); // actionable and unclaimed - rule 1 holds
        var epic = await h.FileAsync(priority: PriorityLevels.Emergency, title: "the emergency epic");
        var heartbeating = await h.FileAsync(parent: epic); // own row normal, inherits from the epic
        var token = await h.TakeAsync(heartbeating);

        // Rule 2 reads the effective level: a task under an emergency epic is
        // never told, exactly as one that is emergency on its own row.
        Assert.IsType<NoContentResult>(
            (await h.Claims.Heartbeat(heartbeating, new ClaimHeartbeatRequest(token, null), default)).Result);
    }

    [SkippableFact]
    public async Task OnlyTheLastHeldIssueInBoardOrder_IsTold()
    {
        await using var h = await NewAsync();
        var emergency = await h.FileAsync(priority: PriorityLevels.Emergency);
        var earlier = await h.FileAsync();
        var later = await h.FileAsync();
        var earlierToken = await h.TakeAsync(earlier, "somewhere:/checkouts/one");
        var laterToken = await h.TakeAsync(later, "elsewhere:/checkouts/two");

        // The one holding the board's later card is last in the dispatcher's
        // own order, and is the one told.
        var told = Value(await h.Claims.Heartbeat(later, new ClaimHeartbeatRequest(laterToken, null), default));
        Assert.Equal(emergency, told.Key);

        // The other hears nothing - not because it asked second, but because
        // it is not last.
        Assert.IsType<NoContentResult>(
            (await h.Claims.Heartbeat(earlier, new ClaimHeartbeatRequest(earlierToken, null), default)).Result);
    }

    [SkippableFact]
    public async Task SwappingRank_SwapsWhichHeldIssueIsLast()
    {
        await using var h = await NewAsync();
        var emergency = await h.FileAsync(priority: PriorityLevels.Emergency);
        var one = await h.FileAsync();
        var two = await h.FileAsync();
        var oneToken = await h.TakeAsync(one, "somewhere:/checkouts/one");
        var twoToken = await h.TakeAsync(two, "elsewhere:/checkouts/two");

        // Same board, told the other way round: swap where the two cards sit
        // rather than who claimed which, so it is the order and not the
        // claim that decides.
        var oneRank = (await h.RowAsync(one)).Rank;
        var twoRank = (await h.RowAsync(two)).Rank;
        await h.RerankAsync(one, twoRank);
        await h.RerankAsync(two, oneRank);

        var told = Value(await h.Claims.Heartbeat(one, new ClaimHeartbeatRequest(oneToken, null), default));
        Assert.Equal(emergency, told.Key);

        Assert.IsType<NoContentResult>(
            (await h.Claims.Heartbeat(two, new ClaimHeartbeatRequest(twoToken, null), default)).Result);
    }

    [SkippableFact]
    public async Task AnEconomyClaim_IsPreemptedBeforeANormalOne()
    {
        await using var h = await NewAsync();
        var emergency = await h.FileAsync(priority: PriorityLevels.Emergency);
        var normal = await h.FileAsync();
        var economy = await h.FileAsync(priority: PriorityLevels.Economy);
        var normalToken = await h.TakeAsync(normal, "somewhere:/checkouts/one");
        var economyToken = await h.TakeAsync(economy, "elsewhere:/checkouts/two");

        // Economy sits last in the dispatcher's own walk - after normal, not
        // merely below emergency - so it is the one told, with zero lines
        // changed in Preemption.cs.
        var told = Value(await h.Claims.Heartbeat(economy, new ClaimHeartbeatRequest(economyToken, null), default));
        Assert.Equal(emergency, told.Key);

        Assert.IsType<NoContentResult>(
            (await h.Claims.Heartbeat(normal, new ClaimHeartbeatRequest(normalToken, null), default)).Result);
    }

    // ---- Express protects nothing, and marks nothing (HA-241) ----
    //
    // Rule 2 reads effective level alone - Express is not read anywhere in
    // Preemption.cs - so a held issue below emergency is exactly as exposed
    // with the flag set as without it, and does not jump the board's own
    // order by carrying it.

    [SkippableFact]
    public async Task AnExpressHeldIssue_IsPreemptedExactlyAsANonExpressOneWouldBe()
    {
        await using var h = await NewAsync();
        var emergency = await h.FileAsync(priority: PriorityLevels.Emergency);
        var victim = await h.FileAsync(express: true);
        var token = await h.TakeAsync(victim);

        // Last in board order and held below emergency: told, the flag giving
        // it no immunity rule 2 does not already grant emergency work itself.
        var told = Value(await h.Claims.Heartbeat(victim, new ClaimHeartbeatRequest(token, null), default));
        Assert.Equal(emergency, told.Key);
    }

    [SkippableFact]
    public async Task AnExpressHeldIssue_NotLastInBoardOrder_IsNotToldAheadOfItsTurn()
    {
        await using var h = await NewAsync();
        var emergency = await h.FileAsync(priority: PriorityLevels.Emergency);
        var earlier = await h.FileAsync(express: true);
        var later = await h.FileAsync();
        var earlierToken = await h.TakeAsync(earlier, "somewhere:/checkouts/one");
        var laterToken = await h.TakeAsync(later, "elsewhere:/checkouts/two");

        // Express does not mark its own row as the one to preempt: the later,
        // non-express claim is still last in board order and is the one told.
        var told = Value(await h.Claims.Heartbeat(later, new ClaimHeartbeatRequest(laterToken, null), default));
        Assert.Equal(emergency, told.Key);

        Assert.IsType<NoContentResult>(
            (await h.Claims.Heartbeat(earlier, new ClaimHeartbeatRequest(earlierToken, null), default)).Result);
    }

    [SkippableFact]
    public async Task WithNormalLowAndEconomyClaims_TheEconomyOneIsToldFirstAndTheLowOneSecond()
    {
        await using var h = await NewAsync();
        var emergencyA = await h.FileAsync(priority: PriorityLevels.Emergency);
        await h.FileAsync(priority: PriorityLevels.Emergency);
        var normal = await h.FileAsync();
        var low = await h.FileAsync(priority: PriorityLevels.Low);
        var economy = await h.FileAsync(priority: PriorityLevels.Economy);
        var normalToken = await h.TakeAsync(normal, "somewhere:/checkouts/one");
        var lowToken = await h.TakeAsync(low, "elsewhere:/checkouts/two");
        var economyToken = await h.TakeAsync(economy, "anywhere:/checkouts/three");

        // Economy sits last in the walk, low second-to-last - so economy is
        // told first and low second, with two unclaimed emergency issues
        // giving two runners a turn to be told before normal's.
        var toldEconomy = Value(await h.Claims.Heartbeat(economy, new ClaimHeartbeatRequest(economyToken, null), default));
        Assert.Equal(emergencyA, toldEconomy.Key);

        var toldLow = Value(await h.Claims.Heartbeat(low, new ClaimHeartbeatRequest(lowToken, null), default));
        Assert.Equal(emergencyA, toldLow.Key);

        Assert.IsType<NoContentResult>(
            (await h.Claims.Heartbeat(normal, new ClaimHeartbeatRequest(normalToken, null), default)).Result);
    }

    [SkippableFact]
    public async Task FewerRunnersToldThanEmergencyIssues_TellsExactlyThatMany()
    {
        await using var h = await NewAsync();
        var emergencyA = await h.FileAsync(priority: PriorityLevels.Emergency);
        await h.FileAsync(priority: PriorityLevels.Emergency);
        var best = await h.FileAsync();
        var middle = await h.FileAsync();
        var worst = await h.FileAsync();

        var bestToken = await h.TakeAsync(best, "checkouts/best");
        var middleToken = await h.TakeAsync(middle, "checkouts/middle");
        var worstToken = await h.TakeAsync(worst, "checkouts/worst");

        // Worst first - it is last in board order, so it is told first; the
        // middle one then becomes last among what is left, and is told in
        // turn.
        var first = Value(await h.Claims.Heartbeat(worst, new ClaimHeartbeatRequest(worstToken, null), default));
        Assert.Equal(emergencyA, first.Key);

        var second = Value(await h.Claims.Heartbeat(middle, new ClaimHeartbeatRequest(middleToken, null), default));
        Assert.Equal(emergencyA, second.Key);

        // The best of the three is never told: by the time it would be last
        // among the untold, two runners have already been told, which is as
        // many as there are unclaimed emergency issues.
        Assert.IsType<NoContentResult>(
            (await h.Claims.Heartbeat(best, new ClaimHeartbeatRequest(bestToken, null), default)).Result);
    }

    [SkippableFact]
    public async Task ALiveRunnerHoldingNothing_BlocksPreemptionUntilItAges()
    {
        await using var h = await NewAsync();
        var emergency = await h.FileAsync(priority: PriorityLevels.Emergency);
        var victim = await h.FileAsync();
        var token = await h.TakeAsync(victim, "checkouts/victim");
        await h.AddRunnerAsync("checkouts/free", Now);

        // A free runner takes the emergency ticket on its own next pass, so
        // preempting while one exists would spend a session for nothing.
        Assert.IsType<NoContentResult>(
            (await h.Claims.Heartbeat(victim, new ClaimHeartbeatRequest(token, null), default)).Result);

        // Once that row ages past its own horizon it no longer reads as live,
        // and the same heartbeat is told.
        h.Time.Advance(TimeSpan.FromSeconds(Harness.RunnerGoneAfterSeconds + 1));
        var told = Value(await h.Claims.Heartbeat(victim, new ClaimHeartbeatRequest(token, null), default));
        Assert.Equal(emergency, told.Key);
    }

    [SkippableFact]
    public async Task TheSameRunnerHeartbeatingTwice_IsToldOnce()
    {
        await using var h = await NewAsync();
        var emergency = await h.FileAsync(priority: PriorityLevels.Emergency);
        var victim = await h.FileAsync();
        var token = await h.TakeAsync(victim, "somewhere:/checkouts/one");

        var first = Value(await h.Claims.Heartbeat(victim, new ClaimHeartbeatRequest(token, null), default));
        Assert.Equal(emergency, first.Key);

        // Its own next beat, still holding the ticket: the event the first
        // beat wrote is the record that it was already told, so this one
        // finds nobody left to tell it a second time.
        Assert.IsType<NoContentResult>(
            (await h.Claims.Heartbeat(victim, new ClaimHeartbeatRequest(token, null), default)).Result);

        Assert.Single(await h.EventsAsync(victim), e => e.Kind == EfHatchIssueEvent.ClaimPreempted);
        Assert.Single(await h.CommentsAsync(emergency), c => c.Kind == EfHatchComment.Note);
    }

    [SkippableFact]
    public async Task AnEmergencyIssueThatIsItselfClaimed_TellsNobody()
    {
        await using var h = await NewAsync();
        var emergency = await h.FileAsync(priority: PriorityLevels.Emergency);
        await h.TakeAsync(emergency, "checkouts/emergency-runner");
        var victim = await h.FileAsync();
        var token = await h.TakeAsync(victim);

        Assert.IsType<NoContentResult>(
            (await h.Claims.Heartbeat(victim, new ClaimHeartbeatRequest(token, null), default)).Result);
    }

    [SkippableFact]
    public async Task AnEmergencyIssueFoldedForAnUnrelatedReason_TellsNobody()
    {
        await using var h = await NewAsync();
        var emergency = await h.FileAsync(priority: PriorityLevels.Emergency);
        await h.AskProseAsync(emergency, Now, "what should this be called?");
        var victim = await h.FileAsync();
        var token = await h.TakeAsync(victim);

        // Not actionable - waiting on a person, not on an agent - so rule 1
        // never holds, the same as an emergency issue already claimed.
        Assert.IsType<NoContentResult>(
            (await h.Claims.Heartbeat(victim, new ClaimHeartbeatRequest(token, null), default)).Result);
    }

    [SkippableFact]
    public async Task APreemptedHeartbeat_IsStillAPlainSuccessAnOlderHatchReads()
    {
        await using var h = await NewAsync();
        await h.FileAsync(priority: PriorityLevels.Emergency);
        var victim = await h.FileAsync();
        var token = await h.TakeAsync(victim);

        var result = await h.Claims.Heartbeat(victim, new ClaimHeartbeatRequest(token, null), default);

        // Claim.cs:213 reads only answer.Ok - true of any 2xx, whatever shape
        // the body takes. Here that is a value with no Result wrapping it,
        // the same success shape TakeClaim already answers with.
        Assert.Null(result.Result);
        Assert.NotNull(result.Value);
    }

    // ---- The harness ----

    private sealed class Harness : IAsyncDisposable
    {
        public required string ConnectionString { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required StubCallerIdentity Caller { get; init; }
        public required int ProjectId { get; init; }
        public required int StatusId { get; init; }
        public required int DoingStatusId { get; init; }

        private readonly List<HatchContext> open = [];
        private int next = 1;

        /// <summary>The rule this harness's controllers are built against - overridable per test, for the lapse window above all.</summary>
        public IssueClaims Rule { get; set; } = TestClaims.With();

        /// <summary>
        /// Who <see cref="Dispatch"/>'s scan may ask about - empty by default,
        /// since these tests are not about assignment or a <c>--mine</c> pass.
        /// </summary>
        public StubActorDirectory Actors { get; } = new();

        /// <summary>
        /// The runner horizon <see cref="Preemption"/>'s rule 5 is judged
        /// against - short enough that a test crosses it by advancing
        /// <see cref="Time"/> rather than waiting out the shipped default.
        /// </summary>
        public const int RunnerGoneAfterSeconds = 60;

        public Runners RunnersRule { get; } =
            new(Options.Create(new HatchOptions { RunnerGoneAfterSeconds = RunnerGoneAfterSeconds }));

        /// <summary>
        /// A controller on a context of its own, because that is what an HTTP
        /// request is. It matters more here than anywhere else in these tests:
        /// the claim's writes run outside the change tracker, so a controller
        /// handed a context that already materialised the row would read a
        /// version of it the database stopped having - and a suite that shared
        /// one would be testing the tracker rather than the SQL.
        ///
        /// <para><see cref="Preemption"/> and the <see cref="Dispatch"/> it
        /// decides alongside share that same context, the way DI hands them
        /// the same scoped one in a real request - not a connection of their
        /// own.</para>
        /// </summary>
        public IssueClaimController Claims
        {
            get
            {
                var db = Connect();
                var dispatch = new Dispatch(db, Actors, Rule, Time);
                var preemption = new Preemption(db, dispatch, Rule, RunnersRule, Time);
                return new IssueClaimController(db, Rule, Caller, Time, preemption);
            }
        }

        public IssueThreadController Thread => new(Connect(), Caller, Time);

        /// <summary>A connection of its own, disposed with the harness.</summary>
        public HatchContext Connect()
        {
            var db = new HatchContext(
                new DbContextOptionsBuilder<HatchContext>().UseNpgsql(ConnectionString).Options);

            open.Add(db);
            return db;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var db in open) await db.DisposeAsync();
        }

        /// <summary>An issue, placed directly - the create path is not under test here.</summary>
        public async Task<string> FileAsync(
            string? parent = null, int priority = PriorityLevels.Normal, string title = "a thing to do",
            bool express = false)
        {
            var number = next++;
            var db = Connect();

            long? parentId = null;
            if (parent is not null)
            {
                IssueKey.TryParse(parent, out var parentProject, out var parentNumber);
                parentId = (await db.Issues.AsNoTracking().WithKey(parentProject, parentNumber).FirstAsync()).Id;
            }

            db.Issues.Add(new EfHatchIssue
            {
                ProjectId = ProjectId,
                Number = number,
                Type = "story",
                Title = title,
                StatusId = StatusId,
                ParentId = parentId,
                Rank = 1024 * number,
                Priority = priority,
                Express = express,
                CreatedBy = "operator",
                CreatedAt = Now,
                UpdatedAt = Now,
            });

            await db.SaveChangesAsync();
            return IssueKey.Format("AER", number);
        }

        /// <summary>
        /// This issue's own place in board order - the rank
        /// <see cref="Preemption"/>'s rule 3 reads through the dispatcher's
        /// scan - moved without refiling it.
        /// </summary>
        public async Task RerankAsync(string key, long rank)
        {
            IssueKey.TryParse(key, out var projectKey, out var number);
            var db = Connect();
            await db.Issues.WithKey(projectKey, number).ExecuteUpdateAsync(s => s.SetProperty(i => i.Rank, rank));
        }

        /// <summary>
        /// A runner row, written straight to the table the way
        /// <c>RunnersControllerTests.cs</c> does - these tests are about what a
        /// claim heartbeat does with a row, not about how one is created.
        /// </summary>
        public async Task AddRunnerAsync(string name, DateTimeOffset lastSeenAt)
        {
            var db = Connect();
            db.Runners.Add(new EfHatchRunner
            {
                Name = name,
                Kind = EfHatchRunner.LoopKind,
                FirstSeenAt = lastSeenAt,
                LastSeenAt = lastSeenAt,
                State = EfHatchRunner.Running,
            });
            await db.SaveChangesAsync();
        }

        public async Task<Guid> TakeAsync(string key, string runner = "somewhere:/checkouts/one") =>
            Value(await Claims.TakeClaim(key, new ClaimRequest(runner), default)).Token;

        /// <summary>
        /// Paused, written straight to the row - a person setting a ticket
        /// aside takes effect between increments, not mid-one, so these tests
        /// are about what a live heartbeat does once the level changes under
        /// it, not about the route that sets it (<see cref="IssueExpediteControllerTests"/>).
        /// </summary>
        public async Task PauseAsync(string key)
        {
            var db = Connect();
            IssueKey.TryParse(key, out var projectKey, out var number);
            var issue = await db.Issues.WithKey(projectKey, number).FirstAsync();
            issue.Priority = PriorityLevels.Paused;
            await db.SaveChangesAsync();
        }

        /// <summary>
        /// The row as the database has it, read past the change tracker -
        /// which every write here runs outside of.
        /// </summary>
        public async Task<EfHatchIssue> RowAsync(string key)
        {
            IssueKey.TryParse(key, out var projectKey, out var number);
            var db = Connect();
            return await db.Issues.AsNoTracking().WithKey(projectKey, number).FirstAsync();
        }

        public async Task<IReadOnlyList<IssueEventDto>> EventsAsync(string key) =>
            Value(await Thread.GetEvents(key, default))
                .Where(e => e.Kind.StartsWith("claim_"))
                .Reverse()
                .ToList();

        /// <summary>Every event on the issue, oldest first - unlike <see cref="EventsAsync"/>, not narrowed to the claim's own kinds.</summary>
        public async Task<IReadOnlyList<IssueEventDto>> EventsAllAsync(string key) =>
            Value(await Thread.GetEvents(key, default)).Reverse().ToList();

        public async Task<IReadOnlyList<string>> EventKindsAsync(string key) =>
            (await EventsAsync(key)).Select(e => e.Kind).ToList();

        public async Task<IReadOnlyList<CommentDto>> CommentsAsync(string key) =>
            Value(await Thread.GetComments(key, default));

        /// <summary>A question asked in prose - no options, and never a stall question however long it waits.</summary>
        public async Task<long> AskProseAsync(string key, DateTimeOffset at, string body)
        {
            var issue = await RowAsync(key);
            var db = Connect();

            var comment = new EfHatchComment
            {
                IssueId = issue.Id,
                Author = "hatch-agent",
                Body = body,
                Kind = EfHatchComment.Question,
                CreatedAt = at,
            };
            db.Comments.Add(comment);
            db.IssueEvents.Add(new EfHatchIssueEvent
            {
                IssueId = issue.Id,
                Actor = "hatch-agent",
                Kind = EfHatchIssueEvent.Asked,
                At = at,
            });

            await db.SaveChangesAsync();
            return comment.Id;
        }
    }

    private static async Task<Harness> NewAsync()
    {
        Skip.IfNot(
            HatchDatabase.Available,
            "HATCH_TEST_DATABASE_URL is unset - the claim's writes are conditional UPDATEs, which EF's in-memory " +
            "provider cannot execute. Run `make test-api-db`, or point the variable at a scratch database.");

        var connectionString = await HatchDatabase.PrepareAsync();

        await using var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var status = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        var doing = new EfHatchStatus { Name = "doing", SortOrder = 30 };
        db.AddRange(project, status, doing);
        await db.SaveChangesAsync();

        // A playbook covering every type, so a Dispatch.ScanAsync scan reads
        // an issue filed straight into "todo" as actionable rather than
        // folding it with "no playbook covers this" - Preemption's own rule 1
        // and rule 3 both walk a scan, not a bare row.
        db.Playbooks.Add(new EfHatchPlaybook
        {
            FromStatusId = status.Id,
            ToStatusId = doing.Id,
            Types = "",
            Shape = "any",
            Prompt = "do the thing",
            Model = "sonnet",
            Effort = "medium",
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        await db.SaveChangesAsync();

        return new Harness
        {
            ConnectionString = connectionString,
            Time = new FakeTimeProvider(Now),
            Caller = new StubCallerIdentity
            {
                Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now },
            },
            ProjectId = project.Id,
            StatusId = status.Id,
            DoingStatusId = doing.Id,
        };
    }

    /// <summary>Whoever the test says is calling: a person, or a key wearing one's clothes.</summary>
    private sealed class StubCallerIdentity : ICallerIdentity
    {
        public EfPerson? Person { get; set; }

        public EfApiKey? Key { get; set; }

        public Task<EfAuthGrant?> GrantAsync(CancellationToken ct) => Task.FromResult<EfAuthGrant?>(null);

        public Task<Guid?> PersonIdAsync(CancellationToken ct) => Task.FromResult(Person?.Id);

        public Task<EfPerson?> PersonAsync(CancellationToken ct) => Task.FromResult(Person);

        public Task<EfApiKey?> ApiKeyAsync(CancellationToken ct) => Task.FromResult(Key);

        /// <summary>Local mode's third lane: the person at the machine, or a runner that named itself. Null is every install with a wall.</summary>
        public Actor? Local { get; set; }

        public Task<Actor?> LocalAsync(CancellationToken ct) => Task.FromResult(Local);

        public Task<bool> IsProgramAsync(CancellationToken ct) =>
            Task.FromResult(Key is not null || Local is { Kind: ActorKind.Key });

        public Task<string> ActorNameAsync(CancellationToken ct) =>
            Task.FromResult(Person?.Name ?? Key?.Name ?? Local?.Name ?? CallerIdentity.Unattributed);
    }

    /// <summary>A key wearing a caller's clothes - what a dispatcher's requests arrive as.</summary>
    private static EfApiKey AKey() => new()
    {
        Name = "hatch",
        Prefix = "hatch_ak_x",
        Hash = [1],
        Scopes = [ApiKeyScopes.Hatch],
        CreatedAt = Now,
    };

    private static (string? From, string? To) Payload(IssueEventDto e)
    {
        var payload = e.Payload!.Value;
        return (payload.GetProperty("from").GetString(), payload.GetProperty("to").GetString());
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {Reason(result.Result)}");

    private static string? Conflict<T>(ActionResult<T> result) =>
        Assert.IsType<ConflictObjectResult>(result.Result).Value?.ToString();

    private static string? Conflict(IActionResult result) =>
        Assert.IsType<ConflictObjectResult>(result).Value?.ToString();

    private static string? BadRequest<T>(ActionResult<T> result) =>
        Assert.IsType<BadRequestObjectResult>(result.Result).Value?.ToString();

    private static string? BadRequest(IActionResult result) =>
        Assert.IsType<BadRequestObjectResult>(result).Value?.ToString();

    private static string Reason(IActionResult? result) => result switch
    {
        ObjectResult o => o.Value?.ToString() ?? $"{o.StatusCode}",
        StatusCodeResult s => s.StatusCode.ToString(),
        null => "no result",
        _ => result.GetType().Name,
    };
}

using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
        Assert.IsType<NoContentResult>(await h.Claims.Heartbeat(issue, new ClaimHeartbeatRequest(token, null), default));

        // Refreshed, so the lease outlives the TTL it was taken under - and
        // ClaimedAt stays where it was, because "how long has this been
        // running" is a question about the take.
        var row = await h.RowAsync(issue);
        Assert.Equal(Now.AddSeconds(TestClaims.Ttl - 1), row.ClaimHeartbeatAt);
        Assert.Equal(Now, row.ClaimedAt);
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
        Assert.IsType<NoContentResult>(await h.Claims.Heartbeat(issue, new ClaimHeartbeatRequest(token, wide), default));

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
            "\"quit\" is not an outcome - it is \"dropped\", \"worked\", or nothing at all",
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
        Assert.IsType<NoContentResult>(await h.Claims.Heartbeat(issue, new ClaimHeartbeatRequest(token, "still going"), default));

        h.Time.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        Assert.IsType<NoContentResult>(await h.Claims.Heartbeat(issue, new ClaimHeartbeatRequest(token, null), default));
    }

    // ---- Lapsed stall questions, answered on take ----

    [SkippableFact]
    public async Task ATakeAnswersALapsedStallQuestion_AndLeavesAProseQuestionAlone()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();
        var stall = await h.AskStallAsync(issue, Now);
        var prose = await h.AskProseAsync(issue, Now, "what should this be called?");

        h.Time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

        Value(await h.Claims.TakeClaim(issue, new ClaimRequest("somewhere:/checkouts/one"), default));

        var comments = await h.CommentsAsync(issue);
        var answer = Assert.Single(comments, c => c.Kind == EfHatchComment.Answer);
        Assert.Equal(stall, answer.AnswersId);
        Assert.Equal(StallAnswers.TryAgain, answer.Body);
        Assert.Equal("Hatch", answer.Author);

        var answered = (await h.EventsAllAsync(issue)).Single(e => e.Kind == EfHatchIssueEvent.Answered);
        Assert.Equal("Hatch", answered.Actor);
        Assert.True(answered.Payload!.Value.GetProperty("lapsed").GetBoolean());
        Assert.Equal(stall, answered.Payload!.Value.GetProperty("questionId").GetInt64());

        // The prose question is a question nobody may guess the label for -
        // never a stall question, never auto-answered.
        Assert.DoesNotContain(comments, c => c.Kind == EfHatchComment.Answer && c.AnswersId == prose);
    }

    [SkippableFact]
    public async Task AFreshStallQuestion_IsNotYetAnsweredOnATake()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.AskStallAsync(issue, Now);

        // Not yet five minutes old.
        h.Time.Advance(TimeSpan.FromMinutes(1));

        Value(await h.Claims.TakeClaim(issue, new ClaimRequest("somewhere:/checkouts/one"), default));

        Assert.DoesNotContain(await h.CommentsAsync(issue), c => c.Kind == EfHatchComment.Answer);
    }

    [SkippableFact]
    public async Task ATakeThatLosesTheLineRecheck_AnswersNothing()
    {
        await using var h = await NewAsync();
        var parent = await h.FileAsync();
        var child = await h.FileAsync(parent);
        await h.AskStallAsync(child, Now);

        h.Time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

        // Two connections racing a take on the parent and the child, the same
        // interleaving TwoTakesOnAParentAndItsChild_NeverBothKeepAClaim uses -
        // one of the two takes is bound to lose the recheck and let go again.
        var one = h.Connect();
        var two = h.Connect();
        var parentId = (await h.RowAsync(parent)).Id;
        var childId = (await h.RowAsync(child)).Id;
        var claims = TestClaims.With();

        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        Assert.True(await claims.TryTakeAsync(one, parentId, mine, "Nathan", "somewhere:/checkouts/one", h.Time.GetUtcNow(), default));
        Assert.True(await claims.TryTakeAsync(two, childId, theirs, "Nathan", "elsewhere:/checkouts/two", h.Time.GetUtcNow(), default));

        Assert.NotNull(await claims.ConfirmLineAsync(one, parentId, mine, h.Time.GetUtcNow(), default));
        Assert.Null(await claims.ConfirmLineAsync(two, childId, theirs, h.Time.GetUtcNow(), default));

        // Neither of these bare writes goes through TakeClaim's own answering
        // step, so the lapsed question on the child is untouched either way -
        // which is exactly the state a losing take must leave it in.
        Assert.DoesNotContain(await h.CommentsAsync(child), c => c.Kind == EfHatchComment.Answer);
    }

    [SkippableFact]
    public async Task StallLapseMinutesOfZero_NeverAnswersAQuestionOnATake()
    {
        await using var h = await NewAsync();
        h.Rule = TestClaims.With(stallLapseMinutes: 0);
        var issue = await h.FileAsync();
        await h.AskStallAsync(issue, Now);

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
            Assert.IsType<NoContentResult>(await h.Claims.Heartbeat(issue, new ClaimHeartbeatRequest(token, null), default));
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

    // ---- The harness ----

    private sealed class Harness : IAsyncDisposable
    {
        public required string ConnectionString { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required StubCallerIdentity Caller { get; init; }
        public required int ProjectId { get; init; }
        public required int StatusId { get; init; }

        private readonly List<HatchContext> open = [];
        private int next = 1;

        /// <summary>The rule this harness's controllers are built against - overridable per test, for the lapse window above all.</summary>
        public IssueClaims Rule { get; set; } = TestClaims.With();

        /// <summary>
        /// A controller on a context of its own, because that is what an HTTP
        /// request is. It matters more here than anywhere else in these tests:
        /// the claim's writes run outside the change tracker, so a controller
        /// handed a context that already materialised the row would read a
        /// version of it the database stopped having - and a suite that shared
        /// one would be testing the tracker rather than the SQL.
        /// </summary>
        public IssueClaimController Claims => new(Connect(), Rule, Caller, Time);

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
        public async Task<string> FileAsync(string? parent = null)
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
                Title = "a thing to do",
                StatusId = StatusId,
                ParentId = parentId,
                Rank = 1024 * number,
                CreatedBy = "operator",
                CreatedAt = Now,
                UpdatedAt = Now,
            });

            await db.SaveChangesAsync();
            return IssueKey.Format("AER", number);
        }

        public async Task<Guid> TakeAsync(string key, string runner = "somewhere:/checkouts/one") =>
            Value(await Claims.TakeClaim(key, new ClaimRequest(runner), default)).Token;

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

        /// <summary>
        /// A stall question, written straight to the table with the shared
        /// options and its own <c>asked</c> event - what a runner asks when an
        /// increment does nothing, and what <see cref="BuildCheckController"/>
        /// asks when a fixed build fails again. These tests are about what a
        /// take does with one once it has gone quiet, not about who asks it.
        /// </summary>
        public async Task<long> AskStallAsync(string key, DateTimeOffset at)
        {
            var issue = await RowAsync(key);
            var db = Connect();

            var comment = new EfHatchComment
            {
                IssueId = issue.Id,
                Author = "hatch-agent",
                Body = "an increment did nothing - what next?",
                Kind = EfHatchComment.Question,
                Options = Questions.WriteOptions(StallAnswers.Options()),
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
        db.AddRange(project, status);
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

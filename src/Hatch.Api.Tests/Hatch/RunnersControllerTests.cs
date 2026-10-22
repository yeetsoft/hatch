using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The runners: what a heartbeat writes and what it must never overwrite, what
/// the read says about a runner that has stopped answering, and the one
/// property the control surface rests on - that an agent cannot raise its own
/// bounds.
/// </summary>
public class RunnersControllerTests
{
    private const string Runner = "here:/checkouts/one";

    // ---- Saying hello ----

    [Fact]
    public async Task AHeartbeat_PutsTheRunnerOnTheBoard()
    {
        var h = await NewAsync();

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop", Line: "reading the board"));

        var runner = Assert.Single(await h.ListAsync());
        Assert.Equal(Runner, runner.Name);
        Assert.Equal("loop", runner.Kind);
        Assert.Equal("running", runner.State);
        Assert.Equal("reading the board", runner.Line);
        Assert.Equal(Now, runner.FirstSeenAt);
        Assert.Equal(Now, runner.LastSeenAt);
        Assert.Null(runner.ClaimKey);
    }

    [Fact]
    public async Task TheHeartbeatAnswer_NamesWhoTheRunnerWorksFor()
    {
        var h = await NewAsync();
        var nathan = h.Actors.AddPerson("Nathan");
        h.Actors.Principal = nathan;

        var instruction = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        Assert.Equal("Nathan", instruction.For);
    }

    [Fact]
    public async Task AKeyWithNoOwner_FallsBackToTheKeysOwnName()
    {
        var h = await NewAsync(program: true);

        // The stub key is called "hatch", and nobody has set it an owner - the
        // directory's principal is null, the same as an admin never having
        // visited the API Keys page.
        var instruction = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        Assert.Equal("hatch", instruction.For);
    }

    [Fact]
    public async Task ASecondHeartbeat_MovesTheClockAndNotTheFirstSighting()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        h.Time.Advance(TimeSpan.FromMinutes(4));
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        var runner = Assert.Single(await h.ListAsync());
        Assert.Equal(Now, runner.FirstSeenAt);
        Assert.Equal(Now.AddMinutes(4), runner.LastSeenAt);
    }

    [Fact]
    public async Task AnEscapedName_IsTheNameTheRunnerCallsItself()
    {
        var h = await NewAsync();

        // Kestrel leaves %2F encoded in the path on purpose, so a runner whose
        // name is a path arrives here still carrying them.
        await h.BeatAsync("here:%2Fcheckouts%2Fone", new RunnerHeartbeatRequest(Kind: "loop"));

        var runner = Assert.Single(await h.ListAsync());
        Assert.Equal(Runner, runner.Name);
    }

    [Fact]
    public async Task ANamelessRunner_IsRefused()
    {
        var h = await NewAsync();

        var refusal = await h.Runners.Heartbeat("   ", new RunnerHeartbeatRequest(), default);

        Assert.Contains("a runner names itself", Reason(refusal.Result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWideLine_IsTruncatedRatherThanEndingTheNight()
    {
        var h = await NewAsync();

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Line: new string('x', 900)));

        var runner = Assert.Single(await h.ListAsync());
        Assert.Equal(EfHatchRunner.MaxLineLength, runner.Line!.Length);
    }

    [Fact]
    public async Task AnEmptyLine_ClearsItAndAnAbsentOneLeavesItAlone()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Line: "waiting"));

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());
        Assert.Equal("waiting", (await h.OneAsync()).Line);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Line: ""));
        Assert.Null((await h.OneAsync()).Line);
    }

    // ---- What it serves ----

    [Fact]
    public async Task AFirstHeartbeatNamingRemotes_StoresTheCanonicalForm()
    {
        var h = await NewAsync();

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Remotes: ["git@example.com:owner/repo.git"]));

        var runner = await h.OneAsync();
        Assert.Equal(["example.com/owner/repo"], runner.Repositories);
    }

    [Fact]
    public async Task ALaterHeartbeat_OverwritesTheRepositoriesAPriorOneReported()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Remotes: ["git@example.com:owner/repo.git"]));

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Remotes: ["git@example.com:owner/other.git"]));

        Assert.Equal(["example.com/owner/other"], (await h.OneAsync()).Repositories);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Remotes: []));
        Assert.Empty((await h.OneAsync()).Repositories);
    }

    [Fact]
    public async Task ABeatWithNoRemotes_LeavesAPreviouslyReportedRowUntouched()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Remotes: ["git@example.com:owner/repo.git"]));

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        Assert.Equal(["example.com/owner/repo"], (await h.OneAsync()).Repositories);
    }

    [Fact]
    public async Task AnInvalidRemote_CanonicalisesToNullAndIsDroppedRatherThanRefusingTheHeartbeat()
    {
        var h = await NewAsync();

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Remotes: ["not a url"]));

        Assert.Empty((await h.OneAsync()).Repositories);
    }

    [Fact]
    public async Task Clones_IsSeededOverwrittenAndLeftAloneTheSameWayRemotesIs()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Clones: true));
        Assert.True((await h.OneAsync()).Clones);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Clones: false));
        Assert.False((await h.OneAsync()).Clones);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());
        Assert.False((await h.OneAsync()).Clones);
    }

    /// <summary>Whether this runner was started with do-my-work or --mine, on the same terms Clones is.</summary>
    [Fact]
    public async Task Mine_IsSeededOverwrittenAndLeftAloneTheSameWayCloneIs()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Mine: true));
        Assert.True((await h.OneAsync()).Mine);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Mine: false));
        Assert.False((await h.OneAsync()).Mine);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());
        Assert.False((await h.OneAsync()).Mine);
    }

    [Fact]
    public async Task Standing_IsSeededOverwrittenAndLeftAloneTheSameWayMineIs()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Standing: true));
        Assert.True((await h.OneAsync()).Standing);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Standing: false));
        Assert.False((await h.OneAsync()).Standing);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());
        Assert.False((await h.OneAsync()).Standing);
    }

    // ---- Tags ----

    [Fact]
    public async Task AFirstHeartbeatNamingTags_StoresThemTrimmedAndLowercased()
    {
        var h = await NewAsync();

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Tags: [" Fast ", "GPU"]));

        Assert.Equal(["fast", "gpu"], (await h.OneAsync()).Tags);
    }

    [Fact]
    public async Task ALaterHeartbeat_NeverOverwritesTagsOnceTheRowHasAny()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Tags: ["fast"]));

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Tags: ["gpu"]));
        Assert.Equal(["fast"], (await h.OneAsync()).Tags);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Tags: []));
        Assert.Equal(["fast"], (await h.OneAsync()).Tags);
    }

    [Fact]
    public async Task AFirstHeartbeatWithNoTags_LeavesTheRowOpenForALaterOneToSeedThem()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Tags: ["gpu"]));

        Assert.Equal(["gpu"], (await h.OneAsync()).Tags);
    }

    [Fact]
    public async Task ABeatWithNoTags_LeavesAPreviouslyReportedRowUntouched()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Tags: ["fast"]));

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        Assert.Equal(["fast"], (await h.OneAsync()).Tags);
    }

    [Fact]
    public async Task ATagThatBreaksTheRule_IsRefusedBeforeAnythingOnTheRowChanges()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Tags: ["fast"]));
        var seenAt = (await h.RowAsync()).LastSeenAt;
        h.Time.Advance(TimeSpan.FromSeconds(10));

        // Not BeatAsync: it throws on anything but a value, and this refusal
        // is the assertion.
        var refusal = await h.Runners.Heartbeat(Runner, new RunnerHeartbeatRequest(Tags: ["has space"]), default);

        Assert.Contains("a tag has no space or comma inside", Reason(refusal.Result), StringComparison.Ordinal);
        var row = await h.RowAsync();
        Assert.Equal("fast", row.Tags);
        Assert.Equal(seenAt, row.LastSeenAt);
    }

    [Fact]
    public async Task GetRunners_AnswersTagsSortedRegardlessOfTheOrderAHeartbeatNamedThemIn()
    {
        var h = await NewAsync();

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Tags: ["gpu", "fast"]));

        Assert.Equal(["fast", "gpu"], (await h.OneAsync()).Tags);
    }

    [Fact]
    public async Task APatchChangingAnUnrelatedField_LeavesTagsExactlyAsAPriorHeartbeatLeftThem()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Tags: ["fast"]));

        await h.PatchAsync(Runner, new RunnerPatchRequest(State: "paused"));

        Assert.Equal(["fast"], (await h.OneAsync()).Tags);
    }

    [Fact]
    public async Task APatchNamingTags_StoresThemTrimmedAndLowercased()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        await h.PatchAsync(Runner, new RunnerPatchRequest(Tags: ["GPU ", " linux"]));

        Assert.Equal(["gpu", "linux"], (await h.OneAsync()).Tags);
    }

    [Fact]
    public async Task APatchTagThatBreaksTheRule_IsRefused()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        var refusal = await h.Runners.PatchRunner(Runner, new RunnerPatchRequest(Tags: ["a b"]), default);

        Assert.Contains("a tag has no space or comma inside", Reason(refusal.Result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task APatchWithNineTags_IsRefused()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        var nine = Enumerable.Range(1, 9).Select(i => $"tag{i}").ToArray();
        var refusal = await h.Runners.PatchRunner(Runner, new RunnerPatchRequest(Tags: nine), default);

        Assert.Contains("a runner carries at most 8 tags", Reason(refusal.Result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task APatchClearingTags_StoresEmptyAndAFollowingHeartbeatDoesNotReseedIt()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Tags: ["fast"]));

        await h.PatchAsync(Runner, new RunnerPatchRequest(Tags: []));
        Assert.Equal("", (await h.RowAsync()).Tags);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Tags: ["gpu"]));
        Assert.Equal("", (await h.RowAsync()).Tags);
    }

    // ---- Out of Claude usage ----

    [Fact]
    public async Task ExhaustedUntil_IsSetClearedAndLeftAloneTheSameWayMineIs()
    {
        var h = await NewAsync();
        var resetAt = Now.AddHours(2);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Exhausted: true, ExhaustedUntil: resetAt));
        Assert.Equal(resetAt, (await h.OneAsync()).ExhaustedUntil);

        // A loop heartbeat that says it is not out clears it at once.
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Exhausted: false));
        Assert.Null((await h.OneAsync()).ExhaustedUntil);

        // And absent - an older CLI, or hatch work's single beat - leaves
        // whatever the row already said alone.
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Exhausted: true, ExhaustedUntil: resetAt));
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());
        Assert.Equal(resetAt, (await h.OneAsync()).ExhaustedUntil);
    }

    [Fact]
    public async Task ExhaustedUntil_IsSeededOnANewRow()
    {
        var h = await NewAsync();
        var resetAt = Now.AddHours(1);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Exhausted: true, ExhaustedUntil: resetAt));

        Assert.Equal(resetAt, (await h.OneAsync()).ExhaustedUntil);
    }

    [Fact]
    public async Task ExhaustedUntil_IsNotSomethingAPersonPatches()
    {
        // No field on the request at all - it is a fact a runner reports about
        // itself, never a person's to set from the page.
        Assert.DoesNotContain(
            typeof(RunnerPatchRequest).GetProperties(), p => p.Name == "ExhaustedUntil" || p.Name == "Exhausted");
    }

    // ---- The account's usage reading, and who it works for ----

    [Fact]
    public async Task ABeatCarryingAReading_StoresItWithTheCallersOwnerId()
    {
        var h = await NewAsync();
        var nathan = h.Actors.AddPerson("Nathan");
        h.Actors.Principal = nathan;
        var readAt = Now.AddMinutes(-1);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(
            Usage: [new RunnerUsageWindowDto("session", "Session", 63, null)], UsageReadAt: readAt));

        var row = await h.RowAsync();
        Assert.Equal(nathan.Id, row.ForPersonId);
        Assert.Equal(readAt, row.UsageReadAt);
        Assert.Contains("63", row.Usage);
    }

    /// <summary>
    /// HA-173: a fourth window the session stream never carries, and a scoped
    /// weekly one named by the account - both kept exactly as the heartbeat
    /// sent them, the same as every window Hatch has never seen before.
    /// </summary>
    [Fact]
    public async Task ABeatCarryingExtraUsageAndANamedScopedWindow_KeepsBothAsSent()
    {
        var h = await NewAsync();
        var nathan = h.Actors.AddPerson("Nathan");
        h.Actors.Principal = nathan;

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(
            Usage:
            [
                new RunnerUsageWindowDto("extra", "Extra usage", 12, null),
                new RunnerUsageWindowDto("weeklyModel", "Weekly (Opus 5)", 88, null),
            ],
            UsageReadAt: Now));

        var row = await h.RowAsync();
        Assert.Contains("\"extra\"", row.Usage);
        Assert.Contains("\"Extra usage\"", row.Usage);
        Assert.Contains("\"weeklyModel\"", row.Usage);
        Assert.Contains("\"Weekly (Opus 5)\"", row.Usage);
    }

    [Fact]
    public async Task ASecondBeatCarryingNoReading_LeavesTheStoredOneWhereItWas()
    {
        var h = await NewAsync();
        var readAt = Now.AddMinutes(-1);
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(
            Usage: [new RunnerUsageWindowDto("session", "Session", 63, null)], UsageReadAt: readAt));

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        var row = await h.RowAsync();
        Assert.Equal(readAt, row.UsageReadAt);
        Assert.Contains("63", row.Usage);
    }

    [Fact]
    public async Task ABeatFromAKeyThatBelongsToNobody_StoresTheReadingWithNoOwner_AndIsNotRefused()
    {
        var h = await NewAsync(program: true);

        // Not refused: BeatAsync throws on anything but a value, so a heartbeat
        // from an ownerless key reaching this line at all is the assertion.
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(
            Usage: [new RunnerUsageWindowDto("session", "Session", 10, null)], UsageReadAt: Now));

        var row = await h.RowAsync();
        Assert.Null(row.ForPersonId);
        Assert.Contains("10", row.Usage);
    }

    [Fact]
    public async Task APercentPast100_IsClamped()
    {
        var h = await NewAsync();

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(
            Usage: [new RunnerUsageWindowDto("session", "Session", 140, null)], UsageReadAt: Now));

        Assert.Contains("100", (await h.RowAsync()).Usage);
        Assert.DoesNotContain("140", (await h.RowAsync()).Usage);
    }

    [Fact]
    public async Task ForPersonId_IsWrittenOnEveryBeatLikeWhereAndRemotes()
    {
        var h = await NewAsync();
        var nathan = h.Actors.AddPerson("Nathan");
        h.Actors.Principal = nathan;
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        h.Actors.Principal = null;
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        Assert.Null((await h.RowAsync()).ForPersonId);
    }

    // ---- Where it runs ----

    [Fact]
    public async Task Where_IsStoredAndReadBack()
    {
        var h = await NewAsync();

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Where: "here:/checkouts/one"));

        Assert.Equal("here:/checkouts/one", (await h.OneAsync()).Where);
    }

    [Fact]
    public async Task ALiveRowAtAnotherWhere_Is409AndLeftUnchanged()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Where: "here:/checkouts/one"));

        var refusal = await h.Runners.Heartbeat(
            Runner, new RunnerHeartbeatRequest(Where: "elsewhere:/checkouts/two"), default);

        Assert.Equal(409, ((ObjectResult)refusal.Result!).StatusCode);
        Assert.Contains("here:/checkouts/one", Reason(refusal.Result), StringComparison.Ordinal);
        Assert.Contains("hatch config", Reason(refusal.Result), StringComparison.Ordinal);
        Assert.Equal("here:/checkouts/one", (await h.OneAsync()).Where);
    }

    [Fact]
    public async Task AGoneRowAtAnotherWhere_IsTakenOver()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Where: "here:/checkouts/one"));

        h.Time.Advance(TimeSpan.FromSeconds(Horizon + 1));
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Where: "elsewhere:/checkouts/two"));

        Assert.Equal("elsewhere:/checkouts/two", (await h.OneAsync()).Where);
    }

    [Fact]
    public async Task ABeatWithNoWhere_IsNeverRefusedOnThatAccount()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Where: "here:/checkouts/one"));

        var instruction = await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        Assert.NotNull(instruction);
        Assert.Equal("here:/checkouts/one", (await h.OneAsync()).Where);
    }

    // ---- Agent ----

    [Fact]
    public async Task Agent_IsStoredAndReadBack()
    {
        var h = await NewAsync();

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Agent: "claude"));

        Assert.Equal("claude", (await h.OneAsync()).Agent);
    }

    [Fact]
    public async Task ABeatWithNoAgent_IsNeverRefusedOnThatAccount()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Agent: "claude"));

        var instruction = await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        Assert.NotNull(instruction);
        Assert.Equal("claude", (await h.OneAsync()).Agent);
    }

    // ---- What it is working ----

    [Fact]
    public async Task ARunnerHoldingATicket_SaysWhichOneAndWhatItIsDoing()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.ClaimAsync(issue.Key, Runner, chatter: "make test-api");
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Line: "claimed a ticket"));

        var runner = await h.OneAsync();

        // Read off the issue at request time rather than copied onto the row:
        // an operator clearing a claim clears this too, on the next poll.
        Assert.Equal(issue.Key, runner.ClaimKey);
        Assert.Equal("make test-api", runner.Line);
    }

    [Fact]
    public async Task AClaimThatWasCleared_LeavesTheRunnerIdleWithNoSecondCopyToTidy()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.ClaimAsync(issue.Key, Runner, chatter: "make test-api");
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Line: "claimed a ticket"));

        await h.ReleaseAsync(issue.Key);

        var runner = await h.OneAsync();
        Assert.Null(runner.ClaimKey);
        Assert.Equal("claimed a ticket", runner.Line);
    }

    [Fact]
    public async Task AnExpiredClaim_IsNotSomethingARunnerIsStillWorking()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.ClaimAsync(issue.Key, Runner, chatter: "make test-api");
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        // Past the lease, and the same arithmetic the board does: a claim
        // nothing has refreshed is not a claim.
        h.Time.Advance(TimeSpan.FromSeconds(TestClaims.Ttl + 1));
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        Assert.Null((await h.OneAsync()).ClaimKey);
    }

    [Fact]
    public async Task AnotherRunnersClaim_IsNotDrawnOnThisRow()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.ClaimAsync(issue.Key, "elsewhere:/tree", chatter: "make test-api");
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        Assert.Null((await h.OneAsync()).ClaimKey);
    }

    [Fact]
    public async Task ALiveClaim_NoLongerKeepsTheRunnersRowFreshByItself()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());
        await h.ClaimAsync(issue.Key, Runner, chatter: "make test-api");

        // IssueClaimController's own heartbeat and release now touch the
        // holding runner's row directly, on every beat - see the remark on
        // Runners.Project and Preemption's rule 5, which is what needs
        // LastSeenAt to be that current the moment a runner frees itself.
        // This harness's ClaimAsync writes the claim columns straight onto
        // the row without going through that controller (the endpoint is
        // built out of conditional UPDATEs the in-memory provider refuses),
        // so nothing here simulates the touch - which is exactly why a live
        // claim, on its own, no longer keeps this row reading as here past
        // its own horizon.
        h.Time.Advance(TimeSpan.FromSeconds(Horizon + 10));

        var runner = await h.OneAsync();
        Assert.Equal(issue.Key, runner.ClaimKey);
        Assert.Equal(Now, runner.LastSeenAt);
    }

    // ---- Aging ----

    [Fact]
    public async Task ARunnerThatStoppedAnswering_IsStillDrawnUntilTwoHoursHaveGone()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        // Gone, but on the page: a loop that died at midnight is a fact
        // somebody wants to see in the morning, not a row that quietly vanished.
        h.Time.Advance(TimeSpan.FromSeconds(Horizon * 3));
        var gone = await h.OneAsync();
        Assert.Equal(Now, gone.LastSeenAt);
        Assert.Equal(Horizon, gone.GoneAfterSeconds);

        // And then not, dropped by the WHERE on the read with nothing swept.
        h.Time.Advance(TimeSpan.FromHours(2) + TimeSpan.FromSeconds(1) - TimeSpan.FromSeconds(Horizon * 3));
        Assert.Empty(await h.ListAsync());
    }

    [Fact]
    public async Task ADroppedRunnerThatComesBack_KeepsTheBoundsItHadRatherThanBeingSeededAfresh()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(MaxRuns: 5));
        await h.PatchAsync(Runner, new RunnerPatchRequest(MaxRuns: "40"));

        h.Time.Advance(TimeSpan.FromHours(2) + TimeSpan.FromSeconds(1));
        Assert.Empty(await h.ListAsync());

        // The row was never deleted, only aged out of the read - so a checkout
        // that starts a loop again the next evening is still bounded the way
        // somebody set it.
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(MaxRuns: 5));
        Assert.Equal(40, (await h.OneAsync()).MaxRuns);
    }

    [Fact]
    public async Task TheMostRecentlyHeardFrom_IsFirst()
    {
        var h = await NewAsync();
        await h.BeatAsync("first:/tree", new RunnerHeartbeatRequest());

        h.Time.Advance(TimeSpan.FromSeconds(10));
        await h.BeatAsync("second:/tree", new RunnerHeartbeatRequest());

        Assert.Equal(["second:/tree", "first:/tree"], (await h.ListAsync()).Select(r => r.Name));
    }

    // ---- Names ----

    [Fact]
    public async Task Names_AreMostRecentlyHeardFromFirst()
    {
        var h = await NewAsync();
        await h.BeatAsync("first:/tree", new RunnerHeartbeatRequest());

        h.Time.Advance(TimeSpan.FromSeconds(10));
        await h.BeatAsync("second:/tree", new RunnerHeartbeatRequest());

        Assert.Equal(["second:/tree", "first:/tree"], await h.NamesAsync());
    }

    [Fact]
    public async Task Names_LeaveOutARunnerOlderThanTheDropHorizon()
    {
        var h = await NewAsync();
        await h.BeatAsync("old:/tree", new RunnerHeartbeatRequest());

        h.Time.Advance(TimeSpan.FromHours(2) + TimeSpan.FromSeconds(1));
        await h.BeatAsync("new:/tree", new RunnerHeartbeatRequest());

        Assert.Equal(["new:/tree"], await h.NamesAsync());

        h.Time.Advance(TimeSpan.FromHours(2) + TimeSpan.FromSeconds(1));
        Assert.Empty(await h.NamesAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Names_AreTheSameListWhoeverAsksAndWithTheWallOnOrOff(bool wall)
    {
        var h = await NewAsync(wall: wall);
        var alice = h.Actors.AddPerson("Alice");
        await h.LoopBeatAsync(Runner, alice);
        h.Time.Advance(TimeSpan.FromSeconds(10));
        await h.LoopBeatAsync("here:/checkouts/two", null);

        // The caller now is nobody, who may not control the owned runner;
        // its name is still in the list.
        Assert.Equal(["here:/checkouts/two", Runner], await h.NamesAsync());

        h.Actors.Principal = alice;
        Assert.Equal(["here:/checkouts/two", Runner], await h.NamesAsync());
    }

    // ---- Driving a project ----

    [Fact]
    public async Task TheEarlierFirstSeenRunner_IsTheOnlyOneWhoseInstructionCarriesTheProject()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");

        var first = await h.BeatAsync("here:/one", new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));
        Assert.Equal(["AER"], first.Drives);

        h.Time.Advance(TimeSpan.FromSeconds(10));
        var second = await h.BeatAsync("here:/two", new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));

        Assert.Empty(second.Drives!);
    }

    [Fact]
    public async Task TheDrivingRunnerGoingQuiet_HandsTheProjectToTheOtherRunner()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");

        await h.BeatAsync("here:/one", new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));
        h.Time.Advance(TimeSpan.FromSeconds(10));
        await h.BeatAsync("here:/two", new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));

        h.Time.Advance(TimeSpan.FromSeconds(Horizon));
        var second = await h.BeatAsync("here:/two", new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));

        Assert.Equal(["AER"], second.Drives);
    }

    [Fact]
    public async Task ARunnerNamingARemoteNoProjectBinds_DrivesNothing()
    {
        var h = await NewAsync();

        var beat = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:unbound/repo.git"]));

        Assert.Empty(beat.Drives!);
    }

    [Fact]
    public async Task AOnceRunner_IsNeverElectedEvenAsTheOnlyCandidate()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");

        var beat = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "once", Remotes: ["git@example.com:o/r.git"]));

        Assert.Empty(beat.Drives!);
    }

    [Fact]
    public async Task IdenticalFirstSeen_IsBrokenByNameOrdinally_AndStaysThatWayAcrossRepeatedCalls()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");

        // Written directly, bypassing the heartbeat's own insert ordering, so
        // both rows carry exactly the same FirstSeenAt.
        h.Db.Runners.Add(new EfHatchRunner
        {
            Name = "b:/tree", Kind = EfHatchRunner.LoopKind, FirstSeenAt = Now, LastSeenAt = Now,
            State = EfHatchRunner.Running, Remotes = "example.com/o/r",
        });
        h.Db.Runners.Add(new EfHatchRunner
        {
            Name = "a:/tree", Kind = EfHatchRunner.LoopKind, FirstSeenAt = Now, LastSeenAt = Now,
            State = EfHatchRunner.Running, Remotes = "example.com/o/r",
        });
        await h.Db.SaveChangesAsync();

        for (var i = 0; i < 3; i++)
        {
            var rows = await h.ListAsync();
            Assert.Equal(["AER"], rows.Single(r => r.Name == "a:/tree").Drives);
            Assert.Empty(rows.Single(r => r.Name == "b:/tree").Drives!);
        }
    }

    [Fact]
    public async Task GetRunnersAndPatchRunner_AgreeWithTheRunnersOwnHeartbeat()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");

        var beat = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));
        var listed = await h.OneAsync();
        var patched = await h.PatchAsync(Runner, new RunnerPatchRequest(State: "paused"));

        Assert.Equal(beat.Drives, listed.Drives);
        Assert.Equal(beat.Drives, patched.Drives);
    }

    // ---- Driving a project: whose runner may ----

    [Fact]
    public async Task AnOwnersRunnerStartedLater_StillDrivesAnOwnedProject_OverAStrangersOlderOne()
    {
        var h = await NewAsync(wall: true);
        await h.BindRepositoryAsync("https://example.com/o/r");
        var owner = h.Actors.AddPerson("Owner");
        var stranger = h.Actors.AddPerson("Stranger");
        await h.AddMemberAsync(owner.Id, ProjectMemberRole.Owner);

        var first = await h.LoopBeatAsync("here:/stranger", stranger);
        h.Time.Advance(TimeSpan.FromSeconds(10));
        var second = await h.LoopBeatAsync("here:/owner", owner);

        Assert.Empty(first.Drives!);
        Assert.Equal(["AER"], second.Drives);
        Assert.Empty((await h.ListAsync()).Single(r => r.Name == "here:/stranger").Drives!);
    }

    [Fact]
    public async Task AnApproversRunner_DrivesAnOwnedProject()
    {
        var h = await NewAsync(wall: true);
        await h.BindRepositoryAsync("https://example.com/o/r");
        var owner = h.Actors.AddPerson("Owner");
        var approver = h.Actors.AddPerson("Approver");
        await h.AddMemberAsync(owner.Id, ProjectMemberRole.Owner);
        await h.AddMemberAsync(approver.Id, ProjectMemberRole.Approver);

        var beat = await h.LoopBeatAsync("here:/approver", approver);

        Assert.Equal(["AER"], beat.Drives);
    }

    [Fact]
    public async Task AKeyBelongingToNobody_DrivesNothingOnAnOwnedProject()
    {
        var h = await NewAsync(wall: true);
        await h.BindRepositoryAsync("https://example.com/o/r");
        await h.AddMemberAsync(h.Actors.AddPerson("Owner").Id, ProjectMemberRole.Owner);

        var beat = await h.LoopBeatAsync("here:/orphan", null);

        Assert.Empty(beat.Drives!);
    }

    [Fact]
    public async Task WithOnlyAStrangersRunnerLive_NobodyDrivesTheOwnedProject()
    {
        var h = await NewAsync(wall: true);
        await h.BindRepositoryAsync("https://example.com/o/r");
        await h.AddMemberAsync(h.Actors.AddPerson("Owner").Id, ProjectMemberRole.Owner);

        var beat = await h.LoopBeatAsync("here:/stranger", h.Actors.AddPerson("Stranger"));

        Assert.Empty(beat.Drives!);
        Assert.Empty((await h.OneAsync()).Drives!);
    }

    [Fact]
    public async Task WithTheWallOff_TheOldestRunnerDrivesAnOwnedProject_WhoeverItWorksFor()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");
        await h.AddMemberAsync(h.Actors.AddPerson("Owner").Id, ProjectMemberRole.Owner);

        var beat = await h.LoopBeatAsync("here:/stranger", h.Actors.AddPerson("Stranger"));

        Assert.Equal(["AER"], beat.Drives);
    }

    [Fact]
    public async Task AProjectWithNoOwner_IsDrivenByTheOldestRunner_EvenWithTheWallUp()
    {
        var h = await NewAsync(wall: true);
        await h.BindRepositoryAsync("https://example.com/o/r");
        var stranger = h.Actors.AddPerson("Stranger");

        var bare = await h.LoopBeatAsync("here:/stranger", stranger);
        Assert.Equal(["AER"], bare.Drives);

        // An approver without an owner is still an ownerless project.
        await h.AddMemberAsync(h.Actors.AddPerson("Approver").Id, ProjectMemberRole.Approver);
        h.Time.Advance(TimeSpan.FromSeconds(10));
        var later = await h.LoopBeatAsync("here:/orphan", null);

        Assert.Empty(later.Drives!);
        Assert.Equal(["AER"], (await h.LoopBeatAsync("here:/stranger", stranger)).Drives);
    }

    [Fact]
    public async Task MembershipChanges_MoveTheProjectOnTheNextBeat()
    {
        var h = await NewAsync(wall: true);
        await h.BindRepositoryAsync("https://example.com/o/r");
        var owner = h.Actors.AddPerson("Owner");
        var stranger = h.Actors.AddPerson("Stranger");
        await h.AddMemberAsync(owner.Id, ProjectMemberRole.Owner);

        await h.LoopBeatAsync("here:/owner", owner);
        h.Time.Advance(TimeSpan.FromSeconds(10));
        Assert.Empty((await h.LoopBeatAsync("here:/stranger", stranger)).Drives!);

        // The stranger becomes an approver, but the owner's runner still holds it.
        await h.AddMemberAsync(stranger.Id, ProjectMemberRole.Approver);
        Assert.Empty((await h.LoopBeatAsync("here:/stranger", stranger)).Drives!);

        // The owner's runner goes quiet, and the approver's takes over.
        h.Time.Advance(TimeSpan.FromSeconds(Horizon));
        Assert.Equal(["AER"], (await h.LoopBeatAsync("here:/stranger", stranger)).Drives);

        // Removed again, they lose it on the very next beat.
        await h.RemoveMemberAsync(stranger.Id);
        Assert.Empty((await h.LoopBeatAsync("here:/stranger", stranger)).Drives!);
    }

    // ---- The seed ----

    [Fact]
    public async Task TheFirstHeartbeat_SeedsTheBoundsTheProcessStartedWith()
    {
        var h = await NewAsync();

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(
            Kind: "loop", Under: "AER-930", MaxRuns: 12, MaxSpend: 40m, UntilAt: Now.AddHours(6)));

        var runner = await h.OneAsync();
        Assert.Equal("AER-930", runner.Under);
        Assert.Equal(12, runner.MaxRuns);
        Assert.Equal(40m, runner.MaxSpend);
        Assert.Equal(Now.AddHours(6), runner.UntilAt);
    }

    [Fact]
    public async Task ALaterHeartbeat_NeverSeedsOverWhatAPersonSet()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(MaxRuns: 12, MaxSpend: 40m));
        await h.PatchAsync(Runner, new RunnerPatchRequest(MaxRuns: "3", MaxSpend: "5"));

        // A restart sends the flags it was started with all over again. If that
        // re-seeded, an edit made at midnight would be undone by the loop's own
        // half-hourly restart - which is the failure this rule exists for.
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(MaxRuns: 12, MaxSpend: 40m));

        var runner = await h.OneAsync();
        Assert.Equal(3, runner.MaxRuns);
        Assert.Equal(5m, runner.MaxSpend);
    }

    [Fact]
    public async Task AHeartbeat_NeverWritesTheState()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());
        await h.PatchAsync(Runner, new RunnerPatchRequest(State: "stopping"));

        var instruction = await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        // It reads the instruction back rather than resetting it: a runner that
        // could un-stop itself by saying hello is a runner nobody can stop.
        Assert.Equal("stopping", instruction.State);
        Assert.Equal("stopping", (await h.OneAsync()).State);
    }

    [Fact]
    public async Task TheKind_FollowsWhicheverCommandIsRunningNow()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        // One checkout runs both commands. A row that remembered the loop would
        // offer buttons for a process that ended an hour ago.
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "once"));

        Assert.Equal("once", (await h.OneAsync()).Kind);
    }

    // ---- What the board asks for ----

    [Fact]
    public async Task TheThreeStates_AreTheOnesTheLoopKnows()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        foreach (var state in EfHatchRunner.States)
        {
            Assert.Equal(state, (await h.PatchAsync(Runner, new RunnerPatchRequest(State: state))).State);
            Assert.Equal(state, (await h.BeatAsync(Runner, new RunnerHeartbeatRequest())).State);
        }
    }

    [Fact]
    public async Task AStateNobodyImplements_IsRefusedInWords()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        var refusal = await h.Runners.PatchRunner(Runner, new RunnerPatchRequest(State: "halt"), default);

        Assert.Equal(
            "a runner is running, paused, stopping - not \"halt\"",
            Reason(refusal.Result));
    }

    [Fact]
    public async Task TheBounds_AreFoldedIntoWhatTheLoopReadsBack()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        await h.PatchAsync(Runner, new RunnerPatchRequest(
            Under: "aer-930", MaxRuns: "12", MaxSpend: "40.00", UntilAt: "2026-09-09T06:00:00Z"));

        var instruction = await h.BeatAsync(Runner, new RunnerHeartbeatRequest());
        Assert.Equal("AER-930", instruction.Under);
        Assert.Equal(12, instruction.MaxRuns);
        Assert.Equal(40m, instruction.MaxSpend);
        Assert.Equal(new DateTimeOffset(2026, 9, 9, 6, 0, 0, TimeSpan.Zero), instruction.UntilAt);
    }

    [Fact]
    public async Task AnAbsentFieldLeavesOneAloneAndAnEmptyOneTakesItOff()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());
        await h.PatchAsync(Runner, new RunnerPatchRequest(
            Under: "AER-930", MaxRuns: "12", MaxSpend: "40", UntilAt: "2026-09-09T06:00:00Z"));

        var still = await h.PatchAsync(Runner, new RunnerPatchRequest(State: "paused"));
        Assert.Equal("AER-930", still.Under);
        Assert.Equal(12, still.MaxRuns);

        var cleared = await h.PatchAsync(Runner, new RunnerPatchRequest(
            Under: "", MaxRuns: "", MaxSpend: "", UntilAt: ""));

        // The tri-state, all the way through: a cap taken off is a loop with no
        // cap, which a nullable number could not have said.
        Assert.Null(cleared.Under);
        Assert.Null(cleared.MaxRuns);
        Assert.Null(cleared.MaxSpend);
        Assert.Null(cleared.UntilAt);
    }

    [Fact]
    public async Task ARefusedFieldWritesNothingAtAll()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        var refusal = await h.Runners.PatchRunner(
            Runner, new RunnerPatchRequest(State: "paused", MaxSpend: "lots"), default);

        Assert.Contains("--max-spend takes an amount", Reason(refusal.Result), StringComparison.Ordinal);

        // The good half of a bad request is not half-applied.
        Assert.Equal("running", (await h.OneAsync()).State);
    }

    [Fact]
    public async Task ABareDate_IsRefusedBecauseAStopHourIsAnInstant()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        var refusal = await h.Runners.PatchRunner(Runner, new RunnerPatchRequest(UntilAt: "2026-09-09"), default);

        Assert.Contains("--until takes an instant", Reason(refusal.Result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SomethingThatIsNotAnIssueKey_IsNotAScope()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        var refusal = await h.Runners.PatchRunner(Runner, new RunnerPatchRequest(Under: "the epic"), default);

        Assert.Contains("--under names an issue", Reason(refusal.Result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARunnerNobodyHasEverSeen_IsNotSomethingToPatch()
    {
        var h = await NewAsync();

        var refusal = await h.Runners.PatchRunner("nowhere:/tree", new RunnerPatchRequest(State: "paused"), default);

        // The table records what has spoken to this Hatch. Typing a hostname
        // into a URL does not make a runner.
        Assert.IsType<NotFoundResult>(refusal.Result);
    }

    // ---- The order ----

    [Fact]
    public async Task AnOrder_RoundTripsThroughOneLoopHeartbeatAndIsGoneByTheNext()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        var issue = await h.FileAsync();

        var ordered = await h.OrderAsync(Runner, new RunnerOrderRequest("work", issue.Key));
        Assert.Equal("work", ordered.Order!.Kind);
        Assert.Equal(issue.Key, ordered.Order.Key);
        Assert.Equal("Nathan", ordered.Order.By);

        var first = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        Assert.Equal("work", first.Order!.Kind);
        Assert.Equal(issue.Key, first.Order.Key);
        Assert.Equal("Nathan", first.Order.By);

        var second = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        Assert.Null(second.Order);
        Assert.Null((await h.OneAsync()).Order);
    }

    [Fact]
    public async Task AOnceBeat_LeavesAPendingOrderInPlace()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        await h.OrderAsync(Runner, new RunnerOrderRequest("pass"));

        // Touch overwrites row.Kind, and a `hatch work` beat beside a loop
        // shares this runner's name - it must not swallow the order meant
        // for the loop's own next pass.
        var once = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "once"));
        Assert.Null(once.Order);

        Assert.Equal("pass", (await h.OneAsync()).Order!.Kind);

        var loop = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        Assert.Equal("pass", loop.Order!.Kind);
    }

    [Fact]
    public async Task OrderingWork_WritesTheOrderedEventWithTheActorAndPayload()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        var issue = await h.FileAsync();

        await h.OrderAsync(Runner, new RunnerOrderRequest("work", issue.Key));

        var last = Assert.Single(await h.EventsAsync(issue.Key), e => e.Kind == EfHatchIssueEvent.Ordered);
        Assert.Equal("Nathan", last.Actor);
        Assert.Contains(Runner, last.Payload);
        Assert.Contains("Nathan", last.Payload);
    }

    [Fact]
    public async Task OrderingAPass_WritesNoIssueEvent_ThereIsNoIssueToWriteItOn()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        var ordered = await h.OrderAsync(Runner, new RunnerOrderRequest("pass"));

        Assert.Equal("pass", ordered.Order!.Kind);
        Assert.Null(ordered.Order.Key);
    }

    [Fact]
    public async Task AKindNobodyImplements_Is400WithTheSentence()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        var refusal = await h.Runners.PostRunnerOrder(Runner, new RunnerOrderRequest("sleep"), default);

        Assert.Equal(400, ((ObjectResult)refusal.Result!).StatusCode);
        Assert.Contains("an order is work, pass", Reason(refusal.Result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkWithNoKey_Is400WithTheSentence()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        var refusal = await h.Runners.PostRunnerOrder(Runner, new RunnerOrderRequest("work"), default);

        Assert.Equal(400, ((ObjectResult)refusal.Result!).StatusCode);
        Assert.Contains("work names an issue", Reason(refusal.Result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkWithAKeyThatIsNotAnIssueKey_Is400WithTheSentence()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        var refusal = await h.Runners.PostRunnerOrder(Runner, new RunnerOrderRequest("work", "the epic"), default);

        Assert.Equal(400, ((ObjectResult)refusal.Result!).StatusCode);
        Assert.Contains("work names an issue", Reason(refusal.Result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkNamingAnIssueThatDoesNotExist_Is404WithTheSentence()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        var refusal = await h.Runners.PostRunnerOrder(Runner, new RunnerOrderRequest("work", "AER-999"), default);

        Assert.Equal(404, ((ObjectResult)refusal.Result!).StatusCode);
        Assert.Contains("AER-999", Reason(refusal.Result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AOnceRunner_RefusesAnOrderItWillNeverReadBack()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "once"));

        var refusal = await h.Runners.PostRunnerOrder(Runner, new RunnerOrderRequest("pass"), default);

        Assert.Equal(409, ((ObjectResult)refusal.Result!).StatusCode);
        Assert.Contains("single increment", Reason(refusal.Result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStoppingRunner_RefusesAnotherOrder()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        await h.PatchAsync(Runner, new RunnerPatchRequest(State: "stopping"));

        var refusal = await h.Runners.PostRunnerOrder(Runner, new RunnerOrderRequest("pass"), default);

        Assert.Equal(409, ((ObjectResult)refusal.Result!).StatusCode);
        Assert.Contains("stopping", Reason(refusal.Result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStandingOrder_RefusesASecondUntilItIsWithdrawn()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        var issue = await h.FileAsync();
        await h.OrderAsync(Runner, new RunnerOrderRequest("work", issue.Key));

        var refusal = await h.Runners.PostRunnerOrder(Runner, new RunnerOrderRequest("pass"), default);

        Assert.Equal(409, ((ObjectResult)refusal.Result!).StatusCode);
        var reason = Reason(refusal.Result);
        Assert.Contains("work", reason, StringComparison.Ordinal);
        Assert.Contains(issue.Key, reason, StringComparison.Ordinal);
        Assert.Contains("withdraw it first", reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteOnAnUnorderedRow_Is204AndWritesNothing()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        var seenAt = (await h.RowAsync()).LastSeenAt;

        var result = await h.WithdrawAsync(Runner);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(seenAt, (await h.RowAsync()).LastSeenAt);
    }

    [Fact]
    public async Task DeleteOnAnOrderedRow_Is204AndClearsIt()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        await h.OrderAsync(Runner, new RunnerOrderRequest("pass"));

        var result = await h.WithdrawAsync(Runner);

        Assert.IsType<NoContentResult>(result);
        Assert.Null((await h.OneAsync()).Order);
        Assert.Null((await h.RowAsync()).OrderKind);
    }

    [Fact]
    public async Task GetRunnersAndPatchRunner_AgreeWithTheRunnersOwnOrder()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        var issue = await h.FileAsync();
        var ordered = await h.OrderAsync(Runner, new RunnerOrderRequest("work", issue.Key));

        var listed = await h.OneAsync();
        var patched = await h.PatchAsync(Runner, new RunnerPatchRequest(State: "paused"));

        Assert.Equal(ordered.Order, listed.Order);
        Assert.Equal(ordered.Order, patched.Order);
    }

    // ---- Who may press ----

    [Fact]
    public async Task AnOwnersRunner_IsControllableByItsOwner()
    {
        var h = await NewAsync(wall: true);
        await h.LoopBeatAsync(Runner, new Actor(ActorKind.Person, h.PersonId, "Nathan"));

        var patched = await h.PatchAsync(Runner, new RunnerPatchRequest(State: "paused"));

        Assert.Equal("paused", patched.State);
    }

    [Fact]
    public async Task ADifferentUsersRunner_Is403WithTheSentence()
    {
        var h = await NewAsync(wall: true, role: PersonRole.User);
        var alice = h.Actors.AddPerson("Alice");
        await h.LoopBeatAsync(Runner, alice);

        var refusal = await h.Runners.PatchRunner(Runner, new RunnerPatchRequest(State: "paused"), default);

        Assert.Equal(403, ((ObjectResult)refusal.Result!).StatusCode);
        Assert.Contains("Alice", Reason(refusal.Result), StringComparison.Ordinal);
        Assert.Contains(Runner, Reason(refusal.Result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAdmin_ControlsAnyonesRunner()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        var alice = h.Actors.AddPerson("Alice");
        await h.LoopBeatAsync(Runner, alice);

        var patched = await h.PatchAsync(Runner, new RunnerPatchRequest(State: "paused"));

        Assert.Equal("paused", patched.State);
    }

    [Fact]
    public async Task ARunnerThatBelongsToNobody_IsOpenToAnyUser()
    {
        var h = await NewAsync(wall: true);
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        var patched = await h.PatchAsync(Runner, new RunnerPatchRequest(State: "paused"));

        Assert.Equal("paused", patched.State);
    }

    [Fact]
    public async Task WithTheWallOff_AMismatchedOwner_StillControlsIt()
    {
        var h = await NewAsync();
        var alice = h.Actors.AddPerson("Alice");
        await h.LoopBeatAsync(Runner, alice);

        var patched = await h.PatchAsync(Runner, new RunnerPatchRequest(State: "paused"));

        Assert.Equal("paused", patched.State);
    }

    [Fact]
    public async Task GetRunners_AnswersForAndCanControl()
    {
        var h = await NewAsync(wall: true);
        var alice = h.Actors.AddPerson("Alice");
        await h.LoopBeatAsync(Runner, alice);
        await h.LoopBeatAsync("here:/checkouts/two", null);

        var rows = await h.ListAsync();

        var owned = Assert.Single(rows, r => r.Name == Runner);
        Assert.Equal("Alice", owned.For);
        Assert.False(owned.CanControl);

        var open = Assert.Single(rows, r => r.Name == "here:/checkouts/two");
        Assert.Null(open.For);
        Assert.True(open.CanControl);
    }

    // ---- Who sees which runner ----

    /// <summary>Three rows: Alice's, Bob's, and nobody's - ending with the caller's own <c>Principal</c> left at null, the last beat's.</summary>
    private static async Task SeedThreeAsync(Harness h)
    {
        var alice = h.Actors.AddPerson("Alice");
        var bob = h.Actors.AddPerson("Bob");

        await h.LoopBeatAsync("here:/checkouts/alice", alice);
        h.Time.Advance(TimeSpan.FromSeconds(10));
        await h.LoopBeatAsync("here:/checkouts/bob", bob);
        h.Time.Advance(TimeSpan.FromSeconds(10));
        await h.LoopBeatAsync("here:/checkouts/nobody", null);
    }

    [Fact]
    public async Task WithTheWallOff_EveryRunnerIsVisible()
    {
        var h = await NewAsync(program: true, keyOwner: PersonRole.User);
        await SeedThreeAsync(h);

        var rows = await h.ListAsync();

        Assert.Equal(["here:/checkouts/nobody", "here:/checkouts/bob", "here:/checkouts/alice"], rows.Select(r => r.Name));
    }

    [Fact]
    public async Task APersonCalling_SeesEveryRunner()
    {
        var h = await NewAsync(wall: true);
        await SeedThreeAsync(h);

        var rows = await h.ListAsync();

        Assert.Equal(["here:/checkouts/nobody", "here:/checkouts/bob", "here:/checkouts/alice"], rows.Select(r => r.Name));
    }

    [Fact]
    public async Task AnAdminsKey_SeesEveryRunner()
    {
        var h = await NewAsync(wall: true, program: true, keyOwner: PersonRole.Admin);
        await SeedThreeAsync(h);

        var rows = await h.ListAsync();

        Assert.Equal(["here:/checkouts/nobody", "here:/checkouts/bob", "here:/checkouts/alice"], rows.Select(r => r.Name));
    }

    [Fact]
    public async Task AUsersKey_SeesOnlyItsOwnersRunner()
    {
        var h = await NewAsync(wall: true, program: true, keyOwner: PersonRole.User);
        await SeedThreeAsync(h);

        h.Actors.AddPerson("Owner", h.PersonId);
        await h.LoopBeatAsync("here:/checkouts/owner", new Actor(ActorKind.Person, h.PersonId, "Owner"));
        h.Actors.Principal = new Actor(ActorKind.Person, h.PersonId, "Owner");

        var rows = await h.ListAsync();

        var owned = Assert.Single(rows);
        Assert.Equal("here:/checkouts/owner", owned.Name);
        Assert.Equal("Owner", owned.For);
    }

    [Fact]
    public async Task AUsersKey_WithNoPrincipal_SeesOnlyTheOwnerlessRunner()
    {
        var h = await NewAsync(wall: true, program: true);
        await SeedThreeAsync(h);
        h.Actors.Principal = null;

        var rows = await h.ListAsync();

        Assert.Equal(["here:/checkouts/nobody"], rows.Select(r => r.Name));
    }

    [Fact]
    public async Task NoIdentityAtAll_SeesOnlyTheOwnerlessRunner()
    {
        var h = await NewAsync(wall: true, nobody: true);
        await SeedThreeAsync(h);

        var rows = await h.ListAsync();

        Assert.Equal(["here:/checkouts/nobody"], rows.Select(r => r.Name));
    }

    // ---- The edge that is cut ----

    [Fact]
    public async Task SettingWhatARunnerMaySpend_IsClosedToAKey()
    {
        var h = await NewAsync(program: true);
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        var refusal = await h.Runners.PatchRunner(Runner, new RunnerPatchRequest(MaxSpend: "1000"), default);

        Assert.Equal(403, ((ObjectResult)refusal.Result!).StatusCode);
        Assert.Null((await h.OneAsync()).MaxSpend);
    }

    [Fact]
    public void TheThreeRoutes_AreScopedTheWayTheClaimAndThePlaybookAre()
    {
        // The heartbeat and the read are a dispatcher's - a runner that could
        // not say it was alive would leave a page that could only be empty. The
        // write is a person's, because it is the budget.
        Assert.Equal(ApiKeyScopes.Hatch, Scope(nameof(RunnersController.Heartbeat))!.AcceptScope);
        Assert.Equal(ApiKeyScopes.Hatch, Scope(nameof(RunnersController.GetRunners))!.AcceptScope);
        Assert.Equal(ApiKeyScopes.Hatch, Scope(nameof(RunnersController.GetRunnerNames))!.AcceptScope);

        var patch = Scope(nameof(RunnersController.PatchRunner));
        Assert.NotNull(patch);
        Assert.Null(patch.AcceptScope);

        // And no class-level attribute for the PATCH to inherit a scope from.
        Assert.Empty(typeof(RunnersController).GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false));
    }

    private static RequireRoleAttribute? Scope(string method) =>
        typeof(RunnersController).GetMethod(method)!
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .SingleOrDefault();

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The horizon these tests judge against, short enough to step over inside one.</summary>
    private const int Horizon = 60;

    private sealed class Harness
    {
        public required RunnersController Runners { get; init; }
        public required IssuesController Issues { get; init; }
        public required HatchContext Db { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }
        public required StubActorDirectory Actors { get; init; }
        public required Guid PersonId { get; init; }

        private int nextRepoOrder;

        public async Task<IssueDto> FileAsync(string title = "a thing") =>
            Created(await Issues.CreateIssue(new IssueCreateRequest(ProjectId, "task", title, null, null, null, null), default));

        /// <summary>A remote bound to the shared project, written straight to the table - WorkControllerTests's own pattern.</summary>
        public async Task<EfHatchProjectRepository> BindRepositoryAsync(string remote)
        {
            var (canonical, _) = RemoteIdentity.Canonical(remote);
            var repo = new EfHatchProjectRepository
            {
                ProjectId = ProjectId,
                Remote = remote,
                Canonical = canonical!,
                SortOrder = nextRepoOrder++,
                CreatedAt = Now,
            };

            Db.ProjectRepositories.Add(repo);
            await Db.SaveChangesAsync();
            return repo;
        }

        public async Task AddMemberAsync(Guid personId, string role)
        {
            Db.ProjectMembers.Add(new EfHatchProjectMember
            {
                ProjectId = ProjectId, PersonId = personId, Role = role, CreatedAt = Now,
            });
            await Db.SaveChangesAsync();
        }

        public async Task RemoveMemberAsync(Guid personId)
        {
            Db.ProjectMembers.RemoveRange(Db.ProjectMembers.Where(m => m.ProjectId == ProjectId && m.PersonId == personId));
            await Db.SaveChangesAsync();
        }

        /// <summary>A loop beat for the shared project's remote, as the key of <paramref name="who"/> - or of nobody, for null.</summary>
        public async Task<RunnerInstructionDto> LoopBeatAsync(string name, Actor? who)
        {
            Actors.Principal = who;
            return await BeatAsync(name, new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));
        }

        public async Task<RunnerInstructionDto> BeatAsync(string name, RunnerHeartbeatRequest request) =>
            Value(await Runners.Heartbeat(name, request, default));

        public async Task<RunnerDto> PatchAsync(string name, RunnerPatchRequest request) =>
            Value(await Runners.PatchRunner(name, request, default));

        public async Task<RunnerDto> OrderAsync(string name, RunnerOrderRequest request) =>
            Value(await Runners.PostRunnerOrder(name, request, default));

        public async Task<ActionResult> WithdrawAsync(string name) => await Runners.DeleteRunnerOrder(name, default);

        /// <summary>An issue's own events, oldest first.</summary>
        public async Task<IReadOnlyList<EfHatchIssueEvent>> EventsAsync(string key)
        {
            var issue = await Find(key);
            return await Db.IssueEvents.Where(e => e.IssueId == issue.Id).OrderBy(e => e.At).ToListAsync();
        }

        public async Task<IReadOnlyList<RunnerDto>> ListAsync() => Value(await Runners.GetRunners(default));

        public async Task<IReadOnlyList<string>> NamesAsync() => Value(await Runners.GetRunnerNames(default));

        public async Task<RunnerDto> OneAsync() => Assert.Single(await ListAsync());

        /// <summary>
        /// The stored row itself, for the fields no wire DTO carries -
        /// <see cref="EfHatchRunner.ForPersonId"/>, <see cref="EfHatchRunner.Usage"/>
        /// and <see cref="EfHatchRunner.UsageReadAt"/> - none of which
        /// <see cref="RunnerDto"/> answers; see HA-134's "out of scope".
        /// </summary>
        public async Task<EfHatchRunner> RowAsync() => await Db.Runners.AsNoTracking().SingleAsync();

        /// <summary>
        /// A lease, written straight onto the row. The endpoint that normally
        /// writes one is built out of conditional UPDATEs the in-memory
        /// provider refuses - see HatchDatabase - and nothing here is testing
        /// the claim.
        /// </summary>
        public async Task ClaimAsync(string key, string runner, string? chatter = null)
        {
            var issue = await Find(key);
            issue.ClaimToken = Guid.NewGuid();
            issue.ClaimedBy = "hatch";
            issue.ClaimRunner = runner;
            issue.ClaimedAt = Time.GetUtcNow();
            issue.ClaimHeartbeatAt = Time.GetUtcNow();
            issue.ClaimChatter = chatter;
            issue.ClaimChatterAt = chatter is null ? null : Time.GetUtcNow();
            await Db.SaveChangesAsync();
        }

        public async Task ReleaseAsync(string key)
        {
            var issue = await Find(key);
            issue.ClaimToken = null;
            issue.ClaimRunner = null;
            issue.ClaimHeartbeatAt = null;
            issue.ClaimChatter = null;
            await Db.SaveChangesAsync();
        }

        private async Task<EfHatchIssue> Find(string key)
        {
            IssueKey.TryParse(key, out var projectKey, out var number);
            return await Db.Issues.Include(i => i.Project).WithKey(projectKey, number).FirstAsync();
        }
    }

    private static async Task<Harness> NewAsync(
        bool program = false, bool wall = false, PersonRole role = PersonRole.User, Guid? personId = null,
        PersonRole? keyOwner = null, bool nobody = false)
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var hatch = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Add(hatch);
        db.Add(new EfHatchStatus { Name = "inbox", SortOrder = 10 });
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var claims = TestClaims.With();
        var runners = new Runners(Options.Create(new HatchOptions { RunnerGoneAfterSeconds = Horizon }), Options.Create(new AuthOptions { Enabled = wall }));

        var id = personId ?? Guid.NewGuid();
        var caller = nobody
            ? new StubCaller()
            : program
            ? new StubCaller
            {
                Key = new EfApiKey
                {
                    Name = "hatch",
                    Hash = [1],
                    Prefix = "hatch_ak_x",
                    Scopes = [ApiKeyScopes.Hatch],
                    CreatedAt = Now,
                    OwnerPersonId = keyOwner is null ? null : id,
                    Owner = keyOwner is null ? null : new EfPerson { Id = id, Name = "Owner", Role = keyOwner.Value, CreatedAt = Now, UpdatedAt = Now },
                },
            }
            : new StubCaller { Person = new EfPerson { Id = id, Name = "Nathan", Role = role, CreatedAt = Now, UpdatedAt = Now } };

        var actors = new StubActorDirectory();

        return new Harness
        {
            Runners = new RunnersController(db, runners, claims, caller, actors, time, Options.Create(new AuthOptions { Enabled = wall })),
            Issues = new IssuesController(db, new RankService(db), new StubActorDirectory(), claims, caller, time, new ProjectAccess(db, new StubActorDirectory())),
            Db = db,
            Time = time,
            ProjectId = hatch.Id,
            Actors = actors,
            PersonId = id,
        };
    }

    /// <summary>Whoever is holding the phone: a person, or the key an agent carries.</summary>
    private sealed class StubCaller : ICallerIdentity
    {
        public EfPerson? Person { get; init; }

        public EfApiKey? Key { get; init; }

        public Task<EfAuthGrant?> GrantAsync(CancellationToken ct) => Task.FromResult<EfAuthGrant?>(null);

        public Task<Guid?> PersonIdAsync(CancellationToken ct) => Task.FromResult(Person?.Id);

        public Task<EfPerson?> PersonAsync(CancellationToken ct) => Task.FromResult(Person);

        public Task<EfApiKey?> ApiKeyAsync(CancellationToken ct) => Task.FromResult(Key);

        public Task<Actor?> LocalAsync(CancellationToken ct) => Task.FromResult<Actor?>(null);

        public Task<bool> IsProgramAsync(CancellationToken ct) => Task.FromResult(Key is not null);

        public Task<string> ActorNameAsync(CancellationToken ct) =>
            Task.FromResult(Person?.Name ?? Key?.Name ?? CallerIdentity.Unattributed);
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {Reason(result.Result)}");

    private static T Created<T>(ActionResult<T> result) =>
        result.Result is CreatedAtActionResult created
            ? (T)created.Value!
            : result.Value ?? throw new InvalidOperationException($"expected a created value, got {Reason(result.Result)}");

    /// <summary>The plain-text reason on a refusal - what the UI puts on screen.</summary>
    private static string Reason(IActionResult? result) => result switch
    {
        ObjectResult o => o.Value?.ToString() ?? $"{o.StatusCode}",
        StatusCodeResult s => s.StatusCode.ToString(),
        null => "no result",
        _ => result.GetType().Name,
    };
}

using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The work log read across issues - the graph's buckets and the leaderboard's
/// ranked sessions: what a caller may ask for, what the server decides when they
/// ask for nothing, and everything it refuses in a sentence.
/// </summary>
public class WorkLogControllerTests
{
    // ---- Choosing what to look at ----

    [Fact]
    public async Task NoRangeAtAll_IsTheFourteenDaysEndingNow()
    {
        var h = await NewAsync();

        var history = await h.HistoryAsync();

        // Fourteen days back from Now, snapped outward onto whole days - so the
        // range, the buckets and the totals describe one window.
        Assert.Equal("day", history.Bucket);
        Assert.Equal(new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.Zero), history.From);
        Assert.Equal(new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero), history.To);
        Assert.Equal(15, history.Buckets.Count);
        Assert.Equal(history.From, history.Buckets[0].Start);
        Assert.Equal(history.To, history.Buckets[^1].End);
    }

    [Fact]
    public async Task OneBoundOnItsOwn_GetsTheOthersDefault()
    {
        var h = await NewAsync();

        var since = await h.HistoryAsync(from: "2026-09-06T03:00:00Z");
        var until = await h.HistoryAsync(to: "2026-09-06T03:00:00Z");

        // Named `from`, so `to` is now; named `to`, so `from` is fourteen days
        // before it. Neither is a refusal.
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 3, 0, 0, TimeSpan.Zero), since.Buckets[^1].End);
        Assert.Equal(new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero), until.From);
    }

    [Fact]
    public async Task AnAbsentBucket_IsHourlyUpToTwoDaysAndDailyPastIt()
    {
        var h = await NewAsync();

        var day = await h.HistoryAsync(from: "2026-09-06T03:00:00Z", to: "2026-09-07T03:00:00Z");
        var month = await h.HistoryAsync(from: "2026-08-08T03:00:00Z", to: "2026-09-07T03:00:00Z");

        // Named back in the answer, so a client labels its axis from what the
        // server did rather than from what it asked for.
        Assert.Equal("hour", day.Bucket);
        Assert.Equal(24, day.Buckets.Count);
        Assert.Equal("day", month.Bucket);
    }

    [Fact]
    public async Task AnExplicitBucket_IsHonouredInEitherCase()
    {
        var h = await NewAsync();

        var shouted = await h.HistoryAsync(from: "2026-09-06T03:00:00Z", to: "2026-09-07T03:00:00Z", bucket: "HOUR");
        var daily = await h.HistoryAsync(from: "2026-09-06T03:00:00Z", to: "2026-09-07T03:00:00Z", bucket: " Day ");

        Assert.Equal("hour", shouted.Bucket);
        Assert.Equal("day", daily.Bucket);
    }

    [Fact]
    public async Task AnOffset_MovesTheDailyGridToTheReadersMidnight()
    {
        var h = await NewAsync();

        var history = await h.HistoryAsync(
            from: "2026-09-01T12:00:00Z", to: "2026-09-04T12:00:00Z", bucket: "day", offsetMinutes: -300);

        // Midnight five hours west, not UTC midnight: an overnight run split
        // across UTC midnight is two half-nights nobody worked.
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 5, 0, 0, TimeSpan.Zero), history.From);
        Assert.All(history.Buckets, b => Assert.Equal(5, b.Start.UtcDateTime.Hour));
    }

    // ---- Narrowing to one project ----

    [Fact]
    public async Task AnAncestorKey_FiltersToThatIssueAndEverythingBeneathIt()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the effort");
        var story = await h.FileAsync("story", "under it", parentKey: epic.Key);
        var elsewhere = await h.FileAsync("epic", "another effort");

        await h.SpendAsync(epic, 1_000, Now.AddHours(-2));
        await h.SpendAsync(story, 20_000, Now.AddHours(-1));
        await h.SpendAsync(elsewhere, 7_000_000, Now.AddHours(-1));

        var scoped = await h.HistoryAsync(ancestorKey: epic.Key);
        var whole = await h.HistoryAsync();

        // The named issue's own sessions are in it: a planning session run
        // against the epic is money no child holds.
        Assert.Equal(2, scoped.Totals.Sessions);
        Assert.Equal(21_000, scoped.Totals.TotalTokens);
        Assert.Equal(3, whole.Totals.Sessions);
    }

    [Fact]
    public async Task AnAncestorKeyNamingNothing_IsRefusedRatherThanAnsweredAsEmpty()
    {
        var h = await NewAsync();

        Assert.Equal("there is no AER-999", await h.RefusalAsync(ancestorKey: "AER-999"));
        Assert.Equal("there is no nonsense", await h.RefusalAsync(ancestorKey: "nonsense"));
    }

    // ---- Narrowing to several projects ----

    [Fact]
    public async Task NoProjectId_CountsEveryProjectsSpend()
    {
        var h = await NewAsync();
        var mine = await h.FileAsync();
        var (b, c) = await h.TwoMoreProjectsAsync();
        var inB = await h.FileAsync(projectId: b.Id);
        var inC = await h.FileAsync(projectId: c.Id);

        await h.SpendAsync(mine, 1_000, Now.AddHours(-3));
        await h.SpendAsync(inB, 2_000, Now.AddHours(-2));
        await h.SpendAsync(inC, 4_000, Now.AddHours(-1));

        var history = await h.HistoryAsync();

        Assert.Equal(3, history.Totals.Sessions);
        Assert.Equal(7_000, history.Totals.TotalTokens);
    }

    [Fact]
    public async Task OneProjectId_NarrowsTotalsBucketsAndBounds()
    {
        var h = await NewAsync();
        var mine = await h.FileAsync();
        var (b, c) = await h.TwoMoreProjectsAsync();
        var inB = await h.FileAsync(projectId: b.Id);
        var inC = await h.FileAsync(projectId: c.Id);

        var only = Now.AddHours(-3);
        await h.SpendAsync(mine, 1_000, only);
        await h.SpendAsync(inB, 2_000, Now.AddHours(-2));
        await h.SpendAsync(inC, 4_000, Now.AddHours(-1));

        var history = await h.HistoryAsync(bucket: "hour", projectIds: [h.ProjectId]);

        Assert.Equal(1, history.Totals.Sessions);
        Assert.Equal(1_000, history.Totals.TotalTokens);
        Assert.Equal(1_000, history.Buckets.Sum(bk => bk.Totals.TotalTokens));
        Assert.Equal(only, history.FirstSessionAt);
        Assert.Equal(only, history.LastSessionAt);
    }

    [Fact]
    public async Task TwoOfThreeProjectIds_IncludeTheirSpendButNotTheThirds()
    {
        var h = await NewAsync();
        var mine = await h.FileAsync();
        var (b, c) = await h.TwoMoreProjectsAsync();
        var inB = await h.FileAsync(projectId: b.Id);
        var inC = await h.FileAsync(projectId: c.Id);

        await h.SpendAsync(mine, 1_000, Now.AddHours(-3));
        await h.SpendAsync(inB, 2_000, Now.AddHours(-2));
        await h.SpendAsync(inC, 4_000, Now.AddHours(-1));

        var history = await h.HistoryAsync(projectIds: [h.ProjectId, b.Id]);

        Assert.Equal(2, history.Totals.Sessions);
        Assert.Equal(3_000, history.Totals.TotalTokens);
    }

    [Fact]
    public async Task AnUnknownProjectIdMixedWithAKnownOne_OnlyTheKnownOnesSpendCounts_AndAloneAnswersZeroed()
    {
        var h = await NewAsync();
        var mine = await h.FileAsync();

        await h.SpendAsync(mine, 1_000, Now.AddHours(-1));

        var mixed = await h.HistoryAsync(projectIds: [h.ProjectId, 999_999]);
        var unknownAlone = await h.HistoryAsync(projectIds: [999_999]);

        Assert.Equal(1, mixed.Totals.Sessions);
        Assert.Equal(1_000, mixed.Totals.TotalTokens);

        Assert.Equal(0, unknownAlone.Totals.Sessions);
        Assert.Equal(0, unknownAlone.Totals.TotalTokens);
        Assert.Null(unknownAlone.FirstSessionAt);
        Assert.Null(unknownAlone.LastSessionAt);
    }

    [Fact]
    public async Task ARepeatedProjectId_CountsOnce()
    {
        var h = await NewAsync();
        var mine = await h.FileAsync();
        var (b, _) = await h.TwoMoreProjectsAsync();
        var inB = await h.FileAsync(projectId: b.Id);

        await h.SpendAsync(mine, 1_000, Now.AddHours(-1));
        await h.SpendAsync(inB, 2_000, Now.AddHours(-1));

        var once = await h.HistoryAsync(projectIds: [h.ProjectId]);
        var repeated = await h.HistoryAsync(projectIds: [h.ProjectId, h.ProjectId]);

        Assert.Equal(once.Totals, repeated.Totals);
    }

    [Fact]
    public async Task AnAncestorKeyAndAProjectId_IntersectRatherThanUnion()
    {
        var h = await NewAsync();
        var (b, _) = await h.TwoMoreProjectsAsync();
        var epic = await h.FileAsync("epic", "the effort");

        await h.SpendAsync(epic, 1_000, Now.AddHours(-1));

        var crossed = await h.HistoryAsync(ancestorKey: epic.Key, projectIds: [b.Id]);
        var matched = await h.HistoryAsync(ancestorKey: epic.Key, projectIds: [h.ProjectId]);

        Assert.Equal(0, crossed.Totals.Sessions);
        Assert.Equal(1, matched.Totals.Sessions);
    }

    // ---- Narrowing to an outcome ----

    [Fact]
    public async Task AnOutcome_NarrowsTheBucketsAndTheTotals_ButNotTheOtherOutcomesSpend()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.SpendAsync(issue, 1_000, Now.AddHours(-3), outcome: "dropped");
        await h.SpendAsync(issue, 2_000, Now.AddHours(-2), outcome: "worked");

        var dropped = await h.HistoryAsync(bucket: "hour", outcome: "dropped");
        var whole = await h.HistoryAsync(bucket: "hour");

        Assert.Equal(1, dropped.Totals.Sessions);
        Assert.Equal(1_000, dropped.Totals.TotalTokens);
        Assert.Equal(1_000, dropped.Buckets.Sum(b => b.Totals.TotalTokens));
        Assert.Equal(2, whole.Totals.Sessions);
    }

    [Fact]
    public async Task AnUnknownOutcome_IsRefusedNamingTheAcceptedValues()
    {
        var h = await NewAsync();

        var sentence = await h.RefusalAsync(outcome: "finished");

        Assert.Equal(
            "an outcome is dropped, worked, preempted, recused, unservable, abandoned, unstarted or none - not \"finished\"",
            sentence);
    }

    // ---- When there is nothing yet ----

    [Fact]
    public async Task AnEmptyLog_IsZeroedBucketsAndNoBounds()
    {
        var h = await NewAsync();

        var history = await h.HistoryAsync(from: "2026-09-06T00:00:00Z", to: "2026-09-07T00:00:00Z");

        // The ordinary state on the first day, not a fault.
        Assert.Equal(24, history.Buckets.Count);
        Assert.All(history.Buckets, b => Assert.Equal(0, b.Totals.Sessions));
        Assert.Null(history.FirstSessionAt);
        Assert.Null(history.LastSessionAt);
    }

    [Fact]
    public async Task ARangeBeforeTheFirstSession_AnswersWithTheBoundsStillFilledIn()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var ended = Now.AddHours(-1);
        await h.SpendAsync(issue, 1_000, ended);

        var history = await h.HistoryAsync(from: "2026-01-01T00:00:00Z", to: "2026-01-03T00:00:00Z");

        // "Nothing ran in the range you asked for" and "nothing has ever run"
        // are different answers, and a page needs to tell them apart.
        Assert.Equal(0, history.Totals.Sessions);
        Assert.Equal(ended, history.FirstSessionAt);
        Assert.Equal(ended, history.LastSessionAt);
    }

    // ---- Spend over time ----

    [Fact]
    public async Task TheBuckets_ComeBackOldestFirstAndAddUpToTheTotals()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.SpendAsync(issue, 1_000, new DateTimeOffset(2026, 9, 7, 0, 30, 0, TimeSpan.Zero), usd: 0.10m);
        await h.SpendAsync(issue, 20_000, new DateTimeOffset(2026, 9, 7, 2, 15, 0, TimeSpan.Zero), usd: 2.00m);
        await h.SpendAsync(issue, 300_000, new DateTimeOffset(2026, 9, 7, 2, 45, 0, TimeSpan.Zero), usd: 30.00m, isError: true);

        var history = await h.HistoryAsync(from: "2026-09-07T00:00:00Z", to: "2026-09-07T03:00:00Z");

        Assert.Equal(
            history.Buckets.Select(b => b.Start).OrderBy(s => s),
            history.Buckets.Select(b => b.Start));

        Assert.Equal([1, 0, 2], history.Buckets.Select(b => b.Totals.Sessions));
        Assert.Equal(3, history.Totals.Sessions);
        Assert.Equal(1, history.Totals.Errors);
        Assert.Equal(321_000, history.Totals.TotalTokens);
        Assert.Equal(32.10m, history.Totals.CostUsd);
        Assert.Equal(history.Totals.CostUsd, history.Buckets.Sum(b => b.Totals.CostUsd));
    }

    // ---- What it refuses ----

    [Fact]
    public async Task AnUnknownBucket_IsRefusedWithTheSentence()
    {
        var h = await NewAsync();

        Assert.Equal("a bucket is hour or day - not \"week\"", await h.RefusalAsync(bucket: "week"));
    }

    [Fact]
    public async Task AnUnparseableBound_IsRefusedInTheHousesOwnWords()
    {
        var h = await NewAsync();

        Assert.Equal(
            "a range bound is an instant (2026-09-12T17:00:00Z) - not \"last tuesday\"",
            await h.RefusalAsync(from: "last tuesday"));
        Assert.Equal(
            "a range bound is an instant (2026-09-12T17:00:00Z) - not \"09/12/2026\"",
            await h.RefusalAsync(to: "09/12/2026"));
    }

    [Fact]
    public async Task AnInvertedRange_IsRefusedAndAnEmptyOneIsOneBucket()
    {
        var h = await NewAsync();

        Assert.Equal(
            "a range ends before it begins",
            await h.RefusalAsync(from: "2026-09-07T03:00:00Z", to: "2026-09-06T03:00:00Z"));

        // Equal is legal: one bucket rather than none.
        var single = await h.HistoryAsync(from: "2026-09-07T03:00:00Z", to: "2026-09-07T03:00:00Z");
        Assert.Single(single.Buckets);
    }

    [Fact]
    public async Task TooManyBuckets_IsRefusedBeforeAnythingIsScanned()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.SpendAsync(issue, 1_000, Now.AddHours(-1));

        var refusal = await h.RefusalAsync(from: "2026-06-01T00:00:00Z", to: "2026-09-07T00:00:00Z", bucket: "hour");

        Assert.Contains("1000", refusal);
        Assert.Contains("ask for daily buckets, or a shorter range", refusal);

        // Untouched: "how far back does this go" is a question the answer should
        // settle rather than a licence to scan the table.
        Assert.Single(h.Db.WorkLog);
    }

    [Fact]
    public async Task AnOffsetThatIsNotAnOffset_IsRefused()
    {
        var h = await NewAsync();

        Assert.Equal(
            "an offset from UTC is minutes between -1440 and 1440 - not 9999",
            await h.RefusalAsync(offsetMinutes: 9999));
    }

    // ---- The sessions, ranked ----

    [Fact]
    public async Task TheSessionsRange_IsTheFourteenDaysEndingNowAndIsNotSnapped()
    {
        var h = await NewAsync();

        var sessions = await h.SessionsAsync();

        // Echoed exactly as resolved, unlike `history`, which snaps both bounds
        // outward onto the bucket grid it draws on. There is no grid here.
        Assert.Equal(Now.AddDays(-14), sessions.From);
        Assert.Equal(Now, sessions.To);
        Assert.Equal("tokens", sessions.Sort);
    }

    [Fact]
    public async Task OneSessionsBoundOnItsOwn_GetsTheOthersDefault()
    {
        var h = await NewAsync();

        var since = await h.SessionsAsync(from: "2026-09-06T03:00:00Z");
        var until = await h.SessionsAsync(to: "2026-09-06T03:00:00Z");

        Assert.Equal(Now, since.To);
        Assert.Equal(new DateTimeOffset(2026, 8, 23, 3, 0, 0, TimeSpan.Zero), until.From);
    }

    [Fact]
    public async Task TheDefaultSort_IsTokensDescending()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.SpendAsync(issue, 1_000, Now.AddHours(-3));
        await h.SpendAsync(issue, 300_000, Now.AddHours(-2));
        await h.SpendAsync(issue, 20_000, Now.AddHours(-1));

        var sessions = await h.SessionsAsync();

        Assert.Equal([300_000, 20_000, 1_000], sessions.Sessions.Select(s => s.TotalTokens));
    }

    [Fact]
    public async Task ASessionsAgent_IsCarriedExactlyAsStored()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.SpendAsync(issue, 1_000, Now.AddHours(-1), agent: "claude");

        Assert.Equal("claude", Assert.Single((await h.SessionsAsync()).Sessions).Agent);
    }

    [Fact]
    public async Task ASessionsModelPlacementAndDigest_AreCarriedExactlyAsStored_AndNullForAnOlderRow()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.SpendAsync(issue, 4_000, Now.AddHours(-4), dispatchModel: "gemma3:4b", modelPlacement: "local", modelDigest: "sha256:abc");
        await h.SpendAsync(issue, 3_000, Now.AddHours(-3), dispatchModel: "sonnet", modelPlacement: "fallback");
        await h.SpendAsync(issue, 2_000, Now.AddHours(-2), dispatchModel: "sonnet", modelPlacement: "cloud");
        await h.SpendAsync(issue, 1_000, Now.AddHours(-1));

        var rows = (await h.SessionsAsync(sort: "tokens")).Sessions;

        Assert.Equal(("gemma3:4b", "local", "sha256:abc"), (rows[0].DispatchModel, rows[0].ModelPlacement, rows[0].ModelDigest));
        Assert.Equal(("sonnet", "fallback", (string?)null), (rows[1].DispatchModel, rows[1].ModelPlacement, rows[1].ModelDigest));
        Assert.Equal(("sonnet", "cloud", (string?)null), (rows[2].DispatchModel, rows[2].ModelPlacement, rows[2].ModelDigest));
        Assert.Equal(((string?)null, (string?)null, (string?)null), (rows[3].DispatchModel, rows[3].ModelPlacement, rows[3].ModelDigest));
    }

    [Fact]
    public async Task CostRanksOnTheDollars_AndEndedIsNewestFirst()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.SpendAsync(issue, 300_000, Now.AddHours(-3), usd: 0.10m);
        await h.SpendAsync(issue, 1_000, Now.AddHours(-2), usd: 30.00m);
        await h.SpendAsync(issue, 20_000, Now.AddHours(-1), usd: 2.00m);

        var byCost = await h.SessionsAsync(sort: "cost");
        var byEnded = await h.SessionsAsync(sort: "ended");

        Assert.Equal("cost", byCost.Sort);
        Assert.Equal([30.00m, 2.00m, 0.10m], byCost.Sessions.Select(s => s.CostUsd));
        Assert.Equal("ended", byEnded.Sort);
        Assert.Equal(
            byEnded.Sessions.Select(s => s.EndedAt).OrderByDescending(e => e),
            byEnded.Sessions.Select(s => s.EndedAt));
    }

    [Fact]
    public async Task CostPutsAnUnpricedSessionLast_AfterEvenAPricedZero_AndTheTotalsCountIt()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.SpendAsync(issue, 1_000, Now.AddHours(-3), usd: null);
        await h.SpendAsync(issue, 2_000, Now.AddHours(-2), usd: 0m);
        await h.SpendAsync(issue, 3_000, Now.AddHours(-1), usd: 2.00m);

        var byCost = await h.SessionsAsync(sort: "cost");

        Assert.Equal([2.00m, 0m, null], byCost.Sessions.Select(s => s.CostUsd));
        Assert.Equal(1, byCost.Totals.UnpricedSessions);
        Assert.Equal(2.00m, byCost.Totals.CostUsd);
        Assert.Equal(6_000, byCost.Totals.TotalTokens);
    }

    [Fact]
    public async Task RequestsRanksOnItsOwnCount_AndNullsSortLast()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.SpendAsync(issue, 1_000, Now.AddHours(-3), requests: 12);
        await h.SpendAsync(issue, 1_000, Now.AddHours(-2), requests: 103);
        await h.SpendAsync(issue, 1_000, Now.AddHours(-1), requests: null);

        var byRequests = await h.SessionsAsync(sort: "requests");

        Assert.Equal("requests", byRequests.Sort);
        Assert.Equal([103, 12, null], byRequests.Sessions.Select(s => s.Requests));
    }

    [Fact]
    public async Task PeakContextRanksOnItsOwnFigure_AndNullsSortLast()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.SpendAsync(issue, 1_000, Now.AddHours(-3), peakContextTokens: 70_000);
        await h.SpendAsync(issue, 1_000, Now.AddHours(-2), peakContextTokens: 264_000);
        await h.SpendAsync(issue, 1_000, Now.AddHours(-1), peakContextTokens: null);

        var byPeak = await h.SessionsAsync(sort: "peakContext");

        Assert.Equal("peakContext", byPeak.Sort);
        Assert.Equal([264_000, 70_000, null], byPeak.Sessions.Select(s => s.PeakContextTokens));
    }

    [Fact]
    public async Task TiedSessions_BreakOnWhenTheyEndedAndThenOnId()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var older = await h.SpendAsync(issue, 5_000, Now.AddHours(-2));
        var first = await h.SpendAsync(issue, 5_000, Now.AddHours(-1));
        var second = await h.SpendAsync(issue, 5_000, Now.AddHours(-1));

        var sessions = await h.SessionsAsync();

        // Two reads of one filter come back in one order, which is what makes a
        // capped list stable across a refresh.
        Assert.Equal([second, first, older], sessions.Sessions.Select(s => s.Id));
    }

    [Fact]
    public async Task ASort_IsTakenInEitherCaseAndWithSpaceAroundIt()
    {
        var h = await NewAsync();

        Assert.Equal("cost", (await h.SessionsAsync(sort: "COST")).Sort);
        Assert.Equal("ended", (await h.SessionsAsync(sort: " Ended ")).Sort);
        Assert.Equal("tokens", (await h.SessionsAsync(sort: "  ")).Sort);
    }

    [Fact]
    public async Task TheCap_LimitsTheRowsAndNeverTheTotals()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        for (var i = 0; i < 120; i++) await h.SpendAsync(issue, 1_000, Now.AddMinutes(-i - 1));

        var sessions = await h.SessionsAsync();

        // A hundred rows under a total of 120 is a cap, and the page says so -
        // the number itself is never a sample.
        Assert.Equal(100, sessions.Sessions.Count);
        Assert.Equal(120, sessions.Totals.Sessions);
        Assert.Equal(120_000, sessions.Totals.TotalTokens);
    }

    [Fact]
    public async Task AnExplicitLimit_IsHonouredAtEitherEnd()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        for (var i = 0; i < 3; i++) await h.SpendAsync(issue, 1_000, Now.AddMinutes(-i - 1));

        Assert.Single((await h.SessionsAsync(limit: 1)).Sessions);
        Assert.Equal(3, (await h.SessionsAsync(limit: 500)).Sessions.Count);
    }

    [Fact]
    public async Task AnAncestorKeyOnTheSessions_CoversTheIssueItselfAndEveryDepthBeneathIt()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the effort");
        var story = await h.FileAsync("story", "under it", parentKey: epic.Key);
        var task = await h.FileAsync("task", "under that", parentKey: story.Key);
        var elsewhere = await h.FileAsync("epic", "another effort");

        await h.SpendAsync(epic, 1_000, Now.AddHours(-3));
        await h.SpendAsync(story, 20_000, Now.AddHours(-2));
        await h.SpendAsync(task, 300_000, Now.AddHours(-1));
        await h.SpendAsync(elsewhere, 7_000_000, Now.AddHours(-1));

        var scoped = await h.SessionsAsync(ancestorKey: epic.Key);

        // The named issue's own sessions are in it: a planning session run
        // against the epic is money no child holds.
        Assert.Equal(3, scoped.Totals.Sessions);
        Assert.Equal(321_000, scoped.Totals.TotalTokens);
        Assert.DoesNotContain(elsewhere.Key, scoped.Sessions.Select(s => s.IssueKey));
    }

    [Fact]
    public async Task AnErroredSession_IsCountedAndItsSpendCounts()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.SpendAsync(issue, 1_000, Now.AddHours(-2), usd: 0.10m);
        await h.SpendAsync(issue, 300_000, Now.AddHours(-1), usd: 30.00m, isError: true);

        var sessions = await h.SessionsAsync();

        // It ran, and it was billed for running.
        Assert.Equal(2, sessions.Totals.Sessions);
        Assert.Equal(1, sessions.Totals.Errors);
        Assert.Equal(30.10m, sessions.Totals.CostUsd);
        Assert.True(sessions.Sessions[0].IsError);
    }

    [Fact]
    public async Task EachRow_NamesTheIssueItWasRunAgainstAndWhatItSaidOfItself()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "the leaderboard");

        await h.SpendAsync(issue, 20_000, Now.AddHours(-2), title: "Broke the ranking out of the rollup", turns: 41);
        await h.SpendAsync(issue, 1_000, Now.AddHours(-1));

        var sessions = await h.SessionsAsync();

        var described = sessions.Sessions[0];
        Assert.Equal(issue.Key, described.IssueKey);
        Assert.Equal("the leaderboard", described.IssueTitle);
        Assert.Equal("Broke the ranking out of the rollup", described.Title);
        Assert.True(described.Described);
        Assert.Equal(41, described.Turns);
        Assert.Equal(1_000, described.DurationMs);

        // A session that never said what it did is marked, not blanked.
        Assert.False(sessions.Sessions[1].Described);
        Assert.Null(sessions.Sessions[1].Title);
    }

    [Fact]
    public async Task ARangeWithNothingInIt_IsAnEmptyListAndZeroedTotals()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.SpendAsync(issue, 1_000, Now.AddHours(-1));

        var sessions = await h.SessionsAsync(from: "2026-01-01T00:00:00Z", to: "2026-01-03T00:00:00Z");

        Assert.Empty(sessions.Sessions);
        Assert.Equal(0, sessions.Totals.Sessions);
        Assert.Equal(0m, sessions.Totals.CostUsd);
    }

    [Fact]
    public async Task TheTwoBounds_IgnoreTheRangeAndRespectTheAncestor()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the effort");
        var elsewhere = await h.FileAsync("epic", "another effort");

        var first = Now.AddDays(-40);
        var last = Now.AddHours(-1);
        await h.SpendAsync(epic, 1_000, first);
        await h.SpendAsync(epic, 1_000, last);
        await h.SpendAsync(elsewhere, 1_000, Now.AddDays(-90));

        var scoped = await h.SessionsAsync(ancestorKey: epic.Key);
        var empty = await h.SessionsAsync(ancestorKey: (await h.FileAsync()).Key);

        // Outside the fourteen-day default on both sides, and still reported:
        // it is how a page tells "nothing has ever been logged" from "nothing
        // ran in the range you asked for".
        Assert.Equal(first, scoped.FirstSessionAt);
        Assert.Equal(last, scoped.LastSessionAt);

        // The range still applies to everything else: only the session inside
        // the fourteen-day default is counted.
        Assert.Equal(1, scoped.Totals.Sessions);
        Assert.Null(empty.FirstSessionAt);
        Assert.Null(empty.LastSessionAt);
    }

    // ---- Narrowing the sessions to several projects ----

    [Fact]
    public async Task TheSessions_NoProjectId_CountsEveryProjectsSpend()
    {
        var h = await NewAsync();
        var mine = await h.FileAsync();
        var (b, c) = await h.TwoMoreProjectsAsync();
        var inB = await h.FileAsync(projectId: b.Id);
        var inC = await h.FileAsync(projectId: c.Id);

        await h.SpendAsync(mine, 1_000, Now.AddHours(-3));
        await h.SpendAsync(inB, 2_000, Now.AddHours(-2));
        await h.SpendAsync(inC, 4_000, Now.AddHours(-1));

        var sessions = await h.SessionsAsync();

        Assert.Equal(3, sessions.Totals.Sessions);
        Assert.Equal(7_000, sessions.Totals.TotalTokens);
    }

    [Fact]
    public async Task TheSessions_OneProjectId_NarrowsTotalsAndBounds()
    {
        var h = await NewAsync();
        var mine = await h.FileAsync();
        var (b, c) = await h.TwoMoreProjectsAsync();
        var inB = await h.FileAsync(projectId: b.Id);
        var inC = await h.FileAsync(projectId: c.Id);

        var only = Now.AddHours(-3);
        await h.SpendAsync(mine, 1_000, only);
        await h.SpendAsync(inB, 2_000, Now.AddHours(-2));
        await h.SpendAsync(inC, 4_000, Now.AddHours(-1));

        var sessions = await h.SessionsAsync(projectIds: [h.ProjectId]);

        Assert.Equal(1, sessions.Totals.Sessions);
        Assert.Equal(1_000, sessions.Totals.TotalTokens);
        Assert.Equal(only, sessions.FirstSessionAt);
        Assert.Equal(only, sessions.LastSessionAt);
    }

    [Fact]
    public async Task TheSessions_TwoOfThreeProjectIds_IncludeTheirSpendButNotTheThirds()
    {
        var h = await NewAsync();
        var mine = await h.FileAsync();
        var (b, c) = await h.TwoMoreProjectsAsync();
        var inB = await h.FileAsync(projectId: b.Id);
        var inC = await h.FileAsync(projectId: c.Id);

        await h.SpendAsync(mine, 1_000, Now.AddHours(-3));
        await h.SpendAsync(inB, 2_000, Now.AddHours(-2));
        await h.SpendAsync(inC, 4_000, Now.AddHours(-1));

        var sessions = await h.SessionsAsync(projectIds: [h.ProjectId, b.Id]);

        Assert.Equal(2, sessions.Totals.Sessions);
        Assert.Equal(3_000, sessions.Totals.TotalTokens);
    }

    [Fact]
    public async Task TheSessions_AnUnknownProjectIdMixedWithAKnownOne_OnlyTheKnownOnesSpendCounts_AndAloneAnswersZeroed()
    {
        var h = await NewAsync();
        var mine = await h.FileAsync();

        await h.SpendAsync(mine, 1_000, Now.AddHours(-1));

        var mixed = await h.SessionsAsync(projectIds: [h.ProjectId, 999_999]);
        var unknownAlone = await h.SessionsAsync(projectIds: [999_999]);

        Assert.Equal(1, mixed.Totals.Sessions);
        Assert.Equal(1_000, mixed.Totals.TotalTokens);

        Assert.Empty(unknownAlone.Sessions);
        Assert.Equal(0, unknownAlone.Totals.Sessions);
        Assert.Equal(0, unknownAlone.Totals.TotalTokens);
        Assert.Null(unknownAlone.FirstSessionAt);
        Assert.Null(unknownAlone.LastSessionAt);
    }

    [Fact]
    public async Task TheSessions_ARepeatedProjectId_CountsOnce()
    {
        var h = await NewAsync();
        var mine = await h.FileAsync();
        var (b, _) = await h.TwoMoreProjectsAsync();
        var inB = await h.FileAsync(projectId: b.Id);

        await h.SpendAsync(mine, 1_000, Now.AddHours(-1));
        await h.SpendAsync(inB, 2_000, Now.AddHours(-1));

        var once = await h.SessionsAsync(projectIds: [h.ProjectId]);
        var repeated = await h.SessionsAsync(projectIds: [h.ProjectId, h.ProjectId]);

        Assert.Equal(once.Totals, repeated.Totals);
    }

    [Fact]
    public async Task TheSessions_ALimitSmallerThanTheNarrowedSet_StillReportsTheNarrowedTotals()
    {
        var h = await NewAsync();
        var mine = await h.FileAsync();
        var (b, _) = await h.TwoMoreProjectsAsync();
        var inB = await h.FileAsync(projectId: b.Id);

        await h.SpendAsync(mine, 1_000, Now.AddHours(-3));
        await h.SpendAsync(mine, 2_000, Now.AddHours(-2));
        await h.SpendAsync(mine, 3_000, Now.AddHours(-1));
        await h.SpendAsync(inB, 7_000_000, Now.AddHours(-1));

        var sessions = await h.SessionsAsync(projectIds: [h.ProjectId], limit: 2);

        Assert.Equal(2, sessions.Sessions.Count);
        Assert.Equal(3, sessions.Totals.Sessions);
        Assert.Equal(6_000, sessions.Totals.TotalTokens);
    }

    [Fact]
    public async Task TheSessions_AnAncestorKeyAndAProjectId_IntersectRatherThanUnion()
    {
        var h = await NewAsync();
        var (b, _) = await h.TwoMoreProjectsAsync();
        var epic = await h.FileAsync("epic", "the effort");

        await h.SpendAsync(epic, 1_000, Now.AddHours(-1));

        var crossed = await h.SessionsAsync(ancestorKey: epic.Key, projectIds: [b.Id]);
        var matched = await h.SessionsAsync(ancestorKey: epic.Key, projectIds: [h.ProjectId]);

        Assert.Equal(0, crossed.Totals.Sessions);
        Assert.Equal(1, matched.Totals.Sessions);
    }

    [Fact]
    public async Task TheSessionsTopReads_SumsAcrossSessions_RankedHeaviestFirst()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.SpendAsync(issue, 1_000, Now.AddHours(-2),
            reads: [new WorkLogReadDto("a.cs", 3), new WorkLogReadDto("b.cs", 1)]);
        await h.SpendAsync(issue, 1_000, Now.AddHours(-1),
            reads: [new WorkLogReadDto("a.cs", 2), new WorkLogReadDto("c.cs", 5)]);

        var sessions = await h.SessionsAsync();

        Assert.Equal(
            [("a.cs", 5), ("c.cs", 5), ("b.cs", 1)],
            sessions.TopReads.Select(r => (r.Path, r.Count)));
    }

    // ---- What the sessions read refuses ----

    [Fact]
    public async Task TheSessionsRead_RefusesABadBoundAndAnInvertedRangeInTheSameWords()
    {
        var h = await NewAsync();

        Assert.Equal(
            "a range bound is an instant (2026-09-12T17:00:00Z) - not \"last tuesday\"",
            await h.SessionRefusalAsync(from: "last tuesday"));
        Assert.Equal(
            "a range ends before it begins",
            await h.SessionRefusalAsync(from: "2026-09-07T03:00:00Z", to: "2026-09-06T03:00:00Z"));
        Assert.Equal("there is no AER-999", await h.SessionRefusalAsync(ancestorKey: "AER-999"));
    }

    [Fact]
    public async Task AnUnknownSort_IsRefusedWithTheSentence()
    {
        var h = await NewAsync();

        Assert.Equal(
            "a sort is tokens, cost, ended, requests or peakContext - not \"turns\"",
            await h.SessionRefusalAsync(sort: "turns"));
    }

    [Fact]
    public async Task ALimitOutsideTheCap_IsRefusedWithTheCapInIt()
    {
        var h = await NewAsync();

        Assert.Equal("a limit is between 1 and 500 - not 0", await h.SessionRefusalAsync(limit: 0));
        Assert.Equal("a limit is between 1 and 500 - not -1", await h.SessionRefusalAsync(limit: -1));
        Assert.Equal("a limit is between 1 and 500 - not 501", await h.SessionRefusalAsync(limit: 501));
    }

    // ---- The top, asked for beside the table ----

    [Fact]
    public async Task WithoutATop_TheAnswerCarriesNone()
    {
        var h = await NewAsync();
        await h.SpendAsync(await h.FileAsync(), 1_000, Now.AddHours(-1));

        Assert.Null((await h.SessionsAsync()).Top);
    }

    [Fact]
    public async Task ATop_IsRankedByTokens_WhateverTheTableIsSortedByOrCappedTo_AndTheTotalsAreTheSame()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.SpendAsync(issue, 5_000, Now.AddHours(-4));
        await h.SpendAsync(issue, 90_000, Now.AddHours(-3));
        await h.SpendAsync(issue, 7_000, Now.AddHours(-2));
        await h.SpendAsync(issue, 30_000, Now.AddHours(-1));

        var plain = await h.SessionsAsync(sort: "ended", limit: 2);
        var asked = await h.SessionsAsync(sort: "ended", limit: 2, top: 3);

        Assert.Equal([30_000, 7_000], asked.Sessions.Select(s => s.TotalTokens));
        Assert.Equal([90_000, 30_000, 7_000], asked.Top!.Select(s => s.TotalTokens));
        Assert.Equal(plain.Sessions, asked.Sessions);
        Assert.Equal(plain.Totals, asked.Totals);
        Assert.Equal(plain.TopReads, asked.TopReads);
        Assert.Equal(4, asked.Totals.Sessions);
    }

    [Fact]
    public async Task ATop_AtTheTokensSort_IsTheSameRowsAsTheTable()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        for (var i = 1; i <= 4; i++) await h.SpendAsync(issue, i * 1_000, Now.AddHours(-i));

        var sessions = await h.SessionsAsync(sort: "tokens", limit: 3, top: 3);

        Assert.Equal(sessions.Sessions, sessions.Top);
    }

    [Fact]
    public async Task ATop_BreaksATieOnTokens_OnEndedThenId()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var older = await h.SpendAsync(issue, 4_000, Now.AddHours(-3));
        var newer = await h.SpendAsync(issue, 4_000, Now.AddHours(-1));
        var sameEndA = await h.SpendAsync(issue, 4_000, Now.AddHours(-2));
        var sameEndB = await h.SpendAsync(issue, 4_000, Now.AddHours(-2));

        var top = (await h.SessionsAsync(sort: "cost", top: 4)).Top!;

        Assert.Equal([newer, sameEndB, sameEndA, older], top.Select(s => s.Id));
    }

    [Fact]
    public async Task ATop_IsNarrowedByTheOutcomeTheProjectAndTheAncestor_AsTheTableIs()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the effort");
        var story = await h.FileAsync("story", "under it", parentKey: epic.Key);
        var elsewhere = await h.FileAsync("epic", "another effort");
        var (b, _) = await h.TwoMoreProjectsAsync();
        var inB = await h.FileAsync(projectId: b.Id);

        await h.SpendAsync(story, 1_000, Now.AddHours(-5), outcome: "dropped");
        await h.SpendAsync(story, 2_000, Now.AddHours(-4), outcome: "worked");
        await h.SpendAsync(elsewhere, 900_000, Now.AddHours(-3), outcome: "dropped");
        await h.SpendAsync(inB, 800_000, Now.AddHours(-2), outcome: "dropped");

        var byOutcome = await h.SessionsAsync(outcome: "dropped", top: 5);
        var byProject = await h.SessionsAsync(projectIds: [h.ProjectId], top: 5);
        var byAncestor = await h.SessionsAsync(ancestorKey: epic.Key, top: 5);
        var both = await h.SessionsAsync(ancestorKey: epic.Key, outcome: "dropped", top: 5);

        Assert.Equal([900_000, 800_000, 1_000], byOutcome.Top!.Select(s => s.TotalTokens));
        Assert.Equal([900_000, 2_000, 1_000], byProject.Top!.Select(s => s.TotalTokens));
        Assert.Equal([2_000, 1_000], byAncestor.Top!.Select(s => s.TotalTokens));
        Assert.Equal([1_000], both.Top!.Select(s => s.TotalTokens));
        Assert.Equal(byOutcome.Sessions, byOutcome.Top);
        Assert.Equal(byAncestor.Sessions, byAncestor.Top);
    }

    [Fact]
    public async Task ATopOutsideTheCap_IsRefusedWithTheCapInIt()
    {
        var h = await NewAsync();

        Assert.Equal("a top is between 1 and 500 - not 0", await h.SessionRefusalAsync(top: 0));
        Assert.Equal("a top is between 1 and 500 - not 501", await h.SessionRefusalAsync(top: 501));
    }

    // ---- Folded by playbook ----

    [Fact]
    public async Task TheByPlaybookRange_IsHalfOpen_FromIsInAndToIsOut()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        var from = Now.AddHours(-2);
        var to = Now.AddHours(-1);

        await h.SpendAsync(issue, 1_000, from.AddTicks(-1), playbookKey: "before");
        await h.SpendAsync(issue, 20_000, from, playbookKey: "at-from");
        await h.SpendAsync(issue, 300_000, to.AddTicks(-1), playbookKey: "inside");
        await h.SpendAsync(issue, 4_000_000, to, playbookKey: "at-to");

        var byPlaybook = await h.ByPlaybookAsync(from.ToString("o"), to.ToString("o"));

        Assert.Equal(from, byPlaybook.From);
        Assert.Equal(to, byPlaybook.To);
        Assert.Equal(["inside", "at-from"], byPlaybook.Rows.Select(r => r.PlaybookKey));
    }

    [Fact]
    public async Task TheByPlaybookRange_DefaultsToTheFourteenDaysEndingNow()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.SpendAsync(issue, 1_000, Now.AddDays(-20), playbookKey: "old");
        await h.SpendAsync(issue, 2_000, Now.AddHours(-1), playbookKey: "recent");

        var byPlaybook = await h.ByPlaybookAsync();

        Assert.Equal(Now.AddDays(-14), byPlaybook.From);
        Assert.Equal(Now, byPlaybook.To);
        Assert.Equal("recent", Assert.Single(byPlaybook.Rows).PlaybookKey);
    }

    [Fact]
    public async Task ByPlaybook_FoldsEachCombinationOnce_AndEveryOlderRowIntoOneUnknown()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.SpendAsync(issue, 1_000, Now.AddHours(-5), playbookKey: "7", fromStatusId: 1, toStatusId: 2,
            issueType: "task", model: "opus", effort: "high", peakContextTokens: 100, promptChars: 10);
        await h.SpendAsync(issue, 2_000, Now.AddHours(-4), playbookKey: "7", fromStatusId: 1, toStatusId: 2,
            issueType: "task", model: "opus", effort: "high", peakContextTokens: 300, promptChars: 30);
        await h.SpendAsync(issue, 3_000, Now.AddHours(-3), playbookKey: "7", fromStatusId: 1, toStatusId: 2,
            issueType: "task", model: "sonnet", effort: "high");
        await h.SpendAsync(issue, 4_000, Now.AddHours(-2));
        await h.SpendAsync(issue, 5_000, Now.AddHours(-1));

        var rows = (await h.ByPlaybookAsync()).Rows;

        Assert.Equal(3, rows.Count);

        var opus = Assert.Single(rows, r => r.Model == "opus");
        Assert.Equal(2, opus.Sessions);
        Assert.Equal(3_000, opus.TotalTokens);
        Assert.Equal(200, opus.MedianPeakContextTokens);
        Assert.Equal(300, opus.MaxPeakContextTokens);
        Assert.Equal(20, opus.MedianPromptChars);

        var unknown = Assert.Single(rows, r => r.PlaybookKey is null);
        Assert.Equal(2, unknown.Sessions);
        Assert.Equal(9_000, unknown.TotalTokens);
    }

    [Fact]
    public async Task Sessions_NarrowToOneOutcome_CostliestFirst_EachNamingItsReason_AndTheTotalsFollow()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.SpendAsync(issue, 1_000, Now.AddHours(-4), usd: 1.00m, outcome: "dropped", reason: "quiet");
        await h.SpendAsync(issue, 1_000, Now.AddHours(-3), usd: 9.00m, outcome: "dropped", reason: "run-time");
        await h.SpendAsync(issue, 1_000, Now.AddHours(-2), usd: 50.00m, outcome: "worked");
        await h.SpendAsync(issue, 1_000, Now.AddHours(-1), usd: 70.00m);

        var sessions = await h.SessionsAsync(sort: "cost", outcome: "dropped");

        Assert.Equal(["run-time", "quiet"], sessions.Sessions.Select(r => r.Reason));
        Assert.All(sessions.Sessions, r => Assert.Equal("dropped", r.Outcome));
        Assert.Equal(2, sessions.Totals.Sessions);
        Assert.Equal(10.00m, sessions.Totals.CostUsd);
    }

    [Fact]
    public async Task Sessions_OutcomeNone_ReadsOnlyTheUnstampedRows()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.SpendAsync(issue, 1_000, Now.AddHours(-2), outcome: "worked");
        await h.SpendAsync(issue, 2_000, Now.AddHours(-1));

        var row = Assert.Single((await h.SessionsAsync(outcome: "none")).Sessions);

        Assert.Null(row.Outcome);
        Assert.Null(row.Reason);
        Assert.Equal(2_000, row.TotalTokens);
    }

    [Fact]
    public async Task Sessions_WithoutAnOutcome_ReadEverySession_EachCarryingItsOutcomeAndReason()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.SpendAsync(issue, 1_000, Now.AddHours(-2), outcome: "dropped", reason: "quiet");
        await h.SpendAsync(issue, 2_000, Now.AddHours(-1));

        var sessions = await h.SessionsAsync();

        Assert.Equal(2, sessions.Totals.Sessions);
        Assert.Equal("quiet", Assert.Single(sessions.Sessions, r => r.Outcome == "dropped").Reason);
    }

    [Fact]
    public async Task Sessions_TheOutcomeIsCaseInsensitive_AndBlankIsAbsent()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.SpendAsync(issue, 1_000, Now.AddHours(-2), outcome: "dropped");
        await h.SpendAsync(issue, 2_000, Now.AddHours(-1), outcome: "worked");

        Assert.Equal("dropped", Assert.Single((await h.SessionsAsync(outcome: " DROPPED ")).Sessions).Outcome);
        Assert.Equal(2, (await h.SessionsAsync(outcome: "  ")).Sessions.Count);
        Assert.Equal(2, (await h.SessionsAsync(outcome: "")).Sessions.Count);
    }

    [Fact]
    public async Task Sessions_OutcomeAndProject_Intersect()
    {
        var h = await NewAsync();
        var other = (await h.TwoMoreProjectsAsync()).B;
        var mine = await h.FileAsync();
        var theirs = await h.FileAsync(projectId: other.Id);

        await h.SpendAsync(mine, 1_000, Now.AddHours(-3), outcome: "dropped");
        await h.SpendAsync(mine, 2_000, Now.AddHours(-2), outcome: "worked");
        await h.SpendAsync(theirs, 4_000, Now.AddHours(-1), outcome: "dropped");

        var row = Assert.Single((await h.SessionsAsync(projectIds: [h.ProjectId], outcome: "dropped")).Sessions);

        Assert.Equal(1_000, row.TotalTokens);
    }

    [Fact]
    public async Task Sessions_AnUnknownOutcome_IsRefusedNamingTheAcceptedValues()
    {
        var h = await NewAsync();

        var sentence = await h.SessionRefusalAsync(outcome: "finished");

        Assert.Equal(
            "an outcome is dropped, worked, preempted, recused, unservable, abandoned, unstarted or none - not \"finished\"",
            sentence);
    }

    [Fact]
    public async Task ByPlaybook_WithoutAnOutcome_SplitsByOutcomeAndReason_AndTheRowsSumToTheWholeWindow()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.SpendAsync(issue, 1_000, Now.AddHours(-5), usd: 1m, playbookKey: "7", outcome: "dropped", reason: "quiet");
        await h.SpendAsync(issue, 1_000, Now.AddHours(-4), usd: 2m, playbookKey: "7", outcome: "dropped", reason: "run-time");
        await h.SpendAsync(issue, 1_000, Now.AddHours(-3), usd: 4m, playbookKey: "7", outcome: "worked");
        await h.SpendAsync(issue, 1_000, Now.AddHours(-2), usd: 8m, playbookKey: "7");

        var rows = (await h.ByPlaybookAsync()).Rows;

        Assert.Equal(4, rows.Count);
        Assert.Equal(4, rows.Sum(r => r.Sessions));
        Assert.Equal(15m, rows.Sum(r => r.CostUsd));
        Assert.Equal("quiet", Assert.Single(rows, r => r.Reason == "quiet").Reason);
        Assert.Single(rows, r => r.Outcome is null);
    }

    [Fact]
    public async Task ByPlaybook_NarrowsToOneOutcome_OneRowPerReason_AndNothingFromAnother()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.SpendAsync(issue, 1_000, Now.AddHours(-5), usd: 1m, playbookKey: "7", outcome: "dropped", reason: "quiet");
        await h.SpendAsync(issue, 2_000, Now.AddHours(-4), usd: 2m, playbookKey: "7", outcome: "dropped", reason: "quiet");
        await h.SpendAsync(issue, 4_000, Now.AddHours(-3), usd: 4m, playbookKey: "7", outcome: "dropped", reason: "run-time");
        await h.SpendAsync(issue, 8_000, Now.AddHours(-2), usd: 8m, playbookKey: "7", outcome: "worked");

        var rows = (await h.ByPlaybookAsync(outcome: "dropped")).Rows;

        Assert.Equal(["run-time", "quiet"], rows.Select(r => r.Reason));
        Assert.All(rows, r => Assert.Equal("dropped", r.Outcome));
        Assert.Equal(2, Assert.Single(rows, r => r.Reason == "quiet").Sessions);
        Assert.Equal(3m, Assert.Single(rows, r => r.Reason == "quiet").CostUsd);
    }

    [Fact]
    public async Task ByPlaybook_OutcomeNone_ReadsOnlyTheUnstampedRows()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.SpendAsync(issue, 1_000, Now.AddHours(-2), outcome: "worked");
        await h.SpendAsync(issue, 2_000, Now.AddHours(-1));

        var row = Assert.Single((await h.ByPlaybookAsync(outcome: "none")).Rows);

        Assert.Null(row.Outcome);
        Assert.Equal(2_000, row.TotalTokens);
    }

    [Fact]
    public async Task ByPlaybook_OutcomeAndProject_Intersect_AndTheOutcomeIsCaseInsensitive()
    {
        var h = await NewAsync();
        var other = (await h.TwoMoreProjectsAsync()).B;
        var mine = await h.FileAsync();
        var theirs = await h.FileAsync(projectId: other.Id);

        await h.SpendAsync(mine, 1_000, Now.AddHours(-3), playbookKey: "mine", outcome: "dropped");
        await h.SpendAsync(mine, 2_000, Now.AddHours(-2), playbookKey: "mine", outcome: "worked");
        await h.SpendAsync(theirs, 4_000, Now.AddHours(-1), playbookKey: "theirs", outcome: "dropped");

        var row = Assert.Single((await h.ByPlaybookAsync(projectId: [h.ProjectId], outcome: "Dropped")).Rows);

        Assert.Equal("mine", row.PlaybookKey);
        Assert.Equal(1_000, row.TotalTokens);
    }

    [Fact]
    public async Task ByPlaybook_AnUnknownOutcome_IsRefusedNamingTheAcceptedValues()
    {
        var h = await NewAsync();

        var sentence = await h.ByPlaybookRefusalAsync(outcome: "finished");

        Assert.Equal(
            "an outcome is dropped, worked, preempted, recused, unservable, abandoned, unstarted or none - not \"finished\"",
            sentence);
    }

    [Fact]
    public async Task ByPlaybook_NarrowsToOneProject_AndAnUnmatchedProjectAnswersNoRows()
    {
        var h = await NewAsync();
        var mine = await h.FileAsync();

        var other = new EfHatchProject { Key = "OTH", Name = "Other", CreatedAt = Now };
        h.Db.Add(other);
        await h.Db.SaveChangesAsync();
        var elsewhere = await h.FileAsync(projectId: other.Id);

        await h.SpendAsync(mine, 1_000, Now.AddHours(-2), playbookKey: "mine");
        await h.SpendAsync(elsewhere, 20_000, Now.AddHours(-1), playbookKey: "theirs");

        Assert.Equal(2, (await h.ByPlaybookAsync()).Rows.Count);

        var narrowed = await h.ByPlaybookAsync(projectId: [h.ProjectId]);
        Assert.Equal("mine", Assert.Single(narrowed.Rows).PlaybookKey);

        Assert.Equal("theirs", Assert.Single((await h.ByPlaybookAsync(projectId: [other.Id])).Rows).PlaybookKey);
        Assert.Empty((await h.ByPlaybookAsync(projectId: [999_999])).Rows);
    }

    [Fact]
    public async Task ByPlaybook_TwoProjectIds_AnswerEitherProjects()
    {
        var h = await NewAsync();
        var mine = await h.FileAsync();

        var other = new EfHatchProject { Key = "OTH", Name = "Other", CreatedAt = Now };
        h.Db.Add(other);
        await h.Db.SaveChangesAsync();
        var elsewhere = await h.FileAsync(projectId: other.Id);

        await h.SpendAsync(mine, 1_000, Now.AddHours(-2), playbookKey: "mine");
        await h.SpendAsync(elsewhere, 20_000, Now.AddHours(-1), playbookKey: "theirs");

        var both = await h.ByPlaybookAsync(projectId: [h.ProjectId, other.Id]);
        Assert.Equal(2, both.Rows.Count);
    }

    [Fact]
    public async Task ByPlaybook_ARepeatedProjectId_CountsOnce_SameAsOnce()
    {
        var h = await NewAsync();
        var mine = await h.FileAsync();
        await h.SpendAsync(mine, 1_000, Now.AddHours(-2), playbookKey: "mine");

        var repeated = await h.ByPlaybookAsync(projectId: [h.ProjectId, h.ProjectId]);
        Assert.Equal("mine", Assert.Single(repeated.Rows).PlaybookKey);
    }

    [Fact]
    public async Task ByPlaybook_AnUnknownProjectIdMixedWithAKnownOne_AnswersTheKnownOnesRatherThanRefusing()
    {
        var h = await NewAsync();
        var mine = await h.FileAsync();
        await h.SpendAsync(mine, 1_000, Now.AddHours(-2), playbookKey: "mine");

        var narrowed = await h.ByPlaybookAsync(projectId: [h.ProjectId, 999_999]);
        Assert.Equal("mine", Assert.Single(narrowed.Rows).PlaybookKey);
    }

    [Fact]
    public async Task ByPlaybook_NamesTheColumnsFromTheBoardNow_AndLeavesTheIdWhereAColumnIsGone()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        var todo = new EfHatchStatus { Name = "To Do", SortOrder = 20 };
        var doing = new EfHatchStatus { Name = "Doing", SortOrder = 30 };
        h.Db.AddRange(todo, doing);
        await h.Db.SaveChangesAsync();

        await h.SpendAsync(issue, 1_000, Now.AddHours(-1), playbookKey: "7", fromStatusId: todo.Id, toStatusId: doing.Id);

        var row = Assert.Single((await h.ByPlaybookAsync()).Rows);
        Assert.Equal("To Do", row.FromStatusName);
        Assert.Equal("Doing", row.ToStatusName);

        h.Db.Statuses.Remove(doing);
        await h.Db.SaveChangesAsync();

        row = Assert.Single((await h.ByPlaybookAsync()).Rows);
        Assert.Equal(doing.Id, row.ToStatusId);
        Assert.Null(row.ToStatusName);
        Assert.Equal("To Do", row.FromStatusName);
    }

    [Fact]
    public async Task ByPlaybook_NamesNoColumnForARowThatStoredNone()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.SpendAsync(issue, 1_000, Now.AddHours(-1));

        var row = Assert.Single((await h.ByPlaybookAsync()).Rows);

        Assert.Null(row.FromStatusId);
        Assert.Null(row.FromStatusName);
        Assert.Null(row.ToStatusId);
        Assert.Null(row.ToStatusName);
    }

    [Fact]
    public async Task TheByPlaybookTopReads_SumsAcrossSessions_RankedHeaviestFirst()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.SpendAsync(issue, 1_000, Now.AddHours(-2), playbookKey: "7",
            reads: [new WorkLogReadDto("a.cs", 3), new WorkLogReadDto("b.cs", 1)]);
        await h.SpendAsync(issue, 1_000, Now.AddHours(-1), playbookKey: "8",
            reads: [new WorkLogReadDto("a.cs", 2), new WorkLogReadDto("c.cs", 5)]);

        var byPlaybook = await h.ByPlaybookAsync();

        Assert.Equal(
            [("a.cs", 5), ("c.cs", 5), ("b.cs", 1)],
            byPlaybook.TopReads.Select(r => (r.Path, r.Count)));
    }

    [Fact]
    public async Task TheByPlaybookRead_RefusesABadBoundAndAnInvertedRangeInTheSameWords()
    {
        var h = await NewAsync();

        Assert.Equal(
            "a range bound is an instant (2026-09-12T17:00:00Z) - not \"last tuesday\"",
            await h.ByPlaybookRefusalAsync(from: "last tuesday"));
        Assert.Equal(
            "a range ends before it begins",
            await h.ByPlaybookRefusalAsync(from: "2026-09-07T03:00:00Z", to: "2026-09-06T03:00:00Z"));
    }

    [Fact]
    public async Task ByPlaybook_AnAncestorKey_ReadsThatIssueAndEverythingBeneathIt()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the effort");
        var story = await h.FileAsync("story", "under it", parentKey: epic.Key);
        var elsewhere = await h.FileAsync("epic", "another effort");

        await h.SpendAsync(epic, 1_000, Now.AddHours(-3), playbookKey: "epic-key");
        await h.SpendAsync(story, 2_000, Now.AddHours(-2), playbookKey: "story-key");
        await h.SpendAsync(elsewhere, 4_000, Now.AddHours(-1), playbookKey: "elsewhere-key");

        var scoped = await h.ByPlaybookAsync(ancestorKey: epic.Key);
        var whole = await h.ByPlaybookAsync();

        Assert.Equal(["epic-key", "story-key"], scoped.Rows.Select(r => r.PlaybookKey).OrderBy(k => k));
        Assert.Equal(3, whole.Rows.Count);
    }

    [Fact]
    public async Task ByPlaybook_AnAncestorKeyNamingNothing_IsRefusedRatherThanAnsweredAsEmpty()
    {
        var h = await NewAsync();

        Assert.Equal("there is no AER-999", await h.ByPlaybookRefusalAsync(ancestorKey: "AER-999"));
        Assert.Equal("there is no nonsense", await h.ByPlaybookRefusalAsync(ancestorKey: "nonsense"));
    }

    // ---- Who may read it ----

    [Fact]
    public void TheRoute_AcceptsAKey()
    {
        var guard = typeof(WorkLogController)
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .Single();

        Assert.True(guard.AcceptKey);
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 9, 7, 3, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required WorkLogController History { get; init; }

        public required IssuesController Issues { get; init; }

        public required HatchContext Db { get; init; }

        public required int ProjectId { get; init; }

        public async Task<IssueDto> FileAsync(
            string type = "task", string title = "a thing", string? parentKey = null, int? projectId = null) =>
            Created(await Issues.CreateIssue(
                new IssueCreateRequest(projectId ?? ProjectId, type, title, null, parentKey, null, null), default));

        /// <summary>Two projects beside the harness's own, for the tests that narrow to several.</summary>
        public async Task<(EfHatchProject B, EfHatchProject C)> TwoMoreProjectsAsync()
        {
            var b = new EfHatchProject { Key = "PRB", Name = "B", CreatedAt = Now };
            var c = new EfHatchProject { Key = "PRC", Name = "C", CreatedAt = Now };
            Db.AddRange(b, c);
            await Db.SaveChangesAsync();
            return (b, c);
        }

        /// <summary>
        /// A row straight into the context rather than through the writer, so
        /// the test chooses when the session ended.
        /// </summary>
        public async Task<long> SpendAsync(
            IssueDto issue,
            long tokens,
            DateTimeOffset endedAt,
            decimal? usd = 0m,
            bool isError = false,
            string? title = null,
            bool described = false,
            long durationMs = 1_000,
            int turns = 1,
            int? requests = null,
            long? peakContextTokens = null,
            string? agent = null,
            string? playbookKey = null,
            int? fromStatusId = null,
            int? toStatusId = null,
            string? issueType = null,
            string? model = null,
            string? effort = null,
            int? promptChars = null,
            IReadOnlyList<WorkLogReadDto>? reads = null,
            string? dispatchModel = null,
            string? modelPlacement = null,
            string? modelDigest = null,
            string? outcome = null,
            string? reason = null)
        {
            IssueKey.TryParse(issue.Key, out var projectKey, out var number);
            var issueId = await Db.Issues.AsNoTracking().WithKey(projectKey, number).Select(i => i.Id).FirstAsync();

            var entry = new EfHatchWorkLogEntry
            {
                IssueId = issueId,
                SessionId = $"session-{Db.WorkLog.Local.Count}-{issueId}-{tokens}",
                StartedAt = endedAt.AddMilliseconds(-durationMs),
                EndedAt = endedAt,
                DurationMs = durationMs,
                Title = title,
                // What the writer would have derived: a session that named
                // itself described itself.
                Described = described || title is not null,
                IsError = isError,
                Turns = turns,
                CostUsd = usd,
                InputTokens = tokens,
                Requests = requests,
                PeakContextTokens = peakContextTokens,
                Agent = agent,
                PromptChars = promptChars,
                PlaybookKey = playbookKey,
                PlaybookFromStatusId = fromStatusId,
                PlaybookToStatusId = toStatusId,
                IssueType = issueType,
                PlaybookModel = model,
                PlaybookEffort = effort,
                DispatchModel = dispatchModel,
                ModelPlacement = modelPlacement,
                ModelDigest = modelDigest,
                Outcome = outcome,
                Reason = reason,
                Reads = reads is null ? null : WorkLogRollup.WriteReads(reads),
                CreatedAt = Now,
            };

            Db.WorkLog.Add(entry);
            await Db.SaveChangesAsync();
            return entry.Id;
        }

        public async Task<WorkLogHistoryDto> HistoryAsync(
            string? from = null,
            string? to = null,
            string? bucket = null,
            int offsetMinutes = 0,
            string? ancestorKey = null,
            IReadOnlyList<int>? projectIds = null,
            string? outcome = null) =>
            Value(await History.GetHistory(
                from, to, bucket, offsetMinutes, ancestorKey, projectIds?.ToList(), outcome, default));

        public async Task<WorkLogSessionsDto> SessionsAsync(
            string? from = null,
            string? to = null,
            string? ancestorKey = null,
            string? sort = null,
            int limit = 100,
            IReadOnlyList<int>? projectIds = null,
            string? outcome = null,
            int? top = null) =>
            Value(await History.GetSessions(
                from, to, ancestorKey, sort, limit, projectIds?.ToList(), outcome, top, default));

        public async Task<WorkLogByPlaybookDto> ByPlaybookAsync(
            string? from = null,
            string? to = null,
            string? ancestorKey = null,
            List<int>? projectId = null,
            string? outcome = null) =>
            Value(await History.GetByPlaybook(from, to, ancestorKey, projectId, outcome, default));

        public async Task<string> ByPlaybookRefusalAsync(
            string? from = null, string? to = null, string? ancestorKey = null, string? outcome = null)
        {
            var result = await History.GetByPlaybook(from, to, ancestorKey, null, outcome, default);
            return Assert.IsType<BadRequestObjectResult>(result.Result).Value?.ToString() ?? "";
        }

        public async Task<string> SessionRefusalAsync(
            string? from = null,
            string? to = null,
            string? ancestorKey = null,
            string? sort = null,
            int limit = 100,
            IReadOnlyList<int>? projectIds = null,
            string? outcome = null,
            int? top = null)
        {
            var result = await History.GetSessions(
                from, to, ancestorKey, sort, limit, projectIds?.ToList(), outcome, top, default);
            return Assert.IsType<BadRequestObjectResult>(result.Result).Value?.ToString() ?? "";
        }

        public async Task<string> RefusalAsync(
            string? from = null,
            string? to = null,
            string? bucket = null,
            int offsetMinutes = 0,
            string? ancestorKey = null,
            IReadOnlyList<int>? projectIds = null,
            string? outcome = null)
        {
            var result = await History.GetHistory(
                from, to, bucket, offsetMinutes, ancestorKey, projectIds?.ToList(), outcome, default);
            return Assert.IsType<BadRequestObjectResult>(result.Result).Value?.ToString() ?? "";
        }
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var hatch = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Add(hatch);
        db.Add(new EfHatchStatus { Name = "inbox", SortOrder = 10 });
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);

        var caller = new StubCallerIdentity
        {
            Key = new EfApiKey
            {
                Name = "hatch",
                Hash = [1],
                Prefix = "hatch_ak_x",
                CreatedAt = Now,
            },
        };

        return new Harness
        {
            History = new WorkLogController(db, time),
            Issues = new IssuesController(db, new RankService(db), new StubActorDirectory(), TestClaims.With(), caller, time, new ProjectAccess(db, new StubActorDirectory())),
            Db = db,
            ProjectId = hatch.Id,
        };
    }

    /// <summary>Whoever the test says is holding the phone - see IssueWorkLogControllerTests.</summary>
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

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {Reason(result.Result)}");

    private static T Created<T>(ActionResult<T> result) =>
        result.Result is CreatedAtActionResult created
            ? (T)created.Value!
            : result.Value ?? throw new InvalidOperationException($"expected a created value, got {Reason(result.Result)}");

    private static string Reason(IActionResult? result) => result switch
    {
        ObjectResult o => o.Value?.ToString() ?? $"{o.StatusCode}",
        StatusCodeResult s => s.StatusCode.ToString(),
        null => "no result",
        _ => result.GetType().Name,
    };
}

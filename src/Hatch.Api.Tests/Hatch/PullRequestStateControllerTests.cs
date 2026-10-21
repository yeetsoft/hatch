using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What a runner may write about the forge's view of a recorded pull request,
/// and the rule it is kept by: a change is an event, and a repeat is not news.
/// </summary>
public class PullRequestStateControllerTests
{
    private const string Url = "https://forge.example/owner/repo/pull/7";
    private const string Sha = "1111111111111111111111111111111111111111";
    private const string OtherSha = "2222222222222222222222222222222222222222";

    // ---- The first read ----

    [Fact]
    public async Task AFirstWrite_KeepsTheThreeAndWritesOneEvent()
    {
        var h = await NewAsync();
        var issue = await h.FileWithPullRequestAsync();

        Assert.IsType<NoContentResult>(await h.PutAsync(issue, Request()));

        var row = h.Db.Issues.Single();
        Assert.Equal(MergeStates.Clean, row.PullRequestMergeState);
        Assert.Equal(ReviewDecisions.Approved, row.PullRequestReviewDecision);
        Assert.Equal(Sha, row.PullRequestHeadSha);
        Assert.Equal(Now, row.PullRequestReadAt);
        Assert.Equal(Now, row.PullRequestHeadSince);

        var e = Assert.Single(await h.EventsAsync(issue.Key));
        Assert.Equal("Nathan", e.Actor);
        var payload = e.Payload!.Value;
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("from").ValueKind);
        var to = payload.GetProperty("to");
        Assert.Equal(MergeStates.Clean, to.GetProperty("mergeState").GetString());
        Assert.Equal(ReviewDecisions.Approved, to.GetProperty("reviewDecision").GetString());
        Assert.Equal(Sha, to.GetProperty("headSha").GetString());
    }

    // ---- A repeat ----

    [Fact]
    public async Task ARepeat_WritesNoEventAndLeavesUpdatedAtAlone_ButRefreshesTheReadTime()
    {
        var h = await NewAsync();
        var issue = await h.FileWithPullRequestAsync();
        await h.PutAsync(issue, Request());
        var updated = h.Db.Issues.Single().UpdatedAt;

        h.Time.Advance(TimeSpan.FromMinutes(5));
        Assert.IsType<NoContentResult>(await h.PutAsync(issue, Request()));

        var row = h.Db.Issues.Single();
        Assert.Single(await h.EventsAsync(issue.Key));
        Assert.Equal(updated, row.UpdatedAt);
        Assert.Equal(Now.AddMinutes(5), row.PullRequestReadAt);
    }

    // ---- When the head arrived ----

    [Fact]
    public async Task AChangedMergeState_LeavesTheHeadTimeAlone()
    {
        var h = await NewAsync();
        var issue = await h.FileWithPullRequestAsync();
        await h.PutAsync(issue, Request());

        h.Time.Advance(TimeSpan.FromMinutes(5));
        await h.PutAsync(issue, Request() with { MergeState = MergeStates.Behind });

        var row = h.Db.Issues.Single();
        Assert.Equal(Now, row.PullRequestHeadSince);
        Assert.Equal(Now.AddMinutes(5), row.PullRequestReadAt);
    }

    [Fact]
    public async Task AChangedHeadSha_MovesTheHeadTimeToThatRead()
    {
        var h = await NewAsync();
        var issue = await h.FileWithPullRequestAsync();
        await h.PutAsync(issue, Request());

        h.Time.Advance(TimeSpan.FromMinutes(5));
        await h.PutAsync(issue, Request() with { HeadSha = OtherSha });

        var row = h.Db.Issues.Single();
        Assert.Equal(OtherSha, row.PullRequestHeadSha);
        Assert.Equal(Now.AddMinutes(5), row.PullRequestHeadSince);
    }

    // ---- Each of the three, alone ----

    [Fact]
    public async Task AChangedMergeState_WritesOneEvent()
    {
        var h = await NewAsync();
        var issue = await h.FileWithPullRequestAsync();
        await h.PutAsync(issue, Request());

        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.PutAsync(issue, Request() with { MergeState = MergeStates.Behind });

        var payload = (await h.EventsAsync(issue.Key)).Last().Payload!.Value;
        AssertSide(payload.GetProperty("from"), MergeStates.Clean, ReviewDecisions.Approved, Sha);
        AssertSide(payload.GetProperty("to"), MergeStates.Behind, ReviewDecisions.Approved, Sha);
        Assert.Equal(2, (await h.EventsAsync(issue.Key)).Count);
        Assert.Equal(Now.AddMinutes(1), h.Db.Issues.Single().UpdatedAt);
    }

    [Fact]
    public async Task AChangedReviewDecision_WritesOneEvent()
    {
        var h = await NewAsync();
        var issue = await h.FileWithPullRequestAsync();
        await h.PutAsync(issue, Request());

        await h.PutAsync(issue, Request() with { ReviewDecision = ReviewDecisions.ChangesRequested });

        var events = await h.EventsAsync(issue.Key);
        Assert.Equal(2, events.Count);
        var payload = events.Last().Payload!.Value;
        AssertSide(payload.GetProperty("from"), MergeStates.Clean, ReviewDecisions.Approved, Sha);
        AssertSide(payload.GetProperty("to"), MergeStates.Clean, ReviewDecisions.ChangesRequested, Sha);
    }

    [Fact]
    public async Task AChangedHeadSha_WritesOneEvent()
    {
        var h = await NewAsync();
        var issue = await h.FileWithPullRequestAsync();
        await h.PutAsync(issue, Request());

        await h.PutAsync(issue, Request() with { HeadSha = OtherSha });

        var events = await h.EventsAsync(issue.Key);
        Assert.Equal(2, events.Count);
        var payload = events.Last().Payload!.Value;
        AssertSide(payload.GetProperty("from"), MergeStates.Clean, ReviewDecisions.Approved, Sha);
        AssertSide(payload.GetProperty("to"), MergeStates.Clean, ReviewDecisions.Approved, OtherSha);
    }

    // ---- What is accepted ----

    [Fact]
    public async Task SurroundingWhitespace_IsTrimmedAndAccepted()
    {
        var h = await NewAsync();
        var issue = await h.FileWithPullRequestAsync();

        var padded = new PullRequestStateRequest(
            $"  {Url}\n", $" {Sha} ", $" {MergeStates.Clean} ", $"\t{ReviewDecisions.Approved}", "box:/work/repo");
        Assert.IsType<NoContentResult>(await h.PutAsync(issue, padded));

        var row = h.Db.Issues.Single();
        Assert.Equal(MergeStates.Clean, row.PullRequestMergeState);
        Assert.Equal(ReviewDecisions.Approved, row.PullRequestReviewDecision);
        Assert.Equal(Sha, row.PullRequestHeadSha);
    }

    [Fact]
    public async Task TheColumnIsNotChecked()
    {
        var h = await NewAsync();
        var issue = await h.FileWithPullRequestAsync();

        // Filed into the first column and never moved: a state read before
        // anybody moved the ticket is still true.
        var first = await h.Db.Statuses.OrderBy(s => s.SortOrder).FirstAsync();
        Assert.Equal(first.Id, issue.StatusId);
        Assert.IsType<NoContentResult>(await h.PutAsync(issue, Request()));
    }

    // ---- Refusals ----
    //
    // Each begins with one accepted write, so the columns are non-null and there
    // is an event to lose, and each is put through RefusedAsync, which proves the
    // row and its events are as they were.

    [Fact]
    public async Task NoRecordedUrl_IsRefused()
    {
        var h = await NewAsync();
        var issue = await h.FileWithPullRequestAsync();
        await h.PutAsync(issue, Request());

        // A url recorded and later cleared: the columns stay, so the snapshot means something.
        h.Db.Issues.Single().PullRequestUrl = null;
        await h.Db.SaveChangesAsync();

        var result = await h.RefusedAsync(issue, Request());

        Assert.Equal(
            "no pull request is recorded on this issue, so there is no state to keep",
            Assert.IsType<ConflictObjectResult>(result).Value);
    }

    [Fact]
    public async Task ADifferentUrl_IsRefused()
    {
        var h = await NewAsync();
        var issue = await h.FileWithPullRequestAsync();
        await h.PutAsync(issue, Request());

        var other = Url.Replace("/pull/7", "/pull/8");
        var result = await h.RefusedAsync(issue, Request() with { Url = other });

        Assert.Equal(
            $"\"{other}\" is not the pull request recorded on this issue",
            Assert.IsType<ConflictObjectResult>(result).Value);
    }

    [Theory]
    [InlineData("upper")]
    [InlineData("files")]
    public async Task TheRecordedUrlSpelledDifferently_IsRefused(string spelling)
    {
        var h = await NewAsync();
        var issue = await h.FileWithPullRequestAsync();
        await h.PutAsync(issue, Request());

        // The comparison is ordinal: a different case, or a page of the same pull request, is another url.
        var spelled = spelling == "upper" ? Url.ToUpperInvariant() : Url + "/files";
        var result = await h.RefusedAsync(issue, Request() with { Url = spelled });

        Assert.Equal(
            $"\"{spelled}\" is not the pull request recorded on this issue",
            Assert.IsType<ConflictObjectResult>(result).Value);
    }

    [Theory]
    [InlineData("mergeable")]
    [InlineData("CLEAN")]
    [InlineData("")]
    public async Task AnUnknownMergeState_IsRefused(string mergeState)
    {
        var h = await NewAsync();
        var issue = await h.FileWithPullRequestAsync();
        await h.PutAsync(issue, Request());

        var result = await h.RefusedAsync(issue, Request() with { MergeState = mergeState });

        Assert.Equal(
            $"a merge state is one of {string.Join(", ", MergeStates.All)}",
            Assert.IsType<BadRequestObjectResult>(result).Value);
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("APPROVED")]
    [InlineData("")]
    public async Task AnUnknownReviewDecision_IsRefused(string reviewDecision)
    {
        var h = await NewAsync();
        var issue = await h.FileWithPullRequestAsync();
        await h.PutAsync(issue, Request());

        var result = await h.RefusedAsync(issue, Request() with { ReviewDecision = reviewDecision });

        Assert.Equal(
            $"a review decision is one of {string.Join(", ", ReviewDecisions.All)}",
            Assert.IsType<BadRequestObjectResult>(result).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnEmptyHeadSha_IsRefused(string headSha)
    {
        var h = await NewAsync();
        var issue = await h.FileWithPullRequestAsync();
        await h.PutAsync(issue, Request());

        var result = await h.RefusedAsync(issue, Request() with { HeadSha = headSha });

        Assert.Equal(
            "a state names the sha the pull request's head stood at",
            Assert.IsType<BadRequestObjectResult>(result).Value);
    }

    [Fact]
    public async Task AnOverLongHeadSha_IsRefused()
    {
        var h = await NewAsync();
        var issue = await h.FileWithPullRequestAsync();
        await h.PutAsync(issue, Request());

        // The in-memory provider does not enforce [MaxLength], so this exercises
        // the controller's own check rather than the column's.
        var result = await h.RefusedAsync(
            issue, Request() with { HeadSha = new string('a', EfHatchMergeCheck.MaxShaLength + 1) });

        Assert.Equal(
            $"a sha is at most {EfHatchMergeCheck.MaxShaLength} characters",
            Assert.IsType<BadRequestObjectResult>(result).Value);
    }

    [Theory]
    [InlineData("AER-999")]
    [InlineData("nonsense")]
    [InlineData("AER-")]
    [InlineData("AER-0")]
    public async Task AnUnknownOrMalformedKey_IsNotFound(string key)
    {
        var h = await NewAsync();
        var issue = await h.FileWithPullRequestAsync();
        await h.PutAsync(issue, Request());
        var before = await h.SnapshotAsync(issue.Key);

        Assert.IsType<NotFoundResult>(await h.State.PutPullRequestState(key, Request(), default));

        Assert.Equal(before, await h.SnapshotAsync(issue.Key));
    }

    // ---- The gate ----

    [Fact]
    public void WritingOne_IsOpenToAHatchScopedKey()
    {
        var guard = typeof(PullRequestStateController)
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .Single();

        // The caller is a runner, and a state only a person could enter would
        // be one nobody ever entered.
        Assert.Equal(ApiKeyScopes.Hatch, guard.AcceptScope);
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    private static PullRequestStateRequest Request() =>
        new(Url, Sha, MergeStates.Clean, ReviewDecisions.Approved, "box:/work/repo");

    private static void AssertSide(JsonElement side, string mergeState, string reviewDecision, string headSha)
    {
        Assert.Equal(mergeState, side.GetProperty("mergeState").GetString());
        Assert.Equal(reviewDecision, side.GetProperty("reviewDecision").GetString());
        Assert.Equal(headSha, side.GetProperty("headSha").GetString());
    }

    private sealed record Snapshot(
        string? MergeState, string? ReviewDecision, string? HeadSha,
        DateTimeOffset? HeadSince, DateTimeOffset? ReadAt, DateTimeOffset UpdatedAt, int Events);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required PullRequestStateController State { get; init; }
        public required IssuesController Issues { get; init; }
        public required IssueThreadController Thread { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }

        /// <summary>An issue with no pull request recorded on it.</summary>
        public async Task<IssueDto> FileAsync()
        {
            var created = await Issues.CreateIssue(new IssueCreateRequest(ProjectId, "task", "a thing", null, null, null, null), default);
            return created.Result is CreatedAtActionResult c
                ? (IssueDto)c.Value!
                : created.Value ?? throw new InvalidOperationException("expected a created issue");
        }

        /// <summary>An issue with <see cref="Url"/> written straight onto its row - what records one is another route's business.</summary>
        public async Task<IssueDto> FileWithPullRequestAsync()
        {
            var issue = await FileAsync();

            var row = await Db.Issues.SingleAsync();
            row.PullRequestUrl = Url;
            await Db.SaveChangesAsync();
            return issue;
        }

        public Task<IActionResult> PutAsync(IssueDto issue, PullRequestStateRequest request) =>
            State.PutPullRequestState(issue.Key, request, default);

        /// <summary>The four columns a state is kept in, the time the issue last changed, and how many state events it has.</summary>
        public async Task<Snapshot> SnapshotAsync(string key)
        {
            var row = await Db.Issues.SingleAsync();
            var events = await EventsAsync(key);
            return new Snapshot(
                row.PullRequestMergeState, row.PullRequestReviewDecision, row.PullRequestHeadSha,
                row.PullRequestHeadSince, row.PullRequestReadAt, row.UpdatedAt, events.Count);
        }

        /// <summary>Puts a request that must be refused, and proves the refusal left the row and its events as they were.</summary>
        public async Task<IActionResult> RefusedAsync(IssueDto issue, PullRequestStateRequest request)
        {
            var before = await SnapshotAsync(issue.Key);
            var result = await PutAsync(issue, request);
            Assert.Equal(before, await SnapshotAsync(issue.Key));
            return result;
        }

        /// <summary>The state's own events, oldest first.</summary>
        public async Task<IReadOnlyList<IssueEventDto>> EventsAsync(string key)
        {
            var events = await Thread.GetEvents(key, default);
            return (events.Value ?? throw new InvalidOperationException("expected events"))
                .Where(e => e.Kind == EfHatchIssueEvent.PullRequestStateChanged)
                .Reverse()
                .ToList();
        }
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var hatch = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Add(hatch);
        db.Add(new EfHatchStatus { Name = "inbox", SortOrder = 10 });
        db.Add(new EfHatchStatus { Name = "done", SortOrder = 20, IsTerminal = true });
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var caller = new MergeCheckControllerTests.StubCallerIdentity
        {
            Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now },
        };
        var actors = new StubActorDirectory();

        return new Harness
        {
            Db = db,
            State = new PullRequestStateController(db, caller, time),
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time, new ProjectAccess(db, actors)),
            Thread = new IssueThreadController(db, caller, time),
            Time = time,
            ProjectId = hatch.Id,
        };
    }
}

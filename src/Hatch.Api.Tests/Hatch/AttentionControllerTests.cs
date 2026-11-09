using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Models.Auth;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What the nav strip is told is waiting on a person.
///
/// The rules pinned here are the ones a widget cannot recover from getting
/// wrong. A control that lights up for a ticket nobody can act on is one nobody
/// reads after a week, so an issue in review with no pull request is counted and
/// never listed. And the review column is measured off the board's shape rather
/// than off its name, so renaming or reordering columns has to change nothing -
/// which is the whole reason <see cref="Columns"/> exists as a shared static.
/// </summary>
public class AttentionControllerTests : IDisposable
{
    // The folder the backup tests point the service at; the other tests leave
    // the path empty, so the row is null for them and nothing is read.
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hatch-attention-backup-" + Guid.NewGuid().ToString("N"));

    public AttentionControllerTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    // ---- The pull request half ----

    [Fact]
    public async Task Attention_ListsTheReviewColumnsIssuesThatHaveAPullRequest()
    {
        var h = await NewAsync();
        var up = await h.FileAsync("story", "delivered", h.Review, pullRequestUrl: "https://forge.example/pulls/1");
        await h.FileAsync("story", "still being written", h.InProgress, pullRequestUrl: "https://forge.example/pulls/2");

        var row = Assert.Single(Value(await h.Attention.GetAttention(default)).Reviews);

        Assert.Equal(Key(up), row.Key);
        Assert.Equal("delivered", row.Title);
        Assert.Equal("story", row.Type);
        Assert.Equal("https://forge.example/pulls/1", row.PullRequestUrl);
    }

    [Fact]
    public async Task Attention_KeepsTheColumnsOwnOrder()
    {
        var h = await NewAsync();
        var second = await h.FileAsync("story", "below", h.Review, rank: 2048, pullRequestUrl: "https://forge.example/pulls/2");
        var first = await h.FileAsync("story", "top", h.Review, rank: 1024, pullRequestUrl: "https://forge.example/pulls/1");

        // The board's own (Rank, Id), so a row sits where the eye already found
        // it - not in the order the issues were filed.
        Assert.Equal(
            [Key(first), Key(second)],
            Value(await h.Attention.GetAttention(default)).Reviews.Select(r => r.Key));
    }

    [Fact]
    public async Task Attention_CountsAnIssueInReviewWithNoPullRequestRatherThanListingIt()
    {
        var h = await NewAsync();
        await h.FileAsync("story", "a phase story nobody will open a branch for", h.Review);
        await h.FileAsync("story", "another", h.Review);
        await h.FileAsync("story", "delivered", h.Review, pullRequestUrl: "https://forge.example/pulls/1");

        var attention = Value(await h.Attention.GetAttention(default));

        // Never a row, so it never makes the control loud - and never silently
        // dropped either, so a ticket whose agent forgot `hatch pr` is visible.
        Assert.Single(attention.Reviews);
        Assert.Equal(2, attention.InReviewWithoutPullRequest);
    }

    [Fact]
    public async Task Attention_TreatsABlankPullRequestUrlAsNoneAtAll()
    {
        var h = await NewAsync();
        await h.FileAsync("story", "cleared, badly", h.Review, pullRequestUrl: "   ");

        var attention = Value(await h.Attention.GetAttention(default));

        Assert.Empty(attention.Reviews);
        Assert.Equal(1, attention.InReviewWithoutPullRequest);
    }

    [Fact]
    public async Task Attention_SaysNothingIsInReviewWhenNothingIs()
    {
        var h = await NewAsync();
        await h.FileAsync("story", "underway", h.InProgress, pullRequestUrl: "https://forge.example/pulls/1");

        var attention = Value(await h.Attention.GetAttention(default));

        // The two emptinesses the panel tells apart: none at all, versus some
        // with no link. This is the first, and the count is what says so.
        Assert.Empty(attention.Reviews);
        Assert.Equal(0, attention.InReviewWithoutPullRequest);
    }

    [Fact]
    public async Task Attention_LeavesOutACloseHopRowFromInReviewWithoutPullRequest()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "ready to close itself", h.Review);
        issue.PreApproval = PreApprovalLevels.PreApproved;
        await h.Db.SaveChangesAsync();
        await h.FileAsync("story", "a done child", h.Done, parentId: issue.Id);

        var attention = Value(await h.Attention.GetAttention(default));

        Assert.Empty(attention.Reviews);
        Assert.Equal(0, attention.InReviewWithoutPullRequest);
    }

    [Fact]
    public async Task Attention_LeavesOutATaskWaitingOnItsStorysPullRequest_FromInReviewWithoutPullRequest()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "shares one pull request", h.InProgress);
        story.OnePullRequest = true;
        await h.Db.SaveChangesAsync();
        await h.FileAsync("task", "pushed to the story's branch", h.Review, parentId: story.Id);

        var attention = Value(await h.Attention.GetAttention(default));

        Assert.Equal(0, attention.InReviewWithoutPullRequest);
    }

    [Fact]
    public async Task Attention_StillCountsATaskInReviewWithNoPullRequest_WhereTheStoryDoesNotShareOne()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "does not share", h.InProgress);
        await h.FileAsync("task", "has nowhere to be reviewed", h.Review, parentId: story.Id);

        var attention = Value(await h.Attention.GetAttention(default));

        Assert.Equal(1, attention.InReviewWithoutPullRequest);
    }

    [Fact]
    public async Task Attention_ListsADeclaredNoCodeRowInNoCodeHeld_NotInReviewsOrTheWithoutPullRequestCount()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "needed no change", h.Review);
        issue.NoCode = "the setting was already right";
        issue.NoCodeAt = Now;
        await h.Db.SaveChangesAsync();

        var attention = Value(await h.Attention.GetAttention(default));

        var row = Assert.Single(attention.NoCodeHeld!);
        Assert.Equal(Key(issue), row.Key);
        Assert.Equal("needed no change", row.Title);
        Assert.Equal("task", row.Type);
        Assert.Equal("the setting was already right", row.Reason);
        Assert.Empty(attention.Reviews);
        Assert.Equal(0, attention.InReviewWithoutPullRequest);
    }

    [Fact]
    public async Task Attention_LeavesAPreApprovedDeclaredNoCodeRowOutOfNoCodeHeld_ItIsTheCloseHops()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "needed no change", h.Review);
        issue.PreApproval = PreApprovalLevels.PreApproved;
        issue.NoCode = "the setting was already right";
        issue.NoCodeAt = Now;
        await h.Db.SaveChangesAsync();

        var attention = Value(await h.Attention.GetAttention(default));

        Assert.Empty(attention.NoCodeHeld!);
        Assert.Equal(0, attention.InReviewWithoutPullRequest);
    }

    [Fact]
    public async Task Attention_StillCountsAnUndeclaredRowWithNoPullRequest_AndListsNoNoCodeHeld()
    {
        var h = await NewAsync();
        await h.FileAsync("task", "no declaration", h.Review);

        var attention = Value(await h.Attention.GetAttention(default));

        Assert.Empty(attention.NoCodeHeld!);
        Assert.Equal(1, attention.InReviewWithoutPullRequest);
    }

    [Fact]
    public async Task Attention_CountsAChildlessPreApprovedRowWithNoPullRequest()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "ready to close itself, but nothing is filed under it", h.Review);
        issue.PreApproval = PreApprovalLevels.PreApproved;
        await h.Db.SaveChangesAsync();

        var attention = Value(await h.Attention.GetAttention(default));

        Assert.Equal(1, attention.InReviewWithoutPullRequest);
    }

    [Fact]
    public async Task Attention_CountsAPreApprovedRowWithAnOpenChild()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "not ready, its child is still open", h.Review);
        issue.PreApproval = PreApprovalLevels.PreApproved;
        await h.Db.SaveChangesAsync();
        await h.FileAsync("story", "an open child", h.Todo, parentId: issue.Id);

        var attention = Value(await h.Attention.GetAttention(default));

        Assert.Equal(1, attention.InReviewWithoutPullRequest);
    }

    [Fact]
    public async Task Attention_CountsAnExpressOnlyRowWithADoneChildAndNoPullRequest()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "express is not enough to close itself", h.Review);
        issue.PreApproval = PreApprovalLevels.Express;
        await h.Db.SaveChangesAsync();
        await h.FileAsync("story", "a done child", h.Done, parentId: issue.Id);

        var attention = Value(await h.Attention.GetAttention(default));

        Assert.Equal(1, attention.InReviewWithoutPullRequest);
    }

    [Fact]
    public async Task Attention_LeavesOutAPreApprovedEpicRowWhoseOnlyChildIsDeferred()
    {
        var h = await NewAsync();
        var shelf = await h.ShelfAsync(37);
        var issue = await h.FileAsync("epic", "an epic whose only child is shelved", h.Review);
        issue.PreApproval = PreApprovalLevels.PreApproved;
        await h.Db.SaveChangesAsync();
        await h.FileAsync("story", "a deferred child", shelf, parentId: issue.Id);

        var attention = Value(await h.Attention.GetAttention(default));

        // FamilyGate.EpicFold excludes a deferred child entirely, so this is
        // still a close hop.
        Assert.Equal(0, attention.InReviewWithoutPullRequest);
    }

    [Fact]
    public async Task Attention_LeavesOutAPreApprovedReflectionRowWhoseOnlyChildIsDeferred_TheSameAsAnEpic()
    {
        var h = await NewAsync();
        var shelf = await h.ShelfAsync(37);
        var issue = await h.FileAsync("reflection", "a reflection whose only child is shelved", h.Review);
        issue.PreApproval = PreApprovalLevels.PreApproved;
        await h.Db.SaveChangesAsync();
        await h.FileAsync("story", "a deferred child", shelf, parentId: issue.Id);

        var attention = Value(await h.Attention.GetAttention(default));

        Assert.Equal(0, attention.InReviewWithoutPullRequest);
    }

    [Fact]
    public async Task Attention_CountsAPreApprovedStoryRowWhoseOnlyChildIsDeferred()
    {
        var h = await NewAsync();
        var shelf = await h.ShelfAsync(37);
        var issue = await h.FileAsync("story", "a story whose only child is shelved", h.Review);
        issue.PreApproval = PreApprovalLevels.PreApproved;
        await h.Db.SaveChangesAsync();
        await h.FileAsync("story", "a deferred child", shelf, parentId: issue.Id);

        var attention = Value(await h.Attention.GetAttention(default));

        // FamilyGate.OpenChildren counts a deferred child as open, so this is
        // not a close hop - the one place the epic and non-epic rules disagree.
        Assert.Equal(1, attention.InReviewWithoutPullRequest);
    }

    // ---- The conflict half ----

    [Fact]
    public async Task Attention_ListsTheReviewColumnsIssuesWhoseBranchConflicts()
    {
        var h = await NewAsync();
        var conflicted = await h.FileAsync("story", "stopped merging", h.Review, pullRequestUrl: "https://forge.example/pulls/1");
        var clean = await h.FileAsync("story", "merges fine", h.Review, pullRequestUrl: "https://forge.example/pulls/2");
        var unchecked_ = await h.FileAsync("story", "nobody has looked", h.Review);
        await h.CheckAsync(conflicted, MergeVerdicts.Conflicted, ["a.cs", "b.cs"]);
        await h.CheckAsync(clean, MergeVerdicts.Clean);

        var row = Assert.Single(Value(await h.Attention.GetAttention(default)).Conflicts);

        Assert.Equal(Key(conflicted), row.Key);
        Assert.Equal("stopped merging", row.Title);
        Assert.Equal("story", row.Type);
        Assert.Equal("https://forge.example/pulls/1", row.PullRequestUrl);
        Assert.Equal(["a.cs", "b.cs"], Assert.Single(row.Checks).Files);
        Assert.NotEqual(Key(unchecked_), row.Key);
    }

    [Theory]
    [InlineData(MergeVerdicts.Clean)]
    [InlineData(MergeVerdicts.None)]
    [InlineData(MergeVerdicts.Ambiguous)]
    public async Task Attention_SaysNothingAboutAVerdictThatIsNotConflicted(string verdict)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "delivered", h.Review, pullRequestUrl: "https://forge.example/pulls/1");
        await h.CheckAsync(issue, verdict);

        Assert.Empty(Value(await h.Attention.GetAttention(default)).Conflicts);
    }

    [Fact]
    public async Task Attention_LeavesOutAConflictOnAnIssueThatIsNotInReview()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "still being written", h.InProgress);
        await h.CheckAsync(issue, MergeVerdicts.Conflicted, ["a.cs"]);

        Assert.Empty(Value(await h.Attention.GetAttention(default)).Conflicts);
    }

    [Fact]
    public async Task Attention_ListsAConflictWithNoPullRequest_AndStillCountsItAsWithoutOne()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "a branch and no link", h.Review);
        await h.CheckAsync(issue, MergeVerdicts.Conflicted, ["a.cs"]);

        var attention = Value(await h.Attention.GetAttention(default));

        // The two halves are separate questions: whether there is somewhere to
        // review it, and whether the branch still merges.
        Assert.Equal(Key(issue), Assert.Single(attention.Conflicts).Key);
        Assert.Empty(attention.Reviews);
        Assert.Equal(1, attention.InReviewWithoutPullRequest);
    }

    [Fact]
    public async Task Attention_ListsConflictsInTheColumnsOwnOrder()
    {
        var h = await NewAsync();
        var second = await h.FileAsync("story", "below", h.Review, rank: 2048);
        var first = await h.FileAsync("story", "top", h.Review, rank: 1024);
        await h.CheckAsync(second, MergeVerdicts.Conflicted, ["a.cs"]);
        await h.CheckAsync(first, MergeVerdicts.Conflicted, ["a.cs"]);

        Assert.Equal(
            [Key(first), Key(second)],
            Value(await h.Attention.GetAttention(default)).Conflicts.Select(c => c.Key));
    }

    [Fact]
    public async Task Attention_ListsAnIssueOnceAndNamesOnlyTheRepositoriesThatConflict()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "two repositories", h.Review);
        await h.CheckAsync(issue, MergeVerdicts.Clean, remote: "forge.example/owner/one");
        await h.CheckAsync(issue, MergeVerdicts.Conflicted, ["a.cs"], remote: "forge.example/owner/two");
        await h.CheckAsync(issue, MergeVerdicts.Conflicted, ["b.cs"], remote: "forge.example/owner/three");

        var row = Assert.Single(Value(await h.Attention.GetAttention(default)).Conflicts);

        Assert.Equal(["forge.example/owner/three", "forge.example/owner/two"], row.Checks.Select(c => c.Canonical));
    }

    [Fact]
    public async Task Attention_FindsConflictsInTheReviewColumnAfterItIsRenamedAndTheBoardReordered()
    {
        var h = await NewAsync();
        var moved = await h.FileAsync("story", "in the column that is now last before done", h.InProgress);
        var was = await h.FileAsync("story", "in the one that used to be", h.Review);
        await h.CheckAsync(moved, MergeVerdicts.Conflicted, ["a.cs"]);
        await h.CheckAsync(was, MergeVerdicts.Conflicted, ["a.cs"]);

        await h.RenameAsync(h.InProgress, "Waiting on Nathan");
        await h.ReorderAsync(h.InProgress, 36);

        // Measured, not named: whichever column is immediately left of the
        // first terminal one.
        Assert.Equal(Key(moved), Assert.Single(Value(await h.Attention.GetAttention(default)).Conflicts).Key);
    }

    [Fact]
    public async Task Attention_AnswersNoConflictsOnABoardWithNoRoomForAReviewColumn()
    {
        var h = await OneColumnAsync();

        Assert.Empty(Value(await h.Attention.GetAttention(default)).Conflicts);
    }

    // ---- The failing build half ----

    [Fact]
    public async Task Attention_ListsTheReviewColumnsIssuesWhoseBuildFailed_WithOnlyTheFailedVerdicts()
    {
        var h = await NewAsync();
        var failed = await h.FileAsync("story", "red", h.Review, pullRequestUrl: "https://forge.example/pulls/1");
        await h.BuildAsync(failed, BuildVerdicts.Failed, ["api", "CI"], remote: "forge.example/owner/one");
        await h.BuildAsync(failed, BuildVerdicts.Passed, remote: "forge.example/owner/two");

        var row = Assert.Single(Value(await h.Attention.GetAttention(default)).FailingBuilds!);

        Assert.Equal(Key(failed), row.Key);
        Assert.Equal("red", row.Title);
        Assert.Equal("https://forge.example/pulls/1", row.PullRequestUrl);
        Assert.Equal(["forge.example/owner/one"], row.Checks.Select(c => c.Canonical));
        Assert.Equal(["api", "CI"], row.Checks.Single().Failing.Select(f => f.Name));
    }

    [Theory]
    [InlineData(BuildVerdicts.Passed)]
    [InlineData(BuildVerdicts.Pending)]
    [InlineData(BuildVerdicts.None)]
    public async Task Attention_SaysNothingAboutABuildThatDidNotFail(string verdict)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "delivered", h.Review, pullRequestUrl: "https://forge.example/pulls/1");
        await h.BuildAsync(issue, verdict);

        Assert.Empty(Value(await h.Attention.GetAttention(default)).FailingBuilds!);
    }

    [Fact]
    public async Task Attention_LeavesOutAFailingBuildOnAnIssueThatIsNotInReview()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "still being written", h.InProgress);
        await h.BuildAsync(issue, BuildVerdicts.Failed, ["api"]);

        Assert.Empty(Value(await h.Attention.GetAttention(default)).FailingBuilds!);
    }

    // ---- The held-back half ----

    [Fact]
    public async Task Attention_HoldsBackAConflictedIssueFromReviews_AndCountsIt()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "stopped merging", h.Review, pullRequestUrl: "https://forge.example/pulls/1");
        await h.CheckAsync(issue, MergeVerdicts.Conflicted, ["a.cs"]);

        var attention = Value(await h.Attention.GetAttention(default));

        // Held back, not listed: the loop is already on this one, and a
        // control that lit up for it would be one a person cannot act on.
        Assert.Empty(attention.Reviews);
        Assert.Equal(1, attention.ReviewsHeldBack);
        Assert.Equal(0, attention.InReviewWithoutPullRequest);
        Assert.Equal(Key(issue), Assert.Single(attention.Conflicts).Key);
    }

    [Fact]
    public async Task Attention_HoldsBackAnIssueWithAFailedBuild_AndCountsIt()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "red", h.Review, pullRequestUrl: "https://forge.example/pulls/1");
        await h.BuildAsync(issue, BuildVerdicts.Failed, ["api"]);

        var attention = Value(await h.Attention.GetAttention(default));

        Assert.Empty(attention.Reviews);
        Assert.Equal(1, attention.ReviewsHeldBack);
        Assert.Equal(0, attention.InReviewWithoutPullRequest);
        Assert.Equal(Key(issue), Assert.Single(attention.FailingBuilds!).Key);
    }

    [Fact]
    public async Task Attention_HoldsBackAnIssueWithAPendingBuildThatAlreadyHasAFailingCheck()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "one check failed, one still running", h.Review, pullRequestUrl: "https://forge.example/pulls/1");
        await h.BuildAsync(issue, BuildVerdicts.Pending, ["api"]);

        var attention = Value(await h.Attention.GetAttention(default));

        Assert.Empty(attention.Reviews);
        Assert.Equal(1, attention.ReviewsHeldBack);
        Assert.Equal(Key(issue), Assert.Single(attention.FailingBuilds!).Key);
    }

    [Fact]
    public async Task Attention_DoesNotHoldBackAPendingBuildWithNothingFailedYet()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "still running, nothing failed", h.Review, pullRequestUrl: "https://forge.example/pulls/1");
        await h.BuildAsync(issue, BuildVerdicts.Pending);

        var attention = Value(await h.Attention.GetAttention(default));

        Assert.Equal(Key(issue), Assert.Single(attention.Reviews).Key);
        Assert.Equal(0, attention.ReviewsHeldBack);
        Assert.Empty(attention.FailingBuilds!);
    }

    [Fact]
    public async Task Attention_ListsAnIssueWithACleanVerdictAndAPassedBuild()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "delivered", h.Review, pullRequestUrl: "https://forge.example/pulls/1");
        await h.CheckAsync(issue, MergeVerdicts.Clean);
        await h.BuildAsync(issue, BuildVerdicts.Passed);

        var attention = Value(await h.Attention.GetAttention(default));

        var row = Assert.Single(attention.Reviews);
        Assert.Equal(Key(issue), row.Key);
        Assert.Equal(0, attention.ReviewsHeldBack);
        Assert.Equal(ReviewBuildStates.Success, row.BuildState);
    }

    // ---- The pre-approval half (HA-562) ----

    [Fact]
    public async Task Attention_LeavesOutAPreApprovedReviewRowTheJudgeWouldMerge()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "ready to merge itself", h.Review, pullRequestUrl: "https://forge.example/pulls/1");
        issue.PreApproval = PreApprovalLevels.PreApproved;
        await h.Db.SaveChangesAsync();
        await h.CheckAsync(issue, MergeVerdicts.Clean, holdsTrunk: true);
        await h.BuildAsync(issue, BuildVerdicts.Passed);

        var attention = Value(await h.Attention.GetAttention(default));

        Assert.Empty(attention.Reviews);
        Assert.Equal(0, attention.ReviewsHeldBack);
    }

    [Fact]
    public async Task Attention_LeavesOutAPreApprovedReviewRowTheJudgeWouldUpdate()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "ready to update itself", h.Review, pullRequestUrl: "https://forge.example/pulls/1");
        issue.PreApproval = PreApprovalLevels.PreApproved;
        await h.Db.SaveChangesAsync();
        await h.CheckAsync(issue, MergeVerdicts.Clean, holdsTrunk: false);

        var attention = Value(await h.Attention.GetAttention(default));

        Assert.Empty(attention.Reviews);
        Assert.Equal(0, attention.ReviewsHeldBack);
    }

    [Fact]
    public async Task Attention_LeavesOutAPreApprovedReviewRowStillPending()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "nobody has run a build yet", h.Review, pullRequestUrl: "https://forge.example/pulls/1");
        issue.PreApproval = PreApprovalLevels.PreApproved;
        await h.Db.SaveChangesAsync();
        await h.CheckAsync(issue, MergeVerdicts.Clean, holdsTrunk: true);

        var attention = Value(await h.Attention.GetAttention(default));

        Assert.Empty(attention.Reviews);
        Assert.Equal(0, attention.ReviewsHeldBack);
    }

    [Fact]
    public async Task Attention_KeepsAPreApprovedReviewRowTheJudgeHolds_CarryingTheSentence()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "closed without merging", h.Review, pullRequestUrl: "https://forge.example/pulls/1");
        issue.PreApproval = PreApprovalLevels.PreApproved;
        issue.PullRequestClosedAt = Now;
        await h.Db.SaveChangesAsync();

        var attention = Value(await h.Attention.GetAttention(default));

        var row = Assert.Single(attention.Reviews);
        Assert.Equal(Key(issue), row.Key);
        Assert.Equal("its pull request was closed without merging", row.HeldSentence);
    }

    [Fact]
    public async Task Attention_DoesNotAnnotateAnOrdinaryCleanRow()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "delivered", h.Review, pullRequestUrl: "https://forge.example/pulls/1");
        await h.CheckAsync(issue, MergeVerdicts.Clean);
        await h.BuildAsync(issue, BuildVerdicts.Passed);

        var attention = Value(await h.Attention.GetAttention(default));

        var row = Assert.Single(attention.Reviews);
        Assert.Equal(Key(issue), row.Key);
        Assert.Equal(0, attention.ReviewsHeldBack);
        Assert.Equal(ReviewBuildStates.Success, row.BuildState);
        Assert.Null(row.HeldSentence);
    }

    // ---- The build and up-to-date half ----

    [Fact]
    public async Task Attention_BuildStateIsUnknownForAPassedBuildOnAnOlderSha()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "stale build", h.Review, pullRequestUrl: "https://forge.example/pulls/1");
        await h.CheckAsync(issue, MergeVerdicts.Clean);
        await h.BuildAsync(issue, BuildVerdicts.Passed, sha: new string('3', 40));

        var row = Assert.Single(Value(await h.Attention.GetAttention(default)).Reviews);

        Assert.Equal(ReviewBuildStates.Unknown, row.BuildState);
    }

    [Fact]
    public async Task Attention_BuildStateIsUnknownWithNoBuildVerdictAtAll()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "nobody has read it", h.Review, pullRequestUrl: "https://forge.example/pulls/1");
        await h.CheckAsync(issue, MergeVerdicts.Clean);

        var row = Assert.Single(Value(await h.Attention.GetAttention(default)).Reviews);

        Assert.Equal(ReviewBuildStates.Unknown, row.BuildState);
    }

    [Fact]
    public async Task Attention_BuildStateIsUnknownForAPendingBuildWithNothingFailedYet()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "still running", h.Review, pullRequestUrl: "https://forge.example/pulls/1");
        await h.CheckAsync(issue, MergeVerdicts.Clean);
        await h.BuildAsync(issue, BuildVerdicts.Pending);

        var row = Assert.Single(Value(await h.Attention.GetAttention(default)).Reviews);

        Assert.Equal(ReviewBuildStates.Unknown, row.BuildState);
    }

    [Fact]
    public async Task Attention_BuildStateIsUnknownWithNoCleanMergeCheckAtAll()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "no branch read yet", h.Review, pullRequestUrl: "https://forge.example/pulls/1");
        await h.CheckAsync(issue, MergeVerdicts.None);

        var row = Assert.Single(Value(await h.Attention.GetAttention(default)).Reviews);

        Assert.Equal(ReviewBuildStates.Unknown, row.BuildState);
        Assert.Null(row.HoldsTrunk);
    }

    [Fact]
    public async Task Attention_BuildStateIsUnknownWhenOneOfTwoRepositoriesHasNotReported()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.test/one");
        await h.BindRepositoryAsync("https://example.test/two");
        var issue = await h.FileAsync("story", "half reported", h.Review, pullRequestUrl: "https://forge.example/pulls/1");
        await h.CheckAsync(issue, MergeVerdicts.Clean, remote: "https://example.test/one");
        await h.BuildAsync(issue, BuildVerdicts.Passed, remote: "https://example.test/one");
        await h.CheckAsync(issue, MergeVerdicts.Clean, remote: "https://example.test/two");

        var row = Assert.Single(Value(await h.Attention.GetAttention(default)).Reviews);

        Assert.Equal(ReviewBuildStates.Unknown, row.BuildState);
    }

    [Fact]
    public async Task Attention_HoldsTrunkTrueNamesTheTrunk()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "current", h.Review, pullRequestUrl: "https://forge.example/pulls/1");
        await h.CheckAsync(issue, MergeVerdicts.Clean, holdsTrunk: true);

        var row = Assert.Single(Value(await h.Attention.GetAttention(default)).Reviews);

        Assert.Equal(true, row.HoldsTrunk);
        Assert.Equal("main", row.Trunk);
    }

    [Fact]
    public async Task Attention_HoldsTrunkFalse()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "behind", h.Review, pullRequestUrl: "https://forge.example/pulls/1");
        await h.CheckAsync(issue, MergeVerdicts.Clean, holdsTrunk: false);

        var row = Assert.Single(Value(await h.Attention.GetAttention(default)).Reviews);

        Assert.Equal(false, row.HoldsTrunk);
        Assert.Equal("main", row.Trunk);
    }

    [Fact]
    public async Task Attention_ADroppedRepositoryDoesNotCountTowardsBuildStateOrHoldsTrunk()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.test/kept");
        var issue = await h.FileAsync("story", "one repo let go", h.Review, pullRequestUrl: "https://forge.example/pulls/1");
        await h.CheckAsync(issue, MergeVerdicts.Clean, remote: "https://example.test/let-go", holdsTrunk: true);
        await h.BuildAsync(issue, BuildVerdicts.Passed, remote: "https://example.test/let-go");

        var row = Assert.Single(Value(await h.Attention.GetAttention(default)).Reviews);

        Assert.Equal(ReviewBuildStates.Unknown, row.BuildState);
        Assert.Null(row.HoldsTrunk);
    }

    [Fact]
    public async Task Attention_NamesThePlaybookFileAClearCheckFlagged()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "rewrites a playbook", h.Review, pullRequestUrl: "https://forge.example/pulls/1");
        await h.CheckAsync(issue, MergeVerdicts.Clean, holdsTrunk: true, playbookFilePath: ".hatch/playbooks.yaml");

        var attention = Value(await h.Attention.GetAttention(default));
        var row = Assert.Single(attention.Reviews);

        Assert.Equal(".hatch/playbooks.yaml", row.PlaybookFilePath);
        Assert.Equal(1, attention.Reviews.Count);
    }

    [Fact]
    public async Task Attention_ListsAnIssueAgainOnceItsConflictClears()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "was stuck", h.Review, pullRequestUrl: "https://forge.example/pulls/1");
        await h.CheckAsync(issue, MergeVerdicts.Conflicted, ["a.cs"]);

        Assert.Equal(1, Value(await h.Attention.GetAttention(default)).ReviewsHeldBack);

        await h.CheckAsync(issue, MergeVerdicts.Clean);
        var attention = Value(await h.Attention.GetAttention(default));

        Assert.Equal(Key(issue), Assert.Single(attention.Reviews).Key);
        Assert.Equal(0, attention.ReviewsHeldBack);
    }

    [Fact]
    public async Task Attention_DoesNotCountAnIssueWithNoPullRequestAsHeldBack()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "a branch and no link", h.Review);
        await h.CheckAsync(issue, MergeVerdicts.Conflicted, ["a.cs"]);

        var attention = Value(await h.Attention.GetAttention(default));

        // A held-back count is about a pull request the loop is sitting on -
        // an issue with nowhere to review it in the first place is the other
        // emptiness, not this one.
        Assert.Equal(0, attention.ReviewsHeldBack);
        Assert.Equal(1, attention.InReviewWithoutPullRequest);
    }

    // ---- The merges half (HA-438) ----

    [Fact]
    public async Task Attention_HoldsBackAConflictedApprovedIssue()
    {
        var h = await NewAsync();
        var merges = new EfHatchStatus { Name = "approved", SortOrder = 37, IsProtected = true, MergesPullRequest = true };
        h.Db.Add(merges);
        await h.Db.SaveChangesAsync();
        var issue = await h.FileAsync("task", "conflicted", merges.Id, pullRequestUrl: "https://forge.example/pulls/1");
        await h.CheckAsync(issue, MergeVerdicts.Conflicted, ["a.cs"]);

        var attention = Value(await h.Attention.GetAttention(default));

        var row = Assert.Single(attention.ApprovedHeld!);
        Assert.Equal(Key(issue), row.Key);
        Assert.Equal("its branch conflicts with the trunk - back to review to fix it", row.Sentence);
        Assert.Empty(attention.ApprovedWaiting!);
    }

    [Fact]
    public async Task Attention_HoldsBackAnAmbiguousApprovedIssue()
    {
        var h = await NewAsync();
        var merges = new EfHatchStatus { Name = "approved", SortOrder = 37, IsProtected = true, MergesPullRequest = true };
        h.Db.Add(merges);
        await h.Db.SaveChangesAsync();
        var issue = await h.FileAsync("task", "ambiguous", merges.Id, pullRequestUrl: "https://forge.example/pulls/1");
        await h.CheckAsync(issue, MergeVerdicts.Ambiguous);

        var attention = Value(await h.Attention.GetAttention(default));

        var row = Assert.Single(attention.ApprovedHeld!);
        Assert.Equal(Key(issue), row.Key);
        Assert.Equal("more than one branch on origin is named for it - delete the ones that are not its branch", row.Sentence);
        Assert.Empty(attention.ApprovedWaiting!);
    }

    [Fact]
    public async Task Attention_HoldsBackAnApprovedIssueWithNoBranch()
    {
        var h = await NewAsync();
        var merges = new EfHatchStatus { Name = "approved", SortOrder = 37, IsProtected = true, MergesPullRequest = true };
        h.Db.Add(merges);
        await h.Db.SaveChangesAsync();
        var issue = await h.FileAsync("task", "no branch", merges.Id, pullRequestUrl: "https://forge.example/pulls/1");
        await h.CheckAsync(issue, MergeVerdicts.None);

        var attention = Value(await h.Attention.GetAttention(default));

        var row = Assert.Single(attention.ApprovedHeld!);
        Assert.Equal(Key(issue), row.Key);
        Assert.Equal("no branch on origin is named for it", row.Sentence);
        Assert.Empty(attention.ApprovedWaiting!);
    }

    [Fact]
    public async Task Attention_HoldsBackAnApprovedIssueWhoseBuildFailed()
    {
        var h = await NewAsync();
        var merges = new EfHatchStatus { Name = "approved", SortOrder = 37, IsProtected = true, MergesPullRequest = true };
        h.Db.Add(merges);
        await h.Db.SaveChangesAsync();
        var issue = await h.FileAsync("task", "build failed", merges.Id, pullRequestUrl: "https://forge.example/pulls/1");
        await h.CheckAsync(issue, MergeVerdicts.Clean, holdsTrunk: true);
        await h.BuildAsync(issue, BuildVerdicts.Failed, ["api"]);

        var attention = Value(await h.Attention.GetAttention(default));

        var row = Assert.Single(attention.ApprovedHeld!);
        Assert.Equal(Key(issue), row.Key);
        Assert.Equal("its build on 2222222 failed - back to review to fix it", row.Sentence);
        Assert.Empty(attention.ApprovedWaiting!);
    }

    [Fact]
    public async Task Attention_HoldsBackAnApprovedIssueWhosePullRequestClosedWithoutMerging()
    {
        var h = await NewAsync();
        var merges = new EfHatchStatus { Name = "approved", SortOrder = 37, IsProtected = true, MergesPullRequest = true };
        h.Db.Add(merges);
        await h.Db.SaveChangesAsync();
        var issue = await h.FileAsync("task", "closed", merges.Id, pullRequestUrl: "https://forge.example/pulls/1");
        issue.PullRequestClosedAt = Now;
        await h.Db.SaveChangesAsync();

        var attention = Value(await h.Attention.GetAttention(default));

        var row = Assert.Single(attention.ApprovedHeld!);
        Assert.Equal(Key(issue), row.Key);
        Assert.Equal("its pull request was closed without merging", row.Sentence);
        Assert.Empty(attention.ApprovedWaiting!);
    }

    [Fact]
    public async Task Attention_HoldsBackAnApprovedIssuePastTheSettleWindowWithNoChecks()
    {
        var h = await NewAsync();
        var merges = new EfHatchStatus { Name = "approved", SortOrder = 37, IsProtected = true, MergesPullRequest = true };
        h.Db.Add(merges);
        await h.Db.SaveChangesAsync();
        var issue = await h.FileAsync("task", "stuck", merges.Id, pullRequestUrl: "https://forge.example/pulls/1");
        await h.CheckAsync(issue, MergeVerdicts.Clean, holdsTrunk: true);
        await h.BuildAsync(issue, BuildVerdicts.None);
        h.Time.Advance(TimeSpan.FromMinutes(11));

        var attention = Value(await h.Attention.GetAttention(default));

        var row = Assert.Single(attention.ApprovedHeld!);
        Assert.Equal(Key(issue), row.Key);
        Assert.Equal("its branch holds main and no checks ran on it", row.Sentence);
        Assert.Empty(attention.ApprovedWaiting!);
    }

    [Fact]
    public async Task Attention_WaitsOnAnApprovedIssuePastTheSettleWindowWhenNoChecksAreRequired()
    {
        var h = await NewAsync();
        await h.SetLandingAsync(checksRequired: false);
        var merges = new EfHatchStatus { Name = "approved", SortOrder = 37, IsProtected = true, MergesPullRequest = true };
        h.Db.Add(merges);
        await h.Db.SaveChangesAsync();
        var issue = await h.FileAsync("task", "nothing required", merges.Id, pullRequestUrl: "https://forge.example/pulls/1");
        await h.CheckAsync(issue, MergeVerdicts.Clean, holdsTrunk: true);
        await h.BuildAsync(issue, BuildVerdicts.None);
        h.Time.Advance(TimeSpan.FromMinutes(11));

        var attention = Value(await h.Attention.GetAttention(default));

        var row = Assert.Single(attention.ApprovedWaiting!);
        Assert.Equal(Key(issue), row.Key);
        Assert.Equal("merge", row.Kind);
        Assert.Equal("its branch holds main; this project requires no checks", row.Sentence);
        Assert.Empty(attention.ApprovedHeld!);
    }

    [Fact]
    public async Task Attention_DropsAPreApprovedReviewRowTheJudgeMergesForWantOfRequiredChecks()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "nothing required", h.Review, pullRequestUrl: "https://forge.example/pulls/1");
        issue.PreApproval = PreApprovalLevels.PreApproved;
        await h.Db.SaveChangesAsync();
        await h.CheckAsync(issue, MergeVerdicts.Clean, holdsTrunk: true);
        await h.BuildAsync(issue, BuildVerdicts.None);
        h.Time.Advance(TimeSpan.FromMinutes(11));

        var held = Assert.Single(Value(await h.Attention.GetAttention(default)).Reviews);
        Assert.Equal("its branch holds main and no checks ran on it", held.HeldSentence);

        await h.SetLandingAsync(checksRequired: false);

        Assert.Empty(Value(await h.Attention.GetAttention(default)).Reviews);
    }

    [Fact]
    public async Task Attention_DropsAPreApprovedReviewRowTheForgeCallsCleanAtTheCheckedSha()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "forge clean", h.Review, pullRequestUrl: "https://forge.example/pulls/1");
        issue.PreApproval = PreApprovalLevels.PreApproved;
        issue.PullRequestMergeState = MergeStates.Clean;
        issue.PullRequestHeadSha = new string('2', 40);
        await h.Db.SaveChangesAsync();
        await h.CheckAsync(issue, MergeVerdicts.Clean, holdsTrunk: true);
        await h.BuildAsync(issue, BuildVerdicts.None);
        h.Time.Advance(TimeSpan.FromMinutes(11));

        Assert.Empty(Value(await h.Attention.GetAttention(default)).Reviews);
    }

    [Fact]
    public async Task Attention_LeavesAnApprovedIssueWithNoChecksPendingInsideTheSettleWindow()
    {
        var h = await NewAsync();
        var merges = new EfHatchStatus { Name = "approved", SortOrder = 37, IsProtected = true, MergesPullRequest = true };
        h.Db.Add(merges);
        await h.Db.SaveChangesAsync();
        var issue = await h.FileAsync("task", "still settling", merges.Id, pullRequestUrl: "https://forge.example/pulls/1");
        await h.CheckAsync(issue, MergeVerdicts.Clean, holdsTrunk: true);
        await h.BuildAsync(issue, BuildVerdicts.None);

        var attention = Value(await h.Attention.GetAttention(default));

        var row = Assert.Single(attention.ApprovedWaiting!);
        Assert.Equal(Key(issue), row.Key);
        Assert.Equal("pending", row.Kind);
        Assert.Equal("its branch holds main and no checks ran on it", row.Sentence);
        Assert.Empty(attention.ApprovedHeld!);
    }

    [Fact]
    public async Task Attention_ListsAnUncheckedApprovedIssueAsPending()
    {
        var h = await NewAsync();
        var merges = new EfHatchStatus { Name = "approved", SortOrder = 37, IsProtected = true, MergesPullRequest = true };
        h.Db.Add(merges);
        await h.Db.SaveChangesAsync();
        var issue = await h.FileAsync("task", "untouched", merges.Id, pullRequestUrl: "https://forge.example/pulls/1");

        var attention = Value(await h.Attention.GetAttention(default));

        var row = Assert.Single(attention.ApprovedWaiting!);
        Assert.Equal(Key(issue), row.Key);
        Assert.Equal("pending", row.Kind);
        Assert.Equal("no runner has checked its branch against the trunk yet", row.Sentence);
        Assert.Empty(attention.ApprovedHeld!);
    }

    [Fact]
    public async Task Attention_ListsAnApprovedIssueThatDoesNotHoldTrunkAsAnUpdate()
    {
        var h = await NewAsync();
        var merges = new EfHatchStatus { Name = "approved", SortOrder = 37, IsProtected = true, MergesPullRequest = true };
        h.Db.Add(merges);
        await h.Db.SaveChangesAsync();
        var issue = await h.FileAsync("task", "stale", merges.Id, pullRequestUrl: "https://forge.example/pulls/1");
        await h.CheckAsync(issue, MergeVerdicts.Clean, holdsTrunk: false);

        var attention = Value(await h.Attention.GetAttention(default));

        var row = Assert.Single(attention.ApprovedWaiting!);
        Assert.Equal(Key(issue), row.Key);
        Assert.Equal("update", row.Kind);
        Assert.Equal("bringing its branch current from main", row.Sentence);
        Assert.Empty(attention.ApprovedHeld!);
    }

    [Fact]
    public async Task Attention_ListsAnApprovedIssueReadyToMerge()
    {
        var h = await NewAsync();
        var merges = new EfHatchStatus { Name = "approved", SortOrder = 37, IsProtected = true, MergesPullRequest = true };
        h.Db.Add(merges);
        await h.Db.SaveChangesAsync();
        var issue = await h.FileAsync("task", "ready", merges.Id, pullRequestUrl: "https://forge.example/pulls/1");
        await h.CheckAsync(issue, MergeVerdicts.Clean, holdsTrunk: true);
        await h.BuildAsync(issue, BuildVerdicts.Passed);

        var attention = Value(await h.Attention.GetAttention(default));

        var row = Assert.Single(attention.ApprovedWaiting!);
        Assert.Equal(Key(issue), row.Key);
        Assert.Equal("merge", row.Kind);
        Assert.Equal("its branch holds main and its build passes", row.Sentence);
        Assert.Empty(attention.ApprovedHeld!);
    }

    [Fact]
    public async Task Attention_AnswersEmptyApprovedListsOnABoardWithNoMergesColumn()
    {
        var h = await NewAsync();

        var attention = Value(await h.Attention.GetAttention(default));

        Assert.Empty(attention.ApprovedHeld!);
        Assert.Empty(attention.ApprovedWaiting!);
    }

    // ---- Which column review is ----

    [Fact]
    public async Task Attention_FindsTheReviewColumnAfterItIsRenamed()
    {
        var h = await NewAsync();
        await h.RenameAsync(h.Review, "Waiting on Nathan");
        var up = await h.FileAsync("story", "delivered", h.Review, pullRequestUrl: "https://forge.example/pulls/1");

        // Measured, not named: the column immediately left of the first
        // terminal one, whatever anybody has called it.
        Assert.Equal(Key(up), Assert.Single(Value(await h.Attention.GetAttention(default)).Reviews).Key);
    }

    [Fact]
    public async Task Attention_FollowsTheReviewColumnWhenTheBoardIsReordered()
    {
        var h = await NewAsync();
        var moved = await h.FileAsync("story", "in the column that is now last before done", h.InProgress, pullRequestUrl: "https://forge.example/pulls/1");
        await h.FileAsync("story", "in the one that used to be", h.Review, pullRequestUrl: "https://forge.example/pulls/2");

        // The two columns trade places, so "in progress" is now the one
        // immediately left of terminal - and it is the one that answers.
        await h.ReorderAsync(h.InProgress, 36);

        Assert.Equal(Key(moved), Assert.Single(Value(await h.Attention.GetAttention(default)).Reviews).Key);
    }

    /// <summary>
    /// The landmark is measured off the columns the board draws, so a siding
    /// parked between review and done does not become "the column before
    /// terminal" - which would have the panel hunting for pull requests in a
    /// column nothing is ever dispatched from.
    /// </summary>
    [Fact]
    public async Task Attention_IgnoresADeferredColumnWhenItFindsTheReviewColumn()
    {
        var h = await NewAsync();
        var up = await h.FileAsync("story", "delivered", h.Review, pullRequestUrl: "https://forge.example/pulls/1");
        await h.ShelfAsync(37);

        Assert.Equal(Key(up), Assert.Single(Value(await h.Attention.GetAttention(default)).Reviews).Key);
    }

    /// <summary>
    /// And a shelved ticket is not in review however the board is shaped: the
    /// column it sits in is not the review column, so it is neither listed nor
    /// counted as one waiting for a link.
    /// </summary>
    [Fact]
    public async Task Attention_SaysNothingAboutAShelvedIssue()
    {
        var h = await NewAsync();
        var shelf = await h.ShelfAsync(37);
        await h.FileAsync("story", "parked with a branch open", shelf, pullRequestUrl: "https://forge.example/pulls/1");

        var attention = Value(await h.Attention.GetAttention(default));

        Assert.Empty(attention.Reviews);
        Assert.Equal(0, attention.InReviewWithoutPullRequest);
    }

    [Fact]
    public async Task Attention_AnswersEmptyOnABoardWithNoRoomForAReviewColumn()
    {
        var h = await OneColumnAsync();

        var attention = Value(await h.Attention.GetAttention(default));

        // A board whose only column is terminal has no column left of it. The
        // section draws its empty state; nothing here throws.
        Assert.Empty(attention.Reviews);
        Assert.Equal(0, attention.InReviewWithoutPullRequest);
    }

    // ---- The question half ----

    [Fact]
    public async Task Attention_ListsEveryOpenQuestionInTheHouseOldestFirst()
    {
        var h = await NewAsync();
        var one = await h.FileAsync("story", "asking about scope", h.Todo);
        var two = await h.FileAsync("story", "asking about a name", h.Inbox);
        await h.AskAsync(two, "what should it be called?", at: Now.AddHours(2));
        await h.AskAsync(one, "per-node or global?", at: Now.AddHours(1));

        var questions = Value(await h.Attention.GetAttention(default)).Questions;

        // Answering order, wherever the issue happens to stand: the question
        // that has waited longest is the one holding something up longest.
        Assert.Equal(["per-node or global?", "what should it be called?"], questions.Select(q => q.Body));
        Assert.Equal(Key(one), questions[0].IssueKey);
        Assert.Equal("asking about scope", questions[0].IssueTitle);
    }

    [Fact]
    public async Task Attention_LeavesOutAQuestionSomebodyHasAnswered()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "settled", h.Todo);
        var question = await h.AskAsync(issue, "per-node or global?");
        await h.AnswerAsync(issue, question, "per-node");

        // Open is computed, not stored - the same call the board badges a card
        // with, so this list and that badge cannot disagree.
        Assert.Empty(Value(await h.Attention.GetAttention(default)).Questions);
    }

    [Fact]
    public async Task Attention_LeavesOutAQuestionOnAnIssueInATerminalColumn()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "shipped", h.Done);
        await h.AskAsync(issue, "per-node or global?");

        Assert.Empty(Value(await h.Attention.GetAttention(default)).Questions);
    }

    [Fact]
    public async Task Attention_StillListsAQuestionOnADeferredIssue()
    {
        var h = await NewAsync();
        var shelf = await h.ShelfAsync(37);
        var issue = await h.FileAsync("story", "parked", shelf);
        await h.AskAsync(issue, "per-node or global?");

        Assert.Single(Value(await h.Attention.GetAttention(default)).Questions);
    }

    // ---- Runners out of usage ----

    [Fact]
    public async Task Attention_ListsALiveRunnerOutOfUsage()
    {
        var h = await NewAsync();
        await h.RunnerAsync("host:/checkouts/one", h.Time.GetUtcNow().AddHours(2));

        var exhausted = Value(await h.Attention.GetAttention(default)).ExhaustedRunners;

        var row = Assert.Single(exhausted!);
        Assert.Equal("host:/checkouts/one", row.Name);
        Assert.Equal(h.Time.GetUtcNow().AddHours(2), row.ExhaustedUntil);
    }

    [Fact]
    public async Task Attention_DoesNotListOneWhoseResetHasPassed()
    {
        var h = await NewAsync();
        await h.RunnerAsync("host:/checkouts/one", h.Time.GetUtcNow().AddHours(-1));

        Assert.Empty(Value(await h.Attention.GetAttention(default)).ExhaustedRunners!);
    }

    [Fact]
    public async Task Attention_DoesNotListOneThatHasGoneQuiet()
    {
        var h = await NewAsync();
        await h.RunnerAsync(
            "host:/checkouts/one", h.Time.GetUtcNow().AddHours(2), lastSeenAt: h.Time.GetUtcNow().AddDays(-1));

        Assert.Empty(Value(await h.Attention.GetAttention(default)).ExhaustedRunners!);
    }

    [Fact]
    public async Task Attention_OrdersExhaustedRunnersBySoonestResetFirst()
    {
        var h = await NewAsync();
        await h.RunnerAsync("host:/checkouts/late", h.Time.GetUtcNow().AddHours(5));
        await h.RunnerAsync("host:/checkouts/soon", h.Time.GetUtcNow().AddHours(1));

        Assert.Equal(
            ["host:/checkouts/soon", "host:/checkouts/late"],
            Value(await h.Attention.GetAttention(default)).ExhaustedRunners!.Select(r => r.Name));
    }

    [Fact]
    public async Task Attention_ListsExhaustedRunnersEvenOnABoardWithNoReviewColumn()
    {
        var h = await OneColumnAsync();
        await h.RunnerAsync("host:/checkouts/one", h.Time.GetUtcNow().AddHours(2));

        var exhausted = Value(await h.Attention.GetAttention(default)).ExhaustedRunners;

        Assert.Single(exhausted!);
    }

    // ---- A move no live runner can take (HA-2013) ----

    // The board's three playbooks, the implementation move running on <model>:
    // without them every row folds "no playbook covers it".
    private static async Task WaitingBoardAsync(Harness h, string model)
    {
        h.Db.AddRange(
            Playbook(h.Todo, h.InProgress, "", model),
            Playbook(h.InProgress, h.Review, "", "sonnet"));
        await h.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task WaitingOnARunner_ListsAMoveEveryLiveRunnerLacksTheModelFor_UntilOneOffersIt()
    {
        var h = await NewAsync();
        await WaitingBoardAsync(h, "fable");
        var issue = await h.FileAsync("task", "needs fable", h.Todo);
        await h.RunnerAsync("host:/one", null, models: ["haiku", "sonnet"], agent: "claude");
        await h.RunnerAsync("host:/two", null, models: ["sonnet"], agent: "claude");

        var row = Assert.Single(Value(await h.Attention.GetAttention(default)).WaitingOnARunner!);
        Assert.Equal(new WaitingOnARunnerDto(Key(issue), "needs fable", "task", "todo", "no live runner offers fable"), row);

        await h.RunnerAsync("host:/three", null, models: ["sonnet", "fable"], agent: "claude");

        Assert.Empty(Value(await h.Attention.GetAttention(default)).WaitingOnARunner!);
    }

    [Fact]
    public async Task WaitingOnARunner_CountsNoRunnerThatIsStalePausedOrExhausted()
    {
        var h = await NewAsync();
        await WaitingBoardAsync(h, "fable");
        await h.FileAsync("task", "needs fable", h.Todo);
        await h.RunnerAsync("host:/stale", null, lastSeenAt: h.Time.GetUtcNow().AddDays(-1), models: ["sonnet"], agent: "claude");
        await h.RunnerAsync("host:/paused", null, models: ["sonnet"], agent: "claude", state: "paused");
        await h.RunnerAsync("host:/spent", h.Time.GetUtcNow().AddHours(2), models: ["sonnet"], agent: "claude");

        Assert.Empty(Value(await h.Attention.GetAttention(default)).WaitingOnARunner!);

        // The control: the same runner, live, is what makes it a row.
        await h.RunnerAsync("host:/live", null, models: ["sonnet"], agent: "claude");
        Assert.Single(Value(await h.Attention.GetAttention(default)).WaitingOnARunner!);
    }

    [Fact]
    public async Task WaitingOnARunner_NamesTheDependenciesNoLiveRunnerHas()
    {
        var h = await NewAsync();
        await WaitingBoardAsync(h, "sonnet");
        var repo = await h.BindRepositoryAsync("https://example.com/o/r", "main");
        await h.DependencyFileAsync(repo, "main", new DeclaredDependency("pwsh", "7"), new DeclaredDependency("playwright-browsers", null));
        // The move into implementation is never folded on a dependency, so the
        // issue stands in progress: its move to review is code-gated.
        var issue = await h.FileAsync("task", "needs a toolchain", h.InProgress);
        await h.RunnerAsync("host:/one", null, has: ["dotnet@10"]);
        await h.RunnerAsync("host:/two", null, has: ["node@22"]);

        var row = Assert.Single(Value(await h.Attention.GetAttention(default)).WaitingOnARunner!);
        Assert.Equal(Key(issue), row.Key);
        Assert.Equal("in progress", row.Move);
        Assert.Equal("no live runner has playwright-browsers and pwsh 7", row.Missing);

        await h.RunnerAsync("host:/three", null, has: ["pwsh@7.4", "playwright-browsers"]);

        Assert.Empty(Value(await h.Attention.GetAttention(default)).WaitingOnARunner!);
    }

    [Fact]
    public async Task WaitingOnARunner_NeverCountsARunnerThatDeclaredNothingAsLackingIt()
    {
        var h = await NewAsync();
        await WaitingBoardAsync(h, "fable");
        await h.FileAsync("task", "needs fable", h.Todo);
        await h.RunnerAsync("host:/profiled", null, models: ["sonnet"], agent: "claude");
        await h.RunnerAsync("host:/unprofiled", null);

        Assert.Empty(Value(await h.Attention.GetAttention(default)).WaitingOnARunner!);
    }

    [Fact]
    public async Task WaitingOnARunner_ASplitFleetListsNothing()
    {
        var h = await NewAsync();
        await WaitingBoardAsync(h, "fable");
        var repo = await h.BindRepositoryAsync("https://example.com/o/r", "main");
        await h.DependencyFileAsync(repo, "main", new DeclaredDependency("pwsh", "7"));
        await h.FileAsync("task", "needs both", h.Todo);

        // One runner has the tool but not the model, the other the model but not the tool.
        await h.RunnerAsync("host:/tool", null, models: ["sonnet"], agent: "claude", has: ["pwsh@7"]);
        await h.RunnerAsync("host:/model", null, models: ["fable"], agent: "claude", has: ["dotnet@10"]);

        Assert.Empty(Value(await h.Attention.GetAttention(default)).WaitingOnARunner!);
    }

    [Fact]
    public async Task WaitingOnARunner_AnEmptyFleetAnswersEmpty_AndSoDoesABoardWhereEveryGapIsMet()
    {
        var h = await NewAsync();
        await WaitingBoardAsync(h, "fable");
        await h.FileAsync("task", "needs fable", h.Todo);

        Assert.Empty(Value(await h.Attention.GetAttention(default)).WaitingOnARunner!);

        await h.RunnerAsync("host:/one", null, models: ["fable"], agent: "claude");

        Assert.Empty(Value(await h.Attention.GetAttention(default)).WaitingOnARunner!);
    }

    [Fact]
    public async Task WaitingOnARunner_IsOrderedByTierThenRank()
    {
        var h = await NewAsync();
        await WaitingBoardAsync(h, "fable");
        var late = await h.FileAsync("task", "late normal", h.Todo, rank: 4096);
        var early = await h.FileAsync("task", "early normal", h.Todo, rank: 1024);
        var urgent = await h.FileAsync("task", "urgent", h.Todo, rank: 8192);
        urgent.Priority = PriorityLevels.Expedited;
        await h.Db.SaveChangesAsync();
        await h.RunnerAsync("host:/one", null, models: ["sonnet"], agent: "claude");

        Assert.Equal(
            [Key(urgent), Key(early), Key(late)],
            Value(await h.Attention.GetAttention(default)).WaitingOnARunner!.Select(r => r.Key));
    }

    [Fact]
    public async Task WaitingOnARunner_IsAnsweredEvenOnABoardWithNoReviewColumn()
    {
        var h = await OneColumnAsync();

        Assert.Empty(Value(await h.Attention.GetAttention(default)).WaitingOnARunner!);
    }

    // ---- A session-only peer, and a beat beside a session of one key (HA-2036) ----

    [Fact]
    public async Task WaitingOnARunner_ASessionOnlyRunnerWithTheWrongAgent_ListsTheRow_AndASecondWithTheRightOneEmptiesIt()
    {
        var h = await NewAsync();
        await WaitingBoardAsync(h, "claude-opus-5");
        var issue = await h.FileAsync("task", "pinned to claude", h.Todo);

        var key = await h.AddKeyAsync("session-only");
        await h.AddSessionAsync(key, "here:/one", agent: "codex");

        var row = Assert.Single(Value(await h.Attention.GetAttention(default)).WaitingOnARunner!);
        Assert.Equal(Key(issue), row.Key);
        Assert.Equal("pinned to claude-opus-5 - no live runner's agent is claude", row.Missing);

        await h.AddSessionAsync(key, "here:/two", agent: "claude");

        Assert.Empty(Value(await h.Attention.GetAttention(default)).WaitingOnARunner!);
    }

    [Fact]
    public async Task WaitingOnARunner_ABeatAndASessionOfOneKey_AreTwoPeersNeverOne()
    {
        var h = await NewAsync();
        await WaitingBoardAsync(h, "claude-opus-5");
        var issue = await h.FileAsync("task", "pinned to claude", h.Todo);

        // The beat is paused, so it is not itself a live peer - but its row
        // still exists under the same key, and a live session of that key
        // is judged on its own, not folded away by the beat beside it.
        var key = await h.AddKeyAsync("mixed");
        await h.RunnerAsync(key.Name, null, state: "paused");
        await h.AddSessionAsync(key, "here:/one", agent: "codex");

        var row = Assert.Single(Value(await h.Attention.GetAttention(default)).WaitingOnARunner!);
        Assert.Equal(Key(issue), row.Key);

        await h.AddSessionAsync(key, "here:/two", agent: "claude");

        Assert.Empty(Value(await h.Attention.GetAttention(default)).WaitingOnARunner!);
    }

    // ---- The trunk half (HA-95) ----

    [Fact]
    public async Task Attention_ListsAFailedTrunkBuild()
    {
        var h = await NewAsync();
        await h.TrunkAsync(BuildVerdicts.Failed, failing: ["api", "CI"]);

        var row = Assert.Single(Value(await h.Attention.GetAttention(default)).TrunkBuilds!);

        Assert.Equal("main", row.Trunk);
        Assert.Equal("forge.example/owner/repo", row.Canonical);
        Assert.Equal(["api", "CI"], row.Failing.Select(f => f.Name));
        Assert.Null(row.BugIssueKey);
    }

    [Fact]
    public async Task Attention_ListsAPendingTrunkBuildThatAlreadyCarriesAFailingCheck()
    {
        var h = await NewAsync();
        await h.TrunkAsync(BuildVerdicts.Pending, failing: ["api"]);

        Assert.Single(Value(await h.Attention.GetAttention(default)).TrunkBuilds!);
    }

    [Theory]
    [InlineData(BuildVerdicts.Passed)]
    [InlineData(BuildVerdicts.None)]
    public async Task Attention_DoesNotListAPassedOrANoneTrunkBuild(string verdict)
    {
        var h = await NewAsync();
        await h.TrunkAsync(verdict);

        Assert.Empty(Value(await h.Attention.GetAttention(default)).TrunkBuilds!);
    }

    [Fact]
    public async Task Attention_DoesNotListAPendingTrunkBuildWithNothingFailedYet()
    {
        var h = await NewAsync();
        await h.TrunkAsync(BuildVerdicts.Pending);

        Assert.Empty(Value(await h.Attention.GetAttention(default)).TrunkBuilds!);
    }

    [Fact]
    public async Task Attention_CarriesTheAttachedBugsKey()
    {
        var h = await NewAsync();
        var bug = await h.FileAsync("bug", "Build failing on main", h.Inbox);
        await h.TrunkAsync(BuildVerdicts.Failed, failing: ["api"], bugIssueId: bug.Id);

        Assert.Equal(Key(bug), Assert.Single(Value(await h.Attention.GetAttention(default)).TrunkBuilds!).BugIssueKey);
    }

    [Fact]
    public async Task Attention_OrdersTrunkBuildsByCanonicalThenTrunk()
    {
        var h = await NewAsync();
        await h.TrunkAsync(BuildVerdicts.Failed, failing: ["a"], remote: "forge.example/owner/repo", trunk: "release");
        await h.TrunkAsync(BuildVerdicts.Failed, failing: ["a"], remote: "forge.example/owner/repo", trunk: "main");
        await h.TrunkAsync(BuildVerdicts.Failed, failing: ["a"], remote: "forge.example/owner/another");

        var rows = Value(await h.Attention.GetAttention(default)).TrunkBuilds!;

        Assert.Equal(
            [("forge.example/owner/another", "main"), ("forge.example/owner/repo", "main"), ("forge.example/owner/repo", "release")],
            rows.Select(r => (r.Canonical, r.Trunk)));
    }

    [Fact]
    public async Task Attention_ListsAFailedTrunkBuild_EvenOnABoardWithNoRoomForAReviewColumn()
    {
        var h = await OneColumnAsync();
        await h.TrunkAsync(BuildVerdicts.Failed, failing: ["api"]);

        Assert.Single(Value(await h.Attention.GetAttention(default)).TrunkBuilds!);
    }

    // ---- The trunk-landing half ----

    private static readonly string Landed = new('2', 40);

    /// <summary>A trunk-landing issue in review whose landing the trunk's build, in the given verdict, is read on.</summary>
    private static async Task<EfHatchIssue> LandedAsync(Harness h, string? verdict, string? sha = null)
    {
        await h.SetLandingAsync(Landings.Trunk);
        var issue = await h.FileAsync("story", "landed", h.Review);
        await h.LandingAsync(issue, Landed, containedBy: sha ?? Landed);
        if (verdict is not null) await h.TrunkAsync(verdict);
        return issue;
    }

    [Fact]
    public async Task Attention_ListsALandingWhoseTrunkBuildFailedAfterItLanded()
    {
        var h = await NewAsync();
        var issue = await LandedAsync(h, BuildVerdicts.Failed);

        var attention = Value(await h.Attention.GetAttention(default));

        var row = Assert.Single(attention.LandingHeld!);
        Assert.Equal(Key(issue), row.Key);
        Assert.Equal("landed", row.Title);
        Assert.Equal("story", row.Type);
        Assert.Contains("failed after it landed", row.Sentence);
        Assert.Empty(attention.Reviews);
        Assert.Equal(0, attention.InReviewWithoutPullRequest);

        // The trunk's own row is a separate list, and stays in it.
        Assert.Single(attention.TrunkBuilds!);
    }

    [Fact]
    public async Task Attention_ListsALandingWhoseTrunkRanNoChecksOnceTheSettleWindowHasPassed()
    {
        var h = await NewAsync();
        var issue = await LandedAsync(h, BuildVerdicts.None);

        // Inside the window the checks may yet start: the loop's, not a person's.
        Assert.Empty(Value(await h.Attention.GetAttention(default)).LandingHeld!);

        h.Time.Advance(TimeSpan.FromMinutes(11));

        var row = Assert.Single(Value(await h.Attention.GetAttention(default)).LandingHeld!);
        Assert.Equal(Key(issue), row.Key);
        Assert.Contains("no checks ran there", row.Sentence);
    }

    [Fact]
    public async Task Attention_LeavesOutALandingWithNoChecksOnAProjectThatRequiresNone()
    {
        var h = await NewAsync();
        await LandedAsync(h, BuildVerdicts.None);
        await h.SetLandingAsync(checksRequired: false);
        h.Time.Advance(TimeSpan.FromMinutes(11));

        var attention = Value(await h.Attention.GetAttention(default));

        Assert.Empty(attention.LandingHeld!);
        Assert.Empty(attention.Reviews);
    }

    [Fact]
    public async Task Attention_LeavesOutALandingThatPassesAndOneStillPending()
    {
        var h = await NewAsync();
        await h.SetLandingAsync(Landings.Trunk);
        var passed = await h.FileAsync("story", "passed", h.Review);
        var unseen = await h.FileAsync("story", "unseen", h.Review);
        var unread = await h.FileAsync("story", "unread", h.Review);
        var building = await h.FileAsync("story", "building", h.Review);
        await h.LandingAsync(passed, Landed, containedBy: Landed, remote: "forge.example/owner/passed");
        await h.TrunkAsync(BuildVerdicts.Passed, remote: "forge.example/owner/passed");
        await h.LandingAsync(unseen, Landed, remote: "forge.example/owner/unseen");
        await h.LandingAsync(unread, Landed, containedBy: Landed, remote: "forge.example/owner/unread");
        await h.LandingAsync(building, Landed, containedBy: Landed, remote: "forge.example/owner/building");
        await h.TrunkAsync(BuildVerdicts.Pending, remote: "forge.example/owner/building");

        var attention = Value(await h.Attention.GetAttention(default));

        Assert.Empty(attention.LandingHeld!);
        Assert.Empty(attention.Reviews);
        Assert.Equal(0, attention.InReviewWithoutPullRequest);
    }

    [Fact]
    public async Task Attention_LeavesOutALandingWhoseBuildIsOfAnotherSha()
    {
        var h = await NewAsync();
        await LandedAsync(h, BuildVerdicts.Failed, sha: new string('3', 40));

        Assert.Empty(Value(await h.Attention.GetAttention(default)).LandingHeld!);
    }

    [Fact]
    public async Task Attention_CountsATrunkLandingWithoutPullRequestOnlyWhileItHasNoLanding()
    {
        var h = await NewAsync();
        await h.SetLandingAsync(Landings.Trunk);
        var issue = await h.FileAsync("story", "to land", h.Review);

        Assert.Equal(1, Value(await h.Attention.GetAttention(default)).InReviewWithoutPullRequest);

        await h.LandingAsync(issue, Landed);

        var attention = Value(await h.Attention.GetAttention(default));
        Assert.Equal(0, attention.InReviewWithoutPullRequest);
        Assert.Empty(attention.LandingHeld!);
    }

    [Fact]
    public async Task Attention_StillCountsAPullRequestProjectsIssueWithNoPullRequest()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "forgot hatch pr", h.Review);
        await h.LandingAsync(issue, Landed, containedBy: Landed);
        await h.TrunkAsync(BuildVerdicts.Failed);

        var attention = Value(await h.Attention.GetAttention(default));

        Assert.Equal(1, attention.InReviewWithoutPullRequest);
        Assert.Empty(attention.LandingHeld!);
    }

    [Fact]
    public async Task Attention_HasAnEmptyLandingHeldListWhenNothingIsHeld()
    {
        var h = await NewAsync();
        await h.FileAsync("story", "delivered", h.Review, pullRequestUrl: "https://forge.example/pulls/1");

        Assert.Empty(Value(await h.Attention.GetAttention(default)).LandingHeld!);
    }

    // ---- The deadlocked half (HA-1021) ----

    // A waits on B, B on its open child B1, B1 on A: the ring the work queue
    // marks deadlocked. Written straight to the table, since the controller's
    // ring check walks dependency edges only and the parent edge closes this one.
    private static async Task<(EfHatchIssue A, EfHatchIssue B, EfHatchIssue B1)> DeadlockedRingAsync(Harness h)
    {
        // Without playbooks every row folds "no playbook covers it" and no
        // dependency edge ever gates, so the ring needs the board's three.
        h.Db.AddRange(
            Playbook(h.Todo, h.InProgress, "epic", "opus"),
            Playbook(h.Todo, h.InProgress, "", "sonnet"),
            Playbook(h.InProgress, h.Review, "", "sonnet"));
        await h.Db.SaveChangesAsync();

        var a = await h.FileAsync("story", "story a", h.Todo, rank: 1024);
        var b = await h.FileAsync("story", "story b", h.InProgress, rank: 2048);
        var b1 = await h.FileAsync("task", "task b1", h.Todo, parentId: b.Id);
        await h.DependsAsync(a, b);
        await h.DependsAsync(b1, a);
        return (a, b, b1);
    }

    private static EfHatchPlaybook Playbook(int from, int to, string types, string model) => new()
    {
        FromStatusId = from,
        ToStatusId = to,
        Types = types,
        Shape = "any",
        Prompt = "do the thing",
        Model = model,
        Effort = "high",
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    [Fact]
    public async Task Attention_ListsEveryDeadlockedIssueInQueueOrder_WithTheQueuesSentence()
    {
        var h = await NewAsync();
        var (a, b, b1) = await DeadlockedRingAsync(h);
        await h.FileAsync("story", "bystander", h.Todo, rank: 4096);

        var deadlocked = Value(await h.Attention.GetAttention(default)).Deadlocked!;

        // The scan's order, not rank order: columns right to left, so B (in
        // progress) leads, then A and B1 in todo, A first on Id.
        Assert.Equal([Key(b), Key(a), Key(b1)], deadlocked.Select(d => d.Key));
        Assert.All(deadlocked, d => Assert.StartsWith("deadlocked - ", d.Sentence));
        Assert.Equal("story b", deadlocked[0].Title);
        Assert.Equal("story", deadlocked[0].Type);
    }

    [Fact]
    public async Task Attention_AnswersAnEmptyDeadlockedListWhenNothingIsDeadlocked()
    {
        var h = await NewAsync();
        await h.FileAsync("story", "alone", h.Todo);

        var deadlocked = Value(await h.Attention.GetAttention(default)).Deadlocked;

        Assert.NotNull(deadlocked);
        Assert.Empty(deadlocked);
    }

    [Fact]
    public async Task Attention_AnswersAnEmptyDeadlockedListOnABoardWithNoRoomForAReviewColumn()
    {
        var h = await OneColumnAsync();

        var deadlocked = Value(await h.Attention.GetAttention(default)).Deadlocked;

        Assert.NotNull(deadlocked);
        Assert.Empty(deadlocked);
    }

    // ---- The stalled merge train (HA-2094) ----

    private const string StalledPayload = """{"passedOver":"here:/one","elected":"here:/two"}""";

    private static async Task StallAsync(Harness h, EfHatchIssue head, string payload, DateTimeOffset at, string? driver = "here:/one")
    {
        (await h.Db.Projects.SingleAsync()).TrainStalledDriver = driver;
        h.Db.IssueEvents.Add(new EfHatchIssueEvent
        {
            IssueId = head.Id,
            Actor = "Hatch",
            Kind = EfHatchIssueEvent.DriverStalled,
            Payload = payload,
            At = at,
        });
        await h.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Attention_ListsAStalledTrainWithItsHeadAndBothRunners()
    {
        var h = await NewAsync();
        var head = await h.FileAsync("story", "head", h.Todo);
        await StallAsync(h, head, StalledPayload, Now);

        var stalled = Value(await h.Attention.GetAttention(default)).TrainStalled;

        Assert.Equal([new TrainStalledDto("AER", "Hatch", "here:/one", "here:/two", Key(head), Now)], stalled);
    }

    [Fact]
    public async Task Attention_ListsAStalledTrainWithNoElectedRunnerAsNull()
    {
        var h = await NewAsync();
        var head = await h.FileAsync("story", "head", h.Todo);
        await StallAsync(h, head, """{"passedOver":"here:/one","elected":null}""", Now);

        var row = Assert.Single(Value(await h.Attention.GetAttention(default)).TrainStalled!);

        Assert.Equal("here:/one", row.PassedOverRunner);
        Assert.Null(row.ElectedRunner);
    }

    [Fact]
    public async Task Attention_LeavesOutAProjectWhoseDriverWasReinstated()
    {
        var h = await NewAsync();
        var head = await h.FileAsync("story", "head", h.Todo);
        await StallAsync(h, head, StalledPayload, Now, driver: null);

        var stalled = Value(await h.Attention.GetAttention(default)).TrainStalled;

        Assert.NotNull(stalled);
        Assert.Empty(stalled);
    }

    [Fact]
    public async Task Attention_NamesTheHeadOfTheLaterStall()
    {
        var h = await NewAsync();
        var earlier = await h.FileAsync("story", "earlier head", h.Todo);
        var later = await h.FileAsync("story", "later head", h.Todo);
        await StallAsync(h, later, StalledPayload, Now);
        await StallAsync(h, earlier, StalledPayload, Now.AddHours(-1));

        var row = Assert.Single(Value(await h.Attention.GetAttention(default)).TrainStalled!);

        Assert.Equal(Key(later), row.HeadKey);
        Assert.Equal(Now, row.At);
    }

    [Fact]
    public async Task Attention_PassesOverAStallEventNamingAnotherDriver()
    {
        var h = await NewAsync();
        var old = await h.FileAsync("story", "old head", h.Todo);
        var head = await h.FileAsync("story", "head", h.Todo);
        await StallAsync(h, head, StalledPayload, Now.AddHours(-1));
        await StallAsync(h, old, """{"passedOver":"here:/three","elected":null}""", Now);

        var row = Assert.Single(Value(await h.Attention.GetAttention(default)).TrainStalled!);

        Assert.Equal(Key(head), row.HeadKey);
    }

    [Fact]
    public async Task Attention_LeavesOutAStalledProjectWithNoEventToNameAHead()
    {
        var h = await NewAsync();
        (await h.Db.Projects.SingleAsync()).TrainStalledDriver = "here:/one";
        await h.Db.SaveChangesAsync();

        var stalled = Value(await h.Attention.GetAttention(default)).TrainStalled;

        Assert.NotNull(stalled);
        Assert.Empty(stalled);
    }

    [Fact]
    public async Task Attention_AnswersAnEmptyTrainStalledListOnABoardWithNoRoomForAReviewColumn()
    {
        var h = await OneColumnAsync();

        var stalled = Value(await h.Attention.GetAttention(default)).TrainStalled;

        Assert.NotNull(stalled);
        Assert.Empty(stalled);
    }

    // ---- The merges column half (HA-466) ----

    [Fact]
    public async Task Attention_AnApprovedIssueWhoseBranchConflicts_IsHeldNotWaiting()
    {
        var h = await NewAsync();
        var approved = new EfHatchStatus { Name = "approved", SortOrder = 37, IsProtected = true, MergesPullRequest = true };
        h.Db.Add(approved);
        await h.Db.SaveChangesAsync();
        var issue = await h.FileAsync("story", "merging", approved.Id, pullRequestUrl: "https://forge.example/pulls/1");
        await h.CheckAsync(issue, MergeVerdicts.Conflicted, ["a.cs"]);

        var attention = Value(await h.Attention.GetAttention(default));

        var row = Assert.Single(attention.ApprovedHeld);
        Assert.Equal(Key(issue), row.Key);
        Assert.Contains("conflicts", row.Sentence);
        Assert.Empty(attention.ApprovedWaiting);
    }

    // ---- The gate ----

    [Fact]
    public void ReadingAttention_IsOpenToAKey()
    {
        var guard = typeof(AttentionController)
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .Single();

        // The same gate /board and /questions carry: it is the same house data,
        // read from the nav strip instead of from a page.
        Assert.True(guard.AcceptKey);
    }

    // ---- Backups ----

    [Fact]
    public async Task Attention_AnswersNoBackupRowWhenNoPathIsConfigured_EvenAfterAFailure()
    {
        var h = await NewAsync();
        h.Status.Record(new BackupAttempt(Now, BackupOutcome.Failed, "boom", null, null));
        h.Time.Advance(TimeSpan.FromHours(100));

        Assert.Null(Value(await h.Attention.GetAttention(default)).Backup);
    }

    [Fact]
    public async Task Attention_SaysWhyTheLastBackupFailed_WithTheNewestCopysOwnStamp()
    {
        var h = await NewAsync(_dir);
        var copy = Now.AddHours(-30);
        Seed(copy);
        h.Status.Record(new BackupAttempt(Now, BackupOutcome.Failed, "pg_dump: connection refused", null, null));

        var row = Value(await h.Attention.GetAttention(default)).Backup;

        Assert.NotNull(row);
        Assert.Equal("pg_dump: connection refused", row.Sentence);
        Assert.Equal(copy, row.LastSuccessfulAt);
    }

    [Fact]
    public async Task Attention_CallsACopyFiftyHoursOldStale()
    {
        var h = await NewAsync(_dir);
        h.Status.Record(new BackupAttempt(Now, BackupOutcome.Ok, null, "x", 1));
        var stale = Now.AddHours(-50);
        Seed(stale);

        var row = Value(await h.Attention.GetAttention(default)).Backup;

        Assert.Equal("no backup in the last 48 hours", row?.Sentence);
        Assert.Equal(stale, row?.LastSuccessfulAt);
    }

    [Fact]
    public async Task Attention_CallsACopyFortySevenHoursOldFine()
    {
        var h = await NewAsync(_dir);
        h.Status.Record(new BackupAttempt(Now, BackupOutcome.Ok, null, "x", 1));
        Seed(Now.AddHours(-47));

        Assert.Null(Value(await h.Attention.GetAttention(default)).Backup);
    }

    [Fact]
    public async Task Attention_GivesAProcessThatJustStartedFiveMinutesBeforeCallingAnEmptyFolderStale()
    {
        var h = await NewAsync(_dir);
        h.Time.Advance(TimeSpan.FromMinutes(1));

        Assert.Null(Value(await h.Attention.GetAttention(default)).Backup);

        h.Time.Advance(TimeSpan.FromMinutes(10));

        var row = Value(await h.Attention.GetAttention(default)).Backup;
        Assert.Equal("no backup in the last 48 hours", row?.Sentence);
        Assert.Null(row?.LastSuccessfulAt);
    }

    [Fact]
    public async Task Attention_SeesASuccessfulBackUpNowOnTheVeryNextRead()
    {
        var h = await NewAsync(_dir);
        h.Time.Advance(TimeSpan.FromMinutes(10));
        h.Process.ExitCode = 1;
        h.Process.Stderr = "boom";
        await h.Backup.RunAsync(default);

        // Lit by the failure - and the empty listing is now cached.
        Assert.Equal("boom", Value(await h.Attention.GetAttention(default)).Backup?.Sentence);

        h.Process.ExitCode = 0;
        h.Process.Stdout = [1];
        await h.Backup.RunAsync(default);

        // No clock advance: without Record clearing the cache this would still
        // read the empty listing, and say the 48-hours sentence.
        Assert.Null(Value(await h.Attention.GetAttention(default)).Backup);
    }

    [Fact]
    public async Task Attention_ReadsTheFolderOnceAMinute()
    {
        var h = await NewAsync(_dir);
        h.Time.Advance(TimeSpan.FromMinutes(10));
        var copy = Now.AddMinutes(10).AddHours(-47);
        Seed(copy);

        Assert.Null(Value(await h.Attention.GetAttention(default)).Backup);

        File.Delete(Path.Combine(_dir, BackupNames.Final(copy)));
        h.Time.Advance(TimeSpan.FromSeconds(30));

        Assert.Null(Value(await h.Attention.GetAttention(default)).Backup);

        h.Time.Advance(TimeSpan.FromSeconds(31));

        var row = Value(await h.Attention.GetAttention(default)).Backup;
        Assert.Equal("no backup in the last 48 hours", row?.Sentence);
        Assert.Null(row?.LastSuccessfulAt);
    }

    [Fact]
    public async Task Attention_CarriesTheBackupRowOnABoardWithNoReviewColumnToo()
    {
        var h = await OneColumnAsync(_dir);
        h.Status.Record(new BackupAttempt(Now, BackupOutcome.Failed, "boom", null, null));

        var attention = Value(await h.Attention.GetAttention(default));

        Assert.Empty(attention.Reviews);
        Assert.Equal("boom", attention.Backup?.Sentence);
    }

    private void Seed(DateTimeOffset at) =>
        File.WriteAllBytes(Path.Combine(_dir, BackupNames.Final(at)), [1]);

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required IAuthService Keys { get; init; }
        public required AttentionController Attention { get; init; }
        public required Microsoft.Extensions.Time.Testing.FakeTimeProvider Time { get; init; }
        public required BackupService Backup { get; init; }
        public required BackupStatus Status { get; init; }
        public required FakeBackupProcess Process { get; init; }
        public required int ProjectId { get; init; }
        public required int Inbox { get; init; }
        public required int Todo { get; init; }
        public required int InProgress { get; init; }
        public required int Review { get; init; }
        public required int Done { get; init; }

        private int next = 1;
        private int nextRepoOrder = 1;

        /// <summary>A runner row, placed directly - this is not RunnersController's own heartbeat rules to pin.</summary>
        public async Task RunnerAsync(
            string name, DateTimeOffset? exhaustedUntil, DateTimeOffset? lastSeenAt = null, string[]? models = null,
            string[]? has = null, string? agent = null, string state = "running")
        {
            Db.Runners.Add(new EfHatchRunner
            {
                Name = name,
                Kind = "loop",
                FirstSeenAt = Time.GetUtcNow(),
                LastSeenAt = lastSeenAt ?? Time.GetUtcNow(),
                State = state,
                Where = $"host:/checkouts/{name}",
                ExhaustedUntil = exhaustedUntil,
                Models = models is null ? null : string.Join('\n', models),
                Has = has is null ? null : string.Join('\n', has),
                Agent = agent,
            });
            await Db.SaveChangesAsync();
        }

        /// <summary>A live key with no beat of its own, minted through <see cref="Keys"/> - the identity a session is held against.</summary>
        public async Task<EfApiKey> AddKeyAsync(string name)
        {
            var created = await Keys.CreateApiKeyAsync(name, null, default);
            return created!.Key;
        }

        /// <summary>A live loop session for a key, placed directly - the session-carrying counterpart to <see cref="RunnerAsync"/>.</summary>
        public async Task AddSessionAsync(EfApiKey key, string? where = null, string? agent = null, bool standing = true)
        {
            Db.RunnerSessions.Add(new EfHatchRunnerSession
            {
                ApiKeyId = key.Id,
                Kind = EfHatchRunner.LoopKind,
                State = EfHatchRunner.Running,
                StartedAt = Time.GetUtcNow(),
                LastSeenAt = Time.GetUtcNow(),
                Where = where,
                Standing = standing,
                Agent = agent,
            });
            await Db.SaveChangesAsync();
        }

        /// <summary>The state of a repository's <c>.hatch/dependencies.json</c> on a trunk, written straight to the table.</summary>
        public async Task DependencyFileAsync(EfHatchProjectRepository repo, string trunk, params DeclaredDependency[] entries)
        {
            Db.DependencyFiles.Add(new EfHatchDependencyFile
            {
                Remote = repo.Remote,
                Canonical = repo.Canonical,
                Trunk = trunk,
                Sha = new string('a', 40),
                State = EfHatchDependencyFile.ParsedState,
                Entries = EfHatchDependencyFile.WriteEntries(entries),
                CheckedAt = Now,
                Runner = "host:/checkout",
                CheckedBy = "runner",
            });
            await Db.SaveChangesAsync();
        }

        /// <summary>A dependency edge, written straight to the table: <paramref name="issue"/> waits on <paramref name="blocker"/>.</summary>
        public async Task DependsAsync(EfHatchIssue issue, EfHatchIssue blocker)
        {
            Db.Dependencies.Add(new EfHatchIssueDependency
            {
                IssueId = issue.Id,
                DependsOnId = blocker.Id,
                CreatedBy = "hatch-agent",
                CreatedAt = Now,
            });

            await Db.SaveChangesAsync();
        }

        /// <summary>
        /// An issue placed directly. These tests are about which column an
        /// issue stands in and what it carries, so the column, the rank and the
        /// URL are the inputs; what the routes that write them refuse is
        /// <see cref="IssuesControllerTests"/>' business.
        /// </summary>
        public async Task<EfHatchIssue> FileAsync(
            string type, string title, int statusId, long rank = 1024, string? pullRequestUrl = null,
            long? parentId = null)
        {
            var issue = new EfHatchIssue
            {
                ProjectId = ProjectId,
                Number = next++,
                Type = type,
                Title = title,
                Description = "",
                StatusId = statusId,
                Rank = rank,
                PullRequestUrl = pullRequestUrl,
                ParentId = parentId,
                CreatedBy = "operator",
                CreatedAt = Now,
                UpdatedAt = Now,
            };

            Db.Issues.Add(issue);
            await Db.SaveChangesAsync();
            return issue;
        }

        /// <summary>
        /// A runner's verdict, written straight to the row: these tests are about
        /// who reads it, not about what the route refuses. Replaces any existing
        /// row for the same issue and repository, mirroring what
        /// <c>MergeCheckController</c> does for real, so a second call for the
        /// same issue and remote clears the first rather than leaving both.
        /// </summary>
        public async Task CheckAsync(
            EfHatchIssue issue, string verdict, string[]? files = null, string remote = "forge.example/owner/repo",
            string? branchSha = null, bool? holdsTrunk = null, string? playbookFilePath = null)
        {
            Db.MergeChecks.RemoveRange(
                Db.MergeChecks.Where(m => m.IssueId == issue.Id && m.Remote == remote));

            var hasBranch = verdict is MergeVerdicts.Clean or MergeVerdicts.Conflicted;

            Db.MergeChecks.Add(new EfHatchMergeCheck
            {
                IssueId = issue.Id,
                Remote = remote,
                Canonical = remote,
                Trunk = "main",
                TrunkSha = new string('1', 40),
                Verdict = verdict,
                Branch = hasBranch ? "ha-1-thing" : null,
                BranchSha = hasBranch ? branchSha ?? new string('2', 40) : null,
                HoldsTrunk = hasBranch ? holdsTrunk : null,
                PlaybookFilePath = hasBranch ? playbookFilePath : null,
                Files = EfHatchMergeCheck.JoinFiles(files ?? []),
                CheckedAt = Now,
                Runner = "box:/work/repo",
                CheckedBy = "runner",
            });

            await Db.SaveChangesAsync();
        }

        /// <summary>Replaces any existing row for the same issue and repository, for the same reason <see cref="CheckAsync"/> does.</summary>
        public async Task BuildAsync(
            EfHatchIssue issue, string verdict, string[]? failing = null, string remote = "forge.example/owner/repo",
            string? sha = null)
        {
            Db.BuildChecks.RemoveRange(
                Db.BuildChecks.Where(b => b.IssueId == issue.Id && b.Remote == remote));

            Db.BuildChecks.Add(new EfHatchBuildCheck
            {
                IssueId = issue.Id,
                Remote = remote,
                Canonical = remote,
                Branch = "ha-1-thing",
                Sha = sha ?? new string('2', 40),
                ShaSince = Now,
                Verdict = verdict,
                Failing = EfHatchBuildCheck.WriteFailing((failing ?? []).Select(n => new FailingCheckDto(n)).ToList()),
                CheckedAt = Now,
                Runner = "box:/work/repo",
                CheckedBy = "runner",
            });

            await Db.SaveChangesAsync();
        }

        /// <summary>A trunk verdict, written straight to the row - the same reason <see cref="BuildAsync"/> is.</summary>
        public async Task<EfHatchTrunkBuild> TrunkAsync(
            string verdict, string[]? failing = null, string remote = "forge.example/owner/repo", string trunk = "main",
            string? sha = null, long? bugIssueId = null)
        {
            var row = new EfHatchTrunkBuild
            {
                Remote = remote,
                Canonical = remote,
                Trunk = trunk,
                Sha = sha ?? new string('2', 40),
                ShaSince = Now,
                Verdict = verdict,
                Failing = EfHatchBuildCheck.WriteFailing((failing ?? []).Select(n => new FailingCheckDto(n)).ToList()),
                CheckedAt = Now,
                Runner = "box:/work/repo",
                CheckedBy = "runner",
                BugIssueId = bugIssueId,
            };

            Db.TrunkBuilds.Add(row);
            await Db.SaveChangesAsync();
            return row;
        }

        /// <summary>
        /// A project's landing policy and check requirement, written straight
        /// onto the row - what the write route accepts is a separate task's
        /// business.
        /// </summary>
        public async Task SetLandingAsync(string? landing = null, bool? checksRequired = null)
        {
            var project = await Db.Projects.SingleAsync(p => p.Id == ProjectId);
            if (landing is not null) project.Landing = landing;
            if (checksRequired is not null) project.ChecksRequired = checksRequired.Value;
            await Db.SaveChangesAsync();
        }

        /// <summary>
        /// A landing a runner recorded, written straight to the table, with the
        /// canonical the raw remote - as <see cref="TrunkAsync"/> writes it - so
        /// the two join. Contained by <paramref name="containedBy"/> when given,
        /// and unseen otherwise.
        /// </summary>
        public async Task LandingAsync(
            EfHatchIssue issue, string sha, string? containedBy = null,
            string remote = "forge.example/owner/repo", string trunk = "main")
        {
            Db.Landings.Add(new EfHatchLanding
            {
                IssueId = issue.Id,
                Remote = remote,
                Canonical = remote,
                Trunk = trunk,
                Sha = sha,
                ContainedBy = containedBy,
                RecordedAt = Now,
                CheckedAt = containedBy is null ? null : Now,
                RecordedBy = "runner",
            });

            await Db.SaveChangesAsync();
        }

        /// <summary>
        /// A remote bound to the project, written straight to the table in the
        /// order it is called - every issue <see cref="FileAsync"/> files lands
        /// in the one shared project, so every test binds against the same
        /// list. What the route that writes these accepts and refuses is
        /// <c>ProjectsControllerTests</c>'s business.
        /// </summary>
        public async Task<EfHatchProjectRepository> BindRepositoryAsync(string remote, string? baseBranch = null)
        {
            var (canonical, _) = RemoteIdentity.Canonical(remote);
            var repo = new EfHatchProjectRepository
            {
                ProjectId = ProjectId,
                Remote = remote,
                Canonical = canonical!,
                BaseBranch = baseBranch,
                SortOrder = nextRepoOrder++,
                CreatedAt = Now,
            };

            Db.ProjectRepositories.Add(repo);
            await Db.SaveChangesAsync();
            return repo;
        }

        /// <summary>What an operator does on the Statuses page, written straight to the row.</summary>
        public async Task RenameAsync(int statusId, string name)
        {
            (await Db.Statuses.SingleAsync(s => s.Id == statusId)).Name = name;
            await Db.SaveChangesAsync();
        }

        /// <summary>The other thing they do there: dragging a column somewhere else.</summary>
        public async Task ReorderAsync(int statusId, int sortOrder)
        {
            (await Db.Statuses.SingleAsync(s => s.Id == statusId)).SortOrder = sortOrder;
            await Db.SaveChangesAsync();
        }

        /// <summary>A deferred column dropped into the board at a given position.</summary>
        public async Task<int> ShelfAsync(int sortOrder)
        {
            var shelf = new EfHatchStatus { Name = "shelved", SortOrder = sortOrder, IsDeferred = true };
            Db.Statuses.Add(shelf);
            await Db.SaveChangesAsync();
            return shelf.Id;
        }

        public async Task<EfHatchComment> AskAsync(EfHatchIssue issue, string body, DateTimeOffset? at = null)
        {
            var comment = new EfHatchComment
            {
                IssueId = issue.Id,
                Author = "hatch-agent",
                Body = body,
                Kind = EfHatchComment.Question,
                CreatedAt = at ?? Now,
            };

            Db.Comments.Add(comment);
            await Db.SaveChangesAsync();
            return comment;
        }

        public async Task AnswerAsync(EfHatchIssue issue, EfHatchComment question, string body)
        {
            Db.Comments.Add(new EfHatchComment
            {
                IssueId = issue.Id,
                Author = "operator",
                Body = body,
                Kind = EfHatchComment.Answer,
                AnswersId = question.Id,
                CreatedAt = Now,
            });

            await Db.SaveChangesAsync();
        }
    }

    /// <summary>A board with the shape the Playbooks migration leaves behind.</summary>
    private static async Task<Harness> NewAsync(string backupPath = "")
    {
        var db = NewDb();

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var inbox = new EfHatchStatus { Name = "inbox", SortOrder = 10 };
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        var doing = new EfHatchStatus { Name = "in progress", SortOrder = 30 };
        var review = new EfHatchStatus { Name = "review", SortOrder = 35 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 40, IsTerminal = true };
        db.AddRange(project, inbox, todo, doing, review, done);
        await db.SaveChangesAsync();

        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Now);
        var runners = new Runners(Microsoft.Extensions.Options.Options.Create(new HatchOptions()), Microsoft.Extensions.Options.Options.Create(new global::Hatch.Api.Services.Auth.AuthOptions()), new StubHatchRevision());
        var coreDb = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var keys = new AuthService(
            coreDb, Microsoft.Extensions.Options.Options.Create(new global::Hatch.Api.Services.Auth.AuthOptions()), time, NullLogger<AuthService>.Instance);
        var sessions = new RunnerSessions(runners);

        var (backup, status, process) = NewBackup(time, backupPath);

        return new Harness
        {
            Db = db,
            Keys = keys,
            Attention = new AttentionController(db, runners, time, new Dispatch(db, new StubActorDirectory(), TestClaims.With(), time, keys, sessions), backup),
            Time = time,
            Backup = backup,
            Status = status,
            Process = process,
            ProjectId = project.Id,
            Inbox = inbox.Id,
            Todo = todo.Id,
            InProgress = doing.Id,
            Review = review.Id,
            Done = done.Id,
        };
    }

    /// <summary>
    /// A board too short to have a review column at all: one terminal column,
    /// with nothing to its left. Not a board anybody would keep, but it is one
    /// the Statuses page can be left in halfway through building a new one.
    /// </summary>
    private static async Task<Harness> OneColumnAsync(string backupPath = "")
    {
        var db = NewDb();

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var done = new EfHatchStatus { Name = "done", SortOrder = 10, IsTerminal = true };
        db.AddRange(project, done);
        await db.SaveChangesAsync();

        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Now);
        var runners = new Runners(Microsoft.Extensions.Options.Options.Create(new HatchOptions()), Microsoft.Extensions.Options.Options.Create(new global::Hatch.Api.Services.Auth.AuthOptions()), new StubHatchRevision());
        var coreDb = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var keys = new AuthService(
            coreDb, Microsoft.Extensions.Options.Options.Create(new global::Hatch.Api.Services.Auth.AuthOptions()), time, NullLogger<AuthService>.Instance);
        var sessions = new RunnerSessions(runners);

        var (backup, status, process) = NewBackup(time, backupPath);

        return new Harness
        {
            Db = db,
            Keys = keys,
            Attention = new AttentionController(db, runners, time, new Dispatch(db, new StubActorDirectory(), TestClaims.With(), time, keys, sessions), backup),
            Time = time,
            Backup = backup,
            Status = status,
            Process = process,
            ProjectId = project.Id,
            Inbox = done.Id,
            Todo = done.Id,
            InProgress = done.Id,
            Review = done.Id,
            Done = done.Id,
        };
    }

    /// <summary>
    /// A <see cref="BackupService"/> wired as BackupControllerTests wires it:
    /// the harness's own clock, and a fake process that writes nothing real.
    /// </summary>
    private static (BackupService Service, BackupStatus Status, FakeBackupProcess Process) NewBackup(
        Microsoft.Extensions.Time.Testing.FakeTimeProvider time, string path)
    {
        var status = new BackupStatus(time);
        var process = new FakeBackupProcess();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Hatch"] = "Host=db-test;Port=5433;Username=hatchuser;Password=s3cret;Database=hatchdb",
            })
            .Build();
        var service = new BackupService(
            Microsoft.Extensions.Options.Options.Create(new HatchOptions { BackupPath = path }),
            configuration, process, status, time, NullLogger<BackupService>.Instance);

        return (service, status, process);
    }

    private sealed class FakeBackupProcess : IBackupProcess
    {
        public byte[] Stdout { get; set; } = [];
        public int ExitCode { get; set; }
        public string Stderr { get; set; } = "";

        public async Task<BackupProcessResult> RunAsync(BackupProcessStart start, Stream stdout, CancellationToken ct)
        {
            await stdout.WriteAsync(Stdout, ct);
            return new BackupProcessResult(ExitCode, Stderr);
        }
    }

    private static HatchContext NewDb() => new(
        new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    /// <summary>The display key of an issue these tests filed directly.</summary>
    private static string Key(EfHatchIssue issue) => IssueKey.Format("AER", issue.Number);

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result?.GetType().Name ?? "nothing"}");
}

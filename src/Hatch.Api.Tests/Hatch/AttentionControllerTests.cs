using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
public class AttentionControllerTests
{
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
    public void ReadingAttention_IsOpenToAHatchScopedKey()
    {
        var guard = typeof(AttentionController)
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .Single();

        // The same gate /board and /questions carry: it is the same house data,
        // read from the nav strip instead of from a page.
        Assert.Equal(ApiKeyScopes.Hatch, guard.AcceptScope);
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required AttentionController Attention { get; init; }
        public required Microsoft.Extensions.Time.Testing.FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }
        public required int Inbox { get; init; }
        public required int Todo { get; init; }
        public required int InProgress { get; init; }
        public required int Review { get; init; }
        public required int Done { get; init; }

        private int next = 1;
        private int nextRepoOrder = 1;

        /// <summary>A runner row, placed directly - this is not RunnersController's own heartbeat rules to pin.</summary>
        public async Task RunnerAsync(string name, DateTimeOffset? exhaustedUntil, DateTimeOffset? lastSeenAt = null)
        {
            Db.Runners.Add(new EfHatchRunner
            {
                Name = name,
                Kind = "loop",
                FirstSeenAt = Time.GetUtcNow(),
                LastSeenAt = lastSeenAt ?? Time.GetUtcNow(),
                State = "running",
                Where = $"host:/checkouts/{name}",
                ExhaustedUntil = exhaustedUntil,
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
    private static async Task<Harness> NewAsync()
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
        var runners = new Runners(Microsoft.Extensions.Options.Options.Create(new HatchOptions()), Microsoft.Extensions.Options.Options.Create(new global::Hatch.Api.Services.Auth.AuthOptions()));

        return new Harness
        {
            Db = db,
            Attention = new AttentionController(db, runners, time),
            Time = time,
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
    private static async Task<Harness> OneColumnAsync()
    {
        var db = NewDb();

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var done = new EfHatchStatus { Name = "done", SortOrder = 10, IsTerminal = true };
        db.AddRange(project, done);
        await db.SaveChangesAsync();

        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Now);
        var runners = new Runners(Microsoft.Extensions.Options.Options.Create(new HatchOptions()), Microsoft.Extensions.Options.Options.Create(new global::Hatch.Api.Services.Auth.AuthOptions()));

        return new Harness
        {
            Db = db,
            Attention = new AttentionController(db, runners, time),
            Time = time,
            ProjectId = project.Id,
            Inbox = done.Id,
            Todo = done.Id,
            InProgress = done.Id,
            Review = done.Id,
            Done = done.Id,
        };
    }

    private static HatchContext NewDb() => new(
        new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    /// <summary>The display key of an issue these tests filed directly.</summary>
    private static string Key(EfHatchIssue issue) => IssueKey.Format("AER", issue.Number);

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result?.GetType().Name ?? "nothing"}");
}

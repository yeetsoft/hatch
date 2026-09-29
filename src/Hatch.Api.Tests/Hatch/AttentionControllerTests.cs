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
            string type, string title, int statusId, long rank = 1024, string? pullRequestUrl = null)
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
            string? branchSha = null, bool? holdsTrunk = null)
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
        var runners = new Runners(Microsoft.Extensions.Options.Options.Create(new HatchOptions()));

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
        var runners = new Runners(Microsoft.Extensions.Options.Options.Create(new HatchOptions()));

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
        };
    }

    private static HatchContext NewDb() => new(
        new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    /// <summary>The display key of an issue these tests filed directly.</summary>
    private static string Key(EfHatchIssue issue) => IssueKey.Format("AER", issue.Number);

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result?.GetType().Name ?? "nothing"}");
}

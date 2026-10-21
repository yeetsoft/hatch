using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="MergeWork.Judge"/> measured off hand-built rows - no
/// <see cref="HatchContext"/> needed, the way <see cref="ColumnsTests"/>
/// tests a static helper with no rows of its own. There is no dedicated test
/// file for <see cref="ReviewWork.Judge"/> either; this one exists because
/// <see cref="MergeWork"/> is not yet wired into <c>WorkController</c>, which
/// is where <c>ReviewWork</c>'s own coverage lives.
/// </summary>
public class MergeWorkTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    static EfHatchProjectRepository Repo(string remote = "https://example.com/o/r", int sortOrder = 0) =>
        new() { Remote = remote, Canonical = RemoteIdentity.Canonical(remote).Item1!, SortOrder = sortOrder, CreatedAt = Now };

    static EfHatchMergeCheck Merge(string remote, string verdict, bool? holdsTrunk = null, string trunk = "main", string? playbookFilePath = null) => new()
    {
        Remote = remote, Canonical = RemoteIdentity.Canonical(remote).Item1!, Trunk = trunk, TrunkSha = new string('a', 40),
        Verdict = verdict,
        Branch = verdict is MergeVerdicts.Clean or MergeVerdicts.Conflicted ? "aer-1-thing" : null,
        BranchSha = verdict is MergeVerdicts.Clean or MergeVerdicts.Conflicted ? new string('b', 40) : null,
        HoldsTrunk = holdsTrunk, PlaybookFilePath = playbookFilePath, CheckedAt = Now, Runner = "host:/checkout", CheckedBy = "runner",
    };

    static EfHatchBuildCheck Build(string remote, string verdict, string? sha = null) => new()
    {
        Remote = remote, Canonical = RemoteIdentity.Canonical(remote).Item1!, Branch = "aer-1-thing",
        Sha = sha ?? new string('b', 40), ShaSince = Now, Verdict = verdict,
        Failing = EfHatchBuildCheck.WriteFailing([]), CheckedAt = Now, Runner = "host:/checkout", CheckedBy = "runner",
    };

    const string Remote = "https://example.com/o/r";
    static readonly string OtherHead = new('c', 40);

    [Fact]
    public void ClosedPullRequest_HoldsAheadOfAConflict()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Conflicted) };

        var judgement = MergeWork.Judge(bound, merges, [], Now, true, null, null, Now);

        Assert.Equal(WorkKinds.Hold, judgement.Kind);
        Assert.Contains("closed without merging", judgement.Sentence);
    }

    [Fact]
    public void NoMergeCheck_PendingOnTheTrunkName()
    {
        var bound = new[] { Repo(Remote) };

        var judgement = MergeWork.Judge(bound, [], [], null, true, null, null, Now);

        Assert.Equal(WorkKinds.Pending, judgement.Kind);
        Assert.Contains("the trunk", judgement.Sentence);
    }

    [Fact]
    public void AnyConflicted_Pushback()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Conflicted) };

        var judgement = MergeWork.Judge(bound, merges, [], null, true, null, null, Now);

        Assert.Equal(WorkKinds.Pushback, judgement.Kind);
        Assert.Contains("conflicts", judgement.Sentence);
        Assert.Contains("review", judgement.Sentence);
    }

    [Fact]
    public void AnyConflicted_PushbackNamesTheGivenReviewColumn()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Conflicted) };

        var judgement = MergeWork.Judge(bound, merges, [], null, true, null, null, Now, reviewColumn: "code review");

        Assert.Equal(WorkKinds.Pushback, judgement.Kind);
        Assert.Contains("code review", judgement.Sentence);
    }

    [Fact]
    public void AnyAmbiguous_Holds()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Ambiguous) };

        var judgement = MergeWork.Judge(bound, merges, [], null, true, null, null, Now);

        Assert.Equal(WorkKinds.Hold, judgement.Kind);
        Assert.Contains("more than one branch", judgement.Sentence);
    }

    [Fact]
    public void AllNone_Holds()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.None) };

        var judgement = MergeWork.Judge(bound, merges, [], null, true, null, null, Now);

        Assert.Equal(WorkKinds.Hold, judgement.Kind);
        Assert.Contains("no branch", judgement.Sentence);
    }

    [Fact]
    public void CleanBuildFailed_PushbackRegardlessOfHoldsTrunk()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true) };
        var builds = new[] { Build(Remote, BuildVerdicts.Failed) };

        var judgement = MergeWork.Judge(bound, merges, builds, null, true, null, null, Now);

        Assert.Equal(WorkKinds.Pushback, judgement.Kind);
        Assert.Contains("failed", judgement.Sentence);
        Assert.Contains("review", judgement.Sentence);
    }

    [Fact]
    public void CleanNotHoldingTrunk_Updates()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: false) };

        var judgement = MergeWork.Judge(bound, merges, [], null, true, null, null, Now);

        Assert.Equal(WorkKinds.Update, judgement.Kind);
        Assert.Contains("bringing its branch current from", judgement.Sentence);
    }

    [Fact]
    public void TwoRepositories_OneNotHoldingTrunk_Updates()
    {
        const string remoteB = "https://example.com/o/s";
        var bound = new[] { Repo(Remote, 0), Repo(remoteB, 1) };
        var merges = new[]
        {
            Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true),
            Merge(remoteB, MergeVerdicts.Clean, holdsTrunk: false),
        };
        var builds = new[] { Build(Remote, BuildVerdicts.Passed) };

        var judgement = MergeWork.Judge(bound, merges, builds, null, true, null, null, Now);

        Assert.Equal(WorkKinds.Update, judgement.Kind);
    }

    [Fact]
    public void CleanHoldsTrunkBuildPending_Pending()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true) };
        var builds = new[] { Build(Remote, BuildVerdicts.Pending) };

        var judgement = MergeWork.Judge(bound, merges, builds, null, true, null, null, Now);

        Assert.Equal(WorkKinds.Pending, judgement.Kind);
        Assert.Contains("still running", judgement.Sentence);
    }

    [Fact]
    public void CleanHoldsTrunkStaleBuild_Pending()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true) };
        var builds = new[] { Build(Remote, BuildVerdicts.Passed, sha: new string('c', 40)) };

        var judgement = MergeWork.Judge(bound, merges, builds, null, true, null, null, Now);

        Assert.Equal(WorkKinds.Pending, judgement.Kind);
        Assert.Contains("no runner has read its build on", judgement.Sentence);
    }

    [Fact]
    public void CleanHoldsTrunkNoBuildAtAll_Pending()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true) };

        var judgement = MergeWork.Judge(bound, merges, [], null, true, null, null, Now);

        Assert.Equal(WorkKinds.Pending, judgement.Kind);
        Assert.Contains("no checks have run on it yet", judgement.Sentence);
    }

    [Fact]
    public void CleanHoldsTrunkBuildPassed_Merges()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true) };
        var builds = new[] { Build(Remote, BuildVerdicts.Passed) };

        var judgement = MergeWork.Judge(bound, merges, builds, null, true, null, null, Now);

        Assert.Equal(WorkKinds.Merge, judgement.Kind);
        Assert.Contains("its build passes", judgement.Sentence);
    }

    [Fact]
    public void CleanHoldsTrunkBuildPassedPlaybookFlagged_Holds()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true, playbookFilePath: ".hatch/playbooks.yaml") };
        var builds = new[] { Build(Remote, BuildVerdicts.Passed) };

        var judgement = MergeWork.Judge(bound, merges, builds, null, true, null, null, Now);

        Assert.Equal(WorkKinds.Hold, judgement.Kind);
        Assert.Contains(".hatch/playbooks.yaml", judgement.Sentence);
        Assert.Contains("a person merges it on the forge after reading it", judgement.Sentence);
    }

    [Fact]
    public void CleanHoldsTrunkBuildNone_PendingUnderTheSettleWindow()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true) };
        var builds = new[] { Build(Remote, BuildVerdicts.None) };

        var judgement = MergeWork.Judge(bound, merges, builds, null, true, null, null, Now.AddMinutes(10).AddSeconds(-1));

        Assert.Equal(WorkKinds.Pending, judgement.Kind);
        Assert.Contains("no checks ran on it", judgement.Sentence);
    }

    [Fact]
    public void CleanHoldsTrunkBuildNone_HoldsAtTheSettleWindow()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true) };
        var builds = new[] { Build(Remote, BuildVerdicts.None) };

        var judgement = MergeWork.Judge(bound, merges, builds, null, true, null, null, Now.AddMinutes(10));

        Assert.Equal(WorkKinds.Hold, judgement.Kind);
        Assert.Contains("no checks ran on it", judgement.Sentence);
    }

    static MergeWork.Judgement CleanNoChecksAt(
        TimeSpan after, bool checksRequired, string? mergeState, string? mergeStateSha, DateTimeOffset? pullRequestHeadSince = null)
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true) };
        var builds = new[] { Build(Remote, BuildVerdicts.None) };

        return MergeWork.Judge(
            bound, merges, builds, null, checksRequired, mergeState, mergeStateSha, Now + after,
            pullRequestHeadSince: pullRequestHeadSince);
    }

    [Fact]
    public void DraftPullRequest_HoldsAheadOfAConflict()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Conflicted) };

        var judgement = MergeWork.Judge(bound, merges, [], null, true, MergeStates.Draft, null, Now);

        Assert.Equal(WorkKinds.Hold, judgement.Kind);
        Assert.Equal("its pull request is a draft", judgement.Sentence);
    }

    [Fact]
    public void ClosedPullRequest_HoldsAheadOfADraft()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true) };

        var judgement = MergeWork.Judge(bound, merges, [], Now, true, MergeStates.Draft, null, Now);

        Assert.Equal(WorkKinds.Hold, judgement.Kind);
        Assert.Contains("closed without merging", judgement.Sentence);
    }

    [Fact]
    public void NoChecksRequired_MergesAtTheSettleWindow()
    {
        var judgement = CleanNoChecksAt(TimeSpan.FromMinutes(10), checksRequired: false, null, null);

        Assert.Equal(WorkKinds.Merge, judgement.Kind);
        Assert.Contains("requires no checks", judgement.Sentence);
    }

    [Fact]
    public void NoChecksRequired_PendingUnderTheSettleWindow()
    {
        var judgement = CleanNoChecksAt(TimeSpan.FromMinutes(9), checksRequired: false, null, null);

        Assert.Equal(WorkKinds.Pending, judgement.Kind);
    }

    [Fact]
    public void ForgeCleanAtTheJudgedSha_Merges()
    {
        var judgement = CleanNoChecksAt(TimeSpan.FromMinutes(10), true, MergeStates.Clean, new string('b', 40));

        Assert.Equal(WorkKinds.Merge, judgement.Kind);
        Assert.Contains("nothing is required of it", judgement.Sentence);
    }

    [Fact]
    public void ForgeCleanAtAnotherSha_WaitsForTheForgeToCatchUp()
    {
        var judgement = CleanNoChecksAt(TimeSpan.FromMinutes(10), true, MergeStates.Clean, OtherHead);

        Assert.Equal(WorkKinds.Pending, judgement.Kind);
        Assert.Contains("waiting for the forge to catch up with its branch", judgement.Sentence);
    }

    [Fact]
    public void ForgeCleanAtAnotherSha_HoldsPastTheForgeCatchUpWindow()
    {
        var judgement = CleanNoChecksAt(TimeSpan.FromMinutes(15), true, MergeStates.Clean, OtherHead);

        Assert.Equal(WorkKinds.Hold, judgement.Kind);
        Assert.Contains("has not caught up with its branch in 15 minutes", judgement.Sentence);
    }

    [Fact]
    public void ForgeBlockedAtTheJudgedSha_HoldsWithTheBlockedSentence()
    {
        var judgement = CleanNoChecksAt(TimeSpan.FromMinutes(10), true, MergeStates.Blocked, new string('b', 40));

        Assert.Equal(WorkKinds.Hold, judgement.Kind);
        Assert.Contains("blocked by the repository's rules", judgement.Sentence);
    }

    [Theory]
    [InlineData(MergeStates.Unstable)]
    [InlineData(null)]
    public void ForgeStateThatSaysNothingUseful_HoldsAsItAlwaysHas(string? state)
    {
        var judgement = CleanNoChecksAt(TimeSpan.FromMinutes(10), true, state, new string('b', 40));

        Assert.Equal(WorkKinds.Hold, judgement.Kind);
        Assert.EndsWith("no checks ran on it", judgement.Sentence);
    }

    [Fact]
    public void FailedBuild_StaysPushbackWhateverTheForgeAndTheProjectSay()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true) };
        var builds = new[] { Build(Remote, BuildVerdicts.Failed) };

        var judgement = MergeWork.Judge(bound, merges, builds, null, false, MergeStates.Clean, new string('b', 40), Now);

        Assert.Equal(WorkKinds.Pushback, judgement.Kind);
    }

    [Fact]
    public void PassedBuild_MergesWithNoForgeState()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true) };
        var builds = new[] { Build(Remote, BuildVerdicts.Passed) };

        var judgement = MergeWork.Judge(bound, merges, builds, null, true, null, null, Now);

        Assert.Equal(WorkKinds.Merge, judgement.Kind);
        Assert.Contains("its build passes", judgement.Sentence);
    }

    [Fact]
    public void OtherHead_TipBuildNone_PendingNamesBothShas()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true) };
        var builds = new[] { Build(Remote, BuildVerdicts.None) };

        var judgement = MergeWork.Judge(bound, merges, builds, null, true, null, OtherHead, Now);

        Assert.Equal(WorkKinds.Pending, judgement.Kind);
        Assert.Contains("ccccccc", judgement.Sentence);
        Assert.Contains("bbbbbbb", judgement.Sentence);
        Assert.DoesNotContain("no checks ran", judgement.Sentence);
    }

    [Fact]
    public void OtherHead_TipBuildPassed_PendingNotMerge()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true) };
        var builds = new[] { Build(Remote, BuildVerdicts.Passed) };

        var judgement = MergeWork.Judge(bound, merges, builds, null, true, null, OtherHead, Now);

        Assert.Equal(WorkKinds.Pending, judgement.Kind);
        Assert.Contains("waiting for the forge to catch up with its branch", judgement.Sentence);
    }

    [Fact]
    public void OtherHead_TipBuildNoneAt16Minutes_Holds()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true) };
        var builds = new[] { Build(Remote, BuildVerdicts.None) };

        var judgement = MergeWork.Judge(bound, merges, builds, null, true, null, OtherHead, Now.AddMinutes(16));

        Assert.Equal(WorkKinds.Hold, judgement.Kind);
        Assert.Contains("has not caught up with its branch in 15 minutes", judgement.Sentence);
    }

    [Fact]
    public void OtherHead_TipBuildNoneAt14Minutes_StillPending()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true) };
        var builds = new[] { Build(Remote, BuildVerdicts.None) };

        var judgement = MergeWork.Judge(bound, merges, builds, null, true, null, OtherHead, Now.AddMinutes(14));

        Assert.Equal(WorkKinds.Pending, judgement.Kind);
        Assert.Contains("waiting for the forge to catch up with its branch", judgement.Sentence);
    }

    [Fact]
    public void OtherHead_NotHoldingTrunk_PendingNotUpdate()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: false) };

        var judgement = MergeWork.Judge(bound, merges, [], null, true, null, OtherHead, Now);

        Assert.Equal(WorkKinds.Pending, judgement.Kind);
        Assert.Contains("waiting for the forge to catch up with its branch", judgement.Sentence);
    }

    [Fact]
    public void OtherHead_NoBuildChecksAtAllAt20Minutes_PendingWithNoClock()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true) };

        var judgement = MergeWork.Judge(bound, merges, [], null, true, null, OtherHead, Now.AddMinutes(20));

        Assert.Equal(WorkKinds.Pending, judgement.Kind);
        Assert.Contains("waiting for the forge to catch up with its branch", judgement.Sentence);
    }

    [Fact]
    public void OtherHead_TipBuildFailed_StillPushback()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true) };
        var builds = new[] { Build(Remote, BuildVerdicts.Failed) };

        var judgement = MergeWork.Judge(bound, merges, builds, null, true, null, OtherHead, Now);

        Assert.Equal(WorkKinds.Pushback, judgement.Kind);
    }

    // The #983 timeline: the forge catches up with the tip 11 minutes after the
    // build was first heard of, so the settle window counts from there, not
    // from the build's own ShaSince.
    [Fact]
    public void SettleWindow_CountsFromTheForgeCatchingUp_PendingAt12Minutes()
    {
        var judgement = CleanNoChecksAt(
            TimeSpan.FromMinutes(12), true, null, new string('b', 40), pullRequestHeadSince: Now.AddMinutes(11));

        Assert.Equal(WorkKinds.Pending, judgement.Kind);
        Assert.Contains("no checks ran on it", judgement.Sentence);
    }

    [Fact]
    public void SettleWindow_CountsFromTheForgeCatchingUp_PendingAt20Minutes()
    {
        var judgement = CleanNoChecksAt(
            TimeSpan.FromMinutes(20), true, null, new string('b', 40), pullRequestHeadSince: Now.AddMinutes(11));

        Assert.Equal(WorkKinds.Pending, judgement.Kind);
    }

    [Fact]
    public void SettleWindow_CountsFromTheForgeCatchingUp_HoldsAt22Minutes()
    {
        var judgement = CleanNoChecksAt(
            TimeSpan.FromMinutes(22), true, null, new string('b', 40), pullRequestHeadSince: Now.AddMinutes(11));

        Assert.Equal(WorkKinds.Hold, judgement.Kind);
    }

    [Fact]
    public void MatchingShaWithNoPullRequestHeadSince_HoldsAtTheSettleWindow()
    {
        var judgement = CleanNoChecksAt(TimeSpan.FromMinutes(10), true, null, new string('b', 40));

        Assert.Equal(WorkKinds.Hold, judgement.Kind);
        Assert.EndsWith("no checks ran on it", judgement.Sentence);
    }

    [Fact]
    public void NullMergeStateShaWithPullRequestHeadSince_IsIgnored_HoldsAtTheSettleWindow()
    {
        var judgement = CleanNoChecksAt(
            TimeSpan.FromMinutes(10), true, null, null, pullRequestHeadSince: Now.AddMinutes(11));

        Assert.Equal(WorkKinds.Hold, judgement.Kind);
        Assert.EndsWith("no checks ran on it", judgement.Sentence);
    }

    [Fact]
    public void PullRequestHeadSinceEarlierThanShaSince_CountsFromShaSince()
    {
        var judgement = CleanNoChecksAt(
            TimeSpan.FromMinutes(10), true, null, new string('b', 40), pullRequestHeadSince: Now.AddMinutes(-5));

        Assert.Equal(WorkKinds.Hold, judgement.Kind);
        Assert.EndsWith("no checks ran on it", judgement.Sentence);
    }
}

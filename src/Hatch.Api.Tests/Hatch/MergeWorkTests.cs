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

    static EfHatchMergeCheck Merge(string remote, string verdict, bool? holdsTrunk = null, string trunk = "main") => new()
    {
        Remote = remote, Canonical = RemoteIdentity.Canonical(remote).Item1!, Trunk = trunk, TrunkSha = new string('a', 40),
        Verdict = verdict,
        Branch = verdict is MergeVerdicts.Clean or MergeVerdicts.Conflicted ? "aer-1-thing" : null,
        BranchSha = verdict is MergeVerdicts.Clean or MergeVerdicts.Conflicted ? new string('b', 40) : null,
        HoldsTrunk = holdsTrunk, CheckedAt = Now, Runner = "host:/checkout", CheckedBy = "runner",
    };

    static EfHatchBuildCheck Build(string remote, string verdict, string? sha = null) => new()
    {
        Remote = remote, Canonical = RemoteIdentity.Canonical(remote).Item1!, Branch = "aer-1-thing",
        Sha = sha ?? new string('b', 40), ShaSince = Now, Verdict = verdict,
        Failing = EfHatchBuildCheck.WriteFailing([]), CheckedAt = Now, Runner = "host:/checkout", CheckedBy = "runner",
    };

    const string Remote = "https://example.com/o/r";

    [Fact]
    public void ClosedPullRequest_HoldsAheadOfAConflict()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Conflicted) };

        var judgement = MergeWork.Judge(bound, merges, [], Now, Now);

        Assert.Equal(WorkKinds.Hold, judgement.Kind);
        Assert.Contains("closed without merging", judgement.Sentence);
    }

    [Fact]
    public void NoMergeCheck_PendingOnTheTrunkName()
    {
        var bound = new[] { Repo(Remote) };

        var judgement = MergeWork.Judge(bound, [], [], null, Now);

        Assert.Equal(WorkKinds.Pending, judgement.Kind);
        Assert.Contains("the trunk", judgement.Sentence);
    }

    [Fact]
    public void AnyConflicted_Pushback()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Conflicted) };

        var judgement = MergeWork.Judge(bound, merges, [], null, Now);

        Assert.Equal(WorkKinds.Pushback, judgement.Kind);
        Assert.Contains("conflicts", judgement.Sentence);
        Assert.Contains("review", judgement.Sentence);
    }

    [Fact]
    public void AnyConflicted_PushbackNamesTheGivenReviewColumn()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Conflicted) };

        var judgement = MergeWork.Judge(bound, merges, [], null, Now, reviewColumn: "code review");

        Assert.Equal(WorkKinds.Pushback, judgement.Kind);
        Assert.Contains("code review", judgement.Sentence);
    }

    [Fact]
    public void AnyAmbiguous_Holds()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Ambiguous) };

        var judgement = MergeWork.Judge(bound, merges, [], null, Now);

        Assert.Equal(WorkKinds.Hold, judgement.Kind);
        Assert.Contains("more than one branch", judgement.Sentence);
    }

    [Fact]
    public void AllNone_Holds()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.None) };

        var judgement = MergeWork.Judge(bound, merges, [], null, Now);

        Assert.Equal(WorkKinds.Hold, judgement.Kind);
        Assert.Contains("no branch", judgement.Sentence);
    }

    [Fact]
    public void CleanBuildFailed_PushbackRegardlessOfHoldsTrunk()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true) };
        var builds = new[] { Build(Remote, BuildVerdicts.Failed) };

        var judgement = MergeWork.Judge(bound, merges, builds, null, Now);

        Assert.Equal(WorkKinds.Pushback, judgement.Kind);
        Assert.Contains("failed", judgement.Sentence);
        Assert.Contains("review", judgement.Sentence);
    }

    [Fact]
    public void CleanNotHoldingTrunk_Updates()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: false) };

        var judgement = MergeWork.Judge(bound, merges, [], null, Now);

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

        var judgement = MergeWork.Judge(bound, merges, builds, null, Now);

        Assert.Equal(WorkKinds.Update, judgement.Kind);
    }

    [Fact]
    public void CleanHoldsTrunkBuildPending_Pending()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true) };
        var builds = new[] { Build(Remote, BuildVerdicts.Pending) };

        var judgement = MergeWork.Judge(bound, merges, builds, null, Now);

        Assert.Equal(WorkKinds.Pending, judgement.Kind);
        Assert.Contains("still running", judgement.Sentence);
    }

    [Fact]
    public void CleanHoldsTrunkStaleBuild_Pending()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true) };
        var builds = new[] { Build(Remote, BuildVerdicts.Passed, sha: new string('c', 40)) };

        var judgement = MergeWork.Judge(bound, merges, builds, null, Now);

        Assert.Equal(WorkKinds.Pending, judgement.Kind);
        Assert.Contains("no runner has read its build on", judgement.Sentence);
    }

    [Fact]
    public void CleanHoldsTrunkNoBuildAtAll_Pending()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true) };

        var judgement = MergeWork.Judge(bound, merges, [], null, Now);

        Assert.Equal(WorkKinds.Pending, judgement.Kind);
        Assert.Contains("no checks have run on it yet", judgement.Sentence);
    }

    [Fact]
    public void CleanHoldsTrunkBuildPassed_Merges()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true) };
        var builds = new[] { Build(Remote, BuildVerdicts.Passed) };

        var judgement = MergeWork.Judge(bound, merges, builds, null, Now);

        Assert.Equal(WorkKinds.Merge, judgement.Kind);
        Assert.Contains("its build passes", judgement.Sentence);
    }

    [Fact]
    public void CleanHoldsTrunkBuildNone_PendingUnderTheSettleWindow()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true) };
        var builds = new[] { Build(Remote, BuildVerdicts.None) };

        var judgement = MergeWork.Judge(bound, merges, builds, null, Now.AddMinutes(10).AddSeconds(-1));

        Assert.Equal(WorkKinds.Pending, judgement.Kind);
        Assert.Contains("no checks ran on it", judgement.Sentence);
    }

    [Fact]
    public void CleanHoldsTrunkBuildNone_HoldsAtTheSettleWindow()
    {
        var bound = new[] { Repo(Remote) };
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true) };
        var builds = new[] { Build(Remote, BuildVerdicts.None) };

        var judgement = MergeWork.Judge(bound, merges, builds, null, Now.AddMinutes(10));

        Assert.Equal(WorkKinds.Hold, judgement.Kind);
        Assert.Contains("no checks ran on it", judgement.Sentence);
    }
}

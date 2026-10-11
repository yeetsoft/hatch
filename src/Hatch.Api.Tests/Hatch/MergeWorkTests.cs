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

    static EfHatchIssue Issue(params EfHatchProjectRepository[] repos) => new()
    {
        ProjectId = 1, Number = 1, Type = "task", Title = "x", StatusId = 1, Rank = 1,
        CreatedBy = "runner", CreatedAt = Now, UpdatedAt = Now,
        Project = new EfHatchProject { Key = "AER", Name = "AER", CreatedAt = Now, Repositories = repos },
    };

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
    public void NoMergeCheck_HoldsOnTheTrunkName()
    {
        var issue = Issue(Repo(Remote));

        var judgement = MergeWork.Judge(issue, [], []);

        Assert.Equal(WorkKinds.Hold, judgement.Kind);
        Assert.Contains("the trunk", judgement.Sentence);
    }

    [Fact]
    public void AnyConflicted_Holds()
    {
        var issue = Issue(Repo(Remote));
        var merges = new[] { Merge(Remote, MergeVerdicts.Conflicted) };

        var judgement = MergeWork.Judge(issue, merges, []);

        Assert.Equal(WorkKinds.Hold, judgement.Kind);
        Assert.Contains("conflicts", judgement.Sentence);
    }

    [Fact]
    public void AnyAmbiguous_Holds()
    {
        var issue = Issue(Repo(Remote));
        var merges = new[] { Merge(Remote, MergeVerdicts.Ambiguous) };

        var judgement = MergeWork.Judge(issue, merges, []);

        Assert.Equal(WorkKinds.Hold, judgement.Kind);
        Assert.Contains("more than one branch", judgement.Sentence);
    }

    [Fact]
    public void AllNone_Holds()
    {
        var issue = Issue(Repo(Remote));
        var merges = new[] { Merge(Remote, MergeVerdicts.None) };

        var judgement = MergeWork.Judge(issue, merges, []);

        Assert.Equal(WorkKinds.Hold, judgement.Kind);
        Assert.Contains("no branch", judgement.Sentence);
    }

    [Fact]
    public void CleanBuildFailed_HoldsRegardlessOfHoldsTrunk()
    {
        var issue = Issue(Repo(Remote));
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true) };
        var builds = new[] { Build(Remote, BuildVerdicts.Failed) };

        var judgement = MergeWork.Judge(issue, merges, builds);

        Assert.Equal(WorkKinds.Hold, judgement.Kind);
        Assert.Contains("failed", judgement.Sentence);
    }

    [Fact]
    public void CleanNotHoldingTrunk_Updates()
    {
        var issue = Issue(Repo(Remote));
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: false) };

        var judgement = MergeWork.Judge(issue, merges, []);

        Assert.Equal(WorkKinds.Update, judgement.Kind);
        Assert.Contains("does not hold its tip yet", judgement.Sentence);
    }

    [Fact]
    public void TwoRepositories_OneNotHoldingTrunk_Updates()
    {
        const string remoteB = "https://example.com/o/s";
        var issue = Issue(Repo(Remote, 0), Repo(remoteB, 1));
        var merges = new[]
        {
            Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true),
            Merge(remoteB, MergeVerdicts.Clean, holdsTrunk: false),
        };
        var builds = new[] { Build(Remote, BuildVerdicts.Passed) };

        var judgement = MergeWork.Judge(issue, merges, builds);

        Assert.Equal(WorkKinds.Update, judgement.Kind);
    }

    [Fact]
    public void CleanHoldsTrunkBuildPending_Holds()
    {
        var issue = Issue(Repo(Remote));
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true) };
        var builds = new[] { Build(Remote, BuildVerdicts.Pending) };

        var judgement = MergeWork.Judge(issue, merges, builds);

        Assert.Equal(WorkKinds.Hold, judgement.Kind);
        Assert.Contains("still running", judgement.Sentence);
    }

    [Fact]
    public void CleanHoldsTrunkStaleBuild_Holds()
    {
        var issue = Issue(Repo(Remote));
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true) };
        var builds = new[] { Build(Remote, BuildVerdicts.Passed, sha: new string('c', 40)) };

        var judgement = MergeWork.Judge(issue, merges, builds);

        Assert.Equal(WorkKinds.Hold, judgement.Kind);
        Assert.Contains("no runner has read its build on", judgement.Sentence);
    }

    [Fact]
    public void CleanHoldsTrunkNoBuildAtAll_Holds()
    {
        var issue = Issue(Repo(Remote));
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true) };

        var judgement = MergeWork.Judge(issue, merges, []);

        Assert.Equal(WorkKinds.Hold, judgement.Kind);
        Assert.Contains("no checks have run on it yet", judgement.Sentence);
    }

    [Fact]
    public void CleanHoldsTrunkBuildPassed_Merges()
    {
        var issue = Issue(Repo(Remote));
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true) };
        var builds = new[] { Build(Remote, BuildVerdicts.Passed) };

        var judgement = MergeWork.Judge(issue, merges, builds);

        Assert.Equal(WorkKinds.Merge, judgement.Kind);
        Assert.Contains("its build passes", judgement.Sentence);
    }

    [Fact]
    public void CleanHoldsTrunkBuildNone_Holds()
    {
        var issue = Issue(Repo(Remote));
        var merges = new[] { Merge(Remote, MergeVerdicts.Clean, holdsTrunk: true) };
        var builds = new[] { Build(Remote, BuildVerdicts.None) };

        var judgement = MergeWork.Judge(issue, merges, builds);

        Assert.Equal(WorkKinds.Hold, judgement.Kind);
        Assert.Contains("no checks ran on it", judgement.Sentence);
    }
}

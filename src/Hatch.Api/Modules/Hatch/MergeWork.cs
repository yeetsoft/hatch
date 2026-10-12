namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// What an issue in the merge column is waiting on, decided the way
/// <see cref="ReviewWork"/> decides it for review: from what runners have
/// reported about its branch. Unlike <see cref="ReviewWork.Judgement"/>,
/// <see cref="Judgement.Sentence"/> is never null - nothing in this column is
/// ever clear for an agent, since the whole point of the column (HA-289) is
/// that no agent runs here. Merging, and updating a branch that does not yet
/// hold the trunk, are both a person's call.
/// </summary>
public static class MergeWork
{
    /// <summary>The kind of work an issue in the merge column is, and why it is not work where it is not.</summary>
    public readonly record struct Judgement(string Kind, string Sentence);

    /// <summary>How long a build check that reads <c>none</c> is given before its wait reads as stuck rather than settling, matching the CLI poll's own <c>NoneWindow</c> for the same reason: a push's checks take a few seconds to appear.</summary>
    public static readonly TimeSpan SettleWindow = TimeSpan.FromMinutes(10);

    public static Judgement Judge(
        IReadOnlyList<EfHatchProjectRepository> bound,
        IReadOnlyList<EfHatchMergeCheck> merges,
        IReadOnlyList<EfHatchBuildCheck> builds,
        DateTimeOffset? pullRequestClosedAt,
        DateTimeOffset now)
    {
        if (pullRequestClosedAt is not null)
            return Hold("its pull request was closed without merging");

        var counted = ReviewWork.Counted(bound, merges);

        if (counted.Count == 0)
            return Pending($"no runner has checked its branch against {bound.FirstOrDefault()?.BaseBranch ?? "the trunk"} yet");

        if (counted.Any(v => v.Verdict == MergeVerdicts.Conflicted))
            return Hold("its branch conflicts with the trunk");

        if (counted.Any(v => v.Verdict == MergeVerdicts.Ambiguous))
            return Hold("more than one branch on origin is named for it - delete the ones that are not its branch");

        if (counted.All(v => v.Verdict == MergeVerdicts.None))
            return Hold("no branch on origin is named for it");

        var clean = counted.Where(v => v.Verdict == MergeVerdicts.Clean).ToList();
        var trunk = clean[0].Trunk;

        // The builds that are about the branch as the merge check read it. A
        // repository with no build verdict at all is not in either list: it
        // says nothing, which is the sentence it has always had.
        var builtAt = builds.Where(b => clean.Any(m => m.Canonical == b.Canonical)).ToList();
        var about = ReviewWork.AboutTheBranch(clean, builtAt);

        if (about.FirstOrDefault(b => b.Verdict == BuildVerdicts.Failed) is { } failed)
            return Hold($"its build on {Short(failed.Sha)} failed");

        if (clean.Any(m => m.HoldsTrunk != true))
            return new Judgement(WorkKinds.Update, $"its branch merges cleanly with {trunk} but does not hold its tip yet");

        if (about.FirstOrDefault(b => b.Verdict == BuildVerdicts.Pending) is { } running)
            return Pending($"its build on {Short(running.Sha)} is still running");

        if (builtAt.Except(about).FirstOrDefault() is { } stale)
        {
            // The sha the merge check read is the one nobody has read a build on.
            var read = clean.First(m => m.Canonical == stale.Canonical).BranchSha;
            return Pending($"no runner has read its build on {Short(read)} yet");
        }

        if (builtAt.Count == 0)
            return Pending($"its branch holds {trunk} - no checks have run on it yet");

        if (about.All(b => b.Verdict == BuildVerdicts.Passed))
            return new Judgement(WorkKinds.Merge, $"its branch holds {trunk} and its build passes");

        return about.All(b => now - b.ShaSince >= SettleWindow)
            ? Hold($"its branch holds {trunk} and no checks ran on it")
            : Pending($"its branch holds {trunk} and no checks ran on it");
    }

    private static Judgement Hold(string why) => new(WorkKinds.Hold, why);

    private static Judgement Pending(string why) => new(WorkKinds.Pending, why);

    private static string Short(string? sha) => sha is null ? "" : sha.Length > 7 ? sha[..7] : sha;
}

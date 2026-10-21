namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// What an issue in the merge column is waiting on, decided the way
/// <see cref="ReviewWork"/> decides it for review: from what runners have
/// reported about its branch. Unlike <see cref="ReviewWork.Judgement"/>,
/// <see cref="Judgement.Sentence"/> is never null - nothing in this column is
/// ever clear for an agent, since the whole point of the column (HA-289) is
/// that no agent runs here. Merging a branch that holds the trunk is a person's
/// call, which this judge defers; updating one that does not is the runner's
/// own, named by this sentence.
/// </summary>
public static class MergeWork
{
    /// <summary>The kind of work an issue in the merge column is, and why it is not work where it is not.</summary>
    public readonly record struct Judgement(string Kind, string Sentence);

    /// <summary>How long a build check that reads <c>none</c> is given before its wait reads as stuck rather than settling, matching the CLI poll's own <c>NoneWindow</c> for the same reason: a push's checks take a few seconds to appear.</summary>
    public static readonly TimeSpan SettleWindow = TimeSpan.FromMinutes(10);

    /// <summary>How long the forge's pull request state may lag behind the branch it is about before the wait for it to catch up reads as stuck rather than settling.</summary>
    public static readonly TimeSpan ForgeCatchUpWindow = TimeSpan.FromMinutes(15);

    public static Judgement Judge(
        IReadOnlyList<EfHatchProjectRepository> bound,
        IReadOnlyList<EfHatchMergeCheck> merges,
        IReadOnlyList<EfHatchBuildCheck> builds,
        DateTimeOffset? pullRequestClosedAt,
        bool checksRequired,
        string? mergeState,
        string? mergeStateSha,
        DateTimeOffset now,
        string? reviewColumn = null,
        DateTimeOffset? pullRequestHeadSince = null)
    {
        if (pullRequestClosedAt is not null)
            return Hold("its pull request was closed without merging");

        if (mergeState == MergeStates.Draft)
            return Hold("its pull request is a draft");

        var counted = ReviewWork.Counted(bound, merges);

        if (counted.Count == 0)
            return Pending($"no runner has checked its branch against {bound.FirstOrDefault()?.BaseBranch ?? "the trunk"} yet");

        if (counted.Any(v => v.Verdict == MergeVerdicts.Conflicted))
            return new Judgement(WorkKinds.Pushback, $"its branch conflicts with the trunk - back to {reviewColumn ?? "review"} to fix it");

        if (counted.Any(v => v.Verdict == MergeVerdicts.Ambiguous))
            return Hold("more than one branch on origin is named for it - delete the ones that are not its branch");

        if (counted.All(v => v.Verdict == MergeVerdicts.None))
            return Hold("no branch on origin is named for it");

        var clean = counted.Where(v => v.Verdict == MergeVerdicts.Clean).ToList();
        var trunk = clean[0].Trunk;

        Judgement MergeOr(string sentence) =>
            clean.FirstOrDefault(m => m.PlaybookFilePath is not null) is { PlaybookFilePath: { } path }
                ? Hold($"its pull request changes {path} - a person merges it on the forge after reading it")
                : new Judgement(WorkKinds.Merge, sentence);

        // The builds that are about the branch as the merge check read it. A
        // repository with no build verdict at all is not in either list: it
        // says nothing, which is the sentence it has always had.
        var builtAt = builds.Where(b => clean.Any(m => m.Canonical == b.Canonical)).ToList();
        var about = ReviewWork.AboutTheBranch(clean, builtAt);

        if (about.FirstOrDefault(b => b.Verdict == BuildVerdicts.Failed) is { } failed)
            return new Judgement(WorkKinds.Pushback, $"its build on {Short(failed.Sha)} failed - back to {reviewColumn ?? "review"} to fix it");

        var tip = clean[0];

        if (mergeStateSha is not null && !clean.Any(m => string.Equals(m.BranchSha, mergeStateSha, StringComparison.Ordinal)))
        {
            var aboutTip = builds.FirstOrDefault(b => b.Canonical == tip.Canonical && b.Sha == tip.BranchSha);
            if (aboutTip is not null && now - aboutTip.ShaSince >= ForgeCatchUpWindow)
                return Hold($"its pull request has not caught up with its branch in {ForgeCatchUpWindow.TotalMinutes:N0} minutes - it names {Short(mergeStateSha)}, its branch is at {Short(tip.BranchSha)}");

            return Pending($"waiting for the forge to catch up with its branch - its pull request names {Short(mergeStateSha)}, its branch is at {Short(tip.BranchSha)}");
        }

        if (clean.Any(m => m.HoldsTrunk != true))
            return new Judgement(WorkKinds.Update, $"bringing its branch current from {trunk}");

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
            return MergeOr($"its branch holds {trunk} and its build passes");

        DateTimeOffset Since(EfHatchBuildCheck b) =>
            pullRequestHeadSince is { } since
            && mergeStateSha is not null
            && string.Equals(mergeStateSha, b.Sha, StringComparison.Ordinal)
            && since > b.ShaSince
                ? since
                : b.ShaSince;

        if (!about.All(b => now - Since(b) >= SettleWindow))
            return Pending($"its branch holds {trunk} and no checks ran on it");

        if (!checksRequired)
            return MergeOr($"its branch holds {trunk}; this project requires no checks");

        // The forge's state is about one sha; it says nothing of a branch that has moved since.
        var judgedSha = mergeStateSha is not null
            && clean.Any(m => string.Equals(m.BranchSha, mergeStateSha, StringComparison.Ordinal));

        if (judgedSha && mergeState == MergeStates.Clean)
            return MergeOr($"its branch holds {trunk}, no checks ran on it, and nothing is required of it");

        if (judgedSha && mergeState == MergeStates.Blocked)
            return Hold($"its branch holds {trunk} and no checks ran on it - its pull request is blocked by the repository's rules");

        return Hold($"its branch holds {trunk} and no checks ran on it");
    }

    private static Judgement Hold(string why) => new(WorkKinds.Hold, why);

    private static Judgement Pending(string why) => new(WorkKinds.Pending, why);

    private static string Short(string? sha) => sha is null ? "" : sha.Length > 7 ? sha[..7] : sha;
}

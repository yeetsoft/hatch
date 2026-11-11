namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// What an issue in review is waiting on, decided from what runners have
/// reported about its branch: conflict work, build work, or nothing an agent has
/// to do - and if nothing, the sentence that says why.
/// </summary>
/// <remarks>
/// <para><b>Code decides, and never a prompt.</b> A runner reports two facts, and
/// this is the one place that says what they mean: whether the branch merges
/// with the trunk (git's answer, taken by the runner) and what the build on the
/// branch's tip came to (<c>gh</c>'s, taken by the runner). Neither is read by the
/// server from a forge.</para>
///
/// <para><b>The order</b> is the order of what a person would do about each, and
/// <b>conflicts come first</b>. An issue whose branch conflicts is conflict work
/// whatever its build says: the merge commit gives the branch a new sha, so the
/// build's answer is about to be stale, and a pull request with a merge conflict
/// often gets no CI run at all.</para>
///
/// <list type="number">
/// <item>An issue that declared it finished with no code (<see cref="NoCode"/>):
/// folded, <i>finished with no code - a person closes it</i>. Asked first, since
/// a declared issue has no counted branch-finding check to say anything else -
/// the declaration is set aside the moment one exists - so this never hides a
/// conflict or a failed build.</item>
/// <item>Any <c>conflicted</c> merge check: <see cref="WorkKinds.Conflicts"/>.</item>
/// <item>No merge check, an ambiguous branch, or no branch at all: folded, with
/// the sentence each has always had. A build is about a branch, and these are
/// the states with no one branch to ask about.</item>
/// <item>For the repositories whose merge check is <c>clean</c>, only build
/// verdicts about that merge check's own branch sha count - a verdict about any
/// other sha says nothing about the branch as it stands. Any <c>failed</c> one:
/// <see cref="WorkKinds.Build"/>, which is the one thing besides a conflict in
/// that column an agent has to do.</item>
/// <item>Any <c>pending</c>: folded, <i>its build on &lt;sha&gt; is still
/// running</i>. Nothing is fixed until every check has concluded.</item>
/// <item>A clean repository whose build verdict is about a different sha than
/// its merge check read: <i>no runner has read its build on &lt;sha&gt;
/// yet</i>.</item>
/// <item>No build verdict at all in any clean repository: today's sentence,
/// unchanged - <i>its branch merges cleanly with &lt;trunk&gt; - nothing for an
/// agent to do</i>. That is also every repository whose builds cannot be read (a
/// runner without <c>gh</c>, one that is not signed in, a host it cannot reach),
/// which must cost nothing.</item>
/// <item>Every counted verdict <c>passed</c>: <i>... and its build passes</i>.</item>
/// <item>Otherwise <c>none</c>: <i>... and no checks ran on it</i>.</item>
/// </list>
///
/// <para>Where the project binds repositories only a verdict for one it still
/// binds counts, for a merge check and a build alike: a verdict about a
/// repository the project let go of is a fact about something nobody is asking
/// about. An unbound project counts every verdict, because it has no list to be
/// measured against.</para>
/// </remarks>
public static class ReviewWork
{
    /// <summary>The kind of work an issue in review is, and why it is not work where it is not.</summary>
    /// <param name="Kind">
    /// <see cref="WorkKinds.Build"/> when no counted merge check conflicts and a
    /// clean repository's build failed on the sha its merge check read;
    /// otherwise <see cref="WorkKinds.Conflicts"/>, which is also what a folded
    /// review row has always kept - nothing reads the kind of a folded row.
    /// </param>
    /// <param name="Fold">Null when there is something for an agent to do.</param>
    /// <param name="Work">What the agent would be asked to do, as a sentence, where <paramref name="Fold"/> is null; null where it is not.</param>
    public readonly record struct Judgement(string Kind, string? Fold, string? Work = null);

    /// <summary>
    /// The sentence for a review row held back because its branch is a
    /// contribution's and the project has not said runners work those - see
    /// <see cref="HeldContribution"/>.
    /// </summary>
    public const string ContributionHeld = "a contribution's branch is not worked by a runner unless the project says so";

    /// <summary>
    /// Whether a review row is held back as a contribution: the project does not
    /// have runners work contributions, the issue's contribution carries a pull
    /// request, and the judge would otherwise have dispatched it as conflict or
    /// build work. A row the judge folds is not held here - it stays where it is.
    /// </summary>
    public static bool HeldContribution(bool runnersWork, string? contributionPullRequestUrl, Judgement judged) =>
        !runnersWork && contributionPullRequestUrl is not null && judged.Fold is null;

    /// <summary>
    /// Why a row has no branch on origin: none is named for its key, or, where its
    /// contribution recorded a head branch, that branch is not there.
    /// </summary>
    internal static string NoBranchOnOrigin(string? recordedBranch) =>
        recordedBranch is { Length: > 0 } branch
            ? $"its recorded branch `{branch}` is not on origin"
            : "no branch on origin is named for it";

    public static Judgement Judge(
        EfHatchIssue issue, IReadOnlyList<EfHatchMergeCheck> merges, IReadOnlyList<EfHatchBuildCheck> builds,
        string? recordedBranch = null) =>
        Judge(issue.NoCode, issue.Project!.Repositories.OrderBy(r => r.SortOrder).ToList(), merges, builds, recordedBranch);

    /// <summary>
    /// The judgement from the facts alone, for a caller that has projected rows
    /// rather than loaded issues: the issue's no-code declaration and the
    /// repositories its project binds, in sort order.
    /// </summary>
    public static Judgement Judge(
        string? noCode, IReadOnlyList<EfHatchProjectRepository> bound,
        IReadOnlyList<EfHatchMergeCheck> merges, IReadOnlyList<EfHatchBuildCheck> builds,
        string? recordedBranch = null)
    {
        if (NoCode.Declared(noCode)) return Fold(NoCode.Sentence);

        var counted = Counted(bound, merges);

        if (counted.Any(v => v.Verdict == MergeVerdicts.Conflicted))
            return new Judgement(WorkKinds.Conflicts, null,
                $"its branch conflicts with {counted.First(v => v.Verdict == MergeVerdicts.Conflicted).Trunk}");

        if (counted.Count == 0)
            return Fold($"no runner has checked its branch against {bound.FirstOrDefault()?.BaseBranch ?? "the trunk"} yet");

        if (counted.Any(v => v.Verdict == MergeVerdicts.Ambiguous))
            return Fold("more than one branch on origin is named for it - delete the ones that are not its branch");

        if (counted.All(v => v.Verdict == MergeVerdicts.None))
            return Fold(NoBranchOnOrigin(recordedBranch));

        var clean = counted.Where(v => v.Verdict == MergeVerdicts.Clean).ToList();
        var trunk = clean[0].Trunk;

        // The builds that are about the branch as the merge check read it. A
        // repository with no build verdict at all is not in either list: it
        // says nothing, which is the sentence it has always had.
        var builtAt = builds.Where(b => clean.Any(m => m.Canonical == b.Canonical)).ToList();
        var about = AboutTheBranch(clean, builtAt);

        if (about.FirstOrDefault(b => b.Verdict == BuildVerdicts.Failed) is { } failed)
            return new Judgement(WorkKinds.Build, null, $"its build on {Short(failed.Sha)} failed");

        if (about.FirstOrDefault(b => b.Verdict == BuildVerdicts.Pending) is { } running)
            return Fold($"its build on {Short(running.Sha)} is still running");

        if (builtAt.Except(about).FirstOrDefault() is { } stale)
        {
            // The sha the merge check read is the one nobody has read a build on.
            var read = clean.First(m => m.Canonical == stale.Canonical).BranchSha;
            return Fold($"no runner has read its build on {Short(read)} yet");
        }

        if (builtAt.Count == 0)
            return Fold($"its branch merges cleanly with {trunk} - nothing for an agent to do");

        return about.All(b => b.Verdict == BuildVerdicts.Passed)
            ? Fold($"its branch merges cleanly with {trunk} and its build passes - nothing for an agent to do")
            : Fold($"its branch merges cleanly with {trunk} and no checks ran on it");
    }

    /// <summary>The merge verdicts for repositories the project still binds - every one, if it binds none.</summary>
    public static IReadOnlyList<EfHatchMergeCheck> Counted(
        IReadOnlyList<EfHatchProjectRepository> bound, IReadOnlyList<EfHatchMergeCheck> merges) =>
        bound.Count == 0 ? merges : merges.Where(v => bound.Any(r => r.Canonical == v.Canonical)).ToList();

    /// <summary>
    /// The build verdicts about the branch as a clean merge check read it - a
    /// verdict on any other sha says nothing about the branch as it stands.
    /// </summary>
    public static IReadOnlyList<EfHatchBuildCheck> AboutTheBranch(
        IReadOnlyList<EfHatchMergeCheck> clean, IReadOnlyList<EfHatchBuildCheck> builds) =>
        builds.Where(b => clean.Any(m => m.Canonical == b.Canonical && m.BranchSha == b.Sha)).ToList();

    /// <summary>A folded row, which keeps the conflicts kind it has always had.</summary>
    private static Judgement Fold(string why) => new(WorkKinds.Conflicts, why);

    private static string Short(string? sha) => sha is null ? "" : sha.Length > 7 ? sha[..7] : sha;
}

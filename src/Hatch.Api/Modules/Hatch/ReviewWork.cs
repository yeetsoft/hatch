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
    public readonly record struct Judgement(string Kind, string? Fold);

    public static Judgement Judge(
        EfHatchIssue issue, IReadOnlyList<EfHatchMergeCheck> merges, IReadOnlyList<EfHatchBuildCheck> builds)
    {
        var bound = issue.Project!.Repositories.OrderBy(r => r.SortOrder).ToList();

        var counted = bound.Count == 0
            ? merges
            : merges.Where(v => bound.Any(r => r.Canonical == v.Canonical)).ToList();

        if (counted.Any(v => v.Verdict == MergeVerdicts.Conflicted))
            return new Judgement(WorkKinds.Conflicts, null);

        if (counted.Count == 0)
            return Fold($"no runner has checked its branch against {bound.FirstOrDefault()?.BaseBranch ?? "the trunk"} yet");

        if (counted.Any(v => v.Verdict == MergeVerdicts.Ambiguous))
            return Fold("more than one branch on origin is named for it - delete the ones that are not its branch");

        if (counted.All(v => v.Verdict == MergeVerdicts.None))
            return Fold("no branch on origin is named for it");

        var clean = counted.Where(v => v.Verdict == MergeVerdicts.Clean).ToList();
        var trunk = clean[0].Trunk;

        // The builds that are about the branch as the merge check read it. A
        // repository with no build verdict at all is not in either list: it
        // says nothing, which is the sentence it has always had.
        var builtAt = builds.Where(b => clean.Any(m => m.Canonical == b.Canonical)).ToList();
        var about = builtAt.Where(b => clean.Any(m => m.Canonical == b.Canonical && m.BranchSha == b.Sha)).ToList();

        if (about.Any(b => b.Verdict == BuildVerdicts.Failed))
            return new Judgement(WorkKinds.Build, null);

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

    /// <summary>A folded row, which keeps the conflicts kind it has always had.</summary>
    private static Judgement Fold(string why) => new(WorkKinds.Conflicts, why);

    private static string Short(string? sha) => sha is null ? "" : sha.Length > 7 ? sha[..7] : sha;
}

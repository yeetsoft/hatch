namespace Hatch.Cli;

/// <summary>
/// One repository's conflict, in the words the prompt names it in: the trunk and
/// its sha, the branch and its sha, and the files.
/// </summary>
/// <param name="Where">The checkout or remote it is in - said only when more than one repository conflicts.</param>
public sealed record ConflictFacts(
    string Where, string Trunk, string TrunkSha, string Branch, string BranchSha, IReadOnlyList<string> Files);

/// <summary>
/// One checkout's verdict, taken just now: which checkout, and what git said.
/// </summary>
public sealed record Checked(string Path, Verdict Verdict);

/// <summary>
/// What the runner found when it checked an issue's branch in every checkout the
/// increment serves.
/// </summary>
/// <param name="Verdicts">One per checkout that could be read.</param>
/// <param name="Unknown">
/// A checkout could not be read - git older than 2.38, no answer from origin - so
/// the answer for it is not "clean", it is nothing. The board's verdict is not
/// contradicted by a runner that cannot read one.
/// </param>
/// <param name="Reported">
/// The board took every verdict. Where it did not, it still holds the old one,
/// and a pass that went straight back would find the same issue again in a
/// tight loop.
/// </param>
public sealed record Rechecked(IReadOnlyList<Checked> Verdicts, bool Unknown, bool Reported)
{
    /// <summary>The checkouts whose branch still conflicts.</summary>
    public IReadOnlyList<Checked> Conflicts => Verdicts.Where(v => v.Verdict.Kind == MergeVerdicts.Conflicted).ToList();

    /// <summary>The trunk the first verdict was taken against, if any was.</summary>
    public string? Trunk => (Conflicts.FirstOrDefault() ?? Verdicts.FirstOrDefault())?.Verdict.Trunk;

    /// <summary>Every file that still conflicts, once, in the order the checkouts gave them.</summary>
    public IReadOnlyList<string> Files => Conflicts.SelectMany(c => c.Verdict.Files).Distinct().ToList();
}

/// <summary>
/// What the board says about an issue whose branch conflicts with the trunk, read
/// off the verdicts the dispatch carries. One place for it, because the queue's
/// row, the ticket header and the prompt's conflict section all name the same
/// trunk and must not come to name different ones.
/// </summary>
internal static class Conflicts
{
    /// <summary>What is said when no verdict names a trunk - a dry run with nothing on the board yet.</summary>
    public const string UnnamedTrunk = "the trunk";

    /// <summary>The verdicts that say <c>conflicted</c>, in the board's order.</summary>
    public static IReadOnlyList<MergeCheckDto> Of(IssueDto issue) =>
        (issue.MergeChecks ?? []).Where(c => c.Verdict == MergeVerdicts.Conflicted).ToList();

    /// <summary>The trunk the first conflicted repository was checked against, or <see cref="UnnamedTrunk"/>.</summary>
    public static string Trunk(IssueDto issue) => Of(issue).FirstOrDefault()?.Trunk ?? UnnamedTrunk;

    /// <summary><c>resolving conflicts with main</c>: what a conflict dispatch is, where a move would say <c>-&gt; In Review</c>.</summary>
    public static string Words(IssueDto issue) => $"resolving conflicts with {Trunk(issue)}";

    /// <summary>
    /// The conflicts to put in the prompt: the runner's own fresh verdicts where
    /// it took some, and what the board holds where it did not - which is a dry
    /// run, and the only time a runner has no git to ask.
    /// </summary>
    public static IReadOnlyList<ConflictFacts> Facts(WorkDto work, Rechecked? found) =>
        found is not null
            ? found.Conflicts
                .Select(c => new ConflictFacts(
                    System.IO.Path.GetFileName(c.Path.TrimEnd('/', '\\')), c.Verdict.Trunk, c.Verdict.TrunkSha,
                    c.Verdict.Branch ?? "", c.Verdict.BranchSha ?? "", c.Verdict.Files))
                .ToList()
            : Of(work.Issue)
                .Select(c => new ConflictFacts(c.Remote, c.Trunk, c.TrunkSha, c.Branch ?? "", c.BranchSha ?? "", c.Files))
                .ToList();
}

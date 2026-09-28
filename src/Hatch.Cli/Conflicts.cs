namespace Hatch.Cli;

/// <summary>
/// What the board says about an issue whose branch conflicts with the trunk,
/// read off the verdicts the dispatch carries. One place for it, because the
/// queue's row, the ticket header and the prompt's conflict section all name the
/// same trunk and must not come to name different ones.
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
}

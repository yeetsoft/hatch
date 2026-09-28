namespace Hatch.Cli;

/// <summary>One check that failed, and what the runner read of its log.</summary>
/// <param name="Url">The check's own page, or null where the forge gave none.</param>
/// <param name="Excerpt">The lines of its log that lead up to the failure, or null where <c>gh</c> gave none.</param>
/// <param name="JobId">What <c>gh run view --job</c> reads the whole log by, where the check is a job.</param>
public sealed record FailingBuild(string Name, string? Url, string? Excerpt, long? JobId = null);

/// <summary>One repository's failing build, in the words the prompt names it in.</summary>
/// <param name="Where">The checkout or remote it is in - said only when more than one repository fails.</param>
public sealed record BuildFacts(string Where, string Branch, string Sha, IReadOnlyList<FailingBuild> Failing);

/// <summary>
/// One checkout's build, read just now: the sha the dispatch was about, the tip
/// origin has now, and what the build on the tip came to.
/// </summary>
/// <param name="Remote">What the verdict is reported under.</param>
/// <param name="Branch">The one branch origin has for the issue.</param>
/// <param name="DispatchedSha">The failed verdict's sha, which is what the dispatch was about.</param>
/// <param name="TipSha">Where the branch stands on origin now.</param>
/// <param name="Failing">The failing checks with their excerpts - only fetched where the tip is still the dispatched one and the build still fails.</param>
public sealed record BuiltRepo(
    string Path, string? Remote, string Branch, string DispatchedSha, string TipSha, string Verdict,
    IReadOnlyList<FailingBuild> Failing)
{
    /// <summary>The build on the sha the dispatch was about still fails: there is something for a session to fix.</summary>
    public bool StillFailing => TipSha == DispatchedSha && Verdict == BuildVerdicts.Failed;
}

/// <summary>
/// What the runner found when it read the build on the issue's branch again, in
/// every checkout the increment serves that holds a failed verdict.
/// </summary>
/// <param name="Unknown">
/// A checkout could not be read - no answer from origin, more or fewer than one
/// branch, a forge that could not say - so the answer for it is not "green", it
/// is nothing. Nothing is spawned for a build that may not be failing, and
/// nothing is stood down for one that may be.
/// </param>
/// <param name="Reported">
/// The board took every verdict. Where it did not, it still holds the old one
/// and a pass that went straight back would find the same issue again in a tight
/// loop.
/// </param>
public sealed record BuildFound(IReadOnlyList<BuiltRepo> Repos, bool Unknown, bool Reported)
{
    /// <summary>The checkouts whose build still fails on the sha the dispatch was about.</summary>
    public IReadOnlyList<BuiltRepo> StillFailing => Repos.Where(r => r.StillFailing).ToList();

    /// <summary>The failing checks' names, once each, in the order the checkouts gave them.</summary>
    public IReadOnlyList<string> Names => StillFailing.SelectMany(r => r.Failing).Select(f => f.Name).Distinct().ToList();

    /// <summary>What is said when there is nothing to spawn for: the tip moved, or the build is no longer red.</summary>
    public string Nothing()
    {
        var moved = Repos.Where(r => r.TipSha != r.DispatchedSha).ToList();
        return moved.Count > 0 && moved.Count == Repos.Count
            ? "its branch has moved since the build failed - the build on its new tip is asked about on its own"
            : "its build is no longer failing";
    }
}

/// <summary>What the session did to the branch, asked of origin after it ended.</summary>
/// <param name="Pushed">A checkout's tip on origin is no longer the one the dispatch was about.</param>
/// <param name="Unknown">A tip could not be read, so whether a fix was pushed is not known.</param>
public sealed record BuildJudged(bool Pushed, bool Unknown);

/// <summary>
/// What a build increment needs that <see cref="Increment"/> has no git or forge
/// to find out: what the recheck found, for the prompt, and the question asked
/// again after the session, for the verdict.
/// </summary>
/// <remarks>
/// A delegate rather than a workspace, for the reason <see cref="ConflictRun"/>
/// is one. Both callers build it from their <see cref="Lifecycle"/>.
/// </remarks>
public sealed record BuildRun(BuildFound Found, Func<CancellationToken, Task<BuildJudged>> Judge);

/// <summary>
/// What the board says about an issue whose build failed, read off the verdicts
/// the dispatch carries. One place for it, because the queue's row, the ticket
/// header and the prompt's section all name the same checks.
/// </summary>
internal static class Builds
{
    /// <summary>The verdicts that say <c>failed</c>, in the board's order.</summary>
    public static IReadOnlyList<BuildCheckDto> Of(IssueDto issue) =>
        (issue.BuildChecks ?? []).Where(c => c.Verdict == BuildVerdicts.Failed).ToList();

    /// <summary>The names of the failing checks across repositories, once each, in the board's order.</summary>
    public static IReadOnlyList<string> Names(IssueDto issue) =>
        Of(issue).SelectMany(c => c.Failing).Select(f => f.Name).Distinct().ToList();

    /// <summary><c>fixing its failing build (api, CI)</c>: what a build dispatch is, where a move would say <c>-&gt; In Review</c>.</summary>
    public static string Words(IReadOnlyList<string> names) =>
        names.Count == 0 ? "fixing its failing build" : $"fixing its failing build ({string.Join(", ", names)})";

    public static string Words(IssueDto issue) => Words(Names(issue));

    /// <summary>
    /// The failing builds to put in the prompt: the runner's own fresh reads
    /// where it took some - with the log excerpts it fetched - and what the board
    /// holds where it did not, which is a dry run, and has no log to give.
    /// </summary>
    public static IReadOnlyList<BuildFacts> Facts(WorkDto work, BuildFound? found) =>
        found is not null
            ? found.StillFailing
                .Select(r => new BuildFacts(
                    System.IO.Path.GetFileName(r.Path.TrimEnd('/', '\\')), r.Branch, r.TipSha, r.Failing))
                .ToList()
            : Of(work.Issue)
                .Select(c => new BuildFacts(
                    c.Remote, c.Branch, c.Sha, c.Failing.Select(f => new FailingBuild(f.Name, f.Url, null)).ToList()))
                .ToList();
}

using System.Text;

namespace Hatch.Cli;

/// <summary>What branch entry found in one checkout.</summary>
public enum BranchKind
{
    /// <summary>Origin has no branch for the key. The tree is on the trunk, and a name is offered to cut.</summary>
    None,

    /// <summary>One unmerged branch, and the tree is on it.</summary>
    Entered,

    /// <summary>Origin has branches for the key and every one is already in the trunk. The tree is on the trunk.</summary>
    AlreadyMerged,

    /// <summary>Two or more unmerged branches. Nothing was chosen, and the tree is on the trunk.</summary>
    Several,

    /// <summary>The branch could not be entered. The tree is on the trunk.</summary>
    Failed,
}

/// <summary>What origin has for a key - see <see cref="IWorkspace.Presence"/>.</summary>
public enum BranchPresence
{
    /// <summary>No branch on origin names the key.</summary>
    None,

    /// <summary>At least one branch names it and is not in the trunk yet.</summary>
    Open,

    /// <summary>Branches name it, and every one is already in the trunk.</summary>
    MergedOnly,
}

/// <summary>What merging the trunk into the branch came to.</summary>
public enum MergeOutcome
{
    /// <summary>The branch already contains the trunk.</summary>
    NoOp,

    /// <summary>The trunk was merged, and the merge is committed.</summary>
    Clean,

    /// <summary>The merge stopped on conflicts and is still in progress in the tree.</summary>
    Conflicted,

    /// <summary>Git would not start the merge at all.</summary>
    Failed,

    /// <summary>A plan on a git too old to say without doing it.</summary>
    Unknown,
}

/// <summary>
/// What branch entry did in one checkout - the whole of what the prompt's
/// <c>## The branch</c> section is written from.
/// </summary>
/// <remarks>
/// A class with initialisers rather than a positional record: which fields mean
/// anything depends on <see cref="Kind"/>, and a constructor with eleven
/// arguments three of which apply is harder to read than the four lines that
/// set them.
/// </remarks>
public sealed class BranchEntry
{
    /// <summary>The checkout this is about.</summary>
    public required string Path { get; init; }

    public required BranchKind Kind { get; init; }

    /// <summary>The branch the tree is on - <see cref="BranchKind.Entered"/> only.</summary>
    public string Branch { get; init; } = "";

    /// <summary>Where the branch stood when it was entered, before the trunk was merged in.</summary>
    public string Sha { get; init; } = "";

    /// <summary>Where it stands now. Differs from <see cref="Sha"/> after a clean merge.</summary>
    public string Head { get; init; } = "";

    /// <summary>Commits the branch has that the trunk does not, before the merge.</summary>
    public int Ahead { get; init; }

    public MergeOutcome Merge { get; init; }

    /// <summary>The files a conflicted merge stopped on.</summary>
    public IReadOnlyList<string> Conflicted { get; init; } = [];

    /// <summary>
    /// The branches on origin that name the key and are not merged - every
    /// candidate for <see cref="BranchKind.Several"/>.
    /// </summary>
    public IReadOnlyList<string> Candidates { get; init; } = [];

    /// <summary>The branches on origin that name the key and are already in the trunk.</summary>
    public IReadOnlyList<string> Merged { get; init; } = [];

    /// <summary>The name to cut, when there is no branch to enter. Never one that exists on origin.</summary>
    public string Cut { get; init; } = "";

    /// <summary>The trunk's name.</summary>
    public string Trunk { get; init; } = "";

    /// <summary>This project lands work on the trunk rather than a branch per issue.</summary>
    public bool TrunkLanding { get; init; }

    /// <summary>Where the local tip was kept, when it had diverged from origin's.</summary>
    public string KeptAs { get; init; } = "";

    /// <summary>Whether the local copy was ahead of origin, and was used as it was.</summary>
    public bool LocalAhead { get; init; }

    /// <summary>Something went wrong, in a sentence - <see cref="BranchKind.Failed"/>, or a merge git would not start.</summary>
    public string Problem { get; init; } = "";

    /// <summary>Nothing was changed: this is what a dry run would do.</summary>
    public bool Planned { get; init; }

    /// <summary>
    /// A person answered which of several branches to use, and the answer named
    /// none of them - so the prompt hands the decision on rather than the
    /// loop asking again.
    /// </summary>
    public bool Decided { get; init; }

    /// <summary>The line the terminal says it in.</summary>
    public string Sentence()
    {
        var where = System.IO.Path.GetFileName(Path.TrimEnd('/', '\\'));
        var verb = Planned ? "would be on" : "on";
        var cut = Planned ? "the session would be told to cut" : "the session is told to cut";

        return Kind switch
        {
            BranchKind.Entered =>
                $"{where}: {verb} {Branch} at {Sha}, {Ahead} commit(s) ahead of the trunk; {MergeWords()}",
            BranchKind.None => TrunkLanding
                ? $"{where}: {(Planned ? "would stay" : "stays")} on {Trunk} at {Sha} - this project lands on the trunk"
                : $"{where}: no branch on origin for this issue; {cut} {Cut}",
            BranchKind.AlreadyMerged => TrunkLanding
                ? $"{where}: {(Planned ? "would stay" : "stays")} on {Trunk} at {Sha} - this project lands on the trunk; {string.Join(", ", Merged)} already merged into the trunk"
                : $"{where}: {string.Join(", ", Merged)} already merged into the trunk; {cut} {Cut}",
            BranchKind.Several => $"{where}: {Candidates.Count} unmerged branches: {string.Join(", ", Candidates)}",
            _ => $"{where}: {Problem}",
        };
    }

    private string MergeWords() => Merge switch
    {
        MergeOutcome.NoOp => "the trunk is already in it",
        MergeOutcome.Clean => Planned ? "the trunk would merge cleanly" : $"the trunk merged cleanly, now at {Head}",
        MergeOutcome.Conflicted =>
            $"the trunk {(Planned ? "would conflict" : "conflicted")} in {Conflicted.Count} file(s): {string.Join(", ", Conflicted)}",
        MergeOutcome.Failed => $"the trunk would not merge: {Problem}",
        _ => "whether the trunk merges cleanly is not known here",
    };
}

/// <summary>What the end of an increment did to one checkout, as lines for the ticket.</summary>
/// <param name="Found">
/// What origin's branch for the issue now comes to against the trunk, when the
/// step fetched and so knows - null when it did not, because nothing is known
/// then and nothing is to be reported.
/// </param>
public sealed record Leaving(string Path, IReadOnlyList<string> Notes, Verdict? Found = null);

/// <summary>
/// The rules about branch names, kept apart from git so they can be read - and
/// tested - without a repository.
/// </summary>
public static class Branches
{
    /// <summary>The longest slug a cut name carries: enough to read, short enough to type.</summary>
    private const int SlugLength = 40;

    /// <summary>
    /// Whether a branch is this issue's: the lowercased key, or that and a hyphen
    /// and anything. Hyphen-anchored, so <c>ha-3</c> is not <c>ha-31-x</c>. The
    /// match runs from the key to the branch and never the other way, so a number
    /// <see cref="Free"/> appends - <c>ha-3-thing-2</c>, or <c>ha-3-2</c> when the
    /// key is the whole name - is only ever more of the name: <c>ha-3-2</c> is
    /// <c>ha-3</c>'s, and is not, and cannot be mistaken for, <c>ha-32</c>'s.
    /// </summary>
    public static bool Names(string branch, string key)
    {
        var lower = key.ToLowerInvariant();
        return branch.Equals(lower, StringComparison.OrdinalIgnoreCase)
            || branch.StartsWith(lower + "-", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <c>ha-31-deterministic-lifecycle-elements</c>: the key, then the title
    /// as words. Cut at a word, and never ending in a hyphen.
    /// </summary>
    public static string Cut(string key, string title)
    {
        var slug = new StringBuilder();
        var pending = false;

        foreach (var c in title.ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (pending && slug.Length > 0) slug.Append('-');
                pending = false;
                slug.Append(c);
            }
            else
            {
                pending = true;
            }
        }

        var text = slug.ToString();
        if (text.Length > SlugLength)
        {
            var cut = text.LastIndexOf('-', SlugLength);
            text = cut > 0 ? text[..cut] : text[..SlugLength];
        }

        return text.Length == 0 ? key.ToLowerInvariant() : $"{key.ToLowerInvariant()}-{text}";
    }

    /// <summary>
    /// A name that does not exist: the wanted one, or the wanted one with a
    /// hyphen and the first free number after it - <c>-2</c>, then <c>-3</c>.
    /// </summary>
    public static string Free(string wanted, Func<string, bool> taken)
    {
        if (!taken(wanted)) return wanted;

        for (var n = 2; ; n++)
            if (!taken($"{wanted}-{n}")) return $"{wanted}-{n}";
    }

    /// <summary>
    /// Which candidates a person's answer names. The label an option carried is
    /// the branch's own name, but an answer can be typed, so a name inside a
    /// longer sentence counts - as long as it is one of them and not several.
    /// </summary>
    public static string? Chosen(string? answer, IReadOnlyList<string> candidates)
    {
        if (string.IsNullOrWhiteSpace(answer)) return null;

        var exact = candidates.FirstOrDefault(c => c.Equals(answer.Trim().Trim('`'), StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        var named = candidates
            .Where(c => answer.Split([' ', '\n', '\t', ',', ';', '`', '"', '\'', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
                .Any(word => word.Equals(c, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return named.Count == 1 ? named[0] : null;
    }

    /// <summary>
    /// Whether this dispatch's work goes on a branch named for another issue - a
    /// task under a story that shares one pull request. The server decides it
    /// (<see cref="WorkDto.BranchKey"/>), and <see cref="Lifecycle.Settle"/> hands
    /// it back to the task's own where git says it should not apply.
    /// </summary>
    public static bool IsShared(WorkDto work) =>
        work.BranchKey is { Length: > 0 } key && !key.Equals(work.Issue.Key, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The head branch the issue's contribution recorded, which stands in for a
    /// branch named for the key - the issue's own record only: a task on its
    /// story's shared branch checks the story's branch, whatever it has recorded.
    /// </summary>
    public static string? RecordedFor(WorkDto work) =>
        IsShared(work) ? null : work.Issue.Contribution?.HeadBranch is { Length: > 0 } head ? head : null;

    /// <summary>The key every git call for this dispatch names its branch by - the issue's own, unless it shares.</summary>
    public static string KeyOf(WorkDto work) => IsShared(work) ? work.BranchKey! : work.Issue.Key;

    /// <summary>
    /// The title the branch's slug is cut from - the story's, when the branch is
    /// the story's. A response that names the story's key and not its title gets
    /// no slug at all, and so a branch named for the key alone: never the key
    /// cut into its own slug, which every sibling that follows would have to
    /// join.
    /// </summary>
    public static string TitleOf(WorkDto work) =>
        IsShared(work) ? work.BranchTitle ?? "" : work.Issue.Title;

    /// <summary>The prefix every which-branch question starts with, so a later pass can find its answer.</summary>
    public static string Question(string key) => $"Which branch should {key} continue on?";
}

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
            BranchKind.None => $"{where}: no branch on origin for this issue; {cut} {Cut}",
            BranchKind.AlreadyMerged =>
                $"{where}: {string.Join(", ", Merged)} already merged into the trunk; {cut} {Cut}",
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
public sealed record Leaving(string Path, IReadOnlyList<string> Notes);

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
    /// and anything. Hyphen-anchored, so <c>ha-3</c> is not <c>ha-31-x</c>.
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
    /// A name that does not exist: the wanted one, or the wanted one with the
    /// first free number after it.
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

    /// <summary>The prefix every which-branch question starts with, so a later pass can find its answer.</summary>
    public static string Question(string key) => $"Which branch should {key} continue on?";
}

namespace Hatch.Cli;

/// <summary>What <see cref="IWorkspace.ReadBlob"/> came to.</summary>
public enum BlobOutcome
{
    /// <summary>The path exists at the ref, and <see cref="BlobRead.Content"/> is its exact bytes.</summary>
    Found,

    /// <summary>The path does not exist at the ref - a clean answer, not a failure.</summary>
    Absent,

    /// <summary>Git could not answer at all - a bad ref, or no git. See <see cref="BlobRead.Why"/>.</summary>
    Failed,
}

/// <param name="Content">The blob's exact bytes. Null unless Outcome is Found.</param>
/// <param name="Why">One line, for a sentence. Set only when Outcome is Failed.</param>
public readonly record struct BlobRead(BlobOutcome Outcome, byte[]? Content, string? Why);

/// <summary>
/// Reading a path's exact bytes from origin's refs, with no text decode
/// anywhere in the path - see <see cref="IWorkspace.ReadBlob"/>.
/// </summary>
public sealed partial class Workspace
{
    /// <summary>
    /// What git says when the path is not at the ref: ordinarily, and - when
    /// the working tree happens to hold the same path checked out from
    /// whatever ref the tree is standing on, unrelated to the one just asked
    /// about - this second wording instead. Either means the same thing: the
    /// ref does not have it.
    /// </summary>
    private static readonly string[] PathNotFound = ["does not exist in", "exists on disk, but not in"];

    /// <summary>
    /// <c>git cat-file blob origin/&lt;trunk&gt;:&lt;path&gt;</c> - the plumbing
    /// form, stable and scriptable, no porcelain decoration. Reads the refs as
    /// they stand; does not fetch.
    /// </summary>
    public BlobRead ReadBlob(string trunk, string path)
    {
        var (code, bytes, err) = GitBytes("cat-file", "blob", $"origin/{trunk}:{path}");
        if (code == 0) return new BlobRead(BlobOutcome.Found, bytes, null);

        var firstLine = err.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);

        if (firstLine is not null && PathNotFound.Any(p => firstLine.Contains(p, StringComparison.Ordinal)))
            return new BlobRead(BlobOutcome.Absent, null, null);

        return new BlobRead(BlobOutcome.Failed, null, Why(code, err, firstLine));
    }

    /// <summary>One line for a sentence: git is not there, or what it said first.</summary>
    private static string Why(int code, string err, string? firstLine)
    {
        if (code == -1 && err.Length == 0) return "git is not installed";
        return firstLine ?? $"git exited {code}";
    }
}

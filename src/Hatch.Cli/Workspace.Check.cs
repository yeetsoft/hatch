using System.Text;

namespace Hatch.Cli;

/// <summary>
/// What origin has right now, read with one <c>git ls-remote</c> - the trunk's
/// name and every branch's sha, and nothing fetched.
/// </summary>
/// <param name="Trunk">The trunk's name in this checkout.</param>
/// <param name="Shas">Every branch head on origin, by name.</param>
public sealed record RemoteHeads(string Trunk, IReadOnlyDictionary<string, string> Shas)
{
    /// <summary>The trunk's sha, or null when origin has no branch of that name.</summary>
    public string? TrunkSha => Shas.GetValueOrDefault(Trunk);

    /// <summary>
    /// What the poll compares: the trunk's sha and every branch the key claims,
    /// name and sha, sorted - or null when origin has no trunk to compare against.
    /// </summary>
    /// <remarks>
    /// Every candidate and not only the unmerged ones, because whether one is
    /// merged is what fetching answers: a fingerprint that had to know would
    /// need the fetch it exists to avoid. A branch that merged, or a second one
    /// that appeared, changes it either way.
    /// </remarks>
    public string? Fingerprint(string key, string? recordedBranch = null)
    {
        if (TrunkSha is not { } trunk) return null;

        var candidates = Candidates(key, recordedBranch)
            .Select(name => $"{name}:{Shas[name]}");

        return string.Join('\n', [trunk, .. candidates]);
    }

    /// <summary>
    /// The branches the key claims, trunk excluded, by name - or, where the
    /// issue's contribution recorded a head branch, that branch alone when
    /// origin has it, and a branch named for the key not at all.
    /// </summary>
    public IReadOnlyList<string> Candidates(string key, string? recordedBranch = null) =>
        recordedBranch is { Length: > 0 } recorded
            ? recorded != Trunk && Shas.ContainsKey(recorded) ? [recorded] : []
            : Shas.Keys.Where(b => b != Trunk && Branches.Names(b, key)).Order(StringComparer.Ordinal).ToList();

    /// <summary>
    /// The branch a build is about: the one branch origin has for the key, or,
    /// where it has several, the one the stored <c>clean</c> or <c>conflicted</c>
    /// merge check names. Null for none, and for several with no usable stored
    /// branch.
    /// </summary>
    /// <remarks>
    /// The merge check fetched, so it knows which of the branches is open;
    /// <c>ls-remote</c> does not, and counts a stale empty branch the same as
    /// the live one.
    /// </remarks>
    public string? BranchFor(string key, MergeCheckDto? stored, string? recordedBranch = null)
    {
        var candidates = Candidates(key, recordedBranch);
        if (candidates.Count == 1) return candidates[0];

        return stored is { Verdict: MergeVerdicts.Clean or MergeVerdicts.Conflicted, Branch: { } b }
            && candidates.Contains(b)
            ? b
            : null;
    }
}

/// <summary>
/// What a branch on origin comes to against the trunk on origin, as the board
/// keeps it - see <see cref="MergeVerdicts"/>.
/// </summary>
/// <param name="Kind">One of <see cref="MergeVerdicts"/>.</param>
/// <param name="TrunkSha">Full, so that two runners looking at the same two shas agree.</param>
/// <param name="BranchSha">Full. Null for the two verdicts that are not about one branch.</param>
/// <param name="HoldsTrunk">See <see cref="MergeCheckRequest.HoldsTrunk"/>.</param>
/// <param name="PlaybookFilePath">See <see cref="MergeCheckRequest.PlaybookFilePath"/>.</param>
/// <param name="DependencyFilePath">See <see cref="MergeCheckRequest.DependencyFilePath"/>.</param>
/// <param name="DependencyFileContent">See <see cref="MergeCheckRequest.DependencyFileContent"/>.</param>
public sealed record Verdict(
    string Kind, string Trunk, string TrunkSha, string? Branch, string? BranchSha, IReadOnlyList<string> Files,
    bool? HoldsTrunk = null, string? PlaybookFilePath = null, string? DependencyFilePath = null,
    string? DependencyFileContent = null)
{
    /// <summary>The wire form, under the remote as this runner spells it.</summary>
    public MergeCheckRequest ToRequest(string remote, string runner) =>
        new(remote, Trunk, TrunkSha, Kind, Branch, BranchSha, Files, runner, HoldsTrunk, PlaybookFilePath,
            DependencyFilePath, DependencyFileContent);

    /// <summary>
    /// The one line the terminal says when a verdict changes: <c>HA-12 conflicts
    /// with main (3 files)</c>.
    /// </summary>
    public string Words(string key) => Kind switch
    {
        MergeVerdicts.Conflicted =>
            $"{key} conflicts with {Trunk} ({Files.Count} file{(Files.Count == 1 ? "" : "s")})",
        MergeVerdicts.Clean => $"{key} merges cleanly with {Trunk}",
        MergeVerdicts.Ambiguous => $"{key} has more than one branch on origin",
        _ => $"{key} has no branch on origin",
    };

    /// <summary>Whether the board's stored verdict already says this - the kind, and for a conflict the files.</summary>
    public bool Says(MergeCheckDto stored) =>
        stored.Verdict == Kind && stored.Files.Order(StringComparer.Ordinal).SequenceEqual(Files.Order(StringComparer.Ordinal));
}

/// <summary>What <see cref="IWorkspace.Sync"/> came to.</summary>
public enum SyncOutcome
{
    /// <summary>This git is too old for <c>merge-tree --write-tree</c>.</summary>
    OldGit,

    /// <summary>No trunk could be named.</summary>
    NoTrunk,

    /// <summary>The fetch that makes the answer current did not go.</summary>
    FetchFailed,

    /// <summary>Origin has no branch for this key.</summary>
    NoBranch,

    /// <summary>Origin has more than one branch for this key.</summary>
    Ambiguous,

    /// <summary>The branch already holds the trunk - nothing to merge.</summary>
    Current,

    /// <summary>The trunk does not merge into the branch.</summary>
    Conflicted,

    /// <summary><c>merge-tree</c> itself did not answer.</summary>
    MergeFailed,

    /// <summary>The merge commit would not write.</summary>
    CommitFailed,

    /// <summary>Pushed to origin.</summary>
    Pushed,

    /// <summary>Origin refused the push.</summary>
    Refused,
}

/// <summary>What <see cref="IWorkspace.Sync"/> found and did, in one of <see cref="SyncOutcome"/>.</summary>
/// <param name="Branches">Every branch on origin naming the key, for <see cref="SyncOutcome.Ambiguous"/>.</param>
/// <param name="RefetchFailed">
/// On <see cref="SyncOutcome.Pushed"/>, whether the remote-tracking ref did not
/// move on its own and the fetch meant to catch it up also failed.
/// </param>
public sealed record Synced(
    SyncOutcome Outcome, string? Trunk = null, string? Branch = null, string? Sha = null,
    IReadOnlyList<string>? Files = null, IReadOnlyList<string>? Branches = null,
    bool RefetchFailed = false, string? Why = null);

/// <summary>
/// Asking git whether an issue's branch still merges with the trunk, against
/// origin's refs - the runner's half of the merge check.
/// </summary>
/// <remarks>
/// <para>Nothing here touches a worktree or an index. <c>ls-remote</c> and
/// <c>fetch</c> move no branch a tree is standing on, and <c>merge-tree
/// --write-tree</c> writes objects only, so the tree stays on the trunk where
/// the end of the last increment left it - which is what lets the poll run
/// between passes without a lock of its own.</para>
///
/// <para>Deliberately not built on <c>Plan</c>. <c>Plan</c> prefers a local
/// branch that is ahead of origin and abbreviates its shas; a verdict is about
/// origin's branch, and two runners have to agree on the two full shas.</para>
/// </remarks>
public sealed partial class Workspace
{
    public RemoteHeads? Heads()
    {
        if (!Git("rev-parse", "--git-dir").Ok || BaseBranch() is not { Length: > 0 } trunk) return null;

        var listed = Git("ls-remote", "--heads", "origin");
        if (!listed.Ok) return null;

        const string prefix = "refs/heads/";
        var shas = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in listed.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !parts[1].StartsWith(prefix, StringComparison.Ordinal)) continue;

            shas[parts[1].Trim()[prefix.Length..]] = parts[0].Trim();
        }

        return new RemoteHeads(trunk, shas);
    }

    /// <summary>
    /// The call <see cref="Prepare"/> makes, and nothing else it does: every
    /// remote-tracking ref made current, and no branch or tree moved.
    /// </summary>
    public bool Fetch() => Git("fetch", "--quiet", "--prune", "origin").Ok;

    /// <summary>
    /// The verdict on this key's branch, from the remote-tracking refs as they
    /// stand - so it does not fetch. Null when it cannot be said: git older
    /// than <see cref="MergeTree"/>, no trunk, or a merge git would not answer.
    /// </summary>
    /// <param name="playbookFilePath">
    /// The path to check for a change between the trunk's tip and the branch's
    /// tip, reported back on the verdict when it changed. Null reports nothing,
    /// even when the branch does change it - only a caller that is asked
    /// reports it at all.
    /// </param>
    /// <param name="recordedBranch">
    /// The head branch the issue's contribution recorded, which stands in for a
    /// branch named for the key. Null reads the key-named rule.
    /// </param>
    public Verdict? Check(string key, string? playbookFilePath = null, string? recordedBranch = null)
    {
        if (!AtLeast(MergeTree))
        {
            SayOldGit("checking a branch against the trunk");
            return null;
        }

        if (BaseBranch() is not { Length: > 0 } trunk) return null;

        var trunkRef = $"refs/remotes/origin/{trunk}";
        if (FullSha(trunkRef) is not { } trunkSha) return null;

        var (open, _) = Branched(key, trunk, recordedBranch);

        if (open.Count == 0) return new Verdict(MergeVerdicts.None, trunk, trunkSha, null, null, []);
        if (open.Count > 1) return new Verdict(MergeVerdicts.Ambiguous, trunk, trunkSha, null, null, []);

        var branch = open[0];
        var branchRef = $"refs/remotes/origin/{branch}";
        if (FullSha(branchRef) is not { } branchSha) return null;

        var changedPlaybookFile = ChangedFile(trunkSha, branchSha, playbookFilePath);
        var changedDependencyFile = ChangedFile(trunkSha, branchSha, DependencyDeclaration.Path);
        var dependencyFileContent = changedDependencyFile is null ? null : DependencyFileContentAt(branch);

        // The trunk already in the branch is a clean merge by definition, and
        // says so without asking for the merge - the test Sync makes, so the
        // two cannot disagree about which branches need a merge commit.
        if (Git("merge-base", "--is-ancestor", trunkRef, branchRef).Ok)
            return new Verdict(
                MergeVerdicts.Clean, trunk, trunkSha, branch, branchSha, [], true,
                changedPlaybookFile, changedDependencyFile, dependencyFileContent);

        var merge = Git("merge-tree", "--write-tree", "--name-only", trunkRef, branchRef);
        if (merge.Ok)
            return new Verdict(
                MergeVerdicts.Clean, trunk, trunkSha, branch, branchSha, [], false,
                changedPlaybookFile, changedDependencyFile, dependencyFileContent);

        // Exit 1 is a conflict and the only exit that says so. A conflict with
        // no file named would be refused by the board and is not one to guess
        // at, so it is unknown like any other answer git did not give.
        if (merge.Code != 1) return null;

        var files = ConflictedFiles(merge.Out);
        return files.Count == 0
            ? null
            : new Verdict(
                MergeVerdicts.Conflicted, trunk, trunkSha, branch, branchSha, files, false,
                changedPlaybookFile, changedDependencyFile, dependencyFileContent);
    }

    private string? FullSha(string reference)
    {
        var sha = Git("rev-parse", "--verify", "--quiet", reference);
        return sha.Ok && sha.Out.Trim() is { Length: > 0 } text ? text : null;
    }

    /// <summary>
    /// <paramref name="path"/> itself when the branch's tip differs from the
    /// trunk's tip there, read from the two shas already in hand - no worktree
    /// touched, no second fetch. Null when no path was asked for, proving the
    /// caller and not the branch's own content decides whether anything is
    /// reported.
    /// </summary>
    private string? ChangedFile(string trunkSha, string branchSha, string? path) =>
        path is not null && Git("diff", "--name-only", trunkSha, branchSha, "--", path).Out.Trim().Length > 0
            ? path : null;

    /// <summary>
    /// The branch's own blob at <see cref="DependencyDeclaration.Path"/>, as
    /// text - null when it could not be read, is not valid UTF-8, or is over
    /// <see cref="DependencyDeclaration.MaxReportedContentLength"/>, so the
    /// board still learns the path changed without a blob it would have to
    /// cap itself.
    /// </summary>
    private string? DependencyFileContentAt(string branch)
    {
        var blob = ReadBlob(branch, DependencyDeclaration.Path);
        if (blob.Outcome != BlobOutcome.Found || blob.Content is not { } bytes) return null;
        if (bytes.Length > DependencyDeclaration.MaxReportedContentLength) return null;

        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }
}

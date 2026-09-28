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
    public string? Fingerprint(string key)
    {
        if (TrunkSha is not { } trunk) return null;

        var candidates = Shas
            .Where(b => b.Key != Trunk && Branches.Names(b.Key, key))
            .OrderBy(b => b.Key, StringComparer.Ordinal)
            .Select(b => $"{b.Key}:{b.Value}");

        return string.Join('\n', [trunk, .. candidates]);
    }

    /// <summary>The branches the key claims, trunk excluded, by name.</summary>
    public IReadOnlyList<string> Candidates(string key) =>
        Shas.Keys.Where(b => b != Trunk && Branches.Names(b, key)).Order(StringComparer.Ordinal).ToList();
}

/// <summary>
/// What a branch on origin comes to against the trunk on origin, as the board
/// keeps it - see <see cref="MergeVerdicts"/>.
/// </summary>
/// <param name="Kind">One of <see cref="MergeVerdicts"/>.</param>
/// <param name="TrunkSha">Full, so that two runners looking at the same two shas agree.</param>
/// <param name="BranchSha">Full. Null for the two verdicts that are not about one branch.</param>
public sealed record Verdict(
    string Kind, string Trunk, string TrunkSha, string? Branch, string? BranchSha, IReadOnlyList<string> Files)
{
    /// <summary>The wire form, under the remote as this runner spells it.</summary>
    public MergeCheckRequest ToRequest(string remote, string runner) =>
        new(remote, Trunk, TrunkSha, Kind, Branch, BranchSha, Files, runner);

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
    public Verdict? Check(string key)
    {
        if (!AtLeast(MergeTree))
        {
            SayOldGit("checking a branch against the trunk");
            return null;
        }

        if (BaseBranch() is not { Length: > 0 } trunk) return null;

        var trunkRef = $"refs/remotes/origin/{trunk}";
        if (FullSha(trunkRef) is not { } trunkSha) return null;

        var (open, _) = Branched(key, trunk);

        if (open.Count == 0) return new Verdict(MergeVerdicts.None, trunk, trunkSha, null, null, []);
        if (open.Count > 1) return new Verdict(MergeVerdicts.Ambiguous, trunk, trunkSha, null, null, []);

        var branch = open[0];
        var branchRef = $"refs/remotes/origin/{branch}";
        if (FullSha(branchRef) is not { } branchSha) return null;

        // The trunk already in the branch is a clean merge by definition, and
        // says so without asking for the merge - the test Sync makes, so the
        // two cannot disagree about which branches need a merge commit.
        if (Git("merge-base", "--is-ancestor", trunkRef, branchRef).Ok)
            return new Verdict(MergeVerdicts.Clean, trunk, trunkSha, branch, branchSha, []);

        var merge = Git("merge-tree", "--write-tree", "--name-only", trunkRef, branchRef);
        if (merge.Ok) return new Verdict(MergeVerdicts.Clean, trunk, trunkSha, branch, branchSha, []);

        // Exit 1 is a conflict and the only exit that says so. A conflict with
        // no file named would be refused by the board and is not one to guess
        // at, so it is unknown like any other answer git did not give.
        if (merge.Code != 1) return null;

        var files = ConflictedFiles(merge.Out);
        return files.Count == 0
            ? null
            : new Verdict(MergeVerdicts.Conflicted, trunk, trunkSha, branch, branchSha, files);
    }

    private string? FullSha(string reference)
    {
        var sha = Git("rev-parse", "--verify", "--quiet", reference);
        return sha.Ok && sha.Out.Trim() is { Length: > 0 } text ? text : null;
    }
}

namespace Hatch.Cli;

/// <summary>What origin answered when asked which branches it has: the trunk's name, and each branch's sha.</summary>
/// <param name="Shas">Every branch on origin, by name, with the trunk among them.</param>
public sealed record RemoteHeads(string Trunk, IReadOnlyDictionary<string, string> Shas);

/// <summary>
/// Whether an issue's branch on origin merges with the trunk, asked of the refs
/// as they stand and never of the tree.
/// </summary>
/// <remarks>
/// <para>Three calls, and the split between them is the point. <see
/// cref="Heads()"/> is one round trip that changes nothing here, so an idle loop
/// can ask it every interval; <see cref="Fetch"/> moves the remote-tracking refs
/// and is made only when <see cref="Heads()"/> said something moved; and <see
/// cref="Check"/> reads what a fetch left and does no network at all.</para>
///
/// <para>None of them touches a worktree or an index. A fetch writes refs and
/// objects, and <c>merge-tree --write-tree</c> writes objects, so the tree stays
/// on the trunk where the last increment left it - the poll runs between
/// increments, and would be no use if it could get in a session's way.</para>
/// </remarks>
public sealed partial class Workspace
{
    public RemoteHeads? Heads()
    {
        // Before the network is asked anything: a git that cannot check what it
        // would then be told is a round trip for nothing, every interval.
        if (!AtLeast(MergeTree))
        {
            SayOldGit("checking a branch against the trunk");
            return null;
        }

        if (BaseBranch() is not { Length: > 0 } trunk) return null;

        var heads = Git("ls-remote", "--heads", "origin");
        if (!heads.Ok) return null;

        var shas = new Dictionary<string, string>(StringComparer.Ordinal);
        const string prefix = "refs/heads/";
        foreach (var line in heads.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[1].StartsWith(prefix, StringComparison.Ordinal))
                shas[parts[1][prefix.Length..].TrimEnd()] = parts[0].Trim();
        }

        return new RemoteHeads(trunk, shas);
    }

    /// <summary>The fetch <see cref="Prepare"/> makes: every remote-tracking ref, and the ones whose branches went taken away. It leaves the worktree and the index alone.</summary>
    public bool Fetch() => Git("fetch", "--quiet", "--prune", "origin").Ok;

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

        // Not Plan, which prefers a local branch that is ahead of origin and
        // shortens its shas: a verdict is about the branch origin has, and two
        // runners agree on it by agreeing on the two full shas.
        var (open, _) = Branched(key.ToLowerInvariant(), trunk);
        if (open.Count == 0) return new Verdict(MergeVerdicts.None, trunk, trunkSha, null, null, []);
        if (open.Count > 1) return new Verdict(MergeVerdicts.Ambiguous, trunk, trunkSha, null, null, []);

        var branch = open[0];
        var branchRef = $"refs/remotes/origin/{branch}";
        if (FullSha(branchRef) is not { } branchSha) return null;

        if (Git("merge-base", "--is-ancestor", trunkRef, branchRef).Ok)
            return new Verdict(MergeVerdicts.Clean, trunk, trunkSha, branch, branchSha, []);

        var merge = Git("merge-tree", "--write-tree", "--name-only", trunkRef, branchRef);
        if (merge.Ok) return new Verdict(MergeVerdicts.Clean, trunk, trunkSha, branch, branchSha, []);
        if (merge.Code != 1) return null;

        // A conflict that names no file is one this cannot describe, and the
        // board refuses a conflicted verdict without files.
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

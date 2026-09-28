using System.Text.RegularExpressions;

namespace Hatch.Cli;

/// <summary>
/// The last half of the slate: what an increment leaves behind, put right by
/// the runner rather than by whichever session happens to be next.
/// </summary>
/// <remarks>
/// <para>Nothing here pushes a session's work. Whether a branch is fit to leave
/// origin is the session's call - green before pushed - so what the runner finds
/// that never left this machine is named on the ticket and left where it is.
/// The one push it does make is the trunk into a branch that has a pull request,
/// and that is made without a worktree, as a fast-forward, so it cannot
/// overwrite anything somebody else pushed in the meantime.</para>
///
/// <para>Nothing here fails an increment. A step that would not go is a line for
/// the terminal and a line for the ticket, and the ones after it still run: a
/// tree that could not be stashed is still worth putting back on the trunk.</para>
/// </remarks>
public sealed partial class Workspace
{
    /// <summary>The first git that has <c>merge-tree --write-tree</c>.</summary>
    private static readonly Version MergeTree = new(2, 38);

    private static int _oldGitSaid;

    public Leaving Leave(string key, bool syncPullRequest)
    {
        var notes = new List<string>();
        void Note(string line, bool bad = false)
        {
            (bad ? complain : say)($"hatch:   {line}");
            notes.Add(line);
        }

        if (!Git("rev-parse", "--git-dir").Ok || BaseBranch() is not { Length: > 0 } trunk)
            return new Leaving(root, notes);

        var lower = key.ToLowerInvariant();
        var trunkRef = $"refs/remotes/origin/{trunk}";

        // Nothing is left in the middle of a merge, a rebase or a cherry-pick:
        // the next increment starts by putting the tree on the trunk, and none
        // of the three can be stashed - so, left, they would end the night.
        foreach (var (what, present, abort) in InProgress())
        {
            if (!present) continue;

            if (Git(abort).Ok) Note($"a {what} was left in progress and has been aborted");
            else Note($"a {what} was left in progress and would not abort - the tree needs a person", bad: true);
        }

        if (MarkerPath() is { } marker && File.Exists(marker) && !MergeInProgress()) File.Delete(marker);

        var dirty = Git("status", "--porcelain");
        if (dirty.Ok && dirty.Out.Trim().Length > 0)
        {
            var files = dirty.Out.Trim().Split('\n').Length;
            var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            if (Git("stash", "push", "--include-untracked", "--quiet", "--message", $"hatch: {key} left uncommitted at {stamp}").Ok)
                Note($"{files} uncommitted file(s) were stashed as \"hatch: {key} left uncommitted at {stamp}\" - git stash pop takes them back");
            else
                Note($"{files} uncommitted file(s) would not stash - they are still in the tree", bad: true);
        }

        // Commits the session made and did not push. Read from the local
        // branches, against whatever origin has for each; a branch origin does
        // not have at all is counted against the trunk.
        foreach (var branch in LocalBranches(lower))
        {
            var upstream = $"refs/remotes/origin/{branch}";
            var ahead = Count(Exists(upstream) ? $"{upstream}..refs/heads/{branch}" : $"{trunkRef}..refs/heads/{branch}");
            if (ahead > 0)
                Note($"{branch} has {ahead} commit(s) origin does not, tip {Short($"refs/heads/{branch}")} - not pushed; that is for whoever checks it is green");
        }

        // Commits on the local trunk are the ones nothing else names, and the
        // return to the trunk moves it off them. A branch of their own is
        // cheaper than the reflog, which is where they would otherwise be.
        if (Exists($"refs/heads/{trunk}") && Count($"{trunkRef}..refs/heads/{trunk}") is var stranded and > 0)
        {
            var rescued = $"{lower}-rescued-{Short($"refs/heads/{trunk}")}";
            if (Git("branch", rescued, $"refs/heads/{trunk}").Ok)
                Note($"{stranded} commit(s) were left on the local {trunk} and are now on {rescued}");
            else
                Note($"{stranded} commit(s) are on the local {trunk} that origin does not have, and would not move to a branch of their own", bad: true);
        }

        if (!Git("checkout", "--quiet", "-B", trunk, trunkRef).Ok)
            Note($"the tree would not go back to {trunk}", bad: true);

        // What origin's branch looks like now, when Sync fetched and so the refs
        // are current - whatever Sync then did or declined to do, which is the
        // one place every outcome can be read the same way. Nothing is known
        // where it did not fetch, and nothing is claimed.
        var found = syncPullRequest && Sync(lower, trunk, Note) ? Check(lower) : null;

        return new Leaving(root, notes, found);
    }

    public void Return()
    {
        if (BaseBranch() is not { Length: > 0 } trunk || !SettleMerge()) return;

        // Not forced: a tree with changes in it is somebody's, and the next
        // increment's reset is where a stash is taken.
        if (!Git("checkout", "--quiet", "-B", trunk, $"refs/remotes/origin/{trunk}").Ok)
            complain($"hatch:   the tree would not go back to {trunk} - the next reset will deal with it");
    }

    /// <summary>The three things git leaves half-done, whether each is there, and what undoes it.</summary>
    private IEnumerable<(string What, bool Present, string[] Abort)> InProgress()
    {
        bool Has(string name) => GitPath(name) is { } path && (File.Exists(path) || Directory.Exists(path));

        yield return ("merge", MergeInProgress(), ["merge", "--abort"]);
        yield return ("rebase", Has("rebase-merge") || Has("rebase-apply"), ["rebase", "--abort"]);
        yield return ("cherry-pick", Has("CHERRY_PICK_HEAD"), ["cherry-pick", "--abort"]);
    }

    /// <summary>The local branches that name the key, and are not the trunk's own.</summary>
    private IEnumerable<string> LocalBranches(string lowerKey)
    {
        var refs = Git("for-each-ref", "--format=%(refname:strip=2)", "refs/heads");
        if (!refs.Ok) yield break;

        foreach (var name in refs.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            if (Branches.Names(name, lowerKey)) yield return name;
    }

    /// <summary>
    /// The trunk into the pull request's branch on origin, when it is not there
    /// already. A merge git can do without a worktree - <c>merge-tree</c> for the
    /// result, <c>commit-tree</c> for the commit, and a push that is a
    /// fast-forward or nothing.
    /// </summary>
    /// <returns>Whether origin was fetched, so that the remote-tracking refs are as origin has them.</returns>
    private bool Sync(string lowerKey, string trunk, Action<string, bool> note)
    {
        if (!AtLeast(MergeTree))
        {
            SayOldGit("merging a pull request's branch without a worktree");
            return false;
        }

        // Fetched again: the session may have pushed, and the answer about what
        // origin's branch contains has to be about origin's branch as it is.
        if (!Git("fetch", "--quiet", "--prune", "origin").Ok)
        {
            note("could not fetch from origin, so the pull request's branch was not brought up to date", true);
            return false;
        }

        var (open, _) = Branched(lowerKey, trunk);
        if (open.Count != 1)
        {
            if (open.Count > 1)
                note($"more than one branch on origin names this issue ({string.Join(", ", open)}), so which is the pull request's was not guessed", true);
            return true;
        }

        var branch = open[0];
        var remote = $"refs/remotes/origin/{branch}";
        var trunkRef = $"refs/remotes/origin/{trunk}";

        if (Git("merge-base", "--is-ancestor", trunkRef, remote).Ok) return true;

        var merge = Git("merge-tree", "--write-tree", "--name-only", trunkRef, remote);
        if (merge.Code == 1)
        {
            var files = ConflictedFiles(merge.Out);
            note($"{trunk} does not merge into {branch} on origin - conflicts in {string.Join(", ", files)}; nothing was pushed", true);
            return true;
        }

        if (!merge.Ok)
        {
            note($"could not work out a merge of {trunk} into {branch}: {merge.Why}", true);
            return true;
        }

        var tree = merge.Out.Split('\n')[0].Trim();
        var commit = Git("commit-tree", tree, "-p", remote, "-p", trunkRef, "-m", $"Merge branch '{trunk}' into {branch}");
        if (!commit.Ok)
        {
            note($"could not commit a merge of {trunk} into {branch}: {commit.Why}", true);
            return true;
        }

        var sha = commit.Out.Trim();
        var push = Git("push", "--quiet", "origin", $"{sha}:refs/heads/{branch}");
        if (push.Ok) note($"{trunk} was merged into {branch} on origin, now at {sha[..Math.Min(9, sha.Length)]}", false);
        else note($"the push of {trunk} merged into {branch} was refused - {push.Why}; the pull request's branch is as it was", true);

        return true;
    }

    /// <summary>
    /// Once, not once per checkout per increment: it is the same news every
    /// time, and the tree is the same tree.
    /// </summary>
    /// <param name="what">What could not be done, as the tail of "which is what ... needs".</param>
    private void SayOldGit(string what)
    {
        if (Interlocked.Exchange(ref _oldGitSaid, 1) == 0)
            complain($"hatch:   this git is older than {MergeTree}, which is what {what} needs - not doing it");
    }

    private bool AtLeast(Version floor)
    {
        var text = Git("version").Out;
        var match = Regex.Match(text, @"(\d+)\.(\d+)");
        return match.Success && new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value)) >= floor;
    }
}

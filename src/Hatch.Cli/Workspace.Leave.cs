using System.Text.RegularExpressions;

namespace Hatch.Cli;

/// <summary>
/// The last half of the slate: what an increment leaves behind, put right by
/// the runner rather than by whichever session happens to be next.
/// </summary>
/// <remarks>
/// <para>Whether a branch is fit to leave origin is the session's call - green
/// before pushed - but a session that held its push has not made the commits
/// safe, and commits that exist only on this machine are one disk away from
/// gone. So the runner pushes the issue's own branch when the increment ends, as
/// a backstop, under the same name and with no force: a push somebody else got in
/// front of is refused and named, never overwritten. It never pushes the trunk,
/// nor a <c>&lt;key&gt;-rescued-&lt;sha&gt;</c> branch, which would be a second
/// unmerged branch naming the issue on origin and leave which one is the issue's
/// to a guess. The other push it makes is the trunk into a branch that has a pull
/// request, without a worktree, as a fast-forward.</para>
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

        PushUnpublished(lower, trunk, trunkRef, Note);

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

        // Whether origin's branch now conflicts is reported from here too, so a
        // pull request that conflicts the moment it opens is conflict work on
        // the next pass and does not wait for the poll. The merge-forward stays
        // gated on a pull request being recorded; the verdict itself does not -
        // Check reads local remote-tracking refs, which are current for a branch
        // this same tree just pushed, pull request or not.
        if (syncPullRequest) NoteSync(Sync(lower), Note);
        var found = Check(lower);

        return new Leaving(root, notes, found);
    }

    public bool TreeUnchanged(string head)
    {
        var dirty = Git("status", "--porcelain");
        return dirty.Ok && dirty.Out.Trim().Length == 0 && Short("HEAD") == head;
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

    /// <summary>
    /// Pushes the issue's branch where it has commits origin does not. A branch
    /// origin already has is pushed under its own name; one it lacks is created
    /// only when it is the one branch with unpublished work and origin has no open
    /// branch for the issue, since a second would leave which is the issue's to a
    /// guess. A refusal is a line, and the commits stay where they are.
    /// </summary>
    private void PushUnpublished(string lower, string trunk, string trunkRef, Action<string, bool> note)
    {
        var rescued = new Regex($"^{Regex.Escape(lower)}-rescued-[0-9a-f]{{7,40}}$");
        var unpublished = new List<(string Branch, int Ahead, bool OnOrigin)>();

        // Read from the local branches, against whatever origin has for each; a
        // branch origin does not have at all is counted against the trunk.
        foreach (var branch in LocalBranches(lower))
        {
            if (branch == trunk || rescued.IsMatch(branch)) continue;

            var upstream = $"refs/remotes/origin/{branch}";
            var onOrigin = Exists(upstream);
            var ahead = Count(onOrigin ? $"{upstream}..refs/heads/{branch}" : $"{trunkRef}..refs/heads/{branch}");
            if (ahead > 0) unpublished.Add((branch, ahead, onOrigin));
        }

        if (unpublished.Count == 0) return;

        var open = Branched(lower, trunk).Open;
        var lacking = unpublished.Where(u => !u.OnOrigin).Select(u => u.Branch).ToList();

        foreach (var (branch, ahead, onOrigin) in unpublished)
        {
            var local = $"refs/heads/{branch}";
            var has = $"{branch} has {ahead} commit(s) origin does not, tip {Short(local)}";

            if (!onOrigin && open.Count > 0)
            {
                note($"{has} - not pushed; origin already has {string.Join(", ", open)} for this issue, and a second branch was not made", false);
                continue;
            }

            if (!onOrigin && unpublished.Count > 1)
            {
                // One line for all of them, said by the first.
                if (branch == lacking[0])
                    note($"{lacking.Count} local branches name this issue and origin has none of them ({string.Join(", ", lacking)}), so which is the issue's branch was not guessed - not pushed", false);
                continue;
            }

            var push = Git("push", "--quiet", "origin", $"{local}:{local}");
            if (!push.Ok)
            {
                note($"{has} - the push was refused ({push.Why}); they are still only on this machine", true);
                continue;
            }

            note($"{has} - the runner pushed them, because the session did not", false);

            // Check reads the remote-tracking refs, and a push by full ref name
            // is not what every git updates them on - fetched once where it did not.
            if (FullSha($"refs/remotes/origin/{branch}") != FullSha(local) && !Fetch())
                note($"could not fetch {branch} after pushing it, so what origin's branch now looks like is not known", true);
        }
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
    /// The trunk into the issue's branch on origin, when it is not there
    /// already. A merge git can do without a worktree - <c>merge-tree</c> for the
    /// result, <c>commit-tree</c> for the commit, and a push that is a
    /// fast-forward or nothing.
    /// </summary>
    public Synced Sync(string key)
    {
        if (!AtLeast(MergeTree))
        {
            SayOldGit("merging a pull request's branch without a worktree");
            return new Synced(SyncOutcome.OldGit);
        }

        if (BaseBranch() is not { Length: > 0 } trunk) return new Synced(SyncOutcome.NoTrunk);

        var lowerKey = key.ToLowerInvariant();

        // Fetched again: the session may have pushed, and the answer about what
        // origin's branch contains has to be about origin's branch as it is.
        if (!Git("fetch", "--quiet", "--prune", "origin").Ok)
            return new Synced(SyncOutcome.FetchFailed, trunk);

        var (open, _) = Branched(lowerKey, trunk);
        if (open.Count == 0) return new Synced(SyncOutcome.NoBranch, trunk);
        if (open.Count > 1) return new Synced(SyncOutcome.Ambiguous, trunk, Branches: open);

        var branch = open[0];
        var remote = $"refs/remotes/origin/{branch}";
        var trunkRef = $"refs/remotes/origin/{trunk}";

        if (Git("merge-base", "--is-ancestor", trunkRef, remote).Ok) return new Synced(SyncOutcome.Current, trunk, branch);

        var merge = Git("merge-tree", "--write-tree", "--name-only", trunkRef, remote);
        if (merge.Code == 1)
        {
            var files = ConflictedFiles(merge.Out);
            return new Synced(SyncOutcome.Conflicted, trunk, branch, Files: files);
        }

        if (!merge.Ok) return new Synced(SyncOutcome.MergeFailed, trunk, branch, Why: merge.Why);

        var tree = merge.Out.Split('\n')[0].Trim();
        var commit = Git("commit-tree", tree, "-p", remote, "-p", trunkRef, "-m", $"Merge branch '{trunk}' into {branch}");
        if (!commit.Ok) return new Synced(SyncOutcome.CommitFailed, trunk, branch, Why: commit.Why);

        var sha = commit.Out.Trim();
        var push = Git("push", "--quiet", "origin", $"{sha}:refs/heads/{branch}");
        if (!push.Ok) return new Synced(SyncOutcome.Refused, trunk, branch, Sha: sha, Why: push.Why);

        // The remote-tracking ref is what the verdict is read from, and a
        // push by sha to a full ref name is not what every git updates it
        // on. Asked, and fetched once more only where it did not move.
        var refetchFailed = FullSha(remote) != sha && !Fetch();
        return new Synced(SyncOutcome.Pushed, trunk, branch, Sha: sha, RefetchFailed: refetchFailed);
    }

    /// <summary>Today's six <c>Leave</c> notes, translated from <see cref="Sync"/>'s outcome.</summary>
    private static void NoteSync(Synced synced, Action<string, bool> note)
    {
        switch (synced.Outcome)
        {
            case SyncOutcome.FetchFailed:
                note("could not fetch from origin, so the pull request's branch was not brought up to date", true);
                break;

            case SyncOutcome.Ambiguous:
                note($"more than one branch on origin names this issue ({string.Join(", ", synced.Branches!)}), so which is the pull request's was not guessed", true);
                break;

            case SyncOutcome.Conflicted:
                note($"{synced.Trunk} does not merge into {synced.Branch} on origin - conflicts in {string.Join(", ", synced.Files!)}; nothing was pushed", true);
                break;

            case SyncOutcome.MergeFailed:
                note($"could not work out a merge of {synced.Trunk} into {synced.Branch}: {synced.Why}", true);
                break;

            case SyncOutcome.CommitFailed:
                note($"could not commit a merge of {synced.Trunk} into {synced.Branch}: {synced.Why}", true);
                break;

            case SyncOutcome.Pushed:
                note($"{synced.Trunk} was merged into {synced.Branch} on origin, now at {synced.Sha![..Math.Min(9, synced.Sha!.Length)]}", false);
                if (synced.RefetchFailed)
                    note($"could not fetch {synced.Branch} after pushing it, so what origin's branch now looks like is not known", true);
                break;

            case SyncOutcome.Refused:
                note($"the push of {synced.Trunk} merged into {synced.Branch} was refused - {synced.Why}; the pull request's branch is as it was", true);
                break;

            // OldGit, NoTrunk, NoBranch, Current: silent, as today.
        }
    }

    /// <summary>
    /// Says once per run that this git cannot do what the runner was about to,
    /// and what that was. Once and not once per checkout per increment: it is
    /// the same news every time, and the tree is the same tree.
    /// </summary>
    private void SayOldGit(string couldNot)
    {
        if (Interlocked.Exchange(ref _oldGitSaid, 1) == 0)
            complain($"hatch:   this git is older than {MergeTree}, which is what {couldNot} needs - not doing it");
    }

    private bool AtLeast(Version floor)
    {
        var text = Git("version").Out;
        var match = Regex.Match(text, @"(\d+)\.(\d+)");
        return match.Success && new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value)) >= floor;
    }
}

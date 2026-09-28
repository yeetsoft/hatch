using System.Diagnostics;

namespace Hatch.Cli;

/// <summary>What a reset came to, and what the loop should do about it.</summary>
public enum Reset
{
    /// <summary>The tree is on the trunk, at the remote's tip.</summary>
    Ready,

    /// <summary>Something transient - the fetch did not answer. Worth asking again.</summary>
    Later,

    /// <summary>Something that will not fix itself. Worth stopping for.</summary>
    Never,
}

/// <summary>
/// Making the tree current, behind an interface - so that the order a pass does
/// things in can be asserted without a repository, a remote and a network. That
/// order is the point: the ticket is claimed before the fetch, and the lease is
/// given back even when the fetch is what went wrong.
/// </summary>
public interface IWorkspace
{
    /// <param name="stash">
    /// Whether a dirty tree is stashed, which is what an unattended loop does,
    /// or refused with a sentence naming the files, which is what a person
    /// running one increment by hand is owed.
    /// </param>
    Reset Prepare(bool stash = true);

    /// <summary>
    /// Put the tree on the issue's branch on origin, with the trunk merged in -
    /// or leave it on the trunk and say what the session is to cut. Called with
    /// the tree already on the trunk.
    /// </summary>
    /// <param name="answer">A person's answer to which of several branches to use, if one has been given.</param>
    BranchEntry Enter(string key, string title, string? answer);

    /// <summary>What <see cref="Enter"/> would do, read from the refs as they stand. Changes nothing, and fetches nothing.</summary>
    BranchEntry Plan(string key, string title, string? answer);

    /// <summary>
    /// Leave the tree the way the next increment expects to find it: nothing
    /// half-done, nothing uncommitted, on the trunk - with what was found
    /// along the way said as lines for the ticket.
    /// </summary>
    /// <param name="syncPullRequest">The issue has a pull request, so its branch on origin is brought up to date with the trunk.</param>
    Leaving Leave(string key, bool syncPullRequest);

    /// <summary>
    /// The trunk's name, and every branch origin has - one <c>ls-remote</c>,
    /// which fetches nothing and changes nothing here. Null where origin did not
    /// answer, or where this git cannot check a branch against the trunk.
    /// </summary>
    RemoteHeads? Heads();

    /// <summary>Fetch from origin, moving the remote-tracking refs and touching no worktree or index. Whether it went.</summary>
    bool Fetch();

    /// <summary>
    /// Whether the issue's branch on origin merges with the trunk, read from
    /// the remote-tracking refs as they stand - so after a <see cref="Fetch"/>,
    /// and without one of its own. Null where that cannot be said: a git too old
    /// to have <c>merge-tree --write-tree</c>, or a merge it could not judge.
    /// </summary>
    Verdict? Check(string key);

    /// <summary>
    /// Back on the trunk and nothing else - what an increment whose lease went
    /// to another runner is owed, which is not the writes <see cref="Leave"/> makes.
    /// </summary>
    void Return();
}

/// <summary>
/// The slate every increment starts on: the trunk, as the remote has it now.
/// </summary>
/// <remarks>
/// <para>This is the loop's guarantee rather than a session's good intentions.
/// A playbook can say "fetch first, then branch from the trunk" and be edited
/// by an operator who did not know that sentence was load-bearing, or read by a
/// session already most of the way through something else. What a night of
/// unattended increments cannot have is one run cutting its branch off the last
/// run's leftovers - so the tree is made current here, before anything is
/// spawned, and a playbook that says nothing at all about git still gets a
/// correct base.</para>
///
/// <para>Nothing here destroys work that is not recoverable. Uncommitted and
/// untracked changes go into a stash, named for the hour it was taken, and stay
/// on this machine - because whatever is in the tree at two in the morning was
/// probably left there by a person, and <c>git stash pop</c> is how they get it
/// back. Committed work is never at risk from a checkout: branches are refs,
/// and the branch the last increment pushed is still under its own name. The
/// exception is a commit sitting on the trunk and nowhere else, which the reset
/// moves off - that is what the "ahead" line is for, said out loud while there
/// is still something to count, with the reflog holding the commits
/// themselves.</para>
/// </remarks>
public sealed partial class Workspace(string root, string? configuredBase, Action<string> say, Action<string> complain)
    : IWorkspace
{
    /// <summary>
    /// What a branch is cut from, and the one thing about it that cannot be
    /// written down in this repository: a repository's trunk is called whatever
    /// its operator calls it.
    /// </summary>
    /// <remarks>
    /// <c>origin/HEAD</c> is what a clone recorded and is a local read, so it is
    /// asked first. Some checkouts never got one - an <c>init</c> and a
    /// <c>remote add</c>, or a mirror that did not publish a default - and for
    /// those the remote is asked directly, which is a round trip and so is the
    /// fallback rather than the rule. <c>HATCH_BASE_BRANCH</c> settles it
    /// without either.
    /// </remarks>
    public string? BaseBranch()
    {
        if (!string.IsNullOrWhiteSpace(configuredBase)) return configuredBase;

        // A clone writes refs/remotes/origin/<name> here. Anything else was
        // written by hand and is not a branch name this can take the tail of -
        // a name may have slashes in it, so there is no guessing at where one
        // starts - and the remote is asked instead.
        var symbolic = Git("symbolic-ref", "--quiet", "refs/remotes/origin/HEAD");
        if (symbolic.Ok)
        {
            var name = symbolic.Out.Trim();
            const string prefix = "refs/remotes/origin/";
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                name = name[prefix.Length..];
                if (!name.StartsWith("refs/", StringComparison.Ordinal) && name.Length > 0) return name;
            }
        }

        var remote = Git("ls-remote", "--symref", "origin", "HEAD");
        if (!remote.Ok) return null;

        foreach (var line in remote.Out.Split('\n'))
        {
            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[0].Trim() != "ref:") continue;

            var name = parts[1].Trim();
            const string heads = "refs/heads/";
            if (name.StartsWith(heads, StringComparison.Ordinal)) return name[heads.Length..];
        }

        return null;
    }

    /// <summary>Put the tree on the trunk, at the remote's tip.</summary>
    public Reset Prepare(bool stash = true)
    {
        if (!Git("rev-parse", "--git-dir").Ok)
        {
            complain($"hatch: {root} is not a git repository, so there is no trunk to reset to");
            return Reset.Never;
        }

        // The fetch comes before the trunk is named, and the order is the whole
        // difference between waiting a minute and ending a night. Naming the
        // trunk can need the remote too - a checkout with no origin/HEAD asks it
        // - and a remote that cannot be reached would come back from there as
        // "there is no trunk", which reads as a broken repository and stops the
        // loop. Asked in this order, an unreachable origin is always the fetch's
        // answer.
        //
        // Every remote-tracking ref and not only the trunk's: an increment that
        // goes looking for what a sibling ticket landed reads a ref that is
        // current, and --prune takes away the ones whose branches went when
        // their pull request merged.
        if (!Git("fetch", "--quiet", "--prune", "origin").Ok)
        {
            complain("hatch: could not fetch from origin");
            return Reset.Later;
        }

        if (BaseBranch() is not { Length: > 0 } trunk)
        {
            complain("hatch: cannot tell which branch is the trunk here - name it in HATCH_BASE_BRANCH");
            return Reset.Never;
        }

        if (!Git("rev-parse", "--verify", "--quiet", $"refs/remotes/origin/{trunk}").Ok)
        {
            complain($"hatch: origin has no {trunk} - HATCH_BASE_BRANCH names the trunk if it is called something else");
            return Reset.Never;
        }

        // Before the tree is looked at for changes: a merge left half-done
        // shows up there as a wall of conflicted files, and is not something a
        // stash can take. The runner's own is taken back; anybody else's is not
        // the runner's to abort.
        if (!SettleMerge()) return Reset.Never;

        if (!(stash ? Stash() : Refuse())) return Reset.Never;

        if (Git("rev-parse", "--verify", "--quiet", $"refs/heads/{trunk}").Ok)
        {
            var ahead = Git("rev-list", "--count", $"origin/{trunk}..{trunk}");
            if (ahead.Ok && int.TryParse(ahead.Out.Trim(), out var count) && count > 0)
                say($"hatch:   {trunk} was {count} commit(s) ahead of origin and is not now - git reflog has them");
        }

        // "HEAD" is what rev-parse calls a detached one, and "was HEAD" is not
        // a sentence anybody can act on.
        var head = Git("rev-parse", "--abbrev-ref", "HEAD").Out.Trim();
        if (head == "HEAD") head = "a detached head";

        // -B rather than a checkout and then a reset: it creates the trunk in a
        // checkout that never had it, moves it in one that has drifted, and
        // lands on it either way.
        if (!Git("checkout", "--quiet", "-B", trunk, $"refs/remotes/origin/{trunk}").Ok)
        {
            complain($"hatch: could not put the tree on {trunk}");
            return Reset.Never;
        }

        var at = Git("rev-parse", "--short", "HEAD").Out.Trim();
        say($"hatch:   workspace on {trunk} at {at}{(head.Length > 0 ? $", was {head}" : "")}");

        // Last, and after the checkout: the branch this is standing on is the
        // trunk by then, so the two branches that must survive are surviving by
        // where the call sits rather than by anything it checks.
        PruneGone(trunk);

        return Reset.Ready;
    }

    /// <summary>
    /// The refusal a person is owed instead of a stash: they are sitting there,
    /// and the changes in the tree are theirs to look at.
    /// </summary>
    private bool Refuse()
    {
        var dirty = Git("status", "--porcelain");
        if (!dirty.Ok || dirty.Out.Trim().Length == 0) return true;

        var files = dirty.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Length > 3 ? l[3..] : l).ToList();
        var named = string.Join(", ", files.Take(5));
        if (files.Count > 5) named += $", and {files.Count - 5} more";

        complain($"hatch: the tree has changes in it ({named}) - commit or stash them first; this is not stashing them for you");
        return false;
    }

    /// <summary>
    /// A merge in progress, and whether it is the runner's: it wrote a marker
    /// before it started one, so a merge that is there with the marker was
    /// interrupted, and one that is there without was somebody's.
    /// </summary>
    /// <remarks>
    /// The marker is cleared whenever no merge is in progress, so one that
    /// outlived its merge - the session resolved it and committed - does not
    /// make a later merge by a person look like the runner's.
    /// </remarks>
    private bool SettleMerge()
    {
        var marker = MarkerPath();
        var merging = MergeInProgress();

        if (!merging)
        {
            if (marker is not null && File.Exists(marker)) File.Delete(marker);
            return true;
        }

        if (marker is null || !File.Exists(marker))
        {
            complain("hatch: a merge is in progress in the tree and hatch did not start it - finish it or abort it by hand");
            return false;
        }

        if (!Git("merge", "--abort").Ok)
        {
            complain("hatch: a merge the loop started was never finished, and would not abort - abort it by hand");
            return false;
        }

        File.Delete(marker);
        say("hatch:   aborted a merge the loop started and never finished");
        return true;
    }

    private bool MergeInProgress() => GitPath("MERGE_HEAD") is { } head && File.Exists(head);

    private string? MarkerPath() => GitPath("hatch-merge");

    /// <summary>A file under the git directory - which is not <c>.git</c> in a worktree or a submodule.</summary>
    private string? GitPath(string name)
    {
        var path = Git("rev-parse", "--git-path", name);
        if (!path.Ok) return null;

        var text = path.Out.Trim();
        return text.Length == 0 ? null : System.IO.Path.GetFullPath(text, root);
    }

    /// <summary>
    /// Everything in the tree, tracked or not, ignored files aside - so a stash
    /// never swallows node_modules or a .env. Named for where it came from,
    /// because a stash list read a week later is otherwise a column of "WIP on
    /// main".
    /// </summary>
    private bool Stash()
    {
        var dirty = Git("status", "--porcelain");
        if (!dirty.Ok || dirty.Out.Trim().Length == 0) return true;

        var count = dirty.Out.Trim().Split('\n').Length;
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        if (!Git("stash", "push", "--include-untracked", "--quiet", "--message", $"hatch: workspace at {stamp}").Ok)
        {
            complain("hatch: the tree has changes in it that would not stash - clear them by hand");
            return false;
        }

        say($"hatch:   stashed {count} change(s) the tree was carrying - git stash pop takes them back");
        return true;
    }

    /// <summary>
    /// The branches whose pull requests merged, and which origin has since
    /// dropped.
    /// </summary>
    /// <remarks>
    /// <para>A checkout that has run a week of increments has a branch for every
    /// ticket it ever worked. This reads what the fetch established and takes
    /// those away; it is housekeeping, and it runs where housekeeping is safe,
    /// which is standing on the trunk with the tree already stashed clean.</para>
    ///
    /// <para><c>%(upstream:track)</c> is the entire rule. It is <c>[gone]</c>
    /// for a branch that had an upstream and has not got one now, and empty for
    /// a branch that never had one - and that second case is the one worth being
    /// careful about. A rule written as "is this name on origin" deletes
    /// somebody's half-finished local work, because unfinished and merged look
    /// identical from there.</para>
    ///
    /// <para><c>-D</c> rather than <c>-d</c>: a squash merge leaves the branch's
    /// commits nowhere in the trunk's history, so the merge check refuses on
    /// exactly the branches this is here to delete. The sha goes on the terminal
    /// instead, and <c>git branch &lt;name&gt; &lt;sha&gt;</c> is how one comes
    /// back.</para>
    ///
    /// <para>Nothing here fails a reset. A branch that will not delete - checked
    /// out in another worktree, most likely - is a line on the terminal, not a
    /// reason to end a night.</para>
    /// </remarks>
    private void PruneGone(string trunk)
    {
        var refs = Git(
            "for-each-ref", "--format=%(refname:short) %(objectname:short) %(upstream) %(upstream:track)",
            "refs/heads");
        if (!refs.Ok) return;

        foreach (var line in refs.Out.Split('\n'))
        {
            // Track last, because it is the only one of the four that can
            // contain a space; a branch with no upstream leaves the last two
            // empty.
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) continue;

            var (name, sha, upstream, track) = (parts[0], parts[1], parts[2], parts[3]);
            if (track != "[gone]") continue;
            if (!upstream.StartsWith("refs/remotes/origin/", StringComparison.Ordinal)) continue;

            // The trunk cannot read [gone] - origin has it, which was checked
            // above - and git will not delete the branch it is standing on
            // either way. The line is here so the rule says so where somebody
            // reads the rule.
            if (name == trunk) continue;

            if (Git("branch", "--delete", "--force", "--quiet", name).Ok)
                say($"hatch:   deleted {name}, was {sha} - origin dropped it when it merged");
            else
                complain($"hatch:   {name} is gone from origin and would not delete - it is still here");
        }
    }

    private readonly record struct Ran(int Code, string Out, string Err)
    {
        public bool Ok => Code == 0;

        /// <summary>What git complained of, first line, for a sentence that is not a transcript.</summary>
        public string Why => Err.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
    }

    private Ran Git(params string[] args)
    {
        var start = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(start);
            if (process is null) return new Ran(-1, "", "");

            // Both at once: a command with more to say on stderr than a pipe
            // holds would otherwise wait for a reader that is busy on stdout.
            var stderr = process.StandardError.ReadToEndAsync();
            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return new Ran(process.ExitCode, stdout, stderr.GetAwaiter().GetResult());
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new Ran(-1, "", "");
        }
    }
}

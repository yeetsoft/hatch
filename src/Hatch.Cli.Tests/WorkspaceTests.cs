using System.Diagnostics;

namespace Hatch.Cli.Tests;

/// <summary>
/// A bare origin, the checkout the runner serves, and a second clone standing in
/// for whoever else pushes - real git, because what the branch step has to get
/// right is git's own behaviour: what a squash merge looks like from the trunk,
/// what a conflicted merge leaves in the tree, what a fast-forward push refuses.
/// A fake would only agree with the person who wrote it.
/// </summary>
/// <remarks>
/// Split over three classes by what they test, and not by taste: xUnit runs the
/// tests inside a class one after another and the classes side by side, and each
/// test here is a dozen git processes.
/// </remarks>
public abstract class RepoFixture : IDisposable
{
    protected readonly string _temp = Directory.CreateTempSubdirectory("hatch-workspace-").FullName;
    protected readonly string _origin;
    protected readonly string _work;
    protected readonly string _other;
    protected readonly List<string> _said = [];
    protected readonly List<string> _complained = [];
    private bool _otherCloned;

    protected RepoFixture()
    {
        _origin = Path.Combine(_temp, "origin.git");
        _work = Path.Combine(_temp, "work");
        _other = Path.Combine(_temp, "other");

        Directory.CreateDirectory(_origin);
        G(_origin, "init", "--bare", "--quiet", "-b", "main");

        Directory.CreateDirectory(_work);
        G(_work, "init", "--quiet", "-b", "main");
        Configure(_work);
        G(_work, "remote", "add", "origin", _origin);
        File.WriteAllText(Path.Combine(_work, "a.txt"), "one\ntwo\nthree\n");
        Commit(_work, "Add a");
        G(_work, "push", "--quiet", "origin", "main");
    }

    protected Workspace Ws() => new(_work, "main", _said.Add, _complained.Add);

    /// <summary>A branch on origin, made from the trunk by somebody else.</summary>
    protected void Publish(string branch, string file, string content, bool reset = true)
    {
        Other();
        G(_other, "fetch", "--quiet", "origin");
        if (reset) G(_other, "checkout", "--quiet", "-B", branch, "origin/main");
        else G(_other, "checkout", "--quiet", "-B", branch, $"origin/{branch}");

        File.WriteAllText(Path.Combine(_other, file), content);
        Commit(_other, $"Change {file}");
        G(_other, "push", "--quiet", "origin", branch);
    }

    protected void MoveMain(string file, string content)
    {
        Other();
        G(_other, "fetch", "--quiet", "origin");
        G(_other, "checkout", "--quiet", "-B", "main", "origin/main");
        File.WriteAllText(Path.Combine(_other, file), content);
        Commit(_other, $"Trunk changes {file}");
        G(_other, "push", "--quiet", "origin", "main");
    }

    private static void Configure(string path)
    {
        G(path, "config", "user.name", "Test");
        G(path, "config", "user.email", "test@example.invalid");
        G(path, "config", "commit.gpgsign", "false");
    }

    /// <summary>The second clone, made the first time somebody else needs to push.</summary>
    private void Other()
    {
        if (_otherCloned) return;
        _otherCloned = true;

        G(_temp, "clone", "--quiet", "--branch", "main", _origin, _other);
        Configure(_other);
    }

    protected static void Commit(string dir, string message)
    {
        G(dir, "add", "--all");
        G(dir, "commit", "--quiet", "--message", message);
    }

    protected static string Current(string dir) => G(dir, "rev-parse", "--abbrev-ref", "HEAD").Trim();

    protected static string Tip(string dir, string branch) => G(dir, "rev-parse", $"refs/heads/{branch}").Trim();

    protected static string Refs(string dir) => G(dir, "for-each-ref", "--format=%(refname) %(objectname)");

    /// <summary>Git that is allowed to fail - a conflicted merge is the point.</summary>
    protected static void Git(string dir, params string[] args) => Run(dir, args);

    protected static string G(string dir, params string[] args)
    {
        var (code, output, error) = Run(dir, args);
        if (code != 0) throw new InvalidOperationException($"git {string.Join(' ', args)} in {dir}: {error}");
        return output;
    }

    private static (int Code, string Output, string Error) Run(string dir, string[] args)
    {
        var start = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);

        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, error.GetAwaiter().GetResult());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_temp, recursive: true);
        }
        catch (IOException)
        {
            // The operating system's to tidy.
        }
        catch (UnauthorizedAccessException)
        {
            // Git marks its objects read-only on some platforms.
        }
    }
}


/// <summary>Branch entry against real git.</summary>
public sealed class WorkspaceTests : RepoFixture
{
    // ---- No branch, merged, and which branches count ----

    [Fact]
    public void An_issue_with_no_branch_starts_on_the_trunk_with_a_name_to_cut()
    {
        var ws = Ws();
        Assert.Equal(Reset.Ready, ws.Prepare());

        var entry = ws.Enter("HA-31", "Deterministic Lifecycle Elements", null);

        Assert.Equal(BranchKind.None, entry.Kind);
        Assert.Equal("ha-31-deterministic-lifecycle-elements", entry.Cut);
        Assert.Equal("main", Current(_work));
    }

    [Fact]
    public void A_branch_for_another_key_that_starts_the_same_is_not_this_issues()
    {
        Publish("ha-3-other", "x.txt", "x");
        var ws = Ws();
        ws.Prepare();

        Assert.Equal(BranchKind.None, ws.Enter("HA-31", "Thing", null).Kind);
        Assert.Equal(BranchKind.Entered, ws.Enter("HA-3", "Thing", null).Kind);
    }

    [Fact]
    public void A_branch_named_for_the_bare_key_or_in_another_case_is_found()
    {
        Publish("HA-31", "x.txt", "x");
        var ws = Ws();
        ws.Prepare();

        var entry = ws.Enter("HA-31", "Thing", null);

        Assert.Equal(BranchKind.Entered, entry.Kind);
        Assert.Equal("HA-31", entry.Branch);
    }

    [Fact]
    public void A_branch_already_merged_counts_as_absent_and_the_cut_name_is_free()
    {
        Publish("ha-31-first", "x.txt", "x");
        G(_other, "checkout", "--quiet", "main");
        G(_other, "merge", "--quiet", "--ff-only", "origin/ha-31-first");
        G(_other, "push", "--quiet", "origin", "main");

        var ws = Ws();
        ws.Prepare();
        var entry = ws.Enter("HA-31", "First", null);

        Assert.Equal(BranchKind.AlreadyMerged, entry.Kind);
        Assert.Equal(["ha-31-first"], entry.Merged);
        Assert.Equal("ha-31-first-2", entry.Cut);
        Assert.Equal("main", Current(_work));
    }

    [Fact]
    public void A_squash_merged_branch_counts_as_merged_though_its_commits_are_nowhere_in_the_trunk()
    {
        Publish("ha-31-first", "x.txt", "x");

        // The trunk gets the same change as one new commit, which is what a
        // squash merge is: the branch's commit is not an ancestor of anything.
        G(_other, "checkout", "--quiet", "main");
        File.WriteAllText(Path.Combine(_other, "x.txt"), "x");
        Commit(_other, "Squash of ha-31-first");
        G(_other, "push", "--quiet", "origin", "main");

        var ws = Ws();
        ws.Prepare();

        Assert.Equal(BranchKind.AlreadyMerged, ws.Enter("HA-31", "First", null).Kind);
    }

    // ---- One branch, and what the trunk merge comes to ----

    [Fact]
    public void A_branch_that_already_has_the_trunk_is_entered_and_nothing_is_merged()
    {
        Publish("ha-31-thing", "x.txt", "x");
        var ws = Ws();
        ws.Prepare();

        var entry = ws.Enter("HA-31", "Thing", null);

        Assert.Equal(BranchKind.Entered, entry.Kind);
        Assert.Equal("ha-31-thing", entry.Branch);
        Assert.Equal(MergeOutcome.NoOp, entry.Merge);
        Assert.Equal(1, entry.Ahead);
        Assert.Equal(entry.Sha, entry.Head);
        Assert.Equal("ha-31-thing", Current(_work));
        Assert.Equal(Tip(_origin, "ha-31-thing"), G(_work, "rev-parse", "HEAD").Trim());
    }

    [Fact]
    public void A_trunk_that_moved_is_merged_in_cleanly_with_a_real_merge_commit()
    {
        Publish("ha-31-thing", "x.txt", "x");
        MoveMain("b.txt", "b");

        var ws = Ws();
        ws.Prepare();
        var entry = ws.Enter("HA-31", "Thing", null);

        Assert.Equal(MergeOutcome.Clean, entry.Merge);
        Assert.NotEqual(entry.Sha, entry.Head);
        Assert.Equal(2, G(_work, "rev-list", "--parents", "-n1", "HEAD").Trim().Split(' ').Length - 1);
        Assert.True(File.Exists(Path.Combine(_work, "b.txt")));
        Assert.True(File.Exists(Path.Combine(_work, "x.txt")));
        Assert.Contains("Merge branch 'main' into ha-31-thing", G(_work, "log", "-1", "--format=%s"));
        Assert.False(File.Exists(Path.Combine(_work, ".git", "MERGE_HEAD")));

        // Not pushed: that is the session's call.
        Assert.Equal(entry.Sha, Tip(_origin, "ha-31-thing")[..entry.Sha.Length]);
    }

    [Fact]
    public void A_conflicting_trunk_leaves_the_merge_in_progress_and_names_the_files()
    {
        Publish("ha-31-thing", "a.txt", "one\nBRANCH\nthree\n");
        MoveMain("a.txt", "one\nTRUNK\nthree\n");

        var ws = Ws();
        ws.Prepare();
        var entry = ws.Enter("HA-31", "Thing", null);

        Assert.Equal(MergeOutcome.Conflicted, entry.Merge);
        Assert.Equal(["a.txt"], entry.Conflicted);
        Assert.True(File.Exists(Path.Combine(_work, ".git", "MERGE_HEAD")));
        Assert.Equal("ha-31-thing", Current(_work));
    }

    [Fact]
    public void The_next_reset_aborts_a_merge_the_runner_started_and_never_finished()
    {
        Publish("ha-31-thing", "a.txt", "one\nBRANCH\nthree\n");
        MoveMain("a.txt", "one\nTRUNK\nthree\n");
        var ws = Ws();
        ws.Prepare();
        ws.Enter("HA-31", "Thing", null);

        Assert.Equal(Reset.Ready, Ws().Prepare());

        Assert.False(File.Exists(Path.Combine(_work, ".git", "MERGE_HEAD")));
        Assert.Equal("main", Current(_work));
        Assert.Contains(_said, l => l.Contains("aborted a merge the loop started"));
    }

    [Fact]
    public void A_merge_somebody_else_left_still_ends_the_night()
    {
        Publish("ha-31-thing", "a.txt", "one\nBRANCH\nthree\n");
        MoveMain("a.txt", "one\nTRUNK\nthree\n");
        Ws().Prepare();

        G(_work, "checkout", "--quiet", "-B", "ha-31-thing", "origin/ha-31-thing");
        Git(_work, "merge", "--no-edit", "origin/main");
        Assert.True(File.Exists(Path.Combine(_work, ".git", "MERGE_HEAD")));

        Assert.Equal(Reset.Never, Ws().Prepare());
        Assert.True(File.Exists(Path.Combine(_work, ".git", "MERGE_HEAD")));
        Assert.Contains(_complained, l => l.Contains("did not start it"));
    }

    // ---- The copy on this machine ----

    [Fact]
    public void A_local_copy_ahead_of_origin_is_the_one_used()
    {
        Publish("ha-31-thing", "x.txt", "x");
        G(_work, "fetch", "--quiet");
        G(_work, "checkout", "--quiet", "-b", "ha-31-thing", "origin/ha-31-thing");
        File.WriteAllText(Path.Combine(_work, "local.txt"), "mine");
        Commit(_work, "Only here");
        var local = G(_work, "rev-parse", "HEAD").Trim();

        var ws = Ws();
        ws.Prepare();
        var entry = ws.Enter("HA-31", "Thing", null);

        Assert.True(entry.LocalAhead);
        Assert.Equal(local, G(_work, "rev-parse", "HEAD").Trim());
        Assert.Equal(2, entry.Ahead);
    }

    [Fact]
    public void A_local_copy_behind_origin_is_fast_forwarded()
    {
        Publish("ha-31-thing", "x.txt", "x");
        Ws().Prepare();
        G(_work, "branch", "ha-31-thing", "origin/ha-31-thing");
        Publish("ha-31-thing", "y.txt", "y", reset: false);

        var ws = Ws();
        ws.Prepare();
        var entry = ws.Enter("HA-31", "Thing", null);

        Assert.Equal(Tip(_origin, "ha-31-thing"), G(_work, "rev-parse", "HEAD").Trim());
        Assert.Equal(2, entry.Ahead);
    }

    [Fact]
    public void A_diverged_local_copy_is_kept_under_a_name_and_origins_branch_is_used()
    {
        Publish("ha-31-thing", "x.txt", "x");
        Ws().Prepare();
        G(_work, "checkout", "--quiet", "-b", "ha-31-thing", "origin/ha-31-thing");
        File.WriteAllText(Path.Combine(_work, "local.txt"), "mine");
        Commit(_work, "Only here");
        var local = G(_work, "rev-parse", "HEAD").Trim();
        G(_work, "checkout", "--quiet", "main");

        Publish("ha-31-thing", "y.txt", "y", reset: false);

        var ws = Ws();
        ws.Prepare();
        var entry = ws.Enter("HA-31", "Thing", null);

        Assert.StartsWith("ha-31-thing-local-", entry.KeptAs);
        Assert.Equal(local, G(_work, "rev-parse", entry.KeptAs).Trim());
        Assert.Equal(Tip(_origin, "ha-31-thing"), G(_work, "rev-parse", "HEAD").Trim());
        Assert.Contains(_complained, l => l.Contains(entry.KeptAs));
    }

    // ---- Several ----

    [Fact]
    public void Two_unmerged_branches_are_not_chosen_between()
    {
        Publish("ha-31-first", "x.txt", "x");
        Publish("ha-31-second", "y.txt", "y");
        var ws = Ws();
        ws.Prepare();

        var entry = ws.Enter("HA-31", "Thing", null);

        Assert.Equal(BranchKind.Several, entry.Kind);
        Assert.Equal(["ha-31-first", "ha-31-second"], entry.Candidates);
        Assert.False(entry.Decided);
        Assert.Equal("main", Current(_work));
    }

    [Fact]
    public void An_answer_naming_one_of_them_is_the_one_used()
    {
        Publish("ha-31-first", "x.txt", "x");
        Publish("ha-31-second", "y.txt", "y");
        var ws = Ws();
        ws.Prepare();

        var entry = ws.Enter("HA-31", "Thing", "ha-31-second");

        Assert.Equal(BranchKind.Entered, entry.Kind);
        Assert.Equal("ha-31-second", Current(_work));
    }

    [Fact]
    public void An_answer_naming_none_of_them_is_handed_on_and_not_asked_again()
    {
        Publish("ha-31-first", "x.txt", "x");
        Publish("ha-31-second", "y.txt", "y");
        var ws = Ws();
        ws.Prepare();

        var entry = ws.Enter("HA-31", "Thing", "start over on a new branch");

        Assert.Equal(BranchKind.Several, entry.Kind);
        Assert.True(entry.Decided);
    }

    // ---- The plan ----

    [Fact]
    public void A_plan_says_what_would_happen_and_changes_nothing()
    {
        Publish("ha-31-thing", "a.txt", "one\nBRANCH\nthree\n");
        MoveMain("a.txt", "one\nTRUNK\nthree\n");
        var ws = Ws();
        ws.Prepare();
        var before = Refs(_work);

        var plan = ws.Plan("HA-31", "Thing", null);

        Assert.True(plan.Planned);
        Assert.Equal("ha-31-thing", plan.Branch);
        Assert.Equal(MergeOutcome.Conflicted, plan.Merge);
        Assert.Equal(["a.txt"], plan.Conflicted);
        Assert.Equal(before, Refs(_work));
        Assert.Equal("main", Current(_work));
        Assert.False(File.Exists(Path.Combine(_work, ".git", "MERGE_HEAD")));
    }
}

/// <summary>Asking origin what it holds, and whether a branch merges with the trunk - against real git.</summary>
public sealed class WorkspaceCheckTests : RepoFixture
{
    // ---- Heads ----

    [Fact]
    public void Heads_names_the_trunk_and_every_branch_origin_has_and_touches_nothing_here()
    {
        Publish("ha-31-thing", "x.txt", "x");
        var ws = Ws();
        ws.Prepare();
        var before = Refs(_work);

        var heads = ws.Heads();

        Assert.NotNull(heads);
        Assert.Equal("main", heads.Trunk);
        Assert.Equal(Tip(_origin, "main"), heads.Shas["main"]);
        Assert.Equal(Tip(_origin, "ha-31-thing"), heads.Shas["ha-31-thing"]);
        Assert.Equal(40, heads.Shas["ha-31-thing"].Length);
        Assert.Equal(before, Refs(_work));
    }

    [Fact]
    public void Heads_is_null_where_origin_does_not_answer()
    {
        var ws = Ws();
        G(_work, "remote", "set-url", "origin", Path.Combine(_temp, "gone.git"));

        Assert.Null(ws.Heads());
    }

    // ---- Check ----

    [Fact]
    public void A_branch_that_already_has_the_trunk_is_clean_at_the_full_shas()
    {
        Publish("ha-31-thing", "x.txt", "x");
        var ws = Ws();
        Assert.True(ws.Fetch());

        var verdict = ws.Check("HA-31");

        Assert.NotNull(verdict);
        Assert.Equal(MergeVerdicts.Clean, verdict.Kind);
        Assert.Equal("main", verdict.Trunk);
        Assert.Equal(Tip(_origin, "main"), verdict.TrunkSha);
        Assert.Equal("ha-31-thing", verdict.Branch);
        Assert.Equal(Tip(_origin, "ha-31-thing"), verdict.BranchSha);
        Assert.Empty(verdict.Files);
    }

    [Fact]
    public void A_branch_the_trunk_has_moved_past_without_a_conflict_is_clean()
    {
        Publish("ha-31-thing", "x.txt", "x");
        MoveMain("b.txt", "b");
        var ws = Ws();
        ws.Fetch();

        Assert.Equal(MergeVerdicts.Clean, ws.Check("HA-31")!.Kind);
    }

    [Fact]
    public void A_branch_that_conflicts_says_which_files_and_leaves_the_tree_alone()
    {
        Publish("ha-31-thing", "a.txt", "one\nBRANCH\nthree\n");
        MoveMain("a.txt", "one\nTRUNK\nthree\n");
        var ws = Ws();
        ws.Prepare();
        var before = Refs(_work);

        Assert.True(ws.Fetch());
        var verdict = ws.Check("HA-31");

        Assert.NotNull(verdict);
        Assert.Equal(MergeVerdicts.Conflicted, verdict.Kind);
        Assert.Equal(["a.txt"], verdict.Files);
        Assert.Equal(Tip(_origin, "ha-31-thing"), verdict.BranchSha);
        Assert.Equal(Tip(_origin, "main"), verdict.TrunkSha);

        // Objects and remote-tracking refs are all that was written: the tree
        // is on the trunk, clean, with nothing half-merged.
        Assert.Equal("main", Current(_work));
        Assert.Equal("", G(_work, "status", "--porcelain").Trim());
        Assert.False(File.Exists(Path.Combine(_work, ".git", "MERGE_HEAD")));
        Assert.Equal("", G(_work, "stash", "list").Trim());
        Assert.Equal(
            before.Split('\n').Where(l => !l.Contains("refs/remotes/")).Order(),
            Refs(_work).Split('\n').Where(l => !l.Contains("refs/remotes/")).Order());
    }

    [Fact]
    public void A_merged_branch_is_none_with_no_branch_on_the_verdict()
    {
        Publish("ha-31-first", "x.txt", "x");
        G(_other, "checkout", "--quiet", "main");
        G(_other, "merge", "--quiet", "--ff-only", "origin/ha-31-first");
        G(_other, "push", "--quiet", "origin", "main");
        var ws = Ws();
        ws.Fetch();

        var verdict = ws.Check("HA-31");

        Assert.NotNull(verdict);
        Assert.Equal(MergeVerdicts.None, verdict.Kind);
        Assert.Null(verdict.Branch);
        Assert.Null(verdict.BranchSha);
        Assert.Equal(Tip(_origin, "main"), verdict.TrunkSha);
    }

    [Fact]
    public void A_squash_merged_branch_is_none_though_its_commits_are_nowhere_in_the_trunk()
    {
        Publish("ha-31-first", "x.txt", "x");
        G(_other, "checkout", "--quiet", "main");
        File.WriteAllText(Path.Combine(_other, "x.txt"), "x");
        Commit(_other, "Squash of ha-31-first");
        G(_other, "push", "--quiet", "origin", "main");
        var ws = Ws();
        ws.Fetch();

        Assert.Equal(MergeVerdicts.None, ws.Check("HA-31")!.Kind);
    }

    [Fact]
    public void Two_unmerged_branches_are_ambiguous_and_name_neither()
    {
        Publish("ha-31-one", "x.txt", "x");
        Publish("ha-31-two", "y.txt", "y");
        var ws = Ws();
        ws.Fetch();

        var verdict = ws.Check("HA-31");

        Assert.Equal(MergeVerdicts.Ambiguous, verdict!.Kind);
        Assert.Null(verdict.Branch);
        Assert.Null(verdict.BranchSha);
    }

    [Fact]
    public void An_issue_with_no_branch_is_none_and_a_check_reads_the_refs_as_they_stand()
    {
        var ws = Ws();
        ws.Fetch();
        Assert.Equal(MergeVerdicts.None, ws.Check("HA-31")!.Kind);

        // Pushed after the fetch: a check does no network, so it does not know.
        Publish("ha-31-thing", "x.txt", "x");
        Assert.Equal(MergeVerdicts.None, ws.Check("HA-31")!.Kind);

        ws.Fetch();
        Assert.Equal(MergeVerdicts.Clean, ws.Check("HA-31")!.Kind);
    }

    [Fact]
    public void A_branch_for_another_key_that_starts_the_same_is_not_this_issues_to_check()
    {
        Publish("ha-3-other", "x.txt", "x");
        var ws = Ws();
        ws.Fetch();

        Assert.Equal(MergeVerdicts.None, ws.Check("HA-31")!.Kind);
        Assert.Equal(MergeVerdicts.Clean, ws.Check("HA-3")!.Kind);
    }

    // ---- Leaving reports what origin's branch looks like ----

    [Fact]
    public void After_leaving_with_a_merge_pushed_the_check_reads_the_new_tip_on_origin()
    {
        Publish("ha-31-thing", "x.txt", "x");
        var pushed = Tip(_origin, "ha-31-thing");
        MoveMain("b.txt", "b");
        var ws = Ws();
        ws.Prepare();

        var left = ws.Leave("HA-31", syncPullRequest: true);

        // The push moved the remote-tracking ref, or this reads the old tip -
        // and if it does not on some git, Leave has to fetch once more.
        var tip = Tip(_origin, "ha-31-thing");
        Assert.NotEqual(pushed, tip);
        Assert.NotNull(left.Found);
        Assert.Equal(MergeVerdicts.Clean, left.Found.Kind);
        Assert.Equal(tip, left.Found.BranchSha);
        Assert.Equal(Tip(_origin, "main"), left.Found.TrunkSha);
    }

    [Fact]
    public void After_leaving_with_the_trunk_already_in_the_branch_the_verdict_is_clean_at_its_tip()
    {
        Publish("ha-31-thing", "x.txt", "x");
        var ws = Ws();
        ws.Prepare();

        var left = ws.Leave("HA-31", syncPullRequest: true);

        Assert.Equal(MergeVerdicts.Clean, left.Found!.Kind);
        Assert.Equal(Tip(_origin, "ha-31-thing"), left.Found.BranchSha);
    }

    [Fact]
    public void After_leaving_where_the_merge_conflicts_the_verdict_names_the_files_and_nothing_was_pushed()
    {
        Publish("ha-31-thing", "a.txt", "one\nBRANCH\nthree\n");
        var before = Tip(_origin, "ha-31-thing");
        MoveMain("a.txt", "one\nTRUNK\nthree\n");
        var ws = Ws();
        ws.Prepare();

        var left = ws.Leave("HA-31", syncPullRequest: true);

        Assert.Equal(MergeVerdicts.Conflicted, left.Found!.Kind);
        Assert.Equal(["a.txt"], left.Found.Files);
        Assert.Equal(before, Tip(_origin, "ha-31-thing"));
    }

    [Fact]
    public void After_leaving_with_no_branch_or_a_merged_one_the_verdict_is_none()
    {
        var ws = Ws();
        ws.Prepare();

        Assert.Equal(MergeVerdicts.None, ws.Leave("HA-31", syncPullRequest: true).Found!.Kind);
    }

    [Fact]
    public void Nothing_is_found_where_leaving_did_not_fetch()
    {
        Publish("ha-31-thing", "x.txt", "x");
        var ws = Ws();
        ws.Prepare();

        // No pull request, so no sync and no fetch: nothing is known.
        Assert.Null(ws.Leave("HA-31", syncPullRequest: false).Found);

        // A fetch that did not answer is nothing known either.
        G(_work, "remote", "set-url", "origin", Path.Combine(_temp, "gone.git"));
        Assert.Null(ws.Leave("HA-31", syncPullRequest: true).Found);
    }
}

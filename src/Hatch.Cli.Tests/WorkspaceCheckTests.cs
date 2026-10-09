namespace Hatch.Cli.Tests;

/// <summary>
/// The poll's git - what origin has, whether a branch still merges with the
/// trunk, and what the end of an increment reports - against real git. The
/// fake workspace agrees with whoever wrote it; a bare origin does not.
/// </summary>
public sealed class WorkspaceCheckTests : RepoFixture
{
    private string OriginTip(string branch) => Tip(_origin, branch);

    // ---- Heads ----

    [Fact]
    public void Heads_are_every_branch_on_origin_by_name_with_the_trunks_name_and_nothing_fetched()
    {
        Publish("ha-31-thing", "x.txt", "x");
        Publish("feature/with/slashes", "y.txt", "y");
        var ws = Ws();
        var before = Refs(_work);

        var heads = ws.Heads()!;

        Assert.Equal("main", heads.Trunk);
        Assert.Equal(OriginTip("main"), heads.TrunkSha);
        Assert.Equal(OriginTip("ha-31-thing"), heads.Shas["ha-31-thing"]);
        Assert.Equal(OriginTip("feature/with/slashes"), heads.Shas["feature/with/slashes"]);

        // Not a fetch: this checkout has heard about none of it.
        Assert.Equal(before, Refs(_work));
        Assert.False(G(_work, "for-each-ref", "refs/remotes/origin/ha-31-thing").Trim().Length > 0);
    }

    [Fact]
    public void Heads_are_null_when_origin_does_not_answer()
    {
        G(_work, "remote", "set-url", "origin", Path.Combine(_temp, "nowhere.git"));

        Assert.Null(Ws().Heads());
    }

    [Fact]
    public void A_fingerprint_names_the_trunk_and_only_the_branches_the_key_claims()
    {
        Publish("ha-31-thing", "x.txt", "x");
        Publish("ha-3-other", "y.txt", "y");
        Publish("ha-310-more", "z.txt", "z");

        var heads = Ws().Heads()!;

        Assert.Equal(
            $"{OriginTip("main")}\nha-31-thing:{OriginTip("ha-31-thing")}",
            heads.Fingerprint("HA-31"));
        Assert.Equal(["ha-31-thing"], heads.Candidates("HA-31"));
    }

    [Fact]
    public void A_fingerprint_changes_when_the_trunk_or_the_branch_moves_and_when_another_issues_branch_does_not()
    {
        Publish("ha-31-thing", "x.txt", "x");
        Publish("ha-3-other", "y.txt", "y");
        var ws = Ws();
        var was = ws.Heads()!.Fingerprint("HA-31");

        Publish("ha-3-other", "y2.txt", "y2", reset: false);
        Assert.Equal(was, ws.Heads()!.Fingerprint("HA-31"));

        Publish("ha-31-thing", "x2.txt", "x2", reset: false);
        Assert.NotEqual(was, ws.Heads()!.Fingerprint("HA-31"));

        was = ws.Heads()!.Fingerprint("HA-31");
        MoveMain("m.txt", "m");
        Assert.NotEqual(was, ws.Heads()!.Fingerprint("HA-31"));
    }

    // ---- Fetch ----

    [Fact]
    public void Fetch_makes_the_remote_tracking_refs_current_and_touches_no_tree()
    {
        var ws = Ws();
        ws.Prepare();
        Publish("ha-31-thing", "x.txt", "x");
        var head = G(_work, "rev-parse", "HEAD");

        Assert.True(ws.Fetch());

        Assert.Equal(OriginTip("ha-31-thing"), G(_work, "rev-parse", "refs/remotes/origin/ha-31-thing").Trim());
        Assert.Equal(head, G(_work, "rev-parse", "HEAD"));
        Assert.Equal("main", Current(_work));
        Assert.Equal("", G(_work, "status", "--porcelain").Trim());
    }

    [Fact]
    public void Fetch_says_so_when_origin_does_not_answer()
    {
        G(_work, "remote", "set-url", "origin", Path.Combine(_temp, "nowhere.git"));

        Assert.False(Ws().Fetch());
    }

    // ---- Check ----

    [Fact]
    public void A_branch_that_merges_cleanly_is_clean_at_two_full_shas()
    {
        Publish("ha-31-thing", "x.txt", "x");
        MoveMain("b.txt", "b");
        var ws = Ws();
        ws.Fetch();

        var verdict = ws.Check("HA-31")!;

        Assert.Equal(MergeVerdicts.Clean, verdict.Kind);
        Assert.Equal("main", verdict.Trunk);
        Assert.Equal(OriginTip("main"), verdict.TrunkSha);
        Assert.Equal("ha-31-thing", verdict.Branch);
        Assert.Equal(OriginTip("ha-31-thing"), verdict.BranchSha);
        Assert.Equal(40, verdict.BranchSha!.Length);
        Assert.Empty(verdict.Files);
        Assert.False(verdict.HoldsTrunk);
    }

    [Fact]
    public void A_branch_that_already_has_the_trunk_is_clean()
    {
        Publish("ha-31-thing", "x.txt", "x");
        var ws = Ws();
        ws.Fetch();

        var verdict = ws.Check("HA-31")!;
        Assert.Equal(MergeVerdicts.Clean, verdict.Kind);
        Assert.True(verdict.HoldsTrunk);
    }

    [Fact]
    public void A_branch_that_falls_behind_and_then_merges_the_trunk_back_in_holds_it_again()
    {
        Publish("ha-31-thing", "x.txt", "x");
        MoveMain("b.txt", "b");
        var ws = Ws();
        ws.Fetch();
        Assert.False(ws.Check("HA-31")!.HoldsTrunk);

        G(_other, "fetch", "--quiet", "origin");
        G(_other, "checkout", "--quiet", "-B", "ha-31-thing", "origin/ha-31-thing");
        G(_other, "merge", "--quiet", "--no-edit", "origin/main");
        G(_other, "push", "--quiet", "origin", "ha-31-thing");
        ws.Fetch();

        var verdict = ws.Check("HA-31")!;
        Assert.Equal(MergeVerdicts.Clean, verdict.Kind);
        Assert.True(verdict.HoldsTrunk);
    }

    [Fact]
    public void A_branch_that_conflicts_names_the_files()
    {
        Publish("ha-31-thing", "a.txt", "one\nBRANCH\nthree\n");
        MoveMain("a.txt", "one\nTRUNK\nthree\n");
        var ws = Ws();
        ws.Fetch();

        var verdict = ws.Check("HA-31")!;

        Assert.Equal(MergeVerdicts.Conflicted, verdict.Kind);
        Assert.Equal(["a.txt"], verdict.Files);
        Assert.Equal(OriginTip("ha-31-thing"), verdict.BranchSha);
    }

    [Fact]
    public void A_merged_branch_is_none()
    {
        Publish("ha-31-thing", "x.txt", "x");
        G(_other, "checkout", "--quiet", "main");
        G(_other, "merge", "--quiet", "--no-edit", "ha-31-thing");
        G(_other, "push", "--quiet", "origin", "main");
        var ws = Ws();
        ws.Fetch();

        var verdict = ws.Check("HA-31")!;

        Assert.Equal(MergeVerdicts.None, verdict.Kind);
        Assert.Null(verdict.Branch);
        Assert.Null(verdict.BranchSha);
        Assert.Equal(OriginTip("main"), verdict.TrunkSha);
    }

    [Fact]
    public void A_squash_merged_branch_is_none_though_its_commits_are_nowhere_in_the_trunk()
    {
        Publish("ha-31-thing", "x.txt", "x");
        G(_other, "checkout", "--quiet", "main");
        File.WriteAllText(Path.Combine(_other, "x.txt"), "x");
        Commit(_other, "Squash of ha-31-thing");
        G(_other, "push", "--quiet", "origin", "main");
        var ws = Ws();
        ws.Fetch();

        Assert.Equal(MergeVerdicts.None, ws.Check("HA-31")!.Kind);
    }

    [Fact]
    public void No_branch_at_all_is_none()
    {
        var ws = Ws();
        ws.Fetch();

        Assert.Equal(MergeVerdicts.None, ws.Check("HA-31")!.Kind);
    }

    [Fact]
    public void Two_unmerged_branches_are_ambiguous_and_neither_is_named()
    {
        Publish("ha-31-first", "x.txt", "x");
        Publish("ha-31-second", "y.txt", "y");
        var ws = Ws();
        ws.Fetch();

        var verdict = ws.Check("HA-31")!;

        Assert.Equal(MergeVerdicts.Ambiguous, verdict.Kind);
        Assert.Null(verdict.Branch);
        Assert.Null(verdict.BranchSha);
    }

    [Fact]
    public void A_merged_branch_beside_an_unmerged_one_is_not_ambiguous()
    {
        Publish("ha-31-old", "x.txt", "x");
        G(_other, "checkout", "--quiet", "main");
        G(_other, "merge", "--quiet", "--no-edit", "ha-31-old");
        G(_other, "push", "--quiet", "origin", "main");
        Publish("ha-31-new", "y.txt", "y");
        var ws = Ws();
        ws.Fetch();

        var verdict = ws.Check("HA-31")!;

        Assert.Equal(MergeVerdicts.Clean, verdict.Kind);
        Assert.Equal("ha-31-new", verdict.Branch);
    }

    /// <summary>
    /// A verdict is about origin's branch: a local copy that is ahead, or that
    /// conflicts where origin's does not, is not what two runners would agree on.
    /// </summary>
    [Fact]
    public void It_reads_origins_branch_and_not_a_local_copy_that_has_drifted()
    {
        Publish("ha-31-thing", "x.txt", "x");
        var ws = Ws();
        ws.Prepare();
        ws.Enter("HA-31", "Thing", null);
        File.WriteAllText(Path.Combine(_work, "a.txt"), "local\n");
        Commit(_work, "Local only");
        ws.Leave("HA-31", false);
        MoveMain("a.txt", "trunk\n");
        ws.Fetch();

        // Origin's branch touches x.txt and the trunk touched a.txt: clean, and
        // the local commit that would have conflicted is nobody's business.
        var verdict = ws.Check("HA-31")!;

        Assert.Equal(MergeVerdicts.Clean, verdict.Kind);
        Assert.Equal(OriginTip("ha-31-thing"), verdict.BranchSha);
    }

    [Fact]
    public void Check_does_not_fetch_and_does_not_touch_the_tree()
    {
        Publish("ha-31-thing", "a.txt", "one\nBRANCH\nthree\n");
        var ws = Ws();
        ws.Prepare();
        MoveMain("a.txt", "one\nTRUNK\nthree\n");

        // Nothing fetched since: the answer is about the refs as they stand.
        Assert.Equal(MergeVerdicts.Clean, ws.Check("HA-31")!.Kind);

        ws.Fetch();
        var index = G(_work, "status", "--porcelain");
        Assert.Equal(MergeVerdicts.Conflicted, ws.Check("HA-31")!.Kind);
        Assert.Equal(index, G(_work, "status", "--porcelain"));
        Assert.Equal("main", Current(_work));
        Assert.False(File.Exists(Path.Combine(_work, ".git", "MERGE_HEAD")));
    }

    // ---- What leaving reports ----

    [Fact]
    public void After_a_pushed_merge_the_verdict_reads_clean_at_the_new_sha_which_proves_the_tracking_ref_moved()
    {
        Publish("ha-31-thing", "x.txt", "x");
        MoveMain("b.txt", "b");
        var ws = Ws();
        ws.Prepare();

        var left = ws.Leave("HA-31", syncPullRequest: true);

        var tip = OriginTip("ha-31-thing");
        Assert.NotNull(left.Found);
        Assert.Equal(MergeVerdicts.Clean, left.Found!.Kind);
        Assert.Equal(tip, left.Found.BranchSha);
        Assert.Equal(tip, G(_work, "rev-parse", "refs/remotes/origin/ha-31-thing").Trim());
    }

    [Fact]
    public void Leaving_reports_clean_where_the_trunk_was_already_in_the_branch()
    {
        Publish("ha-31-thing", "x.txt", "x");
        var ws = Ws();
        ws.Prepare();

        var found = ws.Leave("HA-31", syncPullRequest: true).Found!;

        Assert.Equal(MergeVerdicts.Clean, found.Kind);
        Assert.Equal(OriginTip("ha-31-thing"), found.BranchSha);
    }

    [Fact]
    public void Leaving_reports_conflicted_with_the_files_where_the_sync_refused()
    {
        Publish("ha-31-thing", "a.txt", "one\nBRANCH\nthree\n");
        MoveMain("a.txt", "one\nTRUNK\nthree\n");
        var ws = Ws();
        ws.Prepare();

        var found = ws.Leave("HA-31", syncPullRequest: true).Found!;

        Assert.Equal(MergeVerdicts.Conflicted, found.Kind);
        Assert.Equal(["a.txt"], found.Files);
    }

    [Fact]
    public void Leaving_reports_none_and_ambiguous_too()
    {
        var ws = Ws();
        ws.Prepare();
        Assert.Equal(MergeVerdicts.None, ws.Leave("HA-31", syncPullRequest: true).Found!.Kind);

        Publish("ha-31-first", "x.txt", "x");
        Publish("ha-31-second", "y.txt", "y");
        Assert.Equal(MergeVerdicts.Ambiguous, ws.Leave("HA-31", syncPullRequest: true).Found!.Kind);
    }

    [Fact]
    public void Leaving_reports_whatever_the_local_remote_tracking_refs_say_fetched_or_not()
    {
        Publish("ha-31-thing", "x.txt", "x");
        var ws = Ws();
        ws.Prepare();

        // No pull request, so no sync - but Check reads this tree's own
        // remote-tracking refs, which Prepare's fetch already left current.
        Assert.Equal(MergeVerdicts.Clean, ws.Leave("HA-31", syncPullRequest: false).Found!.Kind);

        // A fetch that cannot reach origin leaves those refs exactly as they
        // were - stale, perhaps, but still what this tree knows, not nothing.
        G(_work, "remote", "set-url", "origin", Path.Combine(_temp, "nowhere.git"));
        Assert.Equal(MergeVerdicts.Clean, ws.Leave("HA-31", syncPullRequest: true).Found!.Kind);
    }
}

namespace Hatch.Cli.Tests;

/// <summary>Leaving the tree against real git.</summary>
public sealed class WorkspaceLeaveTests : RepoFixture
{
    /// <summary>
    /// Readme tests below rely on the once-per-process line being fresh -
    /// cleared here rather than in <see cref="RepoFixture"/> itself, since
    /// xUnit runs every other class that derives from it alongside this one,
    /// and a shared reset in the base constructor would race with them.
    /// </summary>
    public WorkspaceLeaveTests() => Workspace.ForgetReadmeSaid();

    // ---- Leaving ----

    [Fact]
    public void Leaving_stashes_what_was_left_uncommitted_under_a_message_naming_the_ticket()
    {
        var ws = Ws();
        ws.Prepare();
        File.WriteAllText(Path.Combine(_work, "a.txt"), "changed\n");
        File.WriteAllText(Path.Combine(_work, "new.txt"), "new\n");

        var left = ws.Leave("HA-31", false);

        Assert.Contains(left.Notes, n => n.StartsWith("2 uncommitted file(s)"));
        Assert.Contains("HA-31", G(_work, "stash", "list"));
        Assert.Equal("", G(_work, "status", "--porcelain").Trim());
        Assert.Equal("main", Current(_work));
    }

    [Fact]
    public void Leaving_aborts_a_merge_the_session_left_and_says_so()
    {
        Publish("ha-31-thing", "a.txt", "one\nBRANCH\nthree\n");
        MoveMain("a.txt", "one\nTRUNK\nthree\n");
        var ws = Ws();
        ws.Prepare();
        G(_work, "checkout", "--quiet", "-B", "ha-31-thing", "origin/ha-31-thing");
        Git(_work, "merge", "--no-edit", "origin/main");

        var left = ws.Leave("HA-31", false);

        Assert.Contains(left.Notes, n => n.Contains("merge was left in progress and has been aborted"));
        Assert.False(File.Exists(Path.Combine(_work, ".git", "MERGE_HEAD")));
        Assert.Equal("main", Current(_work));
    }

    [Fact]
    public void Leaving_removes_a_stale_lock_past_a_refused_abort_and_says_so()
    {
        Publish("ha-31-thing", "a.txt", "one\nBRANCH\nthree\n");
        MoveMain("a.txt", "one\nTRUNK\nthree\n");
        var ws = Ws();
        ws.Prepare();
        G(_work, "checkout", "--quiet", "-B", "ha-31-thing", "origin/ha-31-thing");
        Git(_work, "merge", "--no-edit", "origin/main");

        var lockPath = Path.Combine(_work, ".git", "index.lock");
        File.WriteAllText(lockPath, "");
        File.SetLastWriteTimeUtc(lockPath, DateTime.UtcNow.AddMinutes(-10));

        var left = ws.Leave("HA-31", false);

        Assert.Contains(left.Notes, n => n.Contains("merge was left in progress and has been aborted"));
        Assert.Contains(_said, l => l.Contains("removed a lock the loop's own interrupted git left behind"));
        Assert.False(File.Exists(lockPath));
        Assert.False(File.Exists(Path.Combine(_work, ".git", "MERGE_HEAD")));
        Assert.Equal("main", Current(_work));
    }

    [Fact]
    public void Leaving_pushes_commits_on_the_issue_branch_that_origin_lacks_and_says_so()
    {
        Publish("ha-31-thing", "x.txt", "x");
        var ws = Ws();
        ws.Prepare();
        ws.Enter("HA-31", "Thing", null);
        File.WriteAllText(Path.Combine(_work, "more.txt"), "more");
        Commit(_work, "Session work");
        File.WriteAllText(Path.Combine(_work, "more2.txt"), "more");
        Commit(_work, "Session work again");
        var tip = G(_work, "rev-parse", "--short=9", "HEAD").Trim();
        var local = Tip(_work, "ha-31-thing");

        var left = ws.Leave("HA-31", false);

        Assert.Contains(left.Notes, n => n.Contains("ha-31-thing has 2 commit(s) origin does not, tip " + tip) && n.Contains("the runner pushed them"));
        Assert.Equal(local, Tip(_origin, "ha-31-thing"));
        Assert.Equal(local, G(_work, "rev-parse", "origin/ha-31-thing").Trim());
        Assert.DoesNotContain(_complained, l => l.Contains("pushed them"));
        Assert.Equal("main", Current(_work));
    }

    [Fact]
    public void Leaving_creates_the_issue_branch_on_origin_when_it_is_the_only_one_with_work_and_origin_has_none()
    {
        var ws = Ws();
        ws.Prepare();
        G(_work, "checkout", "--quiet", "-b", "ha-31-thing");
        File.WriteAllText(Path.Combine(_work, "more.txt"), "more");
        Commit(_work, "Session work");
        var local = Tip(_work, "ha-31-thing");

        var left = ws.Leave("HA-31", false);

        Assert.Contains(left.Notes, n => n.Contains("ha-31-thing has 1 commit(s)") && n.Contains("the runner pushed them"));
        Assert.Equal(local, Tip(_origin, "ha-31-thing"));
        Assert.Equal("ha-31-thing", left.Found!.Branch);
        Assert.Equal("main", Current(_work));
    }

    [Fact]
    public void Leaving_guesses_nothing_when_several_local_branches_name_the_issue_and_origin_has_none()
    {
        var ws = Ws();
        ws.Prepare();
        G(_work, "checkout", "--quiet", "-b", "ha-31-one");
        File.WriteAllText(Path.Combine(_work, "one.txt"), "one");
        Commit(_work, "One");
        G(_work, "checkout", "--quiet", "main");
        G(_work, "checkout", "--quiet", "-b", "ha-31-two");
        File.WriteAllText(Path.Combine(_work, "two.txt"), "two");
        Commit(_work, "Two");

        var left = ws.Leave("HA-31", false);

        Assert.Contains(left.Notes, n => n.Contains("2 local branches name this issue") && n.Contains("ha-31-one, ha-31-two") && n.Contains("was not guessed"));
        Assert.DoesNotContain("ha-31-", Refs(_origin));
        Assert.Equal("main", Current(_work));
    }

    [Fact]
    public void Leaving_reports_a_refused_push_when_origin_moved_on_and_forces_nothing()
    {
        Publish("ha-31-thing", "x.txt", "x");
        var ws = Ws();
        ws.Prepare();
        ws.Enter("HA-31", "Thing", null);
        File.WriteAllText(Path.Combine(_work, "more.txt"), "more");
        Commit(_work, "Session work");
        Publish("ha-31-thing", "y.txt", "y", reset: false);
        var origin = Tip(_origin, "ha-31-thing");

        var left = ws.Leave("HA-31", false);

        Assert.Contains(left.Notes, n => n.Contains("ha-31-thing has 1 commit(s) origin does not") && n.Contains("the push was refused") && n.Contains("only on this machine"));
        Assert.Contains(_complained, l => l.Contains("the push was refused"));
        Assert.Equal(origin, Tip(_origin, "ha-31-thing"));
        Assert.Equal("main", Current(_work));
    }

    [Fact]
    public void Leaving_never_pushes_the_trunk_or_the_rescue_branch_made_from_it()
    {
        var ws = Ws();
        ws.Prepare();
        File.WriteAllText(Path.Combine(_work, "oops.txt"), "oops");
        Commit(_work, "Committed to main by mistake");
        var short9 = G(_work, "rev-parse", "--short=9", "HEAD").Trim();
        var trunk = Tip(_origin, "main");

        var left = ws.Leave("HA-31", false);

        Assert.Contains(left.Notes, n => n.Contains($"ha-31-rescued-{short9}"));
        Assert.Equal(trunk, Tip(_origin, "main"));
        Assert.DoesNotContain("rescued", Refs(_origin));
        Assert.Equal("main", Current(_work));
    }

    [Fact]
    public void Leaving_does_not_push_a_rescue_branch_an_earlier_increment_cut()
    {
        Publish("ha-31-thing", "x.txt", "x");
        var ws = Ws();
        ws.Prepare();
        G(_work, "checkout", "--quiet", "-b", "ha-31-rescued-abcdef123", "origin/main");
        File.WriteAllText(Path.Combine(_work, "oops.txt"), "oops");
        Commit(_work, "Rescued");
        G(_work, "checkout", "--quiet", "main");

        var left = ws.Leave("HA-31", false);

        Assert.DoesNotContain("rescued", Refs(_origin));
        Assert.DoesNotContain(left.Notes, n => n.Contains("the runner pushed them"));
    }

    [Fact]
    public void Leaving_moves_commits_left_on_the_local_trunk_onto_a_rescue_branch()
    {
        var ws = Ws();
        ws.Prepare();
        File.WriteAllText(Path.Combine(_work, "oops.txt"), "oops");
        Commit(_work, "Committed to main by mistake");
        var tip = G(_work, "rev-parse", "HEAD").Trim();
        var short9 = G(_work, "rev-parse", "--short=9", "HEAD").Trim();

        var left = ws.Leave("HA-31", false);

        Assert.Contains(left.Notes, n => n.Contains($"ha-31-rescued-{short9}"));
        Assert.Equal(tip, G(_work, "rev-parse", $"ha-31-rescued-{short9}").Trim());
        Assert.Equal(G(_work, "rev-parse", "origin/main").Trim(), G(_work, "rev-parse", "main").Trim());
        Assert.Equal("main", Current(_work));
    }

    [Fact]
    public void Leaving_reports_a_pushed_branchs_verdict_even_without_a_pull_request()
    {
        var ws = Ws();
        ws.Prepare();
        G(_work, "checkout", "--quiet", "-b", "ha-31-thing");
        File.WriteAllText(Path.Combine(_work, "more.txt"), "more");
        Commit(_work, "Session work");
        G(_work, "push", "--quiet", "-u", "origin", "ha-31-thing");

        var left = ws.Leave("HA-31", false);

        Assert.NotNull(left.Found);
        Assert.Equal("ha-31-thing", left.Found!.Branch);
        Assert.Equal(MergeVerdicts.Clean, left.Found.Kind);
        Assert.Equal("main", Current(_work));
    }

    [Fact]
    public void Leaving_brings_a_pull_requests_branch_up_to_the_trunk_when_the_merge_is_clean()
    {
        Publish("ha-31-thing", "x.txt", "x");
        MoveMain("b.txt", "b");
        var ws = Ws();
        ws.Prepare();

        var left = ws.Leave("HA-31", syncPullRequest: true);

        var tip = Tip(_origin, "ha-31-thing");
        Assert.Contains(left.Notes, n => n.Contains("was merged into ha-31-thing on origin") && n.Contains(tip[..9]));
        Assert.Equal("", G(_origin, "rev-list", "main", "^ha-31-thing").Trim());
        Assert.Equal(2, G(_origin, "rev-list", "--parents", "-n1", "ha-31-thing").Trim().Split(' ').Length - 1);
        Assert.Equal("main", Current(_work));
    }

    [Fact]
    public void Leaving_does_nothing_to_a_branch_that_already_has_the_trunk()
    {
        Publish("ha-31-thing", "x.txt", "x");
        var ws = Ws();
        ws.Prepare();
        var before = Tip(_origin, "ha-31-thing");

        var left = ws.Leave("HA-31", syncPullRequest: true);

        Assert.Empty(left.Notes);
        Assert.Equal(before, Tip(_origin, "ha-31-thing"));
    }

    [Fact]
    public void Leaving_lists_the_conflicted_files_and_pushes_nothing_when_the_trunk_does_not_merge()
    {
        Publish("ha-31-thing", "a.txt", "one\nBRANCH\nthree\n");
        MoveMain("a.txt", "one\nTRUNK\nthree\n");
        var ws = Ws();
        ws.Prepare();
        var before = Tip(_origin, "ha-31-thing");

        var left = ws.Leave("HA-31", syncPullRequest: true);

        Assert.Contains(left.Notes, n => n.Contains("does not merge") && n.Contains("a.txt") && n.Contains("nothing was pushed"));
        Assert.Equal(before, Tip(_origin, "ha-31-thing"));
    }

    [Fact]
    public void A_push_the_forge_refuses_is_a_line_and_not_a_failure()
    {
        if (OperatingSystem.IsWindows()) return;

        Publish("ha-31-thing", "x.txt", "x");
        MoveMain("b.txt", "b");

        var hook = Path.Combine(_origin, "hooks", "pre-receive");
        File.WriteAllText(hook, "#!/bin/sh\necho 'protected' >&2\nexit 1\n");
        File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var ws = Ws();
        ws.Prepare();
        var before = Tip(_origin, "ha-31-thing");

        var left = ws.Leave("HA-31", syncPullRequest: true);

        Assert.Contains(left.Notes, n => n.Contains("was refused"));
        Assert.Equal(before, Tip(_origin, "ha-31-thing"));
        Assert.Equal("main", Current(_work));
    }

    // ---- Readme ----

    private static CommitSigning Unsigned => new(Signing.Off, true);

    private static string ReadmeAt(int version) =>
        $"<!-- Written by Hatch. Version {version}. Change this text in Hatch, not here: a newer version replaces the file. -->\nolder text\n";

    private const string ForeignReadme = "# Our own readme\n";

    private string ReadmeBlob(string branch) => G(_origin, "cat-file", "blob", $"{branch}:{ReadmeFile.Path}");

    [Fact]
    public void Readme_is_written_on_the_branch_when_neither_side_has_a_copy()
    {
        Publish("ha-31-thing", "x.txt", "x");
        var ws = Ws();
        ws.Prepare();

        var left = ws.Leave("HA-31", false, carry: new Carrying(null, Unsigned));

        Assert.Contains(left.Notes, n => n.Contains($".hatch/README.md was written on ha-31-thing (version {ReadmeFile.Version})"));
        Assert.Equal(ReadmeFile.Text, ReadmeBlob("ha-31-thing"));
    }

    [Fact]
    public void Readme_is_replaced_on_the_branch_when_both_sides_lack_the_current_version()
    {
        Publish("ha-31-thing", ReadmeFile.Path, ReadmeAt(0));
        var ws = Ws();
        ws.Prepare();

        var left = ws.Leave("HA-31", false, carry: new Carrying(null, Unsigned));

        Assert.Contains(left.Notes, n => n.Contains($".hatch/README.md was replaced on ha-31-thing (version {ReadmeFile.Version})"));
        Assert.Equal(ReadmeFile.Text, ReadmeBlob("ha-31-thing"));
    }

    [Fact]
    public void Readme_is_untouched_when_the_branch_already_holds_the_current_version()
    {
        Publish("ha-31-thing", ReadmeFile.Path, ReadmeAt(ReadmeFile.Version));
        var ws = Ws();
        ws.Prepare();
        var before = Tip(_origin, "ha-31-thing");

        var left = ws.Leave("HA-31", false, carry: new Carrying(null, Unsigned));

        Assert.DoesNotContain(left.Notes, n => n.Contains(".hatch/README.md"));
        Assert.Equal(before, Tip(_origin, "ha-31-thing"));
    }

    [Fact]
    public void Readme_is_untouched_when_the_branch_already_holds_a_newer_version()
    {
        Publish("ha-31-thing", ReadmeFile.Path, ReadmeAt(ReadmeFile.Version + 1));
        var ws = Ws();
        ws.Prepare();
        var before = Tip(_origin, "ha-31-thing");

        var left = ws.Leave("HA-31", false, carry: new Carrying(null, Unsigned));

        Assert.DoesNotContain(left.Notes, n => n.Contains(".hatch/README.md"));
        Assert.Equal(before, Tip(_origin, "ha-31-thing"));
    }

    [Fact]
    public void Readme_is_untouched_when_the_trunk_already_holds_it_and_the_branch_lacks_it()
    {
        Publish("ha-31-thing", "x.txt", "x");
        MoveMain(ReadmeFile.Path, ReadmeFile.Text);
        var ws = Ws();
        ws.Prepare();
        var before = Tip(_origin, "ha-31-thing");

        var left = ws.Leave("HA-31", false, carry: new Carrying(null, Unsigned));

        Assert.DoesNotContain(left.Notes, n => n.Contains(".hatch/README.md"));
        Assert.Equal(before, Tip(_origin, "ha-31-thing"));
    }

    [Fact]
    public void Readme_is_written_when_the_trunk_holds_an_older_version_than_this_build_and_the_branch_has_none()
    {
        Publish("ha-31-thing", "x.txt", "x");
        MoveMain(ReadmeFile.Path, ReadmeAt(0));
        var ws = Ws();
        ws.Prepare();

        var left = ws.Leave("HA-31", false, carry: new Carrying(null, Unsigned));

        Assert.Contains(left.Notes, n => n.Contains($".hatch/README.md was written on ha-31-thing (version {ReadmeFile.Version})"));
        Assert.Equal(ReadmeFile.Text, ReadmeBlob("ha-31-thing"));
    }

    [Fact]
    public void A_foreign_readme_on_either_side_writes_nothing_and_says_so_once_across_two_leaves()
    {
        Publish("ha-31-thing", ReadmeFile.Path, ForeignReadme);
        Publish("ha-32-other", "x.txt", "x");
        var ws = Ws();
        ws.Prepare();
        var beforeThing = Tip(_origin, "ha-31-thing");

        var left1 = ws.Leave("HA-31", false, carry: new Carrying(null, Unsigned));

        Assert.Equal(beforeThing, Tip(_origin, "ha-31-thing"));
        Assert.Contains(_complained, l => l.Contains(ReadmeFile.ForeignSentence));
        var saidOnce = _complained.Count(l => l.Contains(ReadmeFile.ForeignSentence));

        MoveMain(ReadmeFile.Path, ForeignReadme);
        ws.Prepare();
        var beforeOther = Tip(_origin, "ha-32-other");

        var left2 = ws.Leave("HA-32", false, carry: new Carrying(null, Unsigned));

        Assert.Equal(beforeOther, Tip(_origin, "ha-32-other"));
        Assert.Equal(saidOnce, _complained.Count(l => l.Contains(ReadmeFile.ForeignSentence)));
        Assert.DoesNotContain(left1.Notes, n => n.Contains(".hatch/README.md"));
        Assert.DoesNotContain(left2.Notes, n => n.Contains(".hatch/README.md"));
    }

    [Fact]
    public void Readme_is_untouched_when_the_increment_did_not_move_the_branch()
    {
        Publish("ha-31-thing", "x.txt", "x");
        var ws = Ws();
        ws.Prepare();
        var tip = Tip(_origin, "ha-31-thing");
        var before = tip;

        var left = ws.Leave("HA-31", false, carry: new Carrying(tip, Unsigned));

        Assert.DoesNotContain(left.Notes, n => n.Contains(".hatch/README.md"));
        Assert.Equal(before, Tip(_origin, "ha-31-thing"));
    }

    [Fact]
    public void Readme_is_untouched_when_only_the_runners_own_merge_forward_moved_the_branch()
    {
        Publish("ha-31-thing", "x.txt", "x");
        MoveMain("b.txt", "b");
        var ws = Ws();
        ws.Prepare();
        var entryTip = Tip(_origin, "ha-31-thing");
        var entry = ws.Enter("HA-31", "Thing", null);
        Assert.Equal(MergeOutcome.Clean, entry.Merge);

        var left = ws.Leave("HA-31", false, carry: new Carrying(entryTip, Unsigned));

        Assert.DoesNotContain(left.Notes, n => n.Contains(".hatch/README.md"));
    }

    [Fact]
    public void Readme_is_written_on_a_branch_new_on_origin_with_commits()
    {
        var ws = Ws();
        ws.Prepare();
        G(_work, "checkout", "--quiet", "-b", "ha-31-thing");
        File.WriteAllText(Path.Combine(_work, "more.txt"), "more");
        Commit(_work, "Session work");

        var left = ws.Leave("HA-31", false, carry: new Carrying(null, Unsigned));

        Assert.Contains(left.Notes, n => n.Contains("the runner pushed them"));
        Assert.Contains(left.Notes, n => n.Contains($".hatch/README.md was written on ha-31-thing (version {ReadmeFile.Version})"));
        Assert.Equal(ReadmeFile.Text, ReadmeBlob("ha-31-thing"));
    }

    [Fact]
    public void Readme_is_untouched_when_origin_has_no_branch_for_the_issue()
    {
        var ws = Ws();
        ws.Prepare();

        var left = ws.Leave("HA-31", false, carry: new Carrying(null, Unsigned));

        Assert.DoesNotContain(left.Notes, n => n.Contains(".hatch/README.md"));
    }

    [Fact]
    public void Readme_is_untouched_when_origin_has_two_open_branches()
    {
        Publish("ha-31-one", "one.txt", "one");
        Publish("ha-31-two", "two.txt", "two");
        var ws = Ws();
        ws.Prepare();

        var left = ws.Leave("HA-31", false, carry: new Carrying(null, Unsigned));

        Assert.DoesNotContain(left.Notes, n => n.Contains(".hatch/README.md"));
    }

    [Fact]
    public void A_refused_readme_push_is_a_line_and_the_branch_is_as_it_was()
    {
        if (OperatingSystem.IsWindows()) return;

        Publish("ha-31-thing", "x.txt", "x");

        var hook = Path.Combine(_origin, "hooks", "pre-receive");
        File.WriteAllText(hook, "#!/bin/sh\necho 'protected' >&2\nexit 1\n");
        File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var ws = Ws();
        ws.Prepare();
        var before = Tip(_origin, "ha-31-thing");

        var left = ws.Leave("HA-31", false, carry: new Carrying(null, Unsigned));

        Assert.Contains(left.Notes, n => n.Contains("the push of .hatch/README.md to ha-31-thing was refused") && n.Contains("the branch is as it was"));
        Assert.Contains(_complained, l => l.Contains("the push of .hatch/README.md"));
        Assert.Equal(before, Tip(_origin, "ha-31-thing"));
    }

    [Fact]
    public void Readme_commit_is_signed_when_this_git_can_sign()
    {
        Publish("ha-31-thing", "x.txt", "x");
        var ws = Ws();
        ws.Prepare();
        G(_work, "config", "--local", "commit.gpgsign", "true");
        G(_work, "config", "--local", "gpg.program", FakeSigner());

        var left = ws.Leave("HA-31", false, carry: new Carrying(null, new CommitSigning(Signing.Works, true)));

        Assert.Contains(left.Notes, n => n.Contains(".hatch/README.md was written"));
        Assert.Contains("gpgsig", G(_origin, "cat-file", "commit", "ha-31-thing"));
    }

    [Fact]
    public void Readme_commit_is_unsigned_when_this_git_cannot_sign_and_the_project_allows_it()
    {
        Publish("ha-31-thing", "x.txt", "x");
        var ws = Ws();
        ws.Prepare();

        var left = ws.Leave("HA-31", false, carry: new Carrying(null, new CommitSigning(Signing.Fails, true)));

        Assert.Contains(left.Notes, n => n.Contains(".hatch/README.md was written"));
        Assert.DoesNotContain("gpgsig", G(_origin, "cat-file", "commit", "ha-31-thing"));
    }

    [Fact]
    public void Nothing_is_pushed_when_this_git_cannot_sign_and_the_project_requires_it()
    {
        Publish("ha-31-thing", "x.txt", "x");
        var ws = Ws();
        ws.Prepare();
        var before = Tip(_origin, "ha-31-thing");

        var left = ws.Leave("HA-31", false, carry: new Carrying(null, new CommitSigning(Signing.Fails, false)));

        Assert.DoesNotContain(left.Notes, n => n.Contains(".hatch/README.md"));
        Assert.Equal(before, Tip(_origin, "ha-31-thing"));
        Assert.Contains(_complained, l => l.Contains("cannot sign .hatch/README.md"));
    }

    [Fact]
    public void A_failed_signed_commit_is_a_bad_note_and_never_falls_back_to_unsigned()
    {
        Publish("ha-31-thing", "x.txt", "x");
        var ws = Ws();
        ws.Prepare();
        G(_work, "config", "--local", "commit.gpgsign", "true");
        G(_work, "config", "--local", "gpg.program", FailingSigner());
        var before = Tip(_origin, "ha-31-thing");

        var left = ws.Leave("HA-31", false, carry: new Carrying(null, new CommitSigning(Signing.Works, true)));

        Assert.Contains(left.Notes, n => n.Contains(".hatch/README.md could not be committed"));
        Assert.Equal(before, Tip(_origin, "ha-31-thing"));
    }

    [Fact]
    public void Carry_null_leaves_the_readme_untouched()
    {
        Publish("ha-31-thing", "x.txt", "x");
        var ws = Ws();
        ws.Prepare();
        var before = Tip(_origin, "ha-31-thing");

        var left = ws.Leave("HA-31", false);

        Assert.DoesNotContain(left.Notes, n => n.Contains(".hatch/README.md"));
        Assert.Equal(before, Tip(_origin, "ha-31-thing"));
    }

    /// <summary>A gpg program that signs anything it is handed - for a test where signing has to succeed.</summary>
    private string FakeSigner()
    {
        var script = Path.Combine(_temp, "fake-signer.sh");
        File.WriteAllText(
            script,
            "#!/bin/sh\ncat >/dev/null\necho \"[GNUPG:] SIG_CREATED \" >&2\necho \"-----BEGIN PGP SIGNATURE-----\"\necho \"ZmFrZQ==\"\necho \"-----END PGP SIGNATURE-----\"\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return script.Replace('\\', '/');
    }

    /// <summary>A gpg program that always fails - for a test where signing has to fail.</summary>
    private string FailingSigner()
    {
        var script = Path.Combine(_temp, "failing-signer.sh");
        File.WriteAllText(script, "#!/bin/sh\ncat >/dev/null\necho 'no secret key' >&2\nexit 2\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return script.Replace('\\', '/');
    }

    // ---- Sync ----

    [Fact]
    public void Syncing_a_branch_behind_the_trunk_pushes_a_merge_commit_with_both_shas_as_parents()
    {
        Publish("ha-31-thing", "x.txt", "x");
        MoveMain("b.txt", "b");
        var ws = Ws();
        ws.Prepare();
        var trunkSha = G(_origin, "rev-parse", "main").Trim();
        var branchSha = G(_origin, "rev-parse", "ha-31-thing").Trim();

        var synced = ws.Sync("HA-31");

        Assert.Equal(SyncOutcome.Pushed, synced.Outcome);
        var tip = Tip(_origin, "ha-31-thing");
        Assert.Equal(synced.Sha, tip);
        var parents = G(_origin, "rev-list", "--parents", "-n1", "ha-31-thing").Trim().Split(' ');
        Assert.Equal([tip, branchSha, trunkSha], parents);
        Assert.Equal($"Merge branch 'main' into ha-31-thing", G(_origin, "log", "-1", "--format=%s", "ha-31-thing").Trim());
    }

    [Fact]
    public void Syncing_a_conflicting_branch_returns_conflicted_and_pushes_nothing()
    {
        Publish("ha-31-thing", "a.txt", "one\nBRANCH\nthree\n");
        MoveMain("a.txt", "one\nTRUNK\nthree\n");
        var ws = Ws();
        ws.Prepare();
        var before = Tip(_origin, "ha-31-thing");

        var synced = ws.Sync("HA-31");

        Assert.Equal(SyncOutcome.Conflicted, synced.Outcome);
        Assert.Contains("a.txt", synced.Files!);
        Assert.Equal(before, Tip(_origin, "ha-31-thing"));
    }

    [Fact]
    public void Syncing_a_branch_that_already_holds_the_trunk_touches_nothing()
    {
        Publish("ha-31-thing", "x.txt", "x");
        var ws = Ws();
        ws.Prepare();
        var before = Tip(_origin, "ha-31-thing");

        var synced = ws.Sync("HA-31");

        Assert.Equal(SyncOutcome.Current, synced.Outcome);
        Assert.Equal(before, Tip(_origin, "ha-31-thing"));
    }

    [Fact]
    public void Returning_puts_the_tree_on_the_trunk_and_writes_nothing()
    {
        Publish("ha-31-thing", "x.txt", "x");
        var ws = Ws();
        ws.Prepare();
        ws.Enter("HA-31", "Thing", null);

        ws.Return();

        Assert.Equal("main", Current(_work));
    }

    [Fact]
    public void A_dirty_tree_is_refused_by_name_when_a_person_is_sitting_there()
    {
        File.WriteAllText(Path.Combine(_work, "a.txt"), "changed\n");

        Assert.Equal(Reset.Never, Ws().Prepare(stash: false));

        Assert.Contains(_complained, l => l.Contains("a.txt"));
        Assert.Equal("", G(_work, "stash", "list").Trim());
        Assert.Equal("changed\n", File.ReadAllText(Path.Combine(_work, "a.txt")));
    }

    // ---- TreeUnchanged ----

    [Fact]
    public void The_tree_is_unchanged_right_after_entry()
    {
        Publish("ha-31-thing", "x.txt", "x");
        var ws = Ws();
        ws.Prepare();
        var entry = ws.Enter("HA-31", "Thing", null);

        Assert.True(ws.TreeUnchanged(entry.Head));
    }

    [Fact]
    public void A_file_changed_since_entry_is_not_unchanged()
    {
        Publish("ha-31-thing", "x.txt", "x");
        var ws = Ws();
        ws.Prepare();
        var entry = ws.Enter("HA-31", "Thing", null);
        File.WriteAllText(Path.Combine(_work, "a.txt"), "changed\n");

        Assert.False(ws.TreeUnchanged(entry.Head));
    }

    [Fact]
    public void A_head_moved_since_entry_is_not_unchanged()
    {
        Publish("ha-31-thing", "x.txt", "x");
        var ws = Ws();
        ws.Prepare();
        var entry = ws.Enter("HA-31", "Thing", null);
        File.WriteAllText(Path.Combine(_work, "more.txt"), "more");
        Commit(_work, "Session work");

        Assert.False(ws.TreeUnchanged(entry.Head));
    }
}

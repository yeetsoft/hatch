namespace Hatch.Cli.Tests;

/// <summary>Leaving the tree against real git.</summary>
public sealed class WorkspaceLeaveTests : RepoFixture
{
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
    public void Leaving_names_commits_on_the_issue_branch_that_origin_lacks_and_does_not_push_them()
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
        var origin = Tip(_origin, "ha-31-thing");

        var left = ws.Leave("HA-31", false);

        Assert.Contains(left.Notes, n => n.Contains("ha-31-thing has 2 commit(s) origin does not, tip " + tip));
        Assert.Equal(origin, Tip(_origin, "ha-31-thing"));
        Assert.Equal("main", Current(_work));
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
}

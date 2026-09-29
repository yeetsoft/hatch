namespace Hatch.Cli.Tests;

/// <summary>
/// Pushing a session's own work after its account ran out of usage - the one
/// place a runner publishes something nobody said was ready, against real git.
/// </summary>
public sealed class WorkspaceLimitTests : RepoFixture
{
    [Fact]
    public void An_uncommitted_tree_is_committed_and_pushed_onto_the_issue_branch()
    {
        Publish("ha-31-thing", "x.txt", "x");
        var ws = Ws();
        ws.Prepare();
        ws.Enter("HA-31", "Thing", null);
        File.WriteAllText(Path.Combine(_work, "wip.txt"), "wip");

        var pushed = ws.PushForLimit("HA-31", "Thing");

        Assert.Equal(LimitPush.Pushed, pushed.Outcome);
        Assert.Equal("ha-31-thing", pushed.Branch);
        Assert.Equal("", G(_work, "status", "--porcelain").Trim());
        Assert.Equal(pushed.Sha, Tip(_origin, "ha-31-thing")[..9]);
    }

    [Fact]
    public void The_branch_is_cut_when_the_tree_is_still_on_the_trunk()
    {
        var ws = Ws();
        ws.Prepare();
        File.WriteAllText(Path.Combine(_work, "wip.txt"), "wip");

        var pushed = ws.PushForLimit("HA-31", "A New Thing");

        Assert.Equal(LimitPush.Pushed, pushed.Outcome);
        Assert.Equal("ha-31-a-new-thing", pushed.Branch);
        Assert.Equal("ha-31-a-new-thing", Current(_work));
        Assert.Equal(G(_work, "rev-parse", "HEAD").Trim(), Tip(_origin, "ha-31-a-new-thing"));
    }

    [Fact]
    public void The_trunk_itself_is_never_pushed()
    {
        var before = Tip(_origin, "main");
        var ws = Ws();
        ws.Prepare();
        File.WriteAllText(Path.Combine(_work, "wip.txt"), "wip");

        ws.PushForLimit("HA-31", "Thing");

        Assert.Equal(before, Tip(_origin, "main"));
    }

    [Fact]
    public void Commits_left_on_the_trunk_are_carried_onto_the_cut_branch()
    {
        var ws = Ws();
        ws.Prepare();
        File.WriteAllText(Path.Combine(_work, "oops.txt"), "oops");
        Commit(_work, "Committed to main by mistake");
        var tip = G(_work, "rev-parse", "HEAD").Trim();

        var pushed = ws.PushForLimit("HA-31", "Thing");

        Assert.Equal(LimitPush.Pushed, pushed.Outcome);
        Assert.Equal(tip, Tip(_origin, "ha-31-thing"));
    }

    [Fact]
    public void A_refused_push_leaves_the_work_for_leave_to_name()
    {
        if (OperatingSystem.IsWindows()) return;

        var hook = Path.Combine(_origin, "hooks", "pre-receive");
        File.WriteAllText(hook, "#!/bin/sh\necho 'protected' >&2\nexit 1\n");
        File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var ws = Ws();
        ws.Prepare();
        File.WriteAllText(Path.Combine(_work, "wip.txt"), "wip");

        var pushed = ws.PushForLimit("HA-31", "Thing");

        Assert.Equal(LimitPush.Refused, pushed.Outcome);
        Assert.Contains("protected", pushed.Why);

        var left = ws.Leave("HA-31", false);
        Assert.Contains(left.Notes, n => n.Contains("ha-31-thing has 1 commit(s) origin does not"));
    }

    [Fact]
    public void A_clean_branch_already_on_origin_has_nothing_to_push()
    {
        Publish("ha-31-thing", "x.txt", "x");
        var ws = Ws();
        ws.Prepare();
        ws.Enter("HA-31", "Thing", null);

        var pushed = ws.PushForLimit("HA-31", "Thing");

        Assert.Equal(LimitPush.Nothing, pushed.Outcome);
        Assert.Null(pushed.Sha);
    }

    [Fact]
    public void Still_on_the_trunk_with_nothing_done_has_nothing_to_push_and_no_branch_is_cut()
    {
        var ws = Ws();
        ws.Prepare();

        var pushed = ws.PushForLimit("HA-31", "Thing");

        Assert.Equal(LimitPush.Nothing, pushed.Outcome);
        Assert.Null(pushed.Branch);
        Assert.Equal("main", Current(_work));
        Assert.Throws<InvalidOperationException>(() => Tip(_origin, "ha-31-thing"));
    }
}

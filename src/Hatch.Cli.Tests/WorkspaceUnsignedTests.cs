namespace Hatch.Cli.Tests;

/// <summary>
/// The commits the runner makes itself - the merge of the trunk into an issue's
/// branch, and the work-in-progress commit - on a machine whose signing program
/// fails. Real git and a real failing signer: whether a commit goes through
/// unsigned is git's answer, not a fake's.
/// </summary>
public sealed class WorkspaceUnsignedTests : RepoFixture
{
    /// <summary>A workspace that starts git itself, as the loop does, so the environment it is given is the one git sees.</summary>
    private Workspace Real(UnsignedCommits? unsigned) => new(_work, "main", _said.Add, _complained.Add, unsigned: unsigned);

    private void FailingSigner()
    {
        var script = Path.Combine(_temp, "signer.sh");
        File.WriteAllText(script, "#!/bin/sh\ncat >/dev/null\necho 'no secret key' >&2\nexit 2\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        G(_work, "config", "--local", "commit.gpgsign", "true");
        G(_work, "config", "--local", "tag.gpgsign", "true");
        G(_work, "config", "--local", "gpg.program", script.Replace('\\', '/'));
    }

    private static UnsignedCommits On(bool oldGit = false)
    {
        var flag = new UnsignedCommits();
        flag.Set(oldGit);
        return flag;
    }

    private void BranchWithTrunkMovedOn()
    {
        Publish("ha-31-thing", "x.txt", "x");
        MoveMain("trunk.txt", "trunk");
    }

    [Fact]
    public void The_merge_of_the_trunk_is_left_half_made_under_a_failing_signer_without_the_override()
    {
        BranchWithTrunkMovedOn();
        FailingSigner();
        var ws = Real(null);
        ws.Prepare();

        var entry = ws.Enter("HA-31", "Thing", null);

        // Git leaves the merge half-made when it cannot sign, which reads as a
        // conflict with no files in it - the state a session had to be sent to
        // find out about.
        Assert.Equal(MergeOutcome.Conflicted, entry.Merge);
        Assert.Empty(entry.Conflicted);
        Assert.True(File.Exists(Path.Combine(_work, ".git", "MERGE_HEAD")));
    }

    [Fact]
    public void The_merge_of_the_trunk_succeeds_unsigned_with_the_override()
    {
        BranchWithTrunkMovedOn();
        FailingSigner();
        var ws = Real(On());
        ws.Prepare();

        var entry = ws.Enter("HA-31", "Thing", null);

        Assert.Equal(MergeOutcome.Clean, entry.Merge);
        Assert.DoesNotContain("gpgsig", G(_work, "cat-file", "commit", "HEAD"));
        Assert.Contains("Merge branch 'main' into ha-31-thing", G(_work, "log", "-1", "--format=%s"));
    }

    [Fact]
    public void The_work_in_progress_commit_is_refused_under_a_failing_signer_without_the_override()
    {
        FailingSigner();
        var ws = Real(null);
        ws.Prepare();
        File.WriteAllText(Path.Combine(_work, "wip.txt"), "wip");

        Assert.Equal(LimitPush.Refused, ws.PushForLimit("HA-31", "Thing").Outcome);
    }

    [Fact]
    public void The_work_in_progress_commit_succeeds_unsigned_with_the_override()
    {
        FailingSigner();
        var ws = Real(On());
        ws.Prepare();
        File.WriteAllText(Path.Combine(_work, "wip.txt"), "wip");

        var pushed = ws.PushForLimit("HA-31", "Thing");

        Assert.Equal(LimitPush.Pushed, pushed.Outcome);
        Assert.DoesNotContain("gpgsig", G(_work, "cat-file", "commit", "HEAD"));
        Assert.Equal(G(_work, "rev-parse", "HEAD").Trim(), Tip(_origin, "ha-31-thing"));
    }

    [Fact]
    public void Nothing_is_written_to_a_settings_file()
    {
        BranchWithTrunkMovedOn();
        FailingSigner();
        string Signing() => G(_work, "config", "--local", "--get-regexp", "gpg|sign");
        var before = Signing();
        var ws = Real(On());
        ws.Prepare();

        ws.Enter("HA-31", "Thing", null);

        Assert.Equal(before, Signing());
        Assert.Equal("true", G(_work, "config", "--local", "commit.gpgsign").Trim());
        Assert.Equal("true", G(_work, "config", "--local", "tag.gpgsign").Trim());
    }

    [Fact]
    public void Clearing_the_flag_puts_the_next_increment_back_on_signing()
    {
        BranchWithTrunkMovedOn();
        FailingSigner();
        var flag = On();
        flag.Clear();
        var ws = Real(flag);
        ws.Prepare();

        Assert.NotEqual(MergeOutcome.Clean, ws.Enter("HA-31", "Thing", null).Merge);
    }

    [Fact]
    public void An_old_git_is_given_the_two_settings_as_dash_c_and_the_environment_is_left_alone()
    {
        var seen = new List<string[]>();
        var ws = new Workspace(_work, "main", _said.Add, _complained.Add, (_, args) => { seen.Add(args); return (0, "", ""); },
            unsigned: On(oldGit: true));

        ws.Fetch();

        var args = Assert.Single(seen);
        Assert.Equal(["-c", "commit.gpgsign=false", "-c", "tag.gpgsign=false"], args.Take(4));
        Assert.Contains("fetch", args);
    }

    [Fact]
    public void A_newer_git_is_not_given_dash_c_since_it_reads_the_environment()
    {
        var seen = new List<string[]>();
        var ws = new Workspace(_work, "main", _said.Add, _complained.Add, (_, args) => { seen.Add(args); return (0, "", ""); },
            unsigned: On());

        ws.Fetch();

        Assert.DoesNotContain("-c", Assert.Single(seen));
    }

    [Fact]
    public void Without_the_flag_no_call_is_changed()
    {
        var seen = new List<string[]>();
        var ws = new Workspace(_work, "main", _said.Add, _complained.Add, (_, args) => { seen.Add(args); return (0, "", ""); });

        ws.Fetch();

        Assert.DoesNotContain("-c", Assert.Single(seen));
    }
}

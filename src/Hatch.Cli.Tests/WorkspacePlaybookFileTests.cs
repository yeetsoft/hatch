namespace Hatch.Cli.Tests;

/// <summary>
/// Reading a path's exact bytes from origin's refs - against real git for
/// <c>Found</c> and <c>Absent</c>, the cases a bare origin answers cleanly, and
/// a fake <see cref="Workspace.GitBytesRunner"/> for the two a real repository
/// cannot be made to produce on demand: a git that refuses outright, and a git
/// that is not there at all.
/// </summary>
public sealed class WorkspacePlaybookFileTests : RepoFixture
{
    private Workspace WithGitBytes(Workspace.GitBytesRunner gitBytes) =>
        new(_work, "main", _said.Add, _complained.Add, gitBytes: gitBytes);

    [Fact]
    public void Found_reads_the_blobs_exact_bytes_including_a_non_utf8_byte_and_a_crlf_pair()
    {
        // Left to the default, this machine's global autocrlf would rewrite the
        // \r\n below on the way into the index - exactly the corruption this
        // test exists to catch, so it would be caught by nothing.
        G(_work, "config", "core.autocrlf", "false");

        byte[] bytes = [0x68, 0x65, 0x6C, 0x6C, 0x6F, 0xFF, 0x0D, 0x0A, 0x77, 0x6F, 0x72, 0x6C, 0x64];
        File.WriteAllBytes(Path.Combine(_work, "blob.bin"), bytes);
        Commit(_work, "Add blob.bin");
        G(_work, "push", "--quiet", "origin", "main");

        var read = Ws().ReadBlob("main", "blob.bin");

        Assert.Equal(BlobOutcome.Found, read.Outcome);
        Assert.True(bytes.SequenceEqual(read.Content!));
        Assert.Null(read.Why);
    }

    [Fact]
    public void Absent_is_a_clean_answer_for_a_path_never_committed()
    {
        var read = Ws().ReadBlob("main", "nope.txt");

        Assert.Equal(BlobOutcome.Absent, read.Outcome);
        Assert.Null(read.Content);
        Assert.Null(read.Why);
    }

    [Fact]
    public void Absent_is_a_clean_answer_even_when_the_same_path_is_on_disk_from_a_different_ref()
    {
        // Git phrases this one differently - "exists on disk, but not in" -
        // because the working tree, standing on main, happens to hold this
        // exact path too. Still absence, not a failure: a trunk path on disk
        // says nothing about what some other ref has.
        Publish("ha-31-thing", "x.txt", "x");
        Directory.CreateDirectory(Path.Combine(_work, "sub"));
        File.WriteAllText(Path.Combine(_work, "sub", "only-on-trunk.txt"), "trunk\n");
        Commit(_work, "Add sub/only-on-trunk.txt");
        G(_work, "push", "--quiet", "origin", "main");
        var ws = Ws();
        ws.Fetch();

        var read = ws.ReadBlob("ha-31-thing", "sub/only-on-trunk.txt");

        Assert.Equal(BlobOutcome.Absent, read.Outcome);
        Assert.Null(read.Content);
        Assert.Null(read.Why);
    }

    [Fact]
    public void A_git_answer_that_is_not_about_absence_is_failed_with_its_first_line()
    {
        var ws = WithGitBytes((_, _) =>
            (128, [], "fatal: invalid object name 'origin/nonexistent-trunk'.\n"));

        var read = ws.ReadBlob("nonexistent-trunk", "a.txt");

        Assert.Equal(BlobOutcome.Failed, read.Outcome);
        Assert.Null(read.Content);
        Assert.Equal("fatal: invalid object name 'origin/nonexistent-trunk'.", read.Why);
    }

    [Fact]
    public void A_git_that_never_starts_is_failed_with_the_one_line_fallback()
    {
        var ws = WithGitBytes((_, _) => (-1, [], ""));

        var read = ws.ReadBlob("main", "a.txt");

        Assert.Equal(BlobOutcome.Failed, read.Outcome);
        Assert.Null(read.Content);
        Assert.Equal("git is not installed", read.Why);
    }
}

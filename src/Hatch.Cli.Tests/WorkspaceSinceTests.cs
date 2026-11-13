namespace Hatch.Cli.Tests;

/// <summary>What has landed on the trunk to the files a brief names - read against real git.</summary>
public sealed class WorkspaceSinceTests : RepoFixture
{
    private static readonly DateTimeOffset BeforeEverything = DateTimeOffset.UnixEpoch;

    private Workspace Fetched()
    {
        var ws = Ws();
        ws.Prepare();
        return ws;
    }

    [Fact]
    public void A_commit_to_a_named_file_after_the_brief_is_found_with_its_subject()
    {
        MoveMain(".hatch/dependencies.json", "{}");

        var found = Assert.Single(Fetched().Since(BeforeEverything, [".hatch/dependencies.json"]));

        Assert.Equal(".hatch/dependencies.json", found.Path);
        var commit = Assert.Single(found.Commits);
        Assert.Equal("Trunk changes .hatch/dependencies.json", commit.Subject);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", commit.Date);
        Assert.NotEmpty(commit.Sha);
    }

    [Fact]
    public void A_brief_set_after_the_commit_finds_nothing()
    {
        MoveMain(".hatch/dependencies.json", "{}");

        Assert.Empty(Fetched().Since(DateTimeOffset.UtcNow.AddDays(1), [".hatch/dependencies.json"]));
    }

    [Fact]
    public void Six_commits_to_one_path_show_five()
    {
        for (var i = 0; i < 6; i++) MoveMain("x.txt", $"v{i}");

        var found = Assert.Single(Fetched().Since(BeforeEverything, ["x.txt"]));

        Assert.Equal(Workspace.MaxCommitsPerPath, found.Commits.Count);
    }

    [Fact]
    public void A_path_never_committed_shows_nothing()
    {
        MoveMain("x.txt", "v");

        Assert.Empty(Fetched().Since(BeforeEverything, ["no/such/file.cs", "hatch ask"]));
    }

    [Fact]
    public void Only_ten_paths_are_shown()
    {
        MoveMainAll(Enumerable.Range(0, 12).Select(i => $"f{i}.txt"));

        var found = Fetched().Since(BeforeEverything, Enumerable.Range(0, 12).Select(i => $"f{i}.txt").ToList());

        Assert.Equal(Workspace.MaxPaths, found.Count);
    }
}

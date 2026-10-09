namespace Hatch.Cli.Tests;

/// <summary>What a branch changes relative to the trunk - read against real git.</summary>
public sealed class WorkspaceDiffTests : RepoFixture
{
    [Fact]
    public void A_branch_that_changed_a_file_names_it_in_the_stat()
    {
        Publish("ha-31-thing", "x.txt", "x");
        var ws = Ws();
        ws.Prepare();
        ws.Enter("HA-31", "Thing", null);

        var stat = ws.DiffStat("ha-31-thing");

        Assert.NotNull(stat);
        Assert.Contains("x.txt", stat);
    }

    [Fact]
    public void A_branch_that_does_not_exist_answers_null_rather_than_throwing()
    {
        var ws = Ws();
        ws.Prepare();

        Assert.Null(ws.DiffStat("no-such-branch"));
    }
}

namespace Hatch.Cli.Tests;

/// <summary>The rules about branch names, which need no repository.</summary>
public sealed class BranchesTests
{
    [Theory]
    [InlineData("ha-31", "HA-31", true)]
    [InlineData("ha-31-thing", "HA-31", true)]
    [InlineData("HA-31-Thing", "HA-31", true)]
    [InlineData("ha-3-thing", "HA-31", false)]
    [InlineData("ha-310-thing", "HA-31", false)]
    [InlineData("ha-31thing", "HA-31", false)]
    [InlineData("main", "HA-31", false)]
    public void A_branch_names_a_key_at_a_hyphen(string branch, string key, bool expected) =>
        Assert.Equal(expected, Branches.Names(branch, key));

    [Theory]
    [InlineData("Deterministic Lifecycle Elements", "ha-31-deterministic-lifecycle-elements")]
    [InlineData("Workspace: the loop enters the issue's branch on origin, with the trunk merged in", "ha-31-workspace-the-loop-enters-the-issue-s")]
    [InlineData("  ---  ", "ha-31")]
    public void A_cut_name_is_the_key_and_the_title_as_words(string title, string expected) =>
        Assert.Equal(expected, Branches.Cut("HA-31", title));

    [Fact]
    public void A_taken_name_gets_the_first_free_number()
    {
        var taken = new HashSet<string> { "a", "a-2" };
        Assert.Equal("a-3", Branches.Free("a", taken.Contains));
        Assert.Equal("b", Branches.Free("b", taken.Contains));
    }

    [Theory]
    [InlineData("ha-31-b", "ha-31-b")]
    [InlineData("`ha-31-b`", "ha-31-b")]
    [InlineData("use ha-31-b please", "ha-31-b")]
    [InlineData("HA-31-B", "ha-31-b")]
    [InlineData("ha-31-a or ha-31-b", null)]
    [InlineData("a new one", null)]
    [InlineData("", null)]
    public void An_answer_chooses_a_branch_only_when_it_names_exactly_one(string answer, string? expected) =>
        Assert.Equal(expected, Branches.Chosen(answer, ["ha-31-a", "ha-31-b"]) is { } c ? c.ToLowerInvariant() : null);
}

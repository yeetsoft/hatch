using Hatch.Contracts;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The master dependency list and the one id rule, pinned once here so the
/// detector, the recipe and the routes all inherit them.
/// </summary>
public class DependenciesTests
{
    [Fact]
    public void DefaultHoldsTheSevenIdsInOrder()
    {
        Assert.Equal(
            ["docker", "dotnet", "make", "node", "playwright-browsers", "postgres", "pwsh"],
            Dependencies.Default.Rows.Select(row => row.Id));
    }

    [Fact]
    public void DefaultLabelsAreNotBlank()
    {
        Assert.All(Dependencies.Default.Rows, row => Assert.False(string.IsNullOrWhiteSpace(row.Label)));
    }

    [Fact]
    public void DefaultIdsPassTheIdRule()
    {
        Assert.All(Dependencies.Default.Rows, row => Assert.True(Dependencies.IsValidId(row.Id)));
    }

    [Fact]
    public void DefaultIdsAreStrictlyAscendingOrdinal()
    {
        var ids = Dependencies.Default.Rows.Select(row => row.Id).ToList();

        for (var i = 1; i < ids.Count; i++)
            Assert.True(string.CompareOrdinal(ids[i - 1], ids[i]) < 0, $"\"{ids[i]}\" is out of order after \"{ids[i - 1]}\"");
    }

    [Theory]
    [InlineData("make")]
    [InlineData("a-1")]
    public void ValidIdsPass(string id)
    {
        Assert.True(Dependencies.IsValidId(id));
    }

    [Fact]
    public void AnIdOfExactlyTheMaximumLengthPasses()
    {
        Assert.True(Dependencies.IsValidId(new string('a', Dependencies.MaxIdLength)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Make")]
    [InlineData("a_b")]
    [InlineData("a b")]
    [InlineData("make\n")]
    public void InvalidIdsFail(string? id)
    {
        Assert.False(Dependencies.IsValidId(id));
    }

    [Fact]
    public void AnIdOneOverTheMaximumLengthFails()
    {
        Assert.False(Dependencies.IsValidId(new string('a', Dependencies.MaxIdLength + 1)));
    }

    [Fact]
    public void KnowsTheDefaultIdsOnly()
    {
        Assert.True(Dependencies.Default.Knows("make"));
        Assert.False(Dependencies.Default.Knows("xcode"));
        Assert.False(Dependencies.Default.Knows("Make"));
    }

    [Fact]
    public void IdProblemIsNullForEachDefaultId()
    {
        Assert.All(Dependencies.Default.Rows, row => Assert.Null(Dependencies.Default.IdProblem(row.Id)));
    }

    [Fact]
    public void IdProblemNamesTheIdRuleForABrokenId()
    {
        Assert.Equal(
            "a dependency id is lowercase letters, digits and hyphens, at most 32 characters - not \"Make\"",
            Dependencies.Default.IdProblem("Make"));
    }

    [Fact]
    public void IdProblemNamesTheListForAnUnknownId()
    {
        Assert.Equal(
            "`xcode` is not a dependency Hatch knows - the list is docker, dotnet, make, node, playwright-browsers, postgres, pwsh",
            Dependencies.Default.IdProblem("xcode"));
    }

    [Fact]
    public void IdProblemFollowsTheRowsItWasGiven()
    {
        var custom = new Dependencies([new Dependency("alpha", "Alpha"), new Dependency("beta", "Beta")]);

        Assert.Null(custom.IdProblem("alpha"));
        Assert.Equal("`make` is not a dependency Hatch knows - the list is alpha, beta", custom.IdProblem("make"));
    }
}

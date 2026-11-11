using Hatch.Contracts;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The renderer, against fixture recipe text: what it installs, in what order,
/// at which version, and the sentence it refuses with. The recipes Hatch ships
/// are not read here.
/// </summary>
public class DependencyRecipesTests
{
    private const string Node = "# node\nARG NODE_VERSION=22\nRUN install-node $NODE_VERSION\nARG OTHER_VERSION=1\n";
    private const string Dotnet = "# dotnet\nARG DOTNET_VERSION=10\nRUN install-dotnet $DOTNET_VERSION\n";
    private const string Browsers = "# browsers\nARG BROWSERS_VERSION=1\nRUN install-browsers\n";
    private const string Make = "# make\nRUN install-make\n";
    private const string Docker = "# docker\nRUN install-docker\n";

    private static readonly IReadOnlyDictionary<string, string> Fixture = new Dictionary<string, string>
    {
        ["node"] = Node,
        ["dotnet"] = Dotnet,
        ["playwright-browsers"] = Browsers,
        ["make"] = Make,
        ["docker"] = Docker,
    };

    private static (string? Dockerfile, IReadOnlyList<DeclaredDependency>? Resolved, string? Problem) Render(params DeclaredDependency[] set) =>
        DependencyRecipes.Render(set, Fixture, Dependencies.Default);

    private static DeclaredDependency Dep(string id, string? version = null) => new(id, version);

    [Fact]
    public void InstallsInIdOrderWhateverTheInputOrder()
    {
        var (text, _, problem) = Render(Dep("node"), Dep("make"), Dep("dotnet"), Dep("docker"));

        Assert.Null(problem);
        Assert.Equal(Docker + Dotnet + Make + Node, text);
    }

    [Fact]
    public void SameBytesOnASecondCallAndOnAShuffledInput()
    {
        var set = new[] { Dep("node", "20"), Dep("make"), Dep("dotnet"), Dep("docker") };

        var first = Render(set);
        var second = Render(set);
        var shuffled = Render(set.Reverse().ToArray());

        Assert.Null(first.Problem);
        Assert.Equal(first.Dockerfile, second.Dockerfile);
        Assert.Equal(first.Dockerfile, shuffled.Dockerfile);
        Assert.Equal(first.Resolved, second.Resolved);
        Assert.Equal(first.Resolved, shuffled.Resolved);
    }

    [Fact]
    public void TheSameVersionTwiceIsOneEntry()
    {
        var (twice, _, _) = Render(Dep("dotnet", "8"), Dep("dotnet", "8"));
        var (once, _, _) = Render(Dep("dotnet", "8"));

        Assert.Equal(once, twice);
    }

    [Fact]
    public void ABareEntryAndAVersionedOneKeepTheVersion()
    {
        var (bareFirst, _, _) = Render(Dep("dotnet"), Dep("dotnet", "8"));
        var (bareLast, _, _) = Render(Dep("dotnet", "8"), Dep("dotnet"));

        Assert.Equal(Render(Dep("dotnet", "8")).Dockerfile, bareFirst);
        Assert.Equal(bareFirst, bareLast);
        Assert.Contains("ARG DOTNET_VERSION=8\n", bareFirst);
    }

    [Fact]
    public void TwoDifferentVersionsRefuseWithTheSameSentenceInEitherOrder()
    {
        var (dockerfile, _, problem) = Render(Dep("dotnet", "10"), Dep("dotnet", "8"));
        var (_, _, reversed) = Render(Dep("dotnet", "8"), Dep("dotnet", "10"));

        Assert.Null(dockerfile);
        Assert.Equal("`dotnet` is declared at 10 and at 8 - a recipe installs one version", problem);
        Assert.Equal(problem, reversed);
    }

    [Fact]
    public void ClosureAddsNodeOnceBeneathTheRecipeThatNeedsIt()
    {
        var (text, _, problem) = Render(Dep("playwright-browsers"));

        Assert.Null(problem);
        Assert.Equal(Node + Browsers, text);
    }

    [Fact]
    public void ClosureAddsNothingWhenNodeIsDeclaredAndKeepsItsVersion()
    {
        var (text, _, problem) = Render(Dep("playwright-browsers"), Dep("node", "20"));

        Assert.Null(problem);
        Assert.Equal(1, text!.Split("# node\n").Length - 1);
        Assert.Contains("ARG NODE_VERSION=20\n", text);
    }

    [Fact]
    public void AVersionReplacesOnlyTheVersionLine()
    {
        var (text, _, problem) = Render(Dep("node", "20.11"));

        Assert.Null(problem);
        Assert.Equal(Node.Replace("NODE_VERSION=22", "NODE_VERSION=20.11"), text);
        Assert.Contains("ARG OTHER_VERSION=1\n", text);
    }

    [Fact]
    public void AVersionOnARecipeWithNoVersionArgRefuses()
    {
        var (dockerfile, _, problem) = Render(Dep("make", "4"));

        Assert.Null(dockerfile);
        Assert.Equal("`make` installs whatever its package provides - declare it without a version", problem);
    }

    [Fact]
    public void AnUnknownIdRefusesWithTheIdRulesSentence()
    {
        var (dockerfile, _, problem) = Render(Dep("x"));

        Assert.Null(dockerfile);
        Assert.Equal(Dependencies.Default.IdProblem("x"), problem);
        Assert.Contains("`x`", problem);
    }

    [Theory]
    [InlineData("ten")]
    [InlineData("1.2.3.4")]
    [InlineData("10\nRUN x")]
    public void AnInvalidVersionRefusesAndIsNeverSpliced(string version)
    {
        var (dockerfile, _, problem) = Render(Dep("node", version));

        Assert.Null(dockerfile);
        Assert.Equal(DependencyVersion.Problem(version), problem);
    }

    [Fact]
    public void AKnownIdWithoutARecipeRefuses()
    {
        var (dockerfile, _, problem) = Render(Dep("pwsh"));

        Assert.Null(dockerfile);
        Assert.Equal("`pwsh` is on the master list but ships no install recipe - a repository declaring it stays on host runners", problem);
    }

    [Fact]
    public void AnEmptySetRendersNothing()
    {
        var (dockerfile, resolved, problem) = Render();

        Assert.Equal("", dockerfile);
        Assert.Empty(resolved!);
        Assert.Null(problem);
    }

    [Fact]
    public void TheEmbeddedLookupIsNullForAnIdWithNoRecipeAndAnEmptySetRendersNothing()
    {
        Assert.Null(DependencyRecipes.Recipe("no-such"));
        var (dockerfile, resolved, problem) = DependencyRecipes.Render([]);

        Assert.Equal("", dockerfile);
        Assert.Empty(resolved!);
        Assert.Null(problem);
    }

    [Fact]
    public void TheResolvedSetIsTheDeclaredSetClosedOverRequirementsInIdOrder()
    {
        var (_, resolved, _) = Render(Dep("playwright-browsers"), Dep("make"), Dep("dotnet", "10"));

        Assert.Equal(
            [Dep("dotnet", "10"), Dep("make"), Dep("node"), Dep("playwright-browsers")],
            resolved);
    }

    [Fact]
    public void AProblemHasNoResolvedSet()
    {
        var (_, resolved, _) = Render(Dep("pwsh"));

        Assert.Null(resolved);
    }
}

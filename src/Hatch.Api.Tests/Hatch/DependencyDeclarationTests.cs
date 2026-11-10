using System.Text;
using Hatch.Api.Modules.Hatch;
using Hatch.Contracts;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <c>.hatch/dependencies.json</c> accepted as the epic wrote it, and each way
/// it can be wrong refused with the one sentence that says so.
/// </summary>
public class DependencyDeclarationTests
{
    private const string Example = """
        {
          "dependencies": [
            "make",
            { "id": "dotnet", "version": "10" },
            { "id": "node", "version": "22" },
            { "id": "pwsh", "version": "7" },
            "playwright-browsers",
            "postgres"
          ]
        }
        """;

    private static (IReadOnlyList<DeclaredDependency>? Dependencies, string? Problem) Parse(
        string json, Dependencies? known = null)
        => DependencyDeclaration.Parse(Encoding.UTF8.GetBytes(json), known);

    private static string Problem(string json)
    {
        var (dependencies, problem) = Parse(json);
        Assert.Null(dependencies);
        return Assert.IsType<string>(problem);
    }

    private static string EntryProblem(string entry) => Problem($$"""{"dependencies": [{{entry}}]}""");

    private static void AssertExample(IReadOnlyList<DeclaredDependency>? dependencies, string? problem)
    {
        Assert.Null(problem);
        Assert.Equal(
            [
                new DeclaredDependency("make", null),
                new DeclaredDependency("dotnet", "10"),
                new DeclaredDependency("node", "22"),
                new DeclaredDependency("pwsh", "7"),
                new DeclaredDependency("playwright-browsers", null),
                new DeclaredDependency("postgres", null),
            ],
            dependencies);
    }

    [Fact]
    public void TheEpicsExampleParsesInFileOrder()
    {
        var (dependencies, problem) = Parse(Example);

        AssertExample(dependencies, problem);
    }

    [Fact]
    public void ALeadingByteOrderMarkChangesNothing()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(Example)];

        var (dependencies, problem) = DependencyDeclaration.Parse(bytes);

        AssertExample(dependencies, problem);
    }

    [Fact]
    public void AnEmptyListIsValid()
    {
        var (dependencies, problem) = Parse("""{"dependencies": []}""");

        Assert.Null(problem);
        Assert.NotNull(dependencies);
        Assert.Empty(dependencies);
    }

    [Fact]
    public void TheCapRestatesThePlaybooksFilesCap()
    {
        Assert.Equal(PlaybookFile.MaxFileBytes, DependencyDeclaration.MaxFileBytes);
    }

    [Fact]
    public void AFileOverTheCapIsRefused()
    {
        var (dependencies, problem) = DependencyDeclaration.Parse(new byte[DependencyDeclaration.MaxFileBytes + 1]);

        Assert.Null(dependencies);
        Assert.Equal("is larger than 512 KB", problem);
    }

    [Fact]
    public void BytesThatAreNotUtf8AreRefused()
    {
        var (dependencies, problem) = DependencyDeclaration.Parse([0xFF, 0xFE, 0x41]);

        Assert.Null(dependencies);
        Assert.Equal("is not valid UTF-8", problem);
    }

    [Fact]
    public void ASyntaxErrorNamesItsOneBasedLine()
    {
        const string json = """
            {
            "dependencies": [
            "make",
            ]
            }
            """;

        Assert.Equal("is not valid JSON (line 4)", Problem(json));
    }

    [Theory]
    [InlineData("{ nope")]
    [InlineData("")]
    [InlineData("{} {}")]
    [InlineData("""{"dependencies": [] // none""")]
    [InlineData("""{"dependencies": [""")]
    public void ADocumentThatFailsAtItsStartReadsLineOne(string json)
    {
        Assert.Equal("is not valid JSON (line 1)", Problem(json));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("\"make\"")]
    [InlineData("""{"dependencies": "make"}""")]
    [InlineData("""{"dependencies": {"id": "make"}}""")]
    [InlineData("""{"dependencies": null}""")]
    public void ADocumentWithoutAListIsRefused(string json)
    {
        Assert.Equal("needs a top-level \"dependencies\" key holding a list", Problem(json));
    }

    [Fact]
    public void AnotherKeyAfterTheListIsNamed()
    {
        Assert.Equal(
            "a dependencies file has one key, \"dependencies\" - not \"x\" too",
            Problem("""{"dependencies": [], "x": 1}"""));
    }

    [Fact]
    public void AnotherKeyBeforeTheListIsNamed()
    {
        Assert.Equal(
            "a dependencies file has one key, \"dependencies\" - not \"x\" too",
            Problem("""{"x": 1, "dependencies": []}"""));
    }

    [Fact]
    public void AnotherKeyAloneIsNoListNotAnExtraKey()
    {
        Assert.Equal(
            "needs a top-level \"dependencies\" key holding a list",
            Problem("""{"x": 1}"""));
    }

    [Fact]
    public void ARepeatedListKeyIsNamed()
    {
        Assert.Equal(
            "a dependencies file has one key, \"dependencies\" - not \"dependencies\" too",
            Problem("""{"dependencies": [], "dependencies": []}"""));
    }

    [Fact]
    public void AnExtraKeyIsReportedBeforeAListThatIsNotOne()
    {
        Assert.Equal(
            "a dependencies file has one key, \"dependencies\" - not \"x\" too",
            Problem("""{"dependencies": "make", "x": 1}"""));
    }

    [Theory]
    [InlineData("5")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("""{"id":"make","x":1}""")]
    [InlineData("""{"version":"10"}""")]
    [InlineData("""{"id":5}""")]
    [InlineData("""{"id":null}""")]
    [InlineData("""{"id":"make","id":"make"}""")]
    [InlineData("""{"id":"make","version":"10","version":"10"}""")]
    [InlineData("{}")]
    public void AnEntryOfNeitherShapeIsRefused(string entry)
    {
        Assert.Equal(
            "entry 1: a dependency is an id string or an object with \"id\" and an optional \"version\"",
            EntryProblem(entry));
    }

    [Fact]
    public void AnIdTheListDoesNotKnowNamesTheList()
    {
        var problem = EntryProblem("\"xcode\"");

        Assert.StartsWith("entry 1: ", problem);
        Assert.Contains("`xcode` is not a dependency Hatch knows", problem);
        Assert.Contains("docker, dotnet, make, node, playwright-browsers, postgres, pwsh", problem);
    }

    [Fact]
    public void AnIdInTheObjectFormIsHeldToTheSameList()
    {
        var problem = EntryProblem("""{"id": "xcode", "version": "1"}""");

        Assert.StartsWith("entry 1: ", problem);
        Assert.Contains("`xcode` is not a dependency Hatch knows", problem);
    }

    [Fact]
    public void AnIdIsNotLowercased()
    {
        Assert.Equal(
            "entry 1: " + Dependencies.Default.IdProblem("Make"),
            EntryProblem("\"Make\""));
        Assert.Contains("lowercase letters", EntryProblem("\"Make\""));
    }

    [Theory]
    [InlineData("""["make","make"]""")]
    [InlineData("""["make", {"id":"make","version":"4"}]""")]
    public void ASecondDeclarationNamesTheFirst(string list)
    {
        Assert.Equal(
            "entry 2: `make` is already declared by entry 1",
            Problem($$"""{"dependencies": {{list}}}"""));
    }

    [Fact]
    public void ADuplicateAfterAnObjectNamesTheObjectsPosition()
    {
        Assert.Equal(
            "entry 3: `node` is already declared by entry 2",
            Problem("""{"dependencies": ["make", {"id":"node","version":"22"}, "node"]}"""));
    }

    [Theory]
    [InlineData("\"10.x\"", "a version is one to three dotted whole numbers, like 10 or 10.0.100 - not \"10.x\"")]
    [InlineData("\"\"", "a version is one to three dotted whole numbers, like 10 or 10.0.100 - not \"\"")]
    [InlineData("22", "a version is a string of one to three dotted whole numbers, like \"10\" or \"10.0.100\" - not 22")]
    [InlineData("null", "a version is a string of one to three dotted whole numbers, like \"10\" or \"10.0.100\" - not null")]
    [InlineData("true", "a version is a string of one to three dotted whole numbers, like \"10\" or \"10.0.100\" - not true")]
    [InlineData("[\"10\"]", "a version is a string of one to three dotted whole numbers, like \"10\" or \"10.0.100\" - not [\"10\"]")]
    [InlineData("{}", "a version is a string of one to three dotted whole numbers, like \"10\" or \"10.0.100\" - not {}")]
    public void AVersionThatBreaksTheRuleIsRefused(string version, string sentence)
    {
        Assert.Equal("entry 1: " + sentence, EntryProblem($$"""{"id": "node", "version": {{version}}}"""));
    }

    [Fact]
    public void AVersionIsAcceptedOnAnyId()
    {
        var (dependencies, problem) = Parse("""{"dependencies": [{"id": "make", "version": "4.3"}]}""");

        Assert.Null(problem);
        Assert.Equal([new DeclaredDependency("make", "4.3")], dependencies);
    }

    [Fact]
    public void TheEntryNumberCountsFromOne()
    {
        var problem = Problem("""{"dependencies": ["make", "node", "pwsh", "postgres", "xcode"]}""");

        Assert.StartsWith("entry 5: ", problem);
    }

    [Fact]
    public void AnEntryIsCheckedForShapeThenIdThenVersionThenDuplicate()
    {
        // Entry 2 has a bad id and a bad version: the id is said first.
        var problem = Problem("""{"dependencies": ["make", {"id": "xcode", "version": "x"}]}""");

        Assert.Contains("`xcode` is not a dependency Hatch knows", problem);
    }

    [Fact]
    public void AGivenListReplacesTheDefault()
    {
        var zig = new Dependencies([new Dependency("zig", "Zig")]);

        var (dependencies, problem) = Parse("""{"dependencies": ["zig"]}""", zig);
        Assert.Null(problem);
        Assert.Equal([new DeclaredDependency("zig", null)], dependencies);

        var (refused, refusal) = Parse("""{"dependencies": ["make"]}""", zig);
        Assert.Null(refused);
        Assert.StartsWith("entry 1: `make` is not a dependency Hatch knows", refusal);
    }
}

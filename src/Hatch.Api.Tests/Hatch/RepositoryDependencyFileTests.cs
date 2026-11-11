namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The repository's own <c>.hatch/dependencies.json</c>, held to the same
/// shape <see cref="DependencyDeclaration.Parse"/> demands of anybody else's.
/// </summary>
public class RepositoryDependencyFileTests
{
    [Fact]
    public void TheRepositorysOwnFileParses()
    {
        var (_, problem) = DependencyDeclaration.Parse(File.ReadAllBytes(RepositoryFile()));

        Assert.True(problem is null, $"{DependencyDeclaration.Path} {problem}");
    }

    [Fact]
    public void TheRepositorysOwnFileDeclaresWhatTheMakefileNeeds()
    {
        var (dependencies, problem) = DependencyDeclaration.Parse(File.ReadAllBytes(RepositoryFile()));

        Assert.Null(problem);
        Assert.Equal(
            [
                new DeclaredDependency("make", null),
                new DeclaredDependency("dotnet", "10"),
                new DeclaredDependency("node", "22"),
                new DeclaredDependency("pwsh", "7"),
                new DeclaredDependency("playwright-browsers", null),
                new DeclaredDependency("docker", null),
            ],
            dependencies);
    }

    /// <summary>
    /// Not a change detector: a decision, restored twice after an increment
    /// re-added <c>postgres</c> and turned every runner away from this
    /// repository's development steps (#1625, then #1725). Do not edit this
    /// test to make a change pass - ask the operator first.
    /// </summary>
    [Fact]
    public void TheRepositoryNeverDeclaresAPostgresServer()
    {
        var (dependencies, problem) = DependencyDeclaration.Parse(File.ReadAllBytes(RepositoryFile()));

        Assert.Null(problem);
        Assert.False(
            dependencies.Any(d => d.Id == "postgres"),
            $"{DependencyDeclaration.Path} must not declare `postgres`. Nothing this repository's build runs needs a " +
            "Postgres server binary: `make test-api` needs no database, and `make test-api-db` gets one from " +
            "`docker compose up -d db`, which `docker` already covers. A `postgres` entry no live runner has makes " +
            "the board offer this repository's development steps to nobody, including the ticket that would undo it.");
    }

    /// <summary>
    /// Walks up from the test binary to the repository, rather than assuming
    /// how deep <c>bin/Debug/net10.0</c> happens to be today.
    /// </summary>
    private static string RepositoryFile()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, DependencyDeclaration.Path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException($"Could not find {DependencyDeclaration.Path} above {AppContext.BaseDirectory}.");
    }
}

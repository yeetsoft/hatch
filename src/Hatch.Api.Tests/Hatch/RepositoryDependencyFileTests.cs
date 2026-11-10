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
            ],
            dependencies);
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

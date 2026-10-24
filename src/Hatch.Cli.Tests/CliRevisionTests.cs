namespace Hatch.Cli.Tests;

/// <summary>
/// The stamp read back out of the assembly. Mirrors
/// src/Hatch.Api.Tests/HatchRevision/HatchRevisionTests.cs - see docs/plans/version.md.
/// </summary>
public class CliRevisionTests
{
    private const string Sha = "b465267d5809bc0c672d250a1e7cc81b8714ed92";

    [Fact]
    public void ParseRevision_ReadsTheShaTheSdkAppends()
    {
        Assert.Equal(Sha, CliRevision.ParseRevision($"1.0.0+{Sha}"));
    }

    [Theory]
    // A local `dotnet build`, which passes no SourceRevisionId at all.
    [InlineData("1.0.0")]
    [InlineData(null)]
    [InlineData("")]
    // A '+' with nothing after it.
    [InlineData("1.0.0+")]
    // The 7-character short sha the image tags carried before this existed -
    // rejected rather than accepted, so a half-finished migration to the full
    // sha shows up as "dev" instead of as a value that silently never matches.
    [InlineData("1.0.0+b465267")]
    // Not hex.
    [InlineData("1.0.0+zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void ParseRevision_DegradesToDevelopment(string? informationalVersion)
    {
        Assert.Equal("dev", CliRevision.ParseRevision(informationalVersion));
    }

    [Fact]
    public void ParseSequence_ReadsAPositiveCount()
    {
        Assert.Equal(4127, CliRevision.ParseSequence("4127"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a number")]
    // 0 and negatives are indistinguishable from "unstamped" to every caller,
    // so they collapse to the same answer rather than becoming a second case.
    [InlineData("0")]
    [InlineData("-3")]
    public void ParseSequence_DegradesToZero(string? value)
    {
        Assert.Equal(0, CliRevision.ParseSequence(value));
    }

    [Fact]
    public void ARealAssemblyAlwaysReadsAsEitherAShaOrDev()
    {
        // Not "the test assembly is a dev build" - it usually isn't. The SDK's
        // built-in SourceLink sets SourceRevisionId from git on its own, so a
        // build made inside a checkout is stamped with the full sha whether or
        // not anyone asked.
        var revision = new CliRevision(typeof(CliRevisionTests).Assembly);

        Assert.True(
            revision.Revision == "dev" || System.Text.RegularExpressions.Regex.IsMatch(revision.Revision, "^[0-9a-f]{40}$"),
            $"unexpected revision '{revision.Revision}'");
        Assert.Equal(revision.Revision == "dev", revision.IsDevelopment);
    }
}

using Hatch.Contracts;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The shared rule a runner's advertised dependencies answer to, pinned once
/// here so the controller and the CLI both inherit it rather than re-deriving it.
/// </summary>
public class DependencySetTests
{
    [Fact]
    public void NullForNoDependencies()
    {
        var refusal = DependencySet.Normalise(null, out var normalised);

        Assert.Null(refusal);
        Assert.Empty(normalised);
    }

    [Fact]
    public void NullForAnEmptyList()
    {
        var refusal = DependencySet.Normalise([], out var normalised);

        Assert.Null(refusal);
        Assert.Empty(normalised);
    }

    [Fact]
    public void KeepsAVersionedAndABareIdAsTheyAre()
    {
        var refusal = DependencySet.Normalise(["dotnet@10.0.100", "make"], out var normalised);

        Assert.Null(refusal);
        Assert.Equal(["dotnet@10.0.100", "make"], normalised);
    }

    [Fact]
    public void TrimsAndLowercases()
    {
        var refusal = DependencySet.Normalise(["  DotNet@10 "], out var normalised);

        Assert.Null(refusal);
        Assert.Equal(["dotnet@10"], normalised);
    }

    [Fact]
    public void DeduplicatesCaseInsensitivelyKeepingFirstOccurrence()
    {
        var refusal = DependencySet.Normalise(["Make", "MAKE", "make"], out var normalised);

        Assert.Null(refusal);
        Assert.Equal(["make"], normalised);
    }

    [Fact]
    public void KeepsTwoVersionsOfOneIdInOrder()
    {
        var refusal = DependencySet.Normalise(["dotnet@10", "dotnet@8"], out var normalised);

        Assert.Null(refusal);
        Assert.Equal(["dotnet@10", "dotnet@8"], normalised);
    }

    [Theory]
    [InlineData("xcode")]
    [InlineData("xcode@1")]
    public void RefusesAnUnknownIdWholeEvenAfterAValidEntry(string entry)
    {
        var refusal = DependencySet.Normalise(["make", entry], out var normalised);

        Assert.Equal(Dependencies.Default.IdProblem("xcode"), refusal);
        Assert.Equal(
            "`xcode` is not a dependency Hatch knows - the list is dotnet, make, node, playwright-browsers, postgres, pwsh",
            refusal);
        Assert.Empty(normalised);
    }

    [Fact]
    public void RefusesAMalformedId()
    {
        var refusal = DependencySet.Normalise(["make", "bad_id"], out var normalised);

        Assert.Equal(Dependencies.Default.IdProblem("bad_id"), refusal);
        Assert.Empty(normalised);
    }

    [Theory]
    [InlineData("dotnet@10.", "10.")]
    [InlineData("dotnet@", "")]
    [InlineData("dotnet@v10", "v10")]
    [InlineData("dotnet@1@2", "1@2")]
    public void RefusesABadVersionWholeEvenAfterAValidEntry(string entry, string version)
    {
        var refusal = DependencySet.Normalise(["make", entry], out var normalised);

        Assert.NotNull(refusal);
        Assert.Equal(DependencyVersion.Problem(version), refusal);
        Assert.Empty(normalised);
    }

    [Fact]
    public void RefusesABlankEntry()
    {
        var refusal = DependencySet.Normalise(["make", "  "], out var normalised);

        Assert.Equal("a dependency cannot be blank", refusal);
        Assert.Empty(normalised);
    }

    [Fact]
    public void ReadsACustomListInPlaceOfTheDefault()
    {
        var known = new Dependencies([new Dependency("zig", "Zig")]);

        var accepted = DependencySet.Normalise(["Zig@0.13"], out var normalised, known);
        Assert.Null(accepted);
        Assert.Equal(["zig@0.13"], normalised);

        var refusal = DependencySet.Normalise(["make"], out var refused, known);
        Assert.Equal(known.IdProblem("make"), refusal);
        Assert.Empty(refused);
    }
}

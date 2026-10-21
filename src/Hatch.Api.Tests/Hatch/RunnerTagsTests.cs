using Hatch.Contracts;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The shared rule a tag answers to, pinned once here so the controller and
/// <c>hatch config --tag</c> both inherit it rather than re-deriving it.
/// </summary>
public class RunnerTagsTests
{
    [Fact]
    public void NullForNoTags()
    {
        var refusal = RunnerTags.Normalise(null, out var normalised);

        Assert.Null(refusal);
        Assert.Empty(normalised);
    }

    [Fact]
    public void NullForAnEmptyList()
    {
        var refusal = RunnerTags.Normalise([], out var normalised);

        Assert.Null(refusal);
        Assert.Empty(normalised);
    }

    [Fact]
    public void TrimsAndLowercases()
    {
        var refusal = RunnerTags.Normalise(["  Backend  ", "GPU"], out var normalised);

        Assert.Null(refusal);
        Assert.Equal(["backend", "gpu"], normalised);
    }

    [Fact]
    public void DeduplicatesCaseInsensitivelyKeepingFirstOccurrence()
    {
        var refusal = RunnerTags.Normalise(["gpu", "GPU", "Gpu", "backend"], out var normalised);

        Assert.Null(refusal);
        Assert.Equal(["gpu", "backend"], normalised);
    }

    [Fact]
    public void RefusesABlankTag()
    {
        var refusal = RunnerTags.Normalise(["gpu", "   "], out var normalised);

        Assert.Equal("a tag cannot be blank", refusal);
        Assert.Empty(normalised);
    }

    [Fact]
    public void RefusesATagWithAnInternalSpace()
    {
        var refusal = RunnerTags.Normalise(["gpu box"], out var normalised);

        Assert.Equal("a tag has no space or comma inside - not \"gpu box\"", refusal);
        Assert.Empty(normalised);
    }

    [Fact]
    public void RefusesATagWithAComma()
    {
        var refusal = RunnerTags.Normalise(["gpu,box"], out var normalised);

        Assert.Equal("a tag has no space or comma inside - not \"gpu,box\"", refusal);
        Assert.Empty(normalised);
    }

    [Fact]
    public void RefusesATagOverMaxLengthNamingTheCap()
    {
        var tooLong = new string('a', RunnerTags.MaxLength + 1);

        var refusal = RunnerTags.Normalise([tooLong], out var normalised);

        Assert.Equal($"a tag is at most {RunnerTags.MaxLength} characters - not \"{tooLong}\"", refusal);
        Assert.Empty(normalised);
    }

    [Fact]
    public void RefusesMoreThanMaxCountTagsNamingTheCap()
    {
        var tags = Enumerable.Range(0, RunnerTags.MaxCount + 1).Select(i => $"tag{i}").ToList();

        var refusal = RunnerTags.Normalise(tags, out var normalised);

        Assert.Equal($"a runner carries at most {RunnerTags.MaxCount} tags", refusal);
        Assert.Empty(normalised);
    }
}

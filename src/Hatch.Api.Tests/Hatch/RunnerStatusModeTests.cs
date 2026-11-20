using Hatch.Contracts;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The three words a runner's column rule answers to, pinned once here so the
/// controller inherits them rather than re-deriving them.
/// </summary>
public class RunnerStatusModeTests
{
    [Fact]
    public void NullForNoMode_AnswersAll()
    {
        var refusal = RunnerStatusMode.Normalise(null, out var normalised);

        Assert.Null(refusal);
        Assert.Equal(RunnerStatusMode.All, normalised);
    }

    [Theory]
    [InlineData("all")]
    [InlineData("except")]
    [InlineData("only")]
    public void AcceptsEachOfTheThreeWords(string mode)
    {
        var refusal = RunnerStatusMode.Normalise(mode, out var normalised);

        Assert.Null(refusal);
        Assert.Equal(mode, normalised);
    }

    [Fact]
    public void FoldsCaseAndWhitespace()
    {
        var refusal = RunnerStatusMode.Normalise("  Only  ", out var normalised);

        Assert.Null(refusal);
        Assert.Equal(RunnerStatusMode.Only, normalised);
    }

    [Fact]
    public void RefusesAFourthWordNamingItAndTheThree()
    {
        var refusal = RunnerStatusMode.Normalise("sometimes", out var normalised);

        Assert.Equal("a runner's column rule is all, except, only - not \"sometimes\"", refusal);
        Assert.Equal(RunnerStatusMode.All, normalised);
    }
}

using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="SpentNothing.Judge"/> over hand-built rows: the one rule both the
/// stall mark and a dropped release ask. The query that feeds it is covered
/// beside each route.
/// </summary>
public class SpentNothingTests
{
    [Fact]
    public void NoRowAtAll_SpentNothingThatCanBeShown_IsFalse() =>
        Assert.False(SpentNothing.Judge([]));

    [Fact]
    public void OneRowWithNoTokensThatEndedInError_IsTrue() =>
        Assert.True(SpentNothing.Judge([(0, true)]));

    [Fact]
    public void ARowThatSpentTokens_IsFalse() =>
        Assert.False(SpentNothing.Judge([(1, true)]));

    [Fact]
    public void ARowThatIsNotAnError_IsFalse() =>
        Assert.False(SpentNothing.Judge([(0, false)]));

    [Fact]
    public void TwoRowsWhereOneSpentTokens_IsFalse() =>
        Assert.False(SpentNothing.Judge([(0, true), (250, true)]));

    [Fact]
    public void TwoRowsWhereOneIsNotAnError_IsFalse() =>
        Assert.False(SpentNothing.Judge([(0, true), (0, false)]));

    [Fact]
    public void TwoRowsThatBothSpentNothing_IsTrue() =>
        Assert.True(SpentNothing.Judge([(0, true), (0, true)]));
}

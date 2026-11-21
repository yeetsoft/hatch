namespace Hatch.Api.Tests.Hatch;

public sealed class QuestionLevelsTests
{
    [Fact]
    public void ByStrictness_IsHoldThenBuildThenMerge() =>
        Assert.Equal(["hold", "build", "merge"], QuestionLevels.ByStrictness);

    [Theory]
    [InlineData("hold", 0)]
    [InlineData("build", 1)]
    [InlineData("merge", 2)]
    [InlineData(null, 0)]
    [InlineData("nonsense", 0)]
    public void Rank_IsTheIndex_AndAnythingUnknownRanksAsHold(string? level, int rank) =>
        Assert.Equal(rank, QuestionLevels.Rank(level));

    [Theory]
    [InlineData(null, "hold")]
    [InlineData("", "hold")]
    [InlineData("nonsense", "hold")]
    [InlineData("build", "build")]
    [InlineData("merge", "merge")]
    public void Normalise_ReadsNullAndUnknownAsHold(string? level, string expected) =>
        Assert.Equal(expected, QuestionLevels.Normalise(level));

    [Fact]
    public void Strictest_IsTheLowestRank()
    {
        Assert.Equal("build", QuestionLevels.Strictest(["merge", "build"]));
        Assert.Equal("hold", QuestionLevels.Strictest([null, "merge"]));
    }

    [Fact]
    public void Strictest_OfNothing_IsHold() =>
        Assert.Equal("hold", QuestionLevels.Strictest([]));

    [Fact]
    public void Reaches_TheIssuesOwnQuestion_AlwaysReaches() =>
        Assert.True(QuestionLevels.Reaches("ambition", isOwn: true, targetDangerous: false));

    [Fact]
    public void Reaches_AQuestionOnAnythingOtherThanAnAmbition_Reaches()
    {
        Assert.True(QuestionLevels.Reaches("epic", isOwn: false, targetDangerous: false));
        Assert.True(QuestionLevels.Reaches("story", isOwn: false, targetDangerous: false));
    }

    [Fact]
    public void Reaches_AQuestionOnAnAmbition_DoesNotReachADescendant() =>
        Assert.False(QuestionLevels.Reaches("ambition", isOwn: false, targetDangerous: false));

    [Fact]
    public void Reaches_AQuestionOnAnAmbition_StillReachesADangerousDescendant() =>
        Assert.True(QuestionLevels.Reaches("ambition", isOwn: false, targetDangerous: true));
}

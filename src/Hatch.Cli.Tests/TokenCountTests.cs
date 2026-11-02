namespace Hatch.Cli.Tests;

/// <summary>
/// The one rule the flag and <c>PATCH</c> both read.
/// </summary>
public sealed class TokenCountTests
{
    [Theory]
    [InlineData("500000", 500_000L)]
    [InlineData("007", 7L)]
    [InlineData("500k", 500_000L)]
    [InlineData("500K", 500_000L)]
    [InlineData("2k", 2_000L)]
    [InlineData("2M", 2_000_000L)]
    [InlineData("2m", 2_000_000L)]
    [InlineData("1.5M", 1_500_000L)]
    [InlineData("0.5M", 500_000L)]
    [InlineData("1.0M", 1_000_000L)]
    public void TryParse_AcceptsEveryStatedForm(string text, long expected)
    {
        Assert.True(TokenCount.TryParse(text, out var count));
        Assert.Equal(expected, count);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0k")]
    [InlineData("0.0M")]
    [InlineData("-5")]
    [InlineData("+5")]
    [InlineData("1.25M")]
    [InlineData("1.5k")]
    [InlineData(".5M")]
    [InlineData("1.M")]
    [InlineData("1,000")]
    [InlineData(" 5")]
    [InlineData("5 ")]
    [InlineData("5kk")]
    [InlineData("2MM")]
    [InlineData("lots")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("M")]
    [InlineData("k")]
    public void TryParse_RefusesEveryStatedForm(string? text)
    {
        Assert.False(TokenCount.TryParse(text, out var count));
        Assert.Equal(0, count);
    }

    [Fact]
    public void TryParse_RefusesOverflowWithoutThrowing()
    {
        Assert.False(TokenCount.TryParse("99999999999999999M", out var count1));
        Assert.Equal(0, count1);

        Assert.False(TokenCount.TryParse("99999999999999999999M", out var count2));
        Assert.Equal(0, count2);
    }

    [Fact]
    public void Refusal_IsTheSentenceBothCallersPrepend()
    {
        Assert.Equal("a token count, like 500k or 2M", TokenCount.Refusal);
    }
}

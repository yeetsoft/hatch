using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What a typed query means to the go-to lookup. No database: this is the
/// half the console's <c>goToRows</c> has to agree with.
/// </summary>
public class GoToQueryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-")]
    [InlineData("--")]
    public void BlankWordsAndPunctuation_AreNothing(string? q) =>
        Assert.IsType<GoToQuery.None>(GoToQuery.Parse(q));

    [Theory]
    [InlineData("ha 4")]
    [InlineData("fix the gasket")]
    public void SeveralWords_AreTitleWords(string q) =>
        Assert.Equal(new GoToQuery.Words(q), GoToQuery.Parse(q));

    [Fact]
    public void WhitespaceThatLeavesNoWord_IsNothing() =>
        Assert.IsType<GoToQuery.None>(GoToQuery.Parse("- -"));

    [Theory]
    [InlineData("4", "4")]
    [InlineData("  45 ", "45")]
    [InlineData("045", "045")]
    public void DigitsAlone_AreANumberPrefix(string q, string digits) =>
        Assert.Equal(new GoToQuery.Number(digits), GoToQuery.Parse(q));

    [Theory]
    [InlineData("ha", "ha")]
    [InlineData("HA", "ha")]
    [InlineData("ha-4", "ha4")]
    [InlineData("HA-4", "ha4")]
    [InlineData("ha4", "ha4")]
    [InlineData("-ha-", "ha")]
    public void AnyOtherWord_IsAKeyPrefix_OnLettersAndDigitsAlone(string q, string wanted) =>
        Assert.Equal(new GoToQuery.Key(wanted), GoToQuery.Parse(q));

    [Fact]
    public void Ranges_AreOnePerDigitLength_AscendingFromTheTypedLength()
    {
        var ranges = GoToQuery.Ranges("45");

        Assert.Equal((45, 45), ranges[0]);
        Assert.Equal((450, 459), ranges[1]);
        Assert.Equal((4500, 4599), ranges[2]);

        // Nine digits is the last: 4_500_000_000 is past the top of an int.
        Assert.Equal((450_000_000, 459_999_999), ranges[^1]);
        Assert.Equal(8, ranges.Count);
    }

    [Fact]
    public void ARangeThatPassesTheTopOfAnInt_IsHeldAtIt_AndOneWhollyPastIsDropped()
    {
        var ranges = GoToQuery.Ranges("21");

        // 2_100_000_000 to 2_199_999_999 is inside an int; nothing longer fits.
        Assert.Equal((2_100_000_000, 2_147_483_647), ranges[^1]);
        Assert.Equal(9, ranges.Count);

        // Ten digits, past int.MaxValue: nothing to read.
        Assert.Empty(GoToQuery.Ranges("3000000000"));
        Assert.Equal([(2_147_483_647, 2_147_483_647)], GoToQuery.Ranges("2147483647"));
    }

    [Theory]
    [InlineData("045")]
    [InlineData("0")]
    [InlineData("12345678901")]
    [InlineData("")]
    [InlineData("4a")]
    public void ADigitStringThatNoNumberStartsWith_HasNoRanges(string digits) =>
        Assert.Empty(GoToQuery.Ranges(digits));

    [Fact]
    public void Splits_AreEveryHeadOfTwoOrMoreFollowedByDigitsNotStartingWithZero()
    {
        Assert.Equal([("ha", "34"), ("ha3", "4")], GoToQuery.Splits("ha34"));
        Assert.Equal([("ha", "4")], GoToQuery.Splits("ha4"));
        Assert.Empty(GoToQuery.Splits("ha"));
        Assert.Empty(GoToQuery.Splits("h4"));

        // A tail that starts with 0 is skipped, and the next split is read.
        Assert.Equal([("ha0", "4")], GoToQuery.Splits("ha04"));

        // The head may itself end in digits; the tail must be digits to the end,
        // and a split whose tail starts with 0 is skipped (ha3 and 04).
        Assert.Equal([("ha", "304"), ("ha30", "4")], GoToQuery.Splits("ha304"));
    }
}

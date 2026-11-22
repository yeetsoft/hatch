using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The draft ambition's title and brief - pure and synchronous, so every case
/// here is hand-built values and no database.
/// </summary>
public class AmbitionShapeTests
{
    private static readonly Guid ImageId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid VideoId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // ---- Title ----

    [Fact]
    public void Title_IsTheFirstLineOfTheWords_OnOneLine()
    {
        Assert.Equal("a board for ideas", AmbitionShape.Title("a board for ideas\nand a second line\r\nand a third"));
        Assert.Equal("a board for ideas", AmbitionShape.Title("   a   board\tfor ideas   \nmore"));
    }

    [Fact]
    public void Title_SkipsLeadingBlankLines()
    {
        Assert.Equal("the real first line", AmbitionShape.Title("\n  \r\n\nthe real first line\nnext"));
    }

    [Fact]
    public void Title_IsCutToTheTitleLength()
    {
        var title = AmbitionShape.Title(new string('a', EfHatchIssue.MaxTitleLength + 50) + "\nsecond");

        Assert.Equal(EfHatchIssue.MaxTitleLength, title.Length);
    }

    [Fact]
    public void Title_NeverCutsThroughASurrogatePair()
    {
        var words = new string('a', EfHatchIssue.MaxTitleLength - 1) + "\U0001F600" + "tail";

        var title = AmbitionShape.Title(words);

        Assert.Equal(EfHatchIssue.MaxTitleLength - 1, title.Length);
        Assert.False(char.IsHighSurrogate(title[^1]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n \r\n ")]
    public void Title_WithNoWords_IsDraftAmbition(string? words)
    {
        Assert.Equal("Draft ambition", AmbitionShape.Title(words));
    }

    [Fact]
    public void Title_CarriesNoPrefix()
    {
        Assert.DoesNotContain(":", AmbitionShape.Title("a board for ideas"));
    }

    // ---- Brief ----

    [Fact]
    public void Brief_OpensBySayingItIsADraftNotYetShaped()
    {
        var brief = AmbitionShape.Describe("words", []);

        Assert.StartsWith("This ambition is a draft that has not been shaped yet.\n\n", brief);
    }

    [Fact]
    public void Brief_QuotesEveryLineOfTheWords_AndABlankLineAsAnEmptyQuote()
    {
        var brief = AmbitionShape.Describe("first line\r\n\r\nthird line\nfourth", []);

        Assert.Contains("## In the person's words\n\n> first line\n>\n> third line\n> fourth", brief);
        Assert.DoesNotContain("## Files", brief);
    }

    [Fact]
    public void Brief_WithNoWords_HasNoQuote()
    {
        var brief = AmbitionShape.Describe(null, [new ShapeFile(ImageId, "a.png", "image/png")]);

        Assert.DoesNotContain("In the person's words", brief);
        Assert.DoesNotContain(">", brief.Replace("->", ""));
    }

    [Fact]
    public void Brief_LinksAnImageAsAnImage_AndAVideoAsALink()
    {
        var brief = AmbitionShape.Describe("words",
        [
            new ShapeFile(ImageId, "sketch.png", "image/png"),
            new ShapeFile(VideoId, "walkthrough.mp4", "video/mp4"),
        ]);

        Assert.Contains($"## Files\n\n![sketch.png](/api/images/{ImageId})\n[walkthrough.mp4](/api/attachments/{VideoId})", brief);
    }

    [Fact]
    public void Brief_EscapesBracketsAndBackslashesInAFileName_AndNamesANamelessOne()
    {
        var brief = AmbitionShape.Describe(null,
        [
            new ShapeFile(ImageId, @"a[1]\b.png", "image/png"),
            new ShapeFile(VideoId, "", "video/mp4"),
        ]);

        Assert.Contains($@"![a\[1\]\\b.png](/api/images/{ImageId})", brief);
        Assert.Contains($"[attachment](/api/attachments/{VideoId})", brief);
    }

    [Fact]
    public void Brief_CopiesNoDreamIn()
    {
        // The brief takes no dream at all: they are recorded on the issue instead.
        Assert.DoesNotContain("dream", AmbitionShape.Describe("words", []), StringComparison.OrdinalIgnoreCase);
    }
}

using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The weave title and brief - pure and synchronous, so every case here is
/// hand-built values and no database.
/// </summary>
public class DreamWeaveTests
{
    private static readonly Dictionary<string, string> Asks = new()
    {
        ["seed"] = "the thread running through the dreams that bear on the seed",
        ["pressing"] = "the thread the dreams return to with the most urgency",
        ["common"] = "the thread the most dreams touch",
        ["next"] = "the nearest step toward the vision these dreams describe",
    };

    private static string Brief(string mode, string? seed = null, int projectId = 7, params long[] ids) =>
        DreamWeave.Describe(mode, mode == "seed" ? seed ?? "a seed" : seed, projectId, ids);

    [Theory]
    [InlineData("seed")]
    [InlineData("pressing")]
    [InlineData("common")]
    [InlineData("next")]
    public void Brief_NamesTheModeAndItsOwnSentenceOnly(string mode)
    {
        var brief = Brief(mode);

        Assert.Contains($"Mode: **{mode}**.", brief);
        Assert.Contains(Asks[mode], brief);
        foreach (var other in Asks.Where(a => a.Key != mode))
        {
            Assert.DoesNotContain(other.Value, brief);
            Assert.DoesNotContain($"Mode: **{other.Key}**", brief);
        }
    }

    [Fact]
    public void SeedBrief_QuotesTheSeedAndEscapesItInTheQuery()
    {
        var brief = Brief("seed", "a board for ideas");

        Assert.Contains("> a board for ideas", brief);
        Assert.Contains("q=a%20board%20for%20ideas", brief);
    }

    [Fact]
    public void SeedBrief_EscapesQuotesAndAmpersandsInTheGetLine()
    {
        var brief = Brief("seed", "x&y \"z\"");
        var get = brief.Split('\n').Single(l => l.Contains("/api/dreams?"));

        Assert.Contains("%26", get);
        Assert.Contains("%22", get);
        Assert.DoesNotContain("x&y", get);
        Assert.DoesNotContain("\"z\"", get);
        Assert.Contains("> x&y \"z\"", brief);
    }

    [Fact]
    public void SeedBrief_BlockquotesEveryLineOfAMultiLineSeed()
    {
        var brief = Brief("seed", "one\ntwo\r\nthree");

        Assert.Contains("> one\n", brief.Replace("\r\n", "\n"));
        Assert.Contains("> two\n", brief.Replace("\r\n", "\n"));
        Assert.Contains("> three\n", brief.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Brief_NamesEveryDreamOnScreen_AndOnlyThen()
    {
        var with = Brief("pressing", null, 7, 12, 40);
        var without = Brief("pressing");

        Assert.Contains("12, 40", with);
        Assert.Contains("start from them", with);
        Assert.DoesNotContain("start from them", without);
        Assert.DoesNotContain("on screen", without);
    }

    [Fact]
    public void Brief_CarriesTheProjectAndThePutLine()
    {
        var brief = Brief("common", null, 7);

        Assert.Contains("projectId=7", brief);
        Assert.Contains("PUT /api/issues/<KEY>/dreams", brief);
    }

    [Fact]
    public void Brief_HasTheSixHeadingsInOrder()
    {
        var brief = Brief("seed", "a seed", 7, 12);
        string[] headings =
        [
            "# This epic does not exist yet",
            "## What the mode asks for",
            "## Reading the dreams",
            "## Writing the epic",
            "## Recording what you drew on",
            "## The Dream Board stays as it is",
        ];

        var at = headings.Select(h => brief.IndexOf(h, StringComparison.Ordinal)).ToList();

        Assert.All(at, i => Assert.True(i >= 0));
        Assert.Equal(at.OrderBy(i => i), at);
    }

    [Fact]
    public void Brief_SaysToRetitleAndToOpenWithASummary()
    {
        foreach (var brief in new[] { Brief("seed", "a seed"), Brief("pressing") })
        {
            var start = brief.IndexOf("## Writing the epic", StringComparison.Ordinal);
            var end = brief.IndexOf("## Recording what you drew on", StringComparison.Ordinal);
            var section = brief[start..end];

            Assert.Contains("Replace the `Weave: ` title", section);
            Assert.Contains("a handful of words", section);
            Assert.Contains("summary of at most three short paragraphs", section);
            Assert.Contains("before any heading", section);
            Assert.Contains("quote the dreams after it", section);
            Assert.Contains("How a ticket reads", section);
        }
    }

    [Fact]
    public void NonSeedBrief_HasNoSeedQuoteAndNoQuery()
    {
        foreach (var mode in new[] { "pressing", "common", "next" })
        {
            var brief = Brief(mode);

            Assert.DoesNotContain("Mode: **seed**", brief);
            Assert.DoesNotContain("q=", brief);
            Assert.DoesNotContain("\n> ", brief);
        }
    }

    [Fact]
    public void Title_OfALongSeed_IsCutToTheTitleLimit()
    {
        var title = DreamWeave.Title("seed", new string('a', 400));

        Assert.Equal(EfHatchIssue.MaxTitleLength, title.Length);
        Assert.StartsWith("Weave: ", title);
    }

    [Fact]
    public void Title_OfAMessySeed_IsOneLine()
    {
        var title = DreamWeave.Title("seed", "  a   board\nfor\r\n\tideas  ");

        Assert.Equal("Weave: a board for ideas", title);
    }

    [Fact]
    public void Title_OfTheOtherModes_IsFixed()
    {
        Assert.Equal("Weave: the most pressing thread", DreamWeave.Title("pressing", null));
        Assert.Equal("Weave: the most common thread", DreamWeave.Title("common", null));
        Assert.Equal("Weave: the next thread", DreamWeave.Title("next", null));
    }

    [Fact]
    public void Title_CutThroughASurrogatePair_DropsTheHalf()
    {
        var seed = new string('a', 292) + "😀😀";

        var title = DreamWeave.Title("seed", seed);

        Assert.False(char.IsHighSurrogate(title[^1]));
        Assert.True(title.Length <= EfHatchIssue.MaxTitleLength);
    }

    [Fact]
    public void UnknownMode_Throws()
    {
        Assert.Throws<ArgumentException>(() => DreamWeave.Title("other", null));
        Assert.Throws<ArgumentException>(() => DreamWeave.Describe("other", null, 1, []));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n ")]
    public void SeedMode_WithoutASeed_Throws(string? seed)
    {
        Assert.Throws<ArgumentException>(() => DreamWeave.Title("seed", seed));
        Assert.Throws<ArgumentException>(() => DreamWeave.Describe("seed", seed, 1, []));
    }
}

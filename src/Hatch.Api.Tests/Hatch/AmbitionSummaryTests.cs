using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The one line a row says about what an ambition is for. Each case pins one rule
/// of <see cref="AmbitionSummary.Of"/>, so a failure names the rule that moved.
/// </summary>
public class AmbitionSummaryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\n  ")]
    public void NoDescription_HasNoSummary(string? description) =>
        Assert.Equal("", AmbitionSummary.Of(description));

    [Fact]
    public void AHeadingsOnlyDescription_HasNoSummary() =>
        Assert.Equal("", AmbitionSummary.Of("# Goal\n\n## Principles\n\n### Later"));

    [Theory]
    [InlineData("A person can see what matters. The rest is detail.", "A person can see what matters.")]
    [InlineData("Is it clear? Yes it is.", "Is it clear?")]
    [InlineData("Ship it! Then rest.", "Ship it!")]
    [InlineData("No full stop at all", "No full stop at all")]
    [InlineData("It ends here.", "It ends here.")]
    [InlineData("He said \"stop.\" Then he left.", "He said \"stop.\"")]
    [InlineData("A thing (see below.) Then more.", "A thing (see below.)")]
    [InlineData("Versions like v1.2 stay whole. Next.", "Versions like v1.2 stay whole.")]
    public void TheSummary_IsCutAfterTheFirstSentence(string description, string expected) =>
        Assert.Equal(expected, AmbitionSummary.Of(description));

    [Fact]
    public void AFullStopBeforeALowercaseWord_IsNotTheEndOfTheSentence() =>
        Assert.Equal("Use e.g. a thing and carry on.", AmbitionSummary.Of("Use e.g. a thing and carry on. Then stop."));

    [Fact]
    public void AFullStopBeforeACapital_IsTheEndOfTheSentence() =>
        Assert.Equal("Ask Dr.", AmbitionSummary.Of("Ask Dr. Smith about it."));

    [Fact]
    public void ASentenceMayRunOverSeveralLines_JoinedBySpaces() =>
        Assert.Equal("One long thought that wraps.", AmbitionSummary.Of("One long thought\nthat wraps.\nAnd another."));

    [Fact]
    public void HeadingsAreSkipped_AndTheFirstProseUnderThemIsRead() =>
        Assert.Equal("The real start.", AmbitionSummary.Of("# Title\n\n## Why\n\nThe real start. And more."));

    [Fact]
    public void HtmlComments_AreSkipped_OnOneLineOrMany() =>
        Assert.Equal("Visible.", AmbitionSummary.Of("<!-- note -->\n<!--\nspans\nlines\n-->\nVisible."));

    [Fact]
    public void AnUnclosedHtmlComment_SwallowsTheRest() =>
        Assert.Equal("", AmbitionSummary.Of("<!-- never closed\nVisible."));

    [Theory]
    [InlineData("```\ncode here.\n```\nAfter the fence.")]
    [InlineData("~~~ts\ncode here.\n~~~\nAfter the fence.")]
    [InlineData("````\n```\nstill code\n```\n````\nAfter the fence.")]
    public void FencedCode_IsSkipped(string description) =>
        Assert.Equal("After the fence.", AmbitionSummary.Of(description));

    [Fact]
    public void AnUnclosedFence_SwallowsTheRest() =>
        Assert.Equal("", AmbitionSummary.Of("```\ncode\nmore code."));

    [Theory]
    [InlineData("| a | b |\n|---|---|\n| 1 | 2 |\n\nAfter the table.")]
    [InlineData("---\n***\n___\n- - -\nAfter the table.")]
    [InlineData("[ref]: https://example.com/x\nAfter the table.")]
    public void TablesRulesAndReferences_AreSkipped(string description) =>
        Assert.Equal("After the table.", AmbitionSummary.Of(description));

    [Theory]
    [InlineData("- First point. Second.", "First point.")]
    [InlineData("* Star point.", "Star point.")]
    [InlineData("+ Plus point.", "Plus point.")]
    [InlineData("1. Numbered point.", "Numbered point.")]
    [InlineData("2) Paren point.", "Paren point.")]
    [InlineData("> Quoted point.", "Quoted point.")]
    [InlineData(">> Deep quote.", "Deep quote.")]
    public void AListMarkerOrQuoteMark_IsDropped(string description, string expected) =>
        Assert.Equal(expected, AmbitionSummary.Of(description));

    [Fact]
    public void AListMarkerStartsANewBlock_SoTheProseBeforeItIsNotJoinedToIt() =>
        Assert.Equal("Intro line", AmbitionSummary.Of("Intro line\n- first item\n- second item"));

    [Fact]
    public void AListItemsOwnContinuationLines_JoinIt() =>
        Assert.Equal("First item that wraps.", AmbitionSummary.Of("- First item\n  that wraps.\n- Second."));

    [Fact]
    public void ABlockWithNothingToRead_IsSkippedForTheNextOne() =>
        Assert.Equal("Words at last.", AmbitionSummary.Of("![badge](https://example.com/b.svg)\n\n**\n\nWords at last."));

    [Theory]
    [InlineData("![alt text](https://example.com/a.png) Then words.", "Then words.")]
    [InlineData("See [the page](https://example.com/x) now.", "See the page now.")]
    [InlineData("See [the page][ref] now.", "See the page now.")]
    [InlineData("Go to <https://example.com/x> now.", "Go to https://example.com/x now.")]
    [InlineData("**Bold** and *soft* and ***both*** words.", "Bold and soft and both words.")]
    [InlineData("Some __strong__ and _slanted_ words.", "Some strong and slanted words.")]
    [InlineData("Some ~~struck~~ words.", "Some struck words.")]
    [InlineData("A <b>tagged</b> word<br/> here.", "A tagged word here.")]
    [InlineData("A \\*literal\\* star and a \\_literal\\_ bar.", "A *literal* star and a _literal_ bar.")]
    [InlineData("Fish &amp; chips &lt;3 &#33;", "Fish & chips <3 !")]
    [InlineData("Five * three stays.", "Five * three stays.")]
    public void InlineMarkdown_IsStripped(string description, string expected) =>
        Assert.Equal(expected, AmbitionSummary.Of(description));

    [Fact]
    public void SnakeCase_Survives() =>
        Assert.Equal("Rename my_snake_case_name now.", AmbitionSummary.Of("Rename my_snake_case_name now."));

    [Fact]
    public void ACodeSpansContents_AreKeptUntouched() =>
        Assert.Equal("Run a_b *c* <d> &amp; [e](f) now.", AmbitionSummary.Of("Run `a_b *c* <d> &amp; [e](f)` now."));

    [Fact]
    public void ADoubleBacktickSpan_MayHoldABacktick() =>
        Assert.Equal("Type a`b here.", AmbitionSummary.Of("Type ``a`b`` here."));

    [Fact]
    public void AnOverlongSentence_IsCutToTwoHundredWithAnEllipsis()
    {
        var summary = AmbitionSummary.Of(string.Join(' ', Enumerable.Repeat("word", 80)) + ".");

        Assert.Equal(AmbitionSummary.MaxLength, summary.Length);
        Assert.EndsWith("…", summary);
        Assert.StartsWith("word word word", summary);
    }

    [Fact]
    public void ACutThatLandsOnASpace_IsTrimmedBeforeTheEllipsis()
    {
        // 198 letters, a space, then more: the 199th character is the space.
        var summary = AmbitionSummary.Of(new string('a', 198) + " bbbb");

        Assert.Equal(new string('a', 198) + "…", summary);
    }

    [Fact]
    public void ASentenceOfExactlyTwoHundred_IsKeptWhole()
    {
        var sentence = new string('a', 199) + ".";

        Assert.Equal(sentence, AmbitionSummary.Of(sentence));
    }

    [Fact]
    public void ACutThroughASurrogatePair_DropsTheWholePair()
    {
        // 198 letters then a pair: the pair would start at index 198 and end at 199,
        // so the 199 characters kept would hold only its first half.
        var summary = AmbitionSummary.Of(new string('a', 198) + "\U0001F600" + "tail");

        Assert.Equal(new string('a', 198) + "…", summary);
        Assert.False(summary.Any(char.IsSurrogate));
    }
}

using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Chat;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Tests.Chat;

/// <summary>
/// The shape of a question's own message, asked and answered - no database and
/// no Slack. <see cref="Chat.QuestionChatMessageTests"/> covers where it is
/// queued and edited.
/// </summary>
public class QuestionChatContentTests
{
    [Fact]
    public void Asked_WithOptions_NamesTheRecommendedOneAndTheLevel()
    {
        var content = QuestionChatContent.Build(
            On(), 7, "HA-1", "Rank weighting", "leaf-weighted or child-weighted?",
            [new QuestionOptionDto("leaf-weighted", Recommended: true), new QuestionOptionDto("child-weighted")],
            QuestionLevels.BuildName);

        Assert.Equal("HA-1 Rank weighting", content.Title);
        Assert.Equal("leaf-weighted or child-weighted?", content.Body);
        Assert.Equal(["leaf-weighted (recommended)", "child-weighted", "Level: build"], content.Lines);
    }

    [Fact]
    public void AskedInProse_NoOptions_OmitsTheOptionLines()
    {
        var content = QuestionChatContent.Build(On(), 7, "HA-1", "A question", "how should this go?", null, QuestionLevels.HoldName);

        Assert.Equal(["Level: hold"], content.Lines);
    }

    [Fact]
    public void Answered_ByAPerson_NamesThem()
    {
        var content = QuestionChatContent.Build(
            On(), 7, "HA-1", "A question", "how should this go?", null, QuestionLevels.HoldName,
            answeredBy: "nathan", answerBody: "leaf-weighted", lookBack: false);

        Assert.Equal(["Level: hold", "nathan answered: leaf-weighted"], content.Lines);
    }

    [Fact]
    public void Answered_WhenEveryAnswerIsAKeys_SaysAProgramInstead()
    {
        var content = QuestionChatContent.Build(
            On(), 7, "HA-1", "A question", "how should this go?", null, QuestionLevels.MergeName,
            answeredBy: "some-runner", answerBody: "leaf-weighted", lookBack: true);

        Assert.Equal(["Level: merge", "A program answered: leaf-weighted"], content.Lines);
    }

    [Fact]
    public void Links_AreTheIssueAndTheBallot()
    {
        var content = QuestionChatContent.Build(On("https://hatch.example"), 7, "HA-1", "A question", "how?", null, QuestionLevels.HoldName);

        Assert.Equal(
            [new ChatLink("HA-1", "https://hatch.example/issues/HA-1"), new ChatLink("Answer it", "https://hatch.example/ballot")],
            content.Links);
    }

    [Fact]
    public void LinksOff_WithNoPublicBaseUrl_ShowAsPlainText()
    {
        var content = QuestionChatContent.Build(On(), 7, "HA-1", "A question", "how?", null, QuestionLevels.HoldName);

        Assert.All(content.Links, l => Assert.Null(l.Url));
    }

    private static readonly QuestionOptionDto[] Two =
        [new("Five words", Recommended: true), new("One sentence")];

    [Fact]
    public void Asked_WithOptions_HasAButtonForEach_AcceptRecommendedFirstAndPrimary()
    {
        var content = QuestionChatContent.Build(On(), 7, "HA-1", "Q", "which?", Two, QuestionLevels.HoldName);

        Assert.Equal(
            [
                new ChatAction("hatch.answer:7:r", "Accept recommended", Primary: true),
                new ChatAction("hatch.answer:7:0", "Five words"),
                new ChatAction("hatch.answer:7:1", "One sentence"),
            ],
            content.Actions);
    }

    [Fact]
    public void AskedInProse_HasNoButtons()
    {
        var content = QuestionChatContent.Build(On(), 7, "HA-1", "Q", "which?", null, QuestionLevels.HoldName);

        Assert.Null(content.Actions);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void WithNoOrTwoRecommendations_HasNoAcceptRecommended(int recommended)
    {
        var options = new[] { new QuestionOptionDto("A", Recommended: recommended == 2), new QuestionOptionDto("B", Recommended: recommended == 2) };

        var content = QuestionChatContent.Build(On(), 7, "HA-1", "Q", "which?", options, QuestionLevels.HoldName);

        Assert.Equal(["hatch.answer:7:0", "hatch.answer:7:1"], content.Actions!.Select(a => a.Id));
        Assert.DoesNotContain(content.Actions!, a => a.Primary);
    }

    [Fact]
    public void Answered_HasNoButtons()
    {
        var content = QuestionChatContent.Build(
            On(), 7, "HA-1", "Q", "which?", Two, QuestionLevels.HoldName, answeredBy: "ada", answerBody: "Five words");

        Assert.Null(content.Actions);
    }

    [Fact]
    public void AChangeOfLevel_KeepsTheButtons()
    {
        var before = QuestionChatContent.Build(On(), 7, "HA-1", "Q", "which?", Two, QuestionLevels.HoldName);
        var after = QuestionChatContent.Build(On(), 7, "HA-1", "Q", "which?", Two, QuestionLevels.BuildName);

        Assert.Equal(before.Actions, after.Actions);
        Assert.NotEmpty(after.Actions!);
    }

    [Fact]
    public void AMessageWithNoActions_StoresExactlyTheJsonItAlwaysDid()
    {
        var json = new ChatMessageContent("T", null, ["a"], [new ChatLink("L", null)]).Write();

        Assert.DoesNotContain("actions", json);
        Assert.Equal("""{"title":"T","body":null,"lines":["a"],"links":[{"label":"L","url":null}]}""", json);
    }

    [Fact]
    public void AStoredRowWithNoActions_ReadsAsHavingNone()
    {
        var read = ChatMessageContent.Read("""{"title":"T","body":null,"lines":["a"],"links":[]}""");

        Assert.NotNull(read);
        Assert.Null(read.Actions);
    }

    [Fact]
    public void Actions_RoundTrip()
    {
        var content = QuestionChatContent.Build(On(), 7, "HA-1", "Q", "which?", Two, QuestionLevels.HoldName);

        var read = ChatMessageContent.Read(content.Write());

        Assert.Equal(content.Actions, read!.Actions);
        Assert.Equal(content.Write(), read.Write());
    }

    private static ChatLinks On(string? baseUrl = null) => new(Options.Create(new HatchOptions { PublicBaseUrl = baseUrl! }));
}

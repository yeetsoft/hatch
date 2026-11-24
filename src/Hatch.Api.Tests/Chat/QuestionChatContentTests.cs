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
            On(), "HA-1", "Rank weighting", "leaf-weighted or child-weighted?",
            [new QuestionOptionDto("leaf-weighted", Recommended: true), new QuestionOptionDto("child-weighted")],
            QuestionLevels.BuildName);

        Assert.Equal("HA-1 Rank weighting", content.Title);
        Assert.Equal("leaf-weighted or child-weighted?", content.Body);
        Assert.Equal(["leaf-weighted (recommended)", "child-weighted", "Level: build"], content.Lines);
    }

    [Fact]
    public void AskedInProse_NoOptions_OmitsTheOptionLines()
    {
        var content = QuestionChatContent.Build(On(), "HA-1", "A question", "how should this go?", null, QuestionLevels.HoldName);

        Assert.Equal(["Level: hold"], content.Lines);
    }

    [Fact]
    public void Answered_ByAPerson_NamesThem()
    {
        var content = QuestionChatContent.Build(
            On(), "HA-1", "A question", "how should this go?", null, QuestionLevels.HoldName,
            answeredBy: "nathan", answerBody: "leaf-weighted", lookBack: false);

        Assert.Equal(["Level: hold", "nathan answered: leaf-weighted"], content.Lines);
    }

    [Fact]
    public void Answered_WhenEveryAnswerIsAKeys_SaysAProgramInstead()
    {
        var content = QuestionChatContent.Build(
            On(), "HA-1", "A question", "how should this go?", null, QuestionLevels.MergeName,
            answeredBy: "some-runner", answerBody: "leaf-weighted", lookBack: true);

        Assert.Equal(["Level: merge", "A program answered: leaf-weighted"], content.Lines);
    }

    [Fact]
    public void Links_AreTheIssueAndTheBallot()
    {
        var content = QuestionChatContent.Build(On("https://hatch.example"), "HA-1", "A question", "how?", null, QuestionLevels.HoldName);

        Assert.Equal(
            [new ChatLink("HA-1", "https://hatch.example/issues/HA-1"), new ChatLink("Answer it", "https://hatch.example/ballot")],
            content.Links);
    }

    [Fact]
    public void LinksOff_WithNoPublicBaseUrl_ShowAsPlainText()
    {
        var content = QuestionChatContent.Build(On(), "HA-1", "A question", "how?", null, QuestionLevels.HoldName);

        Assert.All(content.Links, l => Assert.Null(l.Url));
    }

    private static ChatLinks On(string? baseUrl = null) => new(Options.Create(new HatchOptions { PublicBaseUrl = baseUrl! }));
}

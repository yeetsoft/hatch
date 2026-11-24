using Hatch.Api.Services.Auth;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Chat;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Tests.Chat;

/// <summary>What a refused press is told, case by case. Pure: no database, no Slack.</summary>
public class QuestionPressReplyContentTests
{
    private const string Origin = "https://hatch.example";

    private static ChatLinks On() => new(Options.Create(new HatchOptions { PublicBaseUrl = Origin }));

    private static ChatLinks Off() => new(Options.Create(new HatchOptions()));

    [Fact]
    public void NoLink_SaysToLinkInSettings()
    {
        var reply = QuestionPressReplyContent.Build(On(), new PressOutcome(PressCase.NoLink, QuestionId: 7))!;

        Assert.Equal("Link your Slack account to answer from here", reply.Title);
        Assert.Equal(
            "Hatch does not know which person you are in this Slack workspace. Link your account in Settings, then press the button again.",
            reply.Body);
        Assert.Empty(reply.Lines);
        Assert.Equal([new ChatLink("Settings", $"{Origin}/settings")], reply.Links);
    }

    [Fact]
    public void CannotSee_NamesNothing()
    {
        var reply = QuestionPressReplyContent.Build(On(), new PressOutcome(PressCase.CannotSee, "HA-1", 7))!;

        Assert.Equal("Your Hatch account cannot see this project", reply.Title);
        Assert.Equal("Ask an admin to add you to it, then press the button again.", reply.Body);
        Assert.Empty(reply.Lines);
        Assert.Empty(reply.Links);
        Assert.DoesNotContain("HA-1", reply.Write());
    }

    [Fact]
    public void AlreadyAnswered_SaysWhoAnsweredAndWithWhat_AndLinksTheIssue()
    {
        var outcome = new PressOutcome(PressCase.AlreadyAnswered, "HA-1", 7, "Grace", ActorKind.Person, "child-weighted");

        var reply = QuestionPressReplyContent.Build(On(), outcome)!;

        Assert.Equal("This question already has an answer", reply.Title);
        Assert.Null(reply.Body);
        Assert.Equal(["Grace answered: child-weighted"], reply.Lines);
        Assert.Equal([new ChatLink("HA-1", $"{Origin}/issues/HA-1")], reply.Links);
    }

    [Theory]
    [InlineData(false, "hatch-agent answered: leaf-weighted")]
    [InlineData(true, "A program answered: leaf-weighted")]
    public void AlreadyAnswered_WordsTheAnswerAsTheQuestionsMessageDoes(bool lookBack, string line)
    {
        var outcome = new PressOutcome(PressCase.AlreadyAnswered, "HA-1", 7, "hatch-agent", ActorKind.Key, "leaf-weighted", lookBack);

        var reply = QuestionPressReplyContent.Build(On(), outcome)!;

        Assert.Equal([line], reply.Lines);
        Assert.Equal([QuestionChatContent.AnswerLine("hatch-agent", "leaf-weighted", lookBack)], reply.Lines);
    }

    [Fact]
    public void WorkWentAhead_LinksTheBallot()
    {
        var reply = QuestionPressReplyContent.Build(On(), new PressOutcome(PressCase.WorkWentAhead, "HA-1", 7))!;

        Assert.Equal("The work already went ahead on this question", reply.Title);
        Assert.Equal("Open the Ballot to keep that decision or overturn it.", reply.Body);
        Assert.Equal([new ChatLink("Ballot", $"{Origin}/ballot")], reply.Links);
    }

    [Fact]
    public void NoSuchOption_LinksTheIssue()
    {
        var reply = QuestionPressReplyContent.Build(On(), new PressOutcome(PressCase.NoSuchOption, "HA-1", 7))!;

        Assert.Equal("This button no longer works", reply.Title);
        Assert.Equal("Hatch cannot act on it any more. Answer the question from Hatch instead.", reply.Body);
        Assert.Equal([new ChatLink("HA-1", $"{Origin}/issues/HA-1")], reply.Links);
    }

    [Fact]
    public void AMessageHatchDidNotSend_LinksTheBallot_NeverAnIssue()
    {
        var reply = QuestionPressReplyContent.Build(On(), new PressOutcome(PressCase.NotOurs, QuestionId: 7))!;

        Assert.Equal("This button no longer works", reply.Title);
        Assert.Equal([new ChatLink("Ballot", $"{Origin}/ballot")], reply.Links);
    }

    [Fact]
    public void WithNoPublicAddress_EachLinkShowsItsLabelAlone()
    {
        var cases = new[]
        {
            new PressOutcome(PressCase.NoLink),
            new PressOutcome(PressCase.AlreadyAnswered, "HA-1", 7, "Grace", ActorKind.Person, "x"),
            new PressOutcome(PressCase.WorkWentAhead, "HA-1", 7),
            new PressOutcome(PressCase.NoSuchOption, "HA-1", 7),
            new PressOutcome(PressCase.NotOurs),
        };

        foreach (var outcome in cases)
        {
            var reply = QuestionPressReplyContent.Build(Off(), outcome)!;
            var link = Assert.Single(reply.Links);
            Assert.Null(link.Url);
            Assert.False(string.IsNullOrWhiteSpace(link.Label));
        }
    }

    [Theory]
    [InlineData(PressCase.Answered)]
    [InlineData(PressCase.Ignored)]
    public void APressThatAnsweredOrWasNotOurs_NeedsNoReply(PressCase which) =>
        Assert.Null(QuestionPressReplyContent.Build(On(), new PressOutcome(which, "HA-1", 7)));
}

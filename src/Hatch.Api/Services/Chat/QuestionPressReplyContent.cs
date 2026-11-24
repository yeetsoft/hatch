using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Services.Chat;

/// <summary>
/// What a person is told when their press on a question's button answered
/// nothing - see docs/hatch-in-slack.md, "When a press is refused". Only the
/// presser sees it. It carries nothing a person who cannot see the project
/// should not learn: the case that says they cannot see it names no issue,
/// project or question.
/// </summary>
public static class QuestionPressReplyContent
{
    /// <summary>The reply for an outcome, or null when the press needs none: it answered, or was not for this handler.</summary>
    public static ChatMessageContent? Build(ChatLinks links, PressOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(outcome);

        switch (outcome.Case)
        {
            case PressCase.NoLink:
                return new ChatMessageContent(
                    "Link your Slack account to answer from here",
                    "Hatch does not know which person you are in this Slack workspace. Link your account in Settings, then press the button again.",
                    [],
                    [new ChatLink("Settings", links.Settings())]);

            case PressCase.CannotSee:
                return new ChatMessageContent(
                    "Your Hatch account cannot see this project",
                    "Ask an admin to add you to it, then press the button again.",
                    [],
                    []);

            case PressCase.AlreadyAnswered:
                return new ChatMessageContent(
                    "This question already has an answer",
                    null,
                    outcome.AnswerBody is null
                        ? []
                        : [QuestionChatContent.AnswerLine(outcome.AnsweredBy, outcome.AnswerBody, outcome.LookBack)],
                    outcome.IssueKey is { } answered ? [new ChatLink(answered, links.Issue(answered))] : [new ChatLink("Ballot", links.Ballot())]);

            case PressCase.WorkWentAhead:
                return new ChatMessageContent(
                    "The work already went ahead on this question",
                    "Open the Ballot to keep that decision or overturn it.",
                    [],
                    [new ChatLink("Ballot", links.Ballot())]);

            case PressCase.NotOurs:
            case PressCase.NoSuchOption:
                // A message Hatch did not send is never tied to an issue, so it links to the Ballot.
                return new ChatMessageContent(
                    "This button no longer works",
                    "Hatch cannot act on it any more. Answer the question from Hatch instead.",
                    [],
                    [outcome.IssueKey is { } key ? new ChatLink(key, links.Issue(key)) : new ChatLink("Ballot", links.Ballot())]);

            default:
                return null;
        }
    }
}

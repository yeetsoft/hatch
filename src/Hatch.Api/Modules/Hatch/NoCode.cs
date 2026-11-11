using System.Text.Json;
using System.Text.Json.Nodes;
using Hatch.Api.Ef;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The rules every site shares about an issue's no-code declaration - see
/// <see cref="EfHatchIssue.NoCode"/>. The declaration is a claim that the work
/// needed no branch; anything that shows otherwise, or that sends the ticket
/// back to be worked, sets it aside, and the trail says so. A declared issue is
/// carried through the close hop when it is pre-approved or when its project's
/// NoCodeCloses is on; the merge setting (CheckApprovesMerge) alone never
/// carries a childless one.
/// </summary>
public static class NoCode
{
    /// <summary>The sentence a judge gives for an issue that declared it, so every reader says it the same way.</summary>
    public const string Sentence = "finished with no code - a person closes it";

    public static bool Declared(EfHatchIssue issue) => Declared(issue.NoCode);

    /// <summary>The same question of a projected <see cref="EfHatchIssue.NoCode"/>, for a caller that selected the column and not the row.</summary>
    public static bool Declared(string? noCode) => !string.IsNullOrEmpty(noCode);

    /// <summary>
    /// What a card draws for the declaration: null on an issue with a child,
    /// since its children carry the code and a declaration there says
    /// nothing, else <paramref name="noCode"/> unchanged. The declaration
    /// itself - the issue page's No code row, <c>hatch show</c>, the
    /// attention panel and the close hop - is untouched by this; only the
    /// card is asked to look past it.
    /// </summary>
    public static string? OnCard(string? noCode, long issueId, PriorityTree priorities) =>
        priorities.HasChildren(issueId) ? null : noCode;

    /// <summary>
    /// Whether <paramref name="status"/> is left of the review column: not
    /// terminal, not deferred, and not the review column or anything to its
    /// right. A declaration is the review column's to carry, so this is where
    /// one is set aside rather than kept.
    /// </summary>
    public static bool LeftOfReview(EfHatchStatus status, List<EfHatchStatus> statuses) =>
        !(status.IsTerminal || status.IsDeferred || Columns.AtOrPastReview(statuses, status.Id));

    /// <summary>
    /// Clears the declaration when the issue is moved into a column left of the
    /// review column, and returns the event to add; null when there was nothing
    /// to clear or the column is review or to its right. A terminal column and a
    /// deferred one are not left of review - closing a ticket, or shelving it,
    /// does not retract what it said. Call it beside every write of
    /// <c>StatusId</c>.
    /// </summary>
    public static EfHatchIssueEvent? ClearOnMove(
        EfHatchIssue issue, EfHatchStatus to, List<EfHatchStatus> statuses, string actor, DateTimeOffset now)
    {
        if (!Declared(issue)) return null;
        if (!LeftOfReview(to, statuses)) return null;

        var from = issue.NoCode;
        issue.NoCode = null;
        issue.NoCodeAt = null;

        return new EfHatchIssueEvent
        {
            Actor = actor,
            Kind = EfHatchIssueEvent.NoCodeChanged,
            Payload = JsonSerializer.Serialize(new { from, to = (string?)null, moved = to.Name }),
            At = now,
        };
    }

    /// <summary>
    /// Clears the declaration of an issue that stands in <paramref name="standing"/>,
    /// a column left of review, and returns the event to add; null when there
    /// was nothing to clear or the column is review or to its right, terminal or
    /// deferred. The declaration is the review column's: an increment claiming
    /// the ticket and an increment letting it go are the two boundaries at
    /// which a ticket still left of review has been, or is about to be, worked
    /// again, so both call this. The payload names the column it stood in.
    /// </summary>
    public static EfHatchIssueEvent? ClearStanding(
        EfHatchIssue issue, EfHatchStatus standing, List<EfHatchStatus> statuses, string actor, DateTimeOffset now)
    {
        if (!Declared(issue)) return null;
        if (!LeftOfReview(standing, statuses)) return null;

        var from = issue.NoCode;
        issue.NoCode = null;
        issue.NoCodeAt = null;

        return new EfHatchIssueEvent
        {
            Actor = actor,
            Kind = EfHatchIssueEvent.NoCodeChanged,
            Payload = JsonSerializer.Serialize(new { from, to = (string?)null, standing = standing.Name }),
            At = now,
        };
    }

    /// <summary>
    /// Clears the declaration because a branch or a pull request now exists, and
    /// returns the event to add. The payload is the reason that was set aside
    /// plus whatever <paramref name="why"/> carries, spread beside it.
    /// </summary>
    public static EfHatchIssueEvent SetAside(
        EfHatchIssue issue, object why, string actor, DateTimeOffset now)
    {
        var payload = new JsonObject { ["reason"] = issue.NoCode };
        foreach (var (name, value) in JsonSerializer.SerializeToNode(why)!.AsObject())
            payload[name] = value?.DeepClone();

        issue.NoCode = null;
        issue.NoCodeAt = null;

        return new EfHatchIssueEvent
        {
            Actor = actor,
            Kind = EfHatchIssueEvent.NoCodeSetAside,
            Payload = payload.ToJsonString(),
            At = now,
        };
    }
}

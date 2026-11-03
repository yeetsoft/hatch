using System.Text.Json;
using System.Text.Json.Nodes;
using Hatch.Api.Ef;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The rules every site shares about an issue's no-code declaration - see
/// <see cref="EfHatchIssue.NoCode"/>. The declaration is a claim that the work
/// needed no branch; anything that shows otherwise, or that sends the ticket
/// back to be worked, sets it aside, and the trail says so.
/// </summary>
public static class NoCode
{
    /// <summary>The sentence a judge gives for an issue that declared it, so every reader says it the same way.</summary>
    public const string Sentence = "finished with no code - a person closes it";

    public static bool Declared(EfHatchIssue issue) => Declared(issue.NoCode);

    /// <summary>The same question of a projected <see cref="EfHatchIssue.NoCode"/>, for a caller that selected the column and not the row.</summary>
    public static bool Declared(string? noCode) => !string.IsNullOrEmpty(noCode);

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
        if (to.IsTerminal || to.IsDeferred || Columns.AtOrPastReview(statuses, to.Id)) return null;

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

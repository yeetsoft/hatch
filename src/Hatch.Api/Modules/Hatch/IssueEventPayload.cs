using System.Text.Json;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The one parser for <see cref="EfHatchIssueEvent.Payload"/>, shared so a row
/// reads the same way whether it came back through
/// <see cref="IssueThreadController"/> or <see cref="ActivityController"/>.
/// </summary>
public static class IssueEventPayload
{
    /// <summary>
    /// The stored payload as JSON rather than as a string of JSON, so a client
    /// reads it without a second parse. A row that will not parse - hand-edited,
    /// or written by a shape this build does not know - reads as absent rather
    /// than throwing the whole trail away.
    /// </summary>
    public static JsonElement? Parse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;

        try
        {
            return JsonDocument.Parse(stored).RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The payload of a <see cref="EfHatchIssueEvent.StatusChanged"/> row, in
    /// one place so both writers say the same thing. The column is named and
    /// numbered: names because the trail is read long after an id means
    /// anything, ids because a name stops matching once the column is renamed.
    /// <paramref name="from"/> is null for an issue with no previous column,
    /// and then <c>from</c> and <c>fromId</c> are both JSON null. Each
    /// <paramref name="extra"/> pair lands after the four keys, in order.
    /// </summary>
    public static Dictionary<string, object?> StatusChanged(
        EfHatchStatus? from, EfHatchStatus to, params (string Key, object? Value)[] extra)
    {
        var payload = new Dictionary<string, object?>
        {
            ["from"] = from?.Name,
            ["to"] = to.Name,
            ["fromId"] = from?.Id,
            ["toId"] = to.Id,
        };
        foreach (var (key, value) in extra) payload[key] = value;
        return payload;
    }

    /// <summary>
    /// The payload of a <see cref="EfHatchIssueEvent.ContributionFiled"/> row.
    /// Every field is written, in this order, with JSON null where the record
    /// holds none, so a reader finds the same keys on every row.
    /// </summary>
    public static Dictionary<string, object?> ContributionFiled(EfHatchContribution c) => new()
    {
        ["pullRequestUrl"] = c.PullRequestUrl,
        ["headBranch"] = c.HeadBranch,
        ["authorLogin"] = c.AuthorLogin,
        ["authorAssociation"] = c.AuthorAssociation,
        ["originInstance"] = c.OriginInstance,
        ["originProjectKey"] = c.OriginProjectKey,
        ["originKey"] = c.OriginKey,
    };
}

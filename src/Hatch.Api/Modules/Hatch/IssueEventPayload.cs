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
}

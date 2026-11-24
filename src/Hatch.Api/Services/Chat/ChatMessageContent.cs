using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hatch.Api.Services.Chat;

/// <summary>
/// What a queued message says, as plain text: no Slack syntax and no escaping.
/// The provider formats it when it sends.
/// </summary>
/// <param name="Title">One line naming the thing.</param>
/// <param name="Body">A short paragraph, or null for none.</param>
/// <param name="Lines">Short facts, one per line, in order.</param>
/// <param name="Links">Where to go next. A link with no <see cref="ChatLink.Url"/> shows its label as text.</param>
/// <param name="Actions">Buttons under the message, in order. Null - never an empty list - means none, so a message with none stores the JSON it always had.</param>
public sealed record ChatMessageContent(
    string Title,
    string? Body,
    IReadOnlyList<string> Lines,
    IReadOnlyList<ChatLink> Links,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ChatAction>? Actions = null)
{
    /// <summary>The JSON stored in <c>ChatMessages.Content</c>. The same content always gives the same text.</summary>
    public string Write() => JsonSerializer.Serialize(this, JsonSerializerOptions.Web);

    /// <summary>The content a stored row holds, or null if the text is not one.</summary>
    public static ChatMessageContent? Read(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ChatMessageContent>(json, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>A label and where it goes. A null <paramref name="Url"/> is shown as the label alone.</summary>
public sealed record ChatLink(string Label, string? Url);

/// <summary>A button: the text on it, and the id a press of it comes back with. <paramref name="Label"/> is plain text, never markup.</summary>
public sealed record ChatAction(string Id, string Label, bool Primary = false);

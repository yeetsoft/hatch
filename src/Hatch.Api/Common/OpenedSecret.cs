using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hatch.Api.Common;

/// <summary>
/// A secret that has been opened: the plaintext, held in a type that will not
/// print it.
///
/// <see cref="ToString"/> is <c>[secret]</c>, and so is the JSON, under any
/// serializer options. A snapshot that is logged, returned or interpolated by
/// mistake gives nothing away. The plaintext comes out through
/// <see cref="Reveal"/> and nothing else, so a search for that one name finds
/// every place a secret is used.
///
/// "Is it set" is <c>secret is not null</c>, asked without reading the value.
/// <see cref="From"/> answers null for null and for blank, so a set secret is
/// a non-null one and unset and blank are the same answer.
///
/// Equality is by reference: comparing two secrets by value is a way to read one.
/// </summary>
[JsonConverter(typeof(OpenedSecretJsonConverter))]
public sealed class OpenedSecret
{
    /// <summary>What every printed form of a secret says.</summary>
    public const string Placeholder = "[secret]";

    private readonly string plaintext;

    private OpenedSecret(string plaintext) => this.plaintext = plaintext;

    /// <summary>The secret holding <paramref name="plaintext"/>, or null when it is null or blank.</summary>
    public static OpenedSecret? From(string? plaintext) =>
        string.IsNullOrWhiteSpace(plaintext) ? null : new OpenedSecret(plaintext);

    /// <summary>The plaintext. The one way out: call it only where the value is handed to the thing that needs it.</summary>
    public string Reveal() => plaintext;

    /// <summary>The same secret without surrounding whitespace.</summary>
    public OpenedSecret Trim() => new(plaintext.Trim());

    public override string ToString() => Placeholder;
}

/// <summary>Writes the placeholder, never the value, and reads nothing.</summary>
public sealed class OpenedSecretJsonConverter : JsonConverter<OpenedSecret>
{
    public override OpenedSecret Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("An opened secret is never read from JSON.");

    public override void Write(Utf8JsonWriter writer, OpenedSecret value, JsonSerializerOptions options) =>
        writer.WriteStringValue(OpenedSecret.Placeholder);
}

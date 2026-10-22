using System.Text.Json;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The jsonb array shape shared by every runner-tags column - a project's own
/// and an issue's own alike. One read and one write so both entities agree on
/// how a stored string becomes a list and back.
/// </summary>
public static class RunnerTagColumn
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>What a reader gets back - never null, possibly empty.</summary>
    public static IReadOnlyList<string> Read(string stored) =>
        JsonSerializer.Deserialize<List<string>>(stored, Json) ?? [];

    /// <summary>What goes in the column. Always a real array, even when empty.</summary>
    public static string Write(IReadOnlyList<string> tags) =>
        JsonSerializer.Serialize(tags, Json);
}

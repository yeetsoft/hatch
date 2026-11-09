using System.Text;
using System.Text.Json;

namespace Hatch.Contracts;

/// <summary>One entry of <c>.hatch/dependencies.json</c>: a dependency id, and the version it asks for when it asks for one.</summary>
public sealed record DeclaredDependency(string Id, string? Version);

/// <summary>
/// <c>.hatch/dependencies.json</c> parsed into the entries it declares, or the
/// one sentence saying why not.
/// </summary>
/// <remarks>
/// A pure function of its arguments. The file, trunk and sha a refusal names are
/// not known here - the caller that read the file off a checkout holds those - so
/// <see cref="Parse"/> returns a problem that reads after a file name, an entry's
/// carrying its position. <see cref="System.Text.Json.JsonDocument"/> has no node
/// positions, so only a syntax error names a line.
/// </remarks>
public static class DependencyDeclaration
{
    /// <summary>Where this file lives in a repository's checkout.</summary>
    public const string Path = ".hatch/dependencies.json";

    /// <summary>Restates the playbooks file's cap, which <c>Hatch.Contracts</c> cannot reference; a test pins them equal.</summary>
    public const int MaxFileBytes = 512 * 1024;

    private const string ListProblem = "needs a top-level \"dependencies\" key holding a list";

    private const string ShapeProblem = "a dependency is an id string or an object with \"id\" and an optional \"version\"";

    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    public static (IReadOnlyList<DeclaredDependency>? Dependencies, string? Problem) Parse(
        byte[] content, Dependencies? known = null)
    {
        known ??= Dependencies.Default;

        if (content.Length > MaxFileBytes)
            return (null, $"is larger than {MaxFileBytes / 1024} KB");

        var bytes = content.AsSpan().StartsWith(Bom) ? content.AsSpan(Bom.Length) : content.AsSpan();

        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return (null, "is not valid UTF-8");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false,
            });
        }
        catch (JsonException ex)
        {
            return (null, ex.LineNumber is { } line ? $"is not valid JSON (line {line + 1})" : "is not valid JSON");
        }

        using (document)
        {
            return ParseRoot(document.RootElement, known);
        }
    }

    private static (IReadOnlyList<DeclaredDependency>? Dependencies, string? Problem) ParseRoot(
        JsonElement root, Dependencies known)
    {
        if (root.ValueKind != JsonValueKind.Object) return (null, ListProblem);

        // EnumerateObject, not TryGetProperty: JsonDocument accepts a repeated key and the lookup would hide it.
        JsonElement? list = null;
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name == "dependencies") list ??= property.Value;
        }

        if (list is null) return (null, ListProblem);

        var seenList = false;
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name == "dependencies" && !seenList)
            {
                seenList = true;
                continue;
            }

            return (null, $"a dependencies file has one key, \"dependencies\" - not \"{property.Name}\" too");
        }

        if (list.Value.ValueKind != JsonValueKind.Array) return (null, ListProblem);
        var array = list.Value;

        var entries = new List<DeclaredDependency>();
        var firstDeclared = new Dictionary<string, int>(StringComparer.Ordinal);

        var position = 0;
        foreach (var node in array.EnumerateArray())
        {
            position++;

            var (entry, problem) = ParseEntry(node, known);
            if (problem is not null) return (null, FailEntry(position, problem));

            if (!firstDeclared.TryAdd(entry!.Id, position))
                return (null, FailEntry(position, $"`{entry.Id}` is already declared by entry {firstDeclared[entry.Id]}"));

            entries.Add(entry);
        }

        return (entries, null);
    }

    private static (DeclaredDependency? Entry, string? Problem) ParseEntry(JsonElement node, Dependencies known)
    {
        string? id;
        JsonElement? version = null;

        switch (node.ValueKind)
        {
            case JsonValueKind.String:
                id = node.GetString();
                break;

            case JsonValueKind.Object:
                id = null;
                var seenId = false;
                var seenVersion = false;
                foreach (var property in node.EnumerateObject())
                {
                    if (property.Name == "id" && !seenId)
                    {
                        seenId = true;
                        if (property.Value.ValueKind != JsonValueKind.String) return (null, ShapeProblem);
                        id = property.Value.GetString();
                    }
                    else if (property.Name == "version" && !seenVersion)
                    {
                        seenVersion = true;
                        version = property.Value;
                    }
                    else
                    {
                        return (null, ShapeProblem);
                    }
                }

                if (!seenId) return (null, ShapeProblem);
                break;

            default:
                return (null, ShapeProblem);
        }

        if (known.IdProblem(id!) is { } idProblem) return (null, idProblem);

        string? declared = null;
        if (version is { } raw)
        {
            if (raw.ValueKind != JsonValueKind.String)
                return (null, $"a version is a string of one to three dotted whole numbers, like \"10\" or \"10.0.100\" - not {raw.GetRawText()}");

            declared = raw.GetString();
            if (DependencyVersion.Problem(declared) is { } versionProblem) return (null, versionProblem);
        }

        return (new DeclaredDependency(id!, declared), null);
    }

    private static string FailEntry(int position, string problem) => $"entry {position}: {problem}";
}

using System.Security.Cryptography;
using System.Text;
using Hatch.Contracts;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Hatch.Api.Modules.Hatch;

/// <summary>What <see cref="ChangelogFileState.Judge"/> found in the bytes a trunk holds.</summary>
/// <param name="Lacking">The keys of the entries the file lacks or holds differently, for <c>behind</c>; the board's own projection of them is what a task is filed from.</param>
public sealed record ChangelogFileVerdict(
    string State, int? Behind, string? Problem, string? Hash, IReadOnlySet<string> Lacking);

/// <summary>
/// Whether the bytes a trunk holds at <see cref="ChangelogFile.Path"/> are
/// what the board would write, a file of its shape that has fallen behind, or
/// not its file at all.
///
/// Pure and synchronous - no <see cref="HatchContext"/> - because reading the
/// projection off the board is <see cref="ChangelogFiles"/>'s job and this
/// comparison is tested without a database. The caller sends no verdict: the
/// board reads the bytes itself.
/// </summary>
public static class ChangelogFileState
{
    private static readonly string[] Fields = ["key", "url", "project", "type", "title", "note", "shipped", "beneath"];

    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    private sealed record ParsedFile(Dictionary<string, Dictionary<string, string?>> Entries);

    /// <param name="content">The blob's exact bytes, or null for a trunk with nothing at the path.</param>
    /// <param name="projectionText">What <see cref="Changelog.RenderFile"/> writes for this repository now.</param>
    /// <param name="sha">The trunk commit the bytes were read at - named in the sentence a foreign file gets.</param>
    public static ChangelogFileVerdict Judge(byte[]? content, string projectionText, string sha)
    {
        if (content is null) return new(ChangelogFile.AbsentState, null, null, null, None);

        var hash = Convert.ToHexStringLower(SHA256.HashData(content));
        if (content.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(projectionText)))
            return new(ChangelogFile.CurrentState, null, null, hash, None);

        var (file, why) = Parse(content);
        if (file is null) return new(ChangelogFile.ForeignState, null, Foreign(sha, why!), hash, None);

        // The projection is the board's own file, so it parses; were it ever not,
        // everything in the file would read as differing rather than throw.
        var (projected, _) = Parse(Encoding.UTF8.GetBytes(projectionText));

        var lacking = new HashSet<string>(StringComparer.Ordinal);
        var behind = 0;
        foreach (var (key, entry) in projected?.Entries ?? [])
        {
            if (file.Entries.TryGetValue(key, out var held) && Fields.All(f => entry[f] == held[f])) continue;

            lacking.Add(key);
            behind++;
        }

        // An entry the file holds that the board no longer does: an issue reopened since.
        behind += file.Entries.Keys.Count(key => projected is null || !projected.Entries.ContainsKey(key));

        // Same entries, different bytes - order or whitespace only. It is still
        // not what the board writes, and "behind by 0" would read as current.
        return new(ChangelogFile.BehindState, Math.Max(behind, 1), null, hash, lacking);
    }

    private static string Foreign(string sha, string why)
    {
        if (why.Length > 200) why = why[..200] + "...";
        var shortSha = sha.Length > 7 ? sha[..7] : sha;
        return $"{ChangelogFile.Path} at {shortSha} is not a file Hatch wrote ({why}) - move or remove it before Hatch will write there";
    }

    private static (ParsedFile? File, string? Why) Parse(byte[] content)
    {
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(content);
        }
        catch (DecoderFallbackException)
        {
            return (null, "it is not valid UTF-8");
        }

        var firstLine = text.Split('\n', 2)[0];
        if (!firstLine.StartsWith(Changelog.HeaderPrefix, StringComparison.Ordinal))
            return (null, "it does not open with Hatch's header");

        var yaml = new YamlStream();
        try
        {
            yaml.Load(new StringReader(text));
        }
        catch (YamlException e)
        {
            return (null, $"it is not valid YAML - line {e.Start.Line}");
        }

        if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
            return (null, "it is not a mapping");

        if (!root.Children.TryGetValue(new YamlScalarNode("version"), out var version)
            || version is not YamlScalarNode { Value: "1" })
            return (null, "it is not version 1");

        if (!root.Children.TryGetValue(new YamlScalarNode("entries"), out var entries) || entries is not YamlSequenceNode list)
            return (null, "it has no list of entries");

        var parsed = new Dictionary<string, Dictionary<string, string?>>(StringComparer.Ordinal);
        foreach (var node in list.Children)
        {
            if (node is not YamlMappingNode map)
                return (null, "an entry is not a mapping");

            var fields = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var field in Fields)
                fields[field] = map.Children.TryGetValue(new YamlScalarNode(field), out var value) && value is YamlScalarNode scalar
                    ? Value(scalar)
                    : null;

            if (fields["key"] is not { Length: > 0 } key)
                return (null, "an entry has no key");

            if (!parsed.TryAdd(key, fields))
                return (null, $"the key {key} is repeated");
        }

        return (new ParsedFile(parsed), null);
    }

    /// <summary>A plain <c>null</c>, <c>~</c> or empty scalar reads as null; a quoted one is the text it holds.</summary>
    private static string? Value(YamlScalarNode scalar) =>
        scalar.Style == ScalarStyle.Plain && scalar.Value is null or "" or "null" or "~" ? null : scalar.Value;
}

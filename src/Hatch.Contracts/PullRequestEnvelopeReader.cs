using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hatch.Contracts;

/// <summary>
/// Reads the envelope a Hatch-made pull request carries, from any body a
/// forge might hand back - including one a person or another tool has
/// since edited. Every rule here leans toward dropping what it does not
/// understand rather than failing the whole read, since a reader that threw
/// on an unknown field would break the moment a newer writer added one.
/// </summary>
public static class PullRequestEnvelopeReader
{
    private const int MaxKeyLength = 64;
    private const int MaxTitleLength = 500;
    private const int MaxDescriptionLength = 20000;
    private const int MaxStatusLength = 100;
    private const int MaxParentKeyLength = 64;
    private const int MaxUrlLength = 2048;
    private const int MaxInstanceLength = 2048;
    private const int MaxProjectLength = 64;

    private static readonly HashSet<string> KnownTypes = ["epic", "story", "task", "bug", "reflection"];

    private static readonly Regex FencePattern = new("```hatch\\r?\\n?(.*?)```", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex KeyPattern = new("^[A-Za-z][A-Za-z0-9]*-\\d+$", RegexOptions.Compiled);
    private static readonly Regex VersionZeroFirstLine = new(@"^\[(\S+)\]\((\S+)\)$", RegexOptions.Compiled);

    public static bool TryRead(string body, out PullRequestEnvelope? envelope, out string? reason)
    {
        envelope = null;

        if (!TryFindSpan(body, out var span, out reason))
            return false;

        var fenceMatch = FencePattern.Match(span);
        if (!fenceMatch.Success)
        {
            reason = "the envelope has no ```hatch fence";
            return false;
        }

        var json = fenceMatch.Groups[1].Value;
        if (string.IsNullOrWhiteSpace(json))
        {
            reason = "the envelope's fence is empty";
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            reason = $"the envelope's json could not be parsed: {ex.Message}";
            return false;
        }

        using (document)
        {
            var root = document.RootElement;

            if (!root.TryGetProperty("version", out var versionElement))
            {
                reason = "the envelope has no version";
                return false;
            }

            if (versionElement.ValueKind != JsonValueKind.Number || !versionElement.TryGetInt32(out var version))
            {
                reason = "the envelope's version is not an integer";
                return false;
            }

            if (version < 1)
            {
                reason = $"the envelope's version {version} is less than 1";
                return false;
            }

            var instance = OptionalString(root, "instance", MaxInstanceLength);
            var project = OptionalString(root, "project", MaxProjectLength);
            var chain = ReadChain(root);

            envelope = new PullRequestEnvelope(version, instance, project, chain);
            reason = null;
            return true;
        }
    }

    private static IReadOnlyList<PullRequestEnvelopeIssue> ReadChain(JsonElement root)
    {
        var chain = new List<PullRequestEnvelopeIssue>();
        if (!root.TryGetProperty("chain", out var chainElement) || chainElement.ValueKind != JsonValueKind.Array)
            return chain;

        foreach (var element in chainElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;

            var key = RequiredString(element, "key", MaxKeyLength);
            var title = RequiredString(element, "title", MaxTitleLength);
            if (key is null || title is null) continue;

            string? type = null;
            if (element.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
                && typeElement.GetString() is { } typeValue && KnownTypes.Contains(typeValue))
                type = typeValue;

            var description = OptionalString(element, "description", MaxDescriptionLength);
            var status = OptionalString(element, "status", MaxStatusLength);
            var parentKey = OptionalString(element, "parentKey", MaxParentKeyLength);
            var url = OptionalString(element, "url", MaxUrlLength);
            var truncated = element.TryGetProperty("truncated", out var truncatedElement) && truncatedElement.ValueKind == JsonValueKind.True;

            chain.Add(new PullRequestEnvelopeIssue(key, title, type, description, status, parentKey, url, truncated));
        }

        return chain;
    }

    private static string? RequiredString(JsonElement obj, string name, int cap)
    {
        if (obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            && value.GetString() is { Length: > 0 } s)
            return Cap(s, cap);

        return null;
    }

    private static string? OptionalString(JsonElement obj, string name, int cap)
    {
        if (obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            && value.GetString() is { } s)
            return Cap(s, cap);

        return null;
    }

    private static string Cap(string s, int cap) => s.Length > cap ? s[..cap] : s;

    /// <summary>
    /// The marker-delimited span between a lone <see cref="PullRequestEnvelopeMarkers.Start"/>
    /// and a lone <see cref="PullRequestEnvelopeMarkers.End"/> that follows it. Zero of either,
    /// or any arrangement that is not exactly one well-ordered pair other than more than one
    /// start, reads the same as finding none at all.
    /// </summary>
    private static bool TryFindSpan(string body, out string span, out string? reason)
    {
        span = "";
        reason = null;

        var startCount = Count(body, PullRequestEnvelopeMarkers.Start);
        var endCount = Count(body, PullRequestEnvelopeMarkers.End);

        if (startCount == 0 && endCount == 0)
        {
            reason = "no envelope found in the body";
            return false;
        }

        if (startCount > 1)
        {
            reason = "more than one envelope start marker";
            return false;
        }

        if (startCount != 1 || endCount != 1)
        {
            reason = "no envelope found in the body";
            return false;
        }

        var start = body.IndexOf(PullRequestEnvelopeMarkers.Start, StringComparison.Ordinal);
        var end = body.IndexOf(PullRequestEnvelopeMarkers.End, StringComparison.Ordinal);
        if (end < start)
        {
            reason = "no envelope found in the body";
            return false;
        }

        span = body[(start + PullRequestEnvelopeMarkers.Start.Length)..end];
        return true;
    }

    private static int Count(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    /// <summary>
    /// The pre-envelope shape (<c>docs/hatch.md</c>'s "version zero"): a title starting with
    /// the issue's key and a body whose first line is <c>[KEY](url)</c> for that same key,
    /// exactly - never a guess at one from a title or body that merely looks like it.
    /// </summary>
    public static bool TryReadVersionZero(string title, string body, out string? key, out string? instance)
    {
        key = null;
        instance = null;

        var firstToken = FirstToken(title);
        if (!KeyPattern.IsMatch(firstToken)) return false;

        var firstLine = FirstLine(body);
        var lineMatch = VersionZeroFirstLine.Match(firstLine);
        if (!lineMatch.Success) return false;

        var bracketedKey = lineMatch.Groups[1].Value;
        if (bracketedKey != firstToken) return false;

        var url = lineMatch.Groups[2].Value;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return false;

        key = firstToken;
        instance = DeriveInstance(uri);
        return true;
    }

    private static string FirstToken(string title)
    {
        var index = title.IndexOfAny([' ', '\t', '\n', '\r']);
        return index < 0 ? title : title[..index];
    }

    private static string FirstLine(string body)
    {
        var index = body.IndexOf('\n');
        return (index < 0 ? body : body[..index]).Trim();
    }

    private static string DeriveInstance(Uri uri)
    {
        const string marker = "/apps/hatch/";
        var path = uri.AbsolutePath;
        var markerIndex = path.IndexOf(marker, StringComparison.Ordinal);
        var prefix = markerIndex >= 0 ? path[..markerIndex] : path;
        return uri.Authority + prefix;
    }
}

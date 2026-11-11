using System.Text;
using System.Text.Json;

namespace Hatch.Contracts;

/// <summary>
/// Writes the envelope block a Hatch-made pull request carries, and replaces
/// one already there. <see cref="Replace"/> is the only public member: there
/// is nothing else a caller needs to do with an envelope but put it in a
/// body.
/// </summary>
public static class PullRequestEnvelopeWriter
{
    /// <summary>The body-size limit GitHub itself enforces on a pull request or an issue.</summary>
    public const int GitHubBodyLimit = 65536;

    /// <summary>
    /// <paramref name="body"/> with its envelope set to <paramref name="envelope"/>: the
    /// existing marker-to-marker span replaced if there is exactly one, the block appended
    /// after a blank line otherwise (or alone, if <paramref name="body"/> is empty). Never
    /// throws. When the result would exceed <paramref name="limit"/>, each chain entry's
    /// <c>Description</c> is trimmed, longest first, until it fits or every description is
    /// empty - everything else in the chain is left untouched.
    /// </summary>
    public static string Replace(string body, PullRequestEnvelope envelope, int limit = GitHubBodyLimit)
    {
        var hasSpan = TryFindSpan(body, out var start, out var end);
        var rest = hasSpan ? body[..start] + body[end..] : body;
        var separator = hasSpan || body.Length == 0 ? "" : "\n\n";
        var budget = limit - rest.Length - separator.Length;

        var block = Render(envelope, budget);

        return hasSpan
            ? body[..start] + block + body[end..]
            : body.Length == 0 ? block : body + separator + block;
    }

    private static bool TryFindSpan(string body, out int start, out int end)
    {
        start = -1;
        end = -1;

        if (Count(body, PullRequestEnvelopeMarkers.Start) != 1 || Count(body, PullRequestEnvelopeMarkers.End) != 1)
            return false;

        var s = body.IndexOf(PullRequestEnvelopeMarkers.Start, StringComparison.Ordinal);
        var e = body.IndexOf(PullRequestEnvelopeMarkers.End, StringComparison.Ordinal);
        if (e < s) return false;

        start = s;
        end = e + PullRequestEnvelopeMarkers.End.Length;
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

    private static string Render(PullRequestEnvelope envelope, int budget)
    {
        var chain = envelope.Chain.ToList();
        var rendered = RenderBlock(envelope, chain);

        while (rendered.Length > budget)
        {
            var victim = IndexOfLongestDescription(chain);
            if (victim < 0) break;

            var overage = rendered.Length - budget;
            var current = chain[victim].Description ?? "";
            var newLength = Math.Max(0, current.Length - overage);
            chain[victim] = chain[victim] with { Description = current[..newLength], Truncated = true };

            rendered = RenderBlock(envelope, chain);
        }

        return rendered;
    }

    private static int IndexOfLongestDescription(IReadOnlyList<PullRequestEnvelopeIssue> chain)
    {
        var bestIndex = -1;
        var bestLength = 0;
        for (var i = 0; i < chain.Count; i++)
        {
            var length = chain[i].Description?.Length ?? 0;
            if (length > bestLength)
            {
                bestLength = length;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private static string RenderBlock(PullRequestEnvelope envelope, IReadOnlyList<PullRequestEnvelopeIssue> chain)
    {
        var root = chain[^1];

        var sb = new StringBuilder();
        sb.Append(PullRequestEnvelopeMarkers.Start).Append('\n');
        sb.Append("<details>\n");
        sb.Append("<summary>").Append(HtmlEscape(root.Key)).Append(' ').Append(HtmlEscape(root.Title)).Append("</summary>\n");
        sb.Append('\n');
        sb.Append("```hatch\n");
        sb.Append(RenderJson(envelope, chain)).Append('\n');
        sb.Append("```\n");
        sb.Append('\n');
        sb.Append("</details>\n");
        sb.Append(PullRequestEnvelopeMarkers.End);
        return sb.ToString();
    }

    private static string RenderJson(PullRequestEnvelope envelope, IReadOnlyList<PullRequestEnvelopeIssue> chain)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", envelope.Version);
            if (envelope.Instance is not null) writer.WriteString("instance", envelope.Instance);
            if (envelope.Project is not null) writer.WriteString("project", envelope.Project);

            writer.WriteStartArray("chain");
            foreach (var issue in chain)
            {
                writer.WriteStartObject();
                writer.WriteString("key", issue.Key);
                writer.WriteString("title", issue.Title);
                if (issue.Type is not null) writer.WriteString("type", issue.Type);
                if (issue.Description is not null) writer.WriteString("description", issue.Description);
                if (issue.Status is not null) writer.WriteString("status", issue.Status);
                if (issue.ParentKey is not null) writer.WriteString("parentKey", issue.ParentKey);
                if (issue.Url is not null) writer.WriteString("url", issue.Url);
                writer.WriteBoolean("truncated", issue.Truncated);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string HtmlEscape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}

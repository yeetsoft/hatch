using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The one line an Ambitions row says about what the ambition is for: the first
/// sentence of its description, as plain text.
/// </summary>
/// <remarks>
/// A shape reader, in the spirit of <see cref="Principles"/>: it knows a heading, a
/// fence, a list marker and the inline marks, and treats everything else as prose.
/// No markdown library, because the only question asked is "what is the first
/// thing a person would read here". Nothing is stored; every read works it out
/// again from the description, so an edit shows at once.
/// </remarks>
public static partial class AmbitionSummary
{
    /// <summary>The most characters a summary may have, the cut mark included.</summary>
    public const int MaxLength = 200;

    private const string Cut = "\u2026";

    // Private-use marks that stand in for a code span while the rest is stripped, so its
    // contents are never touched.
    private const char SpanOpen = '\uE000';
    private const char SpanClose = '\uE001';

    /// <summary>
    /// The first sentence of the first block of prose in <paramref name="description"/>, or an
    /// empty string where there is none.
    /// </summary>
    /// <remarks>
    /// Skipped whole: HTML comments, fenced code, headings, table rows, rules and link
    /// reference lines. A block is consecutive prose lines joined by a space; a list marker
    /// starts a new block and is dropped, as is a leading <c>&gt;</c>. The first block that
    /// still has a letter or a digit once its inline markdown is stripped is the one read.
    /// It is cut after the first <c>.</c>, <c>!</c> or <c>?</c> (and any closing quote or
    /// bracket) that is followed by the end, or by a space and a character that is not a
    /// lowercase letter - so "e.g. a thing" is not cut at "e.g.". Over
    /// <see cref="MaxLength"/> it keeps 199 characters, never splitting a surrogate pair,
    /// trims, and adds an ellipsis.
    /// </remarks>
    public static string Of(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return "";

        foreach (var block in Blocks(description))
        {
            var text = Plain(block);
            if (!text.Any(char.IsLetterOrDigit)) continue;

            return Fit(FirstSentence(text));
        }

        return "";
    }

    private static IEnumerable<string> Blocks(string description)
    {
        var text = Comment().Replace(description.Replace("\r\n", "\n").Replace('\r', '\n'), "");

        var block = new StringBuilder();
        char fence = default;
        var fenceLength = 0;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();

            if (fence != default)
            {
                if (Fence().Match(line) is { Success: true } closing
                    && closing.Groups[1].Value[0] == fence && closing.Groups[1].Length >= fenceLength
                    && closing.Groups[2].Value.Length == 0)
                    fence = default;
                continue;
            }

            if (Fence().Match(line) is { Success: true } opening)
            {
                if (block.Length > 0) { yield return block.ToString(); block.Clear(); }
                fence = opening.Groups[1].Value[0];
                fenceLength = opening.Groups[1].Length;
                continue;
            }

            if (line.Length == 0 || Heading().IsMatch(line) || line.StartsWith('|') || Rule().IsMatch(line) || Reference().IsMatch(line))
            {
                if (block.Length > 0) { yield return block.ToString(); block.Clear(); }
                continue;
            }

            while (line.StartsWith('>')) line = line[1..].TrimStart();

            if (ListMarker().Match(line) is { Success: true } marker)
            {
                if (block.Length > 0) { yield return block.ToString(); block.Clear(); }
                line = line[marker.Length..];
            }

            if (line.Length == 0) continue;
            if (block.Length > 0) block.Append(' ');
            block.Append(line);
        }

        if (block.Length > 0) yield return block.ToString();
    }

    /// <summary>One block with its inline markdown taken off: what a person would read.</summary>
    private static string Plain(string block)
    {
        var spans = new List<string>();
        var text = CodeSpan().Replace(block, m =>
        {
            spans.Add(m.Groups[2].Value);
            return $"{SpanOpen}{spans.Count - 1}{SpanClose}";
        });

        // An escaped mark is the person's own character, so it is held aside the way a span is.
        text = Escaped().Replace(text, m =>
        {
            spans.Add(m.Groups[1].Value);
            return $"{SpanOpen}{spans.Count - 1}{SpanClose}";
        });

        text = Image().Replace(text, "");
        text = Link().Replace(text, "$1");
        text = AutoLink().Replace(text, "$1");
        text = Tag().Replace(text, "");
        text = text.Replace("~~", "");
        text = StarEmphasis().Replace(text, "");
        text = UnderscoreEmphasis().Replace(text, "");
        text = WebUtility.HtmlDecode(text);
        text = Whitespace().Replace(text, " ");

        text = Placeholder().Replace(text, m => spans[int.Parse(m.Groups[1].Value)]);

        return text.Trim();
    }

    private static string FirstSentence(string text)
    {
        var end = SentenceEnd().Match(text);
        return end.Success ? text[..(end.Index + end.Length)] : text;
    }

    private static string Fit(string text)
    {
        if (text.Length <= MaxLength) return text;

        var keep = MaxLength - 1;
        if (char.IsHighSurrogate(text[keep - 1])) keep--;

        return text[..keep].TrimEnd() + Cut;
    }

    [GeneratedRegex(@"<!--.*?(-->|$)", RegexOptions.Singleline)]
    private static partial Regex Comment();

    /// <summary>An opening or closing fence: three or more backticks or tildes, then whatever follows.</summary>
    [GeneratedRegex(@"^(`{3,}|~{3,})(.*)$")]
    private static partial Regex Fence();

    [GeneratedRegex(@"^#{1,6}([ \t]|$)")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^([-*_])([ \t]*\1){2,}[ \t]*$")]
    private static partial Regex Rule();

    [GeneratedRegex(@"^\[[^\]]+\]:[ \t]*\S")]
    private static partial Regex Reference();

    [GeneratedRegex(@"^([-*+]|\d{1,9}[.)])([ \t]+|$)")]
    private static partial Regex ListMarker();

    [GeneratedRegex(@"(?<!`)(`+)(?!`)(.+?)(?<!`)\1(?!`)")]
    private static partial Regex CodeSpan();

    [GeneratedRegex(@"!\[[^\]]*\](\([^)]*\)|\[[^\]]*\])")]
    private static partial Regex Image();

    [GeneratedRegex(@"\[([^\]]*)\](\([^)]*\)|\[[^\]]*\])")]
    private static partial Regex Link();

    [GeneratedRegex(@"<((?:https?|mailto):[^>\s]+)>")]
    private static partial Regex AutoLink();

    [GeneratedRegex(@"</?[A-Za-z][^>]*>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\\([!-/:-@\[-`{-~])")]
    private static partial Regex Escaped();

    /// <summary>A run of asterisks hugging a word on either side: emphasis. A spaced one, as in "5 * 3", stays.</summary>
    [GeneratedRegex(@"\*+(?=\S)|(?<=\S)\*+")]
    private static partial Regex StarEmphasis();

    /// <summary>A run of underscores at the edge of a word. One inside a word, as in snake_case, stays.</summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9])_+(?=\S)|(?<=\S)_+(?![A-Za-z0-9])")]
    private static partial Regex UnderscoreEmphasis();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex("\uE000(\\d+)\uE001")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"[.!?][""'\u201D\u2019)\]]*(?=$|\s+\P{Ll})")]
    private static partial Regex SentenceEnd();
}

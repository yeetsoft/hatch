using System.Text.RegularExpressions;

namespace Hatch.Cli;

/// <summary>
/// The repository paths a brief names in backticks - what <see cref="Prompt"/>
/// asks git about, to say what has landed on them since the brief was written.
/// </summary>
/// <remarks>
/// A guess, and a cheap one: a backticked span is a path only if it looks like
/// one, and a word that is not (<c>hatch ask</c>, <c>Prompt</c>, <c>true</c>)
/// is dropped here. A span that looks like a path but names nothing git knows
/// is dropped later, when git has no history for it.
/// </remarks>
public static partial class BriefPaths
{
    /// <summary>The most backticked spans looked at, so a long brief costs a bounded number of guesses.</summary>
    public const int MaxCandidates = 40;

    private const string Forbidden = "<>$*?:|\"";

    public static IReadOnlyList<string> Extract(string? description)
    {
        var found = new List<string>();
        if (string.IsNullOrEmpty(description)) return found;

        var scanned = 0;
        var fenced = false;

        foreach (var line in description.ReplaceLineEndings("\n").Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                fenced = !fenced;
                continue;
            }

            if (fenced) continue;

            foreach (Match span in Span().Matches(line))
            {
                if (++scanned > MaxCandidates) return found;
                if (Path(span.Groups[1].Value) is { } path && !found.Contains(path)) found.Add(path);
            }
        }

        return found;
    }

    private static string? Path(string raw)
    {
        var s = raw.Trim();
        if (s.StartsWith("./", StringComparison.Ordinal)) s = s[2..];
        s = Position().Replace(s, "");

        if (s.Length == 0 || s.Any(char.IsWhiteSpace) || s.IndexOfAny(Forbidden.ToCharArray()) >= 0) return null;
        if (s.StartsWith('/') || s.StartsWith('\\') || s.StartsWith('-') || s.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;
        if (s.Split('/').Any(part => part == ".." || part.Length == 0)) return null;

        return s.Contains('/') || Extension().IsMatch(s) ? s : null;
    }

    /// <summary>A span on one line, with no backtick inside it.</summary>
    [GeneratedRegex("`([^`\n]+)`")]
    private static partial Regex Span();

    /// <summary>A trailing <c>:12</c> or <c>:12-30</c>.</summary>
    [GeneratedRegex(@":\d+(-\d+)?$")]
    private static partial Regex Position();

    [GeneratedRegex(@"\.[A-Za-z0-9]{1,8}$")]
    private static partial Regex Extension();
}

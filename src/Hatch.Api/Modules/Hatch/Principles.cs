using System.Text.RegularExpressions;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Reads the bullets under an ambition description's <c>## Principles</c>
/// heading, in order - what <see cref="WorkController"/> hands a dispatched
/// session as <c>WorkDto.Ambition.Principles</c>.
/// </summary>
/// <remarks>
/// A shape reader, in the spirit of <see cref="PlanImportParser"/>: it knows a
/// heading and a bullet, and treats everything else - prose, blank lines, a
/// deeper heading - as text to skip. No markdown library, because the
/// question is only ever "is this line a heading, and is this line a bullet".
/// </remarks>
public static partial class Principles
{
    /// <summary>
    /// The bullets under the first <c>## Principles</c> heading (case-insensitive,
    /// nothing else on the line) in <paramref name="description"/>, in the order
    /// they are written. Any level-1-or-2 heading closes the section, including
    /// one that does not read "Principles" - so a second section starting
    /// elsewhere never leaks in. A bullet is a trimmed line starting <c>-</c> or
    /// <c>*</c> then a space; the marker is stripped and the rest kept exactly as
    /// written, inline markdown and all. Everything else inside the section -
    /// blank lines, prose, a level-3-or-deeper heading - is skipped. No
    /// <c>## Principles</c> heading anywhere returns an empty list.
    /// </summary>
    public static IReadOnlyList<string> Read(string description)
    {
        var principles = new List<string>();
        var inSection = false;

        foreach (var line in Lines(description))
        {
            if (H2().Match(line) is { Success: true } h2)
            {
                inSection = IsPrinciplesHeading().IsMatch(h2.Groups[1].Value.Trim());
                continue;
            }

            if (H1().IsMatch(line))
            {
                inSection = false;
                continue;
            }

            if (!inSection) continue;

            var trimmed = line.Trim();
            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
                principles.Add(trimmed[2..].Trim());
        }

        return principles;
    }

    private static string[] Lines(string content) =>
        content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    /// <summary>One hash then space: never opens the section, but closes one already open.</summary>
    [GeneratedRegex(@"^#[ \t]+(.+)$")]
    private static partial Regex H1();

    /// <summary>Exactly two hashes: opens the section where the text is "Principles", closes it otherwise.</summary>
    [GeneratedRegex(@"^##[ \t]+(.+)$")]
    private static partial Regex H2();

    [GeneratedRegex(@"^principles$", RegexOptions.IgnoreCase)]
    private static partial Regex IsPrinciplesHeading();
}

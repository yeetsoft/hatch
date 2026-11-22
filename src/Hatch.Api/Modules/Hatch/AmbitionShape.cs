using System.Text;
using System.Text.RegularExpressions;

namespace Hatch.Api.Modules.Hatch;

/// <summary>One uploaded file a draft ambition links to.</summary>
public record ShapeFile(Guid Id, string FileName, string ContentType);

/// <summary>
/// The title and brief a shaping press files its draft ambition with - what
/// the person gave, quoted, and the files they gave, linked.
///
/// Pure and synchronous - no <see cref="HatchContext"/> - because filing the
/// issue is the controller's job and the wording is tested without one.
/// The dreams are not here: they are recorded on the issue, not copied in.
/// </summary>
public static partial class AmbitionShape
{
    public const string Fallback = "Draft ambition";

    /// <summary>
    /// The first line of the words that has anything on it, on one line, cut to
    /// <see cref="EfHatchIssue.MaxTitleLength"/>. <see cref="Fallback"/> where
    /// there are no words. No prefix.
    /// </summary>
    public static string Title(string? words)
    {
        var first = NewLine().Split(words ?? "").FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
        if (first is null) return Fallback;

        var title = WhitespaceRun().Replace(first, " ").Trim();
        if (title.Length <= EfHatchIssue.MaxTitleLength) return title;

        var cut = title[..EfHatchIssue.MaxTitleLength];
        // A cut through a surrogate pair would leave half a character.
        if (char.IsHighSurrogate(cut[^1])) cut = cut[..^1];

        return cut.TrimEnd();
    }

    /// <summary>
    /// The markdown brief: says the ambition is a draft, quotes the words, and
    /// links each file in the form the page writes so the list under the
    /// description draws them.
    /// </summary>
    public static string Describe(string? words, IReadOnlyList<ShapeFile> files)
    {
        var parts = new List<string>
        {
            "This ambition is a draft that has not been shaped yet.",
            "This text is what the person gave, not the ambition's own.",
        };

        if (!string.IsNullOrWhiteSpace(words))
        {
            var quoted = NewLine().Split(words.Trim()).Select(l => l.Length == 0 ? ">" : $"> {l}");
            parts.Add("## In the person's words\n\n" + string.Join("\n", quoted));
        }

        if (files.Count > 0)
            parts.Add("## Files\n\n" + string.Join("\n", files.Select(Link)));

        return string.Join("\n\n", parts);
    }

    /// <summary>
    /// The markdown for one file: image syntax for a type the images route
    /// serves, link syntax for everything else. Must agree with
    /// <c>attachmentMarkdown</c> in the web app's <c>lib/descriptionImage.ts</c>.
    /// </summary>
    private static string Link(ShapeFile file)
    {
        var isImage = ImagesController.ImageContentTypes.Contains(file.ContentType);
        var escaped = file.FileName.Replace("\\", "\\\\").Replace("[", "\\[").Replace("]", "\\]");
        var name = escaped.Length == 0 ? (isImage ? "image" : "attachment") : escaped;
        return isImage ? $"![{name}](/api/images/{file.Id})" : $"[{name}](/api/attachments/{file.Id})";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();

    [GeneratedRegex(@"\r\n|\n|\r")]
    private static partial Regex NewLine();
}

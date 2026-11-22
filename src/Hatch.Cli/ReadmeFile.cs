using System.Globalization;
using System.Text;

namespace Hatch.Cli;

/// <summary>What <see cref="ReadmeFile.Judge"/> says to do with the copy a repository already holds.</summary>
internal enum ReadmeVerdict
{
    /// <summary>There is no copy: write one.</summary>
    Write,

    /// <summary>Hatch wrote the copy, and an older text: replace it.</summary>
    Replace,

    /// <summary>Hatch wrote the copy, at this version or a newer one: leave it.</summary>
    Keep,

    /// <summary>The copy's first line is not Hatch's: leave it, and say so.</summary>
    Foreign,
}

/// <summary>
/// The readme Hatch ships, and the one rule that decides whether a copy of it is
/// replaced.
/// </summary>
/// <remarks>
/// One copy of the text lives in this repository, <c>hatch-readme.md</c>, and the
/// binary embeds it. The same bytes go into every repository, so the text names no
/// domain, address, project key, person or path (<c>docs/ethos.md</c>).
/// <see cref="Judge"/> takes the file's text rather than a path so that whatever
/// else writes the file - a branch's blob, say - asks the same question.
/// </remarks>
internal static class ReadmeFile
{
    /// <summary>Where the file lives, beneath the root of a checkout.</summary>
    public const string Path = ".hatch/README.md";

    /// <summary>The sentence for a file Hatch did not write. The changelog's <c>foreign</c> says the same.</summary>
    public const string ForeignSentence = ".hatch/README.md is not a file Hatch wrote - move or remove it before Hatch will write there.";

    private const string Opening = "<!-- Written by Hatch. Version ";
    private const string Closing = ". ";

    private static readonly Lazy<string> Embedded = new(() =>
    {
        using var stream = typeof(ReadmeFile).Assembly.GetManifestResourceStream("hatch-readme.md")
                            ?? throw new InvalidOperationException("hatch-readme.md is not embedded in this build");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().ReplaceLineEndings("\n");
    });

    /// <summary>The text this build ships, with <c>\n</c> line endings whatever the checkout it was built in.</summary>
    public static string Text => Embedded.Value;

    /// <summary>The version on this text's first line.</summary>
    public static int Version => VersionOf(Text)
        ?? throw new InvalidOperationException("hatch-readme.md does not open with Hatch's version line");

    /// <summary>
    /// The version a file says Hatch wrote it at, or null where its first line is
    /// not Hatch's: no such line, no whole number, a number too large to hold, a
    /// byte-order mark ahead of it.
    /// </summary>
    public static int? VersionOf(string? content)
    {
        if (content is null) return null;

        var end = content.IndexOf('\n');
        var first = (end < 0 ? content : content[..end]).TrimEnd('\r');

        if (!first.StartsWith(Opening, StringComparison.Ordinal) || !first.EndsWith(" -->", StringComparison.Ordinal))
            return null;

        var rest = first[Opening.Length..];
        var digits = 0;
        while (digits < rest.Length && char.IsAsciiDigit(rest[digits])) digits++;

        if (digits == 0 || !rest[digits..].StartsWith(Closing, StringComparison.Ordinal)) return null;

        return int.TryParse(rest[..digits], NumberStyles.None, CultureInfo.InvariantCulture, out var version) ? version : null;
    }

    /// <summary>
    /// What to do about the copy a repository holds, or null where it holds none.
    /// </summary>
    public static ReadmeVerdict Judge(string? existing)
    {
        if (existing is null) return ReadmeVerdict.Write;

        return VersionOf(existing) switch
        {
            null => ReadmeVerdict.Foreign,
            var held when held < Version => ReadmeVerdict.Replace,
            _ => ReadmeVerdict.Keep,
        };
    }
}

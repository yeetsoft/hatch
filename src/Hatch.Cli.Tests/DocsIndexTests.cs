using System.Text;

namespace Hatch.Cli.Tests;

/// <summary>
/// <c>docs/hatch.md</c> opens with a <c>## Section index</c> that promises a
/// link to every section below it, so a reader can jump straight to the one
/// they need instead of scrolling a 500 KB file. Nothing but a human keeps
/// that promise honest - a heading renamed without touching the index, or an
/// index entry edited without touching the heading, reads fine until someone
/// actually clicks the link. So the index is read here, against the
/// headings themselves, and a drift between the two fails the build instead.
/// </summary>
public sealed class DocsIndexTests
{
    [Fact]
    public void The_index_comes_before_the_first_section()
    {
        var first = Headings().First(heading => heading.Level == 2);

        Assert.Equal("Section index", first.Text);
    }

    [Fact]
    public void Every_top_level_section_has_a_line_in_the_index()
    {
        var index = IndexBlock();

        var sections = Headings().Where(heading => heading.Level == 2 && heading.Text != "Section index");

        foreach (var section in sections)
        {
            var anchor = $"](#{Slug(section.Text)})";
            var occurrences = Occurrences(index, anchor);

            Assert.True(occurrences == 1,
                $"docs/hatch.md's index does not link '## {section.Text}' exactly once (expected one \"{anchor}\").");
        }
    }

    [Fact]
    public void Every_anchor_in_the_index_is_a_heading_in_the_file()
    {
        var index = IndexBlock();
        var slugs = Headings().Select(heading => Slug(heading.Text)).ToHashSet();

        foreach (var anchor in Anchors(index))
        {
            Assert.True(slugs.Contains(anchor),
                $"docs/hatch.md's index links to #{anchor}, which is not the slug of any ## or ### heading in the file.");
        }
    }

    /// <summary>
    /// Yields every anchor slug named by a <c>](#slug)</c> link in <paramref name="text"/>.
    /// </summary>
    private static IEnumerable<string> Anchors(string text)
    {
        for (var at = text.IndexOf("](#", StringComparison.Ordinal); at >= 0;
             at = text.IndexOf("](#", at + 1, StringComparison.Ordinal))
        {
            var from = at + "](#".Length;
            var to = text.IndexOf(')', from);
            if (to < 0) continue;

            yield return text[from..to];
        }
    }

    /// <summary>
    /// Yields every <c>## </c> or <c>### </c> heading in <see cref="Doc"/>, skipping
    /// lines inside a ``` fence, where a heading-shaped code sample should not count.
    /// </summary>
    private static IEnumerable<(int Level, string Text)> Headings()
    {
        var inFence = false;

        foreach (var line in File.ReadLines(Doc()))
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence) continue;

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                yield return (3, line["### ".Length..]);
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                yield return (2, line["## ".Length..]);
            }
        }
    }

    /// <summary>
    /// The text strictly between the <c>## Section index</c> heading and the
    /// next <c>## </c> heading after it.
    /// </summary>
    private static string IndexBlock()
    {
        var doc = File.ReadAllText(Doc());

        const string start = "## Section index";
        var from = doc.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, "docs/hatch.md has no '## Section index' heading.");
        from += start.Length;

        var to = doc.IndexOf("\n## ", from, StringComparison.Ordinal);
        Assert.True(to >= 0, "docs/hatch.md's '## Section index' is not followed by another '## ' heading.");

        return doc[from..to];
    }

    /// <summary>
    /// Lowercase, keep only letters, digits, spaces and hyphens, then turn
    /// spaces into hyphens - the rule a GitHub-style anchor follows.
    /// </summary>
    private static string Slug(string text)
    {
        var builder = new StringBuilder();

        foreach (var c in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c == ' ' || c == '-') builder.Append(c);
        }

        return builder.ToString().Replace(' ', '-');
    }

    private static int Occurrences(string text, string marker)
    {
        var count = 0;
        for (var at = text.IndexOf(marker, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(marker, at + marker.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// Walks up from the test binary to the repository, the way
    /// <see cref="DocsContractTests"/>'s own <c>Doc()</c> does.
    /// </summary>
    private static string Doc()
    {
        const string relative = "docs/hatch.md";

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException($"Could not find {relative} above {AppContext.BaseDirectory}.");
    }
}

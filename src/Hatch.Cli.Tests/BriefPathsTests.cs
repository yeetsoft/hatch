namespace Hatch.Cli.Tests;

/// <summary>Which backticked spans in a brief are repository paths.</summary>
public sealed class BriefPathsTests
{
    [Fact]
    public void A_file_and_a_file_with_a_line_are_paths()
    {
        var paths = BriefPaths.Extract("Edit `.hatch/dependencies.json` and `src/Hatch.Cli/Prompt.cs:40`, also `./docs/ethos.md:3-9`.");

        Assert.Equal([".hatch/dependencies.json", "src/Hatch.Cli/Prompt.cs", "docs/ethos.md"], paths);
    }

    [Theory]
    [InlineData("`hatch ask`")]
    [InlineData("`true`")]
    [InlineData("`Prompt`")]
    [InlineData("`https://example.test/a/b.html`")]
    [InlineData("`$HOME/x.txt`")]
    [InlineData("`../outside/x.txt`")]
    [InlineData("`/etc/passwd`")]
    [InlineData("`--flag.txt`")]
    [InlineData("`a/*.cs`")]
    [InlineData("`a b/c.cs`")]
    public void A_span_that_is_not_a_path_is_ignored(string span) =>
        Assert.Empty(BriefPaths.Extract($"See {span} for this."));

    [Fact]
    public void A_fenced_block_is_skipped()
    {
        var brief = "Before.\n```\n`src/a.cs`\n```\nAfter `src/b.cs`.";

        Assert.Equal(["src/b.cs"], BriefPaths.Extract(brief));
    }

    [Fact]
    public void A_span_is_one_line_only()
    {
        Assert.Empty(BriefPaths.Extract("`src/a\nb.cs`"));
    }

    [Fact]
    public void Duplicates_collapse_and_keep_the_order_of_appearance()
    {
        Assert.Equal(["b/x.cs", "a/y.cs"], BriefPaths.Extract("`b/x.cs` `a/y.cs` `./b/x.cs:2`"));
    }

    [Fact]
    public void Only_the_first_forty_spans_are_scanned()
    {
        var brief = string.Join(' ', Enumerable.Range(0, 45).Select(i => $"`d/f{i}.cs`"));

        var paths = BriefPaths.Extract(brief);

        Assert.Equal(BriefPaths.MaxCandidates, paths.Count);
        Assert.Equal("d/f39.cs", paths[^1]);
    }

    [Fact]
    public void No_description_is_no_paths()
    {
        Assert.Empty(BriefPaths.Extract(null));
        Assert.Empty(BriefPaths.Extract(""));
    }
}

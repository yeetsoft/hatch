using System.Security.Cryptography;
using System.Text;

namespace Hatch.Cli.Tests;

/// <summary>
/// The readme Hatch ships, and the one rule that decides whether a copy of it is
/// replaced. The text is the same bytes in every repository, so what it may hold
/// is held here.
/// </summary>
public sealed class ReadmeFileTests
{
    /// <summary>
    /// One row per version that has shipped: the version, and the SHA-256 of its
    /// text. A change to the text is a new row, and a new row is a new version.
    /// </summary>
    private static readonly SortedDictionary<int, string> Shipped = new()
    {
        [1] = "1da38eac5868772a86331acdca0418aa5dc500d718f6d40754593d5835d5865d",
    };

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string Held(int version) =>
        $"<!-- Written by Hatch. Version {version}. Change this text in Hatch, not here: a newer version replaces the file. -->\n# Hatch\n";

    [Fact]
    public void No_file_is_written()
    {
        Assert.Equal(ReadmeVerdict.Write, ReadmeFile.Judge(null));
    }

    [Fact]
    public void A_file_Hatch_wrote_with_a_lower_version_is_replaced()
    {
        Assert.Equal(ReadmeVerdict.Replace, ReadmeFile.Judge(Held(0)));
    }

    [Fact]
    public void A_file_Hatch_wrote_with_the_same_version_is_kept()
    {
        Assert.Equal(ReadmeVerdict.Keep, ReadmeFile.Judge(Held(ReadmeFile.Version)));
    }

    [Fact]
    public void A_file_Hatch_wrote_with_a_higher_version_is_kept_because_a_newer_hatch_wrote_it()
    {
        Assert.Equal(ReadmeVerdict.Keep, ReadmeFile.Judge(Held(ReadmeFile.Version + 1)));
        Assert.Equal(ReadmeVerdict.Keep, ReadmeFile.Judge(Held(int.MaxValue)));
    }

    [Fact]
    public void A_file_with_a_windows_line_ending_on_its_first_line_is_still_Hatchs()
    {
        Assert.Equal(ReadmeVerdict.Keep, ReadmeFile.Judge(Held(ReadmeFile.Version).Replace("\n", "\r\n")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("# Readme\n")]
    [InlineData("<!-- Written by Hatch. Change this text in Hatch, not here. -->\n")]
    [InlineData("<!-- Written by Hatch. Version one. Change this text in Hatch, not here. -->\n")]
    [InlineData("<!-- Written by Hatch. Version 1.5. Change this text in Hatch, not here. -->\n")]
    [InlineData("<!-- Written by Hatch. Version -1. Change this text in Hatch, not here. -->\n")]
    [InlineData("<!-- Written by Hatch. Version 99999999999. Change this text in Hatch, not here. -->\n")]
    [InlineData("\n<!-- Written by Hatch. Version 1. Change this text in Hatch, not here. -->\n")]
    [InlineData("﻿<!-- Written by Hatch. Version 1. Change this text in Hatch, not here. -->\n")]
    [InlineData("<!-- Written by Hatch. Version 1. Change this text in Hatch, not here.\n-->\n")]
    public void A_file_whose_first_line_is_not_Hatchs_is_foreign(string content)
    {
        Assert.Equal(ReadmeVerdict.Foreign, ReadmeFile.Judge(content));
    }

    [Fact]
    public void The_version_is_a_whole_number_on_the_first_line()
    {
        Assert.StartsWith("<!-- Written by Hatch. Version ", ReadmeFile.Text);
        Assert.True(ReadmeFile.Version >= 1);
    }

    [Fact]
    public void The_version_goes_up_whenever_the_text_changes()
    {
        var latest = Shipped.Keys.Max();

        Assert.True(ReadmeFile.Version >= latest,
            $"The readme is at version {ReadmeFile.Version}, below the shipped version {latest}. A version never goes down.");

        Assert.True(Shipped.ContainsKey(ReadmeFile.Version),
            $"The readme is at version {ReadmeFile.Version} and this test has no row for it. " +
            $"Add `[{ReadmeFile.Version}] = \"{Hash(ReadmeFile.Text)}\"` to Shipped.");

        Assert.True(Hash(ReadmeFile.Text) == Shipped[ReadmeFile.Version],
            "The readme text changed but its version did not. Raise the version on the first line of " +
            "src/Hatch.Cli/hatch-readme.md, then add a row for the new version here.");
    }

    [Fact]
    public void The_text_is_at_most_sixty_lines()
    {
        var lines = ReadmeFile.Text.TrimEnd('\n').Split('\n').Length;

        Assert.True(lines <= 60, $"The readme is {lines} lines. It is held to 60: an agent reads all of it.");
    }

    [Fact]
    public void The_text_names_no_host_and_no_machine()
    {
        Assert.DoesNotContain("://", ReadmeFile.Text);
        Assert.DoesNotContain(":\\", ReadmeFile.Text);
        Assert.DoesNotContain("\r", ReadmeFile.Text);
        Assert.DoesNotMatch(@"\b[A-Za-z]+-\d+\b", ReadmeFile.Text);
    }

    [Fact]
    public void This_repositorys_copy_is_the_text_in_the_binary()
    {
        var committed = File.ReadAllText(Committed()).ReplaceLineEndings("\n");

        Assert.True(committed == ReadmeFile.Text,
            $"{ReadmeFile.Path} differs from src/Hatch.Cli/hatch-readme.md, the text the binary embeds. " +
            "Copy the source over the committed one.");
    }

    private static string Committed()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, ".hatch", "README.md");
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException($"Could not find {ReadmeFile.Path} above {AppContext.BaseDirectory}.");
    }
}

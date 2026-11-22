using System.Diagnostics;
using System.Text;

namespace Hatch.Cli.Tests;

/// <summary>
/// <c>readme</c> and <c>readme --file</c>: the text printed, and written into a
/// real checkout by the one rule in <see cref="ReadmeFile"/>.
/// </summary>
public sealed class ReadmeCommandTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hatch-readme-").FullName;
    private readonly Transcript _say = new();

    public ReadmeCommandTests()
    {
        Git(_root, "init", "--quiet");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string Target => Path.Combine(_root, ".hatch", "README.md");

    private static void Git(string dir, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = dir, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        process.WaitForExit();
    }

    private int Command(params string[] args) => new ReadmeCommand(_say).Run(args, _root);

    private static string Held(int version) =>
        $"<!-- Written by Hatch. Version {version}. Change this text in Hatch, not here: a newer version replaces the file. -->\nolder text\n";

    private void Hold(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Target)!);
        File.WriteAllBytes(Target, new UTF8Encoding(false).GetBytes(text));
    }

    [Fact]
    public void Printing_it_says_the_text_and_needs_no_checkout()
    {
        var bare = Directory.CreateTempSubdirectory("hatch-nowhere-").FullName;
        try
        {
            Assert.Equal(0, new ReadmeCommand(_say).Run([], bare));
            Assert.Equal(ReadmeFile.Text.TrimEnd('\n'), string.Join("\n", _say.Said));
            Assert.Empty(_say.Complained);
        }
        finally { Directory.Delete(bare, recursive: true); }
    }

    [Fact]
    public void No_file_is_written_and_the_second_run_says_unchanged()
    {
        Assert.Equal(0, Command("--file"));
        Assert.Equal(["written .hatch/README.md"], _say.Said);
        Assert.Equal(ReadmeFile.Text, File.ReadAllText(Target));

        Assert.Equal(0, Command("--file"));
        Assert.Equal(["written .hatch/README.md", "unchanged .hatch/README.md"], _say.Said);
    }

    [Fact]
    public void The_file_is_written_with_unix_line_endings_and_no_byte_order_mark()
    {
        Assert.Equal(0, Command("--file"));

        var bytes = File.ReadAllBytes(Target);
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.NotEqual(0xEF, bytes[0]);
    }

    [Fact]
    public void A_file_Hatch_wrote_at_a_lower_version_is_replaced()
    {
        Hold(Held(0));

        Assert.Equal(0, Command("--file"));
        Assert.Equal(["written .hatch/README.md"], _say.Said);
        Assert.Equal(ReadmeFile.Text, File.ReadAllText(Target));
    }

    [Fact]
    public void A_file_Hatch_wrote_at_a_higher_version_is_left_byte_for_byte()
    {
        var newer = Held(ReadmeFile.Version + 1);
        Hold(newer);

        Assert.Equal(0, Command("--file"));
        Assert.Equal(["unchanged .hatch/README.md"], _say.Said);
        Assert.Equal(newer, File.ReadAllText(Target));
    }

    [Fact]
    public void A_file_at_the_same_version_is_left_even_where_its_body_differs()
    {
        var edited = Held(ReadmeFile.Version);
        Hold(edited);

        Assert.Equal(0, Command("--file"));
        Assert.Equal(["unchanged .hatch/README.md"], _say.Said);
        Assert.Equal(edited, File.ReadAllText(Target));
    }

    [Fact]
    public void A_foreign_file_is_left_and_the_one_sentence_goes_to_standard_error()
    {
        Hold("# Our own readme\n");

        Assert.Equal(1, Command("--file"));
        Assert.Empty(_say.Said);
        Assert.Equal([ReadmeFile.ForeignSentence], _say.Complained);
        Assert.Contains("not a file Hatch wrote", ReadmeFile.ForeignSentence);
        Assert.Contains("move or remove", ReadmeFile.ForeignSentence);
        Assert.Equal("# Our own readme\n", File.ReadAllText(Target));
    }

    [Fact]
    public void Writing_outside_a_checkout_is_refused()
    {
        var bare = Directory.CreateTempSubdirectory("hatch-nowhere-").FullName;
        try
        {
            Assert.Equal(1, new ReadmeCommand(_say).Run(["--file"], bare));
            Assert.Equal(["hatch: readme --file needs a checkout"], _say.Complained);
            Assert.Empty(_say.Said);
            Assert.False(Directory.Exists(Path.Combine(bare, ".hatch")));
        }
        finally { Directory.Delete(bare, recursive: true); }
    }

    [Fact]
    public void Writing_from_a_subdirectory_goes_to_the_root_of_the_checkout()
    {
        var sub = Path.Combine(_root, "src", "deep");
        Directory.CreateDirectory(sub);

        Assert.Equal(0, new ReadmeCommand(_say).Run(["--file"], sub));
        Assert.True(File.Exists(Target));
        Assert.False(File.Exists(Path.Combine(sub, ".hatch", "README.md")));
    }

    [Fact]
    public void Asking_for_help_prints_the_usage()
    {
        Assert.Equal(0, Command("-h"));
        Assert.Equal(ReadmeCommand.ReadmeUsage, _say.Said);
        Assert.False(File.Exists(Target));
    }

    [Theory]
    [InlineData("--files")]
    [InlineData("now")]
    public void An_argument_it_does_not_know_is_refused(string arg)
    {
        Assert.Equal(1, Command(arg));
        Assert.Contains($"hatch: readme does not take \"{arg}\"", _say.Complained);
        Assert.Empty(_say.Said);
    }
}

namespace Hatch.Cli.Tests;

/// <summary>
/// The record an older Hatch kept a checkout's chosen name in - read, and never
/// written, now that a runner's name is its key's.
/// </summary>
public sealed class RunnerNamesTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("hatch-runner-names-").FullName;

    private string RunnersPath => Path.Combine(_temp, "runners");

    [Fact]
    public void An_unwritten_record_reads_empty()
    {
        Assert.Empty(RunnerNames.Record.Read(RunnersPath));
    }

    [Fact]
    public void Key_with_no_profile_is_the_bare_path()
    {
        Assert.Equal("/Users/x/code/hatch", RunnerNames.Record.Key(null, "/Users/x/code/hatch"));
    }

    [Fact]
    public void Key_with_a_profile_differs_from_the_bare_path()
    {
        var key = RunnerNames.Record.Key("prod", "/Users/x/code/hatch");

        Assert.Contains("|", key);
        Assert.NotEqual(RunnerNames.Record.Key(null, "/Users/x/code/hatch"), key);
    }

    [Fact]
    public void A_record_file_in_its_old_form_still_reads()
    {
        File.WriteAllLines(RunnersPath, ["/Users/x/code/one=Jeff Winger", "# a note", "", "/Users/x/code/two=Liz Lemon"]);

        var entries = RunnerNames.Record.Read(RunnersPath);
        Assert.Equal("Jeff Winger", entries["/Users/x/code/one"]);
        Assert.Equal("Liz Lemon", entries["/Users/x/code/two"]);
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void A_profiled_entry_reads_under_its_own_key()
    {
        const string root = "/Users/x/code/hatch";
        File.WriteAllLines(RunnersPath, [$"{RunnerNames.Record.Key("a", root)}=Jeff", $"{root}=Troy"]);

        var entries = RunnerNames.Record.Read(RunnersPath);
        Assert.Equal("Jeff", entries[RunnerNames.Record.Key("a", root)]);
        Assert.Equal("Troy", entries[RunnerNames.Record.Key(null, root)]);
    }

    /// <summary>
    /// Settings.ReadFile warns loudly about a name that is not on its
    /// allowlist - the right thing for `config`, and exactly what would
    /// happen to every line of this file if it were ever read the same way.
    /// The record has no allowlist at all, because a checkout path is not one
    /// of the handful of settings names that file is for.
    /// </summary>
    [Fact]
    public void Reading_the_record_prints_no_allowlist_warning()
    {
        File.WriteAllLines(RunnersPath, ["/Users/x/code/hatch=Jeff Winger"]);

        var original = Console.Error;
        var captured = new StringWriter();
        Console.SetError(captured);
        try
        {
            RunnerNames.Record.Read(RunnersPath);
        }
        finally
        {
            Console.SetError(original);
        }

        Assert.Equal("", captured.ToString());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_temp, recursive: true);
        }
        catch (IOException)
        {
            // A temporary directory that will not go is not a failing test.
        }
    }
}

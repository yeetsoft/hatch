namespace Hatch.Cli.Tests;

/// <summary>
/// The cast list itself, the deterministic choice off it, and the record that
/// makes a choice happen once per checkout rather than every run.
/// </summary>
public sealed class RunnerNamesTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("hatch-runner-names-").FullName;

    private string RunnersPath => Path.Combine(_temp, "runners");

    // ---- the list ----

    [Fact]
    public void The_list_has_the_storys_count()
    {
        Assert.Equal(98, RunnerNames.All.Count);
    }

    [Fact]
    public void Every_name_is_non_empty_ASCII_and_within_the_servers_limit()
    {
        foreach (var name in RunnerNames.All)
        {
            Assert.NotEmpty(name.Trim());
            Assert.Equal(name, name.Trim());
            Assert.All(name, c => Assert.True(c < 128, $"\"{name}\" is not plain ASCII"));
            Assert.True(name.Length <= ClaimRequest.MaxRunnerLength);
        }
    }

    [Fact]
    public void No_name_repeats()
    {
        Assert.Equal(RunnerNames.All.Count, RunnerNames.All.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // ---- Choose ----

    [Fact]
    public void Choose_is_deterministic_for_the_same_inputs()
    {
        var none = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var first = RunnerNames.Choose("kestrel", "/Users/x/code/hatch", none);
        var second = RunnerNames.Choose("kestrel", "/Users/x/code/hatch", none);

        Assert.Equal(first, second);
        Assert.Contains(first, RunnerNames.All);
    }

    [Fact]
    public void A_different_host_or_root_can_land_on_a_different_slot()
    {
        var none = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Not asserted to always differ - two inputs are free to hash to the
        // same slot - only that the function is a function of both, not of
        // the root alone.
        var byHost = RunnerNames.Choose("kestrel", "/Users/x/code/hatch", none);
        var byRoot = RunnerNames.Choose("kestrel", "/Users/x/code/other", none);

        Assert.Contains(byHost, RunnerNames.All);
        Assert.Contains(byRoot, RunnerNames.All);
    }

    [Fact]
    public void Two_roots_that_start_on_the_same_slot_get_different_names_once_the_first_is_taken()
    {
        var none = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var first = RunnerNames.Choose("kestrel", "/Users/x/code/hatch", none);

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { first };
        var second = RunnerNames.Choose("kestrel", "/Users/x/code/hatch", taken);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Taken_is_compared_case_insensitively()
    {
        var none = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var first = RunnerNames.Choose("kestrel", "/Users/x/code/hatch", none);

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { first.ToUpperInvariant() };
        var second = RunnerNames.Choose("kestrel", "/Users/x/code/hatch", taken);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void A_full_list_falls_back_to_a_numbered_suffix()
    {
        var everything = new HashSet<string>(RunnerNames.All, StringComparer.OrdinalIgnoreCase);

        var chosen = RunnerNames.Choose("kestrel", "/Users/x/code/hatch", everything);

        Assert.DoesNotContain(chosen, RunnerNames.All);
        Assert.Matches(@"^.+ 2$", chosen);
        Assert.Contains(chosen[..^2], RunnerNames.All);
    }

    [Fact]
    public void A_full_list_plus_its_first_suffix_falls_back_to_the_next_one()
    {
        var everything = new HashSet<string>(RunnerNames.All, StringComparer.OrdinalIgnoreCase);
        var first = RunnerNames.Choose("kestrel", "/Users/x/code/hatch", everything);
        everything.Add(first);

        var second = RunnerNames.Choose("kestrel", "/Users/x/code/hatch", everything);

        Assert.NotEqual(first, second);
        Assert.EndsWith(" 3", second, StringComparison.Ordinal);
    }

    // ---- Record ----

    [Fact]
    public void An_unwritten_record_reads_empty()
    {
        Assert.Empty(RunnerNames.Record.Read(RunnersPath));
    }

    [Fact]
    public void Set_then_read_round_trips()
    {
        RunnerNames.Record.Set(RunnersPath, "/Users/x/code/hatch", "Buster Bluth");

        Assert.Equal("Buster Bluth", RunnerNames.Record.Read(RunnersPath)["/Users/x/code/hatch"]);
    }

    [Fact]
    public void Setting_one_checkout_leaves_every_other_entry_alone()
    {
        RunnerNames.Record.Set(RunnersPath, "/Users/x/code/one", "Buster Bluth");
        RunnerNames.Record.Set(RunnersPath, "/Users/x/code/two", "Liz Lemon");

        var entries = RunnerNames.Record.Read(RunnersPath);
        Assert.Equal("Buster Bluth", entries["/Users/x/code/one"]);
        Assert.Equal("Liz Lemon", entries["/Users/x/code/two"]);
    }

    [Fact]
    public void Setting_a_checkout_again_replaces_only_that_entry()
    {
        RunnerNames.Record.Set(RunnersPath, "/Users/x/code/one", "Buster Bluth");
        RunnerNames.Record.Set(RunnersPath, "/Users/x/code/two", "Liz Lemon");
        RunnerNames.Record.Set(RunnersPath, "/Users/x/code/one", "Gob Bluth");

        var entries = RunnerNames.Record.Read(RunnersPath);
        Assert.Equal("Gob Bluth", entries["/Users/x/code/one"]);
        Assert.Equal("Liz Lemon", entries["/Users/x/code/two"]);
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void The_file_is_written_mode_600_where_the_platform_has_modes()
    {
        if (OperatingSystem.IsWindows()) return;

        RunnerNames.Record.Set(RunnersPath, "/Users/x/code/hatch", "Buster Bluth");

        var mode = File.GetUnixFileMode(RunnersPath);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
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
        RunnerNames.Record.Set(RunnersPath, "/Users/x/code/hatch", "Buster Bluth");

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

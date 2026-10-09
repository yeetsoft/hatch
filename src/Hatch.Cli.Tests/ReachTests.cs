namespace Hatch.Cli.Tests;

/// <summary>
/// The word <c>hatch</c>, made to resolve inside a session this program
/// spawned - because the prompt that session receives tells it to type one.
/// </summary>
public sealed class ReachTests
{
    private static string Path(params string[] parts) => System.IO.Path.Combine(parts);

    private static Dictionary<string, string?> Env(string? path) =>
        path is null
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : new Dictionary<string, string?>(StringComparer.Ordinal) { ["PATH"] = path };

    [Fact]
    public void A_binary_called_hatch_offers_the_directory_it_sits_in()
    {
        Assert.Equal(Path("opt", "hatch"), Reach.OwnDirectory(Path("opt", "hatch", "hatch")));
    }

    [Fact]
    public void The_windows_spelling_is_the_same_binary()
    {
        Assert.Equal(Path("opt", "hatch"), Reach.OwnDirectory(Path("opt", "hatch", "hatch.exe")));
    }

    /// <summary>
    /// Under <c>dotnet run</c> the executable is <c>dotnet</c>, and there is no
    /// <c>hatch</c> to point at. Inventing a path to a binary that may not exist
    /// would be worse than leaving <c>PATH</c> alone.
    /// </summary>
    [Theory]
    [InlineData("dotnet")]
    [InlineData("testhost")]
    [InlineData("hatch-runner")]
    public void Anything_that_is_not_hatch_offers_nothing(string name)
    {
        Assert.Null(Reach.OwnDirectory(Path("usr", "bin", name)));
    }

    [Fact]
    public void No_process_path_at_all_offers_nothing()
    {
        Assert.Null(Reach.OwnDirectory(null));
        Assert.Null(Reach.OwnDirectory(""));
    }

    [Fact]
    public void A_file_with_no_execute_bit_is_not_found_on_unix()
    {
        if (OperatingSystem.IsWindows()) return;

        var dir = Path(System.IO.Path.GetTempPath(), $"hatch-find-{Guid.NewGuid():N}");
        var later = Path(dir, "later");
        Directory.CreateDirectory(Path(dir, "first"));
        Directory.CreateDirectory(later);
        try
        {
            var inert = Path(dir, "first", "tool");
            var runnable = Path(later, "tool");
            File.WriteAllText(inert, "");
            File.WriteAllText(runnable, "");
            File.SetUnixFileMode(inert, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.SetUnixFileMode(runnable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            Assert.Equal(runnable, Reach.Find("tool", string.Join(System.IO.Path.PathSeparator, Path(dir, "first"), later)));
            Assert.Null(Reach.Find("tool", Path(dir, "first")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void A_relative_path_entry_is_found_as_an_absolute_path()
    {
        var dir = Path(System.IO.Path.GetTempPath(), $"hatch-find-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var original = Directory.GetCurrentDirectory();
        try
        {
            var file = Path(dir, OperatingSystem.IsWindows() ? "tool.exe" : "tool");
            File.WriteAllText(file, "");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            Directory.SetCurrentDirectory(dir);
            var found = Reach.Find("tool", ".");

            Assert.NotNull(found);
            Assert.True(System.IO.Path.IsPathRooted(found));
            Assert.Equal(System.IO.Path.GetFileName(file), System.IO.Path.GetFileName(found));
        }
        finally
        {
            Directory.SetCurrentDirectory(original);
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// The front rather than the back, so the <c>hatch</c> that answers is the
    /// one that composed the prompt.
    /// </summary>
    [Fact]
    public void The_directory_goes_on_the_front_and_what_was_there_stays_behind_it()
    {
        var environment = Env($"/usr/bin{System.IO.Path.PathSeparator}/bin");

        Reach.OnPath(environment, "/opt/hatch");

        Assert.Equal($"/opt/hatch{System.IO.Path.PathSeparator}/usr/bin{System.IO.Path.PathSeparator}/bin",
            environment["PATH"]);
    }

    [Fact]
    public void An_empty_path_becomes_the_directory_alone()
    {
        var environment = Env("");
        Reach.OnPath(environment, "/opt/hatch");
        Assert.Equal("/opt/hatch", environment["PATH"]);
    }

    [Fact]
    public void A_missing_path_is_set_rather_than_appended_to()
    {
        var environment = Env(null);
        Reach.OnPath(environment, "/opt/hatch");
        Assert.Equal("/opt/hatch", environment["PATH"]);
    }

    /// <summary>A restarted loop should not grow its own PATH one entry a night.</summary>
    [Fact]
    public void A_directory_already_at_the_front_is_not_added_twice()
    {
        var environment = Env($"/opt/hatch{System.IO.Path.PathSeparator}/usr/bin");

        Reach.OnPath(environment, "/opt/hatch");
        Reach.OnPath(environment, "/opt/hatch");

        Assert.Equal($"/opt/hatch{System.IO.Path.PathSeparator}/usr/bin", environment["PATH"]);
    }

    [Fact]
    public void Nothing_to_point_at_leaves_the_path_exactly_as_it_was()
    {
        var environment = Env("/usr/bin");

        Reach.OnPath(environment, null);
        Reach.OnPath(environment, "");

        Assert.Equal("/usr/bin", environment["PATH"]);
    }
}

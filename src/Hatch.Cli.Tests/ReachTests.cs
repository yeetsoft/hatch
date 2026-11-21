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

    /// <summary>What a session's environment holds once <see cref="Reach.Unsigned"/> has run over a clean one.</summary>
    internal static void AssertUnsigned(IDictionary<string, string?> environment)
    {
        var count = int.Parse(environment["GIT_CONFIG_COUNT"]!, System.Globalization.CultureInfo.InvariantCulture);
        var pairs = Enumerable.Range(0, count).ToDictionary(n => environment[$"GIT_CONFIG_KEY_{n}"]!, n => environment[$"GIT_CONFIG_VALUE_{n}"]);

        Assert.Equal("false", pairs["commit.gpgsign"]);
        Assert.Equal("false", pairs["tag.gpgsign"]);
    }

    [Fact]
    public void Unsigned_sets_both_signing_settings_to_false_through_the_environment()
    {
        var environment = Env(null);

        Reach.Unsigned(environment);

        Assert.Equal("2", environment["GIT_CONFIG_COUNT"]);
        Assert.Equal("commit.gpgsign", environment["GIT_CONFIG_KEY_0"]);
        Assert.Equal("false", environment["GIT_CONFIG_VALUE_0"]);
        Assert.Equal("tag.gpgsign", environment["GIT_CONFIG_KEY_1"]);
        Assert.Equal("false", environment["GIT_CONFIG_VALUE_1"]);
    }

    [Fact]
    public void Unsigned_goes_after_the_pairs_the_environment_already_holds()
    {
        var environment = Env(null);
        environment["GIT_CONFIG_COUNT"] = "1";
        environment["GIT_CONFIG_KEY_0"] = "core.editor";
        environment["GIT_CONFIG_VALUE_0"] = "true";

        Reach.Unsigned(environment);

        Assert.Equal("3", environment["GIT_CONFIG_COUNT"]);
        Assert.Equal("core.editor", environment["GIT_CONFIG_KEY_0"]);
        Assert.Equal("true", environment["GIT_CONFIG_VALUE_0"]);
        Assert.Equal("commit.gpgsign", environment["GIT_CONFIG_KEY_1"]);
        Assert.Equal("tag.gpgsign", environment["GIT_CONFIG_KEY_2"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("many")]
    [InlineData("-3")]
    [InlineData("1.5")]
    public void Unsigned_reads_a_count_that_is_not_a_whole_number_as_none(string held)
    {
        var environment = Env(null);
        environment["GIT_CONFIG_COUNT"] = held;

        Reach.Unsigned(environment);

        Assert.Equal("2", environment["GIT_CONFIG_COUNT"]);
        Assert.Equal("commit.gpgsign", environment["GIT_CONFIG_KEY_0"]);
    }

    /// <summary>
    /// A build kept under <c>versions/&lt;sha&gt;/hatch</c> is the binary running, so the
    /// folder a session finds first on <c>PATH</c> and the path its hooks run are
    /// that build's own, and a change of build needs nothing from either.
    /// </summary>
    [Fact]
    public void A_kept_build_puts_its_own_folder_first_and_its_own_path_in_the_hooks()
    {
        var kept = Path("root", "versions", new string('a', 40), "hatch");
        var environment = Env("/usr/bin");

        Reach.OnPath(environment, Reach.OwnDirectory(kept));

        Assert.Equal(Path("root", "versions", new string('a', 40)) + System.IO.Path.PathSeparator + "/usr/bin", environment["PATH"]);
        Assert.Equal(kept, SessionHooks.Binary(kept));
    }

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

    [Fact]
    public void ToHatch_drops_an_inherited_HATCH_RUNNER_since_the_key_names_the_runner()
    {
        var environment = Env("/usr/bin");
        environment["HATCH_RUNNER"] = "inherited";

        Reach.ToHatch(environment, new Settings { Base = "https://example.hatch", Key = "hatch_ak_test" });

        Assert.DoesNotContain("HATCH_RUNNER", environment.Keys);
    }

    [Fact]
    public void ToHatch_sets_HATCH_BASE_and_HATCH_KEY()
    {
        var environment = Env("/usr/bin");

        Reach.ToHatch(environment, new Settings { Base = "https://example.hatch", Key = "hatch_ak_test" });

        Assert.Equal("https://example.hatch", environment["HATCH_BASE"]);
        Assert.Equal("hatch_ak_test", environment["HATCH_KEY"]);
    }

    [Fact]
    public void ToHatch_with_an_empty_key_exports_HATCH_BASE_alone()
    {
        var environment = Env("/usr/bin");

        Reach.ToHatch(environment, new Settings { Base = "https://example.hatch", Key = "" });

        Assert.Equal("https://example.hatch", environment["HATCH_BASE"]);
        Assert.False(environment.ContainsKey("HATCH_KEY"));
    }

    [Fact]
    public void ToHatch_with_an_empty_key_removes_a_HATCH_KEY_already_exported()
    {
        var environment = Env("/usr/bin");
        environment["HATCH_KEY"] = "hatch_ak_ignored_by_the_parent";

        Reach.ToHatch(environment, new Settings { Base = "https://example.hatch", Key = "" });

        Assert.Equal("https://example.hatch", environment["HATCH_BASE"]);
        Assert.False(environment.ContainsKey("HATCH_KEY"));
    }

    [Fact]
    public void ToHatch_with_no_settings_leaves_the_environment_alone()
    {
        var environment = Env("/usr/bin");
        environment["HATCH_BASE"] = "inherited";
        var before = new Dictionary<string, string?>(environment);

        Reach.ToHatch(environment, null);

        Assert.Equal(before, environment);
    }
}

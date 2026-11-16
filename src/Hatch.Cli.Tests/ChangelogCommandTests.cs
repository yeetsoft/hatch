using System.Net;
using System.Text;
using System.Text.Json;

namespace Hatch.Cli.Tests;

/// <summary>
/// <c>changelog</c> - what shipped, printed as markdown, and the flags that
/// resize, move and narrow the window.
/// </summary>
public sealed class ChangelogCommandTests
{
    private static ChangelogEntryDto Entry(
        string key, string title, string type = "epic", string project = "AER",
        string? note = null, int descendantCount = 0) =>
        new(key, title, type, project, note, DateTimeOffset.Parse("2026-10-13T12:00:00Z"), descendantCount);

    private static ChangelogDayDto Day(
        string date, IReadOnlyList<ChangelogEntryDto>? features = null, IReadOnlyList<ChangelogEntryDto>? fixes = null) =>
        new(date, features ?? [], fixes ?? []);

    private static ChangelogDto AChangelog(params ChangelogDayDto[] days) => new(days, false, null, []);

    private static ProjectDto AProject(int id, string key) =>
        new(id, key, key, 0, DateTimeOffset.UtcNow, null, null, [], null, [], false, false);

    [Fact]
    public async Task A_day_prints_its_heading_its_features_and_an_also_list_of_its_fixes()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/changelog", AChangelog(
            Day("2026-10-13",
                features: [Entry("AER-12", "Auth: the first Admin", note: "Admins can now invite teammates by email.", descendantCount: 4)],
                fixes: [Entry("AER-9", "Fixed a crash on close", type: "bug")])));

        Assert.Equal(0, await new ChangelogCommand(h.Cli).RunAsync([], default));

        Assert.Equal(
            """
            ## Tuesday, October 13, 2026

            - **AER-12** Auth: the first Admin — Admins can now invite teammates by email. (4 beneath)

            Also:
            - Fixed a crash on close
            """.ReplaceLineEndings("\n"),
            h.Said);
    }

    [Fact]
    public async Task A_feature_with_no_descendants_says_none_beneath()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/changelog", AChangelog(
            Day("2026-10-13", features: [Entry("AER-12", "A title", note: "A note")])));

        await new ChangelogCommand(h.Cli).RunAsync([], default);
        Assert.Contains("(none beneath)", h.Said);
    }

    [Fact]
    public async Task A_fixs_blank_note_falls_back_to_its_title_rather_than_an_empty_bullet()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/changelog", AChangelog(
            Day("2026-10-13", fixes: [Entry("AER-9", "Fixed a crash on close", type: "bug", note: "  ")])));

        await new ChangelogCommand(h.Cli).RunAsync([], default);
        Assert.Contains("- Fixed a crash on close", h.Said);
    }

    [Fact]
    public async Task A_fixs_note_is_cut_to_its_first_line()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/changelog", AChangelog(
            Day("2026-10-13", fixes: [Entry("AER-9", "A title", type: "bug", note: "First line.\nSecond line.")])));

        await new ChangelogCommand(h.Cli).RunAsync([], default);
        Assert.Contains("- First line.", h.Said);
        Assert.DoesNotContain("Second line.", h.Said);
    }

    [Fact]
    public async Task A_feature_with_a_blank_note_shows_no_dash_rather_than_repeating_its_title()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/changelog", AChangelog(
            Day("2026-10-13", features: [Entry("AER-12", "A title")])));

        await new ChangelogCommand(h.Cli).RunAsync([], default);
        Assert.Contains("- **AER-12** A title (none beneath)", h.Said);
    }

    [Fact]
    public async Task Two_days_are_separated_by_a_blank_line()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/changelog", AChangelog(
            Day("2026-10-13", features: [Entry("AER-12", "Newer")]),
            Day("2026-10-12", features: [Entry("AER-11", "Older")])));

        await new ChangelogCommand(h.Cli).RunAsync([], default);

        Assert.Equal(
            """
            ## Tuesday, October 13, 2026

            - **AER-12** Newer (none beneath)

            ## Monday, October 12, 2026

            - **AER-11** Older (none beneath)
            """.ReplaceLineEndings("\n"),
            h.Said);
    }

    [Fact]
    public async Task An_empty_window_prints_one_sentence_rather_than_a_blank_line()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/changelog", AChangelog());

        Assert.Equal(0, await new ChangelogCommand(h.Cli).RunAsync([], default));
        Assert.Equal("Nothing has shipped yet.", h.Said);
    }

    [Fact]
    public async Task With_nothing_set_the_default_window_is_seven_days_and_nothing_else_is_sent()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/changelog", AChangelog());

        await new ChangelogCommand(h.Cli).RunAsync([], default);

        Assert.Equal("?days=7", h.Wire.To("GET", "/api/changelog").Single().Query);
    }

    [Fact]
    public async Task Days_resizes_the_window()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/changelog", AChangelog());

        await new ChangelogCommand(h.Cli).RunAsync(["--days", "30"], default);

        Assert.Equal("?days=30", h.Wire.To("GET", "/api/changelog").Single().Query);
    }

    [Fact]
    public async Task Before_moves_the_window_back()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/changelog", AChangelog());

        await new ChangelogCommand(h.Cli).RunAsync(["--before", "2026-09-01"], default);

        Assert.Equal("?days=7&before=2026-09-01", h.Wire.To("GET", "/api/changelog").Single().Query);
    }

    [Fact]
    public async Task Project_resolves_the_key_to_an_id_and_sends_that()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/projects", new List<ProjectDto> { AProject(3, "AER") });
        h.Wire.Json("GET", "/api/changelog", AChangelog());

        Assert.Equal(0, await new ChangelogCommand(h.Cli).RunAsync(["--project", "aer"], default));

        Assert.Equal("?days=7&projectId=3", h.Wire.To("GET", "/api/changelog").Single().Query);
    }

    [Fact]
    public async Task A_project_key_nobody_has_is_refused_by_name_and_the_changelog_is_never_asked()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/projects", new List<ProjectDto> { AProject(3, "AER") });

        Assert.Equal(1, await new ChangelogCommand(h.Cli).RunAsync(["--project", "ZZZ"], default));
        Assert.Contains("there is no project ZZZ", h.Complained);
        Assert.Empty(h.Wire.To("GET", "/api/changelog"));
    }

    [Fact]
    public async Task Json_prints_the_endpoints_own_answer_and_nothing_else()
    {
        using var h = new CliHarness();
        var changelog = AChangelog(Day("2026-10-13", features: [Entry("AER-12", "Auth: the first Admin")]));
        h.Wire.Json("GET", "/api/changelog", changelog);

        Assert.Equal(0, await new ChangelogCommand(h.Cli).RunAsync(["--json"], default));

        Assert.Equal(JsonSerializer.Serialize(changelog, Fixtures.Json), h.Said);
    }

    [Fact]
    public async Task Dash_h_prints_its_own_usage_and_calls_nothing()
    {
        using var h = new CliHarness();

        Assert.Equal(0, await new ChangelogCommand(h.Cli).RunAsync(["-h"], default));
        Assert.StartsWith("usage: hatch changelog", h.Said);
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task An_unknown_flag_is_refused_and_writes_nothing()
    {
        using var h = new CliHarness();

        Assert.Equal(1, await new ChangelogCommand(h.Cli).RunAsync(["--nonsense"], default));
        Assert.Contains("changelog does not take \"--nonsense\"", h.Complained);
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task Days_takes_a_whole_number_of_at_least_one()
    {
        using var h = new CliHarness();

        Assert.Equal(1, await new ChangelogCommand(h.Cli).RunAsync(["--days", "0"], default));
        Assert.Contains("--days takes a whole number of at least one", h.Complained);
        Assert.Empty(h.Wire.Calls);
    }
}

/// <summary>
/// <c>changelog --file</c> - the repository's changelog file, written as the
/// server serves it, against a real checkout.
/// </summary>
public sealed class ChangelogFileTests : RepoFixture
{
    private const string Yaml = "days:\r\n  - date: 2026-10-13\r\n    note: café — shipped\r\n";

    private string Target => Path.Combine(_work, ".hatch", "changelog.yml");

    [Fact]
    public async Task The_first_run_writes_the_file_and_the_second_says_unchanged()
    {
        using var h = new CliHarness();
        h.Wire.Reply("GET", "/api/changelog/file", HttpStatusCode.OK, Yaml);

        Assert.Equal(0, await new ChangelogCommand(h.Cli).RunAsync(["--file"], _work, default));
        Assert.Equal(Yaml, File.ReadAllText(Target));
        Assert.Equal("written .hatch/changelog.yml", h.Said);
        Assert.Equal("?remote=" + Uri.EscapeDataString(_origin), h.Wire.To("GET", "/api/changelog/file").Single().Query);

        var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Target, stamp);

        using var again = new CliHarness();
        again.Wire.Reply("GET", "/api/changelog/file", HttpStatusCode.OK, Yaml);
        Assert.Equal(0, await new ChangelogCommand(again.Cli).RunAsync(["--file"], _work, default));
        Assert.Equal("unchanged .hatch/changelog.yml", again.Said);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(Target));
    }

    [Fact]
    public async Task A_changed_answer_is_written_over_the_old_file()
    {
        using var h = new CliHarness();
        h.Wire.Reply("GET", "/api/changelog/file", HttpStatusCode.OK, Yaml);
        Directory.CreateDirectory(Path.GetDirectoryName(Target)!);
        File.WriteAllText(Target, "old\n");

        Assert.Equal(0, await new ChangelogCommand(h.Cli).RunAsync(["--file"], _work, default));
        Assert.Equal("written .hatch/changelog.yml", h.Said);
        Assert.Equal(Yaml, File.ReadAllText(Target));
    }

    [Fact]
    public async Task From_a_subdirectory_the_file_lands_under_the_root()
    {
        using var h = new CliHarness();
        h.Wire.Reply("GET", "/api/changelog/file", HttpStatusCode.OK, Yaml);
        var sub = Path.Combine(_work, "deep", "down");
        Directory.CreateDirectory(sub);

        Assert.Equal(0, await new ChangelogCommand(h.Cli).RunAsync(["--file"], sub, default));
        Assert.True(File.Exists(Target));
        Assert.False(File.Exists(Path.Combine(sub, ".hatch", "changelog.yml")));
        Assert.Equal($"written {Target}", h.Said);
    }

    [Fact]
    public async Task Repo_names_the_remote_asked_about_in_place_of_the_origin()
    {
        using var h = new CliHarness();
        h.Wire.Reply("GET", "/api/changelog/file", HttpStatusCode.OK, Yaml);

        Assert.Equal(0, await new ChangelogCommand(h.Cli).RunAsync(["--file", "--repo", "git@example.test:acme/widgets.git"], _work, default));
        Assert.Equal("?remote=" + Uri.EscapeDataString("git@example.test:acme/widgets.git"), h.Wire.To("GET", "/api/changelog/file").Single().Query);
    }

    [Fact]
    public async Task Repo_needs_no_checkout()
    {
        using var h = new CliHarness();
        h.Wire.Reply("GET", "/api/changelog/file", HttpStatusCode.OK, Yaml);
        var bare = Directory.CreateTempSubdirectory("hatch-nowhere-").FullName;

        try
        {
            Assert.Equal(0, await new ChangelogCommand(h.Cli).RunAsync(["--file", "--repo", "acme/widgets"], bare, default));
            Assert.Equal(Yaml, File.ReadAllText(Path.Combine(bare, ".hatch", "changelog.yml")));
        }
        finally
        {
            Directory.Delete(bare, true);
        }
    }

    [Fact]
    public async Task A_remote_nobody_elects_prints_the_servers_sentence_and_writes_nothing()
    {
        using var h = new CliHarness();
        h.Wire.Reply("GET", "/api/changelog/file", HttpStatusCode.NotFound, "No project elects a changelog for that repository.");

        Assert.Equal(1, await new ChangelogCommand(h.Cli).RunAsync(["--file"], _work, default));
        Assert.Contains("No project elects a changelog for that repository.", h.Complained);
        Assert.DoesNotContain("no such issue or route", h.Complained);
        Assert.False(File.Exists(Target));
    }

    [Fact]
    public async Task Outside_a_checkout_without_repo_it_refuses_and_calls_nothing()
    {
        using var h = new CliHarness();
        var bare = Directory.CreateTempSubdirectory("hatch-nowhere-").FullName;

        try
        {
            Assert.Equal(1, await new ChangelogCommand(h.Cli).RunAsync(["--file"], bare, default));
            Assert.Contains("needs a checkout", h.Complained);
            Assert.Empty(h.Wire.Calls);
            Assert.Empty(Directory.GetFileSystemEntries(bare));
        }
        finally
        {
            Directory.Delete(bare, true);
        }
    }

    [Theory]
    [InlineData("--days", "3")]
    [InlineData("--before", "x")]
    [InlineData("--project", "AER")]
    [InlineData("--json")]
    public async Task File_does_not_mix_with_the_feeds_flags(params string[] flag)
    {
        using var h = new CliHarness();

        Assert.Equal(1, await new ChangelogCommand(h.Cli).RunAsync(["--file", .. flag], _work, default));
        Assert.Contains("--file does not mix", h.Complained);
        Assert.Contains("usage: hatch changelog", h.Complained);
        Assert.Empty(h.Wire.Calls);
        Assert.False(File.Exists(Target));
    }

    [Fact]
    public async Task Repo_without_file_is_refused()
    {
        using var h = new CliHarness();

        Assert.Equal(1, await new ChangelogCommand(h.Cli).RunAsync(["--repo", "acme/widgets"], _work, default));
        Assert.Contains("--repo goes with --file", h.Complained);
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task A_path_is_written_to_and_its_missing_directories_made()
    {
        using var h = new CliHarness();
        h.Wire.Reply("GET", "/api/changelog/file", HttpStatusCode.OK, Yaml);
        var target = Path.Combine(_work, "out", "nested", "log.yml");

        Assert.Equal(0, await new ChangelogCommand(h.Cli).RunAsync(["--file", target], _work, default));
        Assert.Equal(Yaml, File.ReadAllText(target));
        Assert.Equal("written out/nested/log.yml", h.Said);
        Assert.False(File.Exists(Target));
    }

    [Fact]
    public async Task The_bytes_are_the_servers_with_no_bom_and_no_newline_translation()
    {
        using var h = new CliHarness();
        h.Wire.Reply("GET", "/api/changelog/file", HttpStatusCode.OK, Yaml);

        await new ChangelogCommand(h.Cli).RunAsync(["--file"], _work, default);

        Assert.Equal(new UTF8Encoding(false).GetBytes(Yaml), File.ReadAllBytes(Target));
    }
}

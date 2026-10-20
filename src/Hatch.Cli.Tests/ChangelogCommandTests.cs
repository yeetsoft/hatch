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
        h.Wire.Json("GET", "/api/hatch/changelog", AChangelog(
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
        h.Wire.Json("GET", "/api/hatch/changelog", AChangelog(
            Day("2026-10-13", features: [Entry("AER-12", "A title", note: "A note")])));

        await new ChangelogCommand(h.Cli).RunAsync([], default);
        Assert.Contains("(none beneath)", h.Said);
    }

    [Fact]
    public async Task A_fixs_blank_note_falls_back_to_its_title_rather_than_an_empty_bullet()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/hatch/changelog", AChangelog(
            Day("2026-10-13", fixes: [Entry("AER-9", "Fixed a crash on close", type: "bug", note: "  ")])));

        await new ChangelogCommand(h.Cli).RunAsync([], default);
        Assert.Contains("- Fixed a crash on close", h.Said);
    }

    [Fact]
    public async Task A_fixs_note_is_cut_to_its_first_line()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/hatch/changelog", AChangelog(
            Day("2026-10-13", fixes: [Entry("AER-9", "A title", type: "bug", note: "First line.\nSecond line.")])));

        await new ChangelogCommand(h.Cli).RunAsync([], default);
        Assert.Contains("- First line.", h.Said);
        Assert.DoesNotContain("Second line.", h.Said);
    }

    [Fact]
    public async Task A_feature_with_a_blank_note_shows_no_dash_rather_than_repeating_its_title()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/hatch/changelog", AChangelog(
            Day("2026-10-13", features: [Entry("AER-12", "A title")])));

        await new ChangelogCommand(h.Cli).RunAsync([], default);
        Assert.Contains("- **AER-12** A title (none beneath)", h.Said);
    }

    [Fact]
    public async Task Two_days_are_separated_by_a_blank_line()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/hatch/changelog", AChangelog(
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
        h.Wire.Json("GET", "/api/hatch/changelog", AChangelog());

        Assert.Equal(0, await new ChangelogCommand(h.Cli).RunAsync([], default));
        Assert.Equal("Nothing has shipped yet.", h.Said);
    }

    [Fact]
    public async Task With_nothing_set_the_default_window_is_seven_days_and_nothing_else_is_sent()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/hatch/changelog", AChangelog());

        await new ChangelogCommand(h.Cli).RunAsync([], default);

        Assert.Equal("?days=7", h.Wire.To("GET", "/api/hatch/changelog").Single().Query);
    }

    [Fact]
    public async Task Days_resizes_the_window()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/hatch/changelog", AChangelog());

        await new ChangelogCommand(h.Cli).RunAsync(["--days", "30"], default);

        Assert.Equal("?days=30", h.Wire.To("GET", "/api/hatch/changelog").Single().Query);
    }

    [Fact]
    public async Task Before_moves_the_window_back()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/hatch/changelog", AChangelog());

        await new ChangelogCommand(h.Cli).RunAsync(["--before", "2026-09-01"], default);

        Assert.Equal("?days=7&before=2026-09-01", h.Wire.To("GET", "/api/hatch/changelog").Single().Query);
    }

    [Fact]
    public async Task Project_resolves_the_key_to_an_id_and_sends_that()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/hatch/projects", new List<ProjectDto> { AProject(3, "AER") });
        h.Wire.Json("GET", "/api/hatch/changelog", AChangelog());

        Assert.Equal(0, await new ChangelogCommand(h.Cli).RunAsync(["--project", "aer"], default));

        Assert.Equal("?days=7&projectId=3", h.Wire.To("GET", "/api/hatch/changelog").Single().Query);
    }

    [Fact]
    public async Task A_project_key_nobody_has_is_refused_by_name_and_the_changelog_is_never_asked()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/hatch/projects", new List<ProjectDto> { AProject(3, "AER") });

        Assert.Equal(1, await new ChangelogCommand(h.Cli).RunAsync(["--project", "ZZZ"], default));
        Assert.Contains("there is no project ZZZ", h.Complained);
        Assert.Empty(h.Wire.To("GET", "/api/hatch/changelog"));
    }

    [Fact]
    public async Task Json_prints_the_endpoints_own_answer_and_nothing_else()
    {
        using var h = new CliHarness();
        var changelog = AChangelog(Day("2026-10-13", features: [Entry("AER-12", "Auth: the first Admin")]));
        h.Wire.Json("GET", "/api/hatch/changelog", changelog);

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

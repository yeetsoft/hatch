using System.Text.Json;

namespace Hatch.Cli.Tests;

/// <summary>
/// <c>activity</c> - one actor's rundown printed as markdown, and the flags
/// that name the actor and move, resize and narrow the window.
/// </summary>
public sealed class ActivityCommandTests
{
    private const string Route = "/api/hatch/reports/activity";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-11T12:00:00Z");

    private static ActivityKindCountDto Kind(string kind, int count) => new(kind, count);

    private static ActivityIssueDto Touched(
        string key, string title, string first, string last, params ActivityKindCountDto[] kinds) =>
        new(
            new IssueCardDto(key, key.Split('-')[0], "story", title, 1, 0, null, null, null),
            DateTimeOffset.Parse(first), DateTimeOffset.Parse(last), kinds.Sum(k => k.Count), kinds);

    private static ActivityReportDto AReport(
        int total, ActivityKindCountDto[] kinds, ActivityIssueDto[] issues,
        string actor = "riker", double minutes = 90, int sessions = 2) =>
        new(
            actor, DateTimeOffset.Parse("2026-10-28T12:00:00Z"), Now, total, kinds, issues, [], false, "day", 0, [], [],
            new ActivityEstimateDto(sessions, minutes, 45, 15, 30));

    private static ActivityReportDto ABusyReport() =>
        AReport(
            41,
            [Kind("commented", 12), Kind("status_changed", 9), Kind("created", 6), Kind("retitled", 8), Kind("asked", 6)],
            [
                Touched("AER-12", "Auth: the first Admin", "2026-11-04T14:05:00Z", "2026-11-05T09:30:00Z",
                    Kind("commented", 1), Kind("status_changed", 3)),
            ]);

    private static ProjectDto AProject(int id, string key) =>
        new(id, key, key, 0, DateTimeOffset.UtcNow, null, null, [], null, [], false, false);

    private static CliHarness AHarness() => new(clock: new FrozenClock(Now));

    private static string Query(CliHarness h) => h.Wire.To("GET", Route).Single().Query;

    [Fact]
    public void More_than_three_kinds_end_the_headline_with_the_events_of_the_rest()
    {
        var lines = ActivityCommand.Render(ABusyReport(), 0);

        Assert.Equal("**41 events: 12 comments, 9 moves, 6 created, and 14 others**", lines[2]);
    }

    [Fact]
    public void A_single_leftover_event_is_one_other()
    {
        var report = AReport(1, [Kind("commented", 1), Kind("created", 1), Kind("asked", 1), Kind("answered", 1)], []);

        Assert.Equal("1 event: 1 comment, 1 created, 1 question, and 1 other", ActivityCommand.HeadlineWords(report));
    }

    [Fact]
    public void A_kind_the_words_do_not_know_is_spelled_out_from_its_own_name()
    {
        Assert.Equal("2 something new", ActivityCommand.KindWords("something_new", 2));
        Assert.Equal("1 dependency added", ActivityCommand.KindWords("dependency_added", 1));
        Assert.Equal("3 dependencies added", ActivityCommand.KindWords("dependency_added", 3));
    }

    [Fact]
    public void An_issue_is_a_heading_a_bullet_per_kind_in_order_and_when_it_was_first_and_last_touched()
    {
        var lines = ActivityCommand.Render(ABusyReport(), 0);

        Assert.Equal(
            """
            # riker, 28 Oct 2026, 12:00 to 11 Nov 2026, 12:00

            **41 events: 12 comments, 9 moves, 6 created, and 14 others**

            Estimated 1.5h across 2 sessions (45m idle gap ends one, 15m lead on each).

            ## AER-12 Auth: the first Admin
            - 1 comment
            - 3 moves
            - first 4 Nov 2026, 14:05, last 5 Nov 2026, 09:30
            """.ReplaceLineEndings("\n"),
            string.Join("\n", lines));
    }

    [Fact]
    public void One_session_is_not_pluralised()
    {
        var report = AReport(1, [Kind("commented", 1)], [], minutes: 15, sessions: 1);

        Assert.Contains("Estimated 0.3h across 1 session (", string.Join("\n", ActivityCommand.Render(report, 0)));
    }

    [Fact]
    public void Touches_are_shifted_onto_the_readers_own_clock()
    {
        var east = ActivityCommand.Render(ABusyReport(), 120);
        Assert.Contains("- first 4 Nov 2026, 16:05, last 5 Nov 2026, 11:30", east);

        var west = ActivityCommand.Render(ABusyReport(), -330);
        Assert.Contains("- first 4 Nov 2026, 08:35, last 5 Nov 2026, 04:00", west);

        // Shifting across midnight moves the date too.
        Assert.Equal("5 Nov 2026, 01:05", ActivityCommand.Touch(DateTimeOffset.Parse("2026-11-04T23:05:00Z"), 120));
    }

    [Fact]
    public void An_empty_range_prints_only_the_one_sentence()
    {
        var lines = ActivityCommand.Render(AReport(0, [], []), 0);

        Assert.Equal(["Nothing recorded for riker in this window."], lines);
    }

    [Fact]
    public async Task Actor_given_skips_me_entirely_and_is_used_as_typed()
    {
        using var h = AHarness();
        h.Wire.Json("GET", Route, AReport(0, [], [], actor: "Riker II"));

        Assert.Equal(0, await new ActivityCommand(h.Cli).RunAsync(["--actor", "Riker II"], default));

        Assert.Empty(h.Wire.To("GET", "/api/hatch/me"));
        Assert.StartsWith("?actor=Riker%20II&", Query(h));
        Assert.Equal("Nothing recorded for Riker II in this window.", h.Said);
    }

    [Fact]
    public async Task Without_an_actor_a_key_with_an_owner_defaults_to_the_owner()
    {
        using var h = AHarness();
        h.Wire.Json("GET", "/api/hatch/me", new MeDto("key", "Riker-MBP", null, true, false, false, "riker"));
        h.Wire.Json("GET", Route, ABusyReport());

        Assert.Equal(0, await new ActivityCommand(h.Cli).RunAsync([], default));

        Assert.StartsWith("?actor=riker&", Query(h));
        Assert.StartsWith("# riker, ", h.Said);
    }

    [Theory]
    [InlineData("key", null)]
    [InlineData("local", "riker")]
    public async Task Without_an_actor_or_an_owner_it_is_refused_and_the_activity_is_never_asked(string kind, string? owner)
    {
        using var h = AHarness();
        h.Wire.Json("GET", "/api/hatch/me", new MeDto(kind, "Somebody", null, true, false, false, owner));

        Assert.Equal(1, await new ActivityCommand(h.Cli).RunAsync([], default));

        Assert.Contains("an actor is required - pass --actor <name>, or run as a key with an owner", h.Complained);
        Assert.Empty(h.Wire.To("GET", Route));
    }

    [Fact]
    public async Task With_nothing_set_the_window_is_the_last_fourteen_days_ending_now()
    {
        using var h = AHarness();
        h.Wire.Json("GET", Route, AReport(0, [], []));

        await new ActivityCommand(h.Cli).RunAsync(["--actor", "riker"], default);

        Assert.Equal("?actor=riker&from=2026-10-28T12:00:00Z&to=2026-11-11T12:00:00Z", Query(h));
    }

    [Fact]
    public async Task Days_resizes_the_window_and_before_moves_where_it_ends()
    {
        using var h = AHarness();
        h.Wire.Json("GET", Route, AReport(0, [], []));

        await new ActivityCommand(h.Cli).RunAsync(
            ["--actor", "riker", "--days", "7", "--before", "2026-09-12T17:00:00Z"], default);

        Assert.Equal("?actor=riker&from=2026-09-05T17:00:00Z&to=2026-09-12T17:00:00Z", Query(h));
    }

    [Fact]
    public async Task A_before_without_a_zone_is_read_as_utc()
    {
        using var h = AHarness();
        h.Wire.Json("GET", Route, AReport(0, [], []));

        await new ActivityCommand(h.Cli).RunAsync(["--actor", "riker", "--days", "1", "--before", "2026-09-12"], default);

        Assert.Equal("?actor=riker&from=2026-09-11T00:00:00Z&to=2026-09-12T00:00:00Z", Query(h));
    }

    [Fact]
    public async Task A_before_that_is_not_an_instant_is_refused_by_name_and_nothing_is_called()
    {
        using var h = AHarness();

        Assert.Equal(1, await new ActivityCommand(h.Cli).RunAsync(["--actor", "riker", "--before", "soon"], default));

        Assert.Contains("--before takes an instant (2026-09-12T17:00:00Z) - not \"soon\"", h.Complained);
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task Days_takes_a_whole_number_of_at_least_one()
    {
        using var h = AHarness();

        Assert.Equal(1, await new ActivityCommand(h.Cli).RunAsync(["--days", "0"], default));
        Assert.Contains("--days takes a whole number of at least one", h.Complained);
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task Project_resolves_the_key_to_an_id_and_sends_that()
    {
        using var h = AHarness();
        h.Wire.Json("GET", "/api/hatch/projects", new List<ProjectDto> { AProject(3, "AER") });
        h.Wire.Json("GET", Route, AReport(0, [], []));

        Assert.Equal(0, await new ActivityCommand(h.Cli).RunAsync(["--actor", "riker", "--project", "aer"], default));

        Assert.EndsWith("&projectId=3", Query(h));
    }

    [Fact]
    public async Task Include_keys_asks_the_endpoint_for_the_actors_keys_too()
    {
        using var h = AHarness();
        h.Wire.Json("GET", Route, AReport(0, [], []));

        Assert.Equal(0, await new ActivityCommand(h.Cli).RunAsync(["--actor", "riker", "--include-keys"], default));

        Assert.EndsWith("&includeKeys=true", Query(h));
    }

    [Fact]
    public async Task Without_include_keys_the_query_does_not_mention_it()
    {
        using var h = AHarness();
        h.Wire.Json("GET", Route, AReport(0, [], []));

        Assert.Equal(0, await new ActivityCommand(h.Cli).RunAsync(["--actor", "riker"], default));

        Assert.DoesNotContain("includeKeys", Query(h));
    }

    [Fact]
    public async Task A_project_key_nobody_has_is_refused_by_name_and_the_activity_is_never_asked()
    {
        using var h = AHarness();
        h.Wire.Json("GET", "/api/hatch/projects", new List<ProjectDto> { AProject(3, "AER") });

        Assert.Equal(1, await new ActivityCommand(h.Cli).RunAsync(["--actor", "riker", "--project", "ZZZ"], default));

        Assert.Contains("there is no project ZZZ", h.Complained);
        Assert.Empty(h.Wire.To("GET", Route));
    }

    [Fact]
    public async Task Json_prints_the_endpoints_own_answer_and_nothing_else()
    {
        using var h = AHarness();
        var report = ABusyReport();
        h.Wire.Json("GET", Route, report);

        Assert.Equal(0, await new ActivityCommand(h.Cli).RunAsync(["--actor", "riker", "--json"], default));

        Assert.Equal(JsonSerializer.Serialize(report, Fixtures.Json), h.Said);
    }

    [Fact]
    public async Task Dash_h_prints_its_own_usage_and_calls_nothing()
    {
        using var h = AHarness();

        Assert.Equal(0, await new ActivityCommand(h.Cli).RunAsync(["-h"], default));
        Assert.StartsWith("usage: hatch activity", h.Said);
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task An_unknown_flag_is_refused_and_calls_nothing()
    {
        using var h = AHarness();

        Assert.Equal(1, await new ActivityCommand(h.Cli).RunAsync(["--nonsense"], default));
        Assert.Contains("activity does not take \"--nonsense\"", h.Complained);
        Assert.Empty(h.Wire.Calls);
    }
}

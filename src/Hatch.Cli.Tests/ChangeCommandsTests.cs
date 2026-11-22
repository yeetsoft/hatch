using System.Net;

namespace Hatch.Cli.Tests;

/// <summary>
/// <c>change</c>: an ambition's latest change to its capabilities, and the
/// outcome each epic beneath it records against one.
/// </summary>
public sealed class ChangeCommandsTests : RepoFixture
{
    private const string Changes = "/api/issues/AER-1/changes";

    private static ChangeEntryDto Entry(
        long eventId, string kind, long capabilityId,
        string? statementBefore = null, string? howToTellBefore = null,
        string? statementAfter = null, string? howToTellAfter = null) =>
        new(eventId, kind, capabilityId, 1, statementBefore, howToTellBefore, statementAfter, howToTellAfter,
            "somebody", DateTimeOffset.UnixEpoch);

    private static ChangeOutcomeDto Outcome(long eventId, string epic, string outcome, string why) =>
        new(1, eventId, epic, "An epic", "To Do", outcome, why, "somebody", DateTimeOffset.UnixEpoch, null);

    private static ChangeDto Change(
        long id, DateTimeOffset savedAt, DateTimeOffset? workedOutAt,
        IReadOnlyList<ChangeEntryDto>? entries = null, IReadOnlyList<ChangeOutcomeDto>? outcomes = null) =>
        new(id, savedAt, workedOutAt is not null, workedOutAt, entries ?? [], outcomes ?? []);

    private static Task<int> Change(CliHarness h, params string[] args) =>
        new ChangeCommands(h.Cli).ChangeAsync(args, default);

    // ---- list ----

    [Fact]
    public async Task List_prints_the_newest_change_its_entries_and_its_outcomes()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", Changes, new[]
        {
            Change(412, new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero), null,
                [
                    Entry(1, "added", 3, statementAfter: "It starts", howToTellAfter: "Start it"),
                    Entry(2, "changed", 2, statementBefore: "It stops", howToTellBefore: "Stop it",
                        statementAfter: "It halts", howToTellAfter: "Stop it"),
                    Entry(3, "changed", 4, statementBefore: "It pauses", howToTellBefore: "Check gently",
                        statementAfter: "It pauses", howToTellAfter: "Check hard"),
                    Entry(4, "removed", 5, statementBefore: "It paused", howToTellBefore: "Note it leaving"),
                ],
                [Outcome(412, "AER-5", "carries_on", "The change now asks for X")]),
        });

        Assert.Equal(0, await Change(h, "list", "AER-1"));

        Assert.Equal(
            """
            AER-1: change 412, saved 2026-11-20, not worked out
              added    #3  It starts
                how to tell: Start it
              changed  #2  It stops -> It halts
              changed  #4  It pauses
                how to tell: Check gently -> Check hard
              removed  #5  It paused
                how to tell: Note it leaving
              AER-5  carries_on  The change now asks for X
            """.ReplaceLineEndings("\n"),
            h.Said);
    }

    [Fact]
    public async Task List_of_an_ambition_with_no_change_says_so()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", Changes, Array.Empty<ChangeDto>());

        Assert.Equal(0, await Change(h, "list", "AER-1"));
        Assert.Equal("AER-1 has not been changed", h.Said);
    }

    [Fact]
    public async Task List_of_a_change_worked_out_says_when()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", Changes, new[]
        {
            Change(412, new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero)),
        });

        Assert.Equal(0, await Change(h, "list", "AER-1"));
        Assert.StartsWith("AER-1: change 412, saved 2026-11-20, worked out 2026-11-21", h.Said);
    }

    [Fact]
    public async Task List_with_no_outcomes_says_so()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", Changes, new[] { Change(412, DateTimeOffset.UnixEpoch, null) });

        Assert.Equal(0, await Change(h, "list", "AER-1"));
        Assert.Contains("\n  no outcomes yet", h.Said);
    }

    [Fact]
    public async Task List_prints_only_the_newest_of_two_changes()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", Changes, new[]
        {
            Change(412, new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero), null),
            Change(300, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), null),
        });

        Assert.Equal(0, await Change(h, "list", "AER-1"));
        Assert.StartsWith("AER-1: change 412,", h.Said);
        Assert.DoesNotContain("change 300", h.Said);
    }

    // ---- outcome ----

    [Fact]
    public async Task Outcome_posts_the_epic_the_word_and_the_why()
    {
        using var h = new CliHarness();
        h.Wire.Json("POST", $"{Changes}/412/outcomes", Outcome(412, "AER-5", "carries_on", "Still needed"));

        Assert.Equal(0, await Change(h, "outcome", "AER-1", "AER-5", "carries_on", "Still needed", "--change", "412"));

        var sent = Assert.Single(h.Wire.To("POST", $"{Changes}/412/outcomes")).Read<ChangeOutcomeRequest>();
        Assert.Equal(new ChangeOutcomeRequest("AER-5", "carries_on", "Still needed"), sent);
        Assert.Equal("AER-1: AER-5 carries_on for change 412", h.Said);
    }

    [Theory]
    [InlineData("")]
    [InlineData("outcome")]
    [InlineData("outcome", "AER-1")]
    [InlineData("outcome", "AER-1", "AER-5")]
    [InlineData("outcome", "AER-1", "AER-5", "carries_on")]
    [InlineData("outcome", "AER-1", "AER-5", "went_fine", "why", "--change", "412")]
    [InlineData("outcome", "AER-1", "AER-5", "carries_on", " ", "--change", "412")]
    [InlineData("outcome", "AER-1", "AER-5", "carries_on", "why")]
    [InlineData("outcome", "AER-1", "AER-5", "carries_on", "why", "--change")]
    [InlineData("outcome", "AER-1", "AER-5", "carries_on", "why", "--change", "0")]
    [InlineData("outcome", "AER-1", "AER-5", "carries_on", "why", "--change", "soon")]
    [InlineData("outcome", "AER-1", "AER-5", "carries_on", "why", "--change", "412", "--change", "413")]
    [InlineData("outcome", "AER-1", "AER-5", "carries_on", "why", "--nope", "412")]
    [InlineData("frobnicate", "AER-1")]
    [InlineData("list")]
    [InlineData("list", "AER-1", "AER-2")]
    public async Task A_usage_mistake_is_refused_and_sends_nothing(params string[] args)
    {
        using var h = new CliHarness();
        var argv = args is [""] ? [] : args;

        Assert.Equal(1, await Change(h, argv));

        Assert.StartsWith("hatch: ", h.Complained);
        Assert.Contains("usage: hatch change list", h.Complained);
        Assert.Empty(h.Wire.Calls);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("outcome", "--help")]
    [InlineData("list", "-h")]
    public async Task Help_prints_the_usage_on_standard_output(params string[] args)
    {
        using var h = new CliHarness();

        Assert.Equal(0, await Change(h, args));

        Assert.Contains("usage: hatch change list", h.Said);
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task A_server_refusal_surfaces_as_it_came()
    {
        using var h = new CliHarness();
        h.Wire.Reply("POST", $"{Changes}/412/outcomes", HttpStatusCode.Conflict,
            "\"a person has already decided AER-5's outcome on this change\"");

        var refused = await Assert.ThrowsAsync<HatchException>(() =>
            Change(h, "outcome", "AER-1", "AER-5", "carries_on", "Still needed", "--change", "412"));

        Assert.Equal(HttpStatusCode.Conflict, refused.Status);
        Assert.Contains("a person has already decided AER-5's outcome on this change", refused.Message);
    }
}

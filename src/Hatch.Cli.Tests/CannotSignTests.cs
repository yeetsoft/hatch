using System.Net;

namespace Hatch.Cli.Tests;

/// <summary>
/// The complaint a loop makes at start where git can never sign and no project
/// it serves allows an unsigned commit (HA-2657) - and the ways it carries on.
/// </summary>
public sealed class CannotSignTests
{
    private static readonly SigningCheck Locked = new(Signing.Fails, "error: gpg failed to sign the data: no secret key");
    private static readonly SigningCheck Off = new(Signing.Off);
    private static readonly SigningCheck Works = new(Signing.Works);

    private static ProjectDto Project(string key, bool allowsUnsigned) =>
        new(1, key, key, 0, DateTimeOffset.UnixEpoch, null, null, [], null, [], false, false, AllowsUnsignedCommits: allowsUnsigned);

    private static CliHarness Board(params ProjectDto[] projects)
    {
        var h = new CliHarness();
        h.Wire.Json("GET", "/api/projects", projects);
        return h;
    }

    private static Task<string[]?> Ask(CliHarness h, IReadOnlyList<string> keys, params SigningCheck[] answers) =>
        CannotSign.RefusalAsync(h.Cli.Board, keys, answers.Select((c, i) => ($"/checkout/{i}", c)).ToList(), default);

    [Fact]
    public async Task Where_no_project_allows_unsigned_commits_it_names_them_what_git_said_and_the_two_ways_out()
    {
        using var h = Board(Project("AER", false), Project("OTH", false));

        var lines = await Ask(h, [], Locked);

        Assert.NotNull(lines);
        Assert.Contains("AER, OTH", lines[0], StringComparison.Ordinal);
        Assert.Contains(lines, l => l.Contains("/checkout/0", StringComparison.Ordinal) && l.Contains("no secret key", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("fix signing on this machine", StringComparison.Ordinal)
            && l.Contains("allow unsigned commits", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Past_five_projects_it_says_how_many_more()
    {
        using var h = Board(Enumerable.Range(1, 8).Select(i => Project($"P{i}", false)).ToArray());

        var lines = await Ask(h, [], Locked);

        Assert.NotNull(lines);
        Assert.Contains("P1, P2, P3, P4, P5 and 3 more", lines[0], StringComparison.Ordinal);
        Assert.DoesNotContain("P6", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_checkout_not_set_to_sign_says_so()
    {
        using var h = Board(Project("AER", false));

        var lines = await Ask(h, [], Off);

        Assert.NotNull(lines);
        Assert.Contains(lines, l => l.Contains("not set to sign", StringComparison.Ordinal));
    }

    [Fact]
    public async Task One_project_that_allows_unsigned_commits_is_enough_to_carry_on()
    {
        using var h = Board(Project("AER", false), Project("OTH", true));

        Assert.Null(await Ask(h, [], Locked));
    }

    [Fact]
    public async Task Any_checkout_that_can_sign_is_enough_to_carry_on()
    {
        using var h = Board(Project("AER", false));

        Assert.Null(await Ask(h, [], Locked, Works));
    }

    [Fact]
    public async Task Only_the_projects_the_loop_was_started_for_are_read()
    {
        using var h = Board(Project("AER", false), Project("OTH", true));

        var lines = await Ask(h, ["AER"], Locked);
        Assert.NotNull(lines);
        Assert.DoesNotContain("OTH", lines[0], StringComparison.Ordinal);

        Assert.Null(await Ask(h, ["OTH"], Locked));
    }

    [Fact]
    public async Task A_project_key_the_board_does_not_return_is_carried_on_from()
    {
        using var h = Board(Project("AER", false));

        Assert.Null(await Ask(h, ["NOPE"], Locked));
    }

    [Fact]
    public async Task No_checkout_to_ask_about_is_carried_on_from()
    {
        using var h = Board(Project("AER", false));

        Assert.Null(await Ask(h, []));
    }

    [Fact]
    public async Task A_board_with_no_projects_is_carried_on_from()
    {
        using var h = Board();

        Assert.Null(await Ask(h, [], Locked));
    }

    [Fact]
    public async Task A_board_that_does_not_answer_is_carried_on_from()
    {
        using var h = new CliHarness();
        h.Wire.Reply("GET", "/api/projects", HttpStatusCode.InternalServerError, "boom");

        Assert.Null(await Ask(h, [], Locked));
        Assert.False(await CannotSign.NoneAllowAsync(h.Cli.Board, [], default));
    }

    [Fact]
    public async Task A_board_that_does_not_list_the_setting_reads_as_allowing_it()
    {
        using var h = new CliHarness();
        h.Wire.Reply("GET", "/api/projects", HttpStatusCode.OK,
            """[{"id":1,"key":"AER","name":"Aerie","issueCount":0,"createdAt":"1970-01-01T00:00:00Z","members":[],"canApprove":false,"canAdminister":false}]""");

        Assert.Null(await Ask(h, [], Locked));
    }
}

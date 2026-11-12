using System.Net;

namespace Hatch.Cli.Tests;

/// <summary>
/// <c>dream</c> - a thought put on the Dream Board.
/// </summary>
public sealed class DreamCommandTests
{
    private const string Route = "/api/dreams";

    private static ProjectDto AProject(int id, string key) =>
        new(id, key, key, 0, DateTimeOffset.UtcNow, null, null, [], null, [], false, false);

    private static DreamDto ADream()
    {
        var now = DateTimeOffset.UtcNow;
        return new(7, "words", "Nathan-MBP", null, null, null, now, now, []);
    }

    [Fact]
    public async Task Words_alone_post_trimmed_with_no_source_or_project_and_say_who_posted()
    {
        using var h = new CliHarness();
        h.Wire.Json("POST", Route, ADream());

        Assert.Equal(0, await new DreamCommand(h.Cli).RunAsync(["  words  "], default));

        var sent = h.Wire.To("POST", Route).Single().Read<DreamCreateRequest>();
        Assert.Equal("words", sent.Body);
        Assert.Null(sent.Source);
        Assert.Null(sent.ProjectId);
        Assert.Equal("posted dream 7 as Nathan-MBP", h.Said.Trim());
    }

    [Theory]
    [InlineData("words", "--source", "s", "--project", "aer")]
    [InlineData("--source", "s", "--project", "aer", "words")]
    public async Task A_source_and_a_project_key_post_the_source_and_the_projects_id(params string[] args)
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/projects", new List<ProjectDto> { AProject(3, "AER") });
        h.Wire.Json("POST", Route, ADream());

        Assert.Equal(0, await new DreamCommand(h.Cli).RunAsync(args, default));

        var sent = h.Wire.To("POST", Route).Single().Read<DreamCreateRequest>();
        Assert.Equal("words", sent.Body);
        Assert.Equal("s", sent.Source);
        Assert.Equal(3, sent.ProjectId);
    }

    [Fact]
    public async Task An_unknown_project_is_refused_before_anything_is_posted()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/projects", new List<ProjectDto> { AProject(3, "AER") });

        Assert.Equal(1, await new DreamCommand(h.Cli).RunAsync(["words", "--project", "ZZZ"], default));

        Assert.Contains("there is no project ZZZ", h.Complained);
        Assert.Empty(h.Wire.To("POST", Route));
    }

    [Theory]
    [MemberData(nameof(BadUsage))]
    public async Task Bad_usage_is_refused_with_the_usage_block_and_calls_nothing(string[] args)
    {
        using var h = new CliHarness();

        Assert.Equal(1, await new DreamCommand(h.Cli).RunAsync(args, default));

        Assert.Contains("usage: hatch dream", h.Complained);
        Assert.Empty(h.Wire.Calls);
    }

    public static TheoryData<string[]> BadUsage() => new()
    {
        Array.Empty<string>(),
        new[] { "   " },
        new[] { "a", "b" },
        new[] { "a", "--nope" },
        new[] { "--nope" },
        new[] { "a", "--help" },
        new[] { "a", "--source" },
        new[] { "a", "--project", "" },
    };

    [Fact]
    public async Task Dash_h_prints_the_usage_and_calls_nothing()
    {
        using var h = new CliHarness();

        Assert.Equal(0, await new DreamCommand(h.Cli).RunAsync(["-h"], default));

        Assert.Equal(string.Join("\n", DreamCommand.DreamUsage), h.Said.TrimEnd());
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task A_blank_source_is_sent_as_none()
    {
        using var h = new CliHarness();
        h.Wire.Json("POST", Route, ADream());

        Assert.Equal(0, await new DreamCommand(h.Cli).RunAsync(["words", "--source", "  "], default));

        Assert.Null(h.Wire.To("POST", Route).Single().Read<DreamCreateRequest>().Source);
    }

    [Fact]
    public async Task A_server_refusal_reaches_the_caller_with_its_reason()
    {
        using var h = new CliHarness();
        h.Wire.Reply("POST", Route, HttpStatusCode.BadRequest, "\"a dream needs some words\"");

        var thrown = await Assert.ThrowsAsync<HatchException>(
            () => new DreamCommand(h.Cli).RunAsync(["words"], default));

        Assert.Equal(HttpStatusCode.BadRequest, thrown.Status);
        Assert.Contains("a dream needs some words", thrown.Message);
    }

    [Fact]
    public async Task An_empty_answer_is_a_failure()
    {
        using var h = new CliHarness();
        h.Wire.Reply("POST", Route, HttpStatusCode.OK);

        Assert.Equal(1, await new DreamCommand(h.Cli).RunAsync(["words"], default));

        Assert.Contains("no dream", h.Complained);
    }
}

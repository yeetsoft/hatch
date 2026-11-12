using System.Net;

namespace Hatch.Cli.Tests;

/// <summary>
/// <see cref="Board.SuggestRunnerNameAsync"/> and
/// <see cref="Board.CreateRunnerAsync"/> - what <c>hatch config</c>'s mint
/// prompt calls on a wall-off board.
/// </summary>
public sealed class BoardRunnerMintTests
{
    [Fact]
    public async Task Suggest_reads_a_bare_string_body()
    {
        using var h = new CliHarness();
        h.Wire.Reply("GET", "/api/runners/suggest-name", HttpStatusCode.OK, "Data");

        Assert.Equal("Data", await h.Board.SuggestRunnerNameAsync(default));
    }

    [Fact]
    public async Task Suggest_reads_a_quoted_string_body()
    {
        using var h = new CliHarness();
        h.Wire.Reply("GET", "/api/runners/suggest-name", HttpStatusCode.OK, "\"Data\"");

        Assert.Equal("Data", await h.Board.SuggestRunnerNameAsync(default));
    }

    [Fact]
    public async Task Create_answers_the_minted_runner_and_its_secret()
    {
        using var h = new CliHarness();
        h.Wire.Json(
            "POST", "/api/runners",
            new RunnerMintedDto(
                new RunnerDto(
                    "Riker", "key", default, default, null, null, null, "new", null, null, null, null, 0,
                    [], null, null, null, null, null),
                "hatch_ak_minted"));

        var (minted, taken) = await h.Board.CreateRunnerAsync("Riker", default);

        Assert.Null(taken);
        Assert.Equal("Riker", minted!.Runner.Name);
        Assert.Equal("hatch_ak_minted", minted.Secret);
        Assert.Equal("{\"name\":\"Riker\"}", h.Wire.To("POST", "/api/runners").Single().Body);
    }

    [Fact]
    public async Task Create_hands_back_a_taken_name_as_the_tuples_second_half()
    {
        using var h = new CliHarness();
        h.Wire.Reply(
            "POST", "/api/runners", HttpStatusCode.Conflict,
            "hatch: there is already a runner called \"Riker\"");

        var (minted, taken) = await h.Board.CreateRunnerAsync("Riker", default);

        Assert.Null(minted);
        Assert.Contains("already a runner called", taken);
    }
}

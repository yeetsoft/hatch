using System.Text.Json;

namespace Hatch.Cli.Tests;

/// <summary>
/// <see cref="HatchClient.Json"/>'s own round trip for the two fields HA-808
/// adds - <see cref="PlaybookDto.Source"/> and <see cref="WorkDto.PlaybookFile"/> -
/// since a type a server might send is also one the CLI's trimmed, source-generated
/// resolver has to actually know about (<c>HatchJson</c>), not just compile against.
/// </summary>
public sealed class DtosTests
{
    [Fact]
    public void PlaybookDto_round_trips_through_HatchClient_Json_with_its_source()
    {
        var playbook = Fixtures.Playbook() with { Source = "file" };

        var json = JsonSerializer.Serialize(playbook, HatchClient.Json);
        var back = JsonSerializer.Deserialize<PlaybookDto>(json, HatchClient.Json);

        Assert.NotNull(back);
        Assert.Equal("file", back.Source);
    }

    [Fact]
    public void WorkDto_round_trips_through_HatchClient_Json_with_its_playbook_file()
    {
        var file = new PlaybookFileReferenceDto(".hatch/playbooks.yaml", "example.com/o/r", "main", "abc123", "deadbeef");
        var work = Fixtures.Work("AER-1") with { PlaybookFile = file };

        var json = JsonSerializer.Serialize(work, HatchClient.Json);
        var back = JsonSerializer.Deserialize<WorkDto>(json, HatchClient.Json);

        Assert.NotNull(back);
        Assert.Equal(file, back.PlaybookFile);
    }

    [Fact]
    public void The_contribution_members_default_to_null_so_an_older_board_still_reads()
    {
        Assert.Null(Fixtures.Issue("AER-1").Contribution);
        Assert.Null(Fixtures.Card("AER-1").Contribution);
        Assert.Null(new ReviewCheckDto("AER-1", [], []).Branch);
    }

    [Fact]
    public void ContributionDto_round_trips_through_HatchClient_Json()
    {
        var contribution = new ContributionDto(
            "https://forge.example/o/r/pull/7", "feature/thing", "octocat", "NONE",
            DateTimeOffset.Parse("2026-11-11T12:34:56+00:00"), "runner-1",
            "https://hatch.example", "OTH", "OTH-4", 1, null);
        var issue = Fixtures.Issue("AER-1") with { Contribution = contribution };

        var json = JsonSerializer.Serialize(issue, HatchClient.Json);
        var back = JsonSerializer.Deserialize<IssueDto>(json, HatchClient.Json);

        Assert.NotNull(back);
        Assert.Equal(contribution, back.Contribution);
        Assert.Contains("\"contribution\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ContributionRequest_serialises_through_HatchClient_Json()
    {
        var json = JsonSerializer.Serialize(
            new ContributionRequest("https://forge.example/o/r/pull/7", "main", "octocat", "NONE"), HatchClient.Json);

        Assert.Contains("\"authorLogin\":\"octocat\"", json, StringComparison.Ordinal);
    }
}

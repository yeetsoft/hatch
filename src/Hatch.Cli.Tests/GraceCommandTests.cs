using System.Net;

namespace Hatch.Cli.Tests;

/// <summary>
/// <c>grace</c> - a session asking that its claim not be called quiet for a
/// while, ahead of a command it knows will say nothing that long.
/// </summary>
public sealed class GraceCommandTests
{
    private const string Route = "/api/hatch/issues/GL-10/claim/grace";

    [Fact]
    public async Task The_minutes_are_sent_to_the_claim_and_said_back()
    {
        using var h = new CliHarness();
        h.Wire.Reply("POST", Route, HttpStatusCode.NoContent);

        Assert.Equal(0, await new GraceCommand(h.Cli).RunAsync(["GL-10", "20"], default));

        Assert.Equal(20, h.Wire.To("POST", Route).Single().Read<ClaimGraceRequest>().Minutes);
        Assert.Contains("GL-10: not quiet for the next 20 minutes", h.Said);
    }

    [Fact]
    public async Task A_refusal_is_printed_as_the_servers_own_sentence()
    {
        using var h = new CliHarness();
        h.Wire.Reply("POST", Route, HttpStatusCode.BadRequest, "grace is at most 30 minutes here");

        Assert.Equal(1, await new GraceCommand(h.Cli).RunAsync(["GL-10", "90"], default));

        Assert.Contains("grace is at most 30 minutes here", h.Complained);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("soon")]
    public async Task Minutes_that_are_not_a_positive_number_never_leave_the_machine(string minutes)
    {
        using var h = new CliHarness();

        Assert.Equal(1, await new GraceCommand(h.Cli).RunAsync(["GL-10", minutes], default));

        Assert.Equal(0, h.Wire.Count("POST", Route));
    }

    [Fact]
    public async Task A_key_alone_is_a_usage_error()
    {
        using var h = new CliHarness();

        Assert.NotEqual(0, await new GraceCommand(h.Cli).RunAsync(["GL-10"], default));
        Assert.Equal(0, h.Wire.Count("POST", Route));
    }
}

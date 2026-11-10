using System.Net;

namespace Hatch.Cli.Tests;

/// <summary>
/// What a loop needs of its key: that there is one to refuse without, and the
/// name <c>GET /api/hatch/me</c> reads off it.
/// </summary>
public sealed class LoopKeyTests
{
    private static Settings Keyed(string key = "hatch_ak_test") =>
        new() { Base = "https://hatch.example", Key = key, HeartbeatSeconds = 0, RetrySeconds = 0 };

    // ---- Refuses ----

    [Theory]
    [InlineData("work", new[] { "AER-1" })]
    [InlineData("work", new[] { "AER-1", "-i" })]
    [InlineData("work", new[] { "AER-1", "--interactive" })]
    [InlineData("go-to-work", new string[0])]
    [InlineData("go-to-work", new[] { "--runners", "2" })]
    [InlineData("do-my-work", new string[0])]
    [InlineData("do-my-work", new[] { "--runners", "2" })]
    public void A_loop_without_a_key_is_refused(string command, string[] rest)
    {
        Assert.True(LoopKey.Refuses(command, rest, Keyed(key: "")));
    }

    [Theory]
    [InlineData("work")]
    [InlineData("go-to-work")]
    [InlineData("do-my-work")]
    public void A_key_of_only_spaces_is_no_key(string command)
    {
        Assert.True(LoopKey.Refuses(command, [], Keyed(key: "   ")));
    }

    [Theory]
    [InlineData("work", new[] { "AER-1" })]
    [InlineData("go-to-work", new[] { "--runners", "2" })]
    [InlineData("do-my-work", new string[0])]
    public void A_loop_with_a_key_is_not_refused(string command, string[] rest)
    {
        Assert.False(LoopKey.Refuses(command, rest, Keyed()));
    }

    [Theory]
    [InlineData("work")]
    [InlineData("go-to-work")]
    [InlineData("do-my-work")]
    public void Usage_is_still_printed_without_a_key(string command)
    {
        Assert.False(LoopKey.Refuses(command, ["-h"], Keyed(key: "")));
        Assert.False(LoopKey.Refuses(command, ["--help"], Keyed(key: "")));
    }

    [Theory]
    [InlineData("board")]
    [InlineData("show")]
    [InlineData("queue")]
    [InlineData("next")]
    [InlineData("runners")]
    [InlineData("recuse")]
    [InlineData("standby")]
    public void Nothing_but_a_loop_is_refused_without_a_key(string command)
    {
        Assert.False(LoopKey.Refuses(command, ["AER-1"], Keyed(key: "")));
    }

    [Fact]
    public void The_refusal_is_one_sentence_naming_hatch_config()
    {
        Assert.Contains("`hatch config`", LoopKey.Refusal);
        Assert.DoesNotContain('\n', LoopKey.Refusal);
    }

    // ---- NameAsync ----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Without_a_key_there_is_no_name_to_read_and_nothing_is_asked(string key)
    {
        using var wire = new Wire();

        Assert.Equal("", await LoopKey.NameAsync(Keyed(key), default, wire));
        Assert.Empty(wire.Calls);
    }

    [Fact]
    public async Task With_a_key_the_name_is_the_one_me_answers()
    {
        using var wire = new Wire();
        wire.Json("GET", "/api/hatch/me", new MeDto("key", "Riker", null, true, false, false));

        Assert.Equal("Riker", await LoopKey.NameAsync(Keyed(), default, wire));
        Assert.Single(wire.To("GET", "/api/hatch/me"));
        Assert.Single(wire.Calls);
    }

    [Fact]
    public async Task A_caller_that_is_not_a_key_has_no_name_to_run_as()
    {
        using var wire = new Wire();
        wire.Json("GET", "/api/hatch/me", new MeDto("local", "Somebody", null, true, false, false));

        Assert.Null(await LoopKey.NameAsync(Keyed(), default, wire));
    }

    [Fact]
    public async Task A_failed_read_has_no_name_either()
    {
        using var wire = new Wire();
        wire.Reply("GET", "/api/hatch/me", HttpStatusCode.InternalServerError);

        Assert.Null(await LoopKey.NameAsync(Keyed(), default, wire));
    }
}

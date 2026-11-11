namespace Hatch.Cli.Tests;

/// <summary>
/// What a session said on its way out, made safe for a comment: keys masked,
/// lines capped, nothing able to close the comment's fence.
/// </summary>
public sealed class ScrubTests
{
    [Theory]
    [InlineData("auth failed for hatch_ak_abc123", "auth failed for [redacted]")]
    [InlineData("Authorization: Bearer abc.def-123_x~y+z/w=", "Authorization: [redacted]")]
    [InlineData("sent authorization: bearer abc123 again", "sent authorization: [redacted] again")]
    [InlineData("bad key sk-ant-api03-AbCdEf12 rejected", "bad key [redacted] rejected")]
    [InlineData("bad key sk-abcdefghijklmnopqrstuvwx rejected", "bad key [redacted] rejected")]
    [InlineData("bad key xai-abcdefghijklmnopqrstuvwx rejected", "bad key [redacted] rejected")]
    public void A_secret_is_masked(string line, string expected)
    {
        Assert.Equal([expected], Scrub.Lines([line]));
    }

    [Fact]
    public void A_short_sk_prefix_is_left_alone()
    {
        Assert.Equal(["sk-short and xai-short"], Scrub.Lines(["sk-short and xai-short"]));
    }

    [Fact]
    public void Forty_one_lines_keep_the_last_forty()
    {
        var lines = Enumerable.Range(1, 41).Select(i => $"line {i}").ToList();

        var kept = Scrub.Lines(lines);

        Assert.Equal(40, kept.Count);
        Assert.Equal("line 2", kept[0]);
        Assert.Equal("line 41", kept[^1]);
    }

    [Fact]
    public void A_long_line_is_capped_with_an_ellipsis()
    {
        var kept = Assert.Single(Scrub.Lines([new string('x', 2000)]));

        Assert.Equal(201, kept.Length);
        Assert.EndsWith("…", kept, StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_straddling_the_cap_is_never_half_shown()
    {
        var kept = Assert.Single(Scrub.Lines([new string('x', 180) + " hatch_ak_" + new string('a', 40)]));

        Assert.DoesNotContain("hatch_ak", kept, StringComparison.Ordinal);
        Assert.DoesNotContain("aaaa", kept, StringComparison.Ordinal);
        Assert.Contains("[redacted]", kept, StringComparison.Ordinal);
    }

    [Fact]
    public void Blank_lines_are_dropped_after_the_last_forty_are_taken()
    {
        Assert.Equal(["a", "b"], Scrub.Lines(["", "a", "  ", "b", ""]));
    }

    [Fact]
    public void A_code_fence_in_the_text_cannot_close_the_comments()
    {
        var kept = Assert.Single(Scrub.Lines(["before ``` after"]));
        var fenced = Scrub.Fence(kept);

        Assert.DoesNotContain("```", kept, StringComparison.Ordinal);
        Assert.Equal(2, fenced.Split("```").Length - 1);
        Assert.StartsWith("The CLI said:\n\n```\n", fenced, StringComparison.Ordinal);
        Assert.EndsWith("\n```", fenced, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tail_keeps_the_last_forty_under_concurrent_adds()
    {
        var tail = new Tail();

        await Task.WhenAll(Enumerable.Range(0, 4).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 100; i++) tail.Add($"{t}-{i}");
        })));

        Assert.Equal(40, tail.ToList().Count);
    }

    [Fact]
    public void A_tail_of_forty_one_drops_the_first()
    {
        var tail = new Tail();
        for (var i = 1; i <= 41; i++) tail.Add($"line {i}");

        var kept = tail.ToList();

        Assert.Equal(40, kept.Count);
        Assert.Equal("line 2", kept[0]);
    }
}

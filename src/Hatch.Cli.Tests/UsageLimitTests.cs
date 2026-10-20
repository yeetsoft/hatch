namespace Hatch.Cli.Tests;

/// <summary>
/// Reading a usage limit out of the one sentence a session leaves behind - the
/// reported line, and the variations on it the story asks for.
/// </summary>
public sealed class UsageLimitTests
{
    private static readonly DateTimeOffset EndedAt = DateTimeOffset.Parse("2026-09-28T22:00:00+00:00");

    [Fact]
    public void The_reported_sentence_gives_the_instant_it_names()
    {
        var hit = UsageLimit.Recognise("You've hit your session limit · resets 7:40pm (America/New_York)", EndedAt);

        Assert.NotNull(hit);
        Assert.True(hit!.Value.ResetKnown);
        Assert.Equal(DateTimeOffset.Parse("2026-09-28T23:40:00+00:00"), hit.Value.ResetAt);
        Assert.Equal("session", hit.Value.Window);
    }

    [Fact]
    public void An_hour_with_no_minutes_is_read_too()
    {
        var hit = UsageLimit.Recognise("You've hit your session limit · resets 7pm (America/New_York)", EndedAt);

        Assert.NotNull(hit);
        Assert.True(hit!.Value.ResetKnown);
        Assert.Equal(DateTimeOffset.Parse("2026-09-28T23:00:00+00:00"), hit.Value.ResetAt);
    }

    [Fact]
    public void A_weekly_limit_names_a_date_and_it_is_taken_as_given()
    {
        var hit = UsageLimit.Recognise("You've hit your weekly limit · resets Oct 5 at 7:40pm (America/New_York)", EndedAt);

        Assert.NotNull(hit);
        Assert.True(hit!.Value.ResetKnown);
        Assert.Equal(DateTimeOffset.Parse("2026-10-05T23:40:00+00:00"), hit.Value.ResetAt);
        Assert.Equal("weekly", hit.Value.Window);
    }

    [Fact]
    public void An_unknown_zone_still_gives_an_instant_by_falling_back_to_the_runners_zone()
    {
        var hit = UsageLimit.Recognise("You've hit your session limit · resets 7:40pm (Mars/Colony)", EndedAt);

        // The next 7:40pm on this machine's own clock after EndedAt, whatever zone that is.
        var local = TimeZoneInfo.Local;
        var today = TimeZoneInfo.ConvertTime(EndedAt, local).Date.AddHours(19).AddMinutes(40);
        if (today <= TimeZoneInfo.ConvertTime(EndedAt, local).DateTime) today = today.AddDays(1);
        var expected = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(today, DateTimeKind.Unspecified), local);

        Assert.NotNull(hit);
        Assert.True(hit!.Value.ResetKnown);
        Assert.Equal(new DateTimeOffset(expected, TimeSpan.Zero), hit.Value.ResetAt);
    }

    [Fact]
    public void An_hour_already_past_today_means_tomorrow()
    {
        // 22:00 UTC is 18:00 in New York on this date - 7:40pm has not happened yet.
        var hit = UsageLimit.Recognise("You've hit your session limit · resets 5:00pm (America/New_York)", EndedAt);

        Assert.NotNull(hit);
        Assert.Equal(DateTimeOffset.Parse("2026-09-29T21:00:00+00:00"), hit!.Value.ResetAt);
    }

    [Fact]
    public void A_limit_whose_time_cannot_be_read_is_still_recognised_but_unknown()
    {
        var hit = UsageLimit.Recognise("You've hit your session limit · resets soon, we promise", EndedAt);

        Assert.NotNull(hit);
        Assert.False(hit!.Value.ResetKnown);
        Assert.Equal(EndedAt + UsageLimit.UnknownWindow, hit.Value.ResetAt);
    }

    [Fact]
    public void A_weekly_limit_that_crosses_the_new_year_resolves_forward_and_not_a_year_back()
    {
        var endedLateDecember = DateTimeOffset.Parse("2026-12-30T22:00:00+00:00");

        var hit = UsageLimit.Recognise(
            "You've hit your weekly limit · resets Jan 3 at 7:40pm (America/New_York)", endedLateDecember);

        Assert.NotNull(hit);
        Assert.True(hit!.Value.ResetKnown);
        Assert.True(hit.Value.ResetAt > endedLateDecember, "the reset must be after the session ended, not a year before it");
        Assert.Equal(DateTimeOffset.Parse("2027-01-04T00:40:00+00:00"), hit.Value.ResetAt);
    }

    [Fact]
    public void An_ordinary_error_is_not_a_limit()
    {
        Assert.Null(UsageLimit.Recognise("the build failed on main", EndedAt));
    }

    [Theory]
    [InlineData("You've hit your session limit · resets 7:40pm (America/New_York)", null, "2026-09-28T22:00:00+00:00", "2026-09-28T23:40:00+00:00", true)]
    [InlineData("You've hit your session limit · resets 7pm (America/New_York)", null, "2026-09-28T22:00:00+00:00", "2026-09-28T23:00:00+00:00", true)]
    [InlineData("You've hit your weekly limit · resets Oct 5 at 7:40pm (America/New_York)", null, "2026-09-28T22:00:00+00:00", "2026-10-05T23:40:00+00:00", true)]
    [InlineData("You've hit your weekly limit · resets Oct 5, 7:40pm (America/New_York)", null, "2026-09-28T22:00:00+00:00", "2026-10-05T23:40:00+00:00", true)]
    [InlineData("You've hit your Opus limit · resets 7:40pm (America/New_York)", null, "2026-09-28T22:00:00+00:00", "2026-09-28T23:40:00+00:00", true)]
    [InlineData("You've hit your Sonnet limit · resets Oct 5, 7:40pm (America/New_York)", null, "2026-09-28T22:00:00+00:00", "2026-10-05T23:40:00+00:00", true)]
    [InlineData("You've hit your Fable limit · resets 7:40pm (America/New_York)", null, "2026-09-28T22:00:00+00:00", "2026-09-28T23:40:00+00:00", true)]
    [InlineData("You've hit your session limit · resets 7:40pm (America/New_York) · progress saved", null, "2026-09-28T22:00:00+00:00", "2026-09-28T23:40:00+00:00", true)]
    [InlineData("You're out of usage credits · resets 7:40pm (America/New_York)", null, "2026-09-28T22:00:00+00:00", "2026-09-28T23:40:00+00:00", true)]
    [InlineData("You've hit your session limit · resets 7:40pm", "America/New_York", "2026-09-28T22:00:00+00:00", "2026-09-28T23:40:00+00:00", true)]
    [InlineData("You've hit your session limit · resets 5:00pm", "America/New_York", "2026-09-28T22:00:00+00:00", "2026-09-29T21:00:00+00:00", true)]
    [InlineData("You've hit your weekly limit · resets Jan 3, 2027 at 7:40pm (America/New_York)", null, "2026-12-30T22:00:00+00:00", "2027-01-04T00:40:00+00:00", true)]
    [InlineData("You've hit your weekly limit · resets Jan 3, 2027, 7:40pm (America/New_York)", null, "2026-12-30T22:00:00+00:00", "2027-01-04T00:40:00+00:00", true)]
    [InlineData("You've hit your session limit · resets 7:40pm (Mars/Colony)", "America/Los_Angeles", "2026-09-28T22:00:00+00:00", "2026-09-29T02:40:00+00:00", true)]
    [InlineData("You've hit your session limit · resets soon, we promise", null, "2026-09-28T22:00:00+00:00", null, false)]
    [InlineData("the build failed on main", null, "2026-09-28T22:00:00+00:00", null, null)]
    public void Every_sentence_shape_the_CLI_prints_resolves_to_the_instant_it_names(
        string said, string? runnerZone, string endedAt, string? expectedResetAt, bool? expectedResetKnown)
    {
        var ended = DateTimeOffset.Parse(endedAt);
        var zone = runnerZone is null ? null : TimeZoneInfo.FindSystemTimeZoneById(runnerZone);

        var hit = UsageLimit.Recognise(said, ended, zone);

        if (expectedResetKnown is null)
        {
            Assert.Null(hit);
            return;
        }

        Assert.NotNull(hit);
        Assert.Equal(expectedResetKnown, hit!.Value.ResetKnown);
        Assert.Equal(
            expectedResetAt is null ? ended + UsageLimit.UnknownWindow : DateTimeOffset.Parse(expectedResetAt),
            hit.Value.ResetAt);
    }

    [Fact]
    public void Nothing_said_is_not_a_limit()
    {
        Assert.Null(UsageLimit.Recognise(null, EndedAt));
        Assert.Null(UsageLimit.Recognise("", EndedAt));
    }

    [Fact]
    public void A_known_zones_offset_is_printed_as_given_even_when_it_is_UTC()
    {
        var at = DateTimeOffset.Parse("2026-09-28T23:40:00+00:00");

        Assert.Equal("11:40pm", UsageLimit.Clock(at, known: true));
    }

    [Fact]
    public void A_known_zones_offset_is_printed_as_given()
    {
        var at = DateTimeOffset.Parse("2026-09-28T19:40:00-04:00");

        Assert.Equal("7:40pm", UsageLimit.Clock(at, known: true));
    }

    [Fact]
    public void The_backstop_reads_in_the_runners_own_local_zone_not_the_UTC_it_carries()
    {
        var at = DateTimeOffset.Parse("2026-09-28T23:00:00+00:00");
        var local = new FrozenClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(-5)));

        Assert.Equal("6:00pm", UsageLimit.Clock(at, known: false, local));
    }

    [Fact]
    public void The_backstop_sentence_names_the_local_hour_not_the_UTC_one_end_to_end()
    {
        var hit = UsageLimit.Recognise("You've hit your session limit · resets soon, we promise", EndedAt);
        var local = new FrozenClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(-5)));

        Assert.NotNull(hit);
        Assert.False(hit!.Value.ResetKnown);
        Assert.Equal("6:00pm", UsageLimit.Clock(hit.Value.ResetAt, hit.Value.ResetKnown, local));
    }

    [Fact]
    public void Codex_reads_the_clock_form_through_Recognise()
    {
        var hit = UsageLimit.RecogniseCodex("You've hit your session limit · resets 7:40pm (America/New_York)", EndedAt);

        Assert.NotNull(hit);
        Assert.True(hit!.Value.ResetKnown);
        Assert.Equal(DateTimeOffset.Parse("2026-09-28T23:40:00+00:00"), hit.Value.ResetAt);
        Assert.Equal("session", hit.Value.Window);
    }

    [Theory]
    [InlineData("You've hit your usage limit. Try again in 4 days")]
    [InlineData("rate limit exceeded")]
    [InlineData("usage_limit_reached")]
    [InlineData("rate_limit_exceeded")]
    public void Codex_limit_reports_with_no_clock_rest_the_backstop_hour(string said)
    {
        var hit = UsageLimit.RecogniseCodex(said, EndedAt);

        Assert.NotNull(hit);
        Assert.False(hit!.Value.ResetKnown);
        Assert.Equal(EndedAt + UsageLimit.UnknownWindow, hit.Value.ResetAt);
        Assert.Equal("session", hit.Value.Window);
    }

    [Fact]
    public void Codex_weekly_limit_is_the_weekly_window()
    {
        var hit = UsageLimit.RecogniseCodex("weekly usage limit reached", EndedAt);

        Assert.NotNull(hit);
        Assert.False(hit!.Value.ResetKnown);
        Assert.Equal("weekly", hit.Value.Window);
    }

    [Theory]
    [InlineData("stream disconnected")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Codex_ordinary_errors_and_nothing_said_are_not_a_limit(string? said)
    {
        Assert.Null(UsageLimit.RecogniseCodex(said, EndedAt));
    }

    [Theory]
    [InlineData("You’ve reached your free Grok Build usage limit for now. Get SuperGrok for much higher limits, or try again later: https://example.invalid")]
    [InlineData("Grok Build usage limit reached")]
    [InlineData("You hit your free usage limit.")]
    public void Grok_limit_reports_rest_the_backstop_hour(string said)
    {
        var hit = UsageLimit.RecogniseGrok(said, EndedAt);

        Assert.NotNull(hit);
        Assert.False(hit!.Value.ResetKnown);
        Assert.Equal(EndedAt + UsageLimit.UnknownWindow, hit.Value.ResetAt);
        Assert.Equal("session", hit.Value.Window);
    }

    [Theory]
    [InlineData("API error (status 429 Too Many Requests): rate limit exceeded")]
    [InlineData("stream disconnected")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Grok_throttling_ordinary_errors_and_nothing_said_are_not_a_limit(string? said)
    {
        Assert.Null(UsageLimit.RecogniseGrok(said, EndedAt));
    }
}

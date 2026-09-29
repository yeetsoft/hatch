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
    }

    [Fact]
    public void An_unknown_zone_still_gives_an_instant_by_falling_back_to_UTC()
    {
        // 19:40 UTC has already passed by 22:00 UTC, so this is 19:40 the next day.
        var hit = UsageLimit.Recognise("You've hit your session limit · resets 7:40pm (Mars/Colony)", EndedAt);

        Assert.NotNull(hit);
        Assert.True(hit!.Value.ResetKnown);
        Assert.Equal(DateTimeOffset.Parse("2026-09-29T19:40:00+00:00"), hit.Value.ResetAt);
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

    [Fact]
    public void Nothing_said_is_not_a_limit()
    {
        Assert.Null(UsageLimit.Recognise(null, EndedAt));
        Assert.Null(UsageLimit.Recognise("", EndedAt));
    }
}

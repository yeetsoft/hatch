using Microsoft.Extensions.Time.Testing;

namespace Hatch.Cli.Tests;

/// <summary>What a night stops for, and what it does not.</summary>
public sealed class TallyTests
{
    private static IncrementReport Report(
        string key = "AER-1", int exit = 0, decimal cost = 1m, bool moved = true, bool lost = false,
        DateTimeOffset? usageLimitResetAt = null, bool letGo = false, bool preempted = false) =>
        new()
        {
            Key = key,
            From = "In Progress",
            To = "In Review",
            Ended = moved ? "In Review" : "In Progress",
            Moved = moved,
            Stalled = !moved,
            LetGo = letGo,
            ExitCode = exit,
            Cost = cost,
            LostLease = lost,
            UsageLimitResetAt = usageLimitResetAt,
            Preempted = preempted,
            PreemptedKey = preempted ? "AER-9" : null,
            PreemptedTitle = preempted ? "Trunk is down" : null,
        };

    [Fact]
    public void Three_failures_in_a_row_end_the_night_and_name_them()
    {
        var tally = new Tally(TimeProvider.System);

        tally.Record(Report("AER-1", exit: 1));
        tally.Record(Report("AER-2", exit: 2));
        Assert.False(tally.ShouldStop());

        tally.Record(Report("AER-3", exit: 1));
        Assert.True(tally.ShouldStop());
        Assert.Contains("AER-3 (exit 1)", tally.StopWhy!, StringComparison.Ordinal);
    }

    [Fact]
    public void One_that_worked_clears_the_streak()
    {
        var tally = new Tally(TimeProvider.System);

        tally.Record(Report(exit: 1));
        tally.Record(Report(exit: 1));
        tally.Record(Report(exit: 0));
        tally.Record(Report(exit: 1));

        Assert.False(tally.ShouldStop());
    }

    [Fact]
    public void A_lost_lease_is_not_one_of_the_three()
    {
        var tally = new Tally(TimeProvider.System);

        // A session stopped because its lease went exits badly, and it must not
        // be counted as a broken increment: it is the loop working correctly on
        // a busy board, and three of them in a row would end a night for a
        // reason that is not a fault.
        tally.Record(Report("AER-1", exit: 143, lost: true, moved: false));
        tally.Record(Report("AER-2", exit: 143, lost: true, moved: false));
        tally.Record(Report("AER-3", exit: 143, lost: true, moved: false));
        tally.Record(Report("AER-4", exit: 143, lost: true, moved: false));

        Assert.False(tally.ShouldStop());
        Assert.Equal(0, tally.Fails);

        // It still happened, and it still cost something.
        Assert.Equal(4, tally.Runs);
        Assert.Equal(4m, tally.Spent);
    }

    /// <summary>
    /// HA-169: the board ordering a ticket put down for an emergency is not a
    /// broken increment either - three of these in a row, each exiting non-zero
    /// because the session was killed, must not arm the three-strikes stop.
    /// </summary>
    [Fact]
    public void Three_preemptions_in_a_row_do_not_stop_the_night()
    {
        var tally = new Tally(TimeProvider.System);

        tally.Record(Report("AER-1", exit: 143, moved: false, preempted: true));
        tally.Record(Report("AER-2", exit: 143, moved: false, preempted: true));
        tally.Record(Report("AER-3", exit: 143, moved: false, preempted: true));

        Assert.False(tally.ShouldStop());
        Assert.Equal(0, tally.Fails);
        Assert.Equal(3, tally.Runs);
    }

    [Fact]
    public void A_usage_limit_is_not_one_of_the_three_either()
    {
        var tally = new Tally(TimeProvider.System);
        var resetAt = DateTimeOffset.Parse("2026-09-28T23:40:00+00:00");

        tally.Record(Report("AER-1", exit: 1, moved: false, usageLimitResetAt: resetAt));
        tally.Record(Report("AER-2", exit: 1, moved: false, usageLimitResetAt: resetAt));
        tally.Record(Report("AER-3", exit: 1, moved: false, usageLimitResetAt: resetAt));
        tally.Record(Report("AER-4", exit: 1, moved: false, usageLimitResetAt: resetAt));

        Assert.False(tally.ShouldStop());
        Assert.Equal(0, tally.Fails);
        Assert.Equal(resetAt, tally.ExhaustedUntil);
    }

    [Fact]
    public void An_increment_that_worked_clears_a_usage_limit_the_night_carried()
    {
        var tally = new Tally(TimeProvider.System);
        tally.Record(Report("AER-1", moved: false, usageLimitResetAt: DateTimeOffset.UtcNow));
        Assert.NotNull(tally.ExhaustedUntil);

        tally.Record(Report("AER-2", moved: true));
        Assert.Null(tally.ExhaustedUntil);
    }

    [Fact]
    public void Max_runs_and_max_spend_are_both_read_before_the_next_increment()
    {
        var runs = new Tally(TimeProvider.System) { MaxRuns = 2 };
        runs.Record(Report());
        Assert.False(runs.ShouldStop());
        runs.Record(Report());
        Assert.True(runs.ShouldStop());
        Assert.Contains("--max-runs 2", runs.StopWhy!, StringComparison.Ordinal);

        var spend = new Tally(TimeProvider.System) { MaxSpend = 3m };
        spend.Record(Report(cost: 2m));
        Assert.False(spend.ShouldStop());
        spend.Record(Report(cost: 1.5m));
        Assert.True(spend.ShouldStop());
        Assert.Contains("$3.50", spend.StopWhy!, StringComparison.Ordinal);
    }

    [Fact]
    public void An_hour_that_has_come_ends_it()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 2, 0, 0, TimeSpan.Zero));
        var tally = new Tally(clock)
        {
            Until = "06:00",
            UntilAt = new DateTimeOffset(2026, 9, 8, 6, 0, 0, TimeSpan.Zero),
        };

        Assert.False(tally.ShouldStop());

        clock.Advance(TimeSpan.FromHours(4));
        Assert.True(tally.ShouldStop());
        Assert.Contains("--until 06:00", tally.StopWhy!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stop_file_that_appears_is_noticed()
    {
        var dir = Directory.CreateTempSubdirectory("hatch-stop-");
        try
        {
            var stop = Path.Combine(dir.FullName, "stop");
            var tally = new Tally(TimeProvider.System) { StopFile = stop };

            Assert.False(tally.ShouldStop());
            File.WriteAllText(stop, "");
            Assert.True(tally.ShouldStop());
            Assert.Contains(stop, tally.StopWhy!, StringComparison.Ordinal);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Somebody_who_says_until_six_at_eleven_at_night_means_the_morning()
    {
        var lateEvening = new DateTimeOffset(2026, 9, 7, 23, 0, 0, TimeSpan.Zero);
        var morning = GoToWorkCommand.AtClock("06:00", lateEvening);

        Assert.NotNull(morning);
        Assert.Equal(new DateTimeOffset(2026, 9, 8, 6, 0, 0, TimeSpan.Zero), morning);

        // And one that has not gone by yet is today's.
        Assert.Equal(
            new DateTimeOffset(2026, 9, 8, 6, 0, 0, TimeSpan.Zero),
            GoToWorkCommand.AtClock("06:00", new DateTimeOffset(2026, 9, 8, 2, 0, 0, TimeSpan.Zero)));

        Assert.Null(GoToWorkCommand.AtClock("half past six", lateEvening));
    }

    [Fact]
    public void The_tally_is_three_lists_because_a_usage_limit_is_a_third_morning()
    {
        var say = new Transcript();
        var tally = new Tally(TimeProvider.System);

        tally.Record(Report("AER-1", moved: true));
        tally.Record(Report("AER-2", moved: false));
        tally.Record(Report("AER-3", moved: false, usageLimitResetAt: DateTimeOffset.UtcNow));
        tally.StopWhy = "--once, and the pass is done";
        tally.Print(say);

        Assert.Contains(say.Said, l => l.Contains("3 increment(s)", StringComparison.Ordinal));
        Assert.Contains(say.Said, l => l.Contains("moved    AER-1", StringComparison.Ordinal));
        Assert.Contains(say.Said, l => l.Contains("stalled  AER-2", StringComparison.Ordinal));
        Assert.Contains(say.Said, l => l.Contains("usage    AER-3", StringComparison.Ordinal));
    }

    /// <summary>HA-169: a sixth list, and not the stalled one.</summary>
    [Fact]
    public void A_preempted_ticket_is_its_own_list_and_not_the_stalled_one()
    {
        var say = new Transcript();
        var tally = new Tally(TimeProvider.System);

        tally.Record(Report("AER-1", exit: 143, moved: false, preempted: true));
        tally.StopWhy = "--once, and the pass is done";
        tally.Print(say);

        Assert.Contains(say.Said, l => l.Contains("preempt  AER-1", StringComparison.Ordinal));
        Assert.DoesNotContain(say.Said, l => l.Contains("stalled  AER-1", StringComparison.Ordinal));
    }

    /// <summary>
    /// HA-118: a ticket let go of quietly - the first increment in a row to
    /// leave it where it found it - is neither moved nor stalled: it is its
    /// own morning, so the next pass knows it is free rather than flagged.
    /// </summary>
    [Fact]
    public void A_let_go_ticket_is_its_own_list_and_not_the_stalled_one()
    {
        var say = new Transcript();
        var tally = new Tally(TimeProvider.System);

        tally.Record(Report("AER-1", moved: false, letGo: true));
        tally.StopWhy = "--once, and the pass is done";
        tally.Print(say);

        Assert.Contains(say.Said, l => l.Contains("let go   AER-1", StringComparison.Ordinal));
        Assert.DoesNotContain(say.Said, l => l.Contains("stalled  AER-1", StringComparison.Ordinal));
    }

    /// <summary>A session that exited zero without moving its ticket still does not count toward three failures in a row.</summary>
    [Fact]
    public void A_let_go_ticket_that_exited_zero_does_not_count_toward_the_failure_streak()
    {
        var tally = new Tally(TimeProvider.System);

        tally.Record(Report("AER-1", exit: 0, moved: false, letGo: true));
        tally.Record(Report("AER-2", exit: 0, moved: false, letGo: true));
        tally.Record(Report("AER-3", exit: 0, moved: false, letGo: true));

        Assert.False(tally.ShouldStop());
        Assert.Equal(0, tally.Fails);
    }

    /// <summary>A non-zero exit still counts toward the streak, whether or not the ticket was let go.</summary>
    [Fact]
    public void A_let_go_ticket_that_exited_non_zero_still_counts_toward_the_failure_streak()
    {
        var tally = new Tally(TimeProvider.System);

        tally.Record(Report("AER-1", exit: 1, moved: false, letGo: true));
        tally.Record(Report("AER-2", exit: 1, moved: false, letGo: true));
        tally.Record(Report("AER-3", exit: 1, moved: false, letGo: true));

        Assert.True(tally.ShouldStop());
        Assert.Equal(3, tally.Fails);
    }
}

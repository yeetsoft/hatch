using Microsoft.Extensions.Time.Testing;

namespace Hatch.Cli.Tests;

/// <summary>What a night stops for, and what it does not.</summary>
public sealed class TallyTests
{
    private static IncrementReport Report(
        string key = "AER-1", int exit = 0, decimal? cost = 1m, long? totalTokens = null, bool moved = true, bool lost = false,
        DateTimeOffset? usageLimitResetAt = null, bool letGo = false, bool preempted = false, int filed = 0,
        bool skipped = false, bool clamped = false, long? clampedAtTokens = null, int? clampedAtRequests = null,
        bool hardLimited = false, long? hardLimitedAtTokens = null, int? hardLimitedAtRequests = null) =>
        new()
        {
            Key = key,
            From = "In Progress",
            To = "In Review",
            Ended = moved ? "In Review" : "In Progress",
            Moved = moved,
            Stalled = !moved && filed == 0,
            LetGo = letGo,
            ExitCode = exit,
            Cost = cost,
            TotalTokens = totalTokens,
            LostLease = lost,
            UsageLimitResetAt = usageLimitResetAt,
            Preempted = preempted,
            PreemptedKey = preempted ? "AER-9" : null,
            PreemptedTitle = preempted ? "Trunk is down" : null,
            Filed = filed > 0 ? Enumerable.Range(1, filed).Select(n => $"AER-{10 + n}").ToList() : [],
            Skipped = skipped,
            ClampedAtTokens = clamped ? clampedAtTokens ?? 500_000L : null,
            ClampedAtRequests = clamped ? clampedAtRequests : null,
            HardLimitedAtTokens = hardLimited ? hardLimitedAtTokens ?? 7_500_000L : null,
            HardLimitedAtRequests = hardLimited ? hardLimitedAtRequests : null,
        };

    [Fact]
    public void A_tally_with_no_carried_state_is_fresh_and_one_carrying_state_is_not()
    {
        var clock = TimeProvider.System;

        Assert.True(new Tally(clock).Fresh);
        Assert.False(new Tally(clock, new NightState()).Fresh);
    }

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
    /// HA-133: a skip is the same story as a lost lease - the loop working
    /// correctly on an operator's own say-so, not a verdict on the increment,
    /// so it must neither arm nor clear the three-strikes streak.
    /// </summary>
    [Fact]
    public void Three_skips_in_a_row_do_not_stop_the_night()
    {
        var tally = new Tally(TimeProvider.System);

        tally.Record(Report("AER-1", exit: 143, skipped: true, moved: false));
        tally.Record(Report("AER-2", exit: 143, skipped: true, moved: false));
        tally.Record(Report("AER-3", exit: 143, skipped: true, moved: false));

        Assert.False(tally.ShouldStop());
        Assert.Equal(0, tally.Fails);
        Assert.Equal(3, tally.Runs);
    }

    /// <summary>A skip neither sets nor clears the fail streak, the same assertion the lost-lease test above makes.</summary>
    [Fact]
    public void A_skip_neither_sets_nor_clears_the_failure_streak()
    {
        var tally = new Tally(TimeProvider.System);

        tally.Record(Report("AER-1", exit: 1));
        Assert.Equal(1, tally.Fails);

        tally.Record(Report("AER-2", exit: 143, skipped: true, moved: false));
        Assert.Equal(1, tally.Fails);
    }

    /// <summary>
    /// Unlike a lost lease, a preemption or a usage limit, a skip is not its
    /// own morning - it goes through the ordinary bucketing and lands on the
    /// stalled list, reading "skipped from the keyboard".
    /// </summary>
    [Fact]
    public void A_skipped_ticket_lands_on_the_stalled_list()
    {
        var say = new Transcript();
        var tally = new Tally(TimeProvider.System);

        tally.Record(Report("AER-1", exit: 143, skipped: true, moved: false));
        tally.StopWhy = "--once, and the pass is done";
        tally.Print(say);

        Assert.Contains(say.Said, l => l.Contains("stalled  AER-1  skipped from the keyboard", StringComparison.Ordinal));
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

    /// <summary>
    /// HA-1107: the probe's own <c>resets_at</c> carries no zone of its own
    /// - usually <c>Z</c> - so <see cref="Tally.CorrectExhausted"/> converts it
    /// into the runner's own local zone as it replaces the carried instant,
    /// the same instant but read in the zone the wait prints in.
    /// </summary>
    [Fact]
    public void A_corrected_reset_instant_is_converted_into_the_runners_own_local_zone()
    {
        var clock = new FrozenClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(-4)));
        var tally = new Tally(clock);
        var carried = DateTimeOffset.Parse("2026-09-28T22:00:00Z");
        tally.Record(Report("AER-1", moved: false, usageLimitResetAt: carried));

        var probed = DateTimeOffset.Parse("2026-09-28T23:40:00Z");
        tally.CorrectExhausted(new UsageWindow("session", "Session", 1.0, probed));

        Assert.Equal(probed, tally.ExhaustedUntil);
        Assert.Equal(TimeSpan.FromHours(-4), tally.ExhaustedUntil!.Value.Offset);
        Assert.True(tally.ExhaustedKnown);
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

    /// <summary>HA-1457: an increment that changes ExhaustedUntil logs it, naming the report's own key.</summary>
    [Fact]
    public void Record_logs_when_it_changes_ExhaustedUntil()
    {
        var logged = new List<string>();
        var tally = new Tally(TimeProvider.System, log: logged.Add);
        var resetAt = DateTimeOffset.UtcNow;

        tally.Record(Report("AER-1", moved: false, usageLimitResetAt: resetAt));

        Assert.Contains(logged, l => l.Contains("Tally.Record", StringComparison.Ordinal) && l.Contains("AER-1", StringComparison.Ordinal));
    }

    /// <summary>HA-1457: an increment that worked, with nothing already exhausted, leaves nothing to say.</summary>
    [Fact]
    public void Record_does_not_log_when_ExhaustedUntil_is_unchanged()
    {
        var logged = new List<string>();
        var tally = new Tally(TimeProvider.System, log: logged.Add);

        tally.Record(Report("AER-1", moved: true));

        Assert.DoesNotContain(logged, l => l.Contains("Tally.Record", StringComparison.Ordinal));
    }

    /// <summary>HA-1457: every branch of CorrectExhausted names itself on the way through.</summary>
    [Fact]
    public void CorrectExhausted_logs_the_branch_that_clears_it()
    {
        var logged = new List<string>();
        var tally = new Tally(TimeProvider.System, log: logged.Add);
        tally.Record(Report("AER-1", moved: false, usageLimitResetAt: DateTimeOffset.UtcNow));

        tally.CorrectExhausted(new UsageWindow("session", "Session", 0.5, DateTimeOffset.UtcNow));

        Assert.Null(tally.ExhaustedUntil);
        Assert.Contains(logged, l => l.Contains("Tally.CorrectExhausted", StringComparison.Ordinal) && l.Contains("clearing", StringComparison.Ordinal));
    }

    [Fact]
    public void CorrectExhausted_logs_the_branch_that_corrects_it()
    {
        var logged = new List<string>();
        var tally = new Tally(TimeProvider.System, log: logged.Add);
        var carried = DateTimeOffset.Parse("2026-09-28T22:00:00Z");
        tally.Record(Report("AER-1", moved: false, usageLimitResetAt: carried));

        var probed = DateTimeOffset.Parse("2026-09-28T23:40:00Z");
        tally.CorrectExhausted(new UsageWindow("session", "Session", 1.0, probed));

        Assert.Contains(logged, l => l.Contains("Tally.CorrectExhausted", StringComparison.Ordinal) && l.Contains("correcting", StringComparison.Ordinal));
    }

    [Fact]
    public void CorrectExhausted_logs_once_when_the_sessions_own_window_carries_no_reset_time()
    {
        var logged = new List<string>();
        var tally = new Tally(TimeProvider.System, log: logged.Add);
        tally.Record(Report("AER-1", moved: false, usageLimitResetAt: DateTimeOffset.UtcNow));

        tally.CorrectExhausted(new UsageWindow("session", "Session", 1.0, null));

        Assert.Contains(logged, l => l.Contains("Tally.CorrectExhausted", StringComparison.Ordinal) && l.Contains("no reset time", StringComparison.Ordinal));
    }

    /// <summary>HA-1457: the backstop logs when it actually fires, naming itself rather than ClearExhausted.</summary>
    [Fact]
    public void ClampExhausted_logs_when_it_clears_a_stale_wait()
    {
        var logged = new List<string>();
        var start = DateTimeOffset.UtcNow;
        var clock = new FrozenClock(start);
        var tally = new Tally(clock, log: logged.Add);
        tally.Record(Report("AER-1", moved: false, usageLimitResetAt: start.AddMinutes(1)));

        tally.ClampExhausted(start + UsageLimit.WindowLengths["session"] + TimeSpan.FromMinutes(1));

        Assert.Null(tally.ExhaustedUntil);
        Assert.Contains(logged, l => l.Contains("Tally.ClampExhausted", StringComparison.Ordinal));
        Assert.Contains(logged, l => l.Contains("Tally.ClearExhausted", StringComparison.Ordinal));
    }

    /// <summary>HA-1457: a direct ClearExhausted call names itself when it actually clears something.</summary>
    [Fact]
    public void ClearExhausted_logs_when_it_clears_a_non_null_wait()
    {
        var logged = new List<string>();
        var tally = new Tally(TimeProvider.System, log: logged.Add);
        tally.Record(Report("AER-1", moved: false, usageLimitResetAt: DateTimeOffset.UtcNow));
        logged.Clear();

        tally.ClearExhausted();

        Assert.Contains(logged, l => l.Contains("Tally.ClearExhausted", StringComparison.Ordinal));
    }

    [Fact]
    public void ClearExhausted_says_nothing_when_there_was_nothing_to_clear()
    {
        var logged = new List<string>();
        var tally = new Tally(TimeProvider.System, log: logged.Add);

        tally.ClearExhausted();

        Assert.Empty(logged);
    }

    [Fact]
    public void A_keyboard_asking_to_stop_ends_the_night_and_names_the_keyboard()
    {
        var controls = new Controls(keysOn: true);
        controls.Press('s');
        var tally = new Tally(TimeProvider.System) { Controls = controls };

        Assert.True(tally.ShouldStop());
        Assert.Contains("keyboard", tally.StopWhy!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fresh_keyboard_that_has_pressed_nothing_does_not_stop_the_night()
    {
        var tally = new Tally(TimeProvider.System) { Controls = new Controls(keysOn: true) };

        Assert.False(tally.ShouldStop());
    }

    [Fact]
    public void Max_runs_and_max_spend_are_both_read_before_the_next_increment()
    {
        var runs = new Tally(TimeProvider.System) { MaxRuns = 2, Controls = new Controls(keysOn: true) };
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
    public void An_unpriced_increment_is_counted_but_never_against_max_spend()
    {
        var tally = new Tally(TimeProvider.System) { MaxSpend = 3m };

        tally.Record(Report(cost: 2m));
        tally.Record(Report(cost: null, totalTokens: 90_000));

        Assert.Equal(2m, tally.Spent);
        Assert.Equal(1, tally.Unpriced);
        Assert.False(tally.ShouldStop());
    }

    [Fact]
    public void An_increment_with_neither_tokens_nor_cost_is_not_reported_and_not_unpriced()
    {
        var tally = new Tally(TimeProvider.System);

        tally.Record(Report(cost: null, totalTokens: null));

        Assert.Equal(0, tally.Unpriced);
    }

    [Fact]
    public void The_tally_says_once_that_unpriced_increments_are_outside_the_dollars_and_the_cap()
    {
        var say = new Transcript();
        var tally = new Tally(TimeProvider.System) { MaxSpend = 3m };
        for (var i = 0; i < 3; i++) tally.Record(Report(cost: null, totalTokens: 1_000));
        tally.StopWhy = "--once, and the pass is done";
        tally.Print(say);

        var line = Assert.Single(say.Said, l => l.Contains("reported no price", StringComparison.Ordinal));
        Assert.Contains("3 increment(s) reported no price", line, StringComparison.Ordinal);
        Assert.Contains("--max-spend counts only priced increments", line, StringComparison.Ordinal);
        Assert.Contains(say.Said, l => l.Contains("3 unpriced", StringComparison.Ordinal) && l.Contains("$0", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_a_max_spend_the_unpriced_line_does_not_mention_one()
    {
        var say = new Transcript();
        var tally = new Tally(TimeProvider.System);
        tally.Record(Report(cost: null, totalTokens: 1_000));
        tally.Print(say);

        var line = Assert.Single(say.Said, l => l.Contains("reported no price", StringComparison.Ordinal));
        Assert.DoesNotContain("--max-spend", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_night_with_nothing_unpriced_says_nothing_of_it()
    {
        var say = new Transcript();
        var tally = new Tally(TimeProvider.System) { MaxSpend = 3m };
        tally.Record(Report(cost: 1m, totalTokens: 1_000));
        tally.Print(say);

        Assert.DoesNotContain(say.Said, l => l.Contains("unpriced", StringComparison.Ordinal) || l.Contains("reported no price", StringComparison.Ordinal));
    }

    [Fact]
    public void The_unpriced_count_survives_a_restart()
    {
        var first = new Tally(TimeProvider.System);
        first.Record(Report(cost: null, totalTokens: 1_000));
        first.Record(Report(cost: null, totalTokens: 2_000));

        var second = new Tally(TimeProvider.System, first.ToState());

        Assert.Equal(2, second.Unpriced);
        Assert.Contains("2 unpriced", second.SoFar(), StringComparison.Ordinal);
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
    public void A_stop_file_in_the_sets_directory_is_noticed_and_only_there()
    {
        var dir = Directory.CreateTempSubdirectory("hatch-set-");
        try
        {
            var inSet = new Tally(TimeProvider.System) { SetDir = dir.FullName };
            var alone = new Tally(TimeProvider.System);

            Assert.False(inSet.ShouldStop());
            File.WriteAllText(Path.Combine(dir.FullName, "stop"), "");

            Assert.True(inSet.ShouldStop());
            Assert.Equal("this runner's set was told to stop", inSet.StopWhy);
            Assert.False(alone.ShouldStop());
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private static (Tally First, Tally Second, DirectoryInfo Dir) Pair(int? maxRuns = null, decimal? maxSpend = null)
    {
        var dir = Directory.CreateTempSubdirectory("hatch-set-");
        return (
            new Tally(TimeProvider.System) { SetDir = dir.FullName, MaxRuns = maxRuns, MaxSpend = maxSpend },
            new Tally(TimeProvider.System) { SetDir = dir.FullName, Slot = 2, MaxRuns = maxRuns, MaxSpend = maxSpend },
            dir);
    }

    [Fact]
    public void Two_members_sharing_a_run_cap_each_stop_at_the_combined_count()
    {
        var (first, second, dir) = Pair(maxRuns: 3);
        try
        {
            first.Record(Report());
            first.Record(Report());
            Assert.False(first.ShouldStop());
            Assert.False(second.ShouldStop());

            second.Record(Report());

            Assert.True(first.ShouldStop());
            Assert.True(second.ShouldStop());
            Assert.Equal("--max-runs 3 reached across the set at 3", first.StopWhy);
            Assert.Contains("across the set", second.StopWhy, StringComparison.Ordinal);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Two_members_sharing_a_budget_each_stop_at_the_combined_spend()
    {
        var (first, second, dir) = Pair(maxSpend: 5m);
        try
        {
            first.Record(Report(cost: 2m));
            second.Record(Report(cost: 2m));
            Assert.False(first.ShouldStop());
            Assert.False(second.ShouldStop());

            first.Record(Report(cost: 2m));

            Assert.True(first.ShouldStop());
            Assert.True(second.ShouldStop());
            Assert.Equal("--max-spend 5 reached across the set at $6.00", second.StopWhy);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void A_lone_runner_keeps_its_own_bounds_and_their_words()
    {
        var tally = new Tally(TimeProvider.System) { MaxRuns = 3 };
        tally.Record(Report());
        tally.Record(Report());
        tally.Record(Report());

        Assert.True(tally.ShouldStop());
        Assert.Equal("--max-runs 3 reached", tally.StopWhy);
    }

    [Fact]
    public void A_garbage_ledger_is_ignored_and_a_members_own_file_may_be_absent()
    {
        var dir = Directory.CreateTempSubdirectory("hatch-set-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "ledger.9"), "garbage");
            var tally = new Tally(TimeProvider.System) { SetDir = dir.FullName, MaxRuns = 1, MaxSpend = 1m };

            Assert.False(tally.ShouldStop());
            Assert.False(File.Exists(Path.Combine(dir.FullName, "ledger.1")));

            tally.Record(Report());

            Assert.True(File.Exists(Path.Combine(dir.FullName, "ledger.1")));
            Assert.True(tally.ShouldStop());
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

    /// <summary>HA-127: a ticket that filed work under it lands on the moved list, not the stalled one.</summary>
    [Fact]
    public void A_filed_ticket_lands_on_the_moved_list_and_reads_like_any_other_exit_zero_run()
    {
        var say = new Transcript();
        var tally = new Tally(TimeProvider.System);

        tally.Record(Report("AER-1", exit: 1));
        tally.Record(Report("AER-2", exit: 1));
        tally.Record(Report("AER-3", exit: 0, moved: false, filed: 2));
        tally.Record(Report("AER-4", exit: 1));
        tally.Print(say);

        // Like any other exit-0 run, it clears the fail streak.
        Assert.False(tally.ShouldStop());
        Assert.Contains(say.Said, l => l.Contains("moved    AER-3  filed 2 under it", StringComparison.Ordinal));
        Assert.DoesNotContain(say.Said, l => l.Contains("stalled  AER-3", StringComparison.Ordinal));
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

    /// <summary>HA-224: a clamp that fired on a ticket left where it found it, with tasks filed under it, is its own list - not stalled.</summary>
    [Fact]
    public void A_clamped_and_filed_but_unmoved_ticket_lands_on_the_clamped_list_and_not_the_stalled_one()
    {
        var say = new Transcript();
        var tally = new Tally(TimeProvider.System);

        tally.Record(Report("AER-1", moved: false, filed: 2, clamped: true, clampedAtTokens: 820_000));
        tally.StopWhy = "--once, and the pass is done";
        tally.Print(say);

        Assert.Contains(say.Said, l => l.Contains("clamped  AER-1", StringComparison.Ordinal) && l.Contains("clamped at", StringComparison.Ordinal));
        Assert.DoesNotContain(say.Said, l => l.Contains("stalled  AER-1", StringComparison.Ordinal));
    }

    /// <summary>HA-224: a clamp firing on a ticket that still moved is reported as an ordinary move - the clamp is incidental.</summary>
    [Fact]
    public void A_clamped_ticket_that_moved_lands_on_the_moved_list_like_any_other_move()
    {
        var say = new Transcript();
        var tally = new Tally(TimeProvider.System);

        tally.Record(Report("AER-1", moved: true, clamped: true, clampedAtTokens: 820_000));
        tally.StopWhy = "--once, and the pass is done";
        tally.Print(say);

        Assert.Contains(say.Said, l => l.Contains("moved    AER-1", StringComparison.Ordinal));
        Assert.DoesNotContain(say.Said, l => l.Contains("clamped  AER-1", StringComparison.Ordinal));
    }

    /// <summary>
    /// HA-337: a hard limit is reported *clamped*, never a stall and never one
    /// of the three failures that end a night - even though the killed CLI
    /// process exits non-zero, the one thing this task's own brief warns is
    /// the trap.
    /// </summary>
    [Fact]
    public void A_hard_limited_report_lands_on_the_clamped_list_and_not_stalled_or_failed()
    {
        var say = new Transcript();
        var tally = new Tally(TimeProvider.System);

        tally.Record(Report("AER-1", exit: 1, moved: false, hardLimited: true));
        tally.StopWhy = "--once, and the pass is done";
        tally.Print(say);

        Assert.Contains(say.Said, l => l.Contains("clamped  AER-1", StringComparison.Ordinal));
        Assert.DoesNotContain(say.Said, l => l.Contains("stalled  AER-1", StringComparison.Ordinal));
        Assert.Equal(0, tally.Fails);
        Assert.False(tally.ShouldStop());
    }

    /// <summary>A hard limit does not arm the three-strikes stop, exactly like a lost lease or a skip.</summary>
    [Fact]
    public void Three_hard_limits_in_a_row_do_not_end_the_night()
    {
        var tally = new Tally(TimeProvider.System);

        tally.Record(Report("AER-1", exit: 1, moved: false, hardLimited: true));
        tally.Record(Report("AER-2", exit: 1, moved: false, hardLimited: true));
        tally.Record(Report("AER-3", exit: 1, moved: false, hardLimited: true));

        Assert.False(tally.ShouldStop());
        Assert.Equal(0, tally.Fails);
    }
}

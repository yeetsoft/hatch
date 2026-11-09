using Microsoft.Extensions.Time.Testing;

namespace Hatch.Cli.Tests;

/// <summary>The models a runner has stopped offering, and what is said of one.</summary>
public sealed class ModelNarrowingTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 2, 0, 0, TimeSpan.Zero);

    private static readonly string[] Aliases = ["haiku", "sonnet", "opus", "fable"];

    [Fact]
    public void Narrowing_is_news_once_and_again_only_when_the_instant_changes()
    {
        var narrowing = new ModelNarrowing();
        var until = Now.AddHours(1);

        Assert.True(narrowing.Narrow("opus", until, Now));
        Assert.False(narrowing.Narrow("opus", until, Now));
        Assert.True(narrowing.Narrow("opus", until.AddHours(1), Now));
        Assert.True(narrowing.Narrow("opus", null, Now));
        Assert.False(narrowing.Narrow("opus", null, Now));
    }

    [Fact]
    public void An_instant_already_past_narrows_nothing()
    {
        var narrowing = new ModelNarrowing();

        Assert.False(narrowing.Narrow("opus", Now, Now));
        Assert.False(narrowing.Narrow("opus", Now.AddMinutes(-1), Now));
        Assert.Equal(Aliases, narrowing.Offered(Aliases, Now));
    }

    [Fact]
    public void A_narrowed_model_is_dropped_and_the_order_of_the_rest_is_kept()
    {
        var narrowing = new ModelNarrowing();
        narrowing.Narrow("sonnet", Now.AddHours(1), Now);

        Assert.Equal(["haiku", "opus", "fable"], narrowing.Offered(Aliases, Now));
    }

    [Fact]
    public void A_model_matches_whatever_its_case()
    {
        var narrowing = new ModelNarrowing();
        narrowing.Narrow("Opus", null, Now);

        Assert.Equal(["haiku", "sonnet", "fable"], narrowing.Offered(Aliases, Now));
        Assert.False(narrowing.Narrow("OPUS", null, Now));
    }

    [Fact]
    public void A_model_is_back_once_the_instant_passes()
    {
        var narrowing = new ModelNarrowing();
        var until = Now.AddHours(1);
        narrowing.Narrow("opus", until, Now);

        Assert.DoesNotContain("opus", narrowing.Offered(Aliases, until.AddTicks(-1)));
        Assert.Contains("opus", narrowing.Offered(Aliases, until));
    }

    [Fact]
    public void A_narrowing_for_the_rest_of_the_run_never_expires()
    {
        var narrowing = new ModelNarrowing();
        narrowing.Narrow("opus", null, Now);

        Assert.DoesNotContain("opus", narrowing.Offered(Aliases, Now.AddYears(10)));
    }

    [Fact]
    public void Every_model_narrowed_leaves_an_empty_list()
    {
        var narrowing = new ModelNarrowing();
        foreach (var model in Aliases) narrowing.Narrow(model, null, Now);

        Assert.Empty(narrowing.Offered(Aliases, Now));
    }

    [Fact]
    public void A_refusal_names_the_model_the_agent_and_the_time_in_the_clocks_own_zone()
    {
        var narrowing = new ModelNarrowing();
        var clock = new ZoneClock(Now, TimeSpan.FromHours(-7));
        narrowing.Narrow("opus", new DateTimeOffset(2026, 9, 8, 3, 40, 0, TimeSpan.Zero), Now);

        Assert.Equal(
            "runs on opus - this runner's claude no longer offers it until 8:40pm",
            narrowing.Refusal("opus", "claude", Now, clock));
    }

    [Fact]
    public void A_refusal_for_the_rest_of_the_run_says_so()
    {
        var narrowing = new ModelNarrowing();
        narrowing.Narrow("opus", null, Now);

        Assert.Equal(
            "runs on opus - this runner's claude no longer offers it for the rest of this run",
            narrowing.Refusal("opus", "claude", Now.AddDays(1), new FakeTimeProvider(Now)));
    }

    [Fact]
    public void There_is_no_refusal_for_a_model_that_is_not_narrowed_or_not_named_or_has_come_back()
    {
        var narrowing = new ModelNarrowing();
        var clock = new FakeTimeProvider(Now);
        narrowing.Narrow("opus", Now.AddHours(1), Now);

        Assert.Null(narrowing.Refusal(null, "claude", Now, clock));
        Assert.Null(narrowing.Refusal("sonnet", "claude", Now, clock));
        Assert.Null(narrowing.Refusal("opus", "claude", Now.AddHours(1), clock));
    }

    [Fact]
    public void What_is_in_force_goes_to_the_state_and_comes_back_into_a_fresh_store()
    {
        var narrowing = new ModelNarrowing();
        narrowing.Narrow("opus", Now.AddHours(1), Now);
        narrowing.Narrow("haiku", null, Now);

        var state = narrowing.ToState(Now);
        var back = new ModelNarrowing();
        back.Load(state, Now);

        Assert.Equal(2, state.Count);
        Assert.Equal(["sonnet", "fable"], back.Offered(Aliases, Now));
        Assert.Equal(state, back.ToState(Now));
    }

    [Fact]
    public void The_state_leaves_out_what_has_run_out()
    {
        var narrowing = new ModelNarrowing();
        narrowing.Narrow("opus", Now.AddHours(1), Now);

        Assert.Empty(narrowing.ToState(Now.AddHours(1)));
    }

    [Fact]
    public void Loading_replaces_what_was_held_and_drops_what_has_run_out()
    {
        var narrowing = new ModelNarrowing();
        narrowing.Narrow("haiku", null, Now);

        narrowing.Load([new("opus", Now.AddHours(-1)), new("sonnet", Now.AddHours(1))], Now);

        Assert.Equal(["haiku", "opus", "fable"], narrowing.Offered(Aliases, Now));
    }

    [Fact]
    public void Loading_nothing_clears_it()
    {
        var narrowing = new ModelNarrowing();
        narrowing.Narrow("haiku", null, Now);

        narrowing.Load(null, Now);

        Assert.Equal(Aliases, narrowing.Offered(Aliases, Now));
    }

    private sealed class ZoneClock(DateTimeOffset now, TimeSpan offset) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("test", offset, "test", "test");
    }
}

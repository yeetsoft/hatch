namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="RunnerForge.Worst"/>, the rule the loop and the board share for
/// folding several forge answers into one.
/// </summary>
public class RunnerForgeTests
{
    [Fact]
    public void Worst_reads_fails_before_works_with_the_reason_of_the_first_failure()
    {
        Assert.Equal(("fails", "first"), RunnerForge.Worst([("works", null), ("fails", "first"), ("fails", "second")]));
        Assert.Equal(("works", null), RunnerForge.Worst([("works", null), ("works", null)]));
    }

    [Fact]
    public void Worst_is_null_when_none_said_and_skips_a_word_it_does_not_know()
    {
        Assert.Null(RunnerForge.Worst([]));
        Assert.Null(RunnerForge.Worst([(null, null), ("maybe", "x")]));
        Assert.Equal(("works", null), RunnerForge.Worst([("maybe", "x"), ("works", "stale")]));
    }

    [Fact]
    public void Worst_keeps_a_reason_only_beside_fails()
    {
        Assert.Equal(("works", null), RunnerForge.Worst([("works", "stale")]));
        Assert.Equal(("fails", null), RunnerForge.Worst([("fails", null)]));
    }

    [Fact]
    public void Off_is_no_answer_for_the_forge()
    {
        Assert.Null(RunnerForge.Worst([("off", null)]));
    }
}

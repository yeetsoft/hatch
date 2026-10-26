using System.Text.Json;
using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="LetGo.By"/> over hand-built trails, newest first: which runner
/// let a ticket go last, and what clears the record.
/// </summary>
public class LetGoTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    private static (string Kind, string? Payload, DateTimeOffset At) Release(string? outcome, string? runner, int minutesAgo)
    {
        var payload = new Dictionary<string, object?> { ["from"] = "someone on somewhere", ["to"] = null };
        if (outcome is not null) payload["outcome"] = outcome;
        if (runner is not null) payload["runner"] = runner;
        return (EfHatchIssueEvent.ClaimReleased, JsonSerializer.Serialize(payload), At.AddMinutes(-minutesAgo));
    }

    private static (string Kind, string? Payload, DateTimeOffset At) Other(string kind, string? payload, int minutesAgo) =>
        (kind, payload, At.AddMinutes(-minutesAgo));

    [Fact]
    public void ADroppedReleaseThatNamesItsRunner_IsTheRecord()
    {
        var runner = $"runner-{Guid.NewGuid():N}";

        var by = LetGo.By([Release(ClaimOutcomes.Dropped, runner, 5)]);

        Assert.Equal(new LetGoBy(runner, At.AddMinutes(-5)), by);
    }

    [Fact]
    public void ATrailWithNoReleaseAtAll_RecordsNothing() =>
        Assert.Null(LetGo.By([]));

    [Fact]
    public void ADropWrittenBeforeThePayloadNamedTheRunner_RecordsNothing() =>
        Assert.Null(LetGo.By([Release(ClaimOutcomes.Dropped, null, 5)]));

    [Theory]
    [InlineData(ClaimOutcomes.Worked)]
    [InlineData(ClaimOutcomes.Preempted)]
    [InlineData(null)]
    public void ANewerReleaseOfAnyOtherOutcome_ClearsTheRecord(string? outcome)
    {
        var runner = $"runner-{Guid.NewGuid():N}";

        Assert.Null(LetGo.By([Release(outcome, runner, 1), Release(ClaimOutcomes.Dropped, runner, 5)]));
    }

    [Fact]
    public void ANewerStatusChange_ClearsTheRecord()
    {
        var runner = $"runner-{Guid.NewGuid():N}";

        Assert.Null(LetGo.By([
            Other(EfHatchIssueEvent.StatusChanged, "{}", 1),
            Release(ClaimOutcomes.Dropped, runner, 5)]));
    }

    [Fact]
    public void ANewerAnswerFromAPerson_ClearsTheRecord_ButALapsesDoesNot()
    {
        var runner = $"runner-{Guid.NewGuid():N}";
        var drop = Release(ClaimOutcomes.Dropped, runner, 5);

        Assert.Null(LetGo.By([Other(EfHatchIssueEvent.Answered, "{\"questionId\":1}", 1), drop]));
        Assert.NotNull(LetGo.By([Other(EfHatchIssueEvent.Answered, "{\"questionId\":1,\"lapsed\":true}", 1), drop]));
    }

    [Fact]
    public void TakesLapsesClearsAndComments_AreSteppedOver()
    {
        var runner = $"runner-{Guid.NewGuid():N}";

        var by = LetGo.By([
            Other(EfHatchIssueEvent.ClaimTaken, "{}", 1),
            Other(EfHatchIssueEvent.ClaimLapsed, "{}", 2),
            Other(EfHatchIssueEvent.ClaimCleared, "{}", 3),
            Other(EfHatchIssueEvent.Commented, "{}", 4),
            Release(ClaimOutcomes.Dropped, runner, 5)]);

        Assert.Equal(runner, by?.Runner);
    }

    [Fact]
    public void TheNewestDrop_IsTheRecord_WhoeverLetGoBefore()
    {
        var first = $"runner-{Guid.NewGuid():N}";
        var second = $"runner-{Guid.NewGuid():N}";

        var by = LetGo.By([Release(ClaimOutcomes.Dropped, second, 1), Release(ClaimOutcomes.Dropped, first, 5)]);

        Assert.Equal(second, by?.Runner);
    }
}

namespace Hatch.Api.Tests;

/// <summary>
/// <see cref="Deadline"/> on its own, with no test host and no Postgres -
/// Done's first bullet, that a blown budget names the phase and fails fast
/// rather than waiting out a real-world timeout.
/// </summary>
public class DeadlineTests
{
    [Fact]
    public async Task WorkThatNeverCompletes_FailsNamingThePhase()
    {
        var deadline = new Deadline(TimeSpan.FromMilliseconds(20), () => "");
        var neverCompletes = new TaskCompletionSource<int>();

        var ex = await Assert.ThrowsAsync<DeadlineExceededException>(
            () => deadline.BoundAsync("the phase that never returns", () => neverCompletes.Task));

        Assert.Contains("the phase that never returns", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkThatCompletesInTime_ReturnsItsValue()
    {
        var deadline = new Deadline(TimeSpan.FromSeconds(10), () => "");

        var result = await deadline.BoundAsync("the phase that finishes", () => Task.FromResult(42));

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task BudgetAlreadySpent_FailsImmediatelyWithoutRunningTheWork()
    {
        var deadline = new Deadline(TimeSpan.FromMilliseconds(1), () => "");
        await Task.Delay(TimeSpan.FromMilliseconds(50));

        var ran = false;

        var ex = await Assert.ThrowsAsync<DeadlineExceededException>(
            () => deadline.BoundAsync("the phase that never gets to run", () =>
            {
                ran = true;
                return Task.FromResult(0);
            }));

        Assert.False(ran);
        Assert.Contains("the phase that never gets to run", ex.Message, StringComparison.Ordinal);
    }
}

using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The rule from a cadence and a last period to the next due date - pure and
/// synchronous, so every case here is hand-built values and no database.
/// </summary>
public class ReflectionCadenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NeverReflected_IsDueFromNow()
    {
        var due = ReflectionCadence.NextDue(7, null, Now);

        Assert.Equal(Now.AddDays(7), due);
    }

    [Fact]
    public void CadenceOff_HasNoNextDue()
    {
        var due = ReflectionCadence.NextDue(null, Now.AddDays(-30), Now);

        Assert.Null(due);
        Assert.False(ReflectionCadence.IsDue(null, Now.AddDays(-30), Now));
    }

    [Fact]
    public void PastDue_IsDue()
    {
        var lastPeriodStart = Now.AddDays(-10);

        Assert.True(ReflectionCadence.IsDue(7, lastPeriodStart, Now));
        Assert.Equal(lastPeriodStart.AddDays(7), ReflectionCadence.NextDue(7, lastPeriodStart, Now));
    }

    [Fact]
    public void NotYetDue_IsNotDue()
    {
        var lastPeriodStart = Now.AddDays(-3);

        Assert.False(ReflectionCadence.IsDue(7, lastPeriodStart, Now));
        Assert.Equal(lastPeriodStart.AddDays(7), ReflectionCadence.NextDue(7, lastPeriodStart, Now));
    }
}

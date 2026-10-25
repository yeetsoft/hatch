using Hatch.Api.Tests.Hatch;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// The phone profile's smoke: the board, rendered in headless WebKit at
/// <c>iPhone 14 Pro</c>, where the phone breakpoint takes the stacked
/// <c>details</c> branch (<c>lib/phoneBoard.ts</c> <c>readBoardView</c>)
/// instead of the columns <see cref="BoardRendersTests"/> renders. The case
/// the bar measurement and HA-1228 build on. Skipped where WebKit is not
/// installed, the same as <see cref="BoardRendersTests"/> is for Chromium.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class BoardRendersPhoneTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 60;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheBoard_RendersAtThePhoneProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);

        var harness = new E2eHarness("BoardRendersPhone");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline, DeviceProfile.IPhone14Pro);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);

            await harness.OpenBoardAsync(deadline, baseUrl);
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForFirstColumnAsync(deadline);

            // Printed, not asserted: what the bar looks like at the phone profile is read before it is judged.
            var bar = await harness.MeasureBarAsync(deadline);
            output.WriteLine(bar.ToString());

            // The board's own bar, which the measurement above does not reach: every trailing control, Go to Work
            // among them, must sit inside the viewport, and Go must be a finger's height. A scratch database has no runner,
            // so the button draws disabled - and that is the width that matters.
            var board = await harness.MeasureBoardBarAsync(deadline);
            output.WriteLine(board.ToString());

            Assert.True(board.ScrollWidth <= board.InnerWidth, $"the board scrolls sideways:{Environment.NewLine}{board}");
            Assert.True(board.CoarsePointerMatches, $"the profile has no coarse pointer:{Environment.NewLine}{board}");
            var go = Assert.Single(board.Trailing, t => t.Name == "span \"Go\"");
            Assert.True(go.Box.Height >= 44, $"Go is under 44px high:{Environment.NewLine}{board}");
            foreach (var (name, box) in board.Trailing)
                Assert.True(box.Right <= board.InnerWidth, $"{name} runs off the right edge:{Environment.NewLine}{board}");

            harness.AssertNoPageErrors();
        }
        catch (Exception e) when (e is not Xunit.SkipException)
        {
            harness.Fail();
            throw;
        }
        finally
        {
            await harness.DisposeAsync();
        }
    }
}

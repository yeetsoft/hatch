using Hatch.Api.Tests.Hatch;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// The desktop counterpart of <see cref="BoardRendersPhoneTests"/>: the board
/// in headless Chromium at 1150 CSS px, the width of a laptop with the editor
/// beside it, where the filter row has to draw on one line with nothing
/// scrolling sideways. A scratch database has one project and no runner, so
/// this is the regression guard; the full condition (a project chosen, a loop
/// working, a filter set) is read in the driver and recorded on the ticket.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class BoardFitsDesktopTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 60;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    /// <summary>How far one control's top may sit from the search box's before it counts as on another line; a row is 36px high.</summary>
    private const double SameLineTolerance = 8;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheBoardFilterRow_FitsOneLineAt1150()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("BoardFitsDesktop");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);

            await harness.OpenBoardAsync(deadline, baseUrl);
            await harness.SetViewportAsync(deadline, 1150, 800);
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForFirstColumnAsync(deadline);

            var board = await harness.MeasureBoardBarAsync(deadline);
            output.WriteLine(board.ToString());

            Assert.Equal(1150, board.InnerWidth);
            Assert.True(board.ScrollWidth <= board.InnerWidth, $"the board scrolls sideways:{Environment.NewLine}{board}");

            var search = board.Search;
            Assert.True(search is not null, $"the search box is not on the page:{Environment.NewLine}{board}");
            Assert.NotEmpty(board.Trailing);
            foreach (var (name, box) in board.Trailing)
            {
                Assert.True(box.Right <= board.InnerWidth, $"{name} runs off the right edge:{Environment.NewLine}{board}");
                Assert.True(
                    Math.Abs(box.Y - search!.Y) <= SameLineTolerance,
                    $"{name} is not on the search box's line:{Environment.NewLine}{board}");
            }

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

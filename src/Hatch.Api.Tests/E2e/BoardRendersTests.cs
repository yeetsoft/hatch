using Hatch.Api.Tests.Hatch;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// The first case that opens a browser: the SPA's board, rendered from the
/// real API in headless Chromium. Nothing else in the repository would notice
/// a change that leaves the bundle building and the board blank.
///
/// <para>Needs a Postgres (<see cref="HatchDatabase"/>), Chromium
/// (<c>make browsers</c>) and the bundle built by hand
/// (<c>npm run build -w apps/hatch</c> in <c>src/Hatch.Web</c>) - CI's api job
/// has the first only, so it reports this case skipped until it grows the rest.
/// A missing bundle is a failure rather than a skip, because it is the one
/// thing here a developer running the case has no excuse for.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class BoardRendersTests
{
    /// <summary>
    /// The scratch pair, the host, a browser and two waits. Sized like
    /// <see cref="MinimalStartupTests"/>'s, which has the reasoning; nothing
    /// has measured this case against it yet.
    /// </summary>
    private const int CaseBudgetSeconds = 60;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheBoard_RendersItsTopBarAndFirstColumn()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("BoardRenders");
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
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForFirstColumnAsync(deadline);

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

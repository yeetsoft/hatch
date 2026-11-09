using Hatch.Api.Tests.Hatch;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// The Types panel's Only buttons at the phone profile, rendered in headless
/// WebKit at <c>iPhone 14 Pro</c>: each must be a finger's height and width
/// (<c>.hatch-types-only</c> carries neither <c>.hatch-btn</c> nor a bar
/// control's class, so App.css reaches it by its own rule), and the panel that
/// grows to take them must still sit inside the viewport. Skipped where WebKit
/// is not installed or is frozen, the same as <see cref="BoardRendersPhoneTests"/>.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class TypesOnlyFitsPhoneTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 60;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string FiltersSummary = "details.hatch-board-filters-more > summary";

    private const string TypesTrigger = "button.hatch-facet:has(.hatch-facet__label:text-is(\"Types\"))";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheTypesPanelsOnlyButtons_AreAFingerTallAtThePhoneProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);

        var harness = new E2eHarness("TypesOnlyFitsPhone");
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

            // The phone folds the filter bar into a details; the Types facet is inside it.
            await harness.ClickAsync(deadline, FiltersSummary);
            await harness.ClickAsync(deadline, TypesTrigger);
            await harness.WaitForVisibleAsync(deadline, E2eHarness.TypesOnlySelector);

            var types = await harness.MeasureTypesOnlyAsync(deadline);
            output.WriteLine(types.ToString());

            Assert.True(types.CoarsePointerMatches, $"the profile has no coarse pointer:{Environment.NewLine}{types}");
            Assert.True(types.Only.Count >= 2, $"fewer than two Only buttons were found, so the selector found something else:{Environment.NewLine}{types}");
            foreach (var (name, box) in types.Only)
            {
                Assert.True(box.Height >= 44, $"{name} is under 44px high:{Environment.NewLine}{types}");
                Assert.True(box.Width >= 44, $"{name} is under 44px wide:{Environment.NewLine}{types}");
            }

            Assert.NotNull(types.Panel);
            Assert.True(types.Panel!.Right <= types.InnerWidth, $"the Types panel runs off the right edge:{Environment.NewLine}{types}");
            Assert.True(types.ScrollWidth <= types.InnerWidth, $"the board scrolls sideways:{Environment.NewLine}{types}");

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

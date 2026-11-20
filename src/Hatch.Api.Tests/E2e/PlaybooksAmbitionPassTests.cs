using Hatch.Api.Tests.Hatch;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2523's acceptance, rendered: on a stock board the Playbooks page lists the
/// seeded ambition pass, a row typed <c>ambition</c> from the implementation
/// column to itself. Run against the real API and the seeded rows at the desktop
/// viewport in Chromium, with the look saved as a PNG beside the case's other
/// artifacts.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class PlaybooksAmbitionPassTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 120;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheSeededAmbitionPass_IsListedFromInProgressToInProgress_AtTheDesktopProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("PlaybooksAmbitionPass");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);

            await harness.OpenAsync(deadline, baseUrl, "/playbooks");
            await harness.WaitForVisibleAsync(deadline, "tbody tr");

            // The row typed ambition, and the two ends of its edge.
            var cells = await harness.EvaluateAsync<string[]>(
                deadline,
                "reading the ambition row",
                """
                () => {
                    const row = [...document.querySelectorAll('tbody tr')]
                        .find((r) => /ambition/i.test(r.querySelectorAll('td')[1]?.textContent ?? ''));
                    return row ? [...row.querySelectorAll('td')].map((td) => td.textContent.trim()) : [];
                }
                """);
            output.WriteLine($"ambition row: {string.Join(" | ", cells)}");
            Assert.NotEmpty(cells);
            Assert.Contains("In Progress", cells[0]);
            Assert.Contains("fable", cells[2]);
            Assert.Contains("max", cells[3]);
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, "playbooks-ambition-pass")}");

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

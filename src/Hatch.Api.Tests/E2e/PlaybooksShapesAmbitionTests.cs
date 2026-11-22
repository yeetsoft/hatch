using Hatch.Api.Tests.Hatch;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// The stock row that shapes an ambition, rendered: on a stock board the Playbooks page lists the
/// seeded shaping row, a row typed <c>ambition</c> from Breakdown to Backlog,
/// badged "shapes an ambition". Run against the
/// real API and the seeded rows at the desktop viewport in Chromium and at the
/// <c>iPhone 14 Pro</c> profile in WebKit, with each look saved as a PNG beside
/// the case's other artifacts.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class PlaybooksShapesAmbitionTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 120;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheSeededRow_IsListedFromBreakdownToBacklog_AtTheDesktopProfile() =>
        RunAsync("PlaybooksShapesAmbitionDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheSeededRow_IsListedFromBreakdownToBacklog_AtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("PlaybooksShapesAmbitionPhone", "phone", DeviceProfile.IPhone14Pro);
    }

    private async Task RunAsync(string name, string profileName, DeviceProfile? profile)
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness(name);
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline, profile);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);

            await harness.OpenAsync(deadline, baseUrl, "/playbooks");
            await harness.WaitForVisibleAsync(deadline, "tbody tr");
            await harness.WaitForVisibleAsync(deadline, "tbody tr:has-text(\"shapes an ambition\")");

            // The row badged "shapes an ambition", and its edge, type, model and effort.
            var cells = await harness.EvaluateAsync<string[]>(
                deadline,
                "reading the shapes-ambition row",
                """
                () => {
                    const row = [...document.querySelectorAll('tbody tr')]
                        .find((r) => /shapes an ambition/i.test(r.textContent ?? ''));
                    return row ? [...row.querySelectorAll('td')].map((td) => td.textContent.trim()) : [];
                }
                """);
            output.WriteLine($"shapes-ambition row: {string.Join(" | ", cells)}");
            Assert.NotEmpty(cells);
            Assert.Contains("Breakdown", cells[0]);
            Assert.Contains("Backlog", cells[0]);
            Assert.Contains("ambition", cells[1]);
            Assert.Contains("fable", cells[2]);
            Assert.Contains("max", cells[3]);
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"playbooks-shapes-ambition-{profileName}")}");

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

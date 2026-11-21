using Hatch.Api.Tests.Hatch;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2621's acceptance, rendered: on a stock board the Playbooks page lists the
/// seeded works-out-changes row, a row typed <c>ambition</c> from the
/// implementation column to itself, badged "works out changes". Run against the
/// real API and the seeded rows at the desktop viewport in Chromium and at the
/// <c>iPhone 14 Pro</c> profile in WebKit, with each look saved as a PNG beside
/// the case's other artifacts.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class PlaybooksWorksOutChangesTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 120;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheSeededRow_IsListedFromInProgressToInProgress_AtTheDesktopProfile() =>
        RunAsync("PlaybooksWorksOutChangesDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheSeededRow_IsListedFromInProgressToInProgress_AtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("PlaybooksWorksOutChangesPhone", "phone", DeviceProfile.IPhone14Pro);
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
            await harness.WaitForVisibleAsync(deadline, "tbody tr:has-text(\"works out changes\")");

            // The row badged "works out changes", and the two ends of its edge.
            var cells = await harness.EvaluateAsync<string[]>(
                deadline,
                "reading the works-out-changes row",
                """
                () => {
                    const row = [...document.querySelectorAll('tbody tr')]
                        .find((r) => /works out changes/i.test(r.textContent ?? ''));
                    return row ? [...row.querySelectorAll('td')].map((td) => td.textContent.trim()) : [];
                }
                """);
            output.WriteLine($"works-out-changes row: {string.Join(" | ", cells)}");
            Assert.NotEmpty(cells);
            Assert.Contains("In Progress", cells[0]);
            Assert.Contains("ambition", cells[1]);
            Assert.Contains("fable", cells[2]);
            Assert.Contains("max", cells[3]);
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"playbooks-works-out-changes-{profileName}")}");

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

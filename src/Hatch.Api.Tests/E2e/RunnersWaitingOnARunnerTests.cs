using Hatch.Api.Tests.Hatch;
using Microsoft.Playwright;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2279's acceptance, rendered: with an attention answer whose only
/// non-empty list is <c>WaitingOnARunner</c>, the Runners page shows the same
/// sentence <c>hatch queue</c> prints, as an alert above its list; with the
/// list empty it shows none. Run at the desktop viewport in Chromium and at the
/// <c>iPhone 14 Pro</c> profile in WebKit.
///
/// <para>A scratch database has no fleet that could be short of a toolchain, so
/// the attention read is fulfilled with <c>page.RouteAsync</c>, serialised from
/// the real record as <see cref="AttentionWaitingOnARunnerTests"/> does. Every
/// other route, and the bundle, is real.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class RunnersWaitingOnARunnerTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string BannerSelector = ".hatch-page .hatch-runner-note[role=alert]";

    private const string Sentence = "1 issue waits for a runner that has postgres 18 or newer";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly WaitingOnARunnerDto Row = new(
        "E2E-24", "A story that builds against postgres", "story", "in progress", "no live runner has postgres 18 or newer");

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task AWaitingRow_IsSaidAboveTheList_AtTheDesktopProfile() =>
        RunAsync("RunnersWaitingDesktop", "desktop", null, waiting: true);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task NothingWaiting_ShowsNoBanner_AtTheDesktopProfile() =>
        RunAsync("RunnersClearDesktop", "desktop", null, waiting: false);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task AWaitingRow_IsSaidAboveTheList_AtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("RunnersWaitingPhone", "phone", DeviceProfile.IPhone14Pro, waiting: true);
    }

    private async Task RunAsync(string name, string profileName, DeviceProfile? profile, bool waiting)
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

            await harness.OpenAsync(deadline, baseUrl, "/runners", context => StubAttention(context, waiting));
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForVisibleAsync(deadline, ".hatch-page h1:text-is(\"Runners\")");

            if (waiting)
            {
                await harness.WaitForVisibleAsync(deadline, $"{BannerSelector}:has-text(\"{Sentence}\")");
            }
            else
            {
                Assert.False(await harness.IsVisibleAsync(deadline, BannerSelector));
            }

            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"{(waiting ? "waiting" : "clear")}-{profileName}")}");

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

    /// <summary>The attention read with the one row, or with every list empty.</summary>
    private static Task StubAttention(IBrowserContext context, bool waiting)
    {
        var attention = new AttentionDto(
            Reviews: [],
            InReviewWithoutPullRequest: 0,
            Questions: [],
            Conflicts: [],
            WaitingOnARunner: waiting ? [Row] : []);

        return context.RouteAsync("**/api/attention", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(attention, Web),
        }));
    }
}

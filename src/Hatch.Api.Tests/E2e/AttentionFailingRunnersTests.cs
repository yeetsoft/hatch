using Hatch.Api.Tests.Hatch;
using Microsoft.Playwright;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2706's acceptance, rendered: with an attention answer whose only
/// non-empty list is <c>RunnerFailures</c>, the bar's attention control
/// lights and is named <c>1 runner is failing</c>, and its panel draws a
/// <em>Failing runners</em> section holding the row - the runner, its
/// checkout, what it cannot do, what the tool said and what to do about it -
/// linked to that runner's page. With the list empty, the control reads
/// <c>Clear</c> and the panel draws no such section. Run at the desktop
/// viewport in Chromium and at the <c>iPhone 14 Pro</c> profile in WebKit.
///
/// <para>Who is sent a failure is the server's rule and is pinned in
/// <c>AttentionControllerTests</c>; this case is about what the panel does with
/// the answer it is handed. The attention read is fulfilled with
/// <c>page.RouteAsync</c>, serialised from the real record so a renamed field
/// breaks this build rather than the panel. Every other route, and the bundle,
/// is real. Each look is saved as a PNG beside the case's other artifacts.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class AttentionFailingRunnersTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string PanelSelector = ".hatch-attention-panel";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly AttentionRunnerFailureDto Row = new(
        "build-box", "host:/checkouts/one", RunnerFailureKinds.Forge, "gh: token expired", DateTimeOffset.UtcNow.AddMinutes(-20));

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task AFailingRunner_LightsTheControlAndIsListed_AtTheDesktopProfile() =>
        RunAsync("AttentionFailingDesktop", "desktop", null, failing: true);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task NoFailingRunner_ReadsClearAndDrawsNoSection_AtTheDesktopProfile() =>
        RunAsync("AttentionNotFailingDesktop", "desktop", null, failing: false);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task AFailingRunner_LightsTheControlAndIsListed_AtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("AttentionFailingPhone", "phone", DeviceProfile.IPhone14Pro, failing: true);
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task NoFailingRunner_ReadsClearAndDrawsNoSection_AtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("AttentionNotFailingPhone", "phone", DeviceProfile.IPhone14Pro, failing: false);
    }

    private async Task RunAsync(string name, string profileName, DeviceProfile? profile, bool failing)
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

            await harness.OpenAsync(deadline, baseUrl, "/", context => StubAttention(context, failing));
            await harness.WaitForTopBarAsync(deadline);

            if (failing)
            {
                await harness.WaitForVisibleAsync(
                    deadline, $"{E2eHarness.AttentionSelector}.hatch-attention-asking[aria-label=\"1 runner is failing\"]");
                await harness.ClickAsync(deadline, E2eHarness.AttentionSelector);
                await harness.WaitForVisibleAsync(deadline, $"{PanelSelector} h3:text-is(\"Failing runners\")");
                await harness.WaitForVisibleAsync(
                    deadline,
                    $"{PanelSelector} a.hatch-attention-row[href=\"/runners/{Row.Name}\"]"
                    + $":has(.hatch-attention-key:text-is(\"{Row.Name}\")):has(.hatch-attention-title:text-is(\"{Row.Where}\"))"
                    + ":has-text(\"Cannot reach its forge since\"):has-text(\"gh said: gh: token expired\")"
                    + ":has-text(\"Sign gh in on that machine.\")");
            }
            else
            {
                await harness.WaitForVisibleAsync(deadline, $"{E2eHarness.AttentionSelector}.hatch-attention-rest .hatch-attention-word:text-is(\"Clear\")");
                await harness.ClickAsync(deadline, E2eHarness.AttentionSelector);
                await harness.WaitForVisibleAsync(deadline, $"{PanelSelector} h2:has-text(\"Human\")");
                Assert.False(await harness.IsVisibleAsync(deadline, $"{PanelSelector} h3:has-text(\"Failing runners\")"));
            }

            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"{(failing ? "failing" : "not-failing")}-{profileName}")}");

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

    /// <summary>The attention read with the one failing runner, or with every list empty.</summary>
    private static Task StubAttention(IBrowserContext context, bool failing)
    {
        var attention = new AttentionDto(
            Reviews: [],
            InReviewWithoutPullRequest: 0,
            Questions: [],
            Conflicts: [],
            RunnerFailures: failing ? [Row] : []);

        return context.RouteAsync("**/api/attention", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(attention, Web),
        }));
    }
}

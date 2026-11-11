using Hatch.Api.Tests.Hatch;
using Microsoft.Playwright;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2193's acceptance, rendered: on a quiet board the opened panel shows the
/// Human and Agent groups as two heading buttons and nothing else; pressing
/// Human reveals its section headings, each a button of its own, and pressing
/// one shows its empty words. On a busy board - a conflicting branch, and an
/// issue in review with nowhere to review it - the Agent and Human headings are
/// plain, with no button around either. Run at the desktop viewport in Chromium
/// and at the <c>iPhone 14 Pro</c> profile in WebKit.
///
/// <para>A scratch database has nothing to conflict, so the attention read is
/// fulfilled with <c>page.RouteAsync</c>, serialised from the real record so a
/// renamed field breaks this build rather than the panel. Every other route,
/// and the bundle, is real. Each look is saved as a PNG beside the case's other
/// artifacts.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class AttentionFoldTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string PanelSelector = ".hatch-attention-panel";

    private const string Fold = "button.hatch-attention-fold";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task AQuietBoard_FoldsBothGroups_AndAPressOpensThem_AtTheDesktopProfile() =>
        RunAsync("AttentionFoldQuietDesktop", "quiet-desktop", null, busy: false);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task ABusyBoard_DrawsBothGroupHeadingsPlain_AtTheDesktopProfile() =>
        RunAsync("AttentionFoldBusyDesktop", "busy-desktop", null, busy: true);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task AQuietBoard_FoldsBothGroups_AndAPressOpensThem_AtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("AttentionFoldQuietPhone", "quiet-phone", DeviceProfile.IPhone14Pro, busy: false);
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task ABusyBoard_DrawsBothGroupHeadingsPlain_AtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("AttentionFoldBusyPhone", "busy-phone", DeviceProfile.IPhone14Pro, busy: true);
    }

    private async Task RunAsync(string name, string label, DeviceProfile? profile, bool busy)
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

            await harness.OpenAsync(deadline, baseUrl, "/apps/hatch/", context => StubAttention(context, busy));
            await harness.WaitForTopBarAsync(deadline);

            await harness.ClickAsync(deadline, E2eHarness.AttentionSelector);
            await harness.WaitForVisibleAsync(deadline, PanelSelector);

            if (busy)
            {
                await harness.WaitForVisibleAsync(deadline, $"{PanelSelector} h2.hatch-attention-group-heading:text-is(\"Human\")");
                await harness.WaitForVisibleAsync(deadline, $"{PanelSelector} h2.hatch-attention-group-heading:text-is(\"Agent\")");
                await harness.WaitForVisibleAsync(deadline, $"{PanelSelector} h3.hatch-attention-heading:text-is(\"Pull requests to review\")");
                await harness.WaitForVisibleAsync(deadline, $"{PanelSelector} h3.hatch-attention-heading:text-is(\"Branches that conflict\")");
                // The one empty section left in Agent stays folded behind its own button.
                await harness.WaitForVisibleAsync(deadline, $"{PanelSelector} h3 {Fold}:text-is(\"Branch builds that fail\")");
                Assert.False(await harness.IsVisibleAsync(deadline, $"{PanelSelector} h2 {Fold}"));
            }
            else
            {
                await harness.WaitForVisibleAsync(deadline, $"{PanelSelector} h2 {Fold}[aria-expanded=\"false\"]:text-is(\"Human\")");
                await harness.WaitForVisibleAsync(deadline, $"{PanelSelector} h2 {Fold}[aria-expanded=\"false\"]:text-is(\"Agent\")");
                Assert.False(await harness.IsVisibleAsync(deadline, $"{PanelSelector} h3"));
                output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"{label}-closed")}");

                await harness.ClickAsync(deadline, $"{PanelSelector} h2 {Fold}:text-is(\"Human\")");
                await harness.WaitForVisibleAsync(deadline, $"{PanelSelector} h2 {Fold}[aria-expanded=\"true\"]:text-is(\"Human\")");
                await harness.WaitForVisibleAsync(deadline, $"{PanelSelector} h3 {Fold}[aria-expanded=\"false\"]:text-is(\"Trunk builds that fail\")");
                await harness.WaitForVisibleAsync(deadline, $"{PanelSelector} h3 {Fold}[aria-expanded=\"false\"]:text-is(\"Pull requests to review\")");
                // Agent was not pressed: its sections stay out of the page.
                Assert.False(await harness.IsVisibleAsync(deadline, $"{PanelSelector} h3:has-text(\"Branches that conflict\")"));
                output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"{label}-human-open")}");

                await harness.ClickAsync(deadline, $"{PanelSelector} h3 {Fold}:text-is(\"Trunk builds that fail\")");
                await harness.WaitForVisibleAsync(deadline, $"{PanelSelector} h3 {Fold}[aria-expanded=\"true\"]:text-is(\"Trunk builds that fail\")");
                await harness.WaitForVisibleAsync(deadline, $"{PanelSelector} .hatch-attention-empty");
                // Pressing a section did not close the panel: the focus stayed in it.
                Assert.True(await harness.IsVisibleAsync(deadline, PanelSelector));
            }

            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, label)}");

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

    /// <summary>The attention read with every list empty, or with one conflicting branch and an issue in review with no pull request.</summary>
    private static Task StubAttention(IBrowserContext context, bool busy)
    {
        var attention = new AttentionDto(
            Reviews: [],
            InReviewWithoutPullRequest: busy ? 3 : 0,
            Questions: [],
            Conflicts: busy
                ? [new ConflictDto("E2E-14", "A story whose branch stopped merging", "story", "https://forge.example/pulls/14", [])]
                : []);

        return context.RouteAsync("**/api/hatch/attention", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(attention, Web),
        }));
    }
}

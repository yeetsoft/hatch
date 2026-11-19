using Hatch.Api.Tests.Hatch;
using Microsoft.Playwright;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2486's acceptance, rendered: with an attention answer whose only
/// non-empty list is <c>HeldAtProjectLine</c>, the bar's attention control
/// lights with no pill, and its panel's Human group shows a <em>Waiting on
/// another project's approver</em> section holding each row - key, title and the
/// API's sentence - linked to its issue. With every list empty, the control reads
/// <c>Clear</c> and the opened panel shows the section's own empty sentence. Run
/// at the desktop viewport in Chromium and at the <c>iPhone 14 Pro</c> profile in
/// WebKit.
///
/// <para>The hold itself is pinned by <c>AttentionControllerTests</c> against the
/// real queue scan; here the attention read is fulfilled with
/// <c>page.RouteAsync</c>, serialised from the real record so a renamed field
/// breaks this build rather than the panel. Every other route, and the bundle, is
/// real. Each look is saved as a PNG beside the case's other artifacts.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class AttentionHeldAtProjectLineTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string EmptySentence = "Nothing is waiting on another project's approver.";

    private const string PanelSelector = ".hatch-attention-panel";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private const string SectionTitle = "Waiting on another project's approver";

    private static readonly ProjectLineHeldDto Row = new(
        "E2E-24", "A story under another project's epic", "story", "E2E",
        "pre-approved on an ancestor in another project, not honoured in E2E - its setter has no say there, so a person moves it");

    private static readonly ProjectLineHeldDto Hidden = new(
        "E2E-25", "A story whose parent is out of sight", "story", "E2E",
        "its parent in another project is in progress, but the person who started it has no say in E2E, so a person moves it");

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task ARow_LightsTheControlAndIsListed_AtTheDesktopProfile() =>
        RunAsync("AttentionProjectLineDesktop", "desktop", null, held: true);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task NothingHeld_ReadsClearAndSaysSo_AtTheDesktopProfile() =>
        RunAsync("AttentionProjectLineClearDesktop", "desktop", null, held: false);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task ARow_LightsTheControlAndIsListed_AtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("AttentionProjectLinePhone", "phone", DeviceProfile.IPhone14Pro, held: true);
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task NothingHeld_ReadsClearAndSaysSo_AtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("AttentionProjectLineClearPhone", "phone", DeviceProfile.IPhone14Pro, held: false);
    }

    /// <summary>The same phone, emulated in Chromium: the cases that still run where WebKit does not open a page.</summary>
    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task ARow_LightsTheControlAndIsListed_AtThePhoneProfileInChromium() =>
        RunAsync("AttentionProjectLinePhoneChromium", "phone-chromium", DeviceProfile.IPhone14ProChromium, held: true);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task NothingHeld_ReadsClearAndSaysSo_AtThePhoneProfileInChromium() =>
        RunAsync("AttentionProjectLineClearPhoneChromium", "phone-chromium", DeviceProfile.IPhone14ProChromium, held: false);

    private async Task RunAsync(string name, string profileName, DeviceProfile? profile, bool held)
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

            await harness.OpenAsync(deadline, baseUrl, "/", context => StubAttention(context, held));
            await harness.WaitForTopBarAsync(deadline);

            if (held)
            {
                await harness.WaitForVisibleAsync(deadline, $"{E2eHarness.AttentionSelector}.hatch-attention-asking");
                await harness.ClickAsync(deadline, E2eHarness.AttentionSelector);
                await harness.WaitForVisibleAsync(deadline, $"{PanelSelector} h3:text-is(\"{SectionTitle}\")");
                foreach (var row in new[] { Row, Hidden })
                {
                    await harness.WaitForVisibleAsync(
                        deadline, $"{PanelSelector} a.hatch-attention-row[href=\"/issues/{row.Key}\"]:has(.hatch-attention-key:text-is(\"{row.Key}\")):has(.hatch-attention-title:text-is(\"{row.Title}\")):has(.hatch-attention-files:text-is(\"{row.Sentence}\"))");
                }
                Assert.False(await harness.IsVisibleAsync(deadline, $"{PanelSelector} .hatch-attention-empty:text-is(\"{EmptySentence}\")"));
                // A light and no pill: the section has no pill of its own.
                Assert.False(await harness.IsVisibleAsync(deadline, ".hatch-attention-pill"));
            }
            else
            {
                await harness.WaitForVisibleAsync(deadline, $"{E2eHarness.AttentionSelector}.hatch-attention-rest .hatch-attention-word:text-is(\"Clear\")");
                await harness.ClickAsync(deadline, E2eHarness.AttentionSelector);
                // Every Human section is empty here, so HA-2193 folds the whole
                // group behind its own heading button - open it first.
                await harness.ClickAsync(deadline, $"{PanelSelector} h2 button.hatch-attention-fold:text-is(\"Human\")");
                // :has-text, not :text-is - empty and factless, HA-2192 folds
                // this section behind its own heading button, and the button's
                // own text is the smallest exact match then, not the h3 around it.
                await harness.WaitForVisibleAsync(deadline, $"{PanelSelector} h3:has-text(\"{SectionTitle}\")");
                await harness.ClickAsync(deadline, $"{PanelSelector} h3:has-text(\"{SectionTitle}\") button.hatch-attention-fold");
                await harness.WaitForVisibleAsync(deadline, $"{PanelSelector} .hatch-attention-empty:text-is(\"{EmptySentence}\")");
                Assert.False(await harness.IsVisibleAsync(deadline, $"{PanelSelector} a.hatch-attention-row[href^=\"/issues/\"]"));
            }

            // The panel scrolls at phone width and the section sits low in it:
            // bring it into the frame so the picture shows the rows.
            await harness.EvaluateAsync<bool>(
                deadline,
                "scrolling the section into view",
                "(title) => { const h = [...document.querySelectorAll('.hatch-attention-panel h3')].find((e) => e.textContent.includes(title)); if (h) h.scrollIntoView({ block: 'center' }); return true; }",
                SectionTitle);

            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"{(held ? "held" : "clear")}-{profileName}")}");

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

    /// <summary>The attention read with the two rows, or with every list empty.</summary>
    private static Task StubAttention(IBrowserContext context, bool held)
    {
        var attention = new AttentionDto(
            Reviews: [],
            InReviewWithoutPullRequest: 0,
            Questions: [],
            Conflicts: [],
            HeldAtProjectLine: held ? [Row, Hidden] : []);

        return context.RouteAsync("**/api/attention", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(attention, Web),
        }));
    }
}

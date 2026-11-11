using Hatch.Api.Tests.Hatch;
using Microsoft.Playwright;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2015's acceptance, rendered: with an attention answer whose only
/// non-empty list is <c>WaitingOnARunner</c>, the bar's attention control
/// lights, and its panel's Human group shows a <em>Waiting on a runner</em>
/// section holding the row, linked to its issue, and one link to the Runners
/// page. With every list empty, the control reads <c>Clear</c> and the opened
/// panel shows the section's own empty sentence. Run at the desktop viewport in
/// Chromium and at the <c>iPhone 14 Pro</c> profile in WebKit.
///
/// <para>A scratch database has no runner that could be short of a model, so
/// the attention read is fulfilled with <c>page.RouteAsync</c>, serialised from
/// the real record so a renamed field breaks this build rather than the panel.
/// Every other route, and the bundle, is real. Each look is saved as a PNG
/// beside the case's other artifacts.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class AttentionWaitingOnARunnerTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string EmptySentence = "Nothing is waiting on a runner.";

    private const string PanelSelector = ".hatch-attention-panel";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly WaitingOnARunnerDto Row = new(
        "E2E-24", "A story that runs on fable", "story", "todo", "no live runner offers fable");

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task AWaitingRow_LightsTheControlAndIsListed_AtTheDesktopProfile() =>
        RunAsync("AttentionWaitingDesktop", "desktop", null, waiting: true);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task NothingWaiting_ReadsClearAndSaysSo_AtTheDesktopProfile() =>
        RunAsync("AttentionClearDesktop", "desktop", null, waiting: false);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task AWaitingRow_LightsTheControlAndIsListed_AtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("AttentionWaitingPhone", "phone", DeviceProfile.IPhone14Pro, waiting: true);
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task NothingWaiting_ReadsClearAndSaysSo_AtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("AttentionClearPhone", "phone", DeviceProfile.IPhone14Pro, waiting: false);
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

            await harness.OpenAsync(deadline, baseUrl, "/", context => StubAttention(context, waiting));
            await harness.WaitForTopBarAsync(deadline);

            if (waiting)
            {
                await harness.WaitForVisibleAsync(deadline, $"{E2eHarness.AttentionSelector}.hatch-attention-asking");
                await harness.ClickAsync(deadline, E2eHarness.AttentionSelector);
                await harness.WaitForVisibleAsync(deadline, $"{PanelSelector} h3:text-is(\"Waiting on a runner\")");
                await harness.WaitForVisibleAsync(
                    deadline, $"{PanelSelector} a.hatch-attention-row:has(.hatch-attention-key:text-is(\"{Row.Key}\")):has-text(\"in {Row.Move} - {Row.Missing}\")");
                await harness.WaitForVisibleAsync(deadline, $"{PanelSelector} a.hatch-attention-row:has-text(\"See the Runners page\")");
                Assert.False(await harness.IsVisibleAsync(deadline, $"{PanelSelector} .hatch-attention-empty:text-is(\"{EmptySentence}\")"));
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
                await harness.WaitForVisibleAsync(deadline, $"{PanelSelector} h3:has-text(\"Waiting on a runner\")");
                await harness.ClickAsync(deadline, $"{PanelSelector} h3:has-text(\"Waiting on a runner\") button.hatch-attention-fold");
                await harness.WaitForVisibleAsync(deadline, $"{PanelSelector} .hatch-attention-empty:text-is(\"{EmptySentence}\")");
                Assert.False(await harness.IsVisibleAsync(deadline, $"{PanelSelector} a.hatch-attention-row:has-text(\"See the Runners page\")"));
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

        return context.RouteAsync("**/api/hatch/attention", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(attention, Web),
        }));
    }
}

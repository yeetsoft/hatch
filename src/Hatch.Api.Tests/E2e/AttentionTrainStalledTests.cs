using Hatch.Api.Tests.Hatch;
using Microsoft.Playwright;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2094's acceptance, rendered: with an attention answer carrying one
/// <c>TrainStalled</c> entry, the opened panel's Human group shows a
/// <em>Stalled merge trains</em> section holding the row, linked to the head
/// issue, and the control still reads <c>Clear</c> - a stalled train is listed,
/// not counted. With the list empty the section is not drawn at all. Run at the
/// desktop viewport in Chromium and at the <c>iPhone 14 Pro</c> profile in
/// WebKit.
///
/// <para>A scratch database has no project whose train was passed over, so the
/// attention read is fulfilled with <c>page.RouteAsync</c>, serialised from the
/// real record so a renamed field breaks this build rather than the panel.
/// Every other route, and the bundle, is real. Each look is saved as a PNG
/// beside the case's other artifacts.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class AttentionTrainStalledTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Heading = "Stalled merge trains";

    private const string PanelSelector = ".hatch-attention-panel";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly TrainStalledDto Row = new(
        "E2E", "Aerie", "laptop:/work/aerie", "desk:/work/aerie", "E2E-31", new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero));

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task AStalledTrain_IsListedAndLeavesTheControlClear_AtTheDesktopProfile() =>
        RunAsync("AttentionStalledDesktop", "desktop", null, stalled: true);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task NoStalledTrain_DrawsNoSection_AtTheDesktopProfile() =>
        RunAsync("AttentionNotStalledDesktop", "desktop", null, stalled: false);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task AStalledTrain_IsListedAndLeavesTheControlClear_AtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("AttentionStalledPhone", "phone", DeviceProfile.IPhone14Pro, stalled: true);
    }

    private async Task RunAsync(string name, string profileName, DeviceProfile? profile, bool stalled)
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

            await harness.OpenAsync(deadline, baseUrl, "/", context => StubAttention(context, stalled));
            await harness.WaitForTopBarAsync(deadline);

            await harness.WaitForVisibleAsync(deadline, $"{E2eHarness.AttentionSelector}.hatch-attention-rest .hatch-attention-word:text-is(\"Clear\")");
            await harness.ClickAsync(deadline, E2eHarness.AttentionSelector);
            // With no stalled train every Human section is empty, so HA-2193
            // folds the whole group behind its own heading button - open it first.
            if (!stalled)
                await harness.ClickAsync(deadline, $"{PanelSelector} h2 button.hatch-attention-fold:text-is(\"Human\")");
            // :has-text, not :text-is - this stub never fills WaitingOnARunner,
            // so HA-2192 folds the section behind its own heading button, and
            // the button's own text is the smallest exact match then, not the
            // h3 around it.
            await harness.WaitForVisibleAsync(deadline, $"{PanelSelector} h3:has-text(\"Waiting on a runner\")");

            if (stalled)
            {
                await harness.WaitForVisibleAsync(deadline, $"{PanelSelector} h3:text-is(\"{Heading}\")");
                await harness.WaitForVisibleAsync(
                    deadline, $"{PanelSelector} a.hatch-attention-row[href$=\"/issues/{Row.HeadKey}\"]:has(.hatch-attention-key:text-is(\"{Row.HeadKey}\")):has(.hatch-attention-title:text-is(\"{Row.ProjectName}\")):has-text(\"{Row.PassedOverRunner} was passed over\"):has-text(\"handed to {Row.ElectedRunner}\")");
            }
            else
            {
                Assert.False(await harness.IsVisibleAsync(deadline, $"{PanelSelector} h3:text-is(\"{Heading}\")"));
            }

            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"{(stalled ? "stalled" : "none")}-{profileName}")}");

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

    /// <summary>The attention read with the one stalled train, or with every list empty.</summary>
    private static Task StubAttention(IBrowserContext context, bool stalled)
    {
        var attention = new AttentionDto(
            Reviews: [],
            InReviewWithoutPullRequest: 0,
            Questions: [],
            Conflicts: [],
            TrainStalled: stalled ? [Row] : []);

        return context.RouteAsync("**/api/attention", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(attention, Web),
        }));
    }
}

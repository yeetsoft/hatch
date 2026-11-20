using Hatch.Api.Tests.Hatch;
using Microsoft.Playwright;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2500's acceptance, rendered: with an attention answer carrying two open
/// findings - one about an issue and with a bug filed, one about a project - and
/// a goose that has stopped, the opened panel's Human group opens with a
/// <em>Findings</em> section above <em>Trunk builds that fail</em>: the stopped
/// row first, then each finding's sentence and age, the first linked to its
/// issue with its bug beside it, the second linked to the project's board. The
/// control is lit and says what it counts. With nothing found and the goose
/// running the section is not drawn. Run at the desktop viewport in Chromium
/// and at the <c>iPhone 14 Pro</c> profile in WebKit.
///
/// <para>A scratch database has no findings, so the attention read is fulfilled
/// with <c>page.RouteAsync</c>, serialised from the real record so a renamed
/// field breaks this build rather than the panel. Every other route, and the
/// bundle, is real. Each look is saved as a PNG beside the case's other
/// artifacts.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class AttentionFindingsTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Heading = "Findings";

    private const string PanelSelector = ".hatch-attention-panel";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly AttentionFindingDto AboutAnIssue = new(
        1, "train-ready", "E2E", "E2E's merge train is standing: E2E-31 is ready to merge and has not",
        DateTimeOffset.UtcNow.AddHours(-3), DateTimeOffset.UtcNow.AddHours(-2), "E2E-77", "E2E-31");

    private static readonly AttentionFindingDto AboutAProject = new(
        2, "train-ready", "E2E", "E2E's merge train is standing: nothing has moved on it",
        DateTimeOffset.UtcNow.AddMinutes(-90), DateTimeOffset.UtcNow.AddMinutes(-60), null, null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task Findings_HeadTheHumanGroup_AtTheDesktopProfile() =>
        RunAsync("AttentionFindingsDesktop", "desktop", null, found: true);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task NothingFound_DrawsNoSection_AtTheDesktopProfile() =>
        RunAsync("AttentionNoFindingsDesktop", "desktop", null, found: false);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task Findings_HeadTheHumanGroup_AtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("AttentionFindingsPhone", "phone", DeviceProfile.IPhone14Pro, found: true);
    }

    private async Task RunAsync(string name, string profileName, DeviceProfile? profile, bool found)
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

            await harness.OpenAsync(deadline, baseUrl, "/", context => StubAttention(context, found));
            await harness.WaitForTopBarAsync(deadline);

            if (found)
                await harness.WaitForVisibleAsync(deadline, $"{E2eHarness.AttentionSelector}[aria-label*=\"2 findings, the goose has stopped\"]");
            else
                await harness.WaitForVisibleAsync(deadline, $"{E2eHarness.AttentionSelector}.hatch-attention-rest .hatch-attention-word:text-is(\"Clear\")");

            await harness.ClickAsync(deadline, E2eHarness.AttentionSelector);
            // With nothing found every Human section is empty, so the whole
            // group folds behind its own heading button - open it first.
            if (!found)
                await harness.ClickAsync(deadline, $"{PanelSelector} h2 button.hatch-attention-fold:text-is(\"Human\")");
            await harness.WaitForVisibleAsync(deadline, $"{PanelSelector} h3:has-text(\"Trunk builds that fail\")");

            if (found)
            {
                await harness.WaitForVisibleAsync(deadline, $"{PanelSelector} h3:text-is(\"{Heading}\")");
                // The stopped row first, to the goose's page.
                await harness.WaitForVisibleAsync(
                    deadline, $"{PanelSelector} li:first-child > a.hatch-attention-row[href$=\"/goose\"]:has-text(\"The goose has stopped. It last ran 12 minutes ago.\")");
                // The finding about an issue: its row to the issue, its bug a sibling link.
                await harness.WaitForVisibleAsync(
                    deadline, $"{PanelSelector} li:has(> a.hatch-attention-row[href$=\"/issues/{AboutAnIssue.IssueKey}\"]:has-text(\"{AboutAnIssue.Sentence}\"):has-text(\"3 hours\")) > .hatch-attention-row-actions > a[href$=\"/issues/{AboutAnIssue.BugIssueKey}\"]");
                // The finding about the project: to its board.
                await harness.WaitForVisibleAsync(
                    deadline, $"{PanelSelector} a.hatch-attention-row[href*=\"scope={AboutAProject.ProjectKey}\"]:has-text(\"{AboutAProject.Sentence}\"):has-text(\"1 hour\")");
            }
            else
            {
                Assert.False(await harness.IsVisibleAsync(deadline, $"{PanelSelector} h3:text-is(\"{Heading}\")"));
            }

            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"{(found ? "found" : "none")}-{profileName}")}");

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

    /// <summary>The attention read with two findings and a stopped goose, or with every list empty and the goose running.</summary>
    private static Task StubAttention(IBrowserContext context, bool found)
    {
        var attention = new AttentionDto(
            Reviews: [],
            InReviewWithoutPullRequest: 0,
            Questions: [],
            Conflicts: [],
            Findings: found ? [AboutAnIssue, AboutAProject] : [],
            Goose: new AttentionGooseDto(DateTimeOffset.UtcNow.AddMinutes(-12), 60, Stopped: found));

        return context.RouteAsync("**/api/attention", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(attention, Web),
        }));
    }
}

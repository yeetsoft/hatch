using System.Net.Http.Json;
using System.Text.Json;
using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2269's web half, rendered in headless Chromium at the desktop viewport
/// against the real API serving the built bundle: the project dialog's
/// <em>Runners work contributions</em> checkbox as an owner saves it through its
/// own route, and the attention panel's <em>Contributions runners will not
/// work</em> section with a row that was mirrored from another instance and one
/// that was filed here.
///
/// <para>A scratch database has no contribution in review with a red branch, so
/// the attention read is fulfilled with <c>page.RouteAsync</c>, serialised from
/// the real record so a renamed field breaks this build rather than the panel.
/// Every other route, and the bundle, is real.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class ContributionsRunnersWorkTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Label = "Runners work contributions";

    private const string PanelSelector = ".hatch-attention-panel";

    private const string CheckedScript = "(el) => el.checked";

    /// <summary>Runs against an element inside the dialog's own scroll: brings it to the middle, so the screenshot shows what the assertions read.</summary>
    private const string ScrollIntoViewScript = "(el) => { el.scrollIntoView({ block: 'center' }); return true; }";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly ContributionHeldDto Mirrored = new(
        "E2E-31", "A contribution with a red build", "task", "https://forge.example/owner/repo/pull/31",
        "Its build failed.", "upstream", "UP-9");

    private static readonly ContributionHeldDto Local = new(
        "E2E-32", "A contribution that conflicts", "task", "https://forge.example/owner/repo/pull/32",
        "It conflicts with main.", null, null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheAttentionPanel_ListsEachHeldContribution_WithItsLinksAndItsOrigin()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("ContributionsHeldPanel");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);

            await harness.OpenAsync(deadline, baseUrl, "/", StubAttention);
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForVisibleAsync(deadline, $"{E2eHarness.AttentionSelector}.hatch-attention-asking");
            await harness.ClickAsync(deadline, E2eHarness.AttentionSelector);

            await harness.WaitForVisibleAsync(deadline, $"{PanelSelector} h3:text-is(\"Contributions runners will not work\")");
            foreach (var row in new[] { Mirrored, Local })
            {
                var item = $"{PanelSelector} li:has(a.hatch-attention-key[href$=\"/issues/{row.Key}\"])";
                await harness.WaitForVisibleAsync(deadline, item);
                await harness.WaitForVisibleAsync(deadline, $"{item} :text-is(\"{row.Title}\")");
                await harness.WaitForVisibleAsync(deadline, $"{item} a[href=\"{row.PullRequestUrl}\"][target=_blank]");
                await harness.WaitForVisibleAsync(deadline, $"{item} :text-is(\"{row.Sentence}\")");
            }

            var mirrored = $"{PanelSelector} li:has(a.hatch-attention-key[href$=\"/issues/{Mirrored.Key}\"])";
            await harness.WaitForVisibleAsync(deadline, $"{mirrored} :text-is(\"mirrored from {Mirrored.OriginInstance}'s {Mirrored.OriginKey}\")");
            Assert.False(await harness.IsVisibleAsync(
                deadline, $"{PanelSelector} li:has(a.hatch-attention-key[href$=\"/issues/{Local.Key}\"]) :has-text(\"mirrored from\")"));

            await harness.EvaluateOnAsync<bool>(deadline, "scrolling the section into view", $"{PanelSelector} h3:text-is(\"Contributions runners will not work\")", ScrollIntoViewScript);
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, "contributions-held")}");

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

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheOwnersDialog_SavesTheFlagThroughItsOwnRoute_AndShowsItTickedOnReopening()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("ContributionsFlagDialog");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);

            using (var http = new HttpClient { BaseAddress = new Uri(baseUrl) })
            {
                using var response = await http.PostAsJsonAsync("/api/projects", new ProjectCreateRequest("OWN", "Project OWN"));
                Assert.True(response.IsSuccessStatusCode, $"POST /api/projects answered {(int)response.StatusCode}");
            }

            var puts = new List<string>();
            await harness.OpenAsync(deadline, baseUrl, "/projects", context => context.RouteAsync(
                "**/api/projects/*/runners-work-contributions", route =>
                {
                    lock (puts) puts.Add($"{route.Request.Method} {route.Request.PostData}");
                    return route.ContinueAsync();
                }));

            var dialog = await OpenDialogAsync(harness, deadline);
            var checkbox = $"{dialog} label:text-is(\"{Label}\") input[type=checkbox]";
            Assert.False(await harness.EvaluateOnAsync<bool>(deadline, "reading the checkbox", checkbox, CheckedScript), "the flag should start off");
            await harness.EvaluateOnAsync<bool>(deadline, "scrolling the checkbox into view", $"{dialog} label:text-is(\"{Label}\")", ScrollIntoViewScript);
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, "contributions-flag-off")}");

            // Saved with nothing changed, the dialog sends no write to the new route.
            await harness.ClickAsync(deadline, $"{dialog} button:text-is(\"Save\")");
            await harness.WaitForHiddenAsync(deadline, dialog);
            lock (puts) Assert.Empty(puts);

            dialog = await OpenDialogAsync(harness, deadline);
            checkbox = $"{dialog} label:text-is(\"{Label}\") input[type=checkbox]";
            await harness.ClickAsync(deadline, checkbox);
            await harness.ClickAsync(deadline, $"{dialog} button:text-is(\"Save\")");
            await harness.WaitForHiddenAsync(deadline, dialog);

            string put;
            lock (puts) put = Assert.Single(puts);
            Assert.StartsWith("PUT", put, StringComparison.Ordinal);
            Assert.Contains("\"runnersWorkContributions\":true", put, StringComparison.Ordinal);

            dialog = await OpenDialogAsync(harness, deadline);
            Assert.True(
                await harness.EvaluateOnAsync<bool>(deadline, "reading the checkbox", $"{dialog} label:text-is(\"{Label}\") input[type=checkbox]", CheckedScript),
                "the flag should be on after Save");
            await harness.WaitForVisibleAsync(deadline, $"{dialog} p.hatch-section-hint:text-matches(\"^set by\")");
            await harness.EvaluateOnAsync<bool>(deadline, "scrolling the checkbox into view", $"{dialog} label:text-is(\"{Label}\")", ScrollIntoViewScript);
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, "contributions-flag-on")}");

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

    private static async Task<string> OpenDialogAsync(E2eHarness harness, Deadline deadline)
    {
        await harness.ClickAsync(deadline, "tr:has(code:text-is(\"OWN\")) button:text-is(\"Edit\")");
        var dialog = "[role=dialog]:has(h3:text-is(\"Edit OWN\"))";
        await harness.WaitForVisibleAsync(deadline, dialog);
        return dialog;
    }

    private static Task StubAttention(IBrowserContext context)
    {
        var attention = new AttentionDto(
            Reviews: [],
            InReviewWithoutPullRequest: 0,
            Questions: [],
            Conflicts: [],
            ContributionsHeld: [Mirrored, Local]);

        return context.RouteAsync("**/api/attention", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(attention, Web),
        }));
    }
}

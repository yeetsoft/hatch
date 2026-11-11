using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.Playwright;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-1704: the Settings page's Runner token budget card, rendered by a real
/// browser for each of the three callers it treats differently - an admin, a
/// person with a role who is not an admin, and the wall off - from stubbed
/// settings and a stubbed <c>/api/hatch/me</c>.
///
/// <para>The admin's card is edited and saved, and the figures read back from
/// the fields; the non-admin's is read-only text; the wall-off's is editable
/// with no one signed in. The screenshot is taken before the assertions, so a
/// failing case still leaves its picture.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class SettingsTokenBudgetTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string PerRun = "Default max tokens per run";
    private const string PerHour = "Default max tokens per hour";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly HatchSettingsDto Seeded = new("", false, "", "", "", 2_000_000, 200_000);
    private static readonly HatchSettingsDto Saved = new("", false, "", "", "", 5_000_000, 500_000);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheRunnerTokenBudgetSection_RendersForAnAdmin()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        await RunAsync("SettingsTokenBudgetAdmin", new MeDto("person", "Admin", "admin", true, true, false), async (harness, deadline) =>
        {
            var page = harness.Page!;

            var perRun = page.GetByLabel(PerRun);
            var perHour = page.GetByLabel(PerHour);

            Assert.Equal("2,000,000", await deadline.BoundAsync("reading the per-run field", () => perRun.InputValueAsync()));
            Assert.Equal("200,000", await deadline.BoundAsync("reading the per-hour field", () => perHour.InputValueAsync()));
            output.WriteLine($"before editing: {PerRun} = 2,000,000; {PerHour} = 200,000");
            output.WriteLine($"screenshot: {await harness.ScreenshotAsync(deadline, "settings-token-budget-admin")}");

            await deadline.BoundAsync("filling the per-run field", () => perRun.FillAsync("5000000"));
            await deadline.BoundAsync("blurring the per-run field", () => perRun.PressAsync("Tab"));
            await deadline.BoundAsync("filling the per-hour field", () => perHour.FillAsync("500000"));
            await deadline.BoundAsync("blurring the per-hour field", () => perHour.PressAsync("Tab"));

            var save = page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true });
            await deadline.BoundAsync("clicking Save", () => save.ClickAsync());

            // Once the answer is written back, the form matches it and Save goes quiet again.
            await deadline.BoundAsync("waiting for the save to settle", () => Assertions.Expect(save).ToBeDisabledAsync());

            var perRunAfter = await deadline.BoundAsync("rereading the per-run field", () => perRun.InputValueAsync());
            var perHourAfter = await deadline.BoundAsync("rereading the per-hour field", () => perHour.InputValueAsync());
            output.WriteLine($"after the save: {PerRun} = {perRunAfter}; {PerHour} = {perHourAfter}");
            output.WriteLine($"screenshot: {await harness.ScreenshotAsync(deadline, "settings-token-budget-admin-saved")}");

            Assert.Equal("5,000,000", perRunAfter);
            Assert.Equal("500,000", perHourAfter);
        });
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheRunnerTokenBudgetSection_RendersForANonAdminWithARole()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        await RunAsync("SettingsTokenBudgetUser", new MeDto("person", "Operator", "user", true, true, false), async (harness, deadline) =>
        {
            var page = harness.Page!;

            output.WriteLine($"screenshot: {await harness.ScreenshotAsync(deadline, "settings-token-budget-user")}");

            Assert.Equal(0, await deadline.BoundAsync("counting the budget inputs", () => page.GetByLabel(PerRun).CountAsync()));
            Assert.Equal(0, await deadline.BoundAsync("counting the budget inputs", () => page.GetByLabel(PerHour).CountAsync()));
            Assert.True(
                await deadline.BoundAsync("looking for the admins-only note", () => page.GetByText("Admins only.", new() { Exact = true }).IsVisibleAsync()),
                "the read-only card does not say \"Admins only.\"");
            Assert.True(
                await deadline.BoundAsync("looking for the per-run figure", () => page.GetByText($"{PerRun}: 2M tokens").IsVisibleAsync()),
                $"the card does not read \"{PerRun}: 2M tokens\"");
            Assert.True(
                await deadline.BoundAsync("looking for the per-hour figure", () => page.GetByText($"{PerHour}: 200k tokens").IsVisibleAsync()),
                $"the card does not read \"{PerHour}: 200k tokens\"");
        });
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheRunnerTokenBudgetSection_RendersWithTheWallOff()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        await RunAsync("SettingsTokenBudgetWallOff", new MeDto("local", "", null, true, false, false), async (harness, deadline) =>
        {
            var page = harness.Page!;

            output.WriteLine($"screenshot: {await harness.ScreenshotAsync(deadline, "settings-token-budget-wall-off")}");

            foreach (var label in new[] { PerRun, PerHour })
            {
                var field = page.GetByLabel(label);
                Assert.Equal(1, await deadline.BoundAsync($"counting the {label} field", () => field.CountAsync()));
                Assert.True(await deadline.BoundAsync($"checking the {label} field", () => field.IsEnabledAsync()), $"\"{label}\" is disabled");
            }
        });
    }

    /// <summary>
    /// Opens Settings as <paramref name="me"/> with the settings stubbed, waits
    /// for the card's heading, and hands the page to <paramref name="check"/>.
    /// </summary>
    private async Task RunAsync(string name, MeDto me, Func<E2eHarness, Deadline, Task> check)
    {
        var harness = new E2eHarness(name);
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline, null);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            // The settings and the caller are stubbed, but the host still serves the bundle.
            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);

            await harness.OpenAsync(deadline, baseUrl, "/settings", context => StubSettings(context, me));
            await harness.WaitForVisibleAsync(deadline, "h2.hatch-section-title:has-text(\"Runner token budget\")");

            // The card sits below the first screenful, and a screenshot is the viewport only. Evaluated rather than
            // ScrollIntoViewIfNeeded, whose stability wait cannot run under the harness's no-timeout page.
            await deadline.BoundAsync("scrolling the card into view", () =>
                harness.Page!.Locator("h2.hatch-section-title", new() { HasText = "Runner token budget" }).EvaluateAsync("heading => heading.scrollIntoView({ block: 'center' })"));

            await check(harness, deadline);

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

    /// <summary>
    /// The settings read answers <see cref="Seeded"/> and the write answers
    /// <see cref="Saved"/> whatever it was sent, so what the fields show after a
    /// save is what the page did with the answer; <c>/api/hatch/me</c> answers
    /// the caller the case is about.
    /// </summary>
    private static async Task StubSettings(IBrowserContext context, MeDto me)
    {
        await context.RouteAsync("**/api/hatch/settings", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(route.Request.Method == "PUT" ? Saved : Seeded, Web),
        }));

        await context.RouteAsync("**/api/hatch/me", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(me, Web),
        }));
    }
}

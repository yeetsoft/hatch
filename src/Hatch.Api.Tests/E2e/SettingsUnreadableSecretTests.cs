using System.Security.Cryptography;
using Hatch.Api.Ef;
using Hatch.Api.Services.Settings;
using Hatch.Api.Tests.Hatch;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2750: the Settings page with one secret stored that cannot be opened. The
/// Claude token's row holds text that looks sealed and is not under any key this
/// host has, so <c>GET /api/settings</c> answers <c>unreadable</c> for it and
/// <c>unset</c> for the other two. Beside that one field the page says the value
/// is saved but cannot be opened, links Troubleshooting, and leaves Remove on;
/// the other two cards say nothing of the kind and keep Remove off.
///
/// <para>The real host, the real <c>GET /api/settings</c> - nothing stubbed. The
/// row is inserted through the host's own services, and the settings snapshot is
/// invalidated so the page's first read sees it. Run in Chromium at the desktop
/// profile and on the <c>iPhone 14 Pro</c> profile, where nothing may scroll
/// sideways. The screenshot is taken before the assertions, so a failing case
/// still leaves its picture.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class SettingsUnreadableSecretTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Sentence = "This is saved, but it cannot be opened. Enter it again, or remove it.";

    /// <summary>Shaped like a sealed value and opens under no key: its key id is not the host's.</summary>
    private const string Unopenable = "v2:AAAA.AAAA.AAAA";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task AnUnreadableSecret_IsSaidBesideItsField_AtTheDesktopProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        return RunAsync("SettingsUnreadableDesktop", profile: null);
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task AnUnreadableSecret_IsSaidBesideItsField_AtThePhoneProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);

        return RunAsync("SettingsUnreadablePhone", DeviceProfile.IPhone14Pro);
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task AnUnreadableSecret_IsSaidBesideItsField_OnThePhoneProfileInChromium()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        return RunAsync("SettingsUnreadablePhoneChromium", DeviceProfile.IPhone14ProChromium);
    }

    private async Task RunAsync(string name, DeviceProfile? profile)
    {
        var harness = new E2eHarness(name);
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline, profile);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            // Made here at run time: no key, real or example, goes in a tracked file.
            var baseUrl = await harness.StartHostAsync(deadline, secretKey: Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

            await deadline.BoundAsync("storing an unreadable Claude token", () => StoreUnreadableAsync(harness.Services));

            await harness.OpenAsync(deadline, baseUrl, "/settings");
            await harness.WaitForVisibleAsync(deadline, "h2.hatch-section-title:has-text(\"Codex API key\")");

            var page = harness.Page!;
            output.WriteLine($"screenshot: {await harness.FullPageScreenshotAsync(deadline, "settings-unreadable-secret")}");

            var cards = page.Locator(".hatch-card:has(input[type=password])");
            Assert.Equal(3, await deadline.BoundAsync("counting the secret cards", () => cards.CountAsync()));

            Assert.Equal(1, await deadline.BoundAsync("counting the sentences", () => page.GetByText(Sentence).CountAsync()));

            // The first card is the Claude token's, the one whose row was written.
            var first = cards.Nth(0);
            Assert.Equal(1, await deadline.BoundAsync("finding the sentence in the first card", () => first.GetByText(Sentence).CountAsync()));
            Assert.Equal(
                "/docs/troubleshooting",
                await deadline.BoundAsync("reading the link", () => first.GetByRole(Microsoft.Playwright.AriaRole.Link, new() { Name = "Troubleshooting" }).GetAttributeAsync("href")));
            Assert.Contains(
                "unreadable",
                await deadline.BoundAsync("reading the placeholder", () => first.Locator("input[type=password]").GetAttributeAsync("placeholder")) ?? "");

            var remove = first.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Remove token" });
            Assert.True(
                await deadline.BoundAsync("checking Remove", () => remove.IsEnabledAsync()),
                "Remove is off for a secret that is stored and cannot be opened");

            for (var i = 1; i < 3; i++)
            {
                var card = cards.Nth(i);
                Assert.Equal(0, await deadline.BoundAsync($"counting card {i}'s sentences", () => card.GetByText(Sentence).CountAsync()));
                Assert.Equal(
                    "Not set",
                    await deadline.BoundAsync($"reading card {i}'s placeholder", () => card.Locator("input[type=password]").GetAttributeAsync("placeholder")));
                Assert.True(
                    await deadline.BoundAsync($"checking card {i}'s Remove", () => card.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Remove API key" }).IsDisabledAsync()),
                    $"Remove is on for card {i}, which has nothing stored");
            }

            output.WriteLine("one unreadable secret: its sentence once, a link to /docs/troubleshooting, Remove on; the other two say Not set with Remove off");

            if (profile is not null)
            {
                var overflow = await harness.EvaluateAsync<int>(
                    deadline, "measuring sideways overflow",
                    "() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
                output.WriteLine($"sideways overflow: {overflow}px");
                Assert.True(overflow <= 0, $"the page scrolls sideways by {overflow}px");
            }

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

    /// <summary>Writes the row through the host's own database, then drops the snapshot so the next read sees it.</summary>
    private static async Task StoreUnreadableAsync(IServiceProvider services)
    {
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.SiteSettings.Add(new EfSiteSetting { Key = SiteSettingKeys.ClaudeSubscriptionToken, Value = Unopenable });
            await db.SaveChangesAsync();
        }

        services.GetRequiredService<ISiteSettingsService>().Invalidate();
    }
}

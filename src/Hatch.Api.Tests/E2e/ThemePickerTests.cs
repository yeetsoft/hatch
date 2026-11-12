using Hatch.Api.Tests.Hatch;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2259's Theme picker, driven on the real board: the gear's palette line
/// with Random off and on, a roll, a refused paste, a pasted seed, and Random
/// turned off and back on with the seed kept. Screenshots of each state land in
/// the case's artifact directory, light and dark, for a person to look at - what
/// the rolls look like is not something a case can assert.
///
/// <para>Chromium only: the phone profile's fit is measured by the bar cases,
/// and WebKit does not open a page on macOS 14.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class ThemePickerTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Palette = "style[data-theme-palette]";
    private const string Random = ".hatch-theme-picker__random input";
    private const string Dice = "button[aria-label=\"Roll a new theme\"]";
    private const string Seed = "input[aria-label=\"Theme seed\"]";
    private const string Copy = "button[aria-label=\"Copy seed\"]";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheGear_TurnsRandomOnAndOff_RollsPastesAndRefuses()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("ThemePicker");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);

            await harness.OpenBoardAsync(deadline, baseUrl);
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForFirstColumnAsync(deadline);
            var page = harness.Page!;

            // Random off: the house palette, the dice and the copy button asleep.
            await harness.ClickAsync(deadline, E2eHarness.GearSelector);
            await harness.WaitForVisibleAsync(deadline, ".hatch-theme-picker__track");
            Assert.False(await Checked(harness, deadline, Random));
            Assert.True(await Disabled(harness, deadline, Dice));
            Assert.True(await Disabled(harness, deadline, Copy));
            Assert.Equal(0, await Count(harness, deadline, Palette));
            output.WriteLine("off, light: " + await harness.ScreenshotAsync(deadline, "off-light"));

            // Random on with no seed stored rolls one.
            await ClickSwitchAsync(harness, deadline);
            var first = await SeedValue(harness, deadline);
            Assert.StartsWith("hx1-", first);
            Assert.True(await Checked(harness, deadline, Random));
            Assert.False(await Disabled(harness, deadline, Dice));
            Assert.Equal(1, await Count(harness, deadline, Palette));
            Assert.Equal(first, await Stored(harness, deadline, "hatch.palette"));
            Assert.Equal(first, await Stored(harness, deadline, "hatch.palette.last"));
            output.WriteLine("on, light: " + await harness.ScreenshotAsync(deadline, "on-light"));

            // The dice rolls another.
            await harness.ClickAsync(deadline, Dice);
            var second = await SeedValue(harness, deadline);
            Assert.NotEqual(first, second);
            Assert.Equal(second, await Stored(harness, deadline, "hatch.palette"));

            // A refused paste: said inline in words, nothing changes.
            await deadline.BoundAsync("typing a bad seed", async () =>
            {
                await page.Locator(Seed).FillAsync("hx1-nope");
                await page.Locator(Seed).PressAsync("Enter");
            });
            var alert = await deadline.BoundAsync("reading the refusal", () => page.Locator("[role=alert].hatch-theme-picker__message").InnerTextAsync());
            Assert.Contains("characters", alert);
            Assert.Equal("true", await deadline.BoundAsync("aria-invalid", () => page.Locator(Seed).GetAttributeAsync("aria-invalid")));
            Assert.Equal(second, await Stored(harness, deadline, "hatch.palette"));
            output.WriteLine("refused, light: " + await harness.ScreenshotAsync(deadline, "refused-light"));

            // Escape puts the seed back and clears the refusal.
            await deadline.BoundAsync("escaping", () => page.Locator(Seed).PressAsync("Escape"));
            Assert.Equal(second, await SeedValue(harness, deadline));
            Assert.Equal(0, await Count(harness, deadline, "[role=alert].hatch-theme-picker__message"));

            // A pasted seed applies and keeps Random on.
            await deadline.BoundAsync("pasting a seed", async () =>
            {
                await page.Locator(Seed).FillAsync($"  {first}  ");
                await page.Locator(Seed).PressAsync("Enter");
            });
            Assert.Equal(first, await Stored(harness, deadline, "hatch.palette"));
            Assert.Equal(first, await SeedValue(harness, deadline));

            // Random off: the house palette, no reload, the seed kept.
            await ClickSwitchAsync(harness, deadline);
            Assert.False(await Checked(harness, deadline, Random));
            Assert.Equal(0, await Count(harness, deadline, Palette));
            Assert.Null(await Stored(harness, deadline, "hatch.palette"));
            Assert.Equal(first, await Stored(harness, deadline, "hatch.palette.last"));
            Assert.Equal(first, await SeedValue(harness, deadline));
            Assert.True(await Disabled(harness, deadline, Dice));

            // ... and back on: the same theme, not a new roll.
            await ClickSwitchAsync(harness, deadline);
            Assert.Equal(first, await Stored(harness, deadline, "hatch.palette"));
            Assert.Equal(1, await Count(harness, deadline, Palette));

            // The same, in dark.
            await deadline.BoundAsync("choosing dark", () => page.Locator(".hatch-theme-switch__option", new() { HasText = "Dark" }).ClickAsync());
            output.WriteLine("on, dark: " + await harness.ScreenshotAsync(deadline, "on-dark"));
            await ClickSwitchAsync(harness, deadline);
            output.WriteLine("off, dark: " + await harness.ScreenshotAsync(deadline, "off-dark"));
            await ClickSwitchAsync(harness, deadline);

            // The seed survives a reload, and the picker shows it.
            await harness.GoToAsync(deadline, "");
            await harness.WaitForTopBarAsync(deadline);
            await harness.ClickAsync(deadline, E2eHarness.GearSelector);
            await harness.WaitForVisibleAsync(deadline, Seed);
            Assert.Equal(first, await SeedValue(harness, deadline));
            Assert.True(await Checked(harness, deadline, Random));

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

    /// <summary>The visible track, which is what a person presses; the input itself is clipped to a pixel.</summary>
    private static Task ClickSwitchAsync(E2eHarness harness, Deadline deadline) =>
        harness.ClickAsync(deadline, ".hatch-theme-picker__random");

    private static Task<bool> Checked(E2eHarness harness, Deadline deadline, string selector) =>
        deadline.BoundAsync("reading checked", () => harness.Page!.Locator(selector).First.IsCheckedAsync());

    private static Task<bool> Disabled(E2eHarness harness, Deadline deadline, string selector) =>
        deadline.BoundAsync("reading disabled", () => harness.Page!.Locator(selector).First.IsDisabledAsync());

    private static Task<int> Count(E2eHarness harness, Deadline deadline, string selector) =>
        deadline.BoundAsync("counting", () => harness.Page!.Locator(selector).CountAsync());

    private static Task<string> SeedValue(E2eHarness harness, Deadline deadline) =>
        deadline.BoundAsync("reading the seed", () => harness.Page!.Locator(Seed).InputValueAsync());

    private static Task<string?> Stored(E2eHarness harness, Deadline deadline, string key) =>
        harness.EvaluateAsync<string?>(deadline, $"reading {key}", "key => window.localStorage.getItem(key)", key);
}

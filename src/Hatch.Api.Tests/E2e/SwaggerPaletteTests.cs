using Hatch.Api.Tests.Hatch;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2323's Swagger UI, rendered: the shared bar on top, Swagger's own bar
/// gone, and the page painted from the tokens so a stored Random palette (or a
/// <c>?palette=</c> link) reaches it in light and dark. What the page looks like
/// is a person's to judge, so the screenshots of each state land in the case's
/// artifact directory; what a case can assert is that the ground moved.
///
/// <para>Chromium only: the bar's fit on a phone is measured by the bar cases,
/// and WebKit does not open a page on macOS 14.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class SwaggerPaletteTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Page = "/swagger/index.html";
    private const string Palette = "style[data-theme-palette]";

    /// <summary>Swagger's own bar, which the sheet hides: the house bar replaces it.</summary>
    private const string SwaggerTopBar = ".swagger-ui .topbar";

    /// <summary>
    /// paletteSeed.test.ts's "mid-range" pin - not <c>hx1-AAAAAAAA</c> ("every
    /// minimum"), whose tint of 0 derives every neutral, <c>--bg</c> included,
    /// at zero chroma: the same hex the house block already carries, in both
    /// themes. This seed's tint is non-zero, so the page the test asserts is
    /// shifted actually is.
    /// </summary>
    private const string Seed = "hx1-M2aZzDNm";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task SwaggerUi_WearsTheBar_AndPaintsAStoredPalette_InLightAndDark()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");
        Skip.IfNot(File.Exists(E2eHarness.ChromeBundle()), $"{E2eHarness.ChromeBundle()} is missing: build it first (`make test-e2e`, or `npm run build -w apps/chrome` in src/Hatch.Web)");

        var harness = new E2eHarness("SwaggerPalette");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);
            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);

            // Nothing stored: the house palette. The bar renders, Swagger's own
            // does not, and no palette style is on the page.
            await harness.OpenAsync(deadline, baseUrl, Page);
            await WaitForSwaggerAsync(harness, deadline);
            Assert.False(await harness.IsVisibleAsync(deadline, SwaggerTopBar), "Swagger's own bar is still showing under the house bar");
            Assert.Equal(0, await Count(harness, deadline, Palette));
            var houseLight = await BodyBackground(harness, deadline);
            output.WriteLine("house, light: " + await harness.ScreenshotAsync(deadline, "house-light"));

            await ReloadAsync(harness, deadline, theme: "dark", seed: null);
            var houseDark = await BodyBackground(harness, deadline);
            Assert.NotEqual(houseLight, houseDark);
            output.WriteLine("house, dark: " + await harness.ScreenshotAsync(deadline, "house-dark"));

            // A stored seed: the shifted palette, in light and in dark, each
            // different from the house ground it replaces.
            await ReloadAsync(harness, deadline, theme: "light", seed: Seed);
            Assert.Equal(1, await Count(harness, deadline, Palette));
            Assert.NotEqual(houseLight, await BodyBackground(harness, deadline));
            output.WriteLine("palette, light: " + await harness.ScreenshotAsync(deadline, "palette-light"));

            await ReloadAsync(harness, deadline, theme: "dark", seed: Seed);
            Assert.Equal(1, await Count(harness, deadline, Palette));
            Assert.NotEqual(houseDark, await BodyBackground(harness, deadline));
            output.WriteLine("palette, dark: " + await harness.ScreenshotAsync(deadline, "palette-dark"));

            // An expanded operation, so the page below the fold is seen too: the
            // sheet maps Swagger's class names, and a renamed one shows here.
            await deadline.BoundAsync("expanding the first operation", () => harness.Page!.Locator(".swagger-ui .opblock-summary-control").First.ClickAsync());
            await harness.WaitForVisibleAsync(deadline, ".swagger-ui .opblock.is-open .opblock-body");
            output.WriteLine("palette, dark, expanded: " + await harness.ScreenshotAsync(deadline, "palette-dark-expanded"));

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
    public async Task SwaggerUi_AppliesASharedLinkOnArrival_AndCleansTheAddress()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");
        Skip.IfNot(File.Exists(E2eHarness.ChromeBundle()), $"{E2eHarness.ChromeBundle()} is missing: build it first (`make test-e2e`, or `npm run build -w apps/chrome` in src/Hatch.Web)");

        var harness = new E2eHarness("SwaggerSharedLink");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);
            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);

            await harness.OpenAsync(deadline, baseUrl, $"{Page}?palette={Seed}");
            await WaitForSwaggerAsync(harness, deadline);

            Assert.DoesNotContain("palette", harness.Page!.Url);
            Assert.Equal(Seed, await harness.EvaluateAsync<string?>(deadline, "reading hatch.palette", "() => window.localStorage.getItem('hatch.palette')"));
            Assert.Equal(1, await Count(harness, deadline, Palette));
            output.WriteLine("shared link applied: " + await harness.ScreenshotAsync(deadline, "shared-link-applied"));

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

    /// <summary>The bar is up and Swagger has drawn the document, which it does after the script has run.</summary>
    private static async Task WaitForSwaggerAsync(E2eHarness harness, Deadline deadline)
    {
        await harness.WaitForTopBarAsync(deadline);
        await harness.WaitForVisibleAsync(deadline, ".swagger-ui .opblock-tag");
    }

    /// <summary>Stores the theme choice and the seed (or none), then reloads, which is how a person's stored palette reaches the page.</summary>
    private static async Task ReloadAsync(E2eHarness harness, Deadline deadline, string theme, string? seed)
    {
        await harness.EvaluateAsync<bool>(
            deadline, "storing the theme and palette",
            "([theme, seed]) => { window.localStorage.setItem('hatch.theme', theme); if (seed) window.localStorage.setItem('hatch.palette', seed); else window.localStorage.removeItem('hatch.palette'); return true; }",
            new object?[] { theme, seed });
        await deadline.BoundAsync("reloading", () => harness.Page!.ReloadAsync());
        await WaitForSwaggerAsync(harness, deadline);
    }

    private static Task<string> BodyBackground(E2eHarness harness, Deadline deadline) =>
        harness.EvaluateAsync<string>(deadline, "reading the page ground", "() => getComputedStyle(document.body).backgroundColor");

    private static Task<int> Count(E2eHarness harness, Deadline deadline, string selector) =>
        deadline.BoundAsync("counting", () => harness.Page!.Locator(selector).CountAsync());
}

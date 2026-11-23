using Hatch.Api.Tests.Hatch;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2767's acceptance at desktop width, driven in Chromium: pressing the sparkline of Moved, In flight, Oldest in
/// flight or Waiting on you opens that tile's dialog, titled with the tile and the window. Escape closes it with
/// focus back on the sparkline; Enter and Space on the focused sparkline open it; an arrow key marks a bar and
/// opens nothing. Pressing 4h inside the dialog presses the strip's own 4h and retitles the dialog. Pressing the
/// In flight sparkline leaves the address alone, while that tile's label keeps its link to the report.
///
/// <para>Each dialog is left in the artifact directory as a screenshot in the light and the dark theme, at rest
/// and with a bar marked. Runners is not covered: it does nothing when pressed until its own dialog lands.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class BoardHealthDialogTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 240;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Dialog = "[role='dialog'] .hatch-health-dialog";

    private static readonly (string Tile, string Title)[] Tiles =
    [
        ("moved", "Moved"),
        ("inFlight", "In flight"),
        ("oldest", "Oldest in flight"),
        ("waiting", "Waiting on you"),
    ];

    private static string Spark(string tile) => $"[data-tile='{tile}'] [data-part='spark']";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task EachOfTheFourTiles_OpensItsDialog_ByPressAndByKeyboard()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("BoardHealthDialog");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);
            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            await deadline.BoundAsync("filing the issues", () => BoardHealthStripTests.SeedAsync(baseUrl));

            await harness.OpenAsync(deadline, baseUrl, "/", reducedMotion: true);
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForFirstColumnAsync(deadline);
            var page = harness.Page!;
            await harness.WaitForVisibleAsync(deadline, ".hatch-health-strip");
            var address = page.Url;

            foreach (var (tile, title) in Tiles)
            {
                // A press on the sparkline opens the dialog, titled with the tile and the window.
                await harness.ClickAsync(deadline, Spark(tile));
                await harness.WaitForVisibleAsync(deadline, $"{Dialog}[data-dialog='{tile}']");
                Assert.Equal($"{title}, last 8h", Assert.Single(await Texts(harness, deadline, "[role='dialog'] h3")));
                Assert.Equal("8h", Assert.Single(await Texts(harness, deadline, $"{Dialog} .hatch-health-window[aria-pressed='true']")));
                Assert.Equal(address, page.Url);
                // The dialog marks nothing as a tile, so the strip's own five stay five.
                Assert.Equal(5, await page.Locator("section[data-tile]").CountAsync());

                // It draws a chart, a legend and a sentence.
                Assert.True(await page.Locator($"{Dialog} .hatch-health-dialog-svg").IsVisibleAsync());
                Assert.True(await page.Locator($"{Dialog} .hatch-health-dialog-legend li").CountAsync() >= 2);
                Assert.False(string.IsNullOrWhiteSpace(Assert.Single(await Texts(harness, deadline, $"{Dialog} [data-part='words']"))));

                // The chart is drawn at the size it is shown: one unit to a pixel, so a pattern is not skewed.
                await harness.WaitForFunctionAsync(
                    deadline, "waiting for the chart to draw at its measured size",
                    """
                    () => {
                      const svg = document.querySelector("[role='dialog'] .hatch-health-dialog-svg");
                      const box = svg.getBoundingClientRect();
                      const view = svg.viewBox.baseVal;
                      return box.width > 100 && Math.abs(box.width - view.width) < 1 && Math.abs(box.height - view.height) < 1;
                    }
                    """);

                // An arrow key marks a bar and the dialog says it; Escape closes the dialog, not just the mark.
                await deadline.BoundAsync("focusing the chart", () => page.Locator($"{Dialog} [data-part='chart']").FocusAsync());
                var resting = Assert.Single(await Texts(harness, deadline, $"{Dialog} [data-part='words']"));
                await deadline.BoundAsync("stepping right", () => page.Keyboard.PressAsync("ArrowRight"));
                await harness.WaitForFunctionAsync(deadline, "waiting for the mark", "() => document.querySelectorAll('.hatch-health-dialog-moment').length === 1");
                Assert.NotEqual(resting, Assert.Single(await Texts(harness, deadline, $"{Dialog} [data-part='words']")));
                // The mark is the strip's one marked moment: the four tiles behind the dialog draw it too, and
                // the dialog's own line is not one of theirs.
                Assert.Equal(4, await page.Locator(".hatch-health-moment").CountAsync());

                foreach (var theme in new[] { "light", "dark" })
                {
                    await harness.EvaluateAsync<bool>(deadline, "setting the theme", $"() => {{ document.documentElement.setAttribute('data-theme', '{theme}'); return true; }}");
                    await Task.Delay(300);
                    await harness.ScreenshotAsync(deadline, $"board-health-dialog-{tile}-{theme}");
                }

                await deadline.BoundAsync("pressing Escape", () => page.Keyboard.PressAsync("Escape"));
                await harness.WaitForHiddenAsync(deadline, "[role='dialog']");
                await harness.WaitForFunctionAsync(deadline, "waiting for focus to return", FocusIsOnSpark, tile);
                Assert.Equal(0, await page.Locator(".hatch-health-dialog-moment").CountAsync());

                // Enter opens it from the keyboard, and so does Space.
                foreach (var key in new[] { "Enter", "Space" })
                {
                    await deadline.BoundAsync("focusing the sparkline", () => page.Locator(Spark(tile)).FocusAsync());
                    await deadline.BoundAsync($"pressing {key}", () => page.Keyboard.PressAsync(key));
                    await harness.WaitForVisibleAsync(deadline, $"{Dialog}[data-dialog='{tile}']");
                    await deadline.BoundAsync("pressing Escape", () => page.Keyboard.PressAsync("Escape"));
                    await harness.WaitForHiddenAsync(deadline, "[role='dialog']");
                    await harness.WaitForFunctionAsync(deadline, "waiting for focus to return", FocusIsOnSpark, tile);
                }

                // An arrow key on the sparkline marks a bar across the strip, and opens nothing.
                await deadline.BoundAsync("stepping left", () => page.Keyboard.PressAsync("ArrowLeft"));
                await harness.WaitForFunctionAsync(deadline, "waiting for the step", "() => document.querySelectorAll('.hatch-health-moment').length === 4");
                Assert.Equal(0, await page.Locator("[role='dialog']").CountAsync());
                await deadline.BoundAsync("pressing Escape", () => page.Keyboard.PressAsync("Escape"));
                await harness.WaitForFunctionAsync(deadline, "waiting for Escape", "() => document.querySelectorAll('.hatch-health-moment').length === 0");
                Assert.Equal(0, await page.Locator("[role='dialog']").CountAsync());
            }

            // Pressing In flight's sparkline leaves the address alone; its label still links to the report, and
            // the dialog carries the same link.
            var label = page.Locator("[data-tile='inFlight'] a.hatch-health-link");
            var href = await label.GetAttributeAsync("href");
            Assert.StartsWith("/reports/cumulative-flow", href);
            await harness.ClickAsync(deadline, Spark("inFlight"));
            await harness.WaitForVisibleAsync(deadline, $"{Dialog}[data-dialog='inFlight']");
            Assert.Equal(address, page.Url);
            Assert.Equal(href, await page.Locator($"{Dialog} a.hatch-health-dialog-link").GetAttributeAsync("href"));
            Assert.Equal("Open the cumulative flow report", Assert.Single(await Texts(harness, deadline, $"{Dialog} a.hatch-health-dialog-link")));
            await deadline.BoundAsync("pressing Escape", () => page.Keyboard.PressAsync("Escape"));
            await harness.WaitForHiddenAsync(deadline, "[role='dialog']");

            // A click outside the panel closes it too.
            await harness.ClickAsync(deadline, Spark("moved"));
            await harness.WaitForVisibleAsync(deadline, Dialog);
            await deadline.BoundAsync("clicking outside", () => page.Locator(".hatch-modal__overlay").ClickAsync(new() { Position = new() { X = 4, Y = 4 } }));
            await harness.WaitForHiddenAsync(deadline, "[role='dialog']");

            // Pressing 4h in the dialog sets the strip's own window, and the title follows the answer's.
            await harness.ClickAsync(deadline, Spark("moved"));
            await harness.WaitForVisibleAsync(deadline, Dialog);
            await harness.ClickAsync(deadline, $"{Dialog} .hatch-health-window:text-is('4h')");
            await harness.WaitForFunctionAsync(
                deadline, "waiting for the title to follow 4h",
                "() => document.querySelector(\"[role='dialog'] h3\")?.textContent.trim() === 'Moved, last 4h'");
            Assert.Equal("4h", Assert.Single(await Texts(harness, deadline, $"{Dialog} .hatch-health-window[aria-pressed='true']")));
            Assert.Equal("4h", Assert.Single(await Texts(harness, deadline, ".hatch-health-control .hatch-health-window[aria-pressed='true']")));
            Assert.EndsWith("10 min bars", Assert.Single(await Texts(harness, deadline, $"{Dialog} .hatch-health-axis")));
            await deadline.BoundAsync("pressing Escape", () => page.Keyboard.PressAsync("Escape"));
            await harness.WaitForHiddenAsync(deadline, "[role='dialog']");

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

    /// <summary>Whether focus is on <c>tile</c>'s own sparkline - where Modal hands it back.</summary>
    private const string FocusIsOnSpark =
        "(tile) => document.activeElement === document.querySelector(`[data-tile='${tile}'] [data-part='spark']`)";

    private static async Task<string[]> Texts(E2eHarness harness, Deadline deadline, string selector) =>
        await harness.EvaluateAsync<string[]>(
            deadline, $"reading {selector}",
            "(selector) => [...document.querySelectorAll(selector)].map((e) => e.textContent.trim())", selector);
}

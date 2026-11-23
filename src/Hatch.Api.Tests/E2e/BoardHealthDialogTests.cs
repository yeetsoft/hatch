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
/// and with a bar marked. The Runners dialog, which draws a row for every lane rather than a chart, has its own
/// case, <see cref="TheRunnersTile_OpensADialogOfEveryLane"/> (HA-2768).</para>
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

    /// <summary>
    /// HA-2768: pressing the Runners sparkline opens a dialog of every lane - more than the eight the tile draws,
    /// since ten more runners are minted beside the seed's own. Key still opens the key and not the dialog. The
    /// lanes scroll in a box whose time axis stays put; an arrow key, or the pointer over the cells, marks one bar
    /// across every row and one sentence names each runner. Escape returns focus to the sparkline; Enter and Space
    /// open it. Left as screenshots in the light and the dark theme, at rest, marked and scrolled.
    /// </summary>
    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheRunnersTile_OpensADialogOfEveryLane()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("BoardHealthRunnersDialog");
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
            var minted = await deadline.BoundAsync("minting the runners", () => BoardHealthStripTests.SeedRunnersAsync(baseUrl, 10));

            await harness.OpenAsync(deadline, baseUrl, "/", reducedMotion: true);
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForFirstColumnAsync(deadline);
            var page = harness.Page!;
            await harness.WaitForVisibleAsync(deadline, ".hatch-health-strip");

            // The tile draws eight lanes and says how many it left out.
            Assert.Equal(8, await page.Locator("[data-tile='runners'] .hatch-health-lane-cells").CountAsync());

            // Key opens the key, not the dialog.
            await harness.ClickAsync(deadline, "[data-tile='runners'] .hatch-health-key");
            await harness.WaitForFunctionAsync(
                deadline, "waiting for the key",
                "() => document.querySelector(\"[data-tile='runners'] .hatch-health-key\").getAttribute('aria-expanded') === 'true'");
            Assert.Equal(0, await page.Locator("[role='dialog']").CountAsync());
            await deadline.BoundAsync("closing the key", () => page.Keyboard.PressAsync("Escape"));

            // Pressing the sparkline opens it, titled with the tile and the window, with the window control.
            await harness.ClickAsync(deadline, Spark("runners"));
            await harness.WaitForVisibleAsync(deadline, $"{Dialog}[data-dialog='runners']");
            Assert.Equal("Runners, last 8h", Assert.Single(await Texts(harness, deadline, "[role='dialog'] h3")));
            Assert.Equal("8h", Assert.Single(await Texts(harness, deadline, $"{Dialog} .hatch-health-window[aria-pressed='true']")));
            Assert.Equal(5, await page.Locator("section[data-tile]").CountAsync());
            Assert.Equal(0, await page.Locator($"{Dialog} svg").CountAsync());

            // Every lane the read answers, with no cap of eight: the seed's own and the ten minted beside it.
            var lanes = await Texts(harness, deadline, $"{Dialog} .hatch-health-dialog-lane-name strong");
            output.WriteLine($"lanes: {string.Join(", ", lanes)}");
            Assert.True(lanes.Length > 8, $"{lanes.Length} lanes drawn, not more than eight");
            foreach (var name in minted) Assert.Contains(name, lanes);
            Assert.Equal($"{lanes.Length} lanes", Assert.Single(await Texts(harness, deadline, $"{Dialog} .hatch-health-dialog-note")));
            Assert.Equal(lanes.Length * 24, await page.Locator($"{Dialog} .hatch-health-dialog-lane-cells .hatch-health-lane-cell").CountAsync());

            // Each row says whether it is up or paused now, and when it was last heard from.
            foreach (var standing in await Texts(harness, deadline, $"{Dialog} [data-part='standing']"))
                Assert.Contains(standing, new[] { "up", "paused", "not up" });
            foreach (var heard in await Texts(harness, deadline, $"{Dialog} [data-part='heard']"))
                Assert.StartsWith("last heard ", heard);

            // The seven legend lines show with no button press.
            Assert.Equal(7, await page.Locator($"{Dialog} .hatch-health-dialog-legend li").CountAsync());
            Assert.Equal(7, await page.Locator($"{Dialog} .hatch-health-dialog-legend li .hatch-health-dialog-lane-swatch .hatch-health-lane-cell").CountAsync());
            Assert.True(await page.Locator($"{Dialog} .hatch-health-dialog-legend").IsVisibleAsync());

            // The pointer over the cells marks one bar across every row; over the names it marks none.
            var cells = await page.Locator($"{Dialog} .hatch-health-dialog-lane-cells").First.BoundingBoxAsync();
            Assert.NotNull(cells);
            var y = (float)(cells.Y + cells.Height / 2);
            await deadline.BoundAsync("pointing at a bar", () => page.Mouse.MoveAsync((float)(cells.X + cells.Width * 0.5), y));
            await harness.WaitForFunctionAsync(deadline, "waiting for the mark", "() => document.querySelectorAll('.hatch-health-dialog-lane-moment').length === 1");
            var markX = await harness.EvaluateAsync<double>(
                deadline, "reading the mark",
                "() => document.querySelector('.hatch-health-dialog-lane-moment').getBoundingClientRect().left");
            Assert.InRange(markX, cells.X, cells.X + cells.Width);
            await deadline.BoundAsync("pointing at a name", () => page.Mouse.MoveAsync((float)(cells.X - 24), y));
            await harness.WaitForFunctionAsync(deadline, "waiting for the mark to clear", "() => document.querySelectorAll('.hatch-health-dialog-lane-moment').length === 0");

            // The box scrolls, and the time axis keeps its place while it does.
            var before = await harness.EvaluateAsync<double[]>(
                deadline, "measuring the box",
                """
                () => {
                  const box = document.querySelector("[role='dialog'] .hatch-health-dialog-lanes");
                  const top = box.getBoundingClientRect().top;
                  return [box.scrollHeight, box.clientHeight,
                    document.querySelector("[role='dialog'] .hatch-health-dialog-lane-axis").getBoundingClientRect().top - top,
                    document.querySelector("[role='dialog'] .hatch-health-dialog-lane").getBoundingClientRect().top - top];
                }
                """);
            Assert.True(before[0] > before[1], $"the lanes do not scroll: scrollHeight {before[0]}, clientHeight {before[1]}");

            await deadline.BoundAsync("focusing the lanes", () => page.Locator($"{Dialog} [data-part='chart']").FocusAsync());
            var resting = Assert.Single(await Texts(harness, deadline, $"{Dialog} [data-part='words']"));
            await deadline.BoundAsync("stepping right", () => page.Keyboard.PressAsync("ArrowRight"));
            await harness.WaitForFunctionAsync(deadline, "waiting for the mark", "() => document.querySelectorAll('.hatch-health-dialog-lane-moment').length === 1");
            var marked = Assert.Single(await Texts(harness, deadline, $"{Dialog} [data-part='words']"));
            Assert.NotEqual(resting, marked);
            foreach (var name in lanes) Assert.Contains($"{name} ", marked);
            Assert.DoesNotContain("not drawn", marked);
            // The strip's own five tiles behind it are marked by the same bar, and the dialog's line is not theirs.
            Assert.Equal(4, await page.Locator(".hatch-health-moment").CountAsync());

            foreach (var theme in new[] { "light", "dark" })
            {
                await harness.EvaluateAsync<bool>(deadline, "setting the theme", $"() => {{ document.documentElement.setAttribute('data-theme', '{theme}'); return true; }}");
                await Task.Delay(300);
                await harness.ScreenshotAsync(deadline, $"board-health-dialog-runners-{theme}");
            }

            // Down scrolls the focused box, since only the four mark keys are taken. Positions are read from the
            // box's own top, so a scroll of the page behind it does not count; the axis stays where it was.
            for (var press = 0; press < 3; press++)
                await deadline.BoundAsync("scrolling the lanes", () => page.Keyboard.PressAsync("ArrowDown"));
            var after = await harness.EvaluateAsync<double[]>(
                deadline, "measuring the box again",
                """
                () => {
                  const box = document.querySelector("[role='dialog'] .hatch-health-dialog-lanes");
                  const top = box.getBoundingClientRect().top;
                  return [box.scrollTop,
                    document.querySelector("[role='dialog'] .hatch-health-dialog-lane-axis").getBoundingClientRect().top - top,
                    document.querySelector("[role='dialog'] .hatch-health-dialog-lane").getBoundingClientRect().top - top];
                }
                """);
            Assert.True(after[0] > 0, "the box did not scroll");
            Assert.True(after[2] < before[3] - 1, "the first lane did not move when the box scrolled");
            Assert.InRange(after[1], before[2] - 1, before[2] + 1);
            await harness.ScreenshotAsync(deadline, "board-health-dialog-runners-scrolled");

            // Escape closes the dialog, not just the mark, and hands focus back to the sparkline.
            await deadline.BoundAsync("pressing Escape", () => page.Keyboard.PressAsync("Escape"));
            await harness.WaitForHiddenAsync(deadline, "[role='dialog']");
            await harness.WaitForFunctionAsync(deadline, "waiting for focus to return", FocusIsOnSpark, "runners");

            // Enter opens it from the keyboard, and so does Space.
            foreach (var key in new[] { "Enter", "Space" })
            {
                await deadline.BoundAsync("focusing the sparkline", () => page.Locator(Spark("runners")).FocusAsync());
                await deadline.BoundAsync($"pressing {key}", () => page.Keyboard.PressAsync(key));
                await harness.WaitForVisibleAsync(deadline, $"{Dialog}[data-dialog='runners']");
                await deadline.BoundAsync("pressing Escape", () => page.Keyboard.PressAsync("Escape"));
                await harness.WaitForHiddenAsync(deadline, "[role='dialog']");
                await harness.WaitForFunctionAsync(deadline, "waiting for focus to return", FocusIsOnSpark, "runners");
            }

            // An arrow key on the sparkline still marks a bar, and opens nothing.
            await deadline.BoundAsync("stepping left", () => page.Keyboard.PressAsync("ArrowLeft"));
            await harness.WaitForFunctionAsync(deadline, "waiting for the step", "() => document.querySelectorAll('.hatch-health-lane-moment').length === 1");
            Assert.Equal(0, await page.Locator("[role='dialog']").CountAsync());

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

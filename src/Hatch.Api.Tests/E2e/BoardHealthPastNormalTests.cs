using System.Text.Json;
using System.Text.Json.Nodes;
using Hatch.Api.Tests.Hatch;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2763, rendered: the warning sign and the "N past normal" line on the Oldest in flight tile lead to the
/// issues they count. With one issue past normal both open that issue; with several both open the tile's dialog
/// with its list narrowed to past normal, once; with none the line is plain text. The dialog's list draws every
/// in-flight issue as a link, with a two-way control and a bar for each stay on one shared scale.
///
/// <para>A scratch database has no column normal (it takes ten finished stays in a column), so the board health
/// read is fetched for real and its <c>inFlightIssues</c> and <c>pastNormal</c> are rewritten in the body with
/// <c>route.FetchAsync</c>; every other route and the bundle are real. The desk runs in Chromium. The phone runs at
/// <c>iPhone 14 Pro</c> in WebKit and again in Chromium, where the controls must be 44px tall and a long list must
/// scroll inside the dialog. Each look is saved as a PNG in the light and the dark theme.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class BoardHealthPastNormalTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 180;

    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Dialog = "[role='dialog'] .hatch-health-dialog[data-dialog='oldest']";
    private const string Rows = $"{Dialog} .hatch-health-dialog-issues li";
    private const string List = $"{Dialog} .hatch-health-dialog-issues";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheWarning_LeadsToTheIssues_AtTheDesktopProfile() =>
        RunAsync("BoardHealthPastNormalDesktop", "past-normal-desktop", null, phone: false);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheWarning_LeadsToTheIssues_AtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("BoardHealthPastNormalPhone", "past-normal-phone", DeviceProfile.IPhone14Pro, phone: true);
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheWarning_LeadsToTheIssues_AtThePhoneProfileInChromium() =>
        RunAsync("BoardHealthPastNormalPhoneChromium", "past-normal-phone-chromium", DeviceProfile.IPhone14ProChromium, phone: true);

    private async Task RunAsync(string name, string label, DeviceProfile? profile, bool phone)
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness(name);
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        // What the stubbed read answers now: how many issues are past normal, and how many more are in flight.
        var shape = new Shape(0, 2);
        try
        {
            await harness.LaunchBrowserAsync(deadline, profile);
            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            await deadline.BoundAsync("filing the issues", () => BoardHealthStripTests.SeedAsync(baseUrl));

            await harness.OpenAsync(deadline, baseUrl, "/", context => StubHealth(context, shape), reducedMotion: true);
            var page = harness.Page!;
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForFirstColumnAsync(deadline);

            var tile = phone ? ".hatch-health-strip--stacked [data-tile='oldest']" : "[data-tile='oldest']";
            var sign = $"{tile} .hatch-health-glyph-lead";
            var line = $"{tile} .hatch-health-second-lead";

            // ---- None past normal: the line is words, not a control, and no sign is drawn. ----
            await OpenStripAsync(harness, deadline, phone, tile);
            Assert.Equal("none past normal", Assert.Single(await Texts(harness, deadline, $"{tile} [data-part='second']")));
            Assert.Equal(0, await page.Locator($"{tile} [data-part='second'] a, {tile} [data-part='second'] button").CountAsync());
            Assert.Equal(0, await page.Locator($"{tile} .hatch-health-glyph, {tile} .hatch-health-lead").CountAsync());

            // ---- Exactly one: the sign and the line each open that issue, and no dialog. ----
            shape.Set(1, 2, "HLT-1");
            await ReloadStripAsync(harness, deadline, phone, tile);
            Assert.Equal("1 past normal", Assert.Single(await Texts(harness, deadline, line)));
            foreach (var control in new[] { sign, line })
            {
                Assert.Equal("Open HLT-1, the issue past normal", await page.Locator(control).GetAttributeAsync("aria-label"));
                Assert.Equal("A", await Tag(harness, deadline, control));
                await AssertFingerHighAsync(harness, deadline, control, phone);
                await harness.ClickAsync(deadline, control);
                await deadline.BoundAsync("waiting for the issue", () => page.WaitForURLAsync("**/issues/HLT-1"));
                Assert.Equal(0, await page.Locator("[role='dialog']").CountAsync());
                await harness.GoToAsync(deadline, "/");
                await harness.WaitForFirstColumnAsync(deadline);
                await OpenStripAsync(harness, deadline, phone, tile);
            }

            // ---- Several: either control opens one dialog, narrowed to past normal. ----
            shape.Set(3, 4);
            await ReloadStripAsync(harness, deadline, phone, tile);
            Assert.Equal("3 past normal", Assert.Single(await Texts(harness, deadline, line)));
            foreach (var control in new[] { line, sign })
            {
                Assert.Equal("Open the 3 issues past normal", await page.Locator(control).GetAttributeAsync("aria-label"));
                Assert.Equal("BUTTON", await Tag(harness, deadline, control));
                await AssertFingerHighAsync(harness, deadline, control, phone);

                await harness.ClickAsync(deadline, control);
                await harness.WaitForVisibleAsync(deadline, Dialog);
                // One dialog, however it was opened: the press does not also open it the way a tile press does.
                Assert.Equal(1, await page.Locator("[role='dialog']").CountAsync());
                Assert.Equal(3, await page.Locator(Rows).CountAsync());
                // The list is what was asked for, so it is in view when the dialog opens, phone or desk.
                await harness.WaitForFunctionAsync(
                    deadline, "waiting for the list to come into view",
                    "() => { const r = document.querySelector(\"[role='dialog'] .hatch-health-dialog-issues li\").getBoundingClientRect(); return r.top >= 0 && r.bottom <= window.innerHeight; }");
                Assert.Equal(3, await page.Locator($"{Rows}:has-text('past normal'):has(.hatch-health-glyph)").CountAsync());
                Assert.Equal("Past normal 3", Assert.Single(await Texts(harness, deadline, $"{Dialog} [data-part='issue-filter'] [aria-pressed='true']")));
                Assert.Equal("All 7", Assert.Single(await Texts(harness, deadline, $"{Dialog} [data-part='issue-filter'] [aria-pressed='false']")));
                Assert.Equal(3, await page.Locator($"{Rows} a[href^='/issues/']").CountAsync());

                // The rows are longest stay first, and the legend is above the list.
                var keys = await Texts(harness, deadline, $"{Rows} strong");
                Assert.Equal(["HLT-901", "HLT-902", "HLT-903"], keys);
                var legendBottom = await page.Locator($"{Dialog} .hatch-health-dialog-legend").BoundingBoxAsync();
                var listTop = await page.Locator(List).BoundingBoxAsync();
                Assert.True(legendBottom!.Y + legendBottom.Height <= listTop!.Y + 0.5, "the list is not under the legend");

                if (control == line)
                {
                    // Every row is a finger high on a phone, and the control's buttons too.
                    await AssertFingerHighAsync(harness, deadline, $"{Rows} a", phone);
                    await AssertFingerHighAsync(harness, deadline, $"{Dialog} [data-part='issue-filter'] button", phone);

                    // All shows the rest: every issue, with a bar for each row whose column has a normal.
                    await harness.ClickAsync(deadline, $"{Dialog} [data-part='issue-filter'] button:has-text('All')");
                    await harness.WaitForFunctionAsync(
                        deadline, "waiting for every issue",
                        "() => document.querySelectorAll(\"[role='dialog'] .hatch-health-dialog-issues li\").length === 7");
                    Assert.Equal(7, await page.Locator(Rows).CountAsync());
                    Assert.Equal(3, await page.Locator($"{Rows}:has(.hatch-health-glyph)").CountAsync());
                    Assert.Equal(1, await page.Locator($"{Rows}:has-text('aging')").CountAsync());
                    Assert.Equal(2, await page.Locator($"{Rows}:has-text('healthy')").CountAsync());
                    var none = page.Locator($"{Rows}:has-text('no normal yet')");
                    Assert.Equal(1, await none.CountAsync());
                    Assert.Equal(0, await none.Locator(".hatch-health-dialog-issue-track").CountAsync());
                    Assert.Equal(6, await page.Locator($"{Rows} .hatch-health-dialog-issue-track").CountAsync());

                    // One scale: the ticks line up down the list, and the one at twice its normal reads past the tick.
                    var ticks = await harness.EvaluateAsync<double[]>(
                        deadline, "reading the ticks",
                        """
                        () => [...document.querySelectorAll("[role='dialog'] .hatch-health-dialog-issue-track")].map((track) => {
                          const box = track.getBoundingClientRect();
                          return (track.querySelector('.hatch-health-dialog-issue-normal').getBoundingClientRect().left - box.left) / box.width;
                        })
                        """);
                    Assert.True(ticks.Max() - ticks.Min() < 0.01, $"the normal ticks differ: {string.Join(", ", ticks)}");
                    var past = await harness.EvaluateAsync<double[]>(
                        deadline, "reading the bars",
                        """
                        () => [...document.querySelectorAll("[role='dialog'] .hatch-health-dialog-issue-track")].map((track) => {
                          const box = track.getBoundingClientRect();
                          const fill = track.querySelector('.hatch-health-dialog-issue-fill').getBoundingClientRect();
                          const tick = track.querySelector('.hatch-health-dialog-issue-normal').getBoundingClientRect();
                          return (fill.right - tick.left) / box.width;
                        })
                        """);
                    Assert.True(past[0] > 0, "the longest stay does not reach past its normal's tick");
                    Assert.True(past[^1] < 0, "the shortest stay passes its normal's tick");
                    await harness.ClickAsync(deadline, $"{Dialog} [data-part='issue-filter'] button:has-text('Past normal')");
                    await harness.WaitForFunctionAsync(
                        deadline, "waiting for the narrowed list",
                        "() => document.querySelectorAll(\"[role='dialog'] .hatch-health-dialog-issues li\").length === 3");

                    await ScreenshotsAsync(harness, deadline, $"{label}-narrowed");
                }

                await deadline.BoundAsync("pressing Escape", () => page.Keyboard.PressAsync("Escape"));
                await harness.WaitForHiddenAsync(deadline, "[role='dialog']");
                // Focus goes back to the control that opened it.
                await harness.WaitForFunctionAsync(
                    deadline, "waiting for focus to return",
                    "(selector) => document.activeElement === document.querySelector(selector)", control);
            }

            // ---- A long list scrolls inside the dialog, and the dialog does not grow with it. ----
            shape.Set(3, 30);
            await ReloadStripAsync(harness, deadline, phone, tile);
            await harness.ClickAsync(deadline, line);
            await harness.WaitForVisibleAsync(deadline, Dialog);
            await harness.ClickAsync(deadline, $"{Dialog} [data-part='issue-filter'] button:has-text('All')");
            await harness.WaitForFunctionAsync(
                deadline, "waiting for every issue",
                "() => document.querySelectorAll(\"[role='dialog'] .hatch-health-dialog-issues li\").length === 33");
            var box = await harness.EvaluateAsync<double[]>(
                deadline, "measuring the list",
                """
                () => {
                  const list = document.querySelector("[role='dialog'] .hatch-health-dialog-issues");
                  const panel = document.querySelector("[role='dialog']").getBoundingClientRect();
                  return [list.scrollHeight, list.clientHeight, panel.height, panel.bottom, window.innerHeight, list.getBoundingClientRect().height];
                }
                """);
            output.WriteLine($"list: scrollHeight {box[0]}, clientHeight {box[1]}, dialog {box[2]}px, bottom {box[3]} of {box[4]}");
            Assert.True(box[0] > box[1] + 1, $"the list does not scroll: scrollHeight {box[0]}, clientHeight {box[1]}");
            Assert.True(box[3] <= box[4] + 0.5, $"the dialog runs past the bottom of the screen: {box[3]} of {box[4]}");
            Assert.True(box[5] <= Math.Max(22 * 16, 0) + 1, $"the list is {box[5]}px high, past 22rem");

            await harness.EvaluateAsync<bool>(deadline, "scrolling the list", "() => { const l = document.querySelector(\"[role='dialog'] .hatch-health-dialog-issues\"); l.scrollTop = l.scrollHeight; return true; }");
            await Task.Delay(300);
            var after = await harness.EvaluateAsync<double[]>(
                deadline, "measuring the list again",
                """
                () => {
                  const list = document.querySelector("[role='dialog'] .hatch-health-dialog-issues");
                  return [list.scrollTop, document.querySelector("[role='dialog']").getBoundingClientRect().height];
                }
                """);
            Assert.True(after[0] > 0, "the list did not scroll");
            Assert.InRange(after[1], box[2] - 1, box[2] + 1);
            await ScreenshotsAsync(harness, deadline, $"{label}-long");
            await deadline.BoundAsync("pressing Escape", () => page.Keyboard.PressAsync("Escape"));
            await harness.WaitForHiddenAsync(deadline, "[role='dialog']");

            if (phone)
            {
                var overflow = await harness.EvaluateAsync<bool>(
                    deadline, "measuring the page", "() => document.documentElement.scrollWidth > window.innerWidth");
                Assert.False(overflow, "the page scrolls sideways");
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

    /// <summary>On a phone the strip opens from its row; on the desk it is already drawn.</summary>
    private static async Task OpenStripAsync(E2eHarness harness, Deadline deadline, bool phone, string tile)
    {
        if (phone && !await harness.IsVisibleAsync(deadline, tile))
        {
            await harness.WaitForVisibleAsync(deadline, ".hatch-health-row");
            await harness.ClickAsync(deadline, ".hatch-health-row");
        }

        await harness.WaitForVisibleAsync(deadline, tile);
    }

    /// <summary>Loads the page again so the strip reads the shape the case has just set.</summary>
    private static async Task ReloadStripAsync(E2eHarness harness, Deadline deadline, bool phone, string tile)
    {
        await harness.GoToAsync(deadline, "/");
        await harness.WaitForFirstColumnAsync(deadline);
        await OpenStripAsync(harness, deadline, phone, tile);
    }

    /// <summary>On a phone, the control's box is at least 44px high.</summary>
    private static async Task AssertFingerHighAsync(E2eHarness harness, Deadline deadline, string selector, bool phone)
    {
        if (!phone) return;

        var heights = await harness.EvaluateAsync<double[]>(
            deadline, $"measuring {selector}",
            "(selector) => [...document.querySelectorAll(selector)].map((e) => e.getBoundingClientRect().height)", selector);
        Assert.NotEmpty(heights);
        foreach (var height in heights) Assert.True(height >= 43.5, $"{selector} is {height}px high on a phone, under 44");
    }

    private static async Task ScreenshotsAsync(E2eHarness harness, Deadline deadline, string label)
    {
        foreach (var theme in new[] { "light", "dark" })
        {
            await harness.EvaluateAsync<bool>(deadline, "setting the theme", $"() => {{ document.documentElement.setAttribute('data-theme', '{theme}'); return true; }}");
            await Task.Delay(300);
            await harness.ScreenshotAsync(deadline, $"{label}-{theme}");
        }
    }

    private static async Task<string> Tag(E2eHarness harness, Deadline deadline, string selector) =>
        await harness.EvaluateAsync<string>(deadline, $"reading {selector}", "(selector) => document.querySelector(selector).tagName", selector);

    private static async Task<string[]> Texts(E2eHarness harness, Deadline deadline, string selector) =>
        await harness.EvaluateAsync<string[]>(
            deadline, $"reading {selector}",
            "(selector) => [...document.querySelectorAll(selector)].map((e) => e.textContent.trim())", selector);

    /// <summary>What the stubbed read lists: <c>Past</c> issues past normal, then <c>Others</c> more in flight.</summary>
    private sealed class Shape(int past, int others)
    {
        public int Past { get; private set; } = past;
        public int Others { get; private set; } = others;
        public string? OnlyKey { get; private set; }

        public void Set(int past, int others, string? onlyKey = null)
        {
            Past = past;
            Others = others;
            OnlyKey = onlyKey;
        }
    }

    /// <summary>
    /// The real board health read, with its in-flight list and past normal count replaced. The past issues stood
    /// 2.5 normals, then less, longest first; the others are one aging, then healthy ones, and last one in a
    /// column with no normal yet, so every band and the missing bar are drawn.
    /// </summary>
    private static Task StubHealth(IBrowserContext context, Shape shape) =>
        context.RouteAsync("**/api/board/health**", async route =>
        {
            var response = await route.FetchAsync();
            var body = JsonNode.Parse(await response.TextAsync())!.AsObject();
            var issues = new JsonArray();
            const int normal = 7200;

            JsonObject Issue(string key, string title, int seconds, int? normalSeconds, string band) => new()
            {
                ["key"] = key,
                ["title"] = title,
                ["statusId"] = 3,
                ["statusName"] = "In Progress",
                ["enteredAt"] = DateTimeOffset.UtcNow.AddSeconds(-seconds).ToString("o"),
                ["seconds"] = seconds,
                ["normalSeconds"] = normalSeconds,
                ["band"] = band,
            };

            for (var i = 0; i < shape.Past; i++)
            {
                var key = shape.OnlyKey ?? $"HLT-{901 + i}";
                issues.Add(Issue(key, $"Stood past its normal, number {i + 1}", (int)(normal * (2.5 - 0.5 * i)) + 600, normal, "pastNormal"));
            }

            for (var i = 0; i < shape.Others; i++)
            {
                var last = i == shape.Others - 1 && shape.Others > 2;
                var band = i == 0 ? "aging" : "healthy";
                var seconds = i == 0 ? normal * 3 / 4 : 3000 - i * 20;
                issues.Add(last
                    ? Issue($"HLT-{950 + i}", "In a column with no normal yet", 900, null, "noNormal")
                    : Issue($"HLT-{950 + i}", $"One that is {band}", seconds, normal, band));
            }

            body["inFlightIssues"] = issues;
            body["pastNormal"] = shape.Past;

            await route.FulfillAsync(new()
            {
                Response = response,
                ContentType = "application/json",
                Body = body.ToJsonString(),
            });
        });
}

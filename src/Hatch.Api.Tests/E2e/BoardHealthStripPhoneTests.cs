using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Microsoft.Playwright;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2733's and HA-2739's acceptance at the phone profile, driven in WebKit at <c>iPhone 14 Pro</c>: the strip is
/// one row of five numbers with no chart, tapping it opens the five tiles stacked with the window control above
/// them, and tapping again closes it. The window chosen in the open strip survives a reload, and the row starts
/// closed again.
///
/// <para>The row must sit inside the viewport and the page must not scroll sideways. The hide button is inside
/// Filters, so the header row keeps its one line. Screenshots of the row closed and open are left in the artifact
/// directory. Skipped where WebKit is not installed, and on macOS 14, where it does not open a page.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class BoardHealthStripPhoneTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 120;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Phone = ".hatch-health-phone";
    private const string Row = ".hatch-health-row";
    private const string Toggle = "button[aria-label='Show the health strip above the board']";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly string[] Titles = ["Draft the brief", "Cut the first slice", "Review the slice", "Ship the slice"];

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheStrip_IsOneRowOnAPhone_AndOpensIntoTheTiles()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);

        var harness = new E2eHarness("BoardHealthStripPhone");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline, DeviceProfile.IPhone14Pro);
            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            await deadline.BoundAsync("filing the issues", () => SeedAsync(baseUrl));

            await harness.OpenAsync(deadline, baseUrl, "/", reducedMotion: true);
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForFirstColumnAsync(deadline);
            var page = harness.Page!;
            await harness.WaitForVisibleAsync(deadline, Row);

            // Closed: five numbers, no chart, no tiles, and the row says it can be opened.
            Assert.Equal(5, (await Texts(harness, deadline, $"{Row} [data-part='value']")).Length);
            Assert.Equal(Titles.Length.ToString(), (await Texts(harness, deadline, $"{Row} [data-tile='moved'] [data-part='value']"))[0]);
            Assert.Equal(0, await page.Locator($"{Phone} svg").CountAsync());
            Assert.Equal(0, await page.Locator("section[data-tile]").CountAsync());
            Assert.Equal("false", await page.Locator(Row).GetAttributeAsync("aria-expanded"));

            // It fits: a finger-high row inside the viewport, and a page that does not scroll sideways.
            var closed = await Measure(harness, deadline);
            output.WriteLine($"closed: {closed}");
            Assert.True(closed.N("rowHeight") >= 44, $"the row is {closed.N("rowHeight")}px high, under 44");
            Assert.True(closed.N("rowRight") <= closed.N("innerWidth") + 0.5, $"the row runs to {closed.N("rowRight")}px in a {closed.N("innerWidth")}px viewport");
            Assert.True(closed.N("scrollWidth") <= closed.N("innerWidth"), $"the page scrolls sideways: {closed}");
            Assert.Equal(0, closed.N("cellsCut"));

            // The hide button is inside Filters, not in the header row.
            Assert.Equal(1, await page.Locator($"details.hatch-board-filters-more {Toggle}").CountAsync());
            Assert.Equal(0, await page.Locator($".hatch-filter-trailing {Toggle}").CountAsync());
            await harness.ScreenshotAsync(deadline, "board-health-phone-closed");

            // Tapping opens five stacked tiles, the window control above them.
            await harness.ClickAsync(deadline, Row);
            await harness.WaitForVisibleAsync(deadline, ".hatch-health-strip--stacked");
            Assert.Equal("true", await page.Locator(Row).GetAttributeAsync("aria-expanded"));
            Assert.Equal(
                ["Moved", "In flight", "Oldest in flight", "Waiting on you", "Runners"],
                await Texts(harness, deadline, ".hatch-health-strip--stacked section[data-tile] h3"));
            var layout = new Reading(await harness.EvaluateAsync<JsonElement>(
                deadline, "measuring the open strip",
                """
                () => {
                  const box = (e) => e.getBoundingClientRect();
                  const control = box(document.querySelector('.hatch-health-strip--stacked .hatch-health-control'));
                  const tiles = [...document.querySelectorAll('.hatch-health-strip--stacked section[data-tile]')].map(box);
                  const spark = box(document.querySelector('.hatch-health-strip--stacked section[data-tile] [data-part="spark"]'));
                  const number = box(document.querySelector('.hatch-health-strip--stacked section[data-tile] [data-part="value"]'));
                  return {
                    controlBottom: control.bottom,
                    firstTileTop: tiles[0].top,
                    tilesLeft: Math.min(...tiles.map((t) => t.left)),
                    tilesRight: Math.max(...tiles.map((t) => t.right)),
                    stacked: tiles.every((t, i) => i === 0 || t.top >= tiles[i - 1].bottom - 0.5),
                    sparkLeft: spark.left,
                    numberRight: number.right,
                    sparkWidth: spark.width,
                    scrollWidth: document.documentElement.scrollWidth,
                    innerWidth: window.innerWidth,
                  };
                }
                """));
            output.WriteLine($"open: {layout}");
            Assert.True(layout.N("controlBottom") <= layout.N("firstTileTop"), "the window control is not above the tiles");
            Assert.True(layout.B("stacked"), "the tiles are not one under another");
            Assert.True(layout.N("tilesRight") <= layout.N("innerWidth") + 0.5, "a tile runs off the right edge");
            Assert.True(layout.N("scrollWidth") <= layout.N("innerWidth"), $"the page scrolls sideways when open: {layout}");
            Assert.True(layout.N("sparkWidth") > 40, $"the sparkline is {layout.N("sparkWidth")}px wide");
            Assert.True(layout.N("sparkLeft") >= layout.N("numberRight") - 0.5, "the sparkline is not to the right of the numbers");

            // The stacked Runners tile draws no names, and its lane grid is as wide as its sparkline area.
            const string stackedRunners = ".hatch-health-strip--stacked [data-tile='runners']";
            Assert.Equal(0, await page.Locator($"{stackedRunners} .hatch-health-lane-name").CountAsync());
            var runnersSpark = await page.Locator($"{stackedRunners} [data-part='spark']").BoundingBoxAsync();
            var runnersGrid = await page.Locator($"{stackedRunners} .hatch-health-lane-grid").BoundingBoxAsync();
            Assert.NotNull(runnersSpark);
            Assert.NotNull(runnersGrid);
            output.WriteLine($"runners spark x={runnersSpark.X} w={runnersSpark.Width}; lane grid x={runnersGrid.X} w={runnersGrid.Width}");
            Assert.InRange(runnersGrid.X, runnersSpark.X - 1, runnersSpark.X + 1);
            Assert.InRange(runnersGrid.Width, runnersSpark.Width - 1, runnersSpark.Width + 1);
            await harness.ScreenshotAsync(deadline, "board-health-phone-open");

            // The window chosen in the open strip is remembered across a reload; the row starts closed again.
            await harness.ClickAsync(deadline, ".hatch-health-window:text-is('4h')");
            await harness.WaitForFunctionAsync(
                deadline, "waiting for 4h",
                "() => document.querySelector(\".hatch-health-window[aria-pressed='true']\")?.textContent.trim() === '4h'");
            await deadline.BoundAsync("reloading", () => page.ReloadAsync());
            await harness.WaitForVisibleAsync(deadline, Row);
            Assert.Equal("false", await page.Locator(Row).GetAttributeAsync("aria-expanded"));
            Assert.Equal(0, await page.Locator(".hatch-health-strip--stacked").CountAsync());
            await harness.ClickAsync(deadline, Row);
            await harness.WaitForVisibleAsync(deadline, ".hatch-health-strip--stacked");
            Assert.Equal("4h", Assert.Single(await Texts(harness, deadline, ".hatch-health-window[aria-pressed='true']")));

            // Tapping again closes it.
            await harness.ClickAsync(deadline, Row);
            await harness.WaitForHiddenAsync(deadline, ".hatch-health-strip--stacked");
            Assert.Equal("false", await page.Locator(Row).GetAttributeAsync("aria-expanded"));

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

    /// <summary>One measurement, read out of the page as JSON: a name and its number or flag.</summary>
    private sealed class Reading(JsonElement read)
    {
        public double N(string name) => read.GetProperty(name).GetDouble();

        public bool B(string name) => read.GetProperty(name).GetBoolean();

        public override string ToString() => read.ToString();
    }

    private static async Task<Reading> Measure(E2eHarness harness, Deadline deadline) =>
        new(await harness.EvaluateAsync<JsonElement>(
            deadline, "measuring the row",
            """
            () => {
              const row = document.querySelector('.hatch-health-row').getBoundingClientRect();
              const cut = [...document.querySelectorAll('.hatch-health-row .hatch-health-cell-number')].filter((e) => e.scrollWidth > e.clientWidth + 1).length;
              return {
                rowHeight: row.height,
                rowRight: row.right,
                innerWidth: window.innerWidth,
                scrollWidth: document.documentElement.scrollWidth,
                cellsCut: cut,
              };
            }
            """));

    private static async Task<string[]> Texts(E2eHarness harness, Deadline deadline, string selector) =>
        await harness.EvaluateAsync<string[]>(
            deadline, $"reading {selector}",
            "(selector) => [...document.querySelectorAll(selector)].map((e) => e.textContent.trim())", selector);

    /// <summary>
    /// Files a project and four issues, then moves each one column right of where it was filed, on a
    /// column that is neither protected, terminal nor deferred, so every move counts as work going forward.
    /// </summary>
    private static async Task SeedAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("HLT", "HLT"));
        var statuses = (await GetAsync<StatusDto[]>(http, "/api/statuses")).OrderBy(s => s.SortOrder).ToList();
        var later = statuses
            .Skip(1)
            .Where(s => !s.IsProtected && !s.IsTerminal && !s.IsDeferred)
            .ToList();
        Assert.NotEmpty(later);

        for (var i = 0; i < Titles.Length; i++)
        {
            var issue = await PostAsync<IssueCreateRequest, IssueDto>(
                http, "/api/issues", new(project.Id, "task", Titles[i], null, null, null, null));
            var target = later[i % later.Count];

            using var response = await http.PostAsJsonAsync($"/api/issues/{issue.Key}/move", new IssueMoveRequest(target.Id, null, null), Web);
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, $"POST /api/issues/{issue.Key}/move answered {(int)response.StatusCode}: {text}");
        }
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }

    private static async Task<TResponse> GetAsync<TResponse>(HttpClient http, string path)
    {
        using var response = await http.GetAsync(path);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"GET {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

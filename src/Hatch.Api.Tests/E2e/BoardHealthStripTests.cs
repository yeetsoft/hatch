using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Microsoft.Playwright;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2732's acceptance at desktop width, driven in Chromium: four tiles sit above the board and share one
/// window and one hovered moment, the operator can hide them, and a board with no history draws no strip.
///
/// <para>A board is filed with four issues, each moved right once, so every move lands in the newest bar.
/// Hovering the oldest bar then reads a count of nothing where the window's own number was four, and a
/// keyboard step to the last bar marks the moment too. The window survives a reload; hiding survives one
/// as well, and a request watcher registered before it saw no read of the health route. A screenshot of
/// the strip is left in the artifact directory in the light and the dark theme, and with a hover.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class BoardHealthStripTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 120;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Strip = ".hatch-health-strip";
    private const string Moved = "[data-tile='moved']";
    private const string Toggle = "button[aria-label='Show the health strip above the board']";
    private const string HealthRoute = "/api/board/health";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly string[] Titles = ["Draft the brief", "Cut the first slice", "Review the slice", "Ship the slice"];

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheStrip_SharesOneWindowAndOneMoment_AndCanBeHidden()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("BoardHealthStrip");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);
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
            await harness.WaitForVisibleAsync(deadline, Strip);

            // Four tiles, the window control with one button pressed, and the axis said in a sentence.
            var tiles = await Texts(harness, deadline, $"{Strip} [data-tile] h3");
            Assert.Equal(["Moved", "In flight", "Oldest in flight", "Waiting on you"], tiles);
            Assert.Equal(["2h", "4h", "8h", "12h", "24h"], await Texts(harness, deadline, ".hatch-health-window"));
            Assert.Equal("8h", Assert.Single(await Texts(harness, deadline, ".hatch-health-window[aria-pressed='true']")));
            Assert.Matches(@"^to \d{1,2}:\d{2}( [AP]M)? · \d+ min bars$", Assert.Single(await Texts(harness, deadline, ".hatch-health-axis")));
            Assert.Equal(Titles.Length.ToString(), await Value(harness, deadline));

            // The strip leaves the page as it was: no page scroll, and the columns still scroll inside themselves.
            var overflow = await harness.EvaluateAsync<double>(deadline, "measuring the page", "() => document.documentElement.scrollHeight - window.innerHeight");
            output.WriteLine($"page overflow: {overflow}");
            Assert.True(overflow <= 2, $"the page scrolls by {overflow}px with the strip drawn");

            var rest = await Readout(harness, deadline);
            var restHeight = await StripHeight(harness, deadline);

            // Each theme is settled before it is drawn, at rest and then with a mouse over the oldest bar,
            // which marks that moment on every tile.
            foreach (var theme in new[] { "light", "dark" })
            {
                await harness.EvaluateAsync<bool>(deadline, "setting the theme", $"() => {{ document.documentElement.setAttribute('data-theme', '{theme}'); return true; }}");
                await Task.Delay(500);
                await harness.ScreenshotAsync(deadline, $"board-health-{theme}");

                var spark = await page.Locator($"{Moved} [data-part='spark']").BoundingBoxAsync();
                Assert.NotNull(spark);
                await deadline.BoundAsync("hovering the first bar", () => page.Mouse.MoveAsync((float)spark.X + 2, (float)(spark.Y + spark.Height / 2)));
                await harness.WaitForFunctionAsync(
                    deadline, "waiting for the hover",
                    "(before) => document.querySelector(\"[data-tile='moved'] [data-part='value']\").textContent.trim() !== before", rest.Value);
                var hovered = await Readout(harness, deadline);
                // A hover moves nothing: the sparkline under the pointer stays where it was.
                Assert.Equal(restHeight, await StripHeight(harness, deadline));
                Assert.NotEqual(rest.Value, hovered.Value);
                Assert.False(string.IsNullOrWhiteSpace(hovered.Second));
                Assert.NotEqual(rest.Second, hovered.Second);
                Assert.Equal(4, await harness.EvaluateAsync<int>(deadline, "counting the marked lines", "() => document.querySelectorAll('.hatch-health-moment').length"));
                await harness.ScreenshotAsync(deadline, $"board-health-{theme}-hover");

                // Moving off puts the window's own numbers back.
                await deadline.BoundAsync("leaving the sparkline", () => page.Mouse.MoveAsync(5, 5));
                await harness.WaitForFunctionAsync(deadline, "waiting for the hover to clear", "() => document.querySelectorAll('.hatch-health-moment').length === 0");
                Assert.Equal(rest, await Readout(harness, deadline));
            }

            // A keyboard step marks a moment, and Escape clears it.
            await deadline.BoundAsync("focusing the sparkline", () => page.Locator($"{Moved} [data-part='spark']").FocusAsync());
            await deadline.BoundAsync("stepping left", () => page.Keyboard.PressAsync("ArrowLeft"));
            await harness.WaitForFunctionAsync(deadline, "waiting for the step", "() => document.querySelectorAll('.hatch-health-moment').length === 4");
            var stepped = await Readout(harness, deadline);
            Assert.NotEqual(rest.Second, stepped.Second);
            await deadline.BoundAsync("pressing Escape", () => page.Keyboard.PressAsync("Escape"));
            await harness.WaitForFunctionAsync(deadline, "waiting for Escape", "() => document.querySelectorAll('.hatch-health-moment').length === 0");
            Assert.Equal(rest, await Readout(harness, deadline));

            // The window is remembered across a reload.
            await harness.ClickAsync(deadline, ".hatch-health-window:text-is('12h')");
            await harness.WaitForFunctionAsync(deadline, "waiting for 12h", "() => document.querySelector('.hatch-health-axis')?.textContent.endsWith('30 min bars') ?? false");
            await deadline.BoundAsync("reloading", () => page.ReloadAsync());
            await harness.WaitForVisibleAsync(deadline, Strip);
            Assert.Equal("12h", Assert.Single(await Texts(harness, deadline, ".hatch-health-window[aria-pressed='true']")));

            // Hiding removes the strip, and a hidden strip stays hidden and reads nothing.
            Assert.Equal("true", await page.Locator(Toggle).GetAttributeAsync("aria-pressed"));
            await harness.ClickAsync(deadline, Toggle);
            await harness.WaitForHiddenAsync(deadline, Strip);
            Assert.Equal("false", await page.Locator(Toggle).GetAttributeAsync("aria-pressed"));

            var reads = new List<string>();
            page.Request += (_, request) => { lock (reads) { if (request.Url.Contains(HealthRoute)) reads.Add(request.Url); } };
            await deadline.BoundAsync("reloading hidden", () => page.ReloadAsync());
            await harness.WaitForFirstColumnAsync(deadline);
            await Task.Delay(1500);
            Assert.Equal(0, await page.Locator(Strip).CountAsync());
            Assert.Equal("false", await page.Locator(Toggle).GetAttributeAsync("aria-pressed"));
            lock (reads) Assert.True(reads.Count == 0, $"a hidden strip read the board's health: {string.Join(", ", reads)}");

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
    public async Task ABoardWithNoHistory_ShowsTheButton_AndNoStrip_AndNoError()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("BoardHealthStripEmpty");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);
            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);

            await harness.OpenAsync(deadline, baseUrl, "/", reducedMotion: true);
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForFirstColumnAsync(deadline);
            var page = harness.Page!;
            await harness.WaitForVisibleAsync(deadline, Toggle);

            // Long enough for the read to have been asked and answered.
            await Task.Delay(1500);
            Assert.Equal(0, await page.Locator(Strip).CountAsync());
            Assert.Equal(0, await page.Locator(".text-danger").CountAsync());
            await harness.ScreenshotAsync(deadline, "board-health-empty");

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

    /// <summary>The Moved tile's big number and the line under it.</summary>
    private sealed record Reading(string Value, string Second);

    private static async Task<Reading> Readout(E2eHarness harness, Deadline deadline)
    {
        var value = await Value(harness, deadline);
        var second = Assert.Single(await Texts(harness, deadline, $"{Moved} [data-part='second']"));
        return new(value, second);
    }

    private static Task<double> StripHeight(E2eHarness harness, Deadline deadline) =>
        harness.EvaluateAsync<double>(deadline, "measuring the strip", "() => document.querySelector('.hatch-health-strip').getBoundingClientRect().height");

    private static async Task<string> Value(E2eHarness harness, Deadline deadline) =>
        Assert.Single(await Texts(harness, deadline, $"{Moved} [data-part='value']"));

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

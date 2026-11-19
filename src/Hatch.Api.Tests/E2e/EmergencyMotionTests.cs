using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Microsoft.Playwright;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2512's acceptance, measured: on a board an emergency's flame flickers and its speed lines streak,
/// and nothing else on a tile moves. A board is filed with a pre-approved emergency bug, an expedited
/// story, an economy task, an express task and an upright emergency task, and drawn on the desktop and at the <c>iPhone 14 Pro</c>
/// profile (in Chromium: WebKit does not open a page on every machine), in the light and the dark theme.
/// The bug's flame body, flame core and trail each carry one running animation, the card under the
/// cursor does the same, the upright emergency's flame holds its base where it is, and the story's ember, the task's fade and
/// the express task's trail carry none. Then the page is asked for reduced motion, and nothing on any
/// tile animates. A screenshot of each state is left in the case's artifact directory.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class EmergencyMotionTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 120;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>The first title is the one <see cref="BoardCardMarksTests.PickUpAsync"/> picks up.</summary>
    private static readonly string[] Cards =
    [
        "Pre-approved emergency bug",
        "Expedited story",
        "Economy task",
        "Express task",
        "Emergency task",
    ];

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheDesktopBoard_MovesOnlyAnEmergency() =>
        RunAsync("EmergencyMotionDesktop", "desktop", null, BoardCardMarksTests.Drag.Mouse);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task ThePhoneBoard_MovesOnlyAnEmergency() =>
        RunAsync("EmergencyMotionPhone", "phone", DeviceProfile.IPhone14ProChromium, BoardCardMarksTests.Drag.Touch);

    private async Task RunAsync(string name, string label, DeviceProfile? profile, BoardCardMarksTests.Drag drag)
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness(name);
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline, profile);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            await deadline.BoundAsync("filing the cards", () => SeedAsync(baseUrl));

            // Motion is on: the page is opened without asking for reduced motion.
            await harness.OpenAsync(deadline, baseUrl, "/", deviceScaleFactor: profile is null ? 3 : null);
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForFirstColumnAsync(deadline);
            var page = harness.Page!;
            await deadline.BoundAsync("waiting for the cards", () =>
                page.Locator(".hatch-card", new() { HasText = Cards[^1] }).First.WaitForAsync(new() { State = WaitForSelectorState.Visible }));

            foreach (var theme in new[] { "light", "dark" })
            {
                await harness.EvaluateAsync<bool>(deadline, "setting the theme", $"() => {{ document.documentElement.setAttribute('data-theme', '{theme}'); return true; }}");

                var moving = await harness.EvaluateAsync<JsonElement>(deadline, "counting the animations", Count);
                output.WriteLine($"{label} {theme}: {moving}");
                AssertMovesOnlyAnEmergency(moving, $"{label} {theme}");
                await harness.ScreenshotAsync(deadline, $"{label}-{theme}-board");
                await BoardCardMarksTests.CardsAsync(harness, deadline, $"{label}-{theme}-moving");

                var sampled = await harness.EvaluateAsync<JsonElement>(deadline, "watching the flame", WatchFlame);
                output.WriteLine($"{label} {theme} flame: {sampled}");
                Assert.True(
                    sampled.GetProperty("bottomRange").GetDouble() <= 0.5,
                    $"{label} {theme}: the flame's base moved by {sampled.GetProperty("bottomRange")}");
                Assert.True(
                    sampled.GetProperty("heightRange").GetDouble() > 0.2,
                    $"{label} {theme}: the flame's height only varied by {sampled.GetProperty("heightRange")}");

                var putDown = await BoardCardMarksTests.PickUpAsync(harness, deadline, drag);
                var preview = await harness.EvaluateAsync<JsonElement>(deadline, "counting the preview's animations", CountPreview);
                output.WriteLine($"{label} {theme} preview: {preview}");
                Assert.Equal(1, preview.GetProperty("body").GetInt32());
                Assert.Equal(1, preview.GetProperty("core").GetInt32());
                Assert.Equal(1, preview.GetProperty("trail").GetInt32());
                await putDown();

                // A finger lifted over the card it held is a tap on it: the phone opens the issue, so come back.
                if (drag == BoardCardMarksTests.Drag.Touch)
                {
                    await harness.GoToAsync(deadline, "/");
                    await harness.WaitForFirstColumnAsync(deadline);
                    await deadline.BoundAsync("waiting for the cards", () =>
                        page.Locator(".hatch-card", new() { HasText = Cards[^1] }).First.WaitForAsync(new() { State = WaitForSelectorState.Visible }));
                }
            }

            // Reduced motion asked for: the media query holds, and nothing on any tile animates.
            await deadline.BoundAsync("asking for reduced motion", () => page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce }));
            var still = await harness.EvaluateAsync<JsonElement>(deadline, "counting the animations", Count);
            output.WriteLine($"{label} reduced motion: {still}");
            Assert.True(still.GetProperty("reduced").GetBoolean(), $"{label}: the page does not match prefers-reduced-motion: reduce");
            Assert.Equal(0, still.GetProperty("all").GetInt32());
            foreach (var card in still.GetProperty("cards").EnumerateArray())
                Assert.Equal(0, card.GetProperty("animations").GetInt32());
            await harness.ScreenshotAsync(deadline, $"{label}-reduced-board");
            await BoardCardMarksTests.CardsAsync(harness, deadline, $"{label}-reduced-still");

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

    private static void AssertMovesOnlyAnEmergency(JsonElement counted, string when)
    {
        Assert.False(counted.GetProperty("reduced").GetBoolean(), $"{when}: the page asks for reduced motion");
        var byTitle = counted.GetProperty("cards").EnumerateArray().ToDictionary(c => c.GetProperty("title").GetString()!);
        Assert.Equal(Cards.Length, byTitle.Count);

        // The emergency's flame body, flame core and trail each carry one animation, and nothing else on its tile does.
        var bug = byTitle[Cards[0]];
        Assert.Equal(1, bug.GetProperty("body").GetInt32());
        Assert.Equal(1, bug.GetProperty("core").GetInt32());
        Assert.Equal(1, bug.GetProperty("trail").GetInt32());
        Assert.Equal(3, bug.GetProperty("animations").GetInt32());

        // The ember, the fade and a trail on a card that is not an emergency are still.
        var story = byTitle[Cards[1]];
        Assert.Equal(0, story.GetProperty("body").GetInt32());
        Assert.Equal(0, story.GetProperty("core").GetInt32());
        Assert.Equal(0, story.GetProperty("animations").GetInt32());
        Assert.Equal(0, byTitle[Cards[2]].GetProperty("animations").GetInt32());
        Assert.True(byTitle[Cards[3]].GetProperty("hasTrail").GetBoolean(), $"{when}: the express task has no trail to be still");
        Assert.Equal(0, byTitle[Cards[3]].GetProperty("animations").GetInt32());

        // An emergency with no pre-approval flickers and has no lines to streak.
        var upright = byTitle[Cards[4]];
        Assert.Equal(1, upright.GetProperty("body").GetInt32());
        Assert.Equal(1, upright.GetProperty("core").GetInt32());
        Assert.Equal(2, upright.GetProperty("animations").GetInt32());
    }

    private static async Task SeedAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("E2E", "E2E"));
        async Task FileAsync(string type, string title, string? priority, string? preApproval) =>
            await PostAsync<IssueCreateRequest, IssueDto>(
                http, "/api/issues", new(project.Id, type, title, null, null, null, null, Priority: priority, PreApproval: preApproval));

        await FileAsync("bug", Cards[0], "emergency", "preapproved");
        await FileAsync("story", Cards[1], "expedited", null);
        await FileAsync("task", Cards[2], "economy", null);
        await FileAsync("task", Cards[3], null, "express");
        await FileAsync("task", Cards[4], "emergency", null);
    }

    /// <summary>Every card's running animations: those on the flame's two parts and the trail, and on anything in its tile at all.</summary>
    private const string Count = """
        () => {
          const count = (el) => (el ? el.getAnimations().length : 0);
          const cards = [...document.querySelectorAll('.hatch-card:not(.hatch-card-preview)')].map((card) => ({
            title: card.querySelector('.hatch-card-title')?.textContent ?? '',
            body: count(card.querySelector('.hatch-type-tile-flame-body')),
            core: count(card.querySelector('.hatch-type-tile-flame-core')),
            trail: count(card.querySelector('.hatch-type-tile-trail')),
            hasTrail: !!card.querySelector('.hatch-type-tile-trail'),
            animations: [...card.querySelectorAll('svg, svg *')].reduce((sum, el) => sum + count(el), 0),
          }));
          const all = [...document.querySelectorAll('.hatch-card svg, .hatch-card svg *')].reduce((sum, el) => sum + count(el), 0);
          return { reduced: matchMedia('(prefers-reduced-motion: reduce)').matches, cards, all };
        }
        """;

    private const string CountPreview = """
        () => {
          const preview = document.querySelector('.hatch-card-preview');
          const count = (sel) => preview.querySelector(sel)?.getAnimations().length ?? 0;
          return {
            body: count('.hatch-type-tile-flame-body'),
            core: count('.hatch-type-tile-flame-core'),
            trail: count('.hatch-type-tile-trail'),
          };
        }
        """;

    /// <summary>
    /// The upright emergency's flame body, sampled every 50ms for a second and a half: its bottom edge (the
    /// base) must hold still while its height changes. Both are in CSS pixels. The pre-approved bug's flame
    /// is turned back off the tile, so its base is not its bottom edge.
    /// </summary>
    private const string WatchFlame = """
        async () => {
          const card = [...document.querySelectorAll('.hatch-card:not(.hatch-card-preview)')]
            .find((c) => c.querySelector('.hatch-card-title')?.textContent === 'Emergency task');
          const body = card.querySelector('.hatch-type-tile-flame-body');
          const bottoms = [], heights = [];
          for (let i = 0; i < 30; i++) {
            const r = body.getBoundingClientRect();
            bottoms.push(r.bottom);
            heights.push(r.height);
            await new Promise((resolve) => setTimeout(resolve, 50));
          }
          const range = (xs) => Math.max(...xs) - Math.min(...xs);
          return { bottomRange: range(bottoms), heightRange: range(heights) };
        }
        """;

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

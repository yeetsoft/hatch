using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Microsoft.Playwright;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2510's acceptance, measured: a board card wears its pre-approval as a trail and its priority as a
/// flame on the type's tile, and what it wears reaches left and up from the tile without running under
/// the key, off the column, or into the contribution mark. A board is filed with one card of each kind
/// the story names - a pre-approved emergency bug, an emergency story owed an answer, an expedited story,
/// an economy task, an express task, a low task and a paused one - and drawn on the desktop and at the
/// <c>iPhone 14 Pro</c> profile (in Chromium: WebKit does not open a page on every machine), in the light and the dark theme, with one card picked up so the drag
/// preview is on the page too.
///
/// <para>The contribution mark needs a contribution the harness cannot file, so it is put on a card by
/// hand, with the classes the card gives it and its head. A screenshot of each state is left in the
/// case's artifact directory, and one of the cards alone, drawn at three device pixels to a CSS one on the desktop, for a reader who wants the flame's edge.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class BoardCardMarksTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 120;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const int HoldMs = 700;

    /// <summary>Sub-pixel rounding in a laid-out box.</summary>
    private const double Tolerance = 0.5;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>The titles the cards are filed under, in the order they are filed.</summary>
    private static readonly string[] Cards =
    [
        "Pre-approved emergency bug",
        "Emergency story owed an answer",
        "Expedited story",
        "Economy task",
        "Express task",
        "Low task",
        "Paused task",
    ];

    internal enum Drag { None, Mouse, Touch }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheDesktopBoard_DrawsEveryLevel_WithNothingClippedOrOverlapping() =>
        RunAsync("BoardCardMarksDesktop", "desktop", null, Drag.Mouse);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task ThePhoneBoard_DrawsEveryLevel_WithNothingClippedOrOverlapping() =>
        RunAsync("BoardCardMarksPhone", "phone", DeviceProfile.IPhone14ProChromium, Drag.Touch);

    private async Task RunAsync(string name, string label, DeviceProfile? profile, Drag drag)
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

            // A desktop context draws at three device pixels to a CSS one, so a flame's edge is more than a few pixels across.
            await harness.OpenAsync(deadline, baseUrl, "/", reducedMotion: true, deviceScaleFactor: profile is null ? 3 : null);
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForFirstColumnAsync(deadline);
            var page = harness.Page!;
            await deadline.BoundAsync("waiting for the cards", () =>
                page.Locator(".hatch-card", new() { HasText = Cards[^1] }).First.WaitForAsync(new() { State = WaitForSelectorState.Visible }));

            foreach (var theme in new[] { "light", "dark" })
            {
                await harness.EvaluateAsync<bool>(deadline, "setting the theme", $"() => {{ document.documentElement.setAttribute('data-theme', '{theme}'); return true; }}");

                var measured = await harness.EvaluateAsync<JsonElement>(deadline, "measuring the cards", Measure);
                output.WriteLine($"{label} {theme}: {measured}");
                AssertMeasured(measured, $"{label} {theme}");
                await harness.ScreenshotAsync(deadline, $"{label}-{theme}-board");
                await CardsAsync(harness, deadline, $"{label}-{theme}-cards");

                if (drag == Drag.None) continue;

                var putDown = await PickUpAsync(harness, deadline, drag);
                await harness.ScreenshotAsync(deadline, $"{label}-{theme}-picked-up");
                var preview = await harness.EvaluateAsync<JsonElement>(deadline, "measuring the preview", MeasurePreview);
                output.WriteLine($"{label} {theme} preview: {preview}");
                Assert.Equal(preview.GetProperty("card").GetString(), preview.GetProperty("tileGround").GetString());
                Assert.True(preview.GetProperty("hasFlame").GetBoolean());
                Assert.True(preview.GetProperty("hasTrail").GetBoolean());
                Assert.StartsWith("rgb(240, 140, 0)", preview.GetProperty("stripe").GetString());
                await CardsAsync(harness, deadline, $"{label}-{theme}-preview");
                await putDown();

                // A finger lifted over the card it held is a tap on it: the phone opens the issue, so come back.
                if (drag == Drag.Touch)
                {
                    await harness.GoToAsync(deadline, "/");
                    await harness.WaitForFirstColumnAsync(deadline);
                    await deadline.BoundAsync("waiting for the cards", () =>
                        page.Locator(".hatch-card", new() { HasText = Cards[^1] }).First.WaitForAsync(new() { State = WaitForSelectorState.Visible }));
                }
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

    private static async Task SeedAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("E2E", "E2E"));
        async Task<IssueDto> FileAsync(string type, string title, string? priority, string? preApproval) =>
            await PostAsync<IssueCreateRequest, IssueDto>(
                http, "/api/issues", new(project.Id, type, title, null, null, null, null, Priority: priority, PreApproval: preApproval));

        await FileAsync("bug", Cards[0], "emergency", "preapproved");
        var asking = await FileAsync("story", Cards[1], "emergency", null);
        await FileAsync("story", Cards[2], "expedited", null);
        await FileAsync("task", Cards[3], "economy", null);
        await FileAsync("task", Cards[4], null, "express");
        await FileAsync("task", Cards[5], "low", null);
        await FileAsync("task", Cards[6], "paused", null);

        await PostAsync<CommentCreateRequest, CommentDto>(
            http, $"/api/issues/{asking.Key}/comments", new($"{asking.Key} question: keep it or drop it?", "question"));
    }

    /// <summary>
    /// Every card's boxes: the key, the tile's wrapper, its trail and flame, and the box the nearest
    /// clipping ancestors leave it. A contribution mark is put on the pre-approved card by hand, measured
    /// against the trail it has to clear, and taken off again.
    /// </summary>
    private const string Measure = """
        () => {
          const box = (el) => {
            if (!el) return null;
            const r = el.getBoundingClientRect();
            return { left: r.left, top: r.top, right: r.right, bottom: r.bottom };
          };
          const clipOf = (el) => {
            let left = -Infinity, top = -Infinity, right = Infinity, bottom = Infinity;
            for (let p = el.parentElement; p && p !== document.documentElement; p = p.parentElement) {
              const s = getComputedStyle(p);
              if (s.overflowX === 'visible' && s.overflowY === 'visible') continue;
              const r = p.getBoundingClientRect();
              left = Math.max(left, r.left); top = Math.max(top, r.top);
              right = Math.min(right, r.right); bottom = Math.min(bottom, r.bottom);
            }
            return { left, top, right, bottom };
          };
          const live = () => [...document.querySelectorAll('.hatch-card:not(.hatch-card-preview)')];
          const cards = live().map((card) => ({
            title: card.querySelector('.hatch-card-title')?.textContent ?? '',
            headPaddingRight: getComputedStyle(card.querySelector('.hatch-card-head')).paddingRight,
            card: box(card),
            key: box(card.querySelector('.hatch-card-key')),
            glyph: box(card.querySelector('.hatch-type-glyph')),
            trail: box(card.querySelector('.hatch-type-tile-trail')),
            flame: box(card.querySelector('.hatch-type-tile-flame')),
            tileOpacity: card.querySelector('.hatch-type-tile-face')?.parentElement?.getAttribute('opacity'),
            stripe: getComputedStyle(card).boxShadow,
            clip: clipOf(card),
            marks: [...card.querySelectorAll('.hatch-card-marks > *')].map((m) => m.textContent.trim()),
          }));

          const first = live().find((c) => c.querySelector('.hatch-card-title')?.textContent === 'Pre-approved emergency bug');
          const head = first.querySelector('.hatch-card-head');
          const mark = document.createElement('span');
          mark.className = 'hatch-contribution-mark hatch-contribution-mark--card hatch-contribution-mark--trailed';
          first.insertBefore(mark, head);
          head.classList.add('hatch-card-head--contribution');
          const contribution = {
            mark: box(mark),
            key: box(first.querySelector('.hatch-card-key')),
            trail: box(first.querySelector('.hatch-type-tile-trail')),
            glyph: box(first.querySelector('.hatch-type-glyph')),
            headPaddingRight: getComputedStyle(head).paddingRight,
          };
          mark.remove();
          head.classList.remove('hatch-card-head--contribution');
          return { cards, contribution };
        }
        """;

    private const string MeasurePreview = """
        () => {
          const preview = document.querySelector('.hatch-card-preview');
          return {
            title: preview.querySelector('.hatch-card-title')?.textContent,
            tileGround: getComputedStyle(preview).getPropertyValue('--tile-ground').trim(),
            card: getComputedStyle(preview).getPropertyValue('--card').trim(),
            hasFlame: !!preview.querySelector('.hatch-type-tile-flame'),
            hasTrail: !!preview.querySelector('.hatch-type-tile-trail'),
            stripe: getComputedStyle(preview).boxShadow,
          };
        }
        """;

    private static void AssertMeasured(JsonElement measured, string when)
    {
        var cards = measured.GetProperty("cards").EnumerateArray().ToList();
        Assert.Equal(Cards.Length, cards.Count);

        foreach (var card in cards)
        {
            var at = $"{when}, {card.GetProperty("title").GetString()}";
            var cardBox = Box(card, "card");
            var glyph = Box(card, "glyph");
            var key = Box(card, "key");

            // The tile stays where it was: 4px in from the card's top and right edges, inside the 1px border.
            Assert.True(Math.Abs(glyph.Top - cardBox.Top - 5) <= Tolerance, $"{at}: the tile is {glyph.Top - cardBox.Top} from the card's top");
            Assert.True(Math.Abs(cardBox.Right - glyph.Right - 5) <= Tolerance, $"{at}: the tile is {cardBox.Right - glyph.Right} from the card's right");

            // The key never runs under the marks.
            var leftmost = glyph.Left;
            if (card.GetProperty("trail").ValueKind == JsonValueKind.Object) leftmost = Math.Min(leftmost, Box(card, "trail").Left);
            Assert.True(key.Right <= leftmost + Tolerance, $"{at}: the key ends at {key.Right}, past the marks at {leftmost}");

            // The flame is drawn whole: it crosses the card's top edge by under 3px, and nothing clips any of it.
            if (card.GetProperty("flame").ValueKind != JsonValueKind.Object) continue;
            var flame = Box(card, "flame");
            var clip = card.GetProperty("clip");
            Assert.True(cardBox.Top - flame.Top < 3 + Tolerance, $"{at}: the flame crosses the card's top by {cardBox.Top - flame.Top}");
            Assert.True(flame.Top >= clip.GetProperty("top").GetDouble() - Tolerance, $"{at}: the flame's top {flame.Top} is clipped at {clip.GetProperty("top")}");
            Assert.True(flame.Left >= clip.GetProperty("left").GetDouble() - Tolerance, $"{at}: the flame's left is clipped");
        }

        // A flame or a trail is drawn for the levels that earn one and no others.
        var byTitle = cards.ToDictionary(c => c.GetProperty("title").GetString()!);
        foreach (var flamed in new[] { Cards[0], Cards[1], Cards[2] })
            Assert.Equal(JsonValueKind.Object, byTitle[flamed].GetProperty("flame").ValueKind);
        foreach (var plain in new[] { Cards[3], Cards[4], Cards[5], Cards[6] })
            Assert.Equal(JsonValueKind.Null, byTitle[plain].GetProperty("flame").ValueKind);
        Assert.Equal(JsonValueKind.Object, byTitle[Cards[0]].GetProperty("trail").ValueKind);
        Assert.Equal(JsonValueKind.Object, byTitle[Cards[4]].GetProperty("trail").ValueKind);
        Assert.Equal(JsonValueKind.Null, byTitle[Cards[2]].GetProperty("trail").ValueKind);
        Assert.Equal("0.4", byTitle[Cards[3]].GetProperty("tileOpacity").GetString());

        // The stripe is the flame's amber on an emergency, never red.
        foreach (var emergency in new[] { Cards[0], Cards[1] })
            Assert.StartsWith("rgb(240, 140, 0)", byTitle[emergency].GetProperty("stripe").GetString());

        // The marks row holds only what the tile cannot say.
        string Marks(string title) => string.Join(",", byTitle[title].GetProperty("marks").EnumerateArray().Select(m => m.GetString()));
        Assert.Equal("↓", Marks(Cards[5]));
        Assert.Equal("⏸", Marks(Cards[6]));
        Assert.Equal("?", Marks(Cards[1]));
        foreach (var bare in new[] { Cards[0], Cards[2], Cards[3], Cards[4] })
            Assert.Equal("", Marks(bare));

        // A contribution mark beside a trail: both shown, neither over the other, the key short of both.
        var contribution = measured.GetProperty("contribution");
        var mark = Box(contribution, "mark");
        var trail = Box(contribution, "trail");
        var contributionKey = Box(contribution, "key");
        Assert.True(mark.Right <= trail.Left + Tolerance, $"{when}: the contribution mark ({mark.Right}) is over the trail ({trail.Left})");
        Assert.True(contributionKey.Right <= mark.Left + Tolerance, $"{when}: the key ({contributionKey.Right}) runs under the contribution mark ({mark.Left})");
    }

    private sealed record Rect(double Left, double Top, double Right, double Bottom);

    private static Rect Box(JsonElement parent, string name)
    {
        var e = parent.GetProperty(name);
        return new(e.GetProperty("left").GetDouble(), e.GetProperty("top").GetDouble(), e.GetProperty("right").GetDouble(), e.GetProperty("bottom").GetDouble());
    }

    /// <summary>A screenshot of the cards alone, a margin round them, so the flame's edge is in the frame.</summary>
    internal static async Task CardsAsync(E2eHarness harness, Deadline deadline, string label)
    {
        var page = harness.Page!;
        var directory = Path.Combine(MinimalDatabases.RepositoryRoot(), "src", "Hatch.Api.Tests", "TestResults", "e2e", $"{label}-{Environment.ProcessId}");
        Directory.CreateDirectory(directory);

        await deadline.BoundAsync($"taking the {label} screenshot", async () =>
        {
            var clip = await page.EvaluateAsync<JsonElement>("""
                () => {
                  const boxes = [...document.querySelectorAll('.hatch-card')].map((c) => c.getBoundingClientRect());
                  const left = Math.max(0, Math.min(...boxes.map((b) => b.left)) - 12);
                  const top = Math.max(0, Math.min(...boxes.map((b) => b.top)) - 12);
                  const right = Math.min(window.innerWidth, Math.max(...boxes.map((b) => b.right)) + 12);
                  const bottom = Math.min(window.innerHeight, Math.max(...boxes.map((b) => b.bottom)) + 12);
                  return { x: left, y: top, width: right - left, height: bottom - top };
                }
                """);
            await page.ScreenshotAsync(new()
            {
                Path = Path.Combine(directory, $"{label}.png"),
                Clip = new()
                {
                    X = (float)clip.GetProperty("x").GetDouble(),
                    Y = (float)clip.GetProperty("y").GetDouble(),
                    Width = (float)clip.GetProperty("width").GetDouble(),
                    Height = (float)clip.GetProperty("height").GetDouble(),
                },
            });
        });
    }

    /// <summary>Picks up the pre-approved emergency bug, and returns what puts it back where it was.</summary>
    internal static async Task<Func<Task>> PickUpAsync(E2eHarness harness, Deadline deadline, Drag drag)
    {
        var page = harness.Page!;
        var from = Centre((await page.Locator(".hatch-card", new() { HasText = Cards[0] }).First.BoundingBoxAsync())!);
        Func<Task> putDown;

        if (drag == Drag.Mouse)
        {
            await deadline.BoundAsync("pressing the card", async () =>
            {
                await page.Mouse.MoveAsync((float)from.X, (float)from.Y);
                await page.Mouse.DownAsync();
                for (var i = 1; i <= 8; i++) await page.Mouse.MoveAsync((float)from.X + i * 6, (float)from.Y + i * 4);
            });
            // Back where it came from, so the drop moves nothing.
            putDown = () => deadline.BoundAsync("putting the card back", async () =>
            {
                await page.Mouse.MoveAsync((float)from.X, (float)from.Y);
                await page.Mouse.UpAsync();
            });
        }
        else
        {
            var cdp = await harness.NewCdpSessionAsync(deadline);
            await TouchAsync(deadline, cdp, "touchStart", from);
            await Task.Delay(HoldMs);
            putDown = () => TouchAsync(deadline, cdp, "touchEnd", from);
        }

        await deadline.BoundAsync("waiting for the card to lift", () =>
            page.Locator(".hatch-card-preview").WaitForAsync(new() { State = WaitForSelectorState.Visible }));
        await Task.Delay(200);

        return async () =>
        {
            await putDown();
            await deadline.BoundAsync("waiting for the preview to go", () =>
                page.Locator(".hatch-card-preview").WaitForAsync(new() { State = WaitForSelectorState.Detached }));
        };
    }

    private static (double X, double Y) Centre(LocatorBoundingBoxResult box) => (box.X + box.Width / 2, box.Y + box.Height / 2);

    private static Task TouchAsync(Deadline deadline, ICDPSession cdp, string type, (double X, double Y) at) =>
        deadline.BoundAsync($"sending {type}", () => cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = type,
            ["touchPoints"] = type == "touchEnd"
                ? Array.Empty<object>()
                : new object[] { new Dictionary<string, object> { ["x"] = at.X, ["y"] = at.Y, ["id"] = 1 } },
        }));

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

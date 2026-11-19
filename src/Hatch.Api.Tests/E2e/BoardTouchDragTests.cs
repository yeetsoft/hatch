using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Microsoft.Playwright;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2514's acceptance, driven: on the <c>iPhone 14 Pro</c> descriptor in
/// Chromium, a card that is pressed and held lifts and stays under the finger,
/// the page stands still under it, and letting go over another column moves it.
/// A quick tap still opens the issue, and a swipe scrolls and lifts nothing.
///
/// <para>Playwright's mouse cannot press and hold a touch, so the touches are
/// the Chrome DevTools Protocol's <c>Input.dispatchTouchEvent</c>. That proves
/// the board's sensor path. It does not prove Safari's own gestures, which only
/// an iPhone can; the operator's look is the last word.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class BoardTouchDragTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 150;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    /// <summary>Longer than the board's 300ms lift delay, and then some: the iOS menu comes a beat after the lift.</summary>
    private const int HoldMs = 700;

    /// <summary>How far the lifted card may drift from where the finger has taken it.</summary>
    private const double FollowTolerancePx = 30;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task ALiftedCard_StaysUnderTheFinger_AndDropsInAnotherColumn()
    {
        await RunAsync("BoardTouchDragMove", 0, async (harness, deadline, http, cdp, page, projectId) =>
        {
            var board = await BoardAsync(http);
            var source = board.Issues.Single(i => i.Title == "Held card");

            // A column a drop may land in: not one an approver guards, and not one that merges a pull request.
            // The stacked view draws no column drop zones, so the drop is aimed at a card standing in it.
            var target = board.Statuses.OrderBy(s => s.SortOrder).First(s => s.Id != source.StatusId && !s.IsDeferred && !s.IsTerminal && !s.IsProtected && !s.MergesPullRequest);
            var anchor = await PostAsync<IssueCreateRequest, IssueDto>(http, "/api/issues", new(projectId, "task", "Anchor card", null, null, null, null));
            await PostAsync<IssueMoveRequest, IssueDto>(http, $"/api/issues/{anchor.Key}/move", new(target.Id, null, null));
            await harness.GoToAsync(deadline, "/");
            await harness.WaitForFirstColumnAsync(deadline);

            var card = page.Locator(".hatch-card", new() { HasText = "Held card" }).First;
            var anchorCard = page.Locator(".hatch-card", new() { HasText = "Anchor card" }).First;
            await deadline.BoundAsync("waiting for the anchor card", () => anchorCard.WaitForAsync(new() { State = WaitForSelectorState.Visible }));
            var from = Centre((await card.BoundingBoxAsync())!);
            var destination = Centre((await anchorCard.BoundingBoxAsync())!);
            var viewport = await page.EvaluateAsync<double>("() => window.innerHeight");
            Assert.True(destination.Y < viewport, $"the anchor card's centre ({destination.Y}) is below the {viewport}px screen: the case needs it in view.");
            var scrollBefore = await ScrollAsync(page);

            await TouchAsync(deadline, cdp, "touchStart", from);
            await Task.Delay(HoldMs);

            var preview = page.Locator(".hatch-card-preview");
            await deadline.BoundAsync("waiting for the card to lift", () => preview.WaitForAsync(new() { State = WaitForSelectorState.Visible }));
            var lifted = Centre((await preview.BoundingBoxAsync())!);
            await harness.ScreenshotAsync(deadline, "1-lifted");

            // Held still a while longer: no menu, no selection, no scroll, and the card has not gone.
            await Task.Delay(HoldMs);
            Assert.Equal(await ScrollAsync(page), scrollBefore);
            Assert.Equal("none", await card.EvaluateAsync<string>("el => getComputedStyle(el).userSelect"));
            Assert.Equal("false", await card.GetAttributeAsync("draggable"));
            Assert.Equal("", await page.EvaluateAsync<string>("() => String(getSelection())"));
            Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.classList.contains('hatch-lifting')"));

            const int steps = 8;
            for (var i = 1; i <= steps; i++)
            {
                var finger = (X: from.X + (destination.X - from.X) * i / steps, Y: from.Y + (destination.Y - from.Y) * i / steps);
                await TouchAsync(deadline, cdp, "touchMove", finger);
                await Task.Delay(40);
                var now = Centre((await preview.BoundingBoxAsync())!);
                Assert.InRange(Math.Abs(now.X - lifted.X - (finger.X - from.X)), 0, FollowTolerancePx);
                Assert.InRange(Math.Abs(now.Y - lifted.Y - (finger.Y - from.Y)), 0, FollowTolerancePx);
            }

            // dnd-kit names what is under the finger a frame after it arrives; a person's finger rests longer than a driver's.
            await Task.Delay(400);
            await harness.ScreenshotAsync(deadline, "2-over-column");
            await TouchAsync(deadline, cdp, "touchEnd", destination);
            await Task.Delay(1500);
            await harness.ScreenshotAsync(deadline, "3-dropped");

            Assert.Equal(target.Id, (await BoardAsync(http)).Issues.Single(i => i.Key == source.Key).StatusId);
            Assert.Equal(0, await preview.CountAsync());
            Assert.False(await page.EvaluateAsync<bool>("() => document.documentElement.classList.contains('hatch-lifting')"));
            var toast = page.Locator(".hatch-confirmation", new() { HasText = source.Key });
            await deadline.BoundAsync("waiting for the undo toast", () => toast.WaitForAsync(new() { State = WaitForSelectorState.Visible }));
        });
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task ALiftedCard_HeldNearTheBottom_ScrollsTheBoard()
    {
        await RunAsync("BoardTouchDragEdge", 8, async (harness, deadline, http, cdp, page, projectId) =>
        {
            var card = page.Locator(".hatch-card", new() { HasText = "Held card" }).First;
            var from = Centre((await card.BoundingBoxAsync())!);
            var scrollBefore = await ScrollAsync(page);
            var height = await page.EvaluateAsync<double>("() => window.innerHeight");

            await TouchAsync(deadline, cdp, "touchStart", from);
            await Task.Delay(HoldMs);
            await deadline.BoundAsync("waiting for the card to lift", () => page.Locator(".hatch-card-preview").WaitForAsync(new() { State = WaitForSelectorState.Visible }));

            var edge = (X: from.X, Y: height - 12);
            for (var i = 1; i <= 6; i++)
            {
                await TouchAsync(deadline, cdp, "touchMove", (from.X, from.Y + (edge.Y - from.Y) * i / 6));
                await Task.Delay(40);
            }

            await Task.Delay(1500);
            var scrolled = await ScrollAsync(page);
            await harness.ScreenshotAsync(deadline, "4-at-the-edge");
            await TouchAsync(deadline, cdp, "touchEnd", edge);

            Assert.True(scrolled > scrollBefore, $"the board did not scroll under a card held at the bottom edge ({scrollBefore} -> {scrolled}).");
        });
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task ATap_OpensTheIssue_AndASwipe_LiftsNothing()
    {
        await RunAsync("BoardTouchDragTap", 8, async (harness, deadline, http, cdp, page, projectId) =>
        {
            // A swipe across the cards: the browser takes it, and nothing lifts.
            var card = page.Locator(".hatch-card", new() { HasText = "Held card" }).First;
            var from = Centre((await card.BoundingBoxAsync())!);
            await TouchAsync(deadline, cdp, "touchStart", from);
            for (var i = 1; i <= 5; i++)
            {
                await TouchAsync(deadline, cdp, "touchMove", (from.X, from.Y - 8 * i));
                await Task.Delay(20);
            }

            await Task.Delay(HoldMs);
            Assert.Equal(0, await page.Locator(".hatch-card-preview").CountAsync());
            await TouchAsync(deadline, cdp, "touchEnd", (from.X, from.Y - 40));
            await Task.Delay(300);
            Assert.DoesNotContain("/issues/", page.Url);

            // A quick tap opens the issue.
            from = Centre((await card.BoundingBoxAsync())!);
            await TouchAsync(deadline, cdp, "touchStart", from);
            await Task.Delay(50);
            await TouchAsync(deadline, cdp, "touchEnd", from);
            await deadline.BoundAsync("waiting for the issue page", () => page.WaitForURLAsync("**/issues/**"));
            await harness.ScreenshotAsync(deadline, "5-tapped");
        });
    }


    private async Task RunAsync(string name, int fillers, Func<E2eHarness, Deadline, HttpClient, ICDPSession, IPage, int, Task> body)
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness(name);
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline, DeviceProfile.IPhone14ProChromium);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

            var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("E2E", "E2E"));
            await PostAsync<IssueCreateRequest, IssueDto>(http, "/api/issues", new(project.Id, "task", "Held card", null, null, null, null));
            for (var i = 1; i <= fillers; i++)
                await PostAsync<IssueCreateRequest, IssueDto>(http, "/api/issues", new(project.Id, "task", $"Filler card {i}", null, null, null, null));

            await harness.OpenBoardAsync(deadline, baseUrl);
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForFirstColumnAsync(deadline);
            await harness.Page!.Locator(".hatch-card", new() { HasText = "Held card" }).First.WaitForAsync(new() { State = WaitForSelectorState.Visible });

            var cdp = await harness.NewCdpSessionAsync(deadline);
            await body(harness, deadline, http, cdp, harness.Page!, project.Id);
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

    private static (double X, double Y) Centre(LocatorBoundingBoxResult box) => (box.X + box.Width / 2, box.Y + box.Height / 2);

    private static Task<double> ScrollAsync(IPage page) =>
        page.EvaluateAsync<double>(
            "() => window.scrollY + [...document.querySelectorAll('*')].reduce((n, el) => n + (el.scrollHeight > el.clientHeight && /auto|scroll/.test(getComputedStyle(el).overflowY) ? el.scrollTop : 0), 0)");

    private static Task TouchAsync(Deadline deadline, ICDPSession cdp, string type, (double X, double Y) at) =>
        deadline.BoundAsync($"sending {type}", () => cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = type,
            ["touchPoints"] = type == "touchEnd"
                ? Array.Empty<object>()
                : new object[] { new Dictionary<string, object> { ["x"] = at.X, ["y"] = at.Y, ["id"] = 1 } },
        }));

    private static async Task<BoardDto> BoardAsync(HttpClient http) =>
        (await http.GetFromJsonAsync<BoardDto>("/api/board", Web))!;

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

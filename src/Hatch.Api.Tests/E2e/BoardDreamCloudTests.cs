using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Microsoft.Playwright;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2348's acceptance, driven: the dream cloud is not drawn at rest, lights
/// under a drag, and a drop on it opens a dialog that moves nothing and sends
/// nothing until Remand is pressed. Run at the desktop viewport (Chromium) and
/// at the <c>iPhone 14 Pro</c> descriptor in Chromium, where the board is
/// stacked and has no column droppables for the pointer to be inside. Chromium
/// rather than the WebKit the phone smoke runs in: the stacked layout is the
/// viewport's, and this case is about the layout, not the engine.
///
/// <para>The drag is the mouse's: the board's mouse sensor takes it on either
/// profile, where the touch sensor's press-and-hold is a gesture Playwright's
/// mouse cannot make. A screenshot of each state is left in the case's artifact
/// directory.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class BoardDreamCloudTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 120;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheCloud_AsksFirst_OnTheDesktop() => RunAsync("BoardDreamCloudDesktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheCloud_AsksFirst_OnThePhone() => RunAsync("BoardDreamCloudPhone", DeviceProfile.IPhone14ProChromium);

    private async Task RunAsync(string name, DeviceProfile? profile)
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
            using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

            var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("E2E", "E2E"));
            var parent = await PostAsync<IssueCreateRequest, IssueDto>(http, "/api/issues", new(project.Id, "story", "Parent card", null, null, null, null));
            var child = await PostAsync<IssueCreateRequest, IssueDto>(http, "/api/issues", new(project.Id, "task", "Child card", null, parent.Key, null, null));
            var spare = await PostAsync<IssueCreateRequest, IssueDto>(http, "/api/issues", new(project.Id, "task", "Spare card", null, null, null, null));

            await harness.OpenBoardAsync(deadline, baseUrl);
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForFirstColumnAsync(deadline);
            var page = harness.Page!;

            var remands = 0;
            page.Request += (_, request) =>
            {
                if (request.Url.Contains("/remand", StringComparison.Ordinal)) Interlocked.Increment(ref remands);
            };

            // No deferred column yet: a drag shows no cloud at all.
            var before = await BoardAsync(http);
            Assert.DoesNotContain(before.Statuses, s => s.IsDeferred);
            await BeginDragAsync(deadline, page, "Parent card");
            Assert.Equal(0, await page.Locator(".hatch-remand-cloud").CountAsync());
            await page.Mouse.UpAsync();

            // Add the shelf and reload, so the board has one.
            await PostAsync<StatusCreateRequest, StatusDto>(http, "/api/statuses", new("Shelved", null, null, null, true));
            await harness.GoToAsync(deadline, "/");
            await harness.WaitForFirstColumnAsync(deadline);

            // The cloud mounts after the drag has begun and pushes the columns down. A drop near the foot of the
            // last column, read off where the column is by then, lands only if dnd-kit has measured it again.
            if (profile is null)
            {
                var lastColumn = before.Statuses.Where(st => !st.IsDeferred).OrderBy(st => st.SortOrder).Last();
                await BeginDragAsync(deadline, page, "Spare card");
                await deadline.BoundAsync("waiting for the cloud", () => page.Locator(".hatch-remand-cloud").WaitForAsync(new() { State = WaitForSelectorState.Visible }));
                var last = (await page.Locator(E2eHarness.Column).Last.BoundingBoxAsync())!;
                await page.Mouse.MoveAsync(last.X + last.Width / 2, last.Y + last.Height - 10, new() { Steps = 10 });
                await page.Mouse.UpAsync();
                await Settle();
                Assert.Equal(lastColumn.Id, (await BoardAsync(http)).Issues.Single(i => i.Key == spare.Key).StatusId);
            }

            // At rest: no cloud. Mid-drag: the cloud, unlit until the pointer is over it.
            Assert.Equal(0, await page.Locator(".hatch-remand-cloud").CountAsync());
            await BeginDragAsync(deadline, page, "Parent card");
            var cloud = page.Locator(".hatch-remand-cloud");
            await deadline.BoundAsync("waiting for the cloud", () => cloud.WaitForAsync(new() { State = WaitForSelectorState.Visible }));
            Assert.Equal("Remand to a dream", (await cloud.InnerTextAsync()).Trim());
            Assert.DoesNotContain("lit", await cloud.GetAttributeAsync("class"));
            await harness.ScreenshotAsync(deadline, "1-cloud-while-dragging");

            await HoverAsync(page, cloud);
            await deadline.BoundAsync("waiting for the cloud to light", () => page.Locator(".hatch-remand-cloud.lit").WaitForAsync());
            await harness.ScreenshotAsync(deadline, "2-cloud-lit");

            // The drop moves nothing, and the dialog names what would go.
            await page.Mouse.UpAsync();
            await Settle();
            var dialog = page.Locator("[aria-modal=\"true\"]");
            await deadline.BoundAsync("waiting for the dialog", () => dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible }));
            Assert.Contains("Remand " + parent.Key + " to a dream?", await dialog.InnerTextAsync());
            Assert.Contains(child.Key, await dialog.InnerTextAsync());
            Assert.Contains("Shelved", await dialog.InnerTextAsync());
            Assert.Equal(0, await dialog.Locator("select").CountAsync());
            Assert.Equal(0, Volatile.Read(ref remands));
            await harness.ScreenshotAsync(deadline, "3-dialog");

            var asked = await BoardAsync(http);
            Assert.Equal(before.Issues.Single(i => i.Key == parent.Key).StatusId, asked.Issues.Single(i => i.Key == parent.Key).StatusId);

            // Leave it: nothing is sent, nothing moved.
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Leave it on the board" }).ClickAsync();
            await deadline.BoundAsync("waiting for the dialog to close", () => dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden }));
            Assert.Equal(0, Volatile.Read(ref remands));
            var left = await BoardAsync(http);
            Assert.All(new[] { parent.Key, child.Key }, k =>
                Assert.Equal(before.Issues.Single(i => i.Key == k).StatusId, left.Issues.Single(i => i.Key == k).StatusId));

            // A refusal shows in the dialog in the server's words, and it stays open.
            await page.Context.RouteAsync("**/api/issues/*/remand", route =>
                route.FulfillAsync(new() { Status = 400, ContentType = "application/json", Body = "\"no room on the shelf\"" }));
            await DragToCloudAsync(deadline, page, "Parent card");
            await deadline.BoundAsync("waiting for the dialog", () => dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible }));
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Remand", Exact = true }).ClickAsync();
            await deadline.BoundAsync("waiting for the refusal", () => dialog.GetByText("no room on the shelf").WaitForAsync());
            Assert.True(await dialog.IsVisibleAsync());
            await harness.ScreenshotAsync(deadline, "4-refusal");
            await harness.UnrouteAllAsync(deadline);

            // Remand for real: the board reloads, and the chicklet says so, links to the dream, and has no Undo.
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Remand", Exact = true }).ClickAsync();
            var chicklet = page.Locator(".hatch-confirmation", new() { HasText = "remanded to a dream" });
            await deadline.BoundAsync("waiting for the chicklet", () => chicklet.WaitForAsync(new() { State = WaitForSelectorState.Visible }));
            Assert.Contains("with 1 issue under it", await chicklet.InnerTextAsync());
            Assert.Equal(0, await chicklet.Locator(".hatch-confirmation-undo").CountAsync());
            var href = await chicklet.GetByText("See the dream").GetAttributeAsync("href");
            Assert.Contains("/dreams?remandedFrom=" + parent.Key, href);
            await harness.ScreenshotAsync(deadline, "5-chicklet");

            var done = await BoardAsync(http);
            var shelf = done.Statuses.Single(s => s.IsDeferred).Id;
            Assert.All(new[] { parent.Key, child.Key }, k => Assert.Equal(shelf, done.Issues.Single(i => i.Key == k).StatusId));

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

    /// <summary>Presses on the card and drags it a little, far enough for the mouse sensor to take it; the button stays down.</summary>
    private static async Task BeginDragAsync(Deadline deadline, IPage page, string title)
    {
        var card = page.Locator(".hatch-card", new() { HasText = title }).First;
        await deadline.BoundAsync("finding the card", () => card.WaitForAsync(new() { State = WaitForSelectorState.Visible }));
        var box = (await card.BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box.X + box.Width / 2 + 12, box.Y + box.Height / 2 + 12, new() { Steps = 4 });
        await page.Mouse.MoveAsync(box.X + box.Width / 2 + 24, box.Y + box.Height / 2 + 24, new() { Steps = 4 });
    }

    private static async Task HoverAsync(IPage page, ILocator target)
    {
        var box = (await target.BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2, new() { Steps = 10 });
    }

    private static async Task DragToCloudAsync(Deadline deadline, IPage page, string title)
    {
        await BeginDragAsync(deadline, page, title);
        var cloud = page.Locator(".hatch-remand-cloud");
        await deadline.BoundAsync("waiting for the cloud", () => cloud.WaitForAsync(new() { State = WaitForSelectorState.Visible }));
        await HoverAsync(page, cloud);
        await deadline.BoundAsync("waiting for the cloud to light", () => page.Locator(".hatch-remand-cloud.lit").WaitForAsync());
        await page.Mouse.UpAsync();
        await Settle();
    }

    /// <summary>
    /// dnd-kit swallows the click that follows a drop, and takes its listener
    /// off on a 50ms timer a busy headless page runs late. A person reading a
    /// dialog takes longer than this to press anything; a driver does not.
    /// </summary>
    private static Task Settle() => Task.Delay(1500);

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

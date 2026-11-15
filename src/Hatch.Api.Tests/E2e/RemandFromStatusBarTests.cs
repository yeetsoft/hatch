using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Microsoft.Playwright;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2351's acceptance, driven: each deferred column in the issue page's
/// status bar and in the board peek's status picker has a second press, "to a
/// dream", that opens the remand dialog with that column chosen. Leaving the
/// dialog sends nothing; Remand sends one request and the page reloads. Run at
/// the desktop viewport, where the peek is, and at the <c>iPhone 14 Pro</c>
/// descriptor in Chromium for the issue page alone - a phone opens a card as a
/// page, so there is no peek there to press in.
///
/// <para>A screenshot of each state is left in the case's artifact
/// directory.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class RemandFromStatusBarTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 120;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheStatusBar_Remands_OnTheDesktop() => RunAsync("RemandFromStatusBarDesktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheStatusBar_Remands_OnThePhone() => RunAsync("RemandFromStatusBarPhone", DeviceProfile.IPhone14ProChromium);

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

            // A board with no deferred column draws no press at all.
            await harness.OpenAsync(deadline, baseUrl, "/issues/" + parent.Key);
            var page = harness.Page!;
            var picker = page.GetByRole(AriaRole.Button, new() { NameRegex = new("^Status: .* Move " + parent.Key) });
            await deadline.BoundAsync("waiting for the status pill", () => picker.WaitForAsync(new() { State = WaitForSelectorState.Visible }));
            await picker.ClickAsync();
            await deadline.BoundAsync("waiting for the band", () => page.Locator(".hatch-status-band").WaitForAsync(new() { State = WaitForSelectorState.Visible }));
            Assert.Equal(0, await page.Locator(".hatch-status-remand").CountAsync());
            Assert.Equal(0, await page.Locator(".hatch-status-shelf-label").CountAsync());

            // Add the shelf and reload, so the board has one.
            await PostAsync<StatusCreateRequest, StatusDto>(http, "/api/statuses", new("Shelved", null, null, null, true));
            var shelf = (await BoardAsync(http)).Statuses.Single(s => s.IsDeferred);
            await harness.GoToAsync(deadline, "/issues/" + parent.Key);

            var remands = 0;
            page.Request += (_, request) =>
            {
                if (request.Url.Contains("/remand", StringComparison.Ordinal) && request.Method == "POST") Interlocked.Increment(ref remands);
            };

            // The issue page: one "to a dream" press beside the shelf, none beside a lane.
            await deadline.BoundAsync("waiting for the status pill", () => picker.WaitForAsync(new() { State = WaitForSelectorState.Visible }));
            await picker.ClickAsync();
            var press = page.GetByRole(AriaRole.Button, new() { Name = $"Remand {parent.Key} to a dream in Shelved" });
            await deadline.BoundAsync("waiting for the press", () => press.WaitForAsync(new() { State = WaitForSelectorState.Visible }));
            Assert.Equal(1, await page.Locator(".hatch-status-remand").CountAsync());
            Assert.Equal("to a dream", (await press.InnerTextAsync()).Trim());
            await Task.Delay(500); // the band fades in; a shot mid-fade shows nothing
            output.WriteLine("issue page press: " + await harness.ScreenshotAsync(deadline, "1-issue-page-press"));

            var dialog = page.Locator("[aria-modal=\"true\"]");
            await press.ClickAsync();
            await deadline.BoundAsync("waiting for the dialog", () => dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible }));
            var text = await dialog.InnerTextAsync();
            Assert.Contains("Remand " + parent.Key + " to a dream?", text);
            Assert.Contains(child.Key, text);
            Assert.Contains("Shelved", text);
            Assert.Equal(0, Volatile.Read(ref remands));
            output.WriteLine("issue page dialog: " + await harness.ScreenshotAsync(deadline, "2-issue-page-dialog"));

            // Leave it: nothing is sent, nothing moved.
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Leave it on the board" }).ClickAsync();
            await deadline.BoundAsync("waiting for the dialog to close", () => dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden }));
            Assert.Equal(0, Volatile.Read(ref remands));
            Assert.NotEqual(shelf.Id, (await BoardAsync(http)).Issues.Single(i => i.Key == parent.Key).StatusId);

            // Press again and confirm: one request, and the page reloads onto the shelf.
            if (!await press.IsVisibleAsync()) await picker.ClickAsync();
            await press.ClickAsync();
            await deadline.BoundAsync("waiting for the dialog", () => dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible }));
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Remand", Exact = true }).ClickAsync();
            await deadline.BoundAsync("waiting for the dialog to close", () => dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden }));
            await deadline.BoundAsync("waiting for the page to read Shelved", () =>
                page.Locator(".hatch-status-bar .hatch-status-pill-press", new() { HasText = "Shelved" }).WaitForAsync());
            Assert.Equal(1, Volatile.Read(ref remands));
            var done = await BoardAsync(http);
            Assert.All(new[] { parent.Key, child.Key }, k => Assert.Equal(shelf.Id, done.Issues.Single(i => i.Key == k).StatusId));
            output.WriteLine("issue page remanded: " + await harness.ScreenshotAsync(deadline, "3-issue-page-remanded"));

            // The board's peek, on the desktop only: a phone opens a card as a page.
            if (profile is null)
            {
                await harness.GoToAsync(deadline, "/");
                await harness.WaitForFirstColumnAsync(deadline);
                await deadline.BoundAsync("peeking the card", () =>
                    page.Locator(".hatch-card", new() { HasText = "Spare card" }).First.ClickAsync());
                var peekPicker = page.Locator("[aria-modal='true']").GetByRole(AriaRole.Button, new() { NameRegex = new("^Status: .* Move " + spare.Key) });
                await deadline.BoundAsync("waiting for the peek's pill", () => peekPicker.WaitForAsync(new() { State = WaitForSelectorState.Visible }));
                await peekPicker.ClickAsync();
                var peekPress = page.GetByRole(AriaRole.Button, new() { Name = $"Remand {spare.Key} to a dream in Shelved" });
                await deadline.BoundAsync("waiting for the peek's press", () => peekPress.WaitForAsync(new() { State = WaitForSelectorState.Visible }));
                await Task.Delay(500); // the band fades in; a shot mid-fade shows nothing
                output.WriteLine("peek press: " + await harness.ScreenshotAsync(deadline, "4-peek-press"));

                await peekPress.ClickAsync();
                var remandDialog = page.Locator("[aria-modal=\"true\"]", new() { HasText = "to a dream?" });
                await deadline.BoundAsync("waiting for the dialog", () => remandDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible }));
                Assert.Contains("Remand " + spare.Key + " to a dream?", await remandDialog.InnerTextAsync());
                Assert.Equal(1, Volatile.Read(ref remands));
                output.WriteLine("peek dialog: " + await harness.ScreenshotAsync(deadline, "5-peek-dialog"));

                await remandDialog.GetByRole(AriaRole.Button, new() { Name = "Leave it on the board" }).ClickAsync();
                await deadline.BoundAsync("waiting for the dialog to close", () => remandDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden }));
                Assert.Equal(1, Volatile.Read(ref remands));
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

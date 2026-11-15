using System.Net.Http.Json;
using System.Text.Json;
using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2338's Status commands in the console, driven on the real app: on an
/// issue's page <c>&gt;status</c> lists the columns with the current one and the
/// protected ones disabled, <c>&gt;done</c> on an epic with open stories asks to
/// close them inline and Yes closes them, on the board a card focused by
/// keyboard is moved by <c>/</c>, <c>&gt;todo</c> and Enter - with the Undo
/// chicklet raised, and Go to Docs not reached - a page with no card in view
/// offers no Status row, and a refusal from the server is said in the status
/// line with the console left open. The wall is off, which reads as a person
/// who is no approver.
///
/// <para>Desktop in Chromium, and the phone profile in WebKit.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class ConsoleStatusTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 150;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Prompt = ".hatch-omni-input";
    private const string Status = ".hatch-omni-status [aria-live]";
    private const string Options = ".hatch-omni [role=option]";
    private const string Panel = ".hatch-omni";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task Desktop_StatusFromTheConsole() => await RunAsync("desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task Phone_StatusFromTheConsole()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        await RunAsync("phone", DeviceProfile.IPhone14Pro);
    }

    private async Task RunAsync(string where, DeviceProfile? profile)
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness($"ConsoleStatus-{where}");
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
            var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("STAT", "Status Checks"));
            var epic = await CreateAsync(http, project.Id, "epic", "An epic with open stories", null);
            var first = await CreateAsync(http, project.Id, "story", "First open story", epic.Key);
            var second = await CreateAsync(http, project.Id, "story", "Second open story", epic.Key);
            var task = await CreateAsync(http, project.Id, "task", "A task to move by keyboard", null);

            var statuses = await GetAsync<List<StatusDto>>(http, "/api/statuses");
            var todo = statuses.Single(s => s.Name == "To Do");
            var done = statuses.Single(s => s.Name == "Done");
            var current = statuses.Single(s => s.Id == epic.StatusId);
            output.WriteLine($"columns: {string.Join(", ", statuses.Select(s => $"{s.Name}{(s.IsProtected ? " (protected)" : "")}"))}; new issues start in {current.Name}");

            // ---- The issue page --------------------------------------------------
            await harness.OpenAsync(deadline, baseUrl, $"/issues/{epic.Key}");
            await harness.WaitForTopBarAsync(deadline);
            var page = harness.Page!;
            await harness.WaitForVisibleAsync(deadline, ".hatch-issue-page");

            await deadline.BoundAsync("opening the console", () => page.Keyboard.PressAsync("/"));
            await harness.WaitForVisibleAsync(deadline, Prompt);
            await Type(harness, deadline, ">");
            // A bare `>` on an issue offers Status first.
            Assert.Equal("Status", (await Names(harness, deadline))[0]);

            await Type(harness, deadline, ">status");
            var names = await Names(harness, deadline);
            Assert.Equal("Status", names[0]);
            Assert.Equal(statuses.Count, names.Count(n => n.StartsWith("Status → ", StringComparison.Ordinal)));
            Assert.Equal(1, await Count(harness, deadline, $"{Options}:has(.hatch-omni-type:text-is('current'))"));
            Assert.Equal(statuses.Count(s => s.IsProtected), await Count(harness, deadline, $"{Options}:has(.hatch-omni-type:text-is('blocked'))"));
            output.WriteLine("issue page >status: " + await harness.ScreenshotAsync(deadline, "1-page-status"));

            // The step lists the columns by name alone, and Escape goes back to `>`.
            await Type(harness, deadline, ">");
            await deadline.BoundAsync("opening Status", () => page.Keyboard.PressAsync("Enter"));
            Assert.Equal("status", await Text(harness, deadline, ".hatch-omni-verb"));
            Assert.Equal(statuses.Select(s => s.Name), await Names(harness, deadline));
            output.WriteLine("issue page step: " + await harness.ScreenshotAsync(deadline, "2-page-step"));
            await deadline.BoundAsync("leaving the step", () => page.Keyboard.PressAsync("Escape"));
            Assert.Equal("run", await Text(harness, deadline, ".hatch-omni-verb"));

            // Landing on a disabled row says why in the status line; Enter on it does nothing.
            await Type(harness, deadline, $">status → {current.Name}");
            Assert.Equal($"Already in {current.Name}", await Text(harness, deadline, Status));
            await deadline.BoundAsync("pressing Enter on the current column", () => page.Keyboard.PressAsync("Enter"));
            Assert.Equal(1, await Count(harness, deadline, Panel));

            // `>done` and Enter on an epic with open stories asks inline.
            await Type(harness, deadline, ">done");
            Assert.Equal("Status → Done", (await Names(harness, deadline))[0]);
            await deadline.BoundAsync("moving to Done", () => page.Keyboard.PressAsync("Enter"));
            await harness.WaitForVisibleAsync(deadline, $"{Panel}:has-text('Close its 2 open children too?')");
            Assert.Equal(["Yes", "No"], await Texts(harness, deadline, $"{Options} .hatch-omni-title"));
            output.WriteLine("inline question: " + await harness.ScreenshotAsync(deadline, "3-question"));
            await deadline.BoundAsync("answering Yes", () => page.Keyboard.PressAsync("Enter"));
            await harness.WaitForHiddenAsync(deadline, Panel);

            Assert.Equal(done.Id, (await GetAsync<IssueDto>(http, $"/api/issues/{first.Key}")).StatusId);
            Assert.Equal(done.Id, (await GetAsync<IssueDto>(http, $"/api/issues/{second.Key}")).StatusId);
            Assert.Equal(done.Id, (await GetAsync<IssueDto>(http, $"/api/issues/{epic.Key}")).StatusId);
            output.WriteLine("issue page after Yes: " + await harness.ScreenshotAsync(deadline, "4-page-after"));

            // ---- A page with no card in view -------------------------------------
            await harness.GoToAsync(deadline, "/plan");
            await harness.WaitForTopBarAsync(deadline);
            await deadline.BoundAsync("opening the console", () => page.Keyboard.PressAsync("/"));
            await harness.WaitForVisibleAsync(deadline, Prompt);
            await Type(harness, deadline, ">status");
            Assert.DoesNotContain(await Names(harness, deadline), n => n.StartsWith("Status", StringComparison.Ordinal));
            await deadline.BoundAsync("pressing Escape", () => page.Keyboard.PressAsync("Escape"));
            await harness.WaitForHiddenAsync(deadline, Panel);

            // ---- The board -------------------------------------------------------
            await harness.GoToAsync(deadline, "/");
            await harness.WaitForVisibleAsync(deadline, E2eHarness.Column);
            var card = $"[data-issue-key='{task.Key}']";
            await harness.WaitForVisibleAsync(deadline, card);

            // Nothing focused, nothing peeked: no Status row on the board either.
            await deadline.BoundAsync("opening the console", () => page.Keyboard.PressAsync("/"));
            await harness.WaitForVisibleAsync(deadline, Prompt);
            await Type(harness, deadline, ">status");
            Assert.DoesNotContain(await Names(harness, deadline), n => n.StartsWith("Status", StringComparison.Ordinal));
            await deadline.BoundAsync("pressing Escape", () => page.Keyboard.PressAsync("Escape"));
            await harness.WaitForHiddenAsync(deadline, Panel);

            // A card focused by keyboard, then `/`, `>todo` and Enter.
            await deadline.BoundAsync("focusing the card", () => page.Locator(card).FocusAsync());
            await deadline.BoundAsync("opening the console", () => page.Keyboard.PressAsync("/"));
            await harness.WaitForVisibleAsync(deadline, Prompt);
            await Type(harness, deadline, ">todo");
            var todoRows = await Names(harness, deadline);
            Assert.Equal("Status → To Do", todoRows[0]);
            output.WriteLine("board >todo: " + await harness.ScreenshotAsync(deadline, "5-board-todo"));
            await deadline.BoundAsync("moving to To Do", () => page.Keyboard.PressAsync("Enter"));
            await harness.WaitForHiddenAsync(deadline, Panel);
            await harness.WaitForVisibleAsync(deadline, ".hatch-confirmation-undo");
            Assert.Equal(todo.Id, (await GetAsync<IssueDto>(http, $"/api/issues/{task.Key}")).StatusId);
            Assert.DoesNotContain("/docs", new Uri(page.Url).AbsolutePath);
            output.WriteLine("board after move: " + await harness.ScreenshotAsync(deadline, "6-board-after"));

            // A peek open, then `/`: the peeked card is the one in view. A phone
            // opens a card as a page, so only the desktop has a peek to try.
            if (profile is null)
            {
                await deadline.BoundAsync("peeking the card", () => page.Locator(card).ClickAsync());
                await harness.WaitForVisibleAsync(deadline, "[aria-modal='true']");
                await deadline.BoundAsync("opening the console over the peek", () => page.Keyboard.PressAsync("/"));
                await harness.WaitForVisibleAsync(deadline, Prompt);
                await Type(harness, deadline, ">status");
                Assert.Equal("Status", (await Names(harness, deadline))[0]);
                output.WriteLine("over a peek: " + await harness.ScreenshotAsync(deadline, "6b-over-peek"));
                await deadline.BoundAsync("pressing Escape", () => page.Keyboard.PressAsync("Escape"));
                await harness.WaitForHiddenAsync(deadline, Panel);
                await deadline.BoundAsync("closing the peek", () => page.Keyboard.PressAsync("Escape"));
                await harness.WaitForHiddenAsync(deadline, "[aria-modal='true']");
            }

            // A refusal is said in the status line, and the console stays.
            await deadline.BoundAsync("refusing the next move", () => page.Context.RouteAsync(
                $"**/api/issues/{task.Key}/move",
                route => route.FulfillAsync(new() { Status = 409, ContentType = "application/json", Body = "\"Refused on purpose, in its own words\"" })));
            await deadline.BoundAsync("focusing the card", () => page.Locator(card).FocusAsync());
            await deadline.BoundAsync("opening the console", () => page.Keyboard.PressAsync("/"));
            await harness.WaitForVisibleAsync(deadline, Prompt);
            await Type(harness, deadline, ">status → in progress");
            await deadline.BoundAsync("moving to In Progress", () => page.Keyboard.PressAsync("Enter"));
            await deadline.BoundAsync("waiting for the refusal", () =>
                page.Locator(Status).Filter(new() { HasText = "Refused on purpose, in its own words" }).WaitForAsync());
            Assert.Equal(1, await Count(harness, deadline, Panel));
            output.WriteLine("refusal: " + await harness.ScreenshotAsync(deadline, "7-refusal"));

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

    private static Task<IssueDto> CreateAsync(HttpClient http, int projectId, string type, string title, string? parentKey) =>
        PostAsync<IssueCreateRequest, IssueDto>(http, "/api/issues", new(projectId, type, title, null, parentKey, null, null));

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }

    private static async Task<T> GetAsync<T>(HttpClient http, string path)
    {
        using var response = await http.GetAsync(path);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"GET {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<T>(text, Web)!;
    }

    private static Task Type(E2eHarness harness, Deadline deadline, string text) =>
        deadline.BoundAsync($"typing {text}", () => harness.Page!.Locator(Prompt).FillAsync(text));

    private static Task<string> Text(E2eHarness harness, Deadline deadline, string selector) =>
        deadline.BoundAsync($"reading {selector}", () => harness.Page!.Locator(selector).InnerTextAsync());

    private static Task<int> Count(E2eHarness harness, Deadline deadline, string selector) =>
        deadline.BoundAsync($"counting {selector}", () => harness.Page!.Locator(selector).CountAsync());

    private static async Task<IReadOnlyList<string>> Texts(E2eHarness harness, Deadline deadline, string selector) =>
        await deadline.BoundAsync($"reading {selector}", () => harness.Page!.Locator(selector).AllInnerTextsAsync());

    /// <summary>The command names listed, without the hint at each row's right.</summary>
    private static Task<IReadOnlyList<string>> Names(E2eHarness harness, Deadline deadline) =>
        Texts(harness, deadline, $"{Options} .hatch-omni-title");
}

using System.Net.Http.Json;
using System.Text.Json;
using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2339's Priority and Pre-approval commands in the console, driven on the
/// real app: on an issue's page <c>&gt;expedited</c> and Enter sets the level and
/// the status bar's pill shows it, <c>&gt;priority</c> and <c>&gt;pre-approval</c>
/// list the levels with the issue's own marked current, <b>Pre-approved</b> is
/// blocked with the permission sentence for a caller who is no approver, a page
/// with no issue in view offers no such row, on the board a card focused by
/// keyboard is set by <c>&gt;paused</c> and the card redraws, a peek that set a
/// level by mouse agrees with a level the console sets after it, and a refusal
/// from the server is said in the status line with the console left open. The
/// wall is off, which reads as the person who made the project, so its owner;
/// the case reads the projects with approval taken away to be one who is not.
///
/// <para>Desktop in Chromium, and the phone profile in WebKit.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class ConsolePriorityTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 150;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Prompt = ".hatch-omni-input";
    private const string Status = ".hatch-omni-status [aria-live]";
    private const string Options = ".hatch-omni [role=option]";
    private const string Panel = ".hatch-omni";
    private const string PagePill = ".hatch-issue-page .hatch-priority-pill";
    private const string PeekPill = "[aria-modal='true'] .hatch-priority-pill";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task Desktop_PriorityFromTheConsole() => await RunAsync("desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task Phone_PriorityFromTheConsole()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        await RunAsync("phone", DeviceProfile.IPhone14Pro);
    }

    private async Task RunAsync(string where, DeviceProfile? profile)
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness($"ConsolePriority-{where}");
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
            var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("PRIO", "Priority Checks"));
            var task = await CreateAsync(http, project.Id, "task", "A task to set by keyboard");

            // ---- The issue page --------------------------------------------------
            // The caller made the project, so the server calls them its owner. The
            // projects are read with approval taken away, to be the caller who is not
            // one: what is under test is the console reading it, not the server's rule.
            await harness.OpenAsync(deadline, baseUrl, $"/issues/{task.Key}", context => context.RouteAsync(
                "**/api/projects",
                async route =>
                {
                    var response = await route.FetchAsync();
                    var body = (await response.TextAsync()).Replace("\"canApprove\":true", "\"canApprove\":false", StringComparison.Ordinal);
                    await route.FulfillAsync(new() { Response = response, Body = body });
                }));
            await harness.WaitForTopBarAsync(deadline);
            var page = harness.Page!;
            await harness.WaitForVisibleAsync(deadline, ".hatch-issue-page");
            await harness.WaitForVisibleAsync(deadline, PagePill);

            await deadline.BoundAsync("opening the console", () => page.Keyboard.PressAsync("/"));
            await harness.WaitForVisibleAsync(deadline, Prompt);
            await Type(harness, deadline, ">");
            // The directory is read as the console opens; the rows follow it.
            await deadline.BoundAsync("waiting for the Priority row", () =>
                page.Locator($"{Options} .hatch-omni-title:text-is('Priority')").WaitForAsync());
            Assert.Equal(["Status", "Priority", "Pre-approval"], (await Names(harness, deadline)).Take(3));
            // Nor does the bare preview fill with a flat row per level.
            Assert.DoesNotContain(await Names(harness, deadline), n => n.StartsWith("Priority →", StringComparison.Ordinal));

            await Type(harness, deadline, ">expedited");
            Assert.Equal("Priority → Expedited", (await Names(harness, deadline))[0]);
            output.WriteLine("issue page >expedited: " + await harness.ScreenshotAsync(deadline, "1-page-expedited"));
            await deadline.BoundAsync("setting Expedited", () => page.Keyboard.PressAsync("Enter"));
            await harness.WaitForHiddenAsync(deadline, Panel);
            await deadline.BoundAsync("waiting for the pill", () =>
                page.Locator(PagePill).Filter(new() { HasText = "Expedited" }).WaitForAsync());
            Assert.Equal("expedited", (await GetAsync<IssueDto>(http, $"/api/issues/{task.Key}")).PriorityOwn);
            output.WriteLine("issue page after: " + await harness.ScreenshotAsync(deadline, "2-page-after"));

            // ---- The priority step -----------------------------------------------
            await deadline.BoundAsync("opening the console", () => page.Keyboard.PressAsync("/"));
            await harness.WaitForVisibleAsync(deadline, Prompt);
            // Off the board the console reads the board as it opens: wait for the rows.
            await Type(harness, deadline, ">priority");
            await deadline.BoundAsync("waiting for the Priority row", () =>
                page.Locator($"{Options} .hatch-omni-title:text-is('Priority')").WaitForAsync());
            Assert.Equal("Priority", (await Names(harness, deadline))[0]);
            await Type(harness, deadline, ">");
            await deadline.BoundAsync("opening Priority", () => page.Locator($"{Options}:has(.hatch-omni-title:text-is('Priority'))").ClickAsync());
            Assert.Equal("priority", await Text(harness, deadline, ".hatch-omni-verb"));
            Assert.Equal(["Emergency", "Expedited", "Normal", "Low", "Economy", "Paused"], await Names(harness, deadline));
            Assert.Equal(1, await Count(harness, deadline, $"{Options}:has(.hatch-omni-type:text-is('current'))"));
            Assert.Equal(
                "Expedited",
                await Text(harness, deadline, $"{Options}:has(.hatch-omni-type:text-is('current')) .hatch-omni-title"));
            output.WriteLine("priority step: " + await harness.ScreenshotAsync(deadline, "3-priority-step"));
            await deadline.BoundAsync("leaving the step", () => page.Keyboard.PressAsync("Escape"));
            Assert.Equal("run", await Text(harness, deadline, ".hatch-omni-verb"));

            // ---- The pre-approval step -------------------------------------------
            await Type(harness, deadline, ">pre-approval");
            await deadline.BoundAsync("waiting for the Pre-approval row", () =>
                page.Locator($"{Options} .hatch-omni-title:text-is('Pre-approval')").WaitForAsync());
            Assert.Equal("Pre-approval", (await Names(harness, deadline))[0]);
            await Type(harness, deadline, ">");
            await deadline.BoundAsync("opening Pre-approval", () => page.Locator($"{Options}:has(.hatch-omni-title:text-is('Pre-approval'))").ClickAsync());
            Assert.Equal("pre-approval", await Text(harness, deadline, ".hatch-omni-verb"));
            Assert.Equal(["None", "Express", "Pre-approved"], await Names(harness, deadline));
            Assert.Equal(
                "None",
                await Text(harness, deadline, $"{Options}:has(.hatch-omni-type:text-is('current')) .hatch-omni-title"));
            Assert.Equal(1, await Count(harness, deadline, $"{Options}:has(.hatch-omni-type:text-is('blocked'))"));
            await Type(harness, deadline, "pre-approved");
            Assert.StartsWith("Only an owner or approver of the project may set pre-approval", await Text(harness, deadline, Status));
            await deadline.BoundAsync("pressing Enter on the blocked row", () => page.Keyboard.PressAsync("Enter"));
            Assert.Equal(1, await Count(harness, deadline, Panel));
            Assert.Equal("none", (await GetAsync<IssueDto>(http, $"/api/issues/{task.Key}")).PreApprovalOwn);
            output.WriteLine("pre-approval step, Pre-approved blocked: " + await harness.ScreenshotAsync(deadline, "4-pre-approval-step"));

            await Type(harness, deadline, "express");
            await deadline.BoundAsync("setting Express", () => page.Keyboard.PressAsync("Enter"));
            await harness.WaitForHiddenAsync(deadline, Panel);
            Assert.Equal("express", (await GetAsync<IssueDto>(http, $"/api/issues/{task.Key}")).PreApprovalOwn);

            // ---- A page with no issue in view ------------------------------------
            await harness.GoToAsync(deadline, "/plan");
            await harness.WaitForTopBarAsync(deadline);
            await deadline.BoundAsync("opening the console", () => page.Keyboard.PressAsync("/"));
            await harness.WaitForVisibleAsync(deadline, Prompt);
            await Type(harness, deadline, ">priority");
            Assert.DoesNotContain(await Names(harness, deadline), n => n.StartsWith("Priority", StringComparison.Ordinal));
            await Type(harness, deadline, ">pre-approval");
            Assert.DoesNotContain(await Names(harness, deadline), n => n.StartsWith("Pre-approval", StringComparison.Ordinal));
            await deadline.BoundAsync("pressing Escape", () => page.Keyboard.PressAsync("Escape"));
            await harness.WaitForHiddenAsync(deadline, Panel);

            // ---- The board -------------------------------------------------------
            await harness.GoToAsync(deadline, "/");
            await harness.WaitForVisibleAsync(deadline, E2eHarness.Column);
            var card = $"[data-issue-key='{task.Key}']";
            await harness.WaitForVisibleAsync(deadline, card);

            await deadline.BoundAsync("focusing the card", () => page.Locator(card).FocusAsync());
            await deadline.BoundAsync("opening the console", () => page.Keyboard.PressAsync("/"));
            await harness.WaitForVisibleAsync(deadline, Prompt);
            await Type(harness, deadline, ">paused");
            await deadline.BoundAsync("waiting for the Paused row", () =>
                page.Locator($"{Options} .hatch-omni-title:text-is('Priority → Paused')").WaitForAsync());
            Assert.Equal("Priority → Paused", (await Names(harness, deadline))[0]);
            output.WriteLine("board >paused: " + await harness.ScreenshotAsync(deadline, "5-board-paused"));
            await deadline.BoundAsync("setting Paused", () => page.Keyboard.PressAsync("Enter"));
            await harness.WaitForHiddenAsync(deadline, Panel);
            await harness.WaitForVisibleAsync(deadline, $"{card}.paused");
            Assert.Equal("paused", (await GetAsync<IssueDto>(http, $"/api/issues/{task.Key}")).PriorityOwn);
            output.WriteLine("board after: " + await harness.ScreenshotAsync(deadline, "6-board-after"));

            // A peek that set a level itself, then the console setting another: the
            // peek's pill follows the card. A phone opens a card as a page, so only
            // the desktop has a peek to try.
            if (profile is null)
            {
                await deadline.BoundAsync("peeking the card", () => page.Locator(card).ClickAsync());
                await harness.WaitForVisibleAsync(deadline, PeekPill);
                await harness.ClickAsync(deadline, PeekPill);
                await harness.ClickAsync(deadline, "[aria-modal='true'] .hatch-priority-step:has-text('Low')");
                await deadline.BoundAsync("waiting for the peek to say Low", () =>
                    page.Locator(PeekPill).Filter(new() { HasText = "Low" }).WaitForAsync());
                await harness.WaitForVisibleAsync(deadline, $"{card}.low");

                await deadline.BoundAsync("opening the console over the peek", () => page.Keyboard.PressAsync("/"));
                await harness.WaitForVisibleAsync(deadline, Prompt);
                await Type(harness, deadline, ">paused");
                await deadline.BoundAsync("waiting for the Paused row", () =>
                    page.Locator($"{Options} .hatch-omni-title:text-is('Priority → Paused')").WaitForAsync());
                await deadline.BoundAsync("setting Paused over the peek", () => page.Keyboard.PressAsync("Enter"));
                await harness.WaitForHiddenAsync(deadline, Panel);
                await deadline.BoundAsync("waiting for the peek to say Paused", () =>
                    page.Locator(PeekPill).Filter(new() { HasText = "Paused" }).WaitForAsync());
                await harness.WaitForVisibleAsync(deadline, $"{card}.paused");
                output.WriteLine("peek after the console: " + await harness.ScreenshotAsync(deadline, "7-peek-after"));
                await deadline.BoundAsync("closing the peek", () => page.Keyboard.PressAsync("Escape"));
                await harness.WaitForHiddenAsync(deadline, "[aria-modal='true']");
            }

            // A refusal is said in the status line, and the console stays.
            await deadline.BoundAsync("refusing the next set", () => page.Context.RouteAsync(
                $"**/api/issues/{task.Key}/priority",
                route => route.FulfillAsync(new() { Status = 409, ContentType = "application/json", Body = "\"Refused on purpose, in its own words\"" })));
            await deadline.BoundAsync("focusing the card", () => page.Locator(card).FocusAsync());
            await deadline.BoundAsync("opening the console", () => page.Keyboard.PressAsync("/"));
            await harness.WaitForVisibleAsync(deadline, Prompt);
            await Type(harness, deadline, ">priority → low");
            await deadline.BoundAsync("waiting for the Low row", () =>
                page.Locator($"{Options} .hatch-omni-title:text-is('Priority → Low')").WaitForAsync());
            await deadline.BoundAsync("setting Low", () => page.Keyboard.PressAsync("Enter"));
            await deadline.BoundAsync("waiting for the refusal", () =>
                page.Locator(Status).Filter(new() { HasText = "Refused on purpose, in its own words" }).WaitForAsync());
            Assert.Equal(1, await Count(harness, deadline, Panel));
            Assert.Equal("paused", (await GetAsync<IssueDto>(http, $"/api/issues/{task.Key}")).PriorityOwn);
            output.WriteLine("refusal: " + await harness.ScreenshotAsync(deadline, "8-refusal"));

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

    private static Task<IssueDto> CreateAsync(HttpClient http, int projectId, string type, string title) =>
        PostAsync<IssueCreateRequest, IssueDto>(http, "/api/issues", new(projectId, type, title, null, null, null, null));

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

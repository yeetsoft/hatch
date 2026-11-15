using System.Net.Http.Json;
using System.Text.Json;
using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2337's filter rows behind the console's <c>&gt;</c> prefix, driven on the
/// real app: on a one-project board only Blocked and Needs me are offered, and
/// once there are two the project rows join them; Enter on Blocked lights its
/// switch and a second take turns it off, a project row writes
/// <c>?project=</c>, and Clear filters takes every filter back to its default
/// and the project off the address. The console closes on each take. Screenshots
/// of each state land in the case's artifact directory for a person to look at.
///
/// <para>Desktop in Chromium, and the phone profile in WebKit.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class ConsoleFilterCommandsTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 120;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Prompt = ".hatch-omni-input";
    private const string Options = ".hatch-omni [role=option]";
    private const string Blocked = ".hatch-board-filters button[aria-pressed]:text-is(\"Blocked\")";
    private const string NeedsMe = ".hatch-board-filters button[aria-pressed]:text-is(\"Needs me\")";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task Desktop_TheFilterRows() => await RunAsync("desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task Phone_TheFilterRows()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        await RunAsync("phone", DeviceProfile.IPhone14Pro);
    }

    private async Task RunAsync(string where, DeviceProfile? profile)
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness($"ConsoleFilterCommands-{where}");
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
            await deadline.BoundAsync("filing the first project", () => FileProjectAsync(http, "ONE", "Alpha"));

            await harness.OpenBoardAsync(deadline, baseUrl);
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForFirstColumnAsync(deadline);
            var page = harness.Page!;

            // One project: no project rows, and nothing set to clear.
            await OpenConsoleAsync(harness, deadline, ">filter");
            Assert.Equal(["Filter Blocked", "Filter Needs me"], await Names(harness, deadline));
            Assert.Equal(["off", "off"], await Hints(harness, deadline));
            output.WriteLine("one project: " + await harness.ScreenshotAsync(deadline, "one-project"));
            await Press(harness, deadline, "Escape");
            await harness.WaitForHiddenAsync(deadline, ".hatch-omni");

            await deadline.BoundAsync("filing the second project", () => FileProjectAsync(http, "TWO", "Beta"));
            await harness.GoToAsync(deadline, "/");
            await harness.WaitForFirstColumnAsync(deadline);

            // Blocked: a take lights it, the console closes, a second take turns it off.
            await OpenConsoleAsync(harness, deadline, ">blocked");
            Assert.Equal(["Filter Blocked"], await Names(harness, deadline));
            Assert.Equal(["off"], await Hints(harness, deadline));
            await Press(harness, deadline, "Enter");
            await harness.WaitForHiddenAsync(deadline, ".hatch-omni");
            Assert.Equal("true", await Pressed(harness, deadline, Blocked));
            output.WriteLine("blocked on: " + await harness.ScreenshotAsync(deadline, "blocked-on"));

            await OpenConsoleAsync(harness, deadline, ">blocked");
            Assert.Equal(["on"], await Hints(harness, deadline));
            await Press(harness, deadline, "Enter");
            await harness.WaitForHiddenAsync(deadline, ".hatch-omni");
            Assert.Equal("false", await Pressed(harness, deadline, Blocked));

            // Needs me, the same way.
            await OpenConsoleAsync(harness, deadline, ">needs me");
            Assert.Equal(["Filter Needs me"], await Names(harness, deadline));
            await Press(harness, deadline, "Enter");
            await harness.WaitForHiddenAsync(deadline, ".hatch-omni");
            Assert.Equal("true", await Pressed(harness, deadline, NeedsMe));
            await OpenConsoleAsync(harness, deadline, ">needs me");
            Assert.Equal(["on"], await Hints(harness, deadline));
            await Press(harness, deadline, "Enter");
            await harness.WaitForHiddenAsync(deadline, ".hatch-omni");
            Assert.Equal("false", await Pressed(harness, deadline, NeedsMe));

            // A project: its row is there now, and a take writes ?project=.
            await OpenConsoleAsync(harness, deadline, ">filter");
            Assert.Equal(
                ["Filter Blocked", "Filter Needs me", "Filter ONE — Alpha", "Filter TWO — Beta"],
                await Names(harness, deadline));
            output.WriteLine("two projects: " + await harness.ScreenshotAsync(deadline, "two-projects"));
            await Type(harness, deadline, ">filter two");
            Assert.Equal(["Filter TWO — Beta"], await Names(harness, deadline));
            await Press(harness, deadline, "Enter");
            await harness.WaitForHiddenAsync(deadline, ".hatch-omni");
            await harness.WaitForFunctionAsync(deadline, "waiting for ?project=TWO", "() => new URLSearchParams(location.search).get('project') === 'TWO'");
            output.WriteLine("project set: " + await harness.ScreenshotAsync(deadline, "project-two"));

            // Clear filters: offered now, takes the project off the address.
            await OpenConsoleAsync(harness, deadline, ">clear");
            Assert.Equal(["Clear filters"], await Names(harness, deadline));
            await Press(harness, deadline, "Enter");
            await harness.WaitForHiddenAsync(deadline, ".hatch-omni");
            await harness.WaitForFunctionAsync(deadline, "waiting for ?project= to go", "() => !new URLSearchParams(location.search).has('project')");
            Assert.Equal("false", await Pressed(harness, deadline, Blocked));
            Assert.Equal("false", await Pressed(harness, deadline, NeedsMe));
            Assert.DoesNotContain("project=", page.Url);

            // And with nothing set it is no longer offered.
            await OpenConsoleAsync(harness, deadline, ">clear");
            Assert.Equal([], await Names(harness, deadline));
            output.WriteLine("cleared: " + await harness.ScreenshotAsync(deadline, "cleared"));
            await Press(harness, deadline, "Escape");
            await harness.WaitForHiddenAsync(deadline, ".hatch-omni");

            // Off the board none of these rows is offered.
            await harness.GoToAsync(deadline, "/plan");
            await harness.WaitForTopBarAsync(deadline);
            await OpenConsoleAsync(harness, deadline, ">filter");
            Assert.Equal([], await Names(harness, deadline));
            await Press(harness, deadline, "Escape");

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

    private static async Task FileProjectAsync(HttpClient http, string key, string name)
    {
        using var response = await http.PostAsJsonAsync("/api/projects", new ProjectCreateRequest(key, name), Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST /api/projects answered {(int)response.StatusCode}: {text}");
    }

    private static async Task OpenConsoleAsync(E2eHarness harness, Deadline deadline, string text)
    {
        await deadline.BoundAsync("opening the console", () => harness.Page!.Keyboard.PressAsync("/"));
        await harness.WaitForVisibleAsync(deadline, Prompt);
        await Type(harness, deadline, text);
    }

    private static Task Press(E2eHarness harness, Deadline deadline, string key) =>
        deadline.BoundAsync($"pressing {key}", () => harness.Page!.Keyboard.PressAsync(key));

    private static Task Type(E2eHarness harness, Deadline deadline, string text) =>
        deadline.BoundAsync($"typing {text}", () => harness.Page!.Locator(Prompt).FillAsync(text));

    /// <summary>
    /// A switch's <c>aria-pressed</c>. Read off the attribute, which a closed phone
    /// "Filters" disclosure does not hide, as it would a click or a visibility wait.
    /// </summary>
    private static Task<string?> Pressed(E2eHarness harness, Deadline deadline, string selector) =>
        deadline.BoundAsync($"reading {selector}", () => harness.Page!.Locator(selector).First.GetAttributeAsync("aria-pressed"));

    private static async Task<IReadOnlyList<string>> Texts(E2eHarness harness, Deadline deadline, string selector) =>
        await deadline.BoundAsync($"reading {selector}", () => harness.Page!.Locator(selector).AllInnerTextsAsync());

    /// <summary>The command names listed, without the hint at each row's right. Read as rendered, so the two spaces after "Filter" in a name read as one.</summary>
    private static Task<IReadOnlyList<string>> Names(E2eHarness harness, Deadline deadline) =>
        Texts(harness, deadline, $"{Options} .hatch-omni-title");

    /// <summary>The hint at each listed row's right.</summary>
    private static Task<IReadOnlyList<string>> Hints(E2eHarness harness, Deadline deadline) =>
        Texts(harness, deadline, $"{Options} .hatch-omni-type");
}

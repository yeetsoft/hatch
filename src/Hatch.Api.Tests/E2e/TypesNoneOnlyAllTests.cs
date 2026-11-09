using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Api.Tests.Hatch;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// The board's Types panel driven through its three one-click states on a board
/// of four issues - a story pair, a task and a bug: None unticks every type,
/// Only story keeps the two stories, All puts every type back - and then the
/// choice is shown to survive a reload, which opens the board already filtered.
/// Each state is asserted by itself, so a failure names the one that broke.
///
/// <para>The reload is a second navigation in the same browser context, not a
/// second <see cref="E2eHarness.OpenBoardAsync"/>: that opens a fresh context
/// with empty <c>localStorage</c>, which is where the choice is kept.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class TypesNoneOnlyAllTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 60;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    /// <summary>
    /// The Types panel alone. Every menu's panel is in the DOM whether open or not, and
    /// <c>.hatch-types-panel</c> is shared with the Projects and Assignee panels.
    /// </summary>
    private const string Panel = ".hatch-types-panel[aria-label=\"Issue types\"]";

    private const string Trigger = "button[aria-label=\"Types\"]";

    private const string OnlyStory = Panel + " button[aria-label=\"Only story\"]";

    private const string FooterAll = Panel + " .hatch-types-footer button:text-is(\"All\")";

    private const string FooterNone = Panel + " .hatch-types-footer button:text-is(\"None\")";

    /// <summary>Epic, reflection, story, task and bug.</summary>
    private const int TypeCount = 5;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheTypesPanel_GoesThroughNoneOnlyAll_AndTheChoiceSurvivesAReload()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("TypesNoneOnlyAll");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            await deadline.BoundAsync("filing four issues", () => SeedAsync(baseUrl));

            await harness.OpenBoardAsync(deadline, baseUrl);
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForFirstColumnAsync(deadline);

            // The panel is opened once: a click on an option does not close it, so a second click on the trigger would.
            await harness.ClickAsync(deadline, Trigger);
            await harness.WaitForVisibleAsync(deadline, Panel);

            var total = await TotalAsync(harness, deadline);
            Assert.True(total == TypeCount, $"Setup: the Types panel has {total} checkboxes, expected {TypeCount}; the selector found something else.");

            // 1. None: every type unticked, the board shows nothing of its four.
            await harness.ClickAsync(deadline, FooterNone);
            await harness.WaitForFunctionAsync(
                deadline, "None: waiting for 0 of 4", "() => document.querySelector('.hatch-filter-count')?.textContent.trim() === '0 of 4'");
            var checkedCount = await CheckedCountAsync(harness, deadline);
            var trigger = await TriggerTextAsync(harness, deadline);
            var count = await FilterCountAsync(harness, deadline);
            output.WriteLine($"none: checked {checkedCount}, trigger '{trigger}', count '{count}'");
            Assert.True(checkedCount == 0, $"None: {checkedCount} of {total} checkboxes are still ticked.");
            Assert.True(trigger == "TypesNone", $"None: the Types trigger reads '{trigger}', expected 'TypesNone'.");
            Assert.True(count == "0 of 4", $"None: the filter count reads '{count ?? "(absent)"}', expected '0 of 4'.");

            // 2. Only story: the one type left ticked, and the two stories are what the board shows.
            await harness.ClickAsync(deadline, OnlyStory);
            await harness.WaitForFunctionAsync(
                deadline, "Only: waiting for 2 of 4", "() => document.querySelector('.hatch-filter-count')?.textContent.trim() === '2 of 4'");
            checkedCount = await CheckedCountAsync(harness, deadline);
            trigger = await TriggerTextAsync(harness, deadline);
            count = await FilterCountAsync(harness, deadline);
            output.WriteLine($"only: checked {checkedCount}, trigger '{trigger}', count '{count}'");
            Assert.True(checkedCount == 1, $"Only: {checkedCount} checkboxes are ticked, expected exactly 1 (story).");
            Assert.True(trigger == "TypesStories", $"Only: the Types trigger reads '{trigger}', expected 'TypesStories'.");
            Assert.True(count == "2 of 4", $"Only: the filter count reads '{count ?? "(absent)"}', expected '2 of 4'.");

            // 3. All: every type back, and with nothing else filtering the count goes away altogether.
            await harness.ClickAsync(deadline, FooterAll);
            await harness.WaitForFunctionAsync(
                deadline, "All: waiting for the filter count to go", "() => document.querySelector('.hatch-filter-count') === null");
            checkedCount = await CheckedCountAsync(harness, deadline);
            trigger = await TriggerTextAsync(harness, deadline);
            count = await FilterCountAsync(harness, deadline);
            output.WriteLine($"all: checked {checkedCount}, trigger '{trigger}', count '{count}'");
            Assert.True(checkedCount == total, $"All: {checkedCount} of {total} checkboxes are ticked.");
            Assert.True(trigger == "TypesAll", $"All: the Types trigger reads '{trigger}', expected 'TypesAll'.");
            Assert.True(count is null, $"All: the filter count still reads '{count}', expected it absent.");

            // 4. Only story again, then reload: the choice is what the board opens on.
            await harness.ClickAsync(deadline, OnlyStory);
            await harness.WaitForFunctionAsync(
                deadline, "After reload: waiting for the choice to be made before reloading",
                $"() => document.querySelector('{Trigger}')?.textContent.trim() === 'TypesStories'");

            await harness.GoToAsync(deadline, "");
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForFirstColumnAsync(deadline);
            await harness.WaitForFunctionAsync(
                deadline, "After reload: waiting for the filtered count",
                "() => document.querySelector('.hatch-filter-count')?.textContent.trim() === '2 of 4'");

            trigger = await TriggerTextAsync(harness, deadline);
            count = await FilterCountAsync(harness, deadline);
            output.WriteLine($"after reload: trigger '{trigger}', count '{count}'");
            Assert.True(trigger == "TypesStories", $"After reload: the Types trigger reads '{trigger}', expected 'TypesStories'.");
            Assert.True(count == "2 of 4", $"After reload: the filter count reads '{count ?? "(absent)"}', expected '2 of 4'.");

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

    private static Task<int> CheckedCountAsync(E2eHarness harness, Deadline deadline) =>
        harness.EvaluateAsync<int>(
            deadline, "counting the ticked types",
            "(sel) => [...document.querySelectorAll(sel)].filter(cb => cb.checked).length",
            Panel + " input[type=checkbox]");

    private static Task<int> TotalAsync(E2eHarness harness, Deadline deadline) =>
        harness.EvaluateAsync<int>(
            deadline, "counting the type checkboxes",
            "(sel) => document.querySelectorAll(sel).length",
            Panel + " input[type=checkbox]");

    private static Task<string> TriggerTextAsync(E2eHarness harness, Deadline deadline) =>
        harness.EvaluateAsync<string>(
            deadline, "reading the Types trigger",
            $"() => document.querySelector('{Trigger}').textContent.trim()");

    private static Task<string?> FilterCountAsync(E2eHarness harness, Deadline deadline) =>
        harness.EvaluateAsync<string?>(
            deadline, "reading the filter count",
            "() => document.querySelector('.hatch-filter-count')?.textContent.trim() ?? null");

    /// <summary>
    /// A project and four top-level issues - two stories, a task and a bug - filed the way any
    /// caller files them with the wall off: naming itself with the runner header. None has a
    /// parent, so the board counts all four.
    /// </summary>
    private static async Task SeedAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        http.DefaultRequestHeaders.Add(LocalCaller.RunnerHeader, "e2e");

        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/hatch/projects", new("E2E", "E2E"));
        foreach (var (type, title) in new[] { ("story", "Story one"), ("story", "Story two"), ("task", "Task one"), ("bug", "Bug one") })
            await PostAsync<IssueCreateRequest, IssueDto>(
                http, "/api/hatch/issues", new(project.Id, type, title, null, null, null, null));
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

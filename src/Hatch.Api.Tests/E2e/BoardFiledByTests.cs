using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// The board's Filed by checklist on a board of five issues from two filers and a third
/// with a long name: on the desktop it sits behind <c>…</c> beside Assignee, on the phone
/// it is inline in Filters. Each state is asserted by itself - Only, None, All and Reset -
/// and a screenshot of each profile is left in the case's artifact directory. The phone
/// profile is WebKit, skipped where that build is frozen, as <see cref="TypesOnlyFitsPhoneTests"/> is.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class BoardFiledByTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 60;

    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Panel = ".hatch-types-panel[aria-label=\"Filed by\"]";

    private const string OnlyAda = Panel + " button[aria-label=\"Only Ada\"]";

    private const string FooterAll = Panel + " .hatch-types-footer button:text-is(\"All\")";

    private const string FooterNone = Panel + " .hatch-types-footer button:text-is(\"None\")";

    private const string MoreFilters = "button[aria-label=\"More filters\"]";

    private const string FiltersSummary = "details.hatch-board-filters-more > summary";

    private const string PhoneTrigger = "button.hatch-facet:has(.hatch-facet__label:text-is(\"Filed by\"))";

    private const string Reset = "button[aria-label=\"Reset filters\"]";

    private const string LongName = "a-key-with-a-very-long-name-indeed-for-a-phone";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task FiledBy_SitsBehindMoreFiltersOnTheDesktop_AndGoesThroughOnlyNoneAllAndReset()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("BoardFiledByDesktop");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);
            var baseUrl = await StartAsync(harness, deadline);

            await harness.OpenBoardAsync(deadline, baseUrl);
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForFirstColumnAsync(deadline);

            Assert.False(
                await harness.IsVisibleAsync(deadline, ".hatch-board-filters > button.hatch-facet:has(.hatch-facet__label:text-is(\"Filed by\"))"),
                "Filed by is drawn inline on the desktop row.");

            await harness.ClickAsync(deadline, MoreFilters);
            await harness.WaitForVisibleAsync(deadline, Panel);
            await harness.WaitForFunctionAsync(
                deadline, "waiting for the Filed by headings", "() => document.querySelectorAll('.hatch-more-filters-title').length === 2");
            Assert.Equal(3, await TotalAsync(harness, deadline));
            await harness.ScreenshotAsync(deadline, "desktop-open");

            await harness.ClickAsync(deadline, OnlyAda);
            await harness.WaitForFunctionAsync(deadline, "Only Ada: waiting for 3 of 5", CountIs("3 of 5"));
            Assert.Equal(1, await CheckedCountAsync(harness, deadline));
            Assert.Contains("hatch-facet--lit", await harness.EvaluateAsync<string>(deadline, "reading the More filters class", $"() => document.querySelector('{MoreFilters}').className"));
            Assert.Contains("Filed by: Ada", await harness.EvaluateAsync<string>(deadline, "reading the tooltip", $"() => document.querySelector('{MoreFilters}').title"));
            await harness.ScreenshotAsync(deadline, "desktop-only-ada");

            await harness.ClickAsync(deadline, FooterNone);
            await harness.WaitForFunctionAsync(deadline, "None: waiting for 0 of 5", CountIs("0 of 5"));
            Assert.Equal(0, await CheckedCountAsync(harness, deadline));

            await harness.ClickAsync(deadline, FooterAll);
            await harness.WaitForFunctionAsync(deadline, "All: waiting for the count to go", "() => document.querySelector('.hatch-filter-count') === null");
            Assert.Equal(3, await CheckedCountAsync(harness, deadline));

            await harness.ClickAsync(deadline, OnlyAda);
            await harness.WaitForFunctionAsync(deadline, "Reset: waiting for 3 of 5", CountIs("3 of 5"));
            await harness.ClickAsync(deadline, Reset);
            await harness.WaitForFunctionAsync(deadline, "Reset: waiting for the count to go", "() => document.querySelector('.hatch-filter-count') === null");
            Assert.Equal(3, await CheckedCountAsync(harness, deadline));
            output.WriteLine("desktop: only, none, all and reset behaved");

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
    public async Task FiledBy_IsInlineInFiltersOnThePhone_AndStaysInsideTheViewport()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);

        var harness = new E2eHarness("BoardFiledByPhone");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline, DeviceProfile.IPhone14Pro);
            var baseUrl = await StartAsync(harness, deadline);

            await harness.OpenBoardAsync(deadline, baseUrl);
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForFirstColumnAsync(deadline);

            await harness.ClickAsync(deadline, FiltersSummary);
            await harness.WaitForVisibleAsync(deadline, PhoneTrigger);
            Assert.False(await harness.IsVisibleAsync(deadline, MoreFilters), "More filters is drawn on the phone.");

            await harness.ClickAsync(deadline, PhoneTrigger);
            await harness.WaitForVisibleAsync(deadline, Panel);
            Assert.Equal(3, await TotalAsync(harness, deadline));

            await harness.ClickAsync(deadline, OnlyAda);
            await harness.WaitForFunctionAsync(deadline, "Only Ada: waiting for 3 of 5", CountIs("3 of 5"));
            await harness.ScreenshotAsync(deadline, "phone-only-ada");

            var fit = await harness.EvaluateAsync<JsonElement>(
                deadline, "measuring the panel",
                $"() => {{ const r = document.querySelector('{Panel}').getBoundingClientRect(); return {{ right: r.right, inner: window.innerWidth, scroll: document.documentElement.scrollWidth }}; }}");
            output.WriteLine(fit.ToString());
            Assert.True(fit.GetProperty("right").GetDouble() <= fit.GetProperty("inner").GetDouble(), $"the Filed by panel runs off the right edge: {fit}");
            Assert.True(fit.GetProperty("scroll").GetDouble() <= fit.GetProperty("inner").GetDouble(), $"the board scrolls sideways: {fit}");

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

    private static string CountIs(string text) =>
        $"() => document.querySelector('.hatch-filter-count')?.textContent.trim() === '{text}'";

    private static Task<int> CheckedCountAsync(E2eHarness harness, Deadline deadline) =>
        harness.EvaluateAsync<int>(
            deadline, "counting the ticked filers",
            "(sel) => [...document.querySelectorAll(sel)].filter(cb => cb.checked).length",
            Panel + " input[type=checkbox]");

    private static Task<int> TotalAsync(E2eHarness harness, Deadline deadline) =>
        harness.EvaluateAsync<int>(
            deadline, "counting the filer checkboxes",
            "(sel) => document.querySelectorAll(sel).length",
            Panel + " input[type=checkbox]");

    private static async Task<string> StartAsync(E2eHarness harness, Deadline deadline)
    {
        Assert.True(
            File.Exists(E2eHarness.BundleIndex()),
            $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

        await harness.CreateDatabasesAsync(deadline);
        var baseUrl = await harness.StartHostAsync(deadline);
        await deadline.BoundAsync("filing five issues", () => SeedAsync(baseUrl, harness.Services));
        return baseUrl;
    }

    /// <summary>
    /// A project and five top-level issues, filed as the local person and then handed to
    /// three names directly: with the wall off every issue is filed by the same caller,
    /// and the board needs more than one filer to have a list to choose from.
    /// </summary>
    private static async Task SeedAsync(string baseUrl, IServiceProvider services)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/hatch/projects", new("E2E", "E2E"));
        var filers = new[] { "Ada", "Ada", "Ada", "claude-key", LongName };
        var titles = new[] { "Story one", "Story two", "Task one", "Bug one", "Task two" };
        var types = new[] { "story", "story", "task", "bug", "task" };
        for (var i = 0; i < titles.Length; i++)
            await PostAsync<IssueCreateRequest, IssueDto>(
                http, "/api/hatch/issues", new(project.Id, types[i], titles[i], null, null, null, null));

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
        for (var i = 0; i < titles.Length; i++)
        {
            var title = titles[i];
            var name = filers[i];
            await db.Issues.Where(x => x.Title == title).ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedBy, name));
        }
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

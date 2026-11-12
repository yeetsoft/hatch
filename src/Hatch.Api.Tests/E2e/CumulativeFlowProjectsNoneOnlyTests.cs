using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-1846's acceptance, rendered for the Cumulative flow report: with two
/// projects on the board, the report's Projects control draws <c>None</c> as
/// the page's own empty sentence and writes an empty <c>project=</c> into the
/// address, and <c>Only</c> on one row draws the chart again and writes that
/// project alone into the address.
///
/// <para>The control's own panel sits earlier in the DOM than the top bar's
/// scope control, which keeps a closed panel of its own in the markup, so
/// every selector below is scoped to the report's own <c>Filters</c> group.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class CumulativeFlowProjectsNoneOnlyTests
{
    private const int CaseBudgetSeconds = 60;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly string[] Keys = ["CFA", "CFB"];

    /// <summary>Every selector below is scoped to the report's own Filters group, not the top bar's scope control, which keeps a closed panel of its own earlier in the DOM.</summary>
    private const string Filters = "[role=\"group\"][aria-label=\"Filters\"]";

    private const string Trigger = $"{Filters} button[aria-label=\"Projects\"]";
    private const string Panel = $"{Filters} .hatch-types-panel";
    private const string NoneButton = $"{Filters} .hatch-types-footer button:text-is(\"None\")";
    private const string OnlyButton = $"{Filters} .hatch-types-only";
    private const string TickedCount = $"() => document.querySelectorAll('{Filters} .hatch-types-item input:checked').length === 1";
    private const string TickedName = $"() => document.querySelector('{Filters} .hatch-types-item input:checked').closest('.hatch-types-item').querySelector('.hatch-types-name').textContent";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task CumulativeFlow_ProjectsControl_DrawsNoneThenOnly()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("CumulativeFlowProjectsNoneOnly");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            await deadline.BoundAsync("filing two issues", () => FileIssuesAsync(baseUrl));

            await harness.OpenAsync(deadline, baseUrl, "/reports/cumulative-flow");
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForVisibleAsync(deadline, Trigger);

            await harness.ClickAsync(deadline, Trigger);
            await harness.WaitForVisibleAsync(deadline, Panel);

            // None: the report's own empty sentence, and an empty project= in the address.
            await harness.ClickAsync(deadline, NoneButton);
            await harness.WaitForVisibleAsync(deadline, ".hatch-empty__message:text-is(\"No project is chosen.\")");
            Assert.True(
                await harness.EvaluateAsync<bool>(
                    deadline,
                    "reading the address after None",
                    "() => { const p = new URLSearchParams(location.search); return p.has('project') && p.get('project') === ''; }"),
                "None did not write an empty project= into the URL");

            // Only, on whichever row comes first: the chart draws again, and that project alone is in the address.
            await harness.ClickAsync(deadline, OnlyButton);
            await harness.WaitForFunctionAsync(deadline, "waiting for one project ticked", TickedCount);
            var text = await harness.EvaluateAsync<string>(deadline, "reading the ticked row", TickedName);
            var key = text.Split(" — ")[0];
            Assert.Contains(key, Keys);

            await harness.WaitForVisibleAsync(deadline, ".hatch-cumulative-flow");
            var urlProject = await harness.EvaluateAsync<string>(
                deadline,
                "reading the address after Only",
                "() => new URLSearchParams(location.search).getAll('project').join(',')");
            Assert.True(urlProject == key, "Only did not write the ticked project into the URL");

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

    /// <summary>Two projects, filed the way any caller files them with the wall off - the local person may file everything this test files - and a task under each, named for its own project so each row reads e.g. <c>CFA — CFA</c>.</summary>
    private static async Task FileIssuesAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        foreach (var key in Keys)
        {
            var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new(key, key));
            await PostAsync<IssueCreateRequest, IssueDto>(
                http, "/api/issues", new(project.Id, "task", $"A task in {key}", null, null, null, null));
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

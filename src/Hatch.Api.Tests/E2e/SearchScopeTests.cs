using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-1889's acceptance, rendered: with a scope of two projects out of three,
/// the Search page's Project facet lists only those two, its empty option
/// reads <c>All in scope</c>, choosing none draws the hits of both and choosing
/// one draws that project's alone, and a <c>?projectId=</c> the scope does not
/// offer is dropped from the address. With no scope the page reads
/// <c>All projects</c> and lists all three. Run at the desktop viewport in
/// Chromium and at the <c>iPhone 14 Pro</c> profile in WebKit.
///
/// <para>One task per project, so what the page fetched is what it drew: the
/// titles are the assert, and each look is saved as a PNG beside the case's
/// other artifacts.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class SearchScopeTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly string[] Keys = ["SEA", "SEB", "SEC"];

    private const string PickerSelector = "label.hatch-facet:has(.hatch-facet__label:text-is(\"Project\")) select";

    /// <summary>The facet's option values and empty label, and the hits' titles, read in one script so they are one moment.</summary>
    private const string ReadScript = """
        () => {
          const label = [...document.querySelectorAll('label.hatch-facet')]
            .find((l) => l.querySelector('.hatch-facet__label')?.textContent === 'Project');
          const select = label ? label.querySelector('select') : null;
          return {
            options: select ? [...select.options].map((o) => o.value) : null,
            empty: select ? select.options[0].textContent : null,
            hits: [...document.querySelectorAll('tbody tr td:nth-child(3)')].map((e) => e.textContent).sort(),
            search: location.search,
            scrollWidth: document.documentElement.scrollWidth,
            innerWidth: window.innerWidth,
          };
        }
        """;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task Search_FollowsTheScopeAtTheDesktopProfile() => RunAsync("SearchScopeDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task Search_FollowsTheScopeAtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("SearchScopePhone", "phone", DeviceProfile.IPhone14Pro);
    }

    private async Task RunAsync(string name, string profileName, DeviceProfile? profile)
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
            var ids = await deadline.BoundAsync("filing three tasks", () => FileTasksAsync(baseUrl));
            string[] scopedIds = [ids["SEA"], ids["SEB"]];

            // Scoped to two of three: the facet offers the two, and none chosen draws both.
            await harness.OpenAsync(deadline, baseUrl, "/apps/hatch/search?scope=SEA,SEB");
            var scoped = await ReadAsync(harness, deadline, "scoped", profileName, TitleOf("SEA"), TitleOf("SEB"));
            Assert.Equal(["", .. scopedIds], Strings(scoped, "options"));
            Assert.Equal("All in scope", scoped.GetProperty("empty").GetString());
            Assert.Equal([TitleOf("SEA"), TitleOf("SEB")], Strings(scoped, "hits"));
            AssertFits(scoped);

            // Choosing one draws that project alone.
            await harness.SelectOptionAsync(deadline, PickerSelector, ids["SEB"]);
            await harness.WaitForHiddenAsync(deadline, $"tbody td:text-is(\"{TitleOf("SEA")}\")");
            var picked = await ReadAsync(harness, deadline, "picked", profileName, TitleOf("SEB"));
            Assert.Equal([TitleOf("SEB")], Strings(picked, "hits"));

            // A project the scope does not offer is dropped from the address, and reads as none chosen.
            await harness.GoToAsync(deadline, $"search?scope=SEA,SEB&projectId={ids["SEC"]}");
            await harness.WaitForFunctionAsync(deadline, "waiting for the param to go", "() => !location.search.includes('projectId=')");
            var dropped = await ReadAsync(harness, deadline, "dropped", profileName, TitleOf("SEA"), TitleOf("SEB"));
            Assert.DoesNotContain("projectId", dropped.GetProperty("search").GetString());
            Assert.Equal([TitleOf("SEA"), TitleOf("SEB")], Strings(dropped, "hits"));

            // No scope: nothing changes.
            await harness.GoToAsync(deadline, "search");
            var unscoped = await ReadAsync(harness, deadline, "unscoped", profileName, TitleOf("SEA"), TitleOf("SEB"), TitleOf("SEC"));
            Assert.Equal(["", ids["SEA"], ids["SEB"], ids["SEC"]], Strings(unscoped, "options"));
            Assert.Equal("All projects", unscoped.GetProperty("empty").GetString());
            Assert.Equal([TitleOf("SEA"), TitleOf("SEB"), TitleOf("SEC")], Strings(unscoped, "hits"));

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

    private static string TitleOf(string key) => $"A task in {key}";

    private static string[] Strings(JsonElement read, string property) =>
        read.GetProperty(property).EnumerateArray().Select(e => e.GetString()!).ToArray();

    private void AssertFits(JsonElement read) =>
        Assert.True(
            read.GetProperty("scrollWidth").GetDouble() <= read.GetProperty("innerWidth").GetDouble() + 0.5,
            $"the page scrolls sideways: {read}");

    /// <summary>Waits for every title named to be drawn, then reads the page and saves the look.</summary>
    private async Task<JsonElement> ReadAsync(E2eHarness harness, Deadline deadline, string label, string profileName, params string[] titles)
    {
        foreach (var title in titles)
            await harness.WaitForVisibleAsync(deadline, $"tbody td:text-is(\"{title}\")");

        var read = await harness.EvaluateAsync<JsonElement>(deadline, $"reading the {label} search", ReadScript);
        output.WriteLine($"{profileName} {label}: {read}");
        output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"{label}-{profileName}")}");
        return read;
    }

    /// <summary>Three projects, filed the way any caller files them with the wall off - the local person may file everything this test files - and a task under each. Returns each project's id as the facet's option value, by key.</summary>
    private static async Task<Dictionary<string, string>> FileTasksAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var ids = new Dictionary<string, string>();
        foreach (var key in Keys)
        {
            var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/hatch/projects", new(key, key));
            await PostAsync<IssueCreateRequest, IssueDto>(
                http, "/api/hatch/issues", new(project.Id, "task", TitleOf(key), null, null, null, null));
            ids[key] = project.Id.ToString();
        }

        return ids;
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

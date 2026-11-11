using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-1888's acceptance, rendered: with a scope of two projects out of three,
/// the Plan page's picker lists only those two, its empty option reads
/// <c>All in scope</c>, picking none draws the epics of both and picking one
/// draws that project's alone, and a <c>?project=</c> the scope does not offer
/// is dropped from the address. With no scope the page reads <c>All projects</c>
/// and lists all three. Run at the desktop viewport in Chromium and at the
/// <c>iPhone 14 Pro</c> profile in WebKit.
///
/// <para>One epic per project, so what the page fetched is what it drew: the
/// epic titles are the assert, and each look is saved as a PNG beside the
/// case's other artifacts.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class PlanScopeTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly string[] Keys = ["PLA", "PLB", "PLC"];

    /// <summary>The picker's option values and empty label, and the epics drawn, read in one script so they are one moment.</summary>
    private const string ReadScript = """
        () => {
          const select = document.querySelector('.hatch-plan-picker select');
          return {
            options: select ? [...select.options].map((o) => o.value) : null,
            empty: select ? select.options[0].textContent : null,
            epics: [...document.querySelectorAll('.hatch-plan-title')].map((e) => e.textContent).sort(),
            search: location.search,
            scrollWidth: document.documentElement.scrollWidth,
            innerWidth: window.innerWidth,
          };
        }
        """;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task ThePlan_FollowsTheScopeAtTheDesktopProfile() => RunAsync("PlanScopeDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task ThePlan_FollowsTheScopeAtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("PlanScopePhone", "phone", DeviceProfile.IPhone14Pro);
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
            await deadline.BoundAsync("filing three epics", () => FileEpicsAsync(baseUrl));

            // Scoped to two of three: the picker offers the two, and none chosen draws both.
            await harness.OpenAsync(deadline, baseUrl, "/apps/hatch/plan?scope=PLA,PLB");
            var scoped = await ReadAsync(harness, deadline, "scoped", profileName, [EpicTitle("PLA"), EpicTitle("PLB")], [EpicTitle("PLC")]);
            Assert.Equal(["", "PLA", "PLB"], Strings(scoped, "options"));
            Assert.Equal("All in scope", scoped.GetProperty("empty").GetString());
            Assert.Equal([EpicTitle("PLA"), EpicTitle("PLB")], Strings(scoped, "epics"));
            AssertFits(scoped);

            // Picking one draws that project alone.
            await harness.SelectOptionAsync(deadline, ".hatch-plan-picker select", "PLB");
            await harness.WaitForHiddenAsync(deadline, $".hatch-plan-title:text-is(\"{EpicTitle("PLA")}\")");
            var picked = await ReadAsync(harness, deadline, "picked", profileName, [EpicTitle("PLB")], []);
            Assert.Equal([EpicTitle("PLB")], Strings(picked, "epics"));

            // A project the scope does not offer is dropped from the address, and reads as none chosen.
            await harness.GoToAsync(deadline, "plan?scope=PLA,PLB&project=PLC");
            await harness.WaitForFunctionAsync(deadline, "waiting for the param to go", "() => !location.search.includes('project=')");
            var dropped = await ReadAsync(harness, deadline, "dropped", profileName, [EpicTitle("PLA"), EpicTitle("PLB")], [EpicTitle("PLC")]);
            Assert.DoesNotContain("project", dropped.GetProperty("search").GetString());
            Assert.Equal([EpicTitle("PLA"), EpicTitle("PLB")], Strings(dropped, "epics"));

            // No scope: nothing changes.
            await harness.GoToAsync(deadline, "plan");
            var unscoped = await ReadAsync(harness, deadline, "unscoped", profileName, [EpicTitle("PLA"), EpicTitle("PLB"), EpicTitle("PLC")], []);
            Assert.Equal(["", "PLA", "PLB", "PLC"], Strings(unscoped, "options"));
            Assert.Equal("All projects", unscoped.GetProperty("empty").GetString());
            Assert.Equal([EpicTitle("PLA"), EpicTitle("PLB"), EpicTitle("PLC")], Strings(unscoped, "epics"));

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

    private static string EpicTitle(string key) => $"An epic in {key}";

    private static string[] Strings(JsonElement read, string property) =>
        read.GetProperty(property).EnumerateArray().Select(e => e.GetString()!).ToArray();

    private void AssertFits(JsonElement read) =>
        Assert.True(
            read.GetProperty("scrollWidth").GetDouble() <= read.GetProperty("innerWidth").GetDouble() + 0.5,
            $"the page scrolls sideways: {read}");

    /// <summary>
    /// Waits for every epic named to be drawn and every one in <paramref name="absent"/> to be gone,
    /// then reads the page and saves the look. The absent ones are waited on so the read is of the
    /// final state rather than a race with it.
    /// </summary>
    private async Task<JsonElement> ReadAsync(E2eHarness harness, Deadline deadline, string label, string profileName, string[] epics, string[] absent)
    {
        foreach (var epic in epics)
            await harness.WaitForVisibleAsync(deadline, $".hatch-plan-title:text-is(\"{epic}\")");

        foreach (var epic in absent)
            await harness.WaitForHiddenAsync(deadline, $".hatch-plan-title:text-is(\"{epic}\")");

        var read = await harness.EvaluateAsync<JsonElement>(deadline, $"reading the {label} plan", ReadScript);
        output.WriteLine($"{profileName} {label}: {read}");
        output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"{label}-{profileName}")}");
        return read;
    }

    /// <summary>Three projects, filed the way any caller files them with the wall off - the local person may file everything this test files - and an epic under each.</summary>
    private static async Task FileEpicsAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        foreach (var key in Keys)
        {
            var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/hatch/projects", new(key, key));
            await PostAsync<IssueCreateRequest, IssueDto>(
                http, "/api/hatch/issues", new(project.Id, "epic", EpicTitle(key), null, null, null, null));
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

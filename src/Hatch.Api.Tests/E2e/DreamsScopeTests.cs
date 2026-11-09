using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-1895's acceptance, rendered: with a scope of two projects out of three,
/// the Dreams list holds those projects' dreams and the loose one and not the
/// third project's, the Project facet, the new-dream Lean chooser and the
/// weave Project chooser list only the two (Lean keeping None), a
/// <c>?projectId=</c> the scope does not offer is dropped from the address, and
/// choosing one project in the facet narrows the list to that project's dreams
/// alone, as it does with no scope. With no scope the page lists all four dreams and all three projects.
/// Run at the desktop viewport in Chromium and at the <c>iPhone 14 Pro</c>
/// profile in WebKit.
///
/// <para>One dream per project and one with none, so what the page fetched is
/// what it drew: the dream bodies are the assert, and each look is saved as a
/// PNG beside the case's other artifacts.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class DreamsScopeTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly string[] Keys = ["DRA", "DRB", "DRC"];

    private const string LooseDream = "A loose dream";

    private const string PickerSelector = "label.hatch-facet:has(.hatch-facet__label:text-is(\"Project\")) select";

    /// <summary>The facet's option values, both choosers' tiles and the listed dreams, read in one script so they are one moment.</summary>
    private const string ReadScript = """
        () => {
          const label = [...document.querySelectorAll('label.hatch-facet')]
            .find((l) => l.querySelector('.hatch-facet__label')?.textContent === 'Project');
          const select = label ? label.querySelector('select') : null;
          const tiles = (root) => [...document.querySelectorAll(root + ' .hatch-project-chooser__tile')]
            .map((t) => t.lastElementChild.textContent);
          return {
            options: select ? [...select.options].map((o) => o.value) : null,
            lean: tiles('.hatch-dream-composer'),
            weave: tiles('details.hatch-dream-weave'),
            dreams: [...document.querySelectorAll('.hatch-dream')]
              .map((li) => (li.textContent.match(/A (?:dream in \w+|loose dream)/) ?? [''])[0]).sort(),
            search: location.search,
            scrollWidth: document.documentElement.scrollWidth,
            innerWidth: window.innerWidth,
          };
        }
        """;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task Dreams_FollowTheScopeAtTheDesktopProfile() => RunAsync("DreamsScopeDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task Dreams_FollowTheScopeAtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("DreamsScopePhone", "phone", DeviceProfile.IPhone14Pro);
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
            var ids = await deadline.BoundAsync("filing four dreams", () => FileDreamsAsync(baseUrl));
            string[] scopedIds = [ids["DRA"], ids["DRB"]];

            // Scoped to two of three: the list holds the two and the loose one, and each chooser offers the two.
            await harness.OpenAsync(deadline, baseUrl, "/apps/hatch/dreams?scope=DRA,DRB");
            await harness.WaitForVisibleAsync(deadline, ".hatch-dream-composer");
            await harness.ClickAsync(deadline, "details.hatch-dream-weave summary");
            var scoped = await ReadAsync(harness, deadline, "scoped", profileName, DreamOf("DRA"), DreamOf("DRB"), LooseDream);
            Assert.Equal(["", .. scopedIds], Strings(scoped, "options"));
            Assert.Equal(["None", "DRA DRA", "DRB DRB"], Strings(scoped, "lean"));
            Assert.Equal(["DRA DRA", "DRB DRB"], Strings(scoped, "weave"));
            Assert.Equal([DreamOf("DRA"), DreamOf("DRB"), LooseDream], Strings(scoped, "dreams"));
            AssertFits(scoped);

            // A project the scope does not offer is dropped from the address, and reads as none chosen.
            await harness.GoToAsync(deadline, $"dreams?scope=DRA,DRB&projectId={ids["DRC"]}");
            await harness.WaitForFunctionAsync(deadline, "waiting for the param to go", "() => !location.search.includes('projectId=')");
            var dropped = await ReadAsync(harness, deadline, "dropped", profileName, DreamOf("DRA"), DreamOf("DRB"), LooseDream);
            Assert.DoesNotContain("projectId", dropped.GetProperty("search").GetString());
            Assert.Equal([DreamOf("DRA"), DreamOf("DRB"), LooseDream], Strings(dropped, "dreams"));

            // Choosing one project: that project's dreams alone; the loose one is the scope's list's, not the project's.
            await harness.SelectOptionAsync(deadline, PickerSelector, ids["DRB"]);
            await harness.WaitForHiddenAsync(deadline, $".hatch-dream:has-text(\"{DreamOf("DRA")}\")");
            var picked = await ReadAsync(harness, deadline, "picked", profileName, DreamOf("DRB"));
            Assert.Equal([DreamOf("DRB")], Strings(picked, "dreams"));

            // No scope: nothing changes.
            await harness.GoToAsync(deadline, "dreams");
            await harness.ClickAsync(deadline, "details.hatch-dream-weave summary");
            var unscoped = await ReadAsync(harness, deadline, "unscoped", profileName, DreamOf("DRA"), DreamOf("DRB"), DreamOf("DRC"), LooseDream);
            Assert.Equal(["", ids["DRA"], ids["DRB"], ids["DRC"]], Strings(unscoped, "options"));
            Assert.Equal(["None", "DRA DRA", "DRB DRB", "DRC DRC"], Strings(unscoped, "lean"));
            Assert.Equal(["DRA DRA", "DRB DRB", "DRC DRC"], Strings(unscoped, "weave"));
            Assert.Equal([DreamOf("DRA"), DreamOf("DRB"), DreamOf("DRC"), LooseDream], Strings(unscoped, "dreams"));

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

    private static string DreamOf(string key) => $"A dream in {key}";

    private static string[] Strings(JsonElement read, string property) =>
        read.GetProperty(property).EnumerateArray().Select(e => e.GetString()!).ToArray();

    private void AssertFits(JsonElement read) =>
        Assert.True(
            read.GetProperty("scrollWidth").GetDouble() <= read.GetProperty("innerWidth").GetDouble() + 0.5,
            $"the page scrolls sideways: {read}");

    /// <summary>Waits for every dream named to be drawn, then reads the page and saves the look.</summary>
    private async Task<JsonElement> ReadAsync(E2eHarness harness, Deadline deadline, string label, string profileName, params string[] bodies)
    {
        foreach (var body in bodies)
            await harness.WaitForVisibleAsync(deadline, $".hatch-dream:has-text(\"{body}\")");

        var read = await harness.EvaluateAsync<JsonElement>(deadline, $"reading the {label} dreams", ReadScript);
        output.WriteLine($"{profileName} {label}: {read}");
        output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"{label}-{profileName}")}");
        return read;
    }

    /// <summary>Three projects, filed the way any caller files them with the wall off - the local person may file everything this test files - a dream leaning toward each, and one toward none. Returns each project's id as the facet's option value, by key.</summary>
    private static async Task<Dictionary<string, string>> FileDreamsAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var ids = new Dictionary<string, string>();
        foreach (var key in Keys)
        {
            var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/hatch/projects", new(key, key));
            await PostAsync<DreamCreateRequest, DreamDto>(http, "/api/hatch/dreams", new(DreamOf(key), null, project.Id));
            ids[key] = project.Id.ToString();
        }

        await PostAsync<DreamCreateRequest, DreamDto>(http, "/api/hatch/dreams", new(LooseDream, null, null));

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

using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-1895's acceptance, rendered for the one picker of HA-2396: with a scope
/// of two projects out of three, the page has one project control, listing the
/// two and the no-project tile, and the list holds those projects' dreams and
/// the loose one and not the third project's. A <c>?projectId=</c> the scope
/// does not offer is dropped from the address. Picking one project writes it
/// to the address and narrows the list to that project's dreams and the loose
/// one, and Weave, which has no chooser of its own, can be pressed. With no
/// scope and none picked, the page lists all four dreams, offers all three
/// projects, and Weave is disabled and says to pick one.
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

    private const string TileSelector = ".hatch-dream-project .hatch-project-chooser__tile";

    /// <summary>The picker's tiles, how many choosers and Project facets the page draws, the caption, Weave's state and hint, and the listed dreams, read in one script so they are one moment.</summary>
    private const string ReadScript = """
        () => {
          const weave = document.querySelector('.hatch-dream-weave__actions button');
          const hint = document.querySelector('.hatch-dream-weave__actions .text-muted');
          return {
            tiles: [...document.querySelectorAll('.hatch-dream-project .hatch-project-chooser__tile')]
              .map((t) => t.lastElementChild.textContent),
            choosers: document.querySelectorAll('.hatch-project-chooser').length,
            facets: [...document.querySelectorAll('label.hatch-facet')]
              .filter((l) => l.querySelector('.hatch-facet__label')?.textContent === 'Project').length,
            caption: document.querySelector('.hatch-dream-project__caption')?.textContent ?? null,
            weaveDisabled: weave ? weave.disabled : null,
            weaveHint: hint ? hint.textContent : null,
            dreams: [...document.querySelectorAll('.hatch-dream')]
              .map((li) => (li.textContent.match(/A (?:dream in \w+|loose dream)|Posted with (?:a project|none)/) ?? [''])[0]).sort(),
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

            // Scoped to two of three: the list holds the two and the loose one, and the one picker offers the two and no project.
            await harness.OpenAsync(deadline, baseUrl, "/dreams?scope=DRA,DRB");
            await harness.WaitForVisibleAsync(deadline, ".hatch-dream-composer");
            await harness.ClickAsync(deadline, "details.hatch-dream-weave summary");
            var scoped = await ReadAsync(harness, deadline, "scoped", profileName, DreamOf("DRA"), DreamOf("DRB"), LooseDream);
            Assert.Equal(["No project", "DRA DRA", "DRB DRB"], Strings(scoped, "tiles"));
            AssertOneControl(scoped);
            Assert.Contains("lean toward no project", scoped.GetProperty("caption").GetString());
            Assert.Equal([DreamOf("DRA"), DreamOf("DRB"), LooseDream], Strings(scoped, "dreams"));
            AssertFits(scoped);

            // A project the scope does not offer is dropped from the address, and reads as none picked.
            await harness.GoToAsync(deadline, $"dreams?scope=DRA,DRB&projectId={ids["DRC"]}");
            await harness.WaitForFunctionAsync(deadline, "waiting for the param to go", "() => !location.search.includes('projectId=')");
            var dropped = await ReadAsync(harness, deadline, "dropped", profileName, DreamOf("DRA"), DreamOf("DRB"), LooseDream);
            Assert.DoesNotContain("projectId", dropped.GetProperty("search").GetString());
            Assert.Equal([DreamOf("DRA"), DreamOf("DRB"), LooseDream], Strings(dropped, "dreams"));

            // Picking one project: that project's dreams and the loose one, never another project's.
            await harness.ClickAsync(deadline, $"{TileSelector}:has-text(\"DRB\")");
            await harness.WaitForHiddenAsync(deadline, $".hatch-dream:has-text(\"{DreamOf("DRA")}\")");
            await harness.ClickAsync(deadline, "details.hatch-dream-weave summary");
            await harness.SelectOptionAsync(deadline, "details.hatch-dream-weave select", "pressing");
            var picked = await ReadAsync(harness, deadline, "picked", profileName, DreamOf("DRB"), LooseDream);
            Assert.Equal([DreamOf("DRB"), LooseDream], Strings(picked, "dreams"));
            Assert.Contains("projectId=", picked.GetProperty("search").GetString());
            Assert.Contains("lean toward DRB", picked.GetProperty("caption").GetString());
            AssertOneControl(picked);
            Assert.False(picked.GetProperty("weaveDisabled").GetBoolean());
            Assert.Equal(JsonValueKind.Null, picked.GetProperty("weaveHint").ValueKind);

            // A post with DRB picked leans toward DRB and leaves the pick; one with none picked leans toward none.
            await PostFromComposerAsync(harness, deadline, profile is not null, PostedWithProject);
            await harness.WaitForVisibleAsync(deadline, $".hatch-dream:has-text(\"{PostedWithProject}\")");
            Assert.Contains("projectId=", await harness.EvaluateAsync<string>(deadline, "reading the address", "() => location.search"));
            Assert.Equal(ids["DRB"], await LeanOfAsync(baseUrl, PostedWithProject));

            await harness.ClickAsync(deadline, $"{TileSelector}:has-text(\"No project\")");
            await harness.WaitForVisibleAsync(deadline, $".hatch-dream:has-text(\"{DreamOf("DRA")}\")");
            await PostFromComposerAsync(harness, deadline, profile is not null, PostedWithNone);
            await harness.WaitForVisibleAsync(deadline, $".hatch-dream:has-text(\"{PostedWithNone}\")");
            Assert.Null(await LeanOfAsync(baseUrl, PostedWithNone));

            // No scope, none picked: nothing is narrowed, and Weave says to pick a project.
            await harness.GoToAsync(deadline, "dreams");
            await harness.ClickAsync(deadline, "details.hatch-dream-weave summary");
            await harness.SelectOptionAsync(deadline, "details.hatch-dream-weave select", "pressing");
            var unscoped = await ReadAsync(harness, deadline, "unscoped", profileName, DreamOf("DRA"), DreamOf("DRB"), DreamOf("DRC"), LooseDream, PostedWithProject, PostedWithNone);
            Assert.Equal(["No project", "DRA DRA", "DRB DRB", "DRC DRC"], Strings(unscoped, "tiles"));
            AssertOneControl(unscoped);
            Assert.Equal([DreamOf("DRA"), DreamOf("DRB"), DreamOf("DRC"), LooseDream, PostedWithProject, PostedWithNone], Strings(unscoped, "dreams"));
            Assert.True(unscoped.GetProperty("weaveDisabled").GetBoolean());
            Assert.Equal("Pick a project above to weave.", unscoped.GetProperty("weaveHint").GetString());

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

    private const string PostedWithProject = "Posted with a project";

    private const string PostedWithNone = "Posted with none";

    /// <summary>
    /// Types into the composer's box and presses Post. A touch profile has a coarse pointer, which keeps
    /// the box a plain textarea, so there is no Monaco to wait for; the desktop waits for Monaco to take it over.
    /// </summary>
    private static async Task PostFromComposerAsync(E2eHarness harness, Deadline deadline, bool touch, string body)
    {
        if (touch)
        {
            await harness.ClickAsync(deadline, ".hatch-dream-composer__box textarea");
        }
        else
        {
            await harness.ClickAsync(deadline, ".hatch-dream-composer__box");
            await harness.WaitForVisibleAsync(deadline, ".hatch-dream-composer__box .monaco-editor");
            await harness.ClickAsync(deadline, ".hatch-dream-composer__box .monaco-editor");
        }

        await harness.InsertTextAsync(deadline, body);
        await harness.ClickAsync(deadline, ".hatch-dream-composer__actions button:text-is(\"Post\")");
    }

    /// <summary>The project id a stored dream leans toward, as the picker's string, or null for none.</summary>
    private static async Task<string?> LeanOfAsync(string baseUrl, string body)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var list = await http.GetFromJsonAsync<JsonElement>("/api/dreams?limit=50", Web);
        var dream = list.GetProperty("dreams").EnumerateArray().Single(d => d.GetProperty("body").GetString() == body);
        var lean = dream.GetProperty("projectId");
        return lean.ValueKind == JsonValueKind.Null ? null : lean.GetInt32().ToString();
    }

    private static string DreamOf(string key) => $"A dream in {key}";

    private static string[] Strings(JsonElement read, string property) =>
        read.GetProperty(property).EnumerateArray().Select(e => e.GetString()!).ToArray();

    private static void AssertOneControl(JsonElement read)
    {
        Assert.Equal(1, read.GetProperty("choosers").GetInt32());
        Assert.Equal(0, read.GetProperty("facets").GetInt32());
    }

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

    /// <summary>Three projects, filed the way any caller files them with the wall off - the local person may file everything this test files - a dream leaning toward each, and one toward none. Returns each project's id, by key.</summary>
    private static async Task<Dictionary<string, string>> FileDreamsAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var ids = new Dictionary<string, string>();
        foreach (var key in Keys)
        {
            var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new(key, key));
            await PostAsync<DreamCreateRequest, DreamDto>(http, "/api/dreams", new(DreamOf(key), null, project.Id));
            ids[key] = project.Id.ToString();
        }

        await PostAsync<DreamCreateRequest, DreamDto>(http, "/api/dreams", new(LooseDream, null, null));

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

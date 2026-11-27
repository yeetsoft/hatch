using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2841's acceptance, rendered: a person opening an epic that hangs beneath
/// an ambition sees a Serves section with a tick box for each of the ambition's
/// capabilities, ticks and unticks them, presses Save, and sees the saved set
/// with no reload - and the ambition's Capabilities section and the Ambitions
/// page show the change. Run at the desktop viewport in Chromium and at the
/// <c>iPhone 14 Pro</c> profile in WebKit, where nothing may scroll sideways.
///
/// <para>One ambition with three capabilities, one epic under it serving the
/// first, and a second epic under that one, serving nothing, to prove the
/// section is drawn at any depth. The wall is off, so the page reads as the
/// local person. Every row is filed over plain HTTP, the way any caller files
/// it, and each look is saved as a PNG beside the case's other artifacts.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class EpicServesPageTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 120;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    /// <summary>Sub-pixel layout is fractional; two boxes that touch are not overlapping.</summary>
    private const double Tolerance = 0.5;

    /// <summary>A section is a <c>details</c> whose summary is its title; the section id is a storage key, not an element id.</summary>
    private const string Capabilities = ".hatch-issue-section:has(> summary:text-matches(\"^Capabilities\"))";
    private const string Serves = ".hatch-issue-section:has(> summary:text-matches(\"^Serves\"))";
    private const string Boxes = Serves + " input[type=checkbox]";
    private const string Save = Serves + " .hatch-section-actions button";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly string[] Statements =
    [
        "Partners can place an order without a phone call",
        "An order is confirmed within a minute",
        "Staff are told of every new order",
    ];

    /// <summary>The Serves section as drawn - each tick box's state, its row's text, the Save button's state and any refusal - and the page's width against the viewport's, read in one script so they are one moment.</summary>
    private const string MeasureScript = """
        () => {
          const section = [...document.querySelectorAll('details.hatch-issue-section')]
            .find((d) => d.querySelector(':scope > summary')?.textContent.startsWith('Serves'));
          const save = section?.querySelector('.hatch-section-actions button');
          return {
            drawn: !!section,
            boxes: section ? [...section.querySelectorAll('input[type=checkbox]')].map((b) => b.checked) : [],
            rows: section ? [...section.querySelectorAll('li')].map((li) => li.textContent ?? '') : [],
            saveDisabled: save ? save.disabled : null,
            text: section ? section.textContent : null,
            innerWidth: window.innerWidth,
            scrollWidth: document.documentElement.scrollWidth,
          };
        }
        """;

    /// <summary>The ticks in the Serves section, as a string of 1s and 0s, and whether Save is disabled - what the page waits on after a press.</summary>
    private const string SettledScript = """
        (want) => {
          const section = [...document.querySelectorAll('details.hatch-issue-section')]
            .find((d) => d.querySelector(':scope > summary')?.textContent.startsWith('Serves'));
          if (!section) return false;
          const ticks = [...section.querySelectorAll('input[type=checkbox]')].map((b) => (b.checked ? '1' : '0')).join('');
          const save = section.querySelector('.hatch-section-actions button');
          return ticks === want && !!save && save.disabled && ![...section.querySelectorAll('input')].some((b) => b.disabled);
        }
        """;

    /// <summary>The Capabilities section's rows, each with its number and the epics that serve it, and the page's width.</summary>
    private const string CapabilityRowsScript = """
        () => {
          const section = [...document.querySelectorAll('details.hatch-issue-section')]
            .find((d) => d.querySelector(':scope > summary')?.textContent.startsWith('Capabilities'));
          return {
            rows: section
              ? [...section.querySelectorAll('.hatch-capability-row')].map((row) => ({
                  text: row.textContent ?? '',
                  servedBy: row.querySelector('.hatch-capability-served-by')?.textContent ?? '',
                }))
              : [],
            innerWidth: window.innerWidth,
            scrollWidth: document.documentElement.scrollWidth,
          };
        }
        """;

    /// <summary>The Ambitions page's rows, found by title, each with its legend, and the page's width.</summary>
    private const string AmbitionRowsScript = """
        () => ({
          rows: [...document.querySelectorAll('.hatch-ambition-row')].map((row) => ({
            title: row.querySelector('.hatch-ambition-title')?.textContent?.trim() ?? '',
            legend: [...row.querySelectorAll('.hatch-ambition-legend li')].map((e) => (e.textContent ?? '').trim()),
          })),
          innerWidth: window.innerWidth,
          scrollWidth: document.documentElement.scrollWidth,
        })
        """;

    private sealed record Filed(string Ambition, string AmbitionTitle, string Epic, string Child);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheSection_SavesAtTheDesktopProfile() => RunAsync("EpicServesDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheSection_SavesAtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("EpicServesPhone", "phone", DeviceProfile.IPhone14Pro);
    }

    private async Task RunAsync(string name, string profileName, DeviceProfile? profile)
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var phone = profile is not null;
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
            var filed = await deadline.BoundAsync("filing the ambition", () => FileAsync(baseUrl));

            // The epic: three tick boxes, the first ticked.
            await harness.OpenAsync(deadline, baseUrl, $"/issues/{filed.Epic}");
            await harness.WaitForVisibleAsync(deadline, ".hatch-issue-page");
            await harness.WaitForVisibleAsync(deadline, Serves);
            await harness.WaitForVisibleAsync(deadline, Boxes);

            var before = await MeasureAsync(harness, deadline, "measuring the Serves section before a save");
            output.WriteLine($"{profileName} /issues/{filed.Epic}: {before}");
            await ScrollToAsync(harness, deadline, "Serves");
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"epic-before-{profileName}")}");

            Assert.Equal([true, false, false], Ticks(before));
            for (var i = 0; i < Statements.Length; i++)
                Assert.Contains(Statements[i], Rows(before)[i]);
            Assert.Contains($"#{1}", Rows(before)[0]);
            Assert.True(before.GetProperty("saveDisabled").GetBoolean(), "Save is enabled with nothing changed");
            AssertFits(before, phone);

            // Tick #2, untick #1: Save wakes, and the section settles on the saved set with no reload.
            await harness.ClickAsync(deadline, $"{Serves} li:nth-child(2) input[type=checkbox]");
            await harness.ClickAsync(deadline, $"{Serves} li:nth-child(1) input[type=checkbox]");
            var changed = await MeasureAsync(harness, deadline, "measuring the Serves section with a change");
            Assert.Equal([false, true, false], Ticks(changed));
            Assert.False(changed.GetProperty("saveDisabled").GetBoolean(), "Save is disabled with a change made");
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"epic-changed-{profileName}")}");

            await harness.ClickAsync(deadline, Save);
            await harness.WaitForFunctionAsync(deadline, "waiting for the saved set to show", SettledScript, "010");

            var after = await MeasureAsync(harness, deadline, "measuring the Serves section after a save");
            output.WriteLine($"{profileName} /issues/{filed.Epic} after Save: {after}");
            await ScrollToAsync(harness, deadline, "Serves");
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"epic-after-{profileName}")}");
            Assert.Equal([false, true, false], Ticks(after));
            Assert.True(after.GetProperty("saveDisabled").GetBoolean(), "Save is enabled after a save");
            AssertFits(after, phone);

            // The ambition: the Capabilities section names the epic on #2 and not on #1.
            await harness.GoToAsync(deadline, $"issues/{filed.Ambition}");
            await harness.WaitForVisibleAsync(deadline, ".hatch-issue-page");
            await harness.WaitForVisibleAsync(deadline, $"{Capabilities} .hatch-capability-row");
            var ambition = await harness.EvaluateAsync<JsonElement>(deadline, "reading the Capabilities section", CapabilityRowsScript);
            output.WriteLine($"{profileName} /issues/{filed.Ambition}: {ambition}");
            await ScrollToAsync(harness, deadline, "Capabilities");
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"ambition-{profileName}")}");

            var capabilityRows = ambition.GetProperty("rows").EnumerateArray().ToList();
            Assert.Equal(3, capabilityRows.Count);
            Assert.DoesNotContain(filed.Epic, capabilityRows[0].GetProperty("servedBy").GetString()!);
            Assert.Contains(filed.Epic, capabilityRows[1].GetProperty("servedBy").GetString()!);
            AssertFits(ambition, phone);

            // The Ambitions page: the epic is open, so #2 is in flight and the others are not started.
            await harness.GoToAsync(deadline, "ambitions");
            await harness.WaitForVisibleAsync(deadline, ".hatch-ambition-row");
            var page = await harness.EvaluateAsync<JsonElement>(deadline, "reading the Ambitions page", AmbitionRowsScript);
            output.WriteLine($"{profileName} /ambitions: {page}");
            output.WriteLine($"saved {await harness.FullPageScreenshotAsync(deadline, $"ambitions-{profileName}")}");

            var row = page.GetProperty("rows").EnumerateArray()
                .Single(r => r.GetProperty("title").GetString() == filed.AmbitionTitle);
            Assert.Equal(
                ["1 in flight", "2 not started"],
                row.GetProperty("legend").EnumerateArray().Select(e => e.GetString()!).ToArray());
            AssertFits(page, phone);

            // Depth two: an epic under the first epic draws the section too, with nothing ticked.
            await harness.GoToAsync(deadline, $"issues/{filed.Child}");
            await harness.WaitForVisibleAsync(deadline, ".hatch-issue-page");
            await harness.WaitForVisibleAsync(deadline, Boxes);
            var child = await MeasureAsync(harness, deadline, "measuring the Serves section on the sub-epic");
            output.WriteLine($"{profileName} /issues/{filed.Child}: {child}");
            await ScrollToAsync(harness, deadline, "Serves");
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"child-{profileName}")}");
            Assert.Equal([false, false, false], Ticks(child));
            Assert.True(child.GetProperty("saveDisabled").GetBoolean(), "Save is enabled on the sub-epic with nothing changed");
            AssertFits(child, phone);

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

    private static Task<JsonElement> MeasureAsync(E2eHarness harness, Deadline deadline, string phase) =>
        harness.EvaluateAsync<JsonElement>(deadline, phase, MeasureScript);

    private static bool[] Ticks(JsonElement measured) =>
        measured.GetProperty("boxes").EnumerateArray().Select(b => b.GetBoolean()).ToArray();

    private static string[] Rows(JsonElement measured) =>
        measured.GetProperty("rows").EnumerateArray().Select(r => r.GetString()!).ToArray();

    /// <summary>Brings the titled section to the top of the viewport, so the screenshot that follows shows what was asserted on rather than the page's header.</summary>
    private static Task ScrollToAsync(E2eHarness harness, Deadline deadline, string title) =>
        harness.EvaluateAsync<bool>(deadline, $"scrolling to {title}", """
            (title) => {
              const section = [...document.querySelectorAll('details.hatch-issue-section')]
                .find((d) => d.querySelector(':scope > summary')?.textContent.startsWith(title));
              section?.scrollIntoView({ block: 'start' });
              return !!section;
            }
            """, title);

    /// <summary>The page may not scroll sideways on the phone; on the desktop the same check costs nothing, so it is made there too.</summary>
    private static void AssertFits(JsonElement measured, bool phone)
    {
        var innerWidth = measured.GetProperty("innerWidth").GetDouble();
        Assert.True(
            measured.GetProperty("scrollWidth").GetDouble() <= innerWidth + Tolerance,
            $"the page scrolls sideways{(phone ? " on the phone" : "")}: {measured}");
    }

    /// <summary>
    /// A project, an ambition with three capabilities, an epic under it serving
    /// the first, and a second epic under that epic, serving nothing.
    /// </summary>
    private static async Task<Filed> FileAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("SRV", "SRV"));
        const string title = "Open the ordering to partners";
        var ambition = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "ambition", title, null, null, null, null));

        var ids = new List<long>();
        foreach (var statement in Statements)
        {
            var capability = await PostAsync<CapabilityCreateRequest, CapabilityDto>(
                http, $"/api/issues/{ambition.Key}/capabilities", new(statement, "Try it and see"));
            ids.Add(capability.Id);
        }

        var epic = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "epic", "A partner order form", null, ambition.Key, null, null));

        using var put = await http.PutAsJsonAsync($"/api/issues/{epic.Key}/serves", new IssueServesWriteRequest([ids[0]]), Web);
        var putText = await put.Content.ReadAsStringAsync();
        Assert.True(put.IsSuccessStatusCode, $"PUT /api/issues/{epic.Key}/serves answered {(int)put.StatusCode}: {putText}");

        var child = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "epic", "Order form validation", null, epic.Key, null, null));

        return new Filed(ambition.Key, title, epic.Key, child.Key);
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

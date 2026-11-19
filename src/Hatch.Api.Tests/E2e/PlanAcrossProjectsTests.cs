using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2470's acceptance, rendered: an epic whose parent is an epic of another
/// project is drawn once, inside that parent, with its project key beside its
/// own; picking the child's project draws it as a root with a line saying what
/// it is part of; and neither look scrolls sideways. Run at the desktop
/// viewport in Chromium and at the <c>iPhone 14 Pro</c> profile in WebKit, and
/// again in Chromium for a machine where WebKit does not open a page.
///
/// <para>The lines for a hidden parent or child cannot be reached here: the
/// host runs with the wall off, so the reader's scope is unbounded and nothing
/// is hidden from them. Those are pinned by <c>PlanTests</c> and by
/// <c>PlanPage.test.tsx</c>.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class PlanAcrossProjectsTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private const string ParentTitle = "The programme in the first project";
    private const string ChildTitle = "The half that lives in the second project";

    /// <summary>Every epic row, with where it is drawn, its chip, and the lines under its head - read in one script so they are one moment.</summary>
    private const string ReadScript = """
        () => ({
          rows: [...document.querySelectorAll('.hatch-plan-row')].map((row) => ({
            title: row.querySelector(':scope > .hatch-plan-head .hatch-plan-title').textContent,
            top: row.classList.contains('top'),
            nested: row.parentElement.classList.contains('hatch-plan-nested'),
            chip: row.querySelector(':scope > .hatch-plan-head .hatch-plan-project')?.textContent ?? null,
            lines: [...row.children].map((c) => c.textContent).filter((t) => t.startsWith('Part of')),
          })),
          cards: document.querySelectorAll('.hatch-plan-row.top').length,
          scrollWidth: document.documentElement.scrollWidth,
          innerWidth: window.innerWidth,
        })
        """;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task ATreeAcrossProjects_IsDrawnAtTheDesktopProfile() => RunAsync("PlanAcrossDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task ATreeAcrossProjects_IsDrawnAtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("PlanAcrossPhone", "phone", DeviceProfile.IPhone14Pro);
    }

    /// <summary>The same phone, emulated in Chromium: the case that still runs where WebKit does not open a page.</summary>
    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task ATreeAcrossProjects_IsDrawnAtThePhoneProfileInChromium() =>
        RunAsync("PlanAcrossPhoneChromium", "phone-chromium", DeviceProfile.IPhone14ProChromium);

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
            var parentKey = await deadline.BoundAsync("filing the tree", () => FileTreeAsync(baseUrl));

            // No narrowing: one card, the child nested inside it and wearing its project.
            await harness.OpenAsync(deadline, baseUrl, "/plan");
            var all = await ReadAsync(harness, deadline, "all", profileName, [ParentTitle, ChildTitle]);
            Assert.Equal(1, all.GetProperty("cards").GetInt32());

            var rows = all.GetProperty("rows").EnumerateArray().ToList();
            Assert.Equal(2, rows.Count);
            var parent = rows.Single(r => r.GetProperty("title").GetString() == ParentTitle);
            var child = rows.Single(r => r.GetProperty("title").GetString() == ChildTitle);
            Assert.True(parent.GetProperty("top").GetBoolean());
            Assert.Equal(JsonValueKind.Null, parent.GetProperty("chip").ValueKind);
            Assert.True(child.GetProperty("nested").GetBoolean());
            Assert.Equal("PXB", child.GetProperty("chip").GetString());
            AssertFits(all);

            // Narrowed to the child's project: it is a root, and says what it is part of.
            await harness.SelectOptionAsync(deadline, ".hatch-plan-picker select", "PXB");
            await harness.WaitForHiddenAsync(deadline, $".hatch-plan-title:text-is(\"{ParentTitle}\")");
            var picked = await ReadAsync(harness, deadline, "picked", profileName, [ChildTitle]);

            var only = Assert.Single(picked.GetProperty("rows").EnumerateArray());
            Assert.True(only.GetProperty("top").GetBoolean());
            Assert.False(only.GetProperty("nested").GetBoolean());
            Assert.Equal(JsonValueKind.Null, only.GetProperty("chip").ValueKind);
            Assert.Equal([$"Part of {parentKey}"], only.GetProperty("lines").EnumerateArray().Select(l => l.GetString()!).ToArray());
            AssertFits(picked);

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

    private void AssertFits(JsonElement read) =>
        Assert.True(
            read.GetProperty("scrollWidth").GetDouble() <= read.GetProperty("innerWidth").GetDouble() + 0.5,
            $"the page scrolls sideways: {read}");

    /// <summary>Waits for each epic named to be drawn, then reads the page and saves the look.</summary>
    private async Task<JsonElement> ReadAsync(E2eHarness harness, Deadline deadline, string label, string profileName, string[] epics)
    {
        foreach (var epic in epics)
            await harness.WaitForVisibleAsync(deadline, $".hatch-plan-title:text-is(\"{epic}\")");

        var read = await harness.EvaluateAsync<JsonElement>(deadline, $"reading the {label} plan", ReadScript);
        output.WriteLine($"{profileName} {label}: {read}");
        output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"{label}-{profileName}")}");
        return read;
    }

    /// <summary>
    /// Two projects, an epic in the first with a story under it, and an epic in
    /// the second parented to it - filed the way any caller files them with the
    /// wall off. Answers the first epic's key.
    /// </summary>
    private static async Task<string> FileTreeAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var first = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("PXA", "PXA"));
        var second = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("PXB", "PXB"));

        var parent = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(first.Id, "epic", ParentTitle, null, null, null, null));
        await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(first.Id, "story", "A story beside it", null, parent.Key, null, null));

        var child = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(second.Id, "epic", ChildTitle, null, parent.Key, null, null));
        await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(second.Id, "story", "A story under the child", null, child.Key, null, null));

        return parent.Key;
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

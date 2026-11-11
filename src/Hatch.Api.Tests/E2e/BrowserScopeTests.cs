using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-1835's acceptance, rendered across the surfaces a person moves between: a
/// tab opened with <c>?scope=BCA</c> draws only that project's card on the
/// board and names the scope in its control, the scope rides the client-side
/// move to Plan (where a one-project scope has nothing to pick, so no picker is
/// drawn), and, on a desktop, the brand link - a real address, so a full page
/// load - lands back on the board still scoped. A bare address then reads
/// <c>All projects</c> and draws both cards. Run at the desktop viewport in
/// Chromium (all four steps) and at the <c>iPhone 14 Pro</c> profile in WebKit
/// (the first two, reaching Plan through the phone menu, since a phone has no
/// primary nav).
///
/// <para>One card per project, so what the page fetched is what it drew: the
/// card titles are the assert, and each look is saved as a PNG beside the
/// case's other artifacts.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class BrowserScopeTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly string[] Keys = ["BCA", "BCB"];

    /// <summary>The scope control as a phone draws it, in the strip under the bar.</summary>
    private const string PhoneScopedControl = ".hatch-scope-strip .hatch-scope-control--scoped";

    /// <summary>The scope control as a desktop draws it, in the bar.</summary>
    private const string DesktopScopedControl = ".hatch-scope-control--scoped";

    /// <summary>The control with no scope: <c>Menu</c> puts the label on its trigger in both states.</summary>
    private const string UnscopedControl = "button[aria-label=\"Projects in scope\"]";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheScope_FollowsTheTabAcrossTheBoardPlanAndTheLogo_AtTheDesktopProfile() =>
        RunAsync("BrowserScopeDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheScope_FollowsTheTabAcrossTheBoardAndPlan_AtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("BrowserScopePhone", "phone", DeviceProfile.IPhone14Pro);
    }

    private async Task RunAsync(string name, string profileName, DeviceProfile? profile)
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness(name);
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        var desktop = profile is null;
        var scopedControl = desktop ? DesktopScopedControl : PhoneScopedControl;
        try
        {
            await harness.LaunchBrowserAsync(deadline, profile);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            await deadline.BoundAsync("filing two cards", () => FileCardsAsync(baseUrl));

            // 1. A tab opened scoped to one project draws that project's card alone.
            await harness.OpenAsync(deadline, baseUrl, "/?scope=BCA");
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForFirstColumnAsync(deadline);
            await AssertScopedBoardAsync(harness, deadline, "scoped board", scopedControl);
            harness.AssertNoPageErrors();
            await Shot(harness, deadline, $"1-board-scoped-{profileName}");

            // 2. The move to Plan is client-side, and the scope goes with it.
            if (desktop)
            {
                await harness.ClickAsync(deadline, ".hatch-primary-nav button:text-is(\"Track\")");
                await harness.ClickAsync(deadline, ".hatch-primary-nav .hatch-menu__item:text-is(\"Plan\")");
            }
            else
            {
                await harness.ClickAsync(deadline, E2eHarness.GearSelector);
                await harness.ClickAsync(deadline, ".hatch-phone-menu a:text-is(\"Plan\")");
            }

            await harness.WaitForVisibleAsync(deadline, "h1:text-is(\"Plan\")");
            var plan = await harness.EvaluateAsync<JsonElement>(deadline, "reading the scoped plan", """
                () => ({
                  search: location.search,
                  pickers: document.querySelectorAll('.hatch-plan-picker').length,
                })
                """);
            output.WriteLine($"{profileName} plan: {plan}");
            Assert.Contains("scope=BCA", plan.GetProperty("search").GetString());
            Assert.Equal(0, plan.GetProperty("pickers").GetInt32());
            harness.AssertNoPageErrors();
            await Shot(harness, deadline, $"2-plan-scoped-{profileName}");

            if (!desktop) return;

            // 3. The brand link is a full page load, and it keeps the scope.
            await harness.ClickAsync(deadline, E2eHarness.BrandSelector);
            await harness.WaitForFirstColumnAsync(deadline);
            await AssertScopedBoardAsync(harness, deadline, "board after the logo", scopedControl);
            harness.AssertNoPageErrors();
            await Shot(harness, deadline, $"3-board-after-logo-{profileName}");

            // 4. No scope: nothing changes.
            await harness.GoToAsync(deadline, "");
            await harness.WaitForVisibleAsync(deadline, CardTitle("BCA"));
            await harness.WaitForVisibleAsync(deadline, CardTitle("BCB"));
            var unscoped = await ReadBoardAsync(harness, deadline, "unscoped board", UnscopedControl);
            Assert.Equal([Title("BCA"), Title("BCB")], Titles(unscoped));
            Assert.Equal("All projects", unscoped.GetProperty("control").GetString());
            harness.AssertNoPageErrors();
            await Shot(harness, deadline, $"4-board-unscoped-{profileName}");
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

    private static string Title(string key) => $"A card in {key}";

    private static string CardTitle(string key) => $".hatch-card-title:text-is(\"{Title(key)}\")";

    private static string[] Titles(JsonElement read) =>
        read.GetProperty("titles").EnumerateArray().Select(e => e.GetString()!).ToArray();

    /// <summary>The scope is BCA: its card is drawn, BCB's is not drawn at all, and the control names BCA alone.</summary>
    private async Task AssertScopedBoardAsync(E2eHarness harness, Deadline deadline, string label, string controlSelector)
    {
        await harness.WaitForVisibleAsync(deadline, CardTitle("BCA"));
        var read = await ReadBoardAsync(harness, deadline, label, controlSelector);
        Assert.Equal([Title("BCA")], Titles(read));
        Assert.Contains("scope=BCA", read.GetProperty("search").GetString());

        var control = read.GetProperty("control").GetString();
        Assert.Contains("BCA", control);
        Assert.DoesNotContain("BCB", control);
    }

    /// <summary>The card titles drawn, the address's query and the scope control's text, read in one script so they are one moment.</summary>
    private async Task<JsonElement> ReadBoardAsync(E2eHarness harness, Deadline deadline, string label, string controlSelector)
    {
        var read = await harness.EvaluateAsync<JsonElement>(deadline, $"reading the {label}", """
            (controlSelector) => ({
              titles: [...document.querySelectorAll('.hatch-card-title')].map((e) => e.textContent).sort(),
              search: location.search,
              control: document.querySelector(controlSelector)?.textContent ?? null,
            })
            """, controlSelector);
        output.WriteLine($"{label}: {read}");
        return read;
    }

    private async Task Shot(E2eHarness harness, Deadline deadline, string label) =>
        output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, label)}");

    /// <summary>Two projects, filed the way any caller files them with the wall off - the local person may file everything this test files - and a task in each.</summary>
    private static async Task FileCardsAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        foreach (var key in Keys)
        {
            var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/hatch/projects", new(key, key));
            await PostAsync<IssueCreateRequest, IssueDto>(
                http, "/api/hatch/issues", new(project.Id, "task", Title(key), null, null, null, null));
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

using System.Net.Http.Json;
using System.Text.Json;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2331: the issue page's status bar draws Status, Priority and Pre-approval
/// as three pills of one height, their words 16px at weight 600. The Status pill
/// once fell back to the bar's 14px body text because the peek's
/// <c>font: inherit</c> reset outranked the large pill's own size, which left it
/// 37px tall beside two pills 45px tall. jsdom cannot measure, so this reads the
/// boxes in a real browser.
///
/// <para>Two issues in two columns at different priority and pre-approval levels
/// are measured, so the heights are held for whatever is showing and not for one
/// colour. The read-only words are held by drawing one inside the bar, since the
/// wall-off caller this host runs as is a person and is never shown them.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class IssuePillSizesTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    /// <summary>Sub-pixel layout is fractional; two pills within half a pixel of each other are the same height.</summary>
    private const double Tolerance = 0.5;

    /// <summary>The Status pill's height before the change, and the Priority and Pre-approval pills'. The three now sit between them.</summary>
    private const double WasStatus = 37;

    private const double WasLevels = 45;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly string[] Pills =
    [
        ".hatch-status-bar .hatch-status-pill-lg",
        ".hatch-status-bar .hatch-priority-pill-lg",
        ".hatch-status-bar .hatch-preapproval-pill-lg",
    ];

    /// <summary>Runs in the page: each pill's box height, font size and font weight, in <see cref="Pills"/> order, then the two read-only words as drawn in the bar.</summary>
    private const string MeasureScript = """
        (selectors) => {
          const read = (el) => {
            const style = getComputedStyle(el);
            return { height: el.getBoundingClientRect().height, size: style.fontSize, weight: style.fontWeight };
          };
          const pills = selectors.map((s) => read(document.querySelector(s)));
          const bar = document.querySelector('.hatch-status-bar');
          const said = ['hatch-priority-said', 'hatch-preapproval-said'].map((c) => {
            const el = document.createElement('span');
            el.className = c;
            el.textContent = 'said';
            bar.appendChild(el);
            const r = read(el);
            el.remove();
            return r;
          });
          return { pills, said };
        }
        """;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task ThePills_AreOneHeightAtSixteenPixels_AtDesktop()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        await RunAsync(null, "IssuePillSizes");
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task ThePills_AreOneHeightAtSixteenPixels_AtThePhoneProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);

        await RunAsync(DeviceProfile.IPhone14Pro, "IssuePillSizesPhone");
    }

    private async Task RunAsync(DeviceProfile? profile, string name)
    {
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
            var keys = await deadline.BoundAsync("filing the issues", () => SeedAsync(baseUrl, harness.Services));

            await harness.OpenAsync(deadline, baseUrl, $"/issues/{keys[0]}");
            foreach (var key in keys)
            {
                await harness.GoToAsync(deadline, $"/issues/{key}");
                foreach (var pill in Pills) await harness.WaitForVisibleAsync(deadline, pill);

                var measured = await harness.EvaluateAsync<JsonElement>(deadline, $"measuring {key}'s pills", MeasureScript, Pills);
                output.WriteLine($"{key}: {measured}");
                await harness.ScreenshotAsync(deadline, $"{name}-{key}");
                AssertPills(key, measured);
            }

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

    private static void AssertPills(string key, JsonElement measured)
    {
        var detail = $"{key}: {measured}";
        var pills = measured.GetProperty("pills").EnumerateArray().ToArray();
        Assert.Equal(Pills.Length, pills.Length);

        var heights = pills.Select(p => p.GetProperty("height").GetDouble()).ToArray();
        Assert.True(heights.Max() - heights.Min() <= Tolerance, $"the pills are not one height ({string.Join(", ", heights)}): {detail}");
        Assert.All(heights, h => Assert.InRange(h, WasStatus + 1, WasLevels - 1));

        foreach (var drawn in pills.Concat(measured.GetProperty("said").EnumerateArray()))
        {
            Assert.True(drawn.GetProperty("size").GetString() == "16px", $"a pill's words are not 16px: {detail}");
            Assert.True(drawn.GetProperty("weight").GetString() == "600", $"a pill's words are not weight 600: {detail}");
        }
    }

    /// <summary>
    /// Two issues, in two columns, at different levels: one at the defaults and one at
    /// emergency and express. Returns their keys.
    /// </summary>
    private static async Task<string[]> SeedAsync(string baseUrl, IServiceProvider services)
    {
        int doing;
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
            var todo = new EfHatchStatus { Name = "todo", SortOrder = 10, Color = "#e8eaed" };
            var other = new EfHatchStatus { Name = "doing", SortOrder = 20, Color = "#1d4ed8" };
            db.Statuses.AddRange(todo, other);
            await db.SaveChangesAsync();
            doing = other.Id;
        }

        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("PIL", "Pills"));
        var plain = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "task", "Plain", null, null, null, null));
        var loud = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "task", "Loud", null, null, null, null,
                Priority: PriorityLevels.EmergencyName, PreApproval: PreApprovalLevels.ExpressName));
        await PostAsync<IssueMoveRequest, IssueDto>(http, $"/api/issues/{loud.Key}/move", new(doing, null, null));

        return [plain.Key, loud.Key];
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

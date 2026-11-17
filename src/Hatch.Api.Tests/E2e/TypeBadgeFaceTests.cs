using System.Net.Http.Json;
using System.Text.Json;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2381: the type badge wears the same face as the type pill, for all five types. One
/// issue of each type is filed; the issue page's Details card is read in both modes (the
/// badge outside edit mode, the pill inside it) and the two painted colours must match.
/// The search page is read for a badge of every type. Screenshots are left in the case's
/// artifact directory.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class TypeBadgeFaceTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 120;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private static readonly string[] Types = ["epic", "reflection", "story", "task", "bug"];

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>Runs in the page: the painted colours of the first element a selector finds.</summary>
    private const string PaintScript = """
        (selector) => {
          const el = document.querySelector(selector);
          if (!el) return null;
          const style = getComputedStyle(el);
          return { word: el.textContent.trim(), color: style.color, background: style.backgroundColor };
        }
        """;

    /// <summary>Runs in the page: every type badge on the page, as a word and its painted colours.</summary>
    private const string BadgesScript = """
        () => [...document.querySelectorAll('.hatch-type-badge')].map((el) => {
          const style = getComputedStyle(el);
          return { word: el.textContent.trim(), color: style.color, background: style.backgroundColor };
        })
        """;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheBadge_MatchesThePill_AtDesktop()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        await RunAsync(null, "TypeBadgeFace");
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheBadge_MatchesThePill_AtThePhoneProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);

        await RunAsync(DeviceProfile.IPhone14Pro, "TypeBadgeFacePhone");
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
            var keys = await deadline.BoundAsync("filing one issue of each type", () => SeedAsync(baseUrl, harness.Services));

            var seen = new Dictionary<string, string>();
            foreach (var type in Types)
            {
                await harness.OpenAsync(deadline, baseUrl, $"/issues/{keys[type]}");
                var page = harness.Page!;
                await harness.WaitForVisibleAsync(deadline, ".hatch-status-bar");
                await harness.WaitForVisibleAsync(deadline, ".hatch-type-badge");

                var badge = await harness.EvaluateAsync<JsonElement>(deadline, $"reading the {type} badge", PaintScript, ".hatch-type-badge");
                await harness.ScreenshotAsync(deadline, $"{name}-{type}-read");

                await deadline.BoundAsync("pressing Edit", () => page.GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true }).First.ClickAsync());
                await harness.WaitForVisibleAsync(deadline, ".hatch-type-pill-press");
                var pill = await harness.EvaluateAsync<JsonElement>(deadline, $"reading the {type} pill", PaintScript, ".hatch-type-pill-press");
                await harness.ScreenshotAsync(deadline, $"{name}-{type}-edit");
                output.WriteLine($"{type}: badge {badge}; pill {pill}");

                Assert.Equal(type, badge.GetProperty("word").GetString());
                Assert.Equal(type, pill.GetProperty("word").GetString());
                Assert.Equal(pill.GetProperty("background").GetString(), badge.GetProperty("background").GetString());
                Assert.Equal(pill.GetProperty("color").GetString(), badge.GetProperty("color").GetString());
                seen[type] = badge.GetProperty("background").GetString()!;
            }

            // Five types, five faces.
            Assert.Equal(Types.Length, seen.Values.Distinct().Count());

            await harness.OpenAsync(deadline, baseUrl, "/search");
            await harness.WaitForFunctionAsync(
                deadline,
                "waiting for a badge of every type on the search page",
                "() => document.querySelectorAll('.hatch-type-badge').length >= 5");
            var badges = await harness.EvaluateAsync<JsonElement>(deadline, "reading the search badges", BadgesScript);
            await harness.ScreenshotAsync(deadline, $"{name}-search");
            output.WriteLine(badges.ToString());
            foreach (var type in Types)
            {
                var row = badges.EnumerateArray().First(b => b.GetProperty("word").GetString() == type);
                Assert.Equal(seen[type], row.GetProperty("background").GetString());
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

    private static async Task<Dictionary<string, string>> SeedAsync(string baseUrl, IServiceProvider services)
    {
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
            db.Statuses.Add(new EfHatchStatus { Name = "todo", SortOrder = 10, Color = "#e8eaed" });
            await db.SaveChangesAsync();
        }

        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("TBF", "Badge faces"));
        var keys = new Dictionary<string, string>();
        foreach (var type in Types)
        {
            var issue = await PostAsync<IssueCreateRequest, IssueDto>(
                http, "/api/issues", new(project.Id, type, $"A {type} to look at", null, null, null, null));
            keys[type] = issue.Key;
        }

        return keys;
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

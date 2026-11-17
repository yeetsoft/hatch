using System.Net.Http.Json;
using System.Text.Json;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2419: the local-model pill on the issue page, rendered and driven against
/// the real host and the built bundle. An epic is set to prefer and a story under
/// it is left alone, so the epic's pill reads Prefer as its own and the story's
/// reads Prefer as inherited. Pressing the story's pill opens three steps, and
/// pressing Require makes the real PUT and repaints the pill and the Details
/// card; the board then marks both cards.
///
/// <para>Run on both profiles. The wall-off caller this host serves is a person,
/// so the key-holder's read-only word is held by the unit suite.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class LocalModelTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Pill = ".hatch-status-bar .hatch-localmodel-pill-lg";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task ThePill_ShowsInheritsAndSaves_AtDesktop()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        await RunAsync(null, "LocalModel");
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task ThePill_ShowsInheritsAndSaves_AtThePhoneProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);

        await RunAsync(DeviceProfile.IPhone14Pro, "LocalModelPhone");
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
            var (epic, story) = await deadline.BoundAsync("filing the issues", () => SeedAsync(baseUrl, harness.Services));

            // 1. The epic wears its own level.
            await harness.OpenAsync(deadline, baseUrl, $"/issues/{epic}");
            await harness.WaitForVisibleAsync(deadline, Pill);
            Assert.Equal($"Local model: Prefer. Set {epic}'s local model", await LabelAsync(harness, deadline));
            await harness.ScreenshotAsync(deadline, $"{name}-1-epic");

            // 2. The story reads it inherited, in the pill and on the Details card.
            await harness.GoToAsync(deadline, $"/issues/{story}");
            await harness.WaitForVisibleAsync(deadline, Pill);
            Assert.Equal($"Local model: Prefer, inherited from {epic}. Set {story}'s local model", await LabelAsync(harness, deadline));
            Assert.Contains($"Prefer (inherited from {epic})", await DetailsAsync(harness, deadline));
            await harness.ScreenshotAsync(deadline, $"{name}-2-story");

            // 3. Opened, the band is three steps, the own level (none) marked and disabled, the none step inheriting.
            await harness.ClickAsync(deadline, Pill);
            await harness.WaitForVisibleAsync(deadline, ".hatch-localmodel-band");
            Assert.Equal(3, await harness.EvaluateAsync<int>(deadline, "counting steps", "() => document.querySelectorAll('.hatch-localmodel-step').length"));
            Assert.Equal(1, await harness.EvaluateAsync<int>(deadline, "counting disabled steps", "() => document.querySelectorAll('.hatch-localmodel-step:disabled').length"));
            var none = await harness.EvaluateAsync<string>(deadline, "reading the none step", "() => document.querySelector('.hatch-localmodel-step.here').textContent");
            Assert.Contains($"None (inherits Prefer from {epic})", none);
            await harness.ScreenshotAsync(deadline, $"{name}-3-band");

            // 4. Pressing Require saves and repaints: the pill, the Details card, and the band folded.
            await harness.ClickAsync(deadline, ".hatch-localmodel-step.require");
            await harness.WaitForFunctionAsync(
                deadline,
                "waiting for the pill to read Require",
                "(want) => document.querySelector(want.selector)?.getAttribute('aria-label') === want.label",
                new { selector = Pill, label = $"Local model: Require. Set {story}'s local model" });
            await harness.WaitForHiddenAsync(deadline, ".hatch-localmodel-band");
            Assert.Matches(@"Local model\s+Require\s*(\n|$)", await DetailsAsync(harness, deadline));
            await harness.ScreenshotAsync(deadline, $"{name}-4-saved");

            // 5. The board marks both cards, at their own levels.
            await harness.GoToAsync(deadline, "/");
            await harness.WaitForFirstColumnAsync(deadline);
            await harness.WaitForVisibleAsync(deadline, ".hatch-card-localmodel.require");
            var marks = await harness.EvaluateAsync<string[]>(
                deadline,
                "reading the card marks",
                "() => [...document.querySelectorAll('.hatch-card-localmodel')].map((el) => el.className + ' | ' + el.title)");
            output.WriteLine(string.Join("\n", marks));
            Assert.Contains("hatch-card-localmodel prefer | Local model: prefer", marks);
            Assert.Contains("hatch-card-localmodel require | Local model: require", marks);
            await harness.ScreenshotAsync(deadline, $"{name}-5-board");

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

    private static Task<string> LabelAsync(E2eHarness harness, Deadline deadline) =>
        harness.EvaluateAsync<string>(deadline, "reading the pill", "(selector) => document.querySelector(selector).getAttribute('aria-label')", Pill);

    private static Task<string> DetailsAsync(E2eHarness harness, Deadline deadline) =>
        harness.EvaluateAsync<string>(
            deadline,
            "reading the Details card",
            "() => [...document.querySelectorAll('.hatch-card')].find((c) => c.querySelector('h2.hatch-section-title')?.textContent === 'Details').innerText");

    /// <summary>An epic at prefer and a story under it at none, with a column so the board draws them. Returns their keys.</summary>
    private static async Task<(string Epic, string Story)> SeedAsync(string baseUrl, IServiceProvider services)
    {
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
            db.Statuses.Add(new EfHatchStatus { Name = "todo", SortOrder = 10, Color = "#e8eaed" });
            await db.SaveChangesAsync();
        }

        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("LMD", "Local model"));
        var epic = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "epic", "Epic", null, null, null, null));
        var story = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "story", "Story", null, epic.Key, null, null));

        using var response = await http.PutAsJsonAsync($"/api/issues/{epic.Key}/local-model", new LocalModelRequest(LocalModelLevels.PreferName), Web);
        Assert.True(response.IsSuccessStatusCode, $"PUT local-model answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        return (epic.Key, story.Key);
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

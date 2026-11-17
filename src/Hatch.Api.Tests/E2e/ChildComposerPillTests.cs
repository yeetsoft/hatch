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
/// HA-2380: the Progress card's child composer, rendered and driven against the real host
/// and the built bundle. Under an epic the pill opens a band of all five types; under a
/// story, task and bug only. Picking bug, typing a title and pressing File it clears the
/// title box and leaves the pill wearing bug. A screenshot with the band open is left in
/// the case's artifact directory, with the band's place in the card's row written to the
/// test output.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class ChildComposerPillTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Composer = ".hatch-child-composer";
    private const string Pill = ".hatch-child-composer .hatch-type-pill-press";
    private const string Band = ".hatch-child-composer .hatch-type-pill-band";
    private const string Title = ".hatch-child-composer-title input";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>Runs in the page: the band's steps, the pill's word, and where the band, the pill and the Title box sit.</summary>
    private const string ReadScript = """
        () => {
          const box = (el) => { const r = el.getBoundingClientRect(); return { top: r.top, bottom: r.bottom, left: r.left, right: r.right, width: r.width }; };
          const composer = document.querySelector('.hatch-child-composer');
          const pill = composer.querySelector('.hatch-type-pill-press');
          const band = composer.querySelector('.hatch-type-pill-band');
          const title = composer.querySelector('.hatch-child-composer-title input');
          return {
            steps: [...composer.querySelectorAll('.hatch-type-pill-step')].map((el) => el.textContent.trim()),
            pill: pill.textContent.trim(),
            pillLabel: pill.getAttribute('aria-label'),
            pillBox: box(pill),
            bandBox: band ? box(band) : null,
            titleBox: box(title),
            composerBox: box(composer),
            viewport: document.documentElement.clientWidth,
            overflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
          };
        }
        """;

    /// <summary>Runs in the page: brings the composer into view and lets the band's fade-in finish, so a screenshot shows it as a person sees it.</summary>
    private const string SettleScript = """
        async () => {
          document.querySelector('.hatch-child-composer').scrollIntoView({ block: 'center' });
          await Promise.all(document.getAnimations().map((a) => a.finished.catch(() => {})));
        }
        """;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheComposerPill_OffersWhatTheParentMayTake_AtDesktop()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        await RunAsync(null, "ChildComposerPill");
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheComposerPill_OffersWhatTheParentMayTake_AtThePhoneProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);

        await RunAsync(DeviceProfile.IPhone14Pro, "ChildComposerPillPhone");
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
            var (epic, story, task) = await deadline.BoundAsync("filing the issues", () => SeedAsync(baseUrl, harness.Services));

            // 1. Under an epic the pill opens a band of all five.
            await harness.OpenAsync(deadline, baseUrl, $"/issues/{epic}");
            var page = harness.Page!;
            await harness.WaitForVisibleAsync(deadline, Pill);
            Assert.False(await harness.IsVisibleAsync(deadline, Band), "the band is open before the pill is pressed");
            Assert.Equal(0, await page.Locator($"{Composer} select").CountAsync());

            await harness.ClickAsync(deadline, Pill);
            await harness.WaitForVisibleAsync(deadline, Band);
            // Settles the band's fade-in before reading its box - read any
            // earlier and the geometry is still mid translateY(-4px), which
            // reads as the band sitting a few px short of the pill's own row.
            await harness.EvaluateAsync<JsonElement?>(deadline, "settling the band", SettleScript);
            var read = await harness.EvaluateAsync<JsonElement>(deadline, "reading the band", ReadScript);
            output.WriteLine(read.ToString());
            await harness.ScreenshotAsync(deadline, $"{name}-epic-open");
            Assert.Equal(["epic", "reflection", "story", "task", "bug"], Steps(read));
            Assert.Equal("epic", read.GetProperty("pill").GetString());
            Assert.Equal("Type: epic. Type of the new child", read.GetProperty("pillLabel").GetString());
            Assert.Equal(0, read.GetProperty("overflow").GetInt32());
            AssertBandIsOnItsOwnRow(read);

            // 2. Picking bug folds the band; the title is typed and filed.
            await deadline.BoundAsync("pressing bug", () => page.Locator(Band).GetByRole(AriaRole.Button, new() { Name = "bug", Exact = true }).ClickAsync());
            await harness.WaitForHiddenAsync(deadline, Band);
            await harness.WaitForFunctionAsync(
                deadline,
                "waiting for the pill to wear bug",
                $"() => document.querySelector('{Pill}')?.textContent.trim() === 'bug'");

            await deadline.BoundAsync("typing the title", () => page.Locator(Title).FillAsync("Filed through the pill"));
            await deadline.BoundAsync("pressing File it", () => page.Locator(Composer).GetByRole(AriaRole.Button, new() { Name = "File it", Exact = true }).ClickAsync());
            await harness.WaitForFunctionAsync(
                deadline,
                "waiting for the title box to clear",
                $"() => document.querySelector('{Title}')?.value === ''");

            // Filing clears the title and keeps the type.
            Assert.Equal("bug", await page.Locator(Pill).InnerTextAsync());

            using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
            var filed = await http.GetFromJsonAsync<IssueDto[]>($"/api/issues?parentKey={epic}", Web);
            var child = Assert.Single(filed!);
            Assert.Equal("bug", child.Type);
            Assert.Equal("Filed through the pill", child.Title);

            // 3. Under a story the band offers task and bug only.
            await harness.GoToAsync(deadline, $"/issues/{story}");
            await harness.WaitForVisibleAsync(deadline, Pill);
            Assert.Equal("task", (await page.Locator(Pill).InnerTextAsync()).Trim());
            await harness.ClickAsync(deadline, Pill);
            await harness.WaitForVisibleAsync(deadline, Band);
            await harness.EvaluateAsync<JsonElement?>(deadline, "settling the band", SettleScript);
            var underStory = await harness.EvaluateAsync<JsonElement>(deadline, "reading the band under a story", ReadScript);
            output.WriteLine(underStory.ToString());
            await harness.ScreenshotAsync(deadline, $"{name}-story-open");
            Assert.Equal(["task", "bug"], Steps(underStory));
            Assert.Equal(0, underStory.GetProperty("overflow").GetInt32());
            AssertBandIsOnItsOwnRow(underStory);

            // 4. A task draws no composer at all.
            await harness.GoToAsync(deadline, $"/issues/{task}");
            await harness.WaitForVisibleAsync(deadline, ".hatch-status-bar");
            Assert.Equal(0, await page.Locator(Composer).CountAsync());

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

    private static string[] Steps(JsonElement read) =>
        read.GetProperty("steps").EnumerateArray().Select(s => s.GetString()!).ToArray();

    /// <summary>The open band sits under the pill, inside the viewport, and leaves the Title box a usable width.</summary>
    private static void AssertBandIsOnItsOwnRow(JsonElement read)
    {
        var pill = read.GetProperty("pillBox");
        var band = read.GetProperty("bandBox");
        var title = read.GetProperty("titleBox");
        var viewport = read.GetProperty("viewport").GetDouble();

        Assert.True(band.GetProperty("top").GetDouble() >= pill.GetProperty("bottom").GetDouble() - 1, $"the band is not under the pill: {read}");
        Assert.True(band.GetProperty("right").GetDouble() <= viewport + 1, $"the band runs past the viewport: {read}");
        Assert.True(title.GetProperty("width").GetDouble() >= 150, $"the Title box is squashed: {read}");
    }

    private static async Task<(string Epic, string Story, string Task)> SeedAsync(string baseUrl, IServiceProvider services)
    {
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
            db.Statuses.Add(new EfHatchStatus { Name = "todo", SortOrder = 10, Color = "#e8eaed" });
            await db.SaveChangesAsync();
        }

        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("CMP", "Composer"));
        var epic = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "epic", "File under me", null, null, null, null));
        var story = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "story", "A story", null, null, null, null));
        var task = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "task", "A task", null, null, null, null));
        return (epic.Key, story.Key, task.Key);
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

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
/// HA-2378: the issue page's Type pill, rendered and driven against the real host and
/// the built bundle. Pressing Edit on Details and then the pill opens a band of five
/// steps; the current type's step is marked and cannot be pressed; Escape folds the
/// band and leaves the card in edit mode; pressing another step saves that type and
/// folds the band. A screenshot with the band open is left in the case's artifact
/// directory, and the pill's and each step's colours are written to the test output.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class TypePillTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    // Scoped to the Details card's edit controls: the Progress card's own
    // composer (HA-2380) draws its own .hatch-type-pill-press whenever its
    // parent takes a child, unscoped selectors here would match whichever of
    // the two the document happens to list first.
    private const string Band = ".hatch-issue-controls .hatch-type-pill-band";
    private const string Pill = ".hatch-issue-controls .hatch-type-pill-press";
    private const string Step = ".hatch-issue-controls .hatch-type-pill-step";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>Runs in the page: each step's word, state, glyph tile and painted colours, then the pill's word.</summary>
    private const string ReadScript = """
        () => {
          const steps = [...document.querySelectorAll('.hatch-issue-controls .hatch-type-pill-step')].map((el) => {
            const style = getComputedStyle(el);
            return {
              word: el.textContent.trim(),
              pressed: el.getAttribute('aria-pressed'),
              disabled: el.disabled,
              hasTile: el.querySelector('svg .hatch-type-tile-face') !== null,
              color: style.color,
              background: style.backgroundColor,
              border: style.borderTopColor,
            };
          });
          const pill = document.querySelector('.hatch-issue-controls .hatch-type-pill-press');
          const style = getComputedStyle(pill);
          return {
            steps,
            pill: pill.textContent.trim(),
            pillColor: style.color,
            pillBackground: style.backgroundColor,
            overflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
          };
        }
        """;

    /// <summary>Runs in the page: brings the Details card's band into view and lets its fade-in finish, so a screenshot shows it as a person sees it.</summary>
    private const string SettleScript = """
        async () => {
          document.querySelector('.hatch-issue-controls .hatch-type-pill-press').scrollIntoView({ block: 'center' });
          await Promise.all(document.getAnimations().map((a) => a.finished.catch(() => {})));
        }
        """;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task ThePill_OpensItsBandAndSavesAType_AtDesktop()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        await RunAsync(null, "TypePill");
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task ThePill_OpensItsBandAndSavesAType_AtThePhoneProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);

        await RunAsync(DeviceProfile.IPhone14Pro, "TypePillPhone");
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
            var key = await deadline.BoundAsync("filing the story", () => SeedAsync(baseUrl, harness.Services));

            await harness.OpenAsync(deadline, baseUrl, $"/issues/{key}");
            var page = harness.Page!;
            await harness.WaitForVisibleAsync(deadline, ".hatch-status-bar");

            // Outside edit mode there is no pill.
            Assert.False(await harness.IsVisibleAsync(deadline, Pill), "the pill is drawn before Edit is pressed");

            // 1. Edit, then the pill, opens the band of five.
            await deadline.BoundAsync("pressing Edit", () => page.GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true }).First.ClickAsync());
            await harness.WaitForVisibleAsync(deadline, Pill);
            Assert.False(await harness.IsVisibleAsync(deadline, Band), "the band is open before the pill is pressed");
            await harness.ClickAsync(deadline, Pill);
            await harness.WaitForVisibleAsync(deadline, Band);

            var read = await harness.EvaluateAsync<JsonElement>(deadline, "reading the band", ReadScript);
            output.WriteLine(read.ToString());
            await harness.EvaluateAsync<JsonElement?>(deadline, "settling the band", SettleScript);
            await harness.ScreenshotAsync(deadline, $"{name}-open");
            AssertBand(read, current: "story");
            Assert.Equal(0, read.GetProperty("overflow").GetInt32());

            // 4. Escape folds the band and nothing else.
            await deadline.BoundAsync("pressing Escape", () => page.Keyboard.PressAsync("Escape"));
            await harness.WaitForHiddenAsync(deadline, Band);
            Assert.True(await harness.IsVisibleAsync(deadline, Pill), "Escape left edit mode");
            Assert.True(await harness.IsVisibleAsync(deadline, ".hatch-issue-controls"), "Escape left edit mode");

            // 3. Another step saves that type and folds the band.
            await harness.ClickAsync(deadline, Pill);
            await harness.WaitForVisibleAsync(deadline, Band);
            await deadline.BoundAsync("pressing task", () => page.Locator(Band).GetByRole(AriaRole.Button, new() { Name = "task", Exact = true }).ClickAsync());
            await harness.WaitForHiddenAsync(deadline, Band);
            await harness.WaitForFunctionAsync(
                deadline,
                "waiting for the pill to wear task",
                "() => document.querySelector('.hatch-issue-controls .hatch-type-pill-press')?.textContent.trim() === 'task'");

            await harness.ClickAsync(deadline, Pill);
            await harness.WaitForVisibleAsync(deadline, Band);
            var after = await harness.EvaluateAsync<JsonElement>(deadline, "reading the band again", ReadScript);
            output.WriteLine(after.ToString());
            await harness.EvaluateAsync<JsonElement?>(deadline, "settling the band", SettleScript);
            await harness.ScreenshotAsync(deadline, $"{name}-task");
            AssertBand(after, current: "task");

            using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
            var issue = await http.GetFromJsonAsync<IssueDto>($"/api/issues/{key}", Web);
            Assert.Equal("task", issue!.Type);

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

    private static void AssertBand(JsonElement read, string current)
    {
        var steps = read.GetProperty("steps").EnumerateArray().ToArray();
        Assert.Equal(["epic", "reflection", "story", "task", "bug"], steps.Select(s => s.GetProperty("word").GetString()).ToArray());
        Assert.All(steps, s => Assert.True(s.GetProperty("hasTile").GetBoolean(), $"a step has no tile: {read}"));

        var marked = steps.Where(s => s.GetProperty("pressed").GetString() == "true").ToArray();
        var one = Assert.Single(marked);
        Assert.Equal(current, one.GetProperty("word").GetString());
        Assert.True(one.GetProperty("disabled").GetBoolean(), $"the current step can be pressed: {read}");
        Assert.All(steps.Except(marked), s => Assert.False(s.GetProperty("disabled").GetBoolean()));

        Assert.Equal(current, read.GetProperty("pill").GetString());
    }

    private static async Task<string> SeedAsync(string baseUrl, IServiceProvider services)
    {
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
            db.Statuses.Add(new EfHatchStatus { Name = "todo", SortOrder = 10, Color = "#e8eaed" });
            await db.SaveChangesAsync();
        }

        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("TYP", "Types"));
        var story = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "story", "Pick a type", null, null, null, null));
        return story.Key;
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

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
/// HA-2400: the Dreams page's mode pill, rendered and driven against the real host and
/// the built bundle, in place of the native Mode dropdown. Pressing the pill opens a
/// band of four steps, each naming its mode and its hint as text; the current mode's
/// step is marked and cannot be pressed, and the pill's own hint sits under it at all
/// times. Escape folds the band with no change; picking a step folds it, swaps the
/// pill and the hint, and shows or hides the Seed box. Pressing Weave afterwards files
/// the epic with the picked mode, which the server's own description echoes back. A
/// screenshot with the band open is left in the case's artifact directory, at the
/// desktop and the iPhone 14 Pro profile.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class WeaveModePillTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Band = ".hatch-weave-mode-pill-band";
    private const string Pill = ".hatch-weave-mode-pill-press";
    private const string SeedBox = ".hatch-dream-weave__seed";
    private const string WeaveButton = ".hatch-dream-weave__actions button";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>Runs in the page: each step's name, hint, state, then the pill's word and the hint under it, and whether the Seed box is drawn.</summary>
    private const string ReadScript = """
        () => {
          const steps = [...document.querySelectorAll('.hatch-weave-mode-step')].map((el) => ({
            name: el.querySelector('.hatch-weave-mode-step__name').textContent.trim(),
            hint: el.querySelector('.hatch-weave-mode-step__hint').textContent.trim(),
            pressed: el.getAttribute('aria-pressed'),
            disabled: el.disabled,
          }));
          const pill = document.querySelector('.hatch-weave-mode-pill-press');
          const hint = document.querySelector('.hatch-dream-weave__mode-hint');
          return {
            steps,
            pill: pill.textContent.trim(),
            hint: hint ? hint.textContent.trim() : null,
            seedVisible: document.querySelector('.hatch-dream-weave__seed') !== null,
          };
        }
        """;

    /// <summary>Runs in the page: brings the pill into view and lets its fade-in finish, so a screenshot shows it as a person sees it.</summary>
    private const string SettleScript = """
        async () => {
          document.querySelector('.hatch-weave-mode-pill-press').scrollIntoView({ block: 'center' });
          await Promise.all(document.getAnimations().map((a) => a.finished.catch(() => {})));
        }
        """;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task ThePill_OpensItsBandAndFilesWithThePickedMode_AtDesktop()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        await RunAsync(null, "WeaveModePill");
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task ThePill_OpensItsBandAndFilesWithThePickedMode_AtThePhoneProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);

        await RunAsync(DeviceProfile.IPhone14Pro, "WeaveModePillPhone");
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
            var projectKey = await deadline.BoundAsync("filing a project with a home for its epics", () => SeedAsync(baseUrl, harness.Services));

            await harness.OpenAsync(deadline, baseUrl, "/dreams");
            var page = harness.Page!;
            await harness.WaitForVisibleAsync(deadline, Pill);
            Assert.False(await harness.IsVisibleAsync(deadline, Band), "the band is open before the pill is pressed");

            // 1. The pill, pressed, opens a band of the four modes, Seed marked, and the Seed box is on the page.
            await harness.ClickAsync(deadline, Pill);
            await harness.WaitForVisibleAsync(deadline, Band);

            var read = await harness.EvaluateAsync<JsonElement>(deadline, "reading the band", ReadScript);
            output.WriteLine(read.ToString());
            await harness.EvaluateAsync<JsonElement?>(deadline, "settling the band", SettleScript);
            await harness.ScreenshotAsync(deadline, $"{name}-open");
            AssertBand(read, current: "Seed");
            Assert.True(read.GetProperty("seedVisible").GetBoolean(), "the Seed box is missing in mode seed");

            // 2. Escape folds the band and changes nothing.
            await deadline.BoundAsync("pressing Escape", () => page.Keyboard.PressAsync("Escape"));
            await harness.WaitForHiddenAsync(deadline, Band);
            var afterEscape = await harness.EvaluateAsync<JsonElement>(deadline, "reading after Escape", ReadScript);
            Assert.Equal("Seed", afterEscape.GetProperty("pill").GetString());
            Assert.True(afterEscape.GetProperty("seedVisible").GetBoolean(), "Escape changed the mode");
            Assert.True(await harness.IsVisibleAsync(deadline, Pill), "Escape left the pill");

            // 3. Picking Next step folds the band, swaps the pill and the hint under it, and hides the Seed box.
            await harness.ClickAsync(deadline, Pill);
            await harness.WaitForVisibleAsync(deadline, Band);
            await deadline.BoundAsync(
                "pressing Next step",
                () => page.Locator(Band).Locator(".hatch-weave-mode-step:has-text(\"Next step\")").ClickAsync());
            await harness.WaitForHiddenAsync(deadline, Band);
            await harness.WaitForFunctionAsync(
                deadline,
                "waiting for the pill to wear Next step",
                "() => document.querySelector('.hatch-weave-mode-pill-press')?.textContent.trim() === 'Next step'");

            var afterPick = await harness.EvaluateAsync<JsonElement>(deadline, "reading after the pick", ReadScript);
            output.WriteLine(afterPick.ToString());
            Assert.False(afterPick.GetProperty("seedVisible").GetBoolean(), "the Seed box is still drawn outside mode seed");
            Assert.Equal(
                "The nearest step toward the vision, given what has already shipped.",
                afterPick.GetProperty("hint").GetString());

            await harness.ClickAsync(deadline, Pill);
            await harness.WaitForVisibleAsync(deadline, Band);
            var reopened = await harness.EvaluateAsync<JsonElement>(deadline, "reading the band again", ReadScript);
            output.WriteLine(reopened.ToString());
            await harness.EvaluateAsync<JsonElement?>(deadline, "settling the band", SettleScript);
            await harness.ScreenshotAsync(deadline, $"{name}-next");
            AssertBand(reopened, current: "Next step");
            await deadline.BoundAsync("pressing Escape", () => page.Keyboard.PressAsync("Escape"));
            await harness.WaitForHiddenAsync(deadline, Band);

            // 4. Weave, unblocked with no seed needed, files the epic with mode next - the string the server itself received.
            await harness.ClickAsync(deadline, WeaveButton);
            await deadline.BoundAsync("waiting for the epic's page", () => page.WaitForURLAsync($"**/issues/{projectKey}-*"));

            using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
            var key = new Uri(page.Url).AbsolutePath.Split('/').Last();
            var issue = await http.GetFromJsonAsync<IssueDto>($"/api/issues/{key}", Web);
            Assert.Equal("epic", issue!.Type);
            Assert.Contains("Mode: **next**.", issue.Description);

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
        Assert.Equal(["Seed", "Most pressing", "Most common", "Next step"], steps.Select(s => s.GetProperty("name").GetString()).ToArray());
        Assert.All(steps, s => Assert.True(s.GetProperty("hint").GetString()!.Length > 0, $"a step has no hint: {read}"));

        var marked = steps.Where(s => s.GetProperty("pressed").GetString() == "true").ToArray();
        var one = Assert.Single(marked);
        Assert.Equal(current, one.GetProperty("name").GetString());
        Assert.True(one.GetProperty("disabled").GetBoolean(), $"the current step can be pressed: {read}");
        Assert.All(steps.Except(marked), s => Assert.False(s.GetProperty("disabled").GetBoolean()));

        Assert.Equal(current, read.GetProperty("pill").GetString());
    }

    /// <summary>A breakdown column with a playbook row covering epic, and one project to weave into - filed the way any caller files them with the wall off, the local person may file everything this test files. Returns the project's key, for matching the epic's address.</summary>
    private static async Task<string> SeedAsync(string baseUrl, IServiceProvider services)
    {
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
            var breakdown = new EfHatchStatus { Name = "breakdown", SortOrder = 10 };
            var done = new EfHatchStatus { Name = "done", SortOrder = 20 };
            db.Statuses.AddRange(breakdown, done);
            await db.SaveChangesAsync();

            var now = DateTimeOffset.UtcNow;
            db.Playbooks.Add(new EfHatchPlaybook
            {
                FromStatusId = breakdown.Id,
                ToStatusId = done.Id,
                Types = "epic",
                Shape = EfHatchPlaybook.AnyShape,
                Prompt = "weave it",
                Model = "sonnet",
                Effort = "high",
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("WMP", "Weave Modes"));
        return project.Key;
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

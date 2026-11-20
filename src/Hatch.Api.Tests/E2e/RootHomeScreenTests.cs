using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2592's acceptance, rendered: at the root address, a person whose home
/// screen is unset sees the board, and a person who set it to Ambitions sees
/// an ambition row instead, with no board column and the logo renamed. Run at
/// the desktop viewport in Chromium and at the <c>iPhone 14 Pro</c> profile in
/// WebKit, where the ambitions page may not scroll sideways. One project, one
/// ambition and one capability, filed over plain HTTP, already draw a row.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class RootHomeScreenTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    /// <summary>Sub-pixel layout is fractional; two boxes that touch are not overlapping.</summary>
    private const double Tolerance = 0.5;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>The brand link's accessible name and the page's width against the viewport's, read in one script so they are one moment.</summary>
    private const string MeasureScript = """
        () => ({
          brandLabel: document.querySelector('a.hatch-topbar__brand')?.getAttribute('aria-label') ?? '',
          innerWidth: window.innerWidth,
          scrollWidth: document.documentElement.scrollWidth,
        })
        """;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheRoot_RendersAtTheDesktopProfile() => RunAsync("RootHomeDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheRoot_RendersAtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("RootHomePhone", "phone", DeviceProfile.IPhone14Pro);
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

            // Nothing seeded yet, and the home screen is unset: the root draws the board.
            await harness.OpenAsync(deadline, baseUrl, "/");
            await harness.WaitForFirstColumnAsync(deadline);
            Assert.False(await harness.IsVisibleAsync(deadline, ".hatch-ambition-row"));

            var unset = await harness.EvaluateAsync<JsonElement>(deadline, "measuring the root with no home screen set", MeasureScript);
            output.WriteLine($"{profileName} / (home unset): {unset}");
            output.WriteLine($"saved {await harness.FullPageScreenshotAsync(deadline, $"home-{profileName}-unset")}");
            Assert.Equal("Board", unset.GetProperty("brandLabel").GetString());

            await deadline.BoundAsync("filing one ambition with one capability", () => SeedAmbitionAsync(baseUrl));
            await deadline.BoundAsync("setting Ambitions as the home screen", () => SetHomeAsync(baseUrl, "ambitions"));

            // A fresh navigation re-runs the Me read, which only runs once per load.
            await harness.OpenAsync(deadline, baseUrl, "/");
            await harness.WaitForVisibleAsync(deadline, ".hatch-ambition-row");
            Assert.False(await harness.IsVisibleAsync(deadline, ".hatch-column"));

            var ambitions = await harness.EvaluateAsync<JsonElement>(deadline, "measuring the root with Ambitions as home", MeasureScript);
            output.WriteLine($"{profileName} / (home ambitions): {ambitions}");
            output.WriteLine($"saved {await harness.FullPageScreenshotAsync(deadline, $"home-{profileName}-ambitions")}");
            Assert.Equal("Ambitions", ambitions.GetProperty("brandLabel").GetString());

            harness.AssertNoPageErrors();

            // Nothing may scroll sideways on the phone; the same check costs nothing on the desktop, so it is made there too.
            AssertFits(ambitions, profile is not null);
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

    private static void AssertFits(JsonElement measured, bool phone)
    {
        var innerWidth = measured.GetProperty("innerWidth").GetDouble();
        Assert.True(
            measured.GetProperty("scrollWidth").GetDouble() <= innerWidth + Tolerance,
            $"the page scrolls sideways{(phone ? " on the phone" : "")}: {measured}");
    }

    /// <summary>One project, one ambition and one capability on it - enough for the Ambitions page to draw a row.</summary>
    private static async Task SeedAmbitionAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("RHS", "RHS"));
        var ambition = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "ambition", "A person reaches the root and sees their own home", null, null, null, null));
        await PostAsync<CapabilityCreateRequest, CapabilityDto>(
            http, $"/api/issues/{ambition.Key}/capabilities",
            new("A person reaches the root and sees their own home", "Open the root address and look at what draws"));
    }

    /// <summary>PUT /api/me/home is person-only: no Authorization header, the same as the browser and the seed above.</summary>
    private static async Task SetHomeAsync(string baseUrl, string home)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        using var response = await http.PutAsJsonAsync("/api/me/home", new PutHomeRequest(home), Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"PUT /api/me/home answered {(int)response.StatusCode}: {text}");
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

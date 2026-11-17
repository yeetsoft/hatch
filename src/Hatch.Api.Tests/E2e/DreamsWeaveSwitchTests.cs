using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2401's acceptance, rendered: the Weave section's switch, <b>Choose the
/// dreams</b>, opens the search row and the list when turned on, and shows a
/// count of what is on screen; turned off, both close. In mode <b>Seed</b>
/// with a seed typed and no search of its own, turning the switch on previews
/// the seed by searching the list for it, carrying both <c>open=true</c> and
/// <c>q=&lt;seed&gt;</c> on the address. Run at the desktop viewport in
/// Chromium and at the <c>iPhone 14 Pro</c> profile in WebKit.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class DreamsWeaveSwitchTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private const string Switch = ".hatch-dream-weave__choose";

    private const string SearchBox = "input[aria-label=\"Search dreams\"]";

    private const string SeedInput = "input[aria-label=\"Seed\"]";

    private const string CountLine = ".hatch-dream-weave__choose-count";

    private static readonly string[] Filed = ["First filed dream", "Second filed dream", "A dream about a needle"];

    private const string Seed = "needle";

    /// <summary>The drawn rows, the switch's own state, the count line and the address, read in one script so they are one moment.</summary>
    private const string StateScript = """
        () => ({
          rows: document.querySelectorAll('.hatch-dream').length,
          search: document.querySelectorAll('input[aria-label="Search dreams"]').length,
          checked: document.querySelector('.hatch-dream-weave__choose input')?.checked ?? null,
          count: document.querySelector('.hatch-dream-weave__choose-count')?.textContent ?? null,
          address: location.search,
        })
        """;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheSwitch_OpensAndClosesTheListAtTheDesktopProfile() => RunAsync("DreamsWeaveSwitchDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheSwitch_OpensAndClosesTheListAtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("DreamsWeaveSwitchPhone", "phone", DeviceProfile.IPhone14Pro);
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
            await deadline.BoundAsync("filing a project and three dreams", () => FileAsync(baseUrl));

            await harness.OpenAsync(deadline, baseUrl, "/dreams");
            await harness.WaitForVisibleAsync(deadline, Switch);

            var folded = await harness.EvaluateAsync<JsonElement>(deadline, "reading the folded page", StateScript);
            output.WriteLine($"{profileName} folded: {folded}");
            Assert.False(folded.GetProperty("checked").GetBoolean());
            Assert.Equal(0, folded.GetProperty("search").GetInt32());
            Assert.Equal(0, folded.GetProperty("rows").GetInt32());

            // On: the search row and the list open, the rows show, and the count line reads 3.
            await harness.ClickAsync(deadline, Switch);
            await harness.WaitForVisibleAsync(deadline, SearchBox);
            foreach (var body in Filed) await harness.WaitForVisibleAsync(deadline, $".hatch-dream:has-text(\"{body}\")");
            await harness.WaitForVisibleAsync(deadline, CountLine);
            var opened = await harness.EvaluateAsync<JsonElement>(deadline, "reading the open page", StateScript);
            output.WriteLine($"{profileName} open: {opened}");
            Assert.True(opened.GetProperty("checked").GetBoolean());
            Assert.Equal(3, opened.GetProperty("rows").GetInt32());
            Assert.Equal("3 dreams will be handed to the weave.", opened.GetProperty("count").GetString());
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"switch-on-{profileName}")}");

            // Off: both close.
            await harness.ClickAsync(deadline, Switch);
            await harness.WaitForHiddenAsync(deadline, SearchBox);
            var closed = await harness.EvaluateAsync<JsonElement>(deadline, "reading the closed page", StateScript);
            output.WriteLine($"{profileName} closed: {closed}");
            Assert.False(closed.GetProperty("checked").GetBoolean());
            Assert.Equal(0, closed.GetProperty("rows").GetInt32());
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"switch-off-{profileName}")}");

            // In mode Seed (the default) with a seed typed and no search of its own,
            // turning the switch on previews the seed by searching the list for it.
            await harness.ClickAsync(deadline, SeedInput);
            await harness.InsertTextAsync(deadline, Seed);
            await harness.ClickAsync(deadline, Switch);
            await harness.WaitForVisibleAsync(deadline, $".hatch-dream:has-text(\"{Filed[2]}\")");
            await harness.WaitForHiddenAsync(deadline, $".hatch-dream:has-text(\"{Filed[0]}\")");
            var previewed = await harness.EvaluateAsync<JsonElement>(deadline, "reading the seed preview", StateScript);
            output.WriteLine($"{profileName} seed preview: {previewed}");
            Assert.Equal(1, previewed.GetProperty("rows").GetInt32());
            Assert.Equal($"?q={Seed}&open=true", previewed.GetProperty("address").GetString());
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"switch-seed-preview-{profileName}")}");

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

    /// <summary>One project, three dreams, one of them naming the seed the mode-Seed case searches for.</summary>
    private static async Task FileAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        await SendAsync<ProjectCreateRequest, ProjectDto>(http, HttpMethod.Post, "/api/projects", new("DWS", "DWS"));
        foreach (var body in Filed)
            await SendAsync<DreamCreateRequest, DreamDto>(http, HttpMethod.Post, "/api/dreams", new(body, null, null));
    }

    private static async Task<TResponse> SendAsync<TRequest, TResponse>(HttpClient http, HttpMethod method, string path, TRequest body)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body, options: Web) };
        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{method} {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

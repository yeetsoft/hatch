using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2519's acceptance, rendered: an ambition's page draws a Capabilities
/// section with one row per capability and all four states told apart, and an
/// epic that serves one of them draws a Serves section naming the ambition and
/// the capability. Run at the desktop viewport in Chromium and at the
/// <c>iPhone 14 Pro</c> profile in WebKit, where nothing may scroll sideways.
///
/// <para>One ambition with four capabilities, three given a verdict and the
/// fourth left unchecked, and one epic under it serving the first. Every row is
/// filed over plain HTTP, the way any caller files it with the wall off. Each
/// look is saved as a PNG beside the case's other artifacts, because a green
/// assert is not a look.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class AmbitionCapabilitiesPageTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    /// <summary>Sub-pixel layout is fractional; two boxes that touch are not overlapping.</summary>
    private const double Tolerance = 0.5;

    /// <summary>A section is a <c>details</c> whose summary is its title; the section id is a storage key, not an element id.</summary>
    private const string Capabilities = ".hatch-issue-section:has(> summary:text-matches(\"^Capabilities\"))";
    private const string Serves = ".hatch-issue-section:has(> summary:text-matches(\"^Serves\"))";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly string[] StateWords = ["Holds", "Does not hold", "Cannot tell", "Not checked"];

    private const string FirstStatement = "Partners can place an order without a phone call";

    /// <summary>One section's text and row count, found by its title, and the page's width against the viewport's, read in one script so they are one moment.</summary>
    private const string MeasureScript = """
        (title) => {
          const section = [...document.querySelectorAll('details.hatch-issue-section')]
            .find((d) => d.querySelector(':scope > summary')?.textContent.startsWith(title));
          return {
            text: section ? section.textContent : null,
            rows: section ? section.querySelectorAll('.hatch-capability-row').length : 0,
            innerWidth: window.innerWidth,
            scrollWidth: document.documentElement.scrollWidth,
          };
        }
        """;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheSections_RenderAtTheDesktopProfile() => RunAsync("AmbitionCapabilitiesDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheSections_RenderAtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("AmbitionCapabilitiesPhone", "phone", DeviceProfile.IPhone14Pro);
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
            var (ambition, epic) = await deadline.BoundAsync("filing the ambition", () => FileAmbitionAsync(baseUrl));

            // The ambition: one row per capability, four states, all drawn.
            await harness.OpenAsync(deadline, baseUrl, $"/issues/{ambition}");
            await harness.WaitForVisibleAsync(deadline, ".hatch-issue-page");
            await harness.WaitForVisibleAsync(deadline, Capabilities);
            await harness.WaitForVisibleAsync(deadline, $"{Capabilities} .hatch-capability-row");

            var measured = await harness.EvaluateAsync<JsonElement>(
                deadline, "measuring the Capabilities section", MeasureScript, "Capabilities");
            output.WriteLine($"{profileName} /issues/{ambition}: {measured}");
            await ScrollToAsync(harness, deadline, "Capabilities");
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"ambition-{profileName}")}");

            var text = measured.GetProperty("text").GetString()!;
            foreach (var word in StateWords)
                Assert.True(text.Contains(word), $"the Capabilities section does not say \"{word}\": {text}");
            Assert.Equal(4, measured.GetProperty("rows").GetInt32());
            Assert.Contains(epic, text);
            AssertFits(measured, profile is not null);

            // The epic: the Serves section names the ambition and the capability it serves.
            await harness.GoToAsync(deadline, $"issues/{epic}");
            await harness.WaitForVisibleAsync(deadline, ".hatch-issue-page");
            await harness.WaitForVisibleAsync(deadline, Serves);

            var serves = await harness.EvaluateAsync<JsonElement>(deadline, "measuring the Serves section", MeasureScript, "Serves");
            output.WriteLine($"{profileName} /issues/{epic}: {serves}");
            await ScrollToAsync(harness, deadline, "Serves");
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"epic-{profileName}")}");

            var servesText = serves.GetProperty("text").GetString()!;
            Assert.Contains(ambition, servesText);
            Assert.Contains(FirstStatement, servesText);
            Assert.Contains("Holds", servesText);
            AssertFits(serves, profile is not null);

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

    /// <summary>Brings the titled section to the top of the viewport, so the screenshot that follows shows what was asserted on rather than the page's header.</summary>
    private static Task ScrollToAsync(E2eHarness harness, Deadline deadline, string title) =>
        harness.EvaluateAsync<bool>(deadline, $"scrolling to {title}", """
            (title) => {
              const section = [...document.querySelectorAll('details.hatch-issue-section')]
                .find((d) => d.querySelector(':scope > summary')?.textContent.startsWith(title));
              section?.scrollIntoView({ block: 'start' });
              return !!section;
            }
            """, title);

    /// <summary>The page may not scroll sideways on the phone; on the desktop the same check costs nothing, so it is made there too.</summary>
    private static void AssertFits(JsonElement measured, bool phone)
    {
        var innerWidth = measured.GetProperty("innerWidth").GetDouble();
        Assert.True(
            measured.GetProperty("scrollWidth").GetDouble() <= innerWidth + Tolerance,
            $"the page scrolls sideways{(phone ? " on the phone" : "")}: {measured}");
    }

    /// <summary>
    /// A project, an ambition with four capabilities - holds, does not hold,
    /// cannot tell and not checked - and an epic under it serving the first.
    /// Returns the ambition's key and the epic's.
    /// </summary>
    private static async Task<(string Ambition, string Epic)> FileAmbitionAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("CAP", "CAP"));
        var ambition = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "ambition", "Open the ordering to partners", null, null, null, null));

        var statements = new[]
        {
            (FirstStatement, "Place one as a partner and count the calls", "holds"),
            ("An order is confirmed within a minute", "Time an order from the button to the email", "does_not_hold"),
            ("A partner sees only their own orders", "Sign in as two partners and compare the lists", "cannot_tell"),
            ("Staff are told of every new order", "Place an order and look for the notice", null),
        };

        var ids = new List<long>();
        foreach (var (statement, howToTell, verdict) in statements)
        {
            var capability = await PostAsync<CapabilityCreateRequest, CapabilityDto>(
                http, $"/api/issues/{ambition.Key}/capabilities", new(statement, howToTell));
            ids.Add(capability.Id);
            if (verdict is not null)
                await PostAsync<CapabilityVerdictRequest, CapabilityVerdictDto>(
                    http, $"/api/issues/{ambition.Key}/capabilities/{capability.Id}/verdicts",
                    new(verdict, "the staging order flow", "3f9c2ab"));
        }

        var epic = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "epic", "A partner order form", null, ambition.Key, null, null));

        using var put = await http.PutAsJsonAsync($"/api/issues/{epic.Key}/serves", new IssueServesWriteRequest([ids[0]]), Web);
        var putText = await put.Content.ReadAsStringAsync();
        Assert.True(put.IsSuccessStatusCode, $"PUT /api/issues/{epic.Key}/serves answered {(int)put.StatusCode}: {putText}");

        return (ambition.Key, epic.Key);
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

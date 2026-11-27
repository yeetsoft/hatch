using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2844's acceptance, rendered: an ambition's own page opens with a crumb
/// back to Ambitions, then a header card - the stage pill and sentence, the
/// capability bar and legend, and a row of facts including the "Epics at
/// once" control - inserted after Details, with the ambition's own
/// Description under it and Capabilities after that. The stage and the bar
/// read the same word and counts <c>GET /api/ambitions?key=</c> answers for
/// the row. An epic's page draws none of this and keeps "Stories at once".
/// Run at the desktop viewport in Chromium and at the <c>iPhone 14 Pro</c>
/// profile in WebKit, where nothing may scroll sideways.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class AmbitionHeaderPageTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    /// <summary>Sub-pixel layout is fractional; two boxes that touch are not overlapping.</summary>
    private const double Tolerance = 0.5;

    private const string DescriptionSection = ".hatch-issue-section:has(> summary:text-matches(\"^Description\"))";
    private const string CapabilitiesSection = ".hatch-issue-section:has(> summary:text-matches(\"^Capabilities\"))";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>What the crumb, the header card and the order of the sections read as, and the page's width against the viewport's, read in one script so they are one moment.</summary>
    private const string MeasureScript = """
        () => {
          const crumb = document.querySelector('.hatch-ambition-crumb');
          const header = document.querySelector('.hatch-ambition-header');
          const pill = header?.querySelector('.hatch-stage-pill');
          const bar = header?.querySelector('.hatch-ambition-bar');
          const why = header?.querySelector('.hatch-ambition-why');
          const facts = header?.querySelector('.hatch-ambition-header-facts');
          const description = document.querySelector('.hatch-issue-section:has(> summary)');
          const sections = [...document.querySelectorAll('details.hatch-issue-section')];
          const descriptionSection = sections.find((d) => d.querySelector(':scope > summary')?.textContent.startsWith('Description'));
          const capabilitiesSection = sections.find((d) => d.querySelector(':scope > summary')?.textContent.startsWith('Capabilities'));
          return {
            crumbText: crumb ? crumb.textContent : null,
            crumbHref: crumb?.querySelector('a')?.getAttribute('href') ?? null,
            pillWord: pill ? pill.textContent : null,
            stageWhy: why ? why.textContent : null,
            barLabel: bar ? bar.getAttribute('aria-label') : null,
            factsText: facts ? facts.textContent : null,
            headerTop: header ? header.getBoundingClientRect().top : null,
            descriptionTop: descriptionSection ? descriptionSection.getBoundingClientRect().top : null,
            capabilitiesTop: capabilitiesSection ? capabilitiesSection.getBoundingClientRect().top : null,
            innerWidth: window.innerWidth,
            scrollWidth: document.documentElement.scrollWidth,
          };
        }
        """;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheHeader_RendersAtTheDesktopProfile() => RunAsync("AmbitionHeaderDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheHeader_RendersAtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("AmbitionHeaderPhone", "phone", DeviceProfile.IPhone14Pro);
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
            var (ambition, epic) = await deadline.BoundAsync("seeding the ambition and its epic", () => SeedAsync(baseUrl, harness.Services));

            using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
            var row = await GetAsync<JsonElement>(http, $"/api/ambitions?key={ambition}");
            var expected = Assert.Single(row.EnumerateArray());
            var expectedStage = expected.GetProperty("stage").GetString()!;
            var expectedPill = char.ToUpperInvariant(expectedStage[0]) + expectedStage[1..];
            var expectedWhy = expected.GetProperty("stageWhy").GetString()!;

            await harness.OpenAsync(deadline, baseUrl, $"/issues/{ambition}");
            await harness.WaitForVisibleAsync(deadline, ".hatch-issue-page");
            await harness.WaitForVisibleAsync(deadline, ".hatch-ambition-crumb");
            await harness.WaitForVisibleAsync(deadline, ".hatch-ambition-header");

            var measured = await harness.EvaluateAsync<JsonElement>(deadline, "measuring the header", MeasureScript);
            output.WriteLine($"{profileName} /issues/{ambition}: {measured}");
            await ScrollToHeaderAsync(harness, deadline);
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"ambition-header-{profileName}")}");

            // The crumb: "Ambitions / the title", linking back with no scope on the tab.
            var crumbText = measured.GetProperty("crumbText").GetString()!;
            Assert.Contains("Ambitions", crumbText);
            Assert.Contains("/ Ship the ambition header", crumbText);
            Assert.Equal("/ambitions", measured.GetProperty("crumbHref").GetString());

            // The stage, the sentence and the bar agree with the Ambitions read for this key.
            Assert.Equal(expectedPill, measured.GetProperty("pillWord").GetString());
            Assert.Equal(expectedWhy, measured.GetProperty("stageWhy").GetString());
            var holds = expected.GetProperty("holds").GetInt32();
            Assert.Contains($"{holds} hold", measured.GetProperty("barLabel").GetString());

            // The facts: the project, the limit control's label, the pass count, pre-approval.
            var factsText = measured.GetProperty("factsText").GetString()!;
            Assert.Contains("Epics at once", factsText);
            Assert.Contains("1 pass so far", factsText);
            Assert.Contains("Pre-approval", factsText);

            // The order: header, then Description, then Capabilities.
            var headerTop = measured.GetProperty("headerTop").GetDouble();
            var descriptionTop = measured.GetProperty("descriptionTop").GetDouble();
            var capabilitiesTop = measured.GetProperty("capabilitiesTop").GetDouble();
            Assert.True(headerTop < descriptionTop, $"the header does not sit above Description: {measured}");
            Assert.True(descriptionTop < capabilitiesTop, $"Description does not sit above Capabilities: {measured}");

            AssertFits(measured, profile is not null);

            // Typing into the header's own limit field saves through the same route
            // the Details form does, and the page keeps reading "Epics at once".
            await harness.ClickAsync(deadline, ".hatch-ambition-header-facts input");
            await harness.SelectAllAsync(deadline);
            await harness.InsertTextAsync(deadline, "3");
            await harness.Page!.Locator(".hatch-ambition-header-facts input").BlurAsync();
            await harness.WaitForFunctionAsync(deadline, "waiting for the limit to save", $$"""
                async (key) => {
                  const res = await fetch(`/api/issues/${key}`);
                  if (!res.ok) return false;
                  const issue = await res.json();
                  return issue.wipLimit === 3;
                }
                """, ambition);
            Assert.Contains("Epics at once", await harness.EvaluateOnAsync<string>(
                deadline, "reading the header after the save", ".hatch-ambition-header-facts", "(el) => el.textContent"));

            // An epic's page draws no crumb and no header, and keeps "Stories at once".
            await harness.GoToAsync(deadline, $"issues/{epic}");
            await harness.WaitForVisibleAsync(deadline, ".hatch-issue-page");
            Assert.False(await harness.IsVisibleAsync(deadline, ".hatch-ambition-crumb"), "an epic's page draws a crumb");
            Assert.False(await harness.IsVisibleAsync(deadline, ".hatch-ambition-header"), "an epic's page draws a header card");

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

    /// <summary>Brings the header card to the top of the viewport, so the screenshot that follows shows what was asserted on rather than the status bar above it.</summary>
    private static Task ScrollToHeaderAsync(E2eHarness harness, Deadline deadline) =>
        harness.EvaluateAsync<bool>(deadline, "scrolling to the header", """
            () => {
              const header = document.querySelector('.hatch-ambition-header');
              header?.scrollIntoView({ block: 'start' });
              return !!header;
            }
            """);

    /// <summary>The page may not scroll sideways on the phone; on the desktop the same check costs nothing, so it is made there too.</summary>
    private static void AssertFits(JsonElement measured, bool phone)
    {
        var innerWidth = measured.GetProperty("innerWidth").GetDouble();
        Assert.True(
            measured.GetProperty("scrollWidth").GetDouble() <= innerWidth + Tolerance,
            $"the page scrolls sideways{(phone ? " on the phone" : "")}: {measured}");
    }

    /// <summary>
    /// A project, an ambition with a description holding Principles and Out of
    /// scope headings, one capability that holds, and one recorded pass. An
    /// epic under it with no capability served, for the comparison that it
    /// draws no header. Returns the ambition's key and the epic's.
    /// </summary>
    private static async Task<(string Ambition, string Epic)> SeedAsync(string baseUrl, IServiceProvider services)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("AHD", "AHD"));
        var ambition = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "ambition", "Ship the ambition header", null, null, null, null));

        const string description = """
            What this ambition is for.

            ## Principles

            Keep it simple.

            ## Out of scope

            Answering a question from this page.
            """;
        using var patch = await http.PatchAsJsonAsync(
            $"/api/issues/{ambition.Key}", new IssuePatchRequest(null, description, null, null, null, null, null, null, null, null), Web);
        var patchText = await patch.Content.ReadAsStringAsync();
        Assert.True(patch.IsSuccessStatusCode, $"PATCH /api/issues/{ambition.Key} answered {(int)patch.StatusCode}: {patchText}");

        var capability = await PostAsync<CapabilityCreateRequest, CapabilityDto>(
            http, $"/api/issues/{ambition.Key}/capabilities", new("Partners can place an order without a phone call", "Place one as a partner and count the calls"));
        await PostAsync<CapabilityVerdictRequest, CapabilityVerdictDto>(
            http, $"/api/issues/{ambition.Key}/capabilities/{capability.Id}/verdicts",
            new("holds", "the staging order flow", "3f9c2ab"));

        var epic = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "epic", "A partner order form", null, ambition.Key, null, null));

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();

        Assert.True(IssueKey.TryParse(ambition.Key, out var projectKey, out var number));
        var issueId = await db.Issues.WithKey(projectKey, number).Select(i => i.Id).SingleAsync();

        var implementation = await db.Statuses.OrderBy(s => s.SortOrder).FirstAsync(s => !s.IsTerminal && !s.IsDeferred);
        var now = DateTimeOffset.UtcNow;
        db.WorkLog.Add(new EfHatchWorkLogEntry
        {
            IssueId = issueId, SessionId = Guid.NewGuid().ToString("N"), StartedAt = now.AddHours(-1), EndedAt = now,
            DurationMs = (long)TimeSpan.FromHours(1).TotalMilliseconds, CreatedAt = now,
            IssueType = "ambition", PlaybookFromStatusId = implementation.Id, PlaybookToStatusId = implementation.Id,
            Outcome = ClaimOutcomes.Worked, Title = "a pass", Summary = "worked the ambition's capabilities",
        });
        await db.SaveChangesAsync();

        return (ambition.Key, epic.Key);
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }

    private static async Task<TResponse> GetAsync<TResponse>(HttpClient http, string path)
    {
        using var response = await http.GetAsync(path);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"GET {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

using Hatch.Api.Tests.Hatch;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2626's acceptance, rendered: an ambition's page draws a Latest change
/// section, after the Budget card and before Epics in flight, reading
/// <c>GET /issues/{key}/changes</c>. One capability is added then edited,
/// giving one change with an Added and a Changed entry; three epics beneath
/// the ambition each carry one of the three outcomes on that change, worked
/// out so the section draws its epic lines. Run at the desktop viewport in
/// Chromium and at the <c>iPhone 14 Pro</c> profile in WebKit, where nothing
/// may scroll sideways.
///
/// <para>Everything is filed over plain HTTP, including a runner minted to
/// write the one work-log row that works the change out - the one route
/// here only a key may post to - rather than anything read from or written
/// straight to the database.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class AmbitionLatestChangePageTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    /// <summary>Sub-pixel layout is fractional; two boxes that touch are not overlapping.</summary>
    private const double Tolerance = 0.5;

    /// <summary>A section is a <c>details</c> whose summary is its title; the section id is a storage key, not an element id.</summary>
    private const string LatestChange = ".hatch-issue-section:has(> summary:text-matches(\"^Latest change\"))";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>The section's text, its entry and outcome counts, and the page's width against the viewport's, read in one script so they are one moment.</summary>
    private const string MeasureScript = """
        (title) => {
          const section = [...document.querySelectorAll('details.hatch-issue-section')]
            .find((d) => d.querySelector(':scope > summary')?.textContent.startsWith(title));
          return {
            text: section ? section.textContent : null,
            entries: section ? section.querySelectorAll('.hatch-change-entry').length : 0,
            outcomes: section ? section.querySelectorAll('.hatch-change-outcome').length : 0,
            presses: section ? section.querySelectorAll('.hatch-change-presses').length : 0,
            innerWidth: window.innerWidth,
            scrollWidth: document.documentElement.scrollWidth,
          };
        }
        """;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheSection_RendersAtTheDesktopProfile() => RunAsync("AmbitionLatestChangeDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheSection_RendersAtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("AmbitionLatestChangePhone", "phone", DeviceProfile.IPhone14Pro);
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
            var (ambition, carriesOn, notTouched, servesNothing) =
                await deadline.BoundAsync("filing the ambition, its change and its three epics", () => FileAmbitionAsync(baseUrl));

            await harness.OpenAsync(deadline, baseUrl, $"/issues/{ambition}");
            await harness.WaitForVisibleAsync(deadline, ".hatch-issue-page");

            await harness.WaitForVisibleAsync(deadline, LatestChange);
            await harness.WaitForVisibleAsync(deadline, $"{LatestChange} .hatch-change-outcome");

            var measured = await harness.EvaluateAsync<JsonElement>(deadline, "measuring the Latest change section", MeasureScript, "Latest change");
            output.WriteLine($"{profileName} /issues/{ambition} latest change: {measured}");

            var text = measured.GetProperty("text").GetString()!;
            Assert.Equal(2, measured.GetProperty("entries").GetInt32());
            Assert.Contains("Added", text);
            Assert.Contains("Changed", text);
            Assert.Equal(3, measured.GetProperty("outcomes").GetInt32());
            Assert.Contains(carriesOn, text);
            Assert.Contains("Carries on", text);
            Assert.Contains(notTouched, text);
            Assert.Contains("Not touched", text);
            Assert.Contains(servesNothing, text);
            Assert.Contains("Serves nothing now", text);

            // The presses show on the one epic only.
            Assert.Equal(1, measured.GetProperty("presses").GetInt32());

            // The shelve dialog names the one open story under it, then is left
            // without deciding anything.
            await harness.ClickAsync(deadline, $"{LatestChange} .hatch-progress-row:has-text(\"{servesNothing}\") button:text-is(\"Shelve it\")");
            await harness.WaitForVisibleAsync(deadline, "[role=dialog]");
            var dialogText = await harness.EvaluateAsync<string>(
                deadline, "reading the shelve dialog", "() => document.querySelector('[role=dialog]')?.textContent ?? ''");
            output.WriteLine($"shelve dialog: {dialogText}");
            Assert.Contains("1 open issue", dialogText);
            Assert.Contains("move to Deferred", dialogText);
            await harness.ClickAsync(deadline, "[role=dialog] button:text-is(\"Leave it\")");
            await harness.WaitForHiddenAsync(deadline, "[role=dialog]");

            // Keep it running decides the outcome, and the line reads the decision in place of the presses.
            await harness.ClickAsync(deadline, $"{LatestChange} .hatch-progress-row:has-text(\"{servesNothing}\") button:text-is(\"Keep it running\")");
            await harness.WaitForVisibleAsync(deadline, $"{LatestChange} .hatch-progress-row:has-text(\"{servesNothing}\") >> text=Kept running by");

            await ScrollToAsync(harness, deadline);
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"latest-change-{profileName}")}");

            var after = await harness.EvaluateAsync<JsonElement>(deadline, "measuring the Latest change section after the decision", MeasureScript, "Latest change");
            AssertFits(after, profile is not null);

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

    /// <summary>Brings the section to the top of the viewport, so the screenshot that follows shows what was asserted on rather than the page's header.</summary>
    private static Task ScrollToAsync(E2eHarness harness, Deadline deadline) =>
        harness.EvaluateAsync<bool>(deadline, "scrolling to Latest change", """
            () => {
              const section = [...document.querySelectorAll('details.hatch-issue-section')]
                .find((d) => d.querySelector(':scope > summary')?.textContent.startsWith('Latest change'));
              section?.scrollIntoView({ block: 'start' });
              return !!section;
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
    /// A project; an ambition with one capability, added then edited while
    /// still left of the implementation column, then moved into it; three
    /// epics beneath the ambition, one story under the one that will serve
    /// nothing; the three outcomes recorded on the change the edit opened;
    /// and the work-log row, written with a key minted for exactly that - the
    /// one write here a plain caller may not make - that works it out, its
    /// outcome stamped `worked` the way a real session's release does: a
    /// claim taken and released naming the row's own session, the same
    /// boundary <c>AmbitionChanges</c> reads. Returns the ambition's key and
    /// the three epics'.
    /// </summary>
    private static async Task<(string Ambition, string CarriesOn, string NotTouched, string ServesNothing)> FileAmbitionAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var statuses = await GetAsync<StatusDto[]>(http, "/api/statuses");
        var inProgress = statuses.Single(s => s.Name == "In Progress");

        using var implementation = await http.PutAsJsonAsync($"/api/statuses/{inProgress.Id}/implementation", new ImplementationRequest(true), Web);
        var implementationText = await implementation.Content.ReadAsStringAsync();
        Assert.True(implementation.IsSuccessStatusCode, $"PUT /api/statuses/{inProgress.Id}/implementation answered {(int)implementation.StatusCode}: {implementationText}");

        // So the shelve dialog names a real column - this board's own seed
        // carries none.
        await PostAsync<StatusCreateRequest, StatusDto>(http, "/api/statuses", new("Deferred", null, false, null, true));

        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("LCH", "LCH"));
        var ambition = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "ambition", "Changes to the ordering flow", null, null, null, null));

        var capability = await PostAsync<CapabilityCreateRequest, CapabilityDto>(
            http, $"/api/issues/{ambition.Key}/capabilities",
            new("Partners can place an order without a phone call", "Place one as a partner and count the calls"));

        using var patch = await PatchAsJsonAsync(
            http, $"/api/issues/{ambition.Key}/capabilities/{capability.Id}",
            new CapabilityPatchRequest("Partners can place and track an order without a phone call", null));
        var patchText = await patch.Content.ReadAsStringAsync();
        Assert.True(patch.IsSuccessStatusCode, $"PATCH /api/issues/{ambition.Key}/capabilities/{capability.Id} answered {(int)patch.StatusCode}: {patchText}");

        await MoveAsync(http, ambition.Key, inProgress.Id);

        var carriesOn = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "epic", "A partner order form", null, ambition.Key, null, null));
        var notTouched = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "epic", "Order notices for staff", null, ambition.Key, null, null));
        var servesNothing = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "epic", "A one-call order line", null, ambition.Key, null, null));
        await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "story", "Dial-in order taking", null, servesNothing.Key, null, null));

        var changes = await GetAsync<ChangeDto[]>(http, $"/api/issues/{ambition.Key}/changes");
        var change = Assert.Single(changes);

        await PostAsync<ChangeOutcomeRequest, ChangeOutcomeDto>(
            http, $"/api/issues/{ambition.Key}/changes/{change.Id}/outcomes",
            new(carriesOn.Key, "carries_on", "its work still stands"));
        await PostAsync<ChangeOutcomeRequest, ChangeOutcomeDto>(
            http, $"/api/issues/{ambition.Key}/changes/{change.Id}/outcomes",
            new(notTouched.Key, "not_touched", "the change did not reach what this epic serves"));
        await PostAsync<ChangeOutcomeRequest, ChangeOutcomeDto>(
            http, $"/api/issues/{ambition.Key}/changes/{change.Id}/outcomes",
            new(servesNothing.Key, "serves_nothing", "a phone call is what it served"));

        var secret = await MintRunnerSecretAsync(http, "ha-2626-change-seed");
        using var workLog = new HttpClient { BaseAddress = new Uri(baseUrl) };
        workLog.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);

        var now = DateTimeOffset.UtcNow;
        var sessionId = Guid.NewGuid().ToString("N");
        await PostWorkLogAsync(workLog, ambition.Key, change.Id, sessionId, now.AddMinutes(-10), now, "worked out the change");

        // The row above carries no outcome of its own - see WorkLogEntryRequest.
        // It is stamped `worked` the way a real session's release does: a
        // claim taken and then released naming the same session, which is
        // what AmbitionChanges reads as the change's boundary.
        var taken = await PostAsync<ClaimRequest, ClaimTakenDto>(http, $"/api/issues/{ambition.Key}/claim", new("ha-2626-change-seed"));
        using var released = await http.DeleteAsync(
            $"/api/issues/{ambition.Key}/claim?token={taken.Token}&outcome=worked&sessionId={sessionId}");
        Assert.Equal(System.Net.HttpStatusCode.NoContent, released.StatusCode);

        return (ambition.Key, carriesOn.Key, notTouched.Key, servesNothing.Key);
    }

    private static async Task MoveAsync(HttpClient http, string key, int statusId)
    {
        using var response = await http.PostAsJsonAsync($"/api/issues/{key}/move", new IssueMoveRequest(statusId, null, null), Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST /api/issues/{key}/move answered {(int)response.StatusCode}: {text}");
    }

    /// <summary>
    /// Mints a runner for the work-log row below, reading only <c>secret</c>
    /// out of the answer rather than the whole <see cref="RunnerMintedDto"/>:
    /// its <c>Runner</c> carries an enum this test's plain <see cref="Web"/>
    /// options cannot read back, the server's own options having a string
    /// converter this JSON came from that these do not.
    /// </summary>
    private static async Task<string> MintRunnerSecretAsync(HttpClient http, string name)
    {
        using var response = await http.PostAsJsonAsync("/api/runners", new CreateRunnerRequest(name), Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST /api/runners answered {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.GetProperty("secret").GetString()!;
    }

    private static async Task PostWorkLogAsync(HttpClient http, string key, long changeEventId, string sessionId, DateTimeOffset start, DateTimeOffset end, string title)
    {
        var request = new WorkLogEntryRequest(
            sessionId, start, end, (long)(end - start).TotalMilliseconds,
            title, "worked out what the change does to each epic", false, 1, 0.5m, [],
            IssueType: "ambition", ChangeEventId: changeEventId);
        await PostAsync<WorkLogEntryRequest, WorkLogEntryDto>(http, $"/api/issues/{key}/work-log", request);
    }

    private static async Task<HttpResponseMessage> PatchAsJsonAsync<TRequest>(HttpClient http, string path, TRequest body) =>
        await http.PatchAsync(path, JsonContent.Create(body, options: Web));

    private static async Task<TResponse> GetAsync<TResponse>(HttpClient http, string path)
    {
        using var response = await http.GetAsync(path);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"GET {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

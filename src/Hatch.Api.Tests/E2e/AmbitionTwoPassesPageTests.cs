using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2552's acceptance, rendered: an ambition's page draws a Capabilities
/// section, an Epics in flight section and a Passes section together, each
/// dated or marked where its own ticket (HA-2519, HA-2551, HA-2550) already
/// proved it in isolation. Run at the desktop viewport in Chromium and at the
/// <c>iPhone 14 Pro</c> profile in WebKit, where nothing may scroll sideways.
///
/// <para>One ambition with three capabilities, two passes over them with one
/// changed verdict, an epic's own pass that cannot record a verdict, and two
/// epics beneath it - one open and serving a capability, one done. Everything
/// is filed over plain HTTP, including a runner minted to write the three
/// work-log rows - the one route here only a key may post to - rather than
/// anything read from or written straight to the database.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class AmbitionTwoPassesPageTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    /// <summary>Sub-pixel layout is fractional; two boxes that touch are not overlapping.</summary>
    private const double Tolerance = 0.5;

    /// <summary>A section is a <c>details</c> whose summary is its title; the section id is a storage key, not an element id.</summary>
    private const string Capabilities = ".hatch-issue-section:has(> summary:text-matches(\"^Capabilities\"))";
    private const string EpicsInFlight = ".hatch-issue-section:has(> summary:text-matches(\"^Epics in flight\"))";
    private const string Passes = ".hatch-issue-section:has(> summary:text-matches(\"^Passes\"))";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>One section's text, row and pass counts, found by its title, and the page's width against the viewport's, read in one script so they are one moment.</summary>
    private const string MeasureScript = """
        (title) => {
          const section = [...document.querySelectorAll('details.hatch-issue-section')]
            .find((d) => d.querySelector(':scope > summary')?.textContent.startsWith(title));
          return {
            text: section ? section.textContent : null,
            rows: section ? section.querySelectorAll('.hatch-capability-row').length : 0,
            dates: section
              ? [...section.querySelectorAll('.hatch-capability-row')].map((row) => ({
                  state: row.querySelector('.hatch-capability-state')?.textContent ?? '',
                  times: [...row.querySelectorAll('time')].map((t) => ({
                    dateTime: t.getAttribute('datetime') ?? '',
                    text: t.textContent ?? '',
                    title: t.getAttribute('title') ?? '',
                  })),
                }))
              : [],
            entries: section ? section.querySelectorAll('.hatch-pass').length : 0,
            changed: section ? section.querySelectorAll('.hatch-pass-changed').length : 0,
            innerWidth: window.innerWidth,
            scrollWidth: document.documentElement.scrollWidth,
          };
        }
        """;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheSections_RenderAtTheDesktopProfile() => RunAsync("AmbitionTwoPassesDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheSections_RenderAtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("AmbitionTwoPassesPhone", "phone", DeviceProfile.IPhone14Pro);
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
            var (ambition, epic1, epic2) = await deadline.BoundAsync("filing the ambition and its two passes", () => FileAmbitionAsync(baseUrl));

            await harness.OpenAsync(deadline, baseUrl, $"/issues/{ambition}");
            await harness.WaitForVisibleAsync(deadline, ".hatch-issue-page");

            // Capabilities: three rows, each dated.
            await harness.WaitForVisibleAsync(deadline, Capabilities);
            await harness.WaitForVisibleAsync(deadline, $"{Capabilities} .hatch-capability-row");

            var capabilities = await harness.EvaluateAsync<JsonElement>(deadline, "measuring the Capabilities section", MeasureScript, "Capabilities");
            output.WriteLine($"{profileName} /issues/{ambition} capabilities: {capabilities}");
            await ScrollToAsync(harness, deadline, "Capabilities");
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"capabilities-{profileName}")}");

            Assert.Equal(3, capabilities.GetProperty("rows").GetInt32());
            AssertDated(capabilities);
            AssertFits(capabilities, profile is not null);

            // Epics in flight: epic1 open and served, epic2 not there at all.
            await harness.WaitForVisibleAsync(deadline, $"{EpicsInFlight} .hatch-progress-row");
            var inFlight = await harness.EvaluateAsync<JsonElement>(deadline, "measuring the Epics in flight section", MeasureScript, "Epics in flight");
            output.WriteLine($"{profileName} /issues/{ambition} epics in flight: {inFlight}");
            await ScrollToAsync(harness, deadline, "Epics in flight");
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"epics-in-flight-{profileName}")}");

            var inFlightText = inFlight.GetProperty("text").GetString()!;
            Assert.Contains(epic1, inFlightText);
            Assert.Contains("1 open, runs 1 at once", inFlightText);
            Assert.Contains("#1", inFlightText);
            Assert.DoesNotContain(epic2, inFlightText);
            AssertFits(inFlight, profile is not null);

            // Passes: two passes and the epic's own entry, the newer pass's
            // changed verdict marked, and the verdicts-not-recorded note on
            // the entry that is not a pass over the ambition itself.
            await harness.WaitForVisibleAsync(deadline, $"{Passes} .hatch-pass");
            var passes = await harness.EvaluateAsync<JsonElement>(deadline, "measuring the Passes section", MeasureScript, "Passes");
            output.WriteLine($"{profileName} /issues/{ambition} passes: {passes}");
            await ScrollToAsync(harness, deadline, "Passes");
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"passes-{profileName}")}");

            Assert.Equal(3, passes.GetProperty("entries").GetInt32());
            Assert.Equal(1, passes.GetProperty("changed").GetInt32());
            Assert.Contains("Verdicts not recorded as data.", passes.GetProperty("text").GetString());
            AssertFits(passes, profile is not null);

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

    /// <summary>HA-2551: each capability row with a verdict holds exactly one <c>time</c> element with a non-empty <c>datetime</c>, text and title.</summary>
    private static void AssertDated(JsonElement measured)
    {
        foreach (var row in measured.GetProperty("dates").EnumerateArray())
        {
            var state = row.GetProperty("state").GetString()!;
            var times = row.GetProperty("times").EnumerateArray().ToList();
            Assert.True(times.Count == 1, $"the \"{state}\" row holds {times.Count} time elements, not one: {row}");
            foreach (var field in new[] { "dateTime", "text", "title" })
                Assert.False(
                    string.IsNullOrWhiteSpace(times[0].GetProperty(field).GetString()),
                    $"the \"{state}\" row's time has an empty {field}: {row}");
        }
    }

    /// <summary>The page may not scroll sideways on the phone; on the desktop the same check costs nothing, so it is made there too.</summary>
    private static void AssertFits(JsonElement measured, bool phone)
    {
        var innerWidth = measured.GetProperty("innerWidth").GetDouble();
        Assert.True(
            measured.GetProperty("scrollWidth").GetDouble() <= innerWidth + Tolerance,
            $"the page scrolls sideways{(phone ? " on the phone" : "")}: {measured}");
    }

    /// <summary>
    /// A project; an ambition with three capabilities, moved into "in
    /// progress"; two epics beneath it - the first serving the first
    /// capability and left open, the second serving nothing and moved to
    /// "done"; a comment, two passes of verdicts each with its own comment,
    /// and an epic's own pass around the first comment; and the three
    /// work-log rows those three passes are read from, written with a key
    /// minted for exactly that - the one write here a plain caller may not
    /// make. Returns the ambition's key and the two epics'.
    /// </summary>
    private static async Task<(string Ambition, string Epic1, string Epic2)> FileAmbitionAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var statuses = await GetAsync<StatusDto[]>(http, "/api/statuses");
        var inProgress = statuses.Single(s => s.Name == "In Progress");
        var review = statuses.Single(s => s.Name == "In Review");
        var done = statuses.Single(s => s.Name == "Done");

        using var implementation = await http.PutAsJsonAsync($"/api/statuses/{inProgress.Id}/implementation", new ImplementationRequest(true), Web);
        var implementationText = await implementation.Content.ReadAsStringAsync();
        Assert.True(implementation.IsSuccessStatusCode, $"PUT /api/statuses/{inProgress.Id}/implementation answered {(int)implementation.StatusCode}: {implementationText}");

        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("TWP", "TWP"));
        var ambition = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "ambition", "Two passes over the ordering flow", null, null, null, null));

        var statements = new[]
        {
            ("Partners can place an order without a phone call", "Place one as a partner and count the calls"),
            ("An order is confirmed within a minute", "Time an order from the button to the email"),
            ("A partner sees only their own orders", "Sign in as two partners and compare the lists"),
        };

        var capIds = new List<long>();
        foreach (var (statement, howToTell) in statements)
        {
            var capability = await PostAsync<CapabilityCreateRequest, CapabilityDto>(
                http, $"/api/issues/{ambition.Key}/capabilities", new(statement, howToTell));
            capIds.Add(capability.Id);
        }

        await MoveAsync(http, ambition.Key, inProgress.Id);

        var epic1 = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "epic", "A partner order form", null, ambition.Key, null, null));
        using var serves = await http.PutAsJsonAsync($"/api/issues/{epic1.Key}/serves", new IssueServesWriteRequest([capIds[0]]), Web);
        var servesText = await serves.Content.ReadAsStringAsync();
        Assert.True(serves.IsSuccessStatusCode, $"PUT /api/issues/{epic1.Key}/serves answered {(int)serves.StatusCode}: {servesText}");

        var epic2 = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "epic", "Order notices for staff", null, ambition.Key, null, null));
        await MoveAsync(http, epic2.Key, done.Id);

        var beforeEpic = DateTimeOffset.UtcNow;
        await PostAsync<CommentCreateRequest, CommentDto>(http, $"/api/issues/{ambition.Key}/comments", new("said while the epic's own session ran"));
        var afterEpic = DateTimeOffset.UtcNow;

        await PostVerdictsAsync(http, ambition.Key, capIds, ["does_not_hold", "holds", "cannot_tell"], "aaa1111");
        await PostAsync<CommentCreateRequest, CommentDto>(http, $"/api/issues/{ambition.Key}/comments", new(PassTable("Does not hold", "Holds", "Cannot tell")));
        var afterPass1 = DateTimeOffset.UtcNow;

        await PostVerdictsAsync(http, ambition.Key, capIds, ["holds", "holds", "cannot_tell"], "bbb2222");
        await PostAsync<CommentCreateRequest, CommentDto>(http, $"/api/issues/{ambition.Key}/comments", new(PassTable("Holds", "Holds", "Cannot tell")));

        var secret = await MintRunnerSecretAsync(http, "ha-2552-passes-seed");
        using var workLog = new HttpClient { BaseAddress = new Uri(baseUrl) };
        workLog.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);

        await PostWorkLogAsync(workLog, ambition.Key, "epic", inProgress.Id, review.Id, beforeEpic, afterEpic, "an epic's own pass");
        await PostWorkLogAsync(workLog, ambition.Key, "ambition", inProgress.Id, inProgress.Id, afterEpic, afterPass1, "the first pass");
        await PostWorkLogAsync(workLog, ambition.Key, "ambition", inProgress.Id, inProgress.Id, afterPass1, afterPass1.AddMinutes(10), "the second pass");

        return (ambition.Key, epic1.Key, epic2.Key);
    }

    private static string PassTable(string capA, string capB, string capC) =>
        $"""
        | Capability | Verdict |
        | --- | --- |
        | a | {capA} |
        | b | {capB} |
        | c | {capC} |
        """;

    private static async Task PostVerdictsAsync(HttpClient http, string ambitionKey, IReadOnlyList<long> capIds, IReadOnlyList<string> verdicts, string sha)
    {
        for (var i = 0; i < capIds.Count; i++)
            await PostAsync<CapabilityVerdictRequest, CapabilityVerdictDto>(
                http, $"/api/issues/{ambitionKey}/capabilities/{capIds[i]}/verdicts", new(verdicts[i], "the staging order flow", sha));
    }

    private static async Task MoveAsync(HttpClient http, string key, int statusId)
    {
        using var response = await http.PostAsJsonAsync($"/api/issues/{key}/move", new IssueMoveRequest(statusId, null, null), Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST /api/issues/{key}/move answered {(int)response.StatusCode}: {text}");
    }

    /// <summary>
    /// Mints a runner for the three work-log rows below, reading only
    /// <c>secret</c> out of the answer rather than the whole
    /// <see cref="RunnerMintedDto"/>: its <c>Runner</c> carries an enum this
    /// test's plain <see cref="Web"/> options cannot read back, the server's
    /// own options having a string converter this JSON came from that these
    /// do not.
    /// </summary>
    private static async Task<string> MintRunnerSecretAsync(HttpClient http, string name)
    {
        using var response = await http.PostAsJsonAsync("/api/runners", new CreateRunnerRequest(name), Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST /api/runners answered {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.GetProperty("secret").GetString()!;
    }

    private static async Task PostWorkLogAsync(
        HttpClient http, string key, string issueType, int fromStatusId, int toStatusId, DateTimeOffset start, DateTimeOffset end, string title)
    {
        var request = new WorkLogEntryRequest(
            Guid.NewGuid().ToString("N"), start, end, (long)(end - start).TotalMilliseconds,
            title, "what it did", false, 1, 0.5m, [],
            PlaybookFromStatusId: fromStatusId, PlaybookToStatusId: toStatusId, IssueType: issueType);
        await PostAsync<WorkLogEntryRequest, WorkLogEntryDto>(http, $"/api/issues/{key}/work-log", request);
    }

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

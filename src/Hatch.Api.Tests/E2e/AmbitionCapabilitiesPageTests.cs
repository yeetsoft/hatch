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
    private const string EpicsInFlight = ".hatch-issue-section:has(> summary:text-matches(\"^Epics in flight\"))";

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
            columns: section
              ? [...section.querySelectorAll('.hatch-capability-row')].map((row) =>
                  [...row.children].map((c) => {
                    const box = c.getBoundingClientRect();
                    return { left: box.left, top: box.top };
                  }))
              : [],
            asides: section
              ? [...section.querySelectorAll('.hatch-ambition-aside')].map((a) => {
                  const box = a.getBoundingClientRect();
                  const title = a.closest('li')?.querySelector('.hatch-progress-title')?.getBoundingClientRect();
                  return { left: box.left, top: box.top, titleBottom: title ? title.bottom : 0 };
                })
              : [],
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
            var (ambition, epic, second) = await deadline.BoundAsync("filing the ambition", () => FileAmbitionAsync(baseUrl));

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
            AssertDated(measured);
            AssertColumns(measured, profile is not null);
            AssertFits(measured, profile is not null);

            // The epics under it: both open, each with the capabilities it serves,
            // and the count line. The badge row sits beside the title on a desk
            // and under it on a phone; nothing may scroll sideways.
            await harness.WaitForVisibleAsync(deadline, $"{EpicsInFlight} .hatch-progress-row");
            var inFlight = await harness.EvaluateAsync<JsonElement>(
                deadline, "measuring the Epics in flight section", MeasureScript, "Epics in flight");
            output.WriteLine($"{profileName} /issues/{ambition} epics: {inFlight}");
            await ScrollToAsync(harness, deadline, "Epics in flight");
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"epics-in-flight-{profileName}")}");

            var inFlightText = inFlight.GetProperty("text").GetString()!;
            Assert.Contains(epic, inFlightText);
            Assert.Contains(second, inFlightText);
            Assert.Contains("2 open, runs 1 at once", inFlightText);
            Assert.Contains("#1", inFlightText);
            Assert.Contains("#2", inFlightText);
            Assert.Contains("#3", inFlightText);
            AssertAside(inFlight, 2, profile is not null);
            AssertFits(inFlight, profile is not null);

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

    /// <summary>
    /// HA-2551: each row that has a verdict holds exactly one <c>time</c> element with a
    /// non-empty <c>datetime</c>, text and title, and the not-checked row holds none.
    /// </summary>
    private static void AssertDated(JsonElement measured)
    {
        foreach (var row in measured.GetProperty("dates").EnumerateArray())
        {
            var state = row.GetProperty("state").GetString()!;
            var times = row.GetProperty("times").EnumerateArray().ToList();
            if (state == "Not checked")
            {
                Assert.True(times.Count == 0, $"the \"{state}\" row draws a date: {row}");
                continue;
            }

            Assert.True(times.Count == 1, $"the \"{state}\" row holds {times.Count} time elements, not one: {row}");
            foreach (var field in new[] { "dateTime", "text", "title" })
                Assert.False(
                    string.IsNullOrWhiteSpace(times[0].GetProperty(field).GetString()),
                    $"the \"{state}\" row's time has an empty {field}: {row}");
        }
    }

    /// <summary>
    /// HA-2703: on a desk the number, the statement, the state and what serves it start at
    /// one place on every row; on a phone each row stacks them in that order.
    /// </summary>
    private void AssertColumns(JsonElement measured, bool phone)
    {
        var rows = measured.GetProperty("columns").EnumerateArray()
            .Select(row => row.EnumerateArray().Select(c => (Left: c.GetProperty("left").GetDouble(), Top: c.GetProperty("top").GetDouble())).ToList())
            .ToList();
        Assert.Equal(4, rows.Count);
        foreach (var row in rows)
            Assert.True(row.Count == 4, $"a capability row holds {row.Count} pieces, not four: {measured.GetProperty("columns")}");

        output.WriteLine($"capability pieces, left then top, per row: {string.Join(" | ", rows.Select(r => string.Join(", ", r.Select(c => $"{c.Left:0.#}/{c.Top:0.#}"))))}");

        if (phone)
        {
            foreach (var row in rows)
                for (var i = 1; i < row.Count; i++)
                    Assert.True(row[i].Top > row[i - 1].Top + Tolerance, $"piece {i + 1} does not sit under piece {i} on the phone: {measured.GetProperty("columns")}");
            return;
        }

        for (var piece = 0; piece < 4; piece++)
            foreach (var row in rows.Skip(1))
                Assert.True(
                    Math.Abs(row[piece].Left - rows[0][piece].Left) <= Tolerance,
                    $"piece {piece + 1} starts in a different place on two rows: {measured.GetProperty("columns")}");
    }

    /// <summary>
    /// HA-2703: the right-hand group starts at one place on every row on a desk, and drops
    /// under the title on a phone.
    /// </summary>
    private void AssertAside(JsonElement measured, int expected, bool phone)
    {
        var asides = measured.GetProperty("asides").EnumerateArray()
            .Select(a => (Left: a.GetProperty("left").GetDouble(), Top: a.GetProperty("top").GetDouble(), TitleBottom: a.GetProperty("titleBottom").GetDouble()))
            .ToList();
        Assert.Equal(expected, asides.Count);
        output.WriteLine($"right-hand groups, left/top: {string.Join(", ", asides.Select(a => $"{a.Left:0.#}/{a.Top:0.#}"))}");

        foreach (var a in asides)
        {
            if (phone)
                Assert.True(a.Top >= a.TitleBottom - Tolerance, $"the right-hand group does not drop under the title on the phone: {measured.GetProperty("asides")}");
            else
                Assert.True(Math.Abs(a.Left - asides[0].Left) <= Tolerance, $"the right-hand group starts in a different place on two rows: {measured.GetProperty("asides")}");
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
    /// A project, an ambition with four capabilities - holds, does not hold,
    /// cannot tell and not checked - and two epics under it, the first serving
    /// the first capability and the second the next two.
    /// Returns the ambition's key and the two epics'.
    /// </summary>
    private static async Task<(string Ambition, string Epic, string Second)> FileAmbitionAsync(string baseUrl)
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

        var second = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "epic", "Order notices for staff", null, ambition.Key, null, null));

        using var putSecond = await http.PutAsJsonAsync($"/api/issues/{second.Key}/serves", new IssueServesWriteRequest([ids[1], ids[2]]), Web);
        var putSecondText = await putSecond.Content.ReadAsStringAsync();
        Assert.True(putSecond.IsSuccessStatusCode, $"PUT /api/issues/{second.Key}/serves answered {(int)putSecond.StatusCode}: {putSecondText}");

        return (ambition.Key, epic.Key, second.Key);
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2570's acceptance, rendered: the Ambitions page draws two ambitions in
/// two projects, every count non-zero somewhere, and a question waiting above
/// the rows. Run at the desktop viewport in Chromium and at the
/// <c>iPhone 14 Pro</c> profile in WebKit, where nothing may scroll sideways
/// once the strip and each row are brought into view.
///
/// <para>The first ambition has four capabilities that land in all four
/// segments of its bar - one held, one served by an epic that waits on a
/// question, one served by an epic that runs, one served by nothing - and runs
/// two epics at a limit of one. The second sits in another project, runs one
/// epic filed in the first project at a limit of two, and has a capability
/// whose newest verdict does not hold. Everything is filed over plain HTTP,
/// rather than anything written straight to the database.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class AmbitionsPageTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    /// <summary>Sub-pixel layout is fractional; two boxes that touch are not overlapping.</summary>
    private const double Tolerance = 0.5;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>What the seed filed, and the column names read back for its epics, so no assertion guesses the default column.</summary>
    private sealed record Seeded(
        string AmbitionA, string TitleA, string AmbitionB, string TitleB,
        string Epic1, string Epic2, string Epic3,
        string EpicTitle1, string EpicTitle2, string EpicTitle3,
        string EpicColumn1, string EpicColumn2, string EpicColumn3);

    /// <summary>
    /// Every row, the strip and the page's width against the viewport's, read in
    /// one script so they are one moment. A row is found by its title, never by
    /// its place.
    /// </summary>
    private const string MeasureScript = """
        () => {
          const texts = (root, selector) => [...root.querySelectorAll(selector)].map((e) => (e.textContent ?? '').trim());
          const strip = document.querySelector('.hatch-ambition-strip');
          const place = (e) => {
            if (!e) return null;
            const r = e.getBoundingClientRect();
            return { left: r.left, top: r.top };
          };
          const edges = (e) => {
            const r = e.getBoundingClientRect();
            return { left: r.left, right: r.right };
          };
          return {
            strip: strip
              ? {
                  edges: edges(strip),
                  heading: strip.querySelector('h2')?.textContent?.trim() ?? '',
                  text: strip.textContent ?? '',
                  hrefs: [...strip.querySelectorAll('a')].map((a) => a.getAttribute('href') ?? ''),
                  strong: texts(strip, 'strong'),
                  keys: texts(strip, '.hatch-plan-key'),
                }
              : null,
            rows: [...document.querySelectorAll('.hatch-ambition-row')].map((row) => ({
              edges: edges(row),
              title: row.querySelector('.hatch-ambition-title')?.textContent?.trim() ?? '',
              project: row.querySelector('.hatch-plan-project')?.textContent?.trim() ?? '',
              headings: texts(row, 'h3'),
              legend: texts(row, '.hatch-ambition-legend li'),
              parts: [...row.querySelectorAll(':scope > .hatch-ambition-part')].map(place),
              firstLines: [row.querySelector('.hatch-ambition-title'), ...row.querySelectorAll('h3')].map(place),
              pills: [...row.querySelectorAll('.hatch-ambition-epics .hatch-epics-in-flight-meta > :first-child')].map(place),
              firstEpicBorderTop: (() => {
                const first = row.querySelector('.hatch-ambition-epics > li');
                return first ? parseFloat(getComputedStyle(first).borderTopWidth) : null;
              })(),
              note: texts(row, 'p.hatch-ambition-tally').find((t) => t.includes('open, runs')) ?? '',
              epics: [...row.querySelectorAll('.hatch-ambition-epics li')].map((li) => ({
                key: li.querySelector('.hatch-plan-key')?.textContent?.trim() ?? '',
                title: li.querySelector('.hatch-progress-title')?.textContent?.trim() ?? '',
                meta: li.querySelector('.hatch-epics-in-flight-meta')?.textContent?.trim() ?? '',
              })),
            })),
            innerWidth: window.innerWidth,
            scrollWidth: document.documentElement.scrollWidth,
          };
        }
        """;

    /// <summary>
    /// Every row's title as it is drawn under one theme, beside what the app's
    /// ink resolves to there: a probe reads the token, so no colour is written
    /// down here.
    /// </summary>
    private const string TitleStyleScript = """
        (theme) => {
          document.documentElement.setAttribute('data-theme', theme);
          const probe = document.body.appendChild(document.createElement('span'));
          probe.style.color = 'var(--ink)';
          const ink = getComputedStyle(probe).color;
          probe.remove();
          return {
            ink,
            titles: [...document.querySelectorAll('.hatch-ambition-title')].map((a) => {
              const s = getComputedStyle(a);
              return { color: s.color, decoration: s.textDecorationLine };
            }),
          };
        }
        """;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheAmbitions_RenderAtTheDesktopProfile() => RunAsync("AmbitionsDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheAmbitions_RenderAtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("AmbitionsPhone", "phone", DeviceProfile.IPhone14Pro);
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
            var seeded = await deadline.BoundAsync("filing two ambitions in two projects", () => SeedAsync(baseUrl));

            await harness.OpenAsync(deadline, baseUrl, "/ambitions");
            await harness.WaitForVisibleAsync(deadline, ".hatch-ambition-strip");
            await harness.WaitForVisibleAsync(deadline, ".hatch-ambition-row");
            await harness.WaitForFunctionAsync(
                deadline, "waiting for both ambition rows", "() => document.querySelectorAll('.hatch-ambition-row').length === 2");

            var measured = await harness.EvaluateAsync<JsonElement>(deadline, "measuring the Ambitions page", MeasureScript);
            output.WriteLine($"{profileName} /ambitions: {measured}");
            output.WriteLine($"saved {await harness.FullPageScreenshotAsync(deadline, $"ambitions-{profileName}")}");

            var rows = measured.GetProperty("rows").EnumerateArray().ToList();
            Assert.Equal(2, rows.Count);
            var rowA = rows.Single(r => r.GetProperty("title").GetString() == seeded.TitleA);
            var rowB = rows.Single(r => r.GetProperty("title").GetString() == seeded.TitleB);
            AssertRowA(rowA, seeded);
            AssertRowB(rowB, seeded);
            if (profile is null)
                AssertRowsLineUp(rowA, rowB);
            else
                AssertRowsStack(rowA, rowB);
            AssertStrip(measured.GetProperty("strip"), seeded);
            AssertRowsMeetTheStrip(measured.GetProperty("strip"), rows);
            AssertFits(measured, profile is not null);

            // Nothing may scroll sideways once the strip and each row are the
            // thing at the top of the view.
            AssertFits(await ScrollIntoViewAsync(harness, deadline, ".hatch-ambition-strip", 0), profile is not null);
            AssertFits(await ScrollIntoViewAsync(harness, deadline, ".hatch-ambition-row", 0), profile is not null);
            AssertFits(await ScrollIntoViewAsync(harness, deadline, ".hatch-ambition-row", 1), profile is not null);

            // Last, since it sets the theme by hand and leaves it set.
            foreach (var theme in new[] { "light", "dark" })
            {
                var style = await harness.EvaluateAsync<JsonElement>(deadline, $"reading the titles in the {theme} theme", TitleStyleScript, theme);
                output.WriteLine($"{profileName} /ambitions {theme} titles: {style}");
                output.WriteLine($"saved {await harness.FullPageScreenshotAsync(deadline, $"ambitions-{profileName}-{theme}")}");
                AssertTitlesAreTheApps(style, theme);
            }

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

    /// <summary>The first ambition: four capabilities, one in each segment, and two epics at a limit of one.</summary>
    private static void AssertRowA(JsonElement row, Seeded s)
    {
        Assert.Equal("AMA", row.GetProperty("project").GetString());
        Assert.Equal(["4 capabilities", "In flight, 2 of 1"], Strings(row, "headings"));
        Assert.Equal(["1 hold", "1 in flight", "1 waiting on a person", "1 not started"], Strings(row, "legend"));
        Assert.Equal("2 open, runs 1 at once", row.GetProperty("note").GetString());

        var epics = row.GetProperty("epics").EnumerateArray().ToList();
        Assert.Equal(2, epics.Count);
        AssertEpic(epics.Single(e => e.GetProperty("key").GetString() == s.Epic1), s.EpicTitle1, s.EpicColumn1, "#2");
        AssertEpic(epics.Single(e => e.GetProperty("key").GetString() == s.Epic2), s.EpicTitle2, s.EpicColumn2, "#3");
    }

    /// <summary>The second ambition, in the other project: two capabilities, one epic filed in the first project, a limit of two.</summary>
    private static void AssertRowB(JsonElement row, Seeded s)
    {
        Assert.Equal("AMB", row.GetProperty("project").GetString());
        Assert.Equal(["2 capabilities", "In flight, 1 of 2"], Strings(row, "headings"));
        Assert.Equal(["1 in flight", "1 not started"], Strings(row, "legend"));
        Assert.Equal("1 open, runs 2 at once", row.GetProperty("note").GetString());

        var epic = Assert.Single(row.GetProperty("epics").EnumerateArray());
        Assert.StartsWith("AMA-", epic.GetProperty("key").GetString());
        AssertEpic(epic, s.EpicTitle3, s.EpicColumn3, "#2");
    }

    /// <summary>HA-2839: each row's left and right edges are the strip's, from the two elements' own boxes.</summary>
    private void AssertRowsMeetTheStrip(JsonElement strip, IReadOnlyList<JsonElement> rows)
    {
        var (left, right) = Edges(strip);
        foreach (var row in rows)
        {
            var (rowLeft, rowRight) = Edges(row);
            output.WriteLine($"edges: strip {left} to {right}, row {rowLeft} to {rowRight}");
            Assert.True(
                Math.Abs(rowLeft - left) <= Tolerance && Math.Abs(rowRight - right) <= Tolerance,
                $"a row runs from {rowLeft} to {rowRight} and the strip from {left} to {right}");
        }
    }

    private static (double Left, double Right) Edges(JsonElement element)
    {
        var edges = element.GetProperty("edges");
        return (edges.GetProperty("left").GetDouble(), edges.GetProperty("right").GetDouble());
    }

    /// <summary>HA-2839: at rest a title is drawn in the app's ink with no underline, not in the browser's style for a link.</summary>
    private static void AssertTitlesAreTheApps(JsonElement style, string theme)
    {
        var ink = style.GetProperty("ink").GetString();
        var titles = style.GetProperty("titles").EnumerateArray().ToList();
        Assert.Equal(2, titles.Count);
        Assert.All(titles, title =>
        {
            Assert.True(
                title.GetProperty("color").GetString() == ink,
                $"a title in the {theme} theme is {title.GetProperty("color")} and the ink is {ink}");
            Assert.Equal("none", title.GetProperty("decoration").GetString());
        });
    }

    /// <summary>
    /// HA-2701 at the desktop profile: each part starts at the same left on
    /// both rows, the three first lines share a top within a row, the column
    /// pills of one row's epics share a left, and the first epic line draws no
    /// rule above it. The numbers go to the output so a reader sees them.
    /// </summary>
    private void AssertRowsLineUp(JsonElement rowA, JsonElement rowB)
    {
        var partsA = Places(rowA, "parts");
        var partsB = Places(rowB, "parts");
        Assert.Equal(3, partsA.Count);
        Assert.Equal(3, partsB.Count);
        output.WriteLine($"part lefts, row A: {string.Join(", ", partsA.Select(p => p.Left))}; row B: {string.Join(", ", partsB.Select(p => p.Left))}");

        for (var i = 0; i < 3; i++)
            Assert.True(
                Math.Abs(partsA[i].Left - partsB[i].Left) <= Tolerance,
                $"part {i} starts at {partsA[i].Left} on the first row and {partsB[i].Left} on the second");
        Assert.True(partsA[0].Left < partsA[1].Left && partsA[1].Left < partsA[2].Left, "the three parts do not sit side by side");

        foreach (var (name, row) in new[] { ("A", rowA), ("B", rowB) })
        {
            var tops = Places(row, "firstLines");
            Assert.Equal(3, tops.Count);
            output.WriteLine($"first line tops, row {name}: {string.Join(", ", tops.Select(p => p.Top))}");
            Assert.All(tops, p => Assert.True(Math.Abs(p.Top - tops[0].Top) <= Tolerance, $"the first lines of row {name} are not level: {string.Join(", ", tops.Select(t => t.Top))}"));

            Assert.Equal(0, row.GetProperty("firstEpicBorderTop").GetDouble());
        }

        var pills = Places(rowA, "pills");
        Assert.Equal(2, pills.Count);
        output.WriteLine($"pill lefts, row A: {string.Join(", ", pills.Select(p => p.Left))}");
        Assert.All(pills, p => Assert.True(Math.Abs(p.Left - pills[0].Left) <= Tolerance, $"the column pills of row A start at {string.Join(", ", pills.Select(t => t.Left))}"));
    }

    /// <summary>HA-2701 at the phone profile: head, capabilities, epics in flight, each below the one before and at the same left.</summary>
    private void AssertRowsStack(JsonElement rowA, JsonElement rowB)
    {
        foreach (var (name, row) in new[] { ("A", rowA), ("B", rowB) })
        {
            var parts = Places(row, "parts");
            Assert.Equal(3, parts.Count);
            output.WriteLine($"part places, row {name}: {string.Join("; ", parts.Select(p => $"({p.Left}, {p.Top})"))}");
            Assert.True(parts[1].Top > parts[0].Top && parts[2].Top > parts[1].Top, $"the parts of row {name} do not stack head, capabilities, epics");
            Assert.All(parts, p => Assert.True(Math.Abs(p.Left - parts[0].Left) <= Tolerance, $"the parts of row {name} do not share a left"));
        }
    }

    private readonly record struct Place(double Left, double Top);

    private static List<Place> Places(JsonElement row, string property) =>
        row.GetProperty(property).EnumerateArray()
            .Select(e => new Place(e.GetProperty("left").GetDouble(), e.GetProperty("top").GetDouble()))
            .ToList();

    private static void AssertEpic(JsonElement epic, string title, string column, string badge)
    {
        Assert.Equal(title, epic.GetProperty("title").GetString());
        var meta = epic.GetProperty("meta").GetString()!;
        Assert.Contains(column, meta);
        Assert.Contains(badge, meta);
    }

    /// <summary>One decision waiting, on the first ambition and the epic carrying the question, linking to the ballot; the second ambition is not in it.</summary>
    private static void AssertStrip(JsonElement strip, Seeded s)
    {
        Assert.Equal("1 decision waiting on you", strip.GetProperty("heading").GetString());
        Assert.Equal([s.TitleA], Strings(strip, "strong"));
        Assert.Equal([s.Epic1], Strings(strip, "keys"));
        Assert.DoesNotContain(s.TitleB, strip.GetProperty("text").GetString());
        Assert.All(Strings(strip, "hrefs"), href => Assert.EndsWith("/ballot", href));
        Assert.NotEmpty(Strings(strip, "hrefs"));
    }

    private static List<string> Strings(JsonElement element, string property) =>
        element.GetProperty(property).EnumerateArray().Select(e => e.GetString()!).ToList();

    /// <summary>Brings the <paramref name="index"/>th match of <paramref name="selector"/> to the top of the viewport and reads the page's width against it, so the check is made with that part in view.</summary>
    private static Task<JsonElement> ScrollIntoViewAsync(E2eHarness harness, Deadline deadline, string selector, int index) =>
        harness.EvaluateAsync<JsonElement>(deadline, $"scrolling {selector} #{index} into view", """
            ([sel, i]) => {
              document.querySelectorAll(sel)[i]?.scrollIntoView({ block: 'start' });
              return { innerWidth: window.innerWidth, scrollWidth: document.documentElement.scrollWidth };
            }
            """, new object[] { selector, index });

    /// <summary>The page may not scroll sideways on the phone; on the desktop the same check costs nothing, so it is made there too.</summary>
    private static void AssertFits(JsonElement measured, bool phone)
    {
        var innerWidth = measured.GetProperty("innerWidth").GetDouble();
        Assert.True(
            measured.GetProperty("scrollWidth").GetDouble() <= innerWidth + Tolerance,
            $"the page scrolls sideways{(phone ? " on the phone" : "")}: {measured}");
    }

    /// <summary>
    /// Two projects. The first ambition, in the first, with four capabilities:
    /// the first holds, the second is served by an epic carrying a question,
    /// the third by an epic with none, the fourth by nothing. The second
    /// ambition, in the second project, with two: the first does not hold and
    /// nothing serves it, the second is served by an epic filed in the first
    /// project; its limit is two. Every epic is left in the column it was filed
    /// in, whose name is read back from the statuses.
    /// </summary>
    private static async Task<Seeded> SeedAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var statuses = await GetAsync<StatusDto[]>(http, "/api/statuses");
        string ColumnOf(IssueDto issue) => statuses.Single(s => s.Id == issue.StatusId).Name;

        var projectA = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("AMA", "AMA"));
        var projectB = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("AMB", "AMB"));

        const string titleA = "Partners order without a phone call";
        const string titleB = "Staff see every order at a glance";

        var a = await PostAsync<IssueCreateRequest, IssueDto>(http, "/api/issues", new(projectA.Id, "ambition", titleA, null, null, null, null));
        var capsA = new List<long>();
        foreach (var (statement, howToTell) in new[]
        {
            ("Partners can place an order without a phone call", "Place one as a partner and count the calls"),
            ("An order is confirmed within a minute", "Time an order from the button to the email"),
            ("A partner sees only their own orders", "Sign in as two partners and compare the lists"),
            ("A partner can change an order until it ships", "Edit one before it ships and again after"),
        })
            capsA.Add((await PostAsync<CapabilityCreateRequest, CapabilityDto>(http, $"/api/issues/{a.Key}/capabilities", new(statement, howToTell))).Id);

        await PostAsync<CapabilityVerdictRequest, CapabilityVerdictDto>(
            http, $"/api/issues/{a.Key}/capabilities/{capsA[0]}/verdicts", new("holds", "the staging flow", "aaa1111"));

        const string epicTitle1 = "A partner order form";
        const string epicTitle2 = "Order notices for staff";
        var epic1 = await PostAsync<IssueCreateRequest, IssueDto>(http, "/api/issues", new(projectA.Id, "epic", epicTitle1, null, a.Key, null, null));
        await ServesAsync(http, epic1.Key, capsA[1]);
        // A question's level is hold unless it says otherwise.
        await PostAsync<CommentCreateRequest, CommentDto>(http, $"/api/issues/{epic1.Key}/comments", new("Which form should it take?", "question"));

        var epic2 = await PostAsync<IssueCreateRequest, IssueDto>(http, "/api/issues", new(projectA.Id, "epic", epicTitle2, null, a.Key, null, null));
        await ServesAsync(http, epic2.Key, capsA[2]);

        var b = await PostAsync<IssueCreateRequest, IssueDto>(http, "/api/issues", new(projectB.Id, "ambition", titleB, null, null, null, null));
        var capsB = new List<long>();
        foreach (var (statement, howToTell) in new[]
        {
            ("Staff see a new order the moment it lands", "File one and watch the staff list"),
            ("Staff can filter orders by partner", "Pick a partner and count the rows"),
        })
            capsB.Add((await PostAsync<CapabilityCreateRequest, CapabilityDto>(http, $"/api/issues/{b.Key}/capabilities", new(statement, howToTell))).Id);

        await PostAsync<CapabilityVerdictRequest, CapabilityVerdictDto>(
            http, $"/api/issues/{b.Key}/capabilities/{capsB[0]}/verdicts", new("does_not_hold", "the staff list", "bbb2222"));

        const string epicTitle3 = "A live list for staff";
        var epic3 = await PostAsync<IssueCreateRequest, IssueDto>(http, "/api/issues", new(projectA.Id, "epic", epicTitle3, null, b.Key, null, null));
        await ServesAsync(http, epic3.Key, capsB[1]);

        using var wip = await http.PatchAsJsonAsync($"/api/issues/{b.Key}/wip", new IssueWipLimitRequest("2"), Web);
        var wipText = await wip.Content.ReadAsStringAsync();
        Assert.True(wip.IsSuccessStatusCode, $"PATCH /api/issues/{b.Key}/wip answered {(int)wip.StatusCode}: {wipText}");

        return new Seeded(
            a.Key, titleA, b.Key, titleB, epic1.Key, epic2.Key, epic3.Key,
            epicTitle1, epicTitle2, epicTitle3, ColumnOf(epic1), ColumnOf(epic2), ColumnOf(epic3));
    }

    private static async Task ServesAsync(HttpClient http, string epicKey, long capabilityId)
    {
        using var response = await http.PutAsJsonAsync($"/api/issues/{epicKey}/serves", new IssueServesWriteRequest([capabilityId]), Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"PUT /api/issues/{epicKey}/serves answered {(int)response.StatusCode}: {text}");
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

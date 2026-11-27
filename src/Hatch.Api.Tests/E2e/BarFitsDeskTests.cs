using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2447's acceptance, measured: between the phone breakpoint and a wide
/// desk the five-entry nav and the bar's right-hand controls never overlap by
/// box, the five entries share one line, none is clipped, and nothing
/// scrolls sideways - with the bar at rest and in its widest state (a battery,
/// an unseen count on the overnight egg, four attention pills) and the scope
/// control drawn, since it is the widest thing in the bar's leading side.
///
/// <para>A bounds-only check passes on the failure this guards: the overlapping
/// entries all lie inside the bar. So every pair of boxes is compared.</para>
///
/// <para>Where the bar draws one row (1280 and up, and the phone, which has no
/// nav) the brand and the gear share a top; between, the controls may drop to a
/// second row. The case prints each measurement the way
/// <see cref="BarFitsPhoneTests"/> does, so it can go on the ticket.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class BarFitsDeskTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 120;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    /// <summary>Sub-pixel layout is fractional; two boxes that touch are not overlapping.</summary>
    private const double Tolerance = 0.5;

    /// <summary>How far one box's top may sit from another's before it counts as on another line; a control is 36px high.</summary>
    private const double SameLineTolerance = 8;

    /// <summary>The narrowest the brand may be and still show its mark: the 28px mark and the link's padding.</summary>
    private const double MinimumBrand = 44;

    /// <summary>The widths the nav has to fit at: one over the phone breakpoint, two between, and a wide desk.</summary>
    private static readonly int[] Widths = [641, 800, 1000, 1280];

    /// <summary>
    /// From this width up the bar fits on one row. With the scope control drawn,
    /// the brand, nav, scope control and the widest trailing side need about
    /// 1140px, and about 1040px at rest; below that the trailing controls wrap
    /// onto a second row by design.
    /// </summary>
    private const int OneRowFrom = 1280;

    private const int Height = 800;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheNav_ClearsTheBarsControls_AcrossTheDeskWidths()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("BarFitsDesk");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            await deadline.BoundAsync("filing two projects", () => FileProjectsAsync(baseUrl));

            var violations = new List<string>();

            await harness.OpenAsync(deadline, baseUrl, "/ballot", BarFitsPhoneTests.StubTheSessionReads);
            await harness.WaitForTopBarAsync(deadline);
            foreach (var width in Widths)
                await MeasureAsync(harness, deadline, width, "widest", widest: true, violations);

            await harness.UnrouteAllAsync(deadline);
            await harness.GoToAsync(deadline, "ballot");
            foreach (var width in Widths)
                await MeasureAsync(harness, deadline, width, "at rest", widest: false, violations);

            // The phone draws no nav and no wrap: the bar is still one row, in its widest state too.
            await MeasurePhoneAsync(harness, deadline, "at rest", violations);
            await harness.OpenAsync(deadline, baseUrl, "/ballot", BarFitsPhoneTests.StubTheSessionReads);
            await MeasurePhoneAsync(harness, deadline, "widest", violations);

            Assert.True(
                violations.Count == 0,
                $"The nav runs into the bar ({violations.Count} violation(s)):{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");

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

    private async Task MeasureAsync(E2eHarness harness, Deadline deadline, int width, string state, bool widest, List<string> violations)
    {
        await harness.SetViewportAsync(deadline, width, Height);
        await WaitForTheBarAsync(harness, deadline, widest);

        var nav = await harness.MeasureNavAsync(deadline);
        output.WriteLine($"--- {width}, {state}{Environment.NewLine}{nav}");
        await harness.ScreenshotAsync(deadline, $"{width}-{state.Replace(' ', '-')}");
        violations.AddRange(Check($"{width}, {state}", widest, nav, oneRow: width >= OneRowFrom));
    }

    private async Task MeasurePhoneAsync(E2eHarness harness, Deadline deadline, string state, List<string> violations)
    {
        await harness.SetViewportAsync(deadline, 393, 852);
        await WaitForTheBarAsync(harness, deadline, widest: state == "widest", nav: false);

        var nav = await harness.MeasureNavAsync(deadline);
        output.WriteLine($"--- 393 phone, {state}{Environment.NewLine}{nav}");
        await harness.ScreenshotAsync(deadline, $"393-{state.Replace(' ', '-')}");

        var where = $"393 phone, {state}";
        if (nav.Controls["brand"] is not { } brand || nav.Controls["gear"] is not { } gear)
            violations.Add($"{where}: the brand or the gear is absent");
        else if (Math.Abs(brand.Y - gear.Y) > SameLineTolerance)
            violations.Add($"{where}: the bar is not one row: brand ({brand}) and gear ({gear}) sit on different lines");
        if (nav.ScrollWidth != nav.ClientWidth)
            violations.Add($"{where}: the document scrolls sideways (scrollWidth {nav.ScrollWidth} != clientWidth {nav.ClientWidth})");
    }

    private static async Task WaitForTheBarAsync(E2eHarness harness, Deadline deadline, bool widest, bool nav = true)
    {
        await harness.WaitForTopBarAsync(deadline);
        if (nav)
        {
            await harness.WaitForVisibleAsync(deadline, ".hatch-primary-nav");
            await harness.WaitForVisibleAsync(deadline, E2eHarness.ScopeSelector);
        }
        await harness.WaitForVisibleAsync(deadline, E2eHarness.AttentionSelector);
        await harness.WaitForVisibleAsync(deadline, E2eHarness.OvernightSelector);
        if (widest)
        {
            await harness.WaitForVisibleAsync(deadline, E2eHarness.BatterySelector);
            await harness.WaitForVisibleAsync(deadline, ".hatch-overnight-control.hatch-overnight-asking");
            await harness.WaitForVisibleAsync(deadline, ".hatch-attention-control.hatch-attention-asking");
        }
    }

    /// <summary>The acceptance against one measurement. Names the width, the state and the two boxes.</summary>
    private static List<string> Check(string where, bool widest, NavMeasurement nav, bool oneRow)
    {
        var found = new List<string>();

        if (nav.ScrollWidth != nav.ClientWidth)
            found.Add($"{where}: the document scrolls sideways (scrollWidth {nav.ScrollWidth} != clientWidth {nav.ClientWidth})");

        if (nav.Entries.Count != 5)
            found.Add($"{where}: the nav has {nav.Entries.Count} entries, not five: {string.Join(", ", nav.Entries.Select(e => e.Name))}");

        if (nav.Controls["scope"] is null)
            found.Add($"{where}: the scope control is absent, so the bar never reached its widest leading side");
        if (widest && nav.Controls["battery"] is null)
            found.Add($"{where}: the battery is absent, so the bar never reached its widest state");

        var boxes = nav.Entries.Select(e => ($"nav \"{e.Name}\"", e.Box))
            .Concat(nav.Controls.Where(c => c.Value is not null).Select(c => (c.Key, c.Value!)))
            .ToList();

        foreach (var (name, box) in boxes)
            if (box.X < -Tolerance || box.Right > nav.InnerWidth + Tolerance)
                found.Add($"{where}: {name} ({box}) lies outside [0, {nav.InnerWidth}]");

        foreach (var entry in nav.Entries)
            if (entry.ScrollWidth > entry.ClientWidth + 1)
                found.Add($"{where}: nav \"{entry.Name}\" is clipped ({entry})");

        for (var i = 0; i < boxes.Count; i++)
            for (var j = i + 1; j < boxes.Count; j++)
            {
                var (a, b) = (boxes[i], boxes[j]);
                var across = Math.Min(a.Item2.Right, b.Item2.Right) - Math.Max(a.Item2.X, b.Item2.X);
                var down = Math.Min(a.Item2.Bottom, b.Item2.Bottom) - Math.Max(a.Item2.Y, b.Item2.Y);
                if (across > Tolerance && down > Tolerance)
                    found.Add($"{where}: {a.Item1} ({a.Item2}) overlaps {b.Item1} ({b.Item2})");
            }

        if (nav.Entries.Count > 0)
        {
            var top = nav.Entries[0].Box.Y;
            foreach (var entry in nav.Entries.Skip(1).Where(e => Math.Abs(e.Box.Y - top) > SameLineTolerance))
                found.Add($"{where}: nav \"{entry.Name}\" is not on the first entry's line ({entry.Box} against y={top})");
        }

        if (nav.Controls["brand"] is { } brand)
        {
            if (brand.Width < MinimumBrand - Tolerance)
                found.Add($"{where}: the brand ({brand}) is under {MinimumBrand}px wide, so its mark is squeezed");

            if (oneRow && nav.Controls["gear"] is { } gear && Math.Abs(brand.Y - gear.Y) > SameLineTolerance)
                found.Add($"{where}: the bar is not one row: brand ({brand}) and gear ({gear}) sit on different lines");
        }

        return found;
    }

    /// <summary>Two projects, so <c>ScopeControl</c> is drawn (it is absent with fewer), and an issue under one so the board and ballot have something to read.</summary>
    private static async Task FileProjectsAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        foreach (var key in new[] { "DKA", "DKB" })
        {
            var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new(key, key));
            await PostAsync<IssueCreateRequest, IssueDto>(
                http, "/api/issues", new(project.Id, "task", $"A task in {key}", null, null, null, null));
        }
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

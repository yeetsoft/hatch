using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Api.Tests.Hatch;
using Microsoft.Playwright;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-1047's acceptance, measured: the top bar in its widest state - a battery
/// drawing a three-figure percentage, an unseen count on the overnight egg,
/// and all four attention pills with two-digit counts - fits an iPhone 14 Pro
/// viewport on the board, the plan page and an issue page, and so does the bar
/// at rest.
///
/// <para>A scratch database draws the narrowest bar, so the widest state is
/// reached by fulfilling the bar's three session reads with
/// <c>page.RouteAsync</c> - serialised from the real
/// records, so a renamed field breaks this build rather than the bar - and
/// leaving every other route, and the bundle, real. The same context is then
/// unrouted and the three pages are measured again at rest.</para>
///
/// <para>Every violation across all six measurements is collected and asserted
/// together, so one run shows every failure rather than the first. The 44px
/// rule (AC4) is asserted here because <c>(pointer: coarse)</c> matches under
/// the descriptor; a run where it does not says so rather than passing.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class BarFitsPhoneTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    /// <summary>The smallest target a coarse pointer should be asked to hit, in CSS pixels.</summary>
    private const double MinimumTarget = 44;

    /// <summary>Sub-pixel layout is fractional; two boxes that touch are not overlapping.</summary>
    private const double Tolerance = 0.5;

    /// <summary>Rows per section: two digits in every pill.</summary>
    private const int RowsPerSection = 11;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheWidestBar_FitsThePhoneOnBoardIssueAndPlan()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("BarFitsPhone");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline, DeviceProfile.IPhone14Pro);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            var issueKey = await deadline.BoundAsync("filing an issue", () => FileAnIssueAsync(baseUrl));

            var violations = new List<string>();

            // The widest state: the three session reads are ours, everything else is real.
            await harness.OpenAsync(deadline, baseUrl, "/apps/hatch/", StubTheSessionReads);
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForFirstColumnAsync(deadline);
            await MeasureAsync(harness, deadline, "board", "widest", widest: true, violations);

            await harness.GoToAsync(deadline, "plan");
            await harness.WaitForVisibleAsync(deadline, ".hatch-page");
            await MeasureAsync(harness, deadline, "plan", "widest", widest: true, violations);

            await harness.GoToAsync(deadline, $"issues/{issueKey}");
            await harness.WaitForVisibleAsync(deadline, ".hatch-issue-page");
            await MeasureAsync(harness, deadline, "issue", "widest", widest: true, violations);

            // The same three pages at rest, against the real host's own answers.
            await harness.UnrouteAllAsync(deadline);

            await harness.GoToAsync(deadline, "");
            await harness.WaitForFirstColumnAsync(deadline);
            await MeasureAsync(harness, deadline, "board", "at rest", widest: false, violations);

            await harness.GoToAsync(deadline, "plan");
            await harness.WaitForVisibleAsync(deadline, ".hatch-page");
            await MeasureAsync(harness, deadline, "plan", "at rest", widest: false, violations);

            await harness.GoToAsync(deadline, $"issues/{issueKey}");
            await harness.WaitForVisibleAsync(deadline, ".hatch-issue-page");
            await MeasureAsync(harness, deadline, "issue", "at rest", widest: false, violations);

            Assert.True(
                violations.Count == 0,
                $"The bar does not fit the phone ({violations.Count} violation(s)):{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");

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

    /// <summary>
    /// Waits for the controls the state is about, measures, prints the
    /// measurement, and adds what is wrong with it. The widest state waits on
    /// the controls' <em>tone</em> classes, not on their pills' markup, so the
    /// wait survives the pills being drawn some other way at phone width.
    /// </summary>
    private async Task MeasureAsync(E2eHarness harness, Deadline deadline, string route, string state, bool widest, List<string> violations)
    {
        await harness.WaitForTopBarAsync(deadline);

        if (widest)
        {
            await harness.WaitForVisibleAsync(deadline, ".hatch-battery");
            await harness.WaitForVisibleAsync(deadline, ".hatch-overnight-control.hatch-overnight-asking");
            await harness.WaitForVisibleAsync(deadline, ".hatch-attention-control.hatch-attention-asking");
        }
        else
        {
            await harness.WaitForVisibleAsync(deadline, E2eHarness.AttentionSelector);
            await harness.WaitForVisibleAsync(deadline, E2eHarness.OvernightSelector);
        }

        var bar = await harness.MeasureBarAsync(deadline);
        output.WriteLine($"--- {route}, {state}{Environment.NewLine}{bar}");
        violations.AddRange(Check(route, state, widest, bar));
    }

    /// <summary>AC1, AC2 and AC4 against one measurement. Names the route, the state and the two controls, with their boxes.</summary>
    private static List<string> Check(string route, string state, bool widest, BarMeasurement bar)
    {
        var where = $"{route}, {state}";
        var found = new List<string>();

        if (bar.ScrollWidth != bar.ClientWidth)
            found.Add($"{where}: the document scrolls sideways (scrollWidth {bar.ScrollWidth} != clientWidth {bar.ClientWidth})");

        if (widest && bar.Controls["battery"] is null)
            found.Add($"{where}: the battery is absent, so the bar never reached its widest state");

        if (!bar.CoarsePointerMatches)
            found.Add($"{where}: (pointer: coarse) does not match under the device descriptor, so the 44px rule cannot be asserted in the browser");

        var present = bar.Controls.Where(c => c.Value is not null).Select(c => (c.Key, Box: c.Value!)).ToList();

        foreach (var (name, box) in present)
        {
            if (box.X < -Tolerance || box.Right > bar.InnerWidth + Tolerance)
                found.Add($"{where}: {name} ({box}) lies outside [0, {bar.InnerWidth}]");

            // Width as well as height: a flex row that cannot hold its controls shrinks the brand
            // link below its own box rather than overflowing, and that is how the bar fails here.
            if (box.Height < MinimumTarget - Tolerance || box.Width < MinimumTarget - Tolerance)
                found.Add($"{where}: {name} ({box}) is under {MinimumTarget}px in a direction");
        }

        for (var i = 0; i < present.Count; i++)
            for (var j = i + 1; j < present.Count; j++)
            {
                var (a, b) = (present[i], present[j]);
                var across = Math.Min(a.Box.Right, b.Box.Right) - Math.Max(a.Box.X, b.Box.X);
                var down = Math.Min(a.Box.Bottom, b.Box.Bottom) - Math.Max(a.Box.Y, b.Box.Y);
                if (across > Tolerance && down > Tolerance)
                    found.Add($"{where}: {a.Key} ({a.Box}) overlaps {b.Key} ({b.Box})");
            }

        if (bar.Controls["gear"] is { } gear)
            foreach (var (name, box) in present.Where(p => p.Key != "gear"))
                if (box.Right > gear.Right + Tolerance)
                    found.Add($"{where}: {name} ({box}) ends right of the gear ({gear})");

        return found;
    }

    /// <summary>A project and an issue under it, filed the way any caller files them with the wall off: naming itself with the runner header.</summary>
    private static async Task<string> FileAnIssueAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        http.DefaultRequestHeaders.Add(LocalCaller.RunnerHeader, "e2e");

        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/hatch/projects", new("E2E", "E2E"));
        var issue = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/hatch/issues", new(project.Id, "task", "A task", null, null, null, null));
        return issue.Key;
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }

    /// <summary>
    /// The widest each read can draw: <c>100%</c> in the battery, two digits
    /// unseen on the egg, and two digits in each of the four attention pills.
    /// <c>ExhaustedRunners</c> stays null: its dot adds no width.
    /// </summary>
    private static async Task StubTheSessionReads(IBrowserContext context)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = Enumerable.Range(0, RowsPerSection).ToList();

        var utilization = new UtilizationReading(
            UtilizationStates.Ok, now, [new UtilizationLimit("session", "Session", 100, now.AddHours(1))]);

        var overnight = new OvernightDto(null, null, 0, [], Unseen: 99);

        var attention = new AttentionDto(
            Reviews: rows.Select(i => new ReviewDto($"E2E-{i}", "A review", "task", "https://example.invalid/pull/" + i)).ToList(),
            InReviewWithoutPullRequest: 0,
            Questions: rows.Select(i => new QuestionDto(i, $"E2E-{i}", "A question", "Which?", "e2e", now, null, [])).ToList(),
            Conflicts: [],
            TrunkBuilds: rows.Select(i => new TrunkBuildDto(
                i, "origin", "example/repo", "main", "0000000", now, "failing", [], now, "e2e", "e2e", null)).ToList(),
            Deadlocked: rows.Select(i => new DeadlockedDto($"E2E-{i}", "A deadlock", "task", "Waits on itself.")).ToList());

        await FulfilAsync(context, "**/api/hatch/utilization", utilization);
        await FulfilAsync(context, "**/api/hatch/overnight", overnight);
        await FulfilAsync(context, "**/api/hatch/attention", attention);
    }

    private static Task FulfilAsync<T>(IBrowserContext context, string pattern, T body) =>
        context.RouteAsync(pattern, route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(body, Web),
        }));
}

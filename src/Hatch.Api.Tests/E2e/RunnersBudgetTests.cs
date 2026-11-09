using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.Playwright;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-1698: the Runners page's Token budget column, rendered by a real browser
/// from five stubbed runners - no budget, one inside its caps, one near its cap,
/// one at it, and one throttled for going over its hourly cap.
///
/// <para>The text is read from the DOM, then the geometry: each bar's fill is
/// held to its percent of its own track, and the document must not scroll
/// sideways. At the phone width the table's wrapper scrolls on purpose, so only
/// the document is held. The screenshot is taken before the assertions, so a
/// failing case still leaves its picture; the colours and wrapping it shows are
/// for a person to read, not for the case to assert.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class RunnersBudgetTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    /// <summary>A fill is a percent of its track's content width; a pixel is the rounding the browser is allowed.</summary>
    private const double FillTolerance = 1;

    private const string NoBudget = "example-host.invalid:/e2e-budget-none";
    private const string OwnBudget = "example-host.invalid:/e2e-budget-own";
    private const string Warn = "example-host.invalid:/e2e-budget-warn";
    private const string AtCap = "example-host.invalid:/e2e-budget-at-cap";
    private const string Throttled = "example-host.invalid:/e2e-budget-throttled";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheRunnersBudgetCells_RenderAtDesktop()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        await RunAsync(null, "RunnersBudget", "runners-budget");
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheRunnersBudgetCells_RenderAtThePhoneProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);

        await RunAsync(DeviceProfile.IPhone14Pro, "RunnersBudgetPhone", "runners-budget-phone");
    }

    private async Task RunAsync(DeviceProfile? profile, string name, string screenshot)
    {
        var harness = new E2eHarness(name);
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline, profile);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            // The runners read is stubbed, but the host still serves the bundle.
            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);

            await harness.OpenAsync(deadline, baseUrl, "/apps/hatch/runners", StubRunners);
            await harness.WaitForVisibleAsync(deadline, ".hatch-token-budget-line");

            var path = await harness.ScreenshotAsync(deadline, screenshot);
            var measurement = await harness.MeasureRunnerBudgetsAsync(deadline);
            var where = profile is null ? "runners, desktop" : $"runners, {profile.Device}";
            output.WriteLine($"--- {where}{Environment.NewLine}screenshot: {path}{Environment.NewLine}{measurement}");

            var violations = Check(where, measurement);

            Assert.True(
                violations.Count == 0,
                $"A Runners token budget cell is wrong ({violations.Count} violation(s)):{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");

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

    /// <summary>Every way the measurement differs from what the five stubbed rows should draw.</summary>
    private static List<string> Check(string where, RunnerBudgetsMeasurement m)
    {
        var found = new List<string>();

        if (m.ScrollWidth != m.ClientWidth)
            found.Add($"{where}: the document scrolls sideways (scrollWidth {m.ScrollWidth} != clientWidth {m.ClientWidth})");

        var names = m.Rows.Select(r => r.Name).ToList();
        string[] expected = [NoBudget, OwnBudget, Warn, AtCap, Throttled];
        if (!expected.All(names.Contains))
        {
            found.Add($"{where}: expected the rows {string.Join(", ", expected)} but the page drew {string.Join(", ", names)}");
            return found;
        }

        RunnerBudgetRow Row(string runner) => m.Rows.Single(r => r.Name == runner);

        var none = Row(NoBudget);
        if (none.CellText != "No token budget") found.Add($"{where}: {NoBudget}: the cell reads \"{none.CellText}\", not \"No token budget\"");
        if (none.Bars.Count != 0) found.Add($"{where}: {NoBudget}: draws {none.Bars.Count} bar(s) with no budget");

        ExpectLines(found, where, Row(OwnBudget), ["1.2M / 2M tokens", "140k / 200k per hour"], [60, 70]);
        ExpectLines(found, where, Row(Warn), ["1.7M / 2M tokens"], [85]);
        ExpectLines(found, where, Row(AtCap), ["2M / 2M tokens"], [100]);
        ExpectLines(found, where, Row(Throttled), ["210k / 200k per hour"], [100]);

        var badge = Row(Throttled).Badge;
        if (!badge.StartsWith("Over 200k tokens/hour until", StringComparison.Ordinal))
            found.Add($"{where}: {Throttled}: the badge reads \"{badge}\", not \"Over 200k tokens/hour until ...\"");

        return found;
    }

    /// <summary>A row's lines are the words given, and each bar is the percent given of its own track, to a pixel.</summary>
    private static void ExpectLines(List<string> found, string where, RunnerBudgetRow row, string[] lines, double[] percents)
    {
        if (!row.Lines.SequenceEqual(lines))
            found.Add($"{where}: {row.Name}: the lines read [{string.Join(" | ", row.Lines)}], not [{string.Join(" | ", lines)}]");

        if (row.Bars.Count != percents.Length)
        {
            found.Add($"{where}: {row.Name}: draws {row.Bars.Count} bar(s), not {percents.Length}");
            return;
        }

        for (var i = 0; i < percents.Length; i++)
        {
            var bar = row.Bars[i];
            var wanted = percents[i] / 100 * bar.TrackWidth;

            if (bar.TrackWidth <= 0)
                found.Add($"{where}: {row.Name}: bar {i + 1} has a track {bar.TrackWidth}px wide");
            else if (Math.Abs(bar.FillWidth - wanted) > FillTolerance)
                found.Add($"{where}: {row.Name}: bar {i + 1} is {bar.FillWidth:0.##}px of a {bar.TrackWidth:0.##}px track, not {percents[i]}% ({wanted:0.##}px)");
        }
    }

    /// <summary>
    /// Five loops, each with its own invented name and no host, so no shared-host
    /// total row sits among them: no budget, a budget inside its caps (60% of the
    /// night's, 70% of the hour's), one at 85%, one at its cap with the line the
    /// loop stopped on, and one an hour into a throttle for going over its hourly
    /// cap. Nothing names a real host, path, repository or person.
    /// </summary>
    private static async Task StubRunners(IBrowserContext context)
    {
        var now = DateTimeOffset.UtcNow;

        RunnerDto Loop(
            string name,
            string? line = null,
            long? maxTokens = null,
            long? tokensUsed = null,
            long? maxTokensPerHour = null,
            long? tokensLastHour = null,
            DateTimeOffset? throttledUntil = null) => new(
            Name: name,
            Kind: "loop",
            FirstSeenAt: now.AddHours(-2),
            LastSeenAt: now,
            ClaimKey: null,
            Line: line,
            LineAt: line is null ? null : now,
            State: RunnerStates.Running,
            Under: null,
            MaxRuns: null,
            MaxSpend: null,
            UntilAt: null,
            GoneAfterSeconds: 300,
            Repositories: [],
            Clones: null,
            Mine: null,
            Standing: null,
            Where: null,
            Agent: null,
            Drives: [],
            Tags: [],
            Projects: [],
            CanControl: true,
            MaxTokens: maxTokens,
            MaxTokensPerHour: maxTokensPerHour,
            MaxTokensSource: maxTokens is null ? "none" : "run",
            MaxTokensPerHourSource: maxTokensPerHour is null ? "none" : "run",
            TokensUsed: tokensUsed,
            TokensLastHour: tokensLastHour,
            ThrottledUntil: throttledUntil);

        var runners = new[]
        {
            Loop(NoBudget),
            Loop(OwnBudget, maxTokens: 2_000_000, tokensUsed: 1_200_000, maxTokensPerHour: 200_000, tokensLastHour: 140_000),
            Loop(Warn, maxTokens: 2_000_000, tokensUsed: 1_700_000),
            Loop(AtCap, line: "Stopped: --max-tokens 2M reached", maxTokens: 2_000_000, tokensUsed: 2_000_000),
            Loop(Throttled, maxTokensPerHour: 200_000, tokensLastHour: 210_000, throttledUntil: now.AddHours(1)),
        };

        await context.RouteAsync("**/api/hatch/runners*", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(runners, Web),
        }));
    }
}

using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.Playwright;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-1704: the Runners page's <c>(default)</c> marker, rendered by a real
/// browser from two stubbed runners - one whose token caps are inherited from
/// the board's defaults and one with a cap of its own.
///
/// <para>The marker is read from the Bounds cell and the Token budget cell of
/// each row, then the inheriting row's Details panel is opened and its empty
/// Max tokens field read, with the hint beneath it. Both screenshots are taken
/// before their assertions, so a failing case still leaves its pictures.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class RunnersDefaultBoundsTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Inherited = "example-host.invalid:/e2e-driver-default";
    private const string OwnCap = "example-host.invalid:/e2e-driver-own";

    private const string Marker = "(default)";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheDefaultMarkerAndTheEmptyBoundsField_RenderAtDesktop()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("RunnersDefaultBounds");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline, null);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            // The runners read is stubbed, but the host still serves the bundle.
            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);

            await harness.OpenAsync(deadline, baseUrl, "/apps/hatch/runners-legacy", StubRunners);
            await harness.WaitForVisibleAsync(deadline, ".hatch-token-budget-line");

            var table = await harness.ScreenshotAsync(deadline, "runners-default-bounds");
            var measurement = await harness.MeasureRunnerBudgetsAsync(deadline);
            output.WriteLine($"--- runners, desktop{Environment.NewLine}screenshot: {table}{Environment.NewLine}{measurement}");

            var inherited = measurement.Rows.Single(r => r.Name == Inherited);
            var own = measurement.Rows.Single(r => r.Name == OwnCap);

            Assert.Contains(Marker, inherited.BoundsText);
            Assert.Contains(inherited.Lines, line => line.Contains(Marker, StringComparison.Ordinal));
            Assert.DoesNotContain(Marker, own.BoundsText);
            Assert.DoesNotContain(own.Lines, line => line.Contains(Marker, StringComparison.Ordinal));

            var page = harness.Page!;
            var row = page.Locator(".hatch-table tbody tr", new() { HasText = Inherited });
            await deadline.BoundAsync("opening the inheriting row's Details", () =>
                row.GetByRole(AriaRole.Button, new() { Name = "Details", Exact = true }).ClickAsync());
            await harness.WaitForVisibleAsync(deadline, ".hatch-runner-bounds");

            var details = await harness.ScreenshotAsync(deadline, "runners-default-bounds-details");

            var field = page.GetByLabel("Max tokens this run");
            var value = await deadline.BoundAsync("reading the Max tokens field", () => field.InputValueAsync());
            var hint = await deadline.BoundAsync("reading the Max tokens hint", () =>
                page.Locator(".hatch-field", new() { Has = field }).Locator(".hatch-field__hint").InnerTextAsync());
            output.WriteLine($"--- details{Environment.NewLine}screenshot: {details}{Environment.NewLine}Max tokens this run = \"{value}\"; hint \"{hint.Trim()}\"");

            Assert.Equal("", value);
            Assert.Equal("Empty inherits the default: 2M tokens.", hint.Trim());

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
    /// Two loops, each with its own invented name and no host, so no shared-host
    /// total row sits between them: one inheriting both caps from the board's
    /// defaults, and one with a cap of its own and no hourly one. Nothing names a
    /// real host, path, repository or person.
    /// </summary>
    private static async Task StubRunners(IBrowserContext context)
    {
        var now = DateTimeOffset.UtcNow;

        RunnerDto Loop(
            string name,
            long? maxTokens,
            string maxTokensSource,
            long? maxTokensPerHour,
            string maxTokensPerHourSource) => new(
            Name: name,
            Kind: "loop",
            FirstSeenAt: now.AddHours(-2),
            LastSeenAt: now,
            ClaimKey: null,
            Line: null,
            LineAt: null,
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
            MaxTokensSource: maxTokensSource,
            MaxTokensPerHourSource: maxTokensPerHourSource,
            TokensUsed: maxTokens is null ? null : 0,
            TokensLastHour: maxTokensPerHour is null ? null : 0,
            ThrottledUntil: null);

        var runners = new[]
        {
            Loop(Inherited, 2_000_000, "default", 200_000, "default"),
            Loop(OwnCap, 1_000_000, "runner", null, "none"),
        };

        await context.RouteAsync("**/api/hatch/runners*", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(runners, Web),
        }));
    }
}

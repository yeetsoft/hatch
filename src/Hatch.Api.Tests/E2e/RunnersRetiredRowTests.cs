using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.Playwright;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// AR-71: a row retired by a rename is not drawn as a runner. Two rows come
/// from a stubbed <c>/api/hatch/runners</c> - one live loop, which draws
/// Pause and Stop, and one retired by it, which draws neither and says what
/// replaced it. The bundle is real.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class RunnersRetiredRowTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task ARetiredRow_IsDrawnWithNoControls_AndSaysWhatReplacedIt()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("RunnersRetiredRow");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline, null);
            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);

            await harness.OpenAsync(deadline, baseUrl, "/apps/hatch/runners-legacy", StubRunners);
            await harness.WaitForVisibleAsync(deadline, ".hatch-reorder--wrap");

            var path = await harness.ScreenshotAsync(deadline, "runners-retired");
            output.WriteLine(path);

            // Per row: the buttons it draws, and the words it says.
            var rows = await harness.EvaluateAsync<string[][]>(
                deadline,
                "reading the rows",
                @"() => [...document.querySelectorAll('tbody tr')]
                    .filter(tr => tr.querySelector('.hatch-runner-name'))
                    .map(tr => [
                        tr.querySelector('.hatch-runner-name').textContent,
                        [...tr.querySelectorAll('button')].map(b => b.textContent).join('|'),
                        tr.textContent,
                    ])");

            var live = Assert.Single(rows, r => r[0] == "example-host.invalid:/e2e-new");
            Assert.Contains("Pause", live[1], StringComparison.Ordinal);
            Assert.Contains("Stop after this one", live[1], StringComparison.Ordinal);

            var old = Assert.Single(rows, r => r[0] == "example-host.invalid:/e2e-old");
            Assert.Equal("", old[1]);
            Assert.Contains("replaced by example-host.invalid:/e2e-new", old[2], StringComparison.Ordinal);
            Assert.Contains("Retired", old[2], StringComparison.Ordinal);

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

    private static Task StubRunners(IBrowserContext context)
    {
        var now = DateTimeOffset.UtcNow;

        RunnerDto Row(string name, DateTimeOffset? retiredAt, string? supersededBy) => new(
            Name: name,
            Kind: "loop",
            FirstSeenAt: now.AddHours(-1),
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
            Where: "example-host.invalid:/e2e",
            Agent: null,
            Drives: [],
            RetiredAt: retiredAt,
            SupersededBy: supersededBy);

        var body = new[]
        {
            Row("example-host.invalid:/e2e-new", null, null),
            Row("example-host.invalid:/e2e-old", now.AddMinutes(-1), "example-host.invalid:/e2e-new"),
        };

        return context.RouteAsync("**/api/hatch/runners*", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(body, Web),
        }));
    }
}

using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.Playwright;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2457: a session the board paused reads as paused by the board on the
/// Runners page, on the runner's row and on the detail's session line, and
/// never as an agent not starting. Rendered by a real browser from a stubbed
/// runner whose one live session carries kind <c>board-paused</c>, at the
/// desktop viewport in Chromium and at the <c>iPhone 14 Pro</c> profile in
/// WebKit. Each look is saved as a PNG beside the case's other artifacts.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class RunnersBoardPauseTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Name = "e2e-board-pause";
    private const string Said = "3 sessions ended in error, the last on E2E-7";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task ABoardPause_ReadsAsPausedByTheBoard_AtDesktop()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        await RunAsync(null, "RunnersBoardPause", "runners-board-pause");
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task ABoardPause_ReadsAsPausedByTheBoard_AtThePhoneProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);

        await RunAsync(DeviceProfile.IPhone14Pro, "RunnersBoardPausePhone", "runners-board-pause-phone");
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

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);

            await harness.OpenAsync(deadline, baseUrl, "/runners", Stub);
            await harness.WaitForVisibleAsync(deadline, ".hatch-runner-row");

            var row = await harness.EvaluateOnAsync<string>(deadline, "reading the row", ".hatch-runner-row", "e => e.textContent ?? ''");
            output.WriteLine($"--- row, {profile?.Device ?? "desktop"}{Environment.NewLine}screenshot: {await harness.ScreenshotAsync(deadline, screenshot)}{Environment.NewLine}{row}");
            Assert.Contains("Paused by the board until", row);
            Assert.Contains(Said, row);
            Assert.DoesNotContain("Agent not starting", row);

            await harness.GoToAsync(deadline, $"runners/{Name}");
            await harness.WaitForVisibleAsync(deadline, ".hatch-runner-sessions");
            var detail = await harness.EvaluateOnAsync<string>(deadline, "reading the sessions", ".hatch-runner-sessions", "e => e.textContent ?? ''");
            output.WriteLine($"--- detail, {profile?.Device ?? "desktop"}{Environment.NewLine}screenshot: {await harness.ScreenshotAsync(deadline, screenshot + "-detail")}{Environment.NewLine}{detail}");
            Assert.Contains("Paused by the board until", detail);
            Assert.Contains(Said, detail);
            Assert.DoesNotContain("Agent not starting", detail);

            Assert.False(
                await harness.EvaluateAsync<bool>(deadline, "measuring overflow", "() => document.documentElement.scrollWidth !== document.documentElement.clientWidth"),
                "the document scrolls sideways");

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

    /// <summary>One runner with an invented name and one live session the board paused an hour ahead.</summary>
    private static async Task Stub(IBrowserContext context)
    {
        var now = DateTimeOffset.UtcNow;
        var until = now.AddHours(1);

        var session = new RunnerSessionDto(
            Id: 1,
            Where: "example-host.invalid:/e2e/1",
            Kind: "loop",
            Host: null,
            State: RunnerStates.Running,
            StartedAt: now.AddMinutes(-30),
            LastSeenAt: now,
            EndedAt: null,
            AgentDownUntil: until,
            AgentDownKind: RunnerAgentDownKinds.BoardPaused,
            AgentDownLine: Said);

        var runner = new RunnerDto(
            Name: Name,
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
            For: null,
            CanControl: true,
            ApiKeyId: Guid.NewGuid(),
            Sessions: [session],
            AgentDownUntil: until,
            AgentDownKind: RunnerAgentDownKinds.BoardPaused,
            AgentDownLine: Said);

        await context.RouteAsync(new System.Text.RegularExpressions.Regex(@"/api/runners(\?.*)?$"), route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new[] { runner }, Web),
        }));
    }
}

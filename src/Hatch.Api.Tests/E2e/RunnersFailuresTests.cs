using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.Playwright;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2737: a runner's page draws what it cannot do now and since when under
/// <b>Failing</b>, and what it could not do before under <b>Earlier</b>. Rendered
/// by a real browser from a stubbed runner with one current forge failure and
/// one ended signing failure, at the desktop viewport in Chromium and at the
/// <c>iPhone 14 Pro</c> profile in WebKit. Each look is saved as a PNG beside
/// the case's other artifacts.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class RunnersFailuresTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Name = "e2e-failures";
    private const string Said = "gh auth status: token expired";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheFailingAndEarlierLists_Draw_AtDesktop()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        await RunAsync(null, "RunnersFailures", "runners-failures");
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheFailingAndEarlierLists_Draw_AtThePhoneProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);

        await RunAsync(DeviceProfile.IPhone14Pro, "RunnersFailuresPhone", "runners-failures-phone");
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

            // The list: one failing badge.
            Assert.Equal(1, await harness.EvaluateAsync<int>(
                deadline, "counting the badges",
                "() => [...document.querySelectorAll('.hatch-badge')].filter(b => b.textContent.trim() === 'failing').length"));

            await harness.GoToAsync(deadline, $"runners/{Name}");
            await harness.WaitForVisibleAsync(deadline, ".hatch-runner-earlier");

            var failing = await harness.EvaluateOnAsync<string>(deadline, "reading Failing", ".hatch-runner-failing", "e => e.textContent ?? ''");
            var earlier = await harness.EvaluateOnAsync<string>(deadline, "reading Earlier", ".hatch-runner-earlier", "e => e.textContent ?? ''");
            output.WriteLine($"--- page, {profile?.Device ?? "desktop"}{Environment.NewLine}screenshot: {await harness.ScreenshotAsync(deadline, screenshot)}{Environment.NewLine}{failing}{Environment.NewLine}{earlier}");
            Assert.Contains("Cannot reach its forge since", failing);
            Assert.Contains("gh said:", failing);
            Assert.Contains(Said, failing);
            Assert.DoesNotContain("Cannot sign its commits", failing);
            Assert.Contains("Cannot sign its commits - began", earlier);
            Assert.Contains(", ended", earlier);

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

    /// <summary>
    /// One runner with an invented name, one forge failure that stands and one
    /// signing failure that ended. The list read carries the current one, as
    /// the board's does; the failures read carries both lists.
    /// </summary>
    private static async Task Stub(IBrowserContext context)
    {
        var now = DateTimeOffset.UtcNow;
        var current = new RunnerFailureDto(RunnerFailureKinds.Forge, "example-host.invalid:/e2e/1", Said, now.AddMinutes(-40), now, null);
        var ended = new RunnerFailureDto(RunnerFailureKinds.Signing, "example-host.invalid:/e2e/1", "no secret key", now.AddHours(-3), now.AddHours(-2).AddMinutes(-30), now.AddHours(-2));

        var runner = new RunnerDto(
            Name: Name,
            Kind: "loop",
            FirstSeenAt: now.AddHours(-4),
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
            Sessions: [],
            Failures: [current]);

        await context.RouteAsync(new System.Text.RegularExpressions.Regex(@"/api/runners(\?.*)?$"), route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new[] { runner }, Web),
        }));

        await context.RouteAsync(new System.Text.RegularExpressions.Regex(@"/api/runners/[^/]+/failures$"), route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new RunnerFailuresDto([current], [ended]), Web),
        }));
    }
}

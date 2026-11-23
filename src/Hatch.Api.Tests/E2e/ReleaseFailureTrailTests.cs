using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2755: a release that carried a failure is its own line in the issue page's
/// History - the runner could not go on and why, what the tool said, and a link
/// to the runner - and the runner's page lists the ticket that failure cost.
///
/// <para>Real end to end: a runner is made, its heartbeat reports a signed-out
/// agent (which opens the failure row), it claims a ticket and lets it go with
/// <c>failure=agent-signed-out</c>. Nothing is stubbed. Each look is saved as a
/// PNG beside the case's other artifacts.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class ReleaseFailureTrailTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string RunnerName = "release-failure-runner";

    private const string Where = "release-failure-host:/code/hatch";

    private const string Said = "agent: not logged in";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheTrailAndTheRunnerPage_NameTheFailure_AtTheDesktopProfile() => RunAsync("ReleaseFailureTrail", null, "desktop");

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheTrailAndTheRunnerPage_NameTheFailure_AtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("ReleaseFailureTrailPhone", DeviceProfile.IPhone14Pro, "phone");
    }

    private async Task RunAsync(string name, DeviceProfile? profile, string label)
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

            using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
            var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("RFT", "Release Failure"));
            var issue = await PostAsync<IssueCreateRequest, IssueDto>(
                http, "/api/issues", new(project.Id, "task", "Release failure task", null, null, null, null));

            using var created = await http.PostAsJsonAsync("/api/runners", new CreateRunnerRequest(RunnerName), Web);
            var secret = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("secret").GetString()!;
            using var runnerHttp = new HttpClient { BaseAddress = new Uri(baseUrl) };
            runnerHttp.DefaultRequestHeaders.Authorization = new("Bearer", secret);

            // The agent is signed out: the beat opens the runner's failure row.
            await PostAsync<RunnerHeartbeatRequest, RunnerInstructionDto>(
                runnerHttp, "/api/runners/sessions",
                new(
                    Kind: "loop", Where: Where, Host: "release-failure-host",
                    AgentDown: true, AgentDownUntil: DateTimeOffset.UtcNow.AddMinutes(30),
                    AgentDownKind: RunnerAgentDownKinds.SignedOut, AgentDownLine: Said));

            var taken = await PostAsync<ClaimRequest, ClaimTakenDto>(runnerHttp, $"/api/issues/{issue.Key}/claim", new(Where));

            using var released = await runnerHttp.DeleteAsync(
                $"/api/issues/{issue.Key}/claim?token={taken.Token}&outcome=unstarted"
                + $"&failure={RunnerFailureKinds.AgentSignedOut}&failureWhy={Uri.EscapeDataString(Said)}");
            Assert.True(released.IsSuccessStatusCode, $"release answered {(int)released.StatusCode}");

            await harness.OpenAsync(deadline, baseUrl, $"/issues/{issue.Key}");
            await harness.ClickAsync(deadline, "#history summary, details:has(.hatch-events) summary");
            await harness.WaitForVisibleAsync(deadline, ".hatch-release-failure");

            await harness.EvaluateOnAsync<bool>(deadline, "scrolling the release line into view", ".hatch-release-failure", "e => { e.scrollIntoView({ block: \"center\" }); return true; }");
            var line = await harness.EvaluateOnAsync<string>(
                deadline, "reading the release line", ".hatch-event:has(.hatch-release-failure)", "e => e.textContent ?? ''");
            output.WriteLine($"--- {label} history: {line}{Environment.NewLine}screenshot: {await harness.ScreenshotAsync(deadline, $"history-{label}")}");
            Assert.Contains($"{RunnerName} could not go on - its agent is signed out", line);
            Assert.Contains($"the tool said: {Said}", line);
            await harness.WaitForVisibleAsync(deadline, $".hatch-release-failure a[href=\"/runners/{RunnerName}\"]");
            Assert.False(
                await harness.EvaluateAsync<bool>(deadline, "measuring overflow", "() => document.documentElement.scrollWidth !== document.documentElement.clientWidth"),
                "the document scrolls sideways");

            await harness.GoToAsync(deadline, $"runners/{RunnerName}");
            await harness.WaitForVisibleAsync(deadline, $".hatch-runner-failing a[href=\"/issues/{issue.Key}\"]");

            var failing = await harness.EvaluateOnAsync<string>(deadline, "reading Failing", ".hatch-runner-failing", "e => e.textContent ?? ''");
            output.WriteLine($"--- {label} runner: {failing}{Environment.NewLine}screenshot: {await harness.ScreenshotAsync(deadline, $"runner-{label}")}");
            Assert.Contains($"cost {issue.Key}", failing);

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

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

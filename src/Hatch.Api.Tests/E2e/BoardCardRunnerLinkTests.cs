using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2159: a claimed board card's robot head, pressed against a real claim
/// held by a real runner, lands on that runner's own page.
///
/// <para>A claim's wire shape carries two names - <c>claimedBy</c>, the key
/// that actually holds it, and <c>runner</c>, the checkout's own <c>--where</c>
/// (still a location, not a cast-list name, on every path that takes a claim
/// today) - and the app's <c>ClaimBadge.tsx</c> once linked the head with the
/// latter, landing on <c>/runners/&lt;a location string&gt;</c>, which no
/// runner is ever called. A click is only provable here, since the web app
/// has no DOM test environment - the same reason
/// <c>RunnersSessionControlsTests</c> drives a press rather than asserting a
/// prop.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class BoardCardRunnerLinkTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string RunnerName = "click-test-runner";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheClaimedCardsRobotHead_LandsOnTheRunnerItIsHeldBy()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("BoardCardRunnerLink");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline, null);
            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);

            using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
            var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("REND", "Render Checks"));
            var issue = await PostAsync<IssueCreateRequest, IssueDto>(
                http, "/api/issues", new(project.Id, "task", "Render check task", null, null, null, null));

            var secret = await PostForSecretAsync(http, "/api/runners", new CreateRunnerRequest(RunnerName));
            using var runnerHttp = new HttpClient { BaseAddress = new Uri(baseUrl) };
            runnerHttp.DefaultRequestHeaders.Authorization = new("Bearer", secret);
            await PostAsync<RunnerHeartbeatRequest, RunnerInstructionDto>(
                runnerHttp, "/api/runners/sessions", new(Kind: "loop", Where: "click-test-host:/code/hatch", Host: "click-test-host"));
            await PostAsync<ClaimRequest, ClaimTakenDto>(runnerHttp, $"/api/issues/{issue.Key}/claim", new("click-test-host:/code/hatch"));

            await harness.OpenBoardAsync(deadline, baseUrl);
            await harness.WaitForVisibleAsync(deadline, ".hatch-card-claim[role=link]");
            await harness.ScreenshotAsync(deadline, "1-board-before-click");

            await harness.ClickAsync(deadline, ".hatch-card-claim[role=link]");
            await deadline.BoundAsync("waiting for the runner detail", () =>
                harness.Page!.WaitForURLAsync($"**/runners/{RunnerName}"));
            await harness.WaitForVisibleAsync(deadline, "h2.hatch-section-title:has-text(\"Identity and key\")");
            var path = await harness.ScreenshotAsync(deadline, "2-runner-detail-after-click");
            output.WriteLine($"landed on: {new Uri(harness.Page!.Url).AbsolutePath}");
            output.WriteLine($"screenshot: {path}");

            Assert.Equal($"/runners/{RunnerName}", new Uri(harness.Page!.Url).AbsolutePath);

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

    /// <summary>Avoids deserialising the full <see cref="RunnerDto"/> (its <c>boardDrift</c>/<c>sourceDrift</c> enums need a converter these plain <see cref="JsonSerializerDefaults.Web"/> options do not carry) for the one field this case needs.</summary>
    private static async Task<string> PostForSecretAsync(HttpClient http, string path, CreateRunnerRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.GetProperty("secret").GetString()!;
    }
}

using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2477: an issue page left open on a claimed issue keeps the claim's
/// heartbeat current without a click, so it never says "not heard from lately"
/// while the claim is being renewed.
///
/// <para>The claim's heartbeat is put 140 seconds back - ten seconds short of
/// half the default lease - and a real heartbeat lands moments after the page
/// opens. A page that read the claim once would go on saying the old time, and
/// would call it quiet on its next render; this one reads again by itself.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class IssuePageClaimStaysCurrentTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string RunnerName = "claim-watch-runner";

    private const string Where = "claim-watch-host:/code/hatch";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task AnOpenIssuePage_ReadsTheClaimsHeartbeatAgain_WithoutAClick()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("IssuePageClaimStaysCurrent");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline, null);
            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);

            using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
            var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("CLW", "Claim Watch"));
            var issue = await PostAsync<IssueCreateRequest, IssueDto>(
                http, "/api/issues", new(project.Id, "task", "Claim watch task", null, null, null, null));

            using var response = await http.PostAsJsonAsync("/api/runners", new CreateRunnerRequest(RunnerName), Web);
            var secret = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("secret").GetString()!;
            using var runnerHttp = new HttpClient { BaseAddress = new Uri(baseUrl) };
            runnerHttp.DefaultRequestHeaders.Authorization = new("Bearer", secret);

            await PostAsync<RunnerHeartbeatRequest, RunnerInstructionDto>(
                runnerHttp, "/api/runners/sessions", new(Kind: "loop", Where: Where, Host: "claim-watch-host"));
            var taken = await PostAsync<ClaimRequest, ClaimTakenDto>(runnerHttp, $"/api/issues/{issue.Key}/claim", new(Where));

            using (var scope = harness.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
                var stale = DateTimeOffset.UtcNow.AddSeconds(-140);
                await db.Issues.Where(i => i.ClaimToken != null).ExecuteUpdateAsync(s => s.SetProperty(i => i.ClaimHeartbeatAt, stale));
            }

            await harness.OpenAsync(deadline, baseUrl, $"/issues/{issue.Key}");
            var facts = ".hatch-claim-facts";
            await harness.WaitForVisibleAsync(deadline, facts);
            await harness.ScreenshotAsync(deadline, "1-open-with-an-old-heartbeat");

            var before = await harness.Page!.Locator(facts).InnerTextAsync();
            output.WriteLine($"on open: {before}");
            Assert.Contains("minutes ago", before.Split("last heard from")[1]);

            // The runner's heartbeat lands; the page is never clicked, focused or typed into.
            using var beat = await runnerHttp.PostAsJsonAsync(
                $"/api/issues/{issue.Key}/claim/heartbeat", new ClaimHeartbeatRequest(taken.Token, null), Web);
            Assert.True(beat.IsSuccessStatusCode, $"heartbeat answered {(int)beat.StatusCode}");

            await harness.WaitForFunctionAsync(
                deadline, "waiting for the page to read the new heartbeat",
                "() => { const t = document.querySelector('.hatch-claim-facts')?.innerText ?? ''; " +
                "const s = t.split('last heard from')[1] ?? ''; return s.length > 0 && !s.includes('minute'); }");

            var after = await harness.Page!.Locator(facts).InnerTextAsync();
            output.WriteLine($"after a poll: {after}");
            await harness.ScreenshotAsync(deadline, "2-after-a-poll-no-click");

            Assert.Equal(0, await harness.Page!.GetByText("not heard from lately").CountAsync());
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

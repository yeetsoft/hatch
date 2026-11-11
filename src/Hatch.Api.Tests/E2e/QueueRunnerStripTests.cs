using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.Playwright;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2223's acceptance, rendered: a keyed runner mid-increment - a key, a live
/// session at a <c>where</c> and a claim taken at that <c>where</c>, with no
/// legacy beat behind it - reads <em>Working</em> with the ticket key in the
/// queue modal's runner strip, rather than Idle. Nothing is stubbed: the rows
/// are made through the real routes, the way a runner's own key makes them,
/// so the fold the runner row takes from its live sessions is what is drawn.
/// Desktop Chromium only.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class QueueRunnerStripTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Runner = "keyed-one";

    private const string Where = "here:/checkouts/one";

    private const string Line = "running the tests";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task AKeyedRunnerMidIncrement_ReadsWorkingWithTheTicketKey_InTheQueueModal()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("QueueRunnerStrip");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            var key = await deadline.BoundAsync("seeding a working keyed runner", () => SeedAsync(baseUrl));

            await harness.OpenBoardAsync(deadline, baseUrl);
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForFirstColumnAsync(deadline);

            await harness.ClickAsync(deadline, ".hatch-queue-control");
            await harness.WaitForVisibleAsync(deadline, "[role=\"dialog\"]:has-text(\"The work queue\")");

            var row = $".hatch-queue-runner:has(.hatch-queue-runner-name:text-is(\"{Runner}\"))";
            await harness.WaitForVisibleAsync(deadline, $"{row} .hatch-card-key:text-is(\"{key}\")");
            await harness.WaitForVisibleAsync(deadline, $"{row}:has-text(\"Working\")");
            await harness.WaitForVisibleAsync(deadline, $"{row} .hatch-queue-runner-line:text-is(\"{Line}\")");
            Assert.False(await harness.IsVisibleAsync(deadline, $"{row}:has-text(\"Idle\")"));

            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, "queue-runner-strip-desktop")}");

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
    /// A minted key, a loop session under it at <see cref="Where"/>, and an
    /// issue claimed at that <c>where</c> with a line - returning the issue's
    /// key. No beat is ever sent, so the runner row has none of its own to
    /// find the claim by.
    /// </summary>
    private static async Task<string> SeedAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/hatch/projects", new("E2E", "E2E"));
        var issue = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/hatch/issues", new(project.Id, "task", "Keep a runner busy", null, null, null, null));
        var minted = await PostAsync<CreateRunnerRequest, RunnerMintedDto>(http, "/api/hatch/runners", new(Runner));

        using var asRunner = new HttpClient { BaseAddress = new Uri(baseUrl) };
        asRunner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", minted.Secret);

        await PostAsync<RunnerHeartbeatRequest, RunnerInstructionDto>(
            asRunner, "/api/hatch/runners/sessions", new(Kind: "loop", Where: Where));
        var taken = await PostAsync<ClaimRequest, ClaimTakenDto>(asRunner, $"/api/hatch/issues/{issue.Key}/claim", new(Where));

        using var beat = await asRunner.PostAsJsonAsync(
            $"/api/hatch/issues/{issue.Key}/claim/heartbeat", new ClaimHeartbeatRequest(taken.Token, Line), Web);
        Assert.True(beat.IsSuccessStatusCode, $"the claim heartbeat answered {(int)beat.StatusCode}: {await beat.Content.ReadAsStringAsync()}");

        return issue.Key;
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Microsoft.Playwright;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2598's acceptance, rendered: a Ballot holding a question only a session
/// has answered draws it as <i>Answered by a session</i>, naming the key, then
/// the decision, then <i>Found in</i> with the rest of the answer, then the
/// standing line, then Keep and Overturn - on an open issue and on a closed
/// one, the session's own answer in prose rather than either option's label.
/// Keep agrees with the session; Overturn disagrees and files the rework.
///
/// <para>The real API and bundle against the harness's own scratch databases,
/// never a real board. Each look is saved as a PNG beside the case's artifacts.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class BallotProgramAnswerLookBackPressesTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string RunnerName = "program-answer-runner";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>Setting a question to <c>build</c> or <c>merge</c> needs a recommended option - <see cref="QuestionLevelController"/> - whatever the session's own answer later says.</summary>
    private static readonly QuestionOptionDto[] StoreOptions = [new("first store", "Already wired in", true), new("second store", "Would need a rewrite", false)];

    private static readonly QuestionOptionDto[] KeepOrDropOptions = [new("keep", "Costs nothing", true), new("drop", "Costs a rewrite", false)];

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task AProgramsAnswer_KeepsAndOverturns_AtTheDesktopProfile() => RunAsync("BallotProgramAnswerDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task AProgramsAnswer_KeepsAndOverturns_AtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("BallotProgramAnswerPhone", "phone", DeviceProfile.IPhone14Pro);
    }

    private async Task RunAsync(string name, string profileName, DeviceProfile? profile)
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
            await deadline.BoundAsync("seeding the two program-answered questions", () => SeedAsync(baseUrl));

            await harness.OpenAsync(deadline, baseUrl, "/ballot");
            var page = harness.Page!;

            // The first card: an open issue, answered only by the session, in prose.
            await harness.WaitForVisibleAsync(deadline, ".hatch-ballot-card-decided");
            var card = page.Locator(".hatch-ballot-card");
            var decided = page.Locator(".hatch-ballot-card-decided");
            await deadline.BoundAsync("reading who answered", () => Assertions.Expect(decided).ToContainTextAsync("Answered by a session"));
            await deadline.BoundAsync("reading the runner's name", () => Assertions.Expect(decided).ToContainTextAsync(RunnerName));
            await deadline.BoundAsync("reading the decision", () => Assertions.Expect(decided).ToContainTextAsync("Went with the first store."));
            await deadline.BoundAsync("reading Found in", () => Assertions.Expect(decided).ToContainTextAsync("Found in"));
            await deadline.BoundAsync("reading the rest of the answer", () => Assertions.Expect(decided).ToContainTextAsync("It was already wired in."));
            output.WriteLine($"saved {await harness.FullPageScreenshotAsync(deadline, $"program-answer-first-{profileName}")}");

            // Keep agrees with the session, not either option - the card moves straight on
            // to the next question, with nothing left to press.
            await deadline.BoundAsync("pressing Keep", () => card.GetByRole(AriaRole.Button, new() { Name = "Keep", Exact = true }).ClickAsync());
            output.WriteLine($"saved {await harness.FullPageScreenshotAsync(deadline, $"program-answer-kept-{profileName}")}");

            // The second card: a closed issue, with options, answered only by the session.
            await deadline.BoundAsync("reading the second decision", () => Assertions.Expect(decided).ToContainTextAsync("Dropped it."));
            await deadline.BoundAsync("reading who answered", () => Assertions.Expect(decided).ToContainTextAsync("Answered by a session"));
            await deadline.BoundAsync("reading the outcome line", () => Assertions.Expect(decided).ToContainTextAsync("It stands in"));
            output.WriteLine($"saved {await harness.FullPageScreenshotAsync(deadline, $"program-answer-second-{profileName}")}");

            // Overturn shows the box, and sending a sentence files the rework.
            await deadline.BoundAsync("pressing Overturn", () => card.GetByRole(AriaRole.Button, new() { Name = "Overturn", Exact = true }).ClickAsync());
            await deadline.BoundAsync("typing the overturn", () => card.GetByLabel("Your answer").FillAsync("Keep it after all."));
            await deadline.BoundAsync("sending", () => card.Locator(".hatch-answer-box").GetByRole(AriaRole.Button, new() { Name = "Overturn", Exact = true }).ClickAsync());
            var filed = card.Locator(".hatch-lookback-presses a[href^='/issues/']");
            await deadline.BoundAsync("reading the filed task", () => Assertions.Expect(filed).ToHaveCountAsync(1));
            output.WriteLine($"saved {await harness.FullPageScreenshotAsync(deadline, $"program-answer-overturned-{profileName}")}");

            await deadline.BoundAsync("pressing Next", () => card.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }).ClickAsync());
            await harness.WaitForVisibleAsync(deadline, ".hatch-ballot-clear");

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
    /// Two stories, each with a question only a session has answered, in prose
    /// rather than either option's label: one at <c>build</c> on an open issue,
    /// the other at <c>merge</c> on a closed one - both left waiting for a
    /// person's look back.
    /// </summary>
    private static async Task SeedAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var secret = await PostForSecretAsync(http, "/api/runners", new CreateRunnerRequest(RunnerName));
        using var keyHttp = new HttpClient { BaseAddress = new Uri(baseUrl) };
        keyHttp.DefaultRequestHeaders.Authorization = new("Bearer", secret);
        await PostAsync<RunnerHeartbeatRequest, RunnerInstructionDto>(
            keyHttp, "/api/runners/sessions", new(Kind: "loop", Where: "program-answer-host:/code/hatch", Host: "program-answer-host"));

        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("E2E", "E2E"));

        // Open, asked oldest - so it sorts first in the ordered list. The session's own
        // answer is in prose, not either option's label - Keep still goes by the answer.
        var open = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "story", "The story the session answered without you", null, null, null, null));
        var openQuestion = await PostAsync<CommentCreateRequest, CommentDto>(
            http, $"/api/issues/{open.Key}/comments", new("Which store should it use?", "question", null, StoreOptions));
        await PutAsync<QuestionLevelRequest, QuestionDto>(
            http, $"/api/issues/{open.Key}/questions/{openQuestion.Id}/level", new(QuestionLevels.BuildName));
        await PostAsync<CommentCreateRequest, CommentDto>(
            keyHttp, $"/api/issues/{open.Key}/comments",
            new("Went with the first store.\n\nIt was already wired in.", "answer", openQuestion.Id, null));

        // Closed, asked after - sorts second.
        var closed = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "story", "The shipped story the session answered without you", null, null, null, null));
        var closedQuestion = await PostAsync<CommentCreateRequest, CommentDto>(
            http, $"/api/issues/{closed.Key}/comments", new("Keep it or drop it?", "question", null, KeepOrDropOptions));
        await PutAsync<QuestionLevelRequest, QuestionDto>(
            http, $"/api/issues/{closed.Key}/questions/{closedQuestion.Id}/level", new(QuestionLevels.MergeName));
        await PostAsync<CommentCreateRequest, CommentDto>(
            keyHttp, $"/api/issues/{closed.Key}/comments",
            new("Dropped it.\n\nToo risky to keep.", "answer", closedQuestion.Id, null));

        var board = (await http.GetFromJsonAsync<BoardDto>("/api/board", Web))!;
        var done = board.Statuses.First(s => s.IsTerminal && !s.IsDeferred);
        await PostAsync<IssueMoveRequest, IssueDto>(http, $"/api/issues/{closed.Key}/move", new(done.Id, null, null));
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }

    private static async Task<TResponse> PutAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PutAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"PUT {path} answered {(int)response.StatusCode}: {text}");
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

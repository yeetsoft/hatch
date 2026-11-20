using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Microsoft.Playwright;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2557's acceptance, rendered: a Ballot holding one look-back question
/// draws it as what was decided without the person, with Keep and Overturn,
/// on the card and in the list by issue. Overturn shows the other options and
/// the box, and sending one files the rework task and names it.
///
/// <para>The real API and bundle against the harness's own scratch databases,
/// never a real board. Each look is saved as a PNG beside the case's artifacts.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class BallotLookBackPressesTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly QuestionOptionDto[] Recommended = [new("keep", "Costs nothing", true), new("drop", "Costs a rewrite", false)];

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task ALookBack_KeepsAndOverturns_AtTheDesktopProfile() => RunAsync("BallotLookBackDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task ALookBack_KeepsAndOverturns_AtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("BallotLookBackPhone", "phone", DeviceProfile.IPhone14Pro);
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
            var story = await deadline.BoundAsync("filing the shipped story", () => SeedAsync(baseUrl));
            // The list by issue: the same two presses under the question's options.
            await harness.OpenAsync(deadline, baseUrl, "/ballot?view=list");
            var page = harness.Page!;
            await harness.WaitForVisibleAsync(deadline, ".hatch-ballot .hatch-question:has-text(\"Q-merge\")");
            var listPresses = page.Locator(".hatch-ballot .hatch-lookback-presses");
            await deadline.BoundAsync("reading Keep in the list", () => Assertions.Expect(listPresses.GetByRole(AriaRole.Button, new() { Name = "Keep", Exact = true })).ToBeVisibleAsync());
            await deadline.BoundAsync("reading Overturn in the list", () => Assertions.Expect(listPresses.GetByRole(AriaRole.Button, new() { Name = "Overturn", Exact = true })).ToBeVisibleAsync());
            output.WriteLine($"saved {await harness.FullPageScreenshotAsync(deadline, $"look-back-presses-list-{profileName}")}");

            // The card: what was decided, what happened since, and the two presses.
            await harness.GoToAsync(deadline, "/ballot");
            await harness.WaitForVisibleAsync(deadline, ".hatch-ballot-card-decided");
            var decided = page.Locator(".hatch-ballot-card-decided");
            await deadline.BoundAsync("reading the decided line", () => Assertions.Expect(decided).ToContainTextAsync("Decided without you: keep"));
            await deadline.BoundAsync("reading the outcome line", () => Assertions.Expect(decided).ToContainTextAsync("It stands in"));
            var card = page.Locator(".hatch-ballot-card");
            await deadline.BoundAsync("reading Holds nothing", () => Assertions.Expect(card).ToContainTextAsync("Holds nothing"));
            output.WriteLine($"saved {await harness.FullPageScreenshotAsync(deadline, $"look-back-card-{profileName}")}");

            // Overturn shows the other options and the box - and not the one taken.
            await deadline.BoundAsync("pressing Overturn", () => card.GetByRole(AriaRole.Button, new() { Name = "Overturn", Exact = true }).ClickAsync());
            var options = card.Locator(".hatch-lookback-presses .hatch-option");
            await deadline.BoundAsync("counting the other options", () => Assertions.Expect(options).ToHaveCountAsync(1));
            await deadline.BoundAsync("reading the other option", () => Assertions.Expect(options.First).ToContainTextAsync("drop"));
            await deadline.BoundAsync("finding the box", () => Assertions.Expect(card.GetByLabel("Your answer")).ToBeVisibleAsync());
            output.WriteLine($"saved {await harness.FullPageScreenshotAsync(deadline, $"look-back-overturn-{profileName}")}");

            var overflow = await deadline.BoundAsync("measuring the page", () =>
                page.EvaluateAsync<double>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth"));
            Assert.True(overflow <= 0.5, $"the card scrolls sideways by {overflow}px at {profileName}");

            // Sending one, on the scratch board: the task is filed and named as a link.
            await deadline.BoundAsync("choosing drop", () => options.First.ClickAsync());
            await deadline.BoundAsync("sending", () => card.Locator(".hatch-answer-box").GetByRole(AriaRole.Button, new() { Name = "Overturn", Exact = true }).ClickAsync());
            var filed = card.Locator(".hatch-lookback-presses a[href^='/issues/']");
            await deadline.BoundAsync("reading the filed task", () => Assertions.Expect(filed).ToHaveCountAsync(1));
            var taskKey = await deadline.BoundAsync("reading the task key", () => filed.InnerTextAsync());
            Assert.NotEqual(story, taskKey);
            await deadline.BoundAsync("reading the filed sentence", () => Assertions.Expect(card).ToContainTextAsync("to bring the work in line"));
            output.WriteLine($"saved {await harness.FullPageScreenshotAsync(deadline, $"look-back-filed-{profileName}")}");

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

    /// <summary>One story with a question asked at merge, moved into a terminal column: a look back.</summary>
    private static async Task<string> SeedAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var project = await SendAsync<ProjectCreateRequest, ProjectDto>(http, HttpMethod.Post, "/api/projects", new("E2E", "E2E"));
        var story = await SendAsync<IssueCreateRequest, IssueDto>(
            http, HttpMethod.Post, "/api/issues", new(project.Id, "story", "The story with a merge question", null, null, null, null));
        var question = await SendAsync<CommentCreateRequest, CommentDto>(
            http, HttpMethod.Post, $"/api/issues/{story.Key}/comments", new("Q-merge: keep it or drop it?", "question", null, Recommended));
        await SendAsync<QuestionLevelRequest, QuestionDto>(
            http, HttpMethod.Put, $"/api/issues/{story.Key}/questions/{question.Id}/level", new(QuestionLevels.MergeName));

        var board = (await http.GetFromJsonAsync<BoardDto>("/api/board", Web))!;
        var done = board.Statuses.First(s => s.IsTerminal && !s.IsDeferred);
        await SendAsync<IssueMoveRequest, IssueDto>(http, HttpMethod.Post, $"/api/issues/{story.Key}/move", new(done.Id, null, null));

        return story.Key;
    }

    private static async Task<TResponse> SendAsync<TRequest, TResponse>(HttpClient http, HttpMethod method, string path, TRequest body)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body, options: Web) };
        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{method} {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

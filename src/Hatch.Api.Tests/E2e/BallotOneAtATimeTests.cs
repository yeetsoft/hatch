using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Microsoft.Playwright;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2558: the Ballot's one-at-a-time form, rendered. With a question at the
/// hold, build and merge levels and one look-back, the card shows the epic's
/// question first and says what is behind it; pressing the recommended option
/// moves to the next, Skip moves past one, and the look-back arrives last,
/// where Overturn files its rework task. At the phone profile the page is never
/// wider than the screen at any of those looks.
///
/// <para>The real API and bundle against the harness's own scratch databases,
/// never a real board. Each look is saved as a PNG beside the case's artifacts.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class BallotOneAtATimeTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly QuestionOptionDto[] Recommended = [new("keep", "Costs nothing", true), new("drop", "Costs a rewrite", false)];

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheBallot_AsksOneAtATime_AtTheDesktopProfile() => RunAsync("BallotOneAtATimeDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheBallot_AsksOneAtATime_AtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("BallotOneAtATimePhone", "phone", DeviceProfile.IPhone14Pro);
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
            var seeded = await deadline.BoundAsync("filing the four questions", () => SeedAsync(baseUrl));
            await harness.OpenAsync(deadline, baseUrl, "/ballot");
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForVisibleAsync(deadline, ".hatch-ballot-card");
            var page = harness.Page!;
            var card = page.Locator(".hatch-ballot-card");
            var issue = card.Locator(".hatch-ballot-card-issue .hatch-ballot-key");

            async Task Shot(string step)
            {
                // The card fades between questions, so wait out the fade before the look.
                await deadline.BoundAsync("letting the card settle", () => Task.Delay(500));
                output.WriteLine($"saved {await harness.FullPageScreenshotAsync(deadline, $"ballot-{step}-{profileName}")}");
                if (profile is not null)
                {
                    var widths = await harness.EvaluateAsync<int[]>(
                        deadline, "measuring the page", "() => [document.documentElement.scrollWidth, window.innerWidth]");
                    AssertNoOverflow(widths[0], widths[1], profileName, step);
                }
            }

            // The epic's question first: the most it holds, so the most it lets go of.
            await deadline.BoundAsync("reading the epic's key", () => Assertions.Expect(issue).ToHaveTextAsync(seeded.Epic));
            await deadline.BoundAsync("reading the holds pill", () => Assertions.Expect(card.Locator(".hatch-ballot-card-pills")).ToContainTextAsync("Holds 4 issues"));
            await deadline.BoundAsync("reading the position", () => Assertions.Expect(card.Locator(".hatch-ballot-card-position")).ToContainTextAsync("1 of 4"));
            await deadline.BoundAsync("reading what is behind it", () => Assertions.Expect(card.Locator(".hatch-ballot-card-foot .text-muted"))
                .ToContainTextAsync("Then 2 more to answer, and 1 decided without you to look over"));
            await Shot("hold");

            // The recommended option is drawn first, and pressing it answers at once.
            await deadline.BoundAsync("pressing the recommended option", () => card.Locator(".hatch-options .hatch-option").First.ClickAsync());
            await deadline.BoundAsync("reading the story's key", () => Assertions.Expect(issue).ToHaveTextAsync(seeded.Story));
            await deadline.BoundAsync("reading the build question", () => Assertions.Expect(card).ToContainTextAsync("Q-build"));
            await Shot("build");

            // Skip leaves one for later and moves on to the open task's merge question.
            await deadline.BoundAsync("pressing Skip", () => card.GetByRole(AriaRole.Button, new() { Name = "Skip", Exact = true }).ClickAsync());
            await deadline.BoundAsync("reading the task's key", () => Assertions.Expect(issue).ToHaveTextAsync(seeded.OpenTask));
            await deadline.BoundAsync("reading the merge question", () => Assertions.Expect(card).ToContainTextAsync("Q-open-merge"));
            await Shot("merge");

            // Skip again: what is left is the look-back, drawn as decided without the person.
            await deadline.BoundAsync("pressing Skip again", () => card.GetByRole(AriaRole.Button, new() { Name = "Skip", Exact = true }).ClickAsync());
            await deadline.BoundAsync("reading the closed story's key", () => Assertions.Expect(issue).ToHaveTextAsync(seeded.Closed));
            await deadline.BoundAsync("reading the decided line", () => Assertions.Expect(card.Locator(".hatch-ballot-card-decided")).ToContainTextAsync("Decided without you: keep"));
            await Shot("look-back");

            // Overturn, a different option, Overturn again: the rework task is filed and named.
            await deadline.BoundAsync("pressing Overturn", () => card.GetByRole(AriaRole.Button, new() { Name = "Overturn", Exact = true }).ClickAsync());
            var other = card.Locator(".hatch-lookback-presses .hatch-option");
            await deadline.BoundAsync("counting the other options", () => Assertions.Expect(other).ToHaveCountAsync(1));
            await deadline.BoundAsync("choosing the other option", () => other.First.ClickAsync());
            await deadline.BoundAsync("sending", () => card.Locator(".hatch-answer-box").GetByRole(AriaRole.Button, new() { Name = "Overturn", Exact = true }).ClickAsync());
            var filed = card.Locator(".hatch-lookback-presses a[href^='/issues/']");
            await deadline.BoundAsync("reading the filed task", () => Assertions.Expect(filed).ToHaveCountAsync(1));
            var filedKey = await deadline.BoundAsync("reading the task key", () => filed.InnerTextAsync());
            Assert.DoesNotContain(filedKey, new[] { seeded.Epic, seeded.Story, seeded.OpenTask, seeded.Closed });
            await deadline.BoundAsync("reading the key on the card", () => Assertions.Expect(card).ToContainTextAsync(filedKey));
            await Shot("filed");

            // The rework hangs under the closed story, where the API will find it.
            using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
            var children = await deadline.BoundAsync("reading the closed story's children", () =>
                http.GetFromJsonAsync<List<IssueCardDto>>($"/api/issues?parentKey={seeded.Closed}", Web));
            var child = Assert.Single(children!);
            Assert.Equal(filedKey, child.Key);
            Assert.Equal("task", child.Type);

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

    private static void AssertNoOverflow(int scroll, int inner, string profileName, string look) =>
        Assert.True(scroll <= inner, $"the ballot at {look} scrolls sideways at the {profileName} profile: {scroll}px in {inner}px");

    private sealed record Seeded(string Epic, string Story, string OpenTask, string Closed);

    /// <summary>
    /// An epic over three tasks with a question at hold, a story with one at build, a task
    /// with one at merge, and a closed story with an unanswered one at merge: a look-back.
    /// The epic holds four open issues, so it is asked first, and the look-back last.
    /// </summary>
    private static async Task<Seeded> SeedAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var project = await SendAsync<ProjectCreateRequest, ProjectDto>(http, HttpMethod.Post, "/api/projects", new("E2E", "E2E"));

        async Task<IssueDto> FileIssue(string type, string title, string? parentKey = null) =>
            await SendAsync<IssueCreateRequest, IssueDto>(http, HttpMethod.Post, "/api/issues", new(project.Id, type, title, null, parentKey, null, null));

        async Task Ask(IssueDto issue, string body, string? level)
        {
            var question = await SendAsync<CommentCreateRequest, CommentDto>(
                http, HttpMethod.Post, $"/api/issues/{issue.Key}/comments", new(body, "question", null, Recommended));
            if (level is not null)
                await SendAsync<QuestionLevelRequest, QuestionDto>(http, HttpMethod.Put, $"/api/issues/{issue.Key}/questions/{question.Id}/level", new(level));
        }

        var epic = await FileIssue("epic", "The epic with three tasks");
        for (var i = 1; i <= 3; i++)
            await FileIssue("task", $"Task {i} under the epic", epic.Key);
        await Ask(epic, "Q-hold: keep it or drop it?", null);

        var story = await FileIssue("story", "The story with a build question");
        await Ask(story, "Q-build: keep it or drop it?", QuestionLevels.BuildName);

        var task = await FileIssue("task", "The task with a merge question");
        await Ask(task, "Q-open-merge: keep it or drop it?", QuestionLevels.MergeName);

        var closed = await FileIssue("story", "The shipped story with a merge question");
        await Ask(closed, "Q-look-back: keep it or drop it?", QuestionLevels.MergeName);
        var board = (await http.GetFromJsonAsync<BoardDto>("/api/board", Web))!;
        var done = board.Statuses.First(s => s.IsTerminal && !s.IsDeferred);
        await SendAsync<IssueMoveRequest, IssueDto>(http, HttpMethod.Post, $"/api/issues/{closed.Key}/move", new(done.Id, null, null));

        return new(epic.Key, story.Key, task.Key, closed.Key);
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

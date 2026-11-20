using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Microsoft.Playwright;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2576: the press that makes an answer a principle, rendered. On the issue
/// page an answered question's answer offers Make this a principle, which opens
/// the sentence and Promote names the ambition. On the Ballot a look-back card
/// carries the same thing as a checkbox that Keep acts on. Each press is read
/// back from the ambition's own description, and at the phone profile no look
/// is wider than the screen.
///
/// <para>The real API and bundle against the harness's own scratch databases,
/// never a real board. Each look is saved as a PNG beside the case's artifacts.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class PromotePressTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string SeededPrinciple = "Seeded principle";

    private const string AnswerSentence = "Keep the first press small";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly QuestionOptionDto[] Recommended = [new("keep", "Costs nothing", true), new("drop", "Costs a rewrite", false)];

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task ThePress_PromotesAnAnswer_AtTheDesktopProfile() => RunAsync("PromotePressDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task ThePress_PromotesAnAnswer_AtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("PromotePressPhone", "phone", DeviceProfile.IPhone14Pro);
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
            var seeded = await deadline.BoundAsync("filing the ambition and its story", () => SeedAsync(baseUrl));
            using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

            async Task Shot(string step, string page)
            {
                output.WriteLine($"saved {await harness.FullPageScreenshotAsync(deadline, $"promote-press-{step}-{profileName}")}");
                if (profile is not null)
                {
                    var widths = await harness.EvaluateAsync<int[]>(
                        deadline, "measuring the page", "() => [document.documentElement.scrollWidth, window.innerWidth]");
                    Assert.True(
                        widths[0] <= widths[1],
                        $"the {page} at {step} scrolls sideways at the {profileName} profile: {widths[0]}px in {widths[1]}px");
                }
            }

            async Task<string[]> PrincipleLines()
            {
                var ambition = await deadline.BoundAsync("reading the ambition", () => http.GetFromJsonAsync<IssueDto>($"/api/issues/{seeded.Ambition}", Web));
                return ambition!.Description
                    .Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => l.StartsWith("- ", StringComparison.Ordinal))
                    .ToArray();
            }

            // The issue page: the answer offers the press, which opens on the answer's sentence.
            await harness.OpenAsync(deadline, baseUrl, $"/issues/{seeded.Story}");
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForVisibleAsync(deadline, ".hatch-comment-answer");
            var answer = harness.Page!.Locator(".hatch-comment-answer");
            var make = answer.GetByRole(AriaRole.Button, new() { Name = "Make this a principle" });
            await deadline.BoundAsync("reading the press", () => Assertions.Expect(make).ToBeVisibleAsync());
            await Shot("issue-closed", "issue page");

            await deadline.BoundAsync("pressing Make this a principle", () => make.ClickAsync());
            var field = answer.GetByLabel("Principle");
            await deadline.BoundAsync("reading the sentence", () => Assertions.Expect(field).ToHaveValueAsync(AnswerSentence));
            await Shot("issue-open", "issue page");

            await deadline.BoundAsync("pressing Promote", () => answer.GetByRole(AriaRole.Button, new() { Name = "Promote", Exact = true }).ClickAsync());
            await deadline.BoundAsync("reading the confirmation", () => Assertions.Expect(answer).ToContainTextAsync("Now a principle of"));
            await deadline.BoundAsync("reading the ambition's link", () => Assertions.Expect(answer.Locator($"a[href='/issues/{seeded.Ambition}']")).ToHaveTextAsync(seeded.Ambition));
            await Shot("issue-done", "issue page");

            Assert.Equal([$"- {SeededPrinciple}", $"- {AnswerSentence}"], await PrincipleLines());

            // The Ballot: the look-back card carries the press as a checkbox beside Keep.
            await harness.GoToAsync(deadline, "/ballot");
            await harness.WaitForVisibleAsync(deadline, ".hatch-ballot-card");
            var card = harness.Page!.Locator(".hatch-ballot-card");
            var presses = card.Locator(".hatch-lookback-presses");
            var tick = presses.GetByLabel("Make this a principle");
            await deadline.BoundAsync("reading the checkbox", () => Assertions.Expect(tick).ToBeVisibleAsync());
            // The card fades in, so wait out the fade before the look.
            await deadline.BoundAsync("letting the card settle", () => Task.Delay(500));
            await Shot("ballot-closed", "ballot");

            await deadline.BoundAsync("ticking the checkbox", () => tick.CheckAsync());
            await deadline.BoundAsync("reading the sentence", () => Assertions.Expect(presses.GetByLabel("Principle", new() { Exact = true })).ToHaveValueAsync("keep"));
            await Shot("ballot-ticked", "ballot");

            await deadline.BoundAsync("pressing Keep", () => presses.GetByRole(AriaRole.Button, new() { Name = "Keep", Exact = true }).ClickAsync());
            await deadline.BoundAsync("reading the confirmation", () => Assertions.Expect(card).ToContainTextAsync("Now a principle of"));
            await deadline.BoundAsync("reading the ambition's link", () => Assertions.Expect(card.Locator($"a[href='/issues/{seeded.Ambition}']")).ToHaveTextAsync(seeded.Ambition));
            await Shot("ballot-done", "ballot");

            Assert.Equal([$"- {SeededPrinciple}", $"- {AnswerSentence}", "- keep"], await PrincipleLines());

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

    private sealed record Seeded(string Ambition, string Story);

    /// <summary>
    /// An ambition with one principle, an epic under it and a story under that. The story holds
    /// one question answered, and one at merge left open before the story closes: a look-back.
    /// </summary>
    private static async Task<Seeded> SeedAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var project = await SendAsync<ProjectCreateRequest, ProjectDto>(http, HttpMethod.Post, "/api/projects", new("E2E", "E2E"));

        async Task<IssueDto> FileIssue(string type, string title, string? description = null, string? parentKey = null) =>
            await SendAsync<IssueCreateRequest, IssueDto>(http, HttpMethod.Post, "/api/issues", new(project.Id, type, title, description, parentKey, null, null));

        var ambition = await FileIssue("ambition", "The ambition with a principle", $"## Principles\n\n- {SeededPrinciple}");
        var epic = await FileIssue("epic", "The epic under it", null, ambition.Key);
        var story = await FileIssue("story", "The story with two questions", null, epic.Key);

        var answered = await SendAsync<CommentCreateRequest, CommentDto>(
            http, HttpMethod.Post, $"/api/issues/{story.Key}/comments", new("Q-answered: how big is the first press?", "question", null, null));
        await SendAsync<CommentCreateRequest, CommentDto>(
            http, HttpMethod.Post, $"/api/issues/{story.Key}/comments", new(AnswerSentence, "answer", answered.Id, null));

        var open = await SendAsync<CommentCreateRequest, CommentDto>(
            http, HttpMethod.Post, $"/api/issues/{story.Key}/comments", new("Q-open-merge: keep it or drop it?", "question", null, Recommended));
        await SendAsync<QuestionLevelRequest, QuestionDto>(
            http, HttpMethod.Put, $"/api/issues/{story.Key}/questions/{open.Id}/level", new(QuestionLevels.MergeName));

        var board = (await http.GetFromJsonAsync<BoardDto>("/api/board", Web))!;
        var done = board.Statuses.First(s => s.IsTerminal && !s.IsDeferred);
        await SendAsync<IssueMoveRequest, IssueDto>(http, HttpMethod.Post, $"/api/issues/{story.Key}/move", new(done.Id, null, null));

        return new(ambition.Key, story.Key);
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

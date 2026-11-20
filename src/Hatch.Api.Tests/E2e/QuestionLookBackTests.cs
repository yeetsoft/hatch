using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Microsoft.Playwright;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2532's acceptance, rendered: a story that shipped with a question asked
/// at merge still shows that question on the Ballot, and its issue page says
/// the question waits for a look back. A question at hold on a shipped story
/// is on neither, and keeps the old welcome-not-needed sentence.
///
/// <para>The real API, real bundle and real asks. Each look is saved as a PNG
/// beside the case's artifacts.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class QuestionLookBackTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly QuestionOptionDto[] Recommended = [new("keep", "Costs nothing", true), new("drop", "Costs a rewrite", false)];

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task AMergeQuestion_WaitsForALookBack_AtTheDesktopProfile() => RunAsync("QuestionLookBackDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task AMergeQuestion_WaitsForALookBack_AtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("QuestionLookBackPhone", "phone", DeviceProfile.IPhone14Pro);
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
            var seeded = await deadline.BoundAsync("filing the shipped stories", () => SeedAsync(baseUrl));

            await harness.OpenAsync(deadline, baseUrl, $"/issues/{seeded.Merge}");
            var page = harness.Page!;
            var title = page.Locator("section .hatch-section-title:has-text(\"Asked\")");
            var lead = page.Locator("section:has(.hatch-section-title:has-text(\"Asked\")) > p.text-muted");

            await harness.WaitForVisibleAsync(deadline, ".hatch-question:has-text(\"Q-merge\")");
            await deadline.BoundAsync("reading the title", () => Assertions.Expect(title).ToHaveTextAsync("Asked (1)"));
            await deadline.BoundAsync("reading the look back", () => Assertions.Expect(lead).ToContainTextAsync("A question asked at merge waits for a look back"));
            output.WriteLine($"saved {await harness.FullPageScreenshotAsync(deadline, $"look-back-issue-{profileName}")}");

            await harness.GoToAsync(deadline, $"/issues/{seeded.Hold}");
            await harness.WaitForVisibleAsync(deadline, ".hatch-question:has-text(\"Q-hold\")");
            await deadline.BoundAsync("reading the old sentence", () => Assertions.Expect(lead).ToHaveTextAsync($"{seeded.Hold} shipped with these open. An answer is welcome, not needed."));

            await harness.GoToAsync(deadline, "/ballot?view=list");
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForVisibleAsync(deadline, ".hatch-ballot .hatch-question:has-text(\"Q-merge\")");
            Assert.Equal(0, await deadline.BoundAsync("looking for the hold question", () => page.Locator(".hatch-ballot .hatch-question:has-text(\"Q-hold\")").CountAsync()));
            output.WriteLine($"saved {await harness.FullPageScreenshotAsync(deadline, $"look-back-ballot-{profileName}")}");

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

    private sealed record Seeded(string Merge, string Hold);

    /// <summary>Two stories, each with one question, then both moved into a terminal column. One question is set to merge.</summary>
    private static async Task<Seeded> SeedAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var project = await SendAsync<ProjectCreateRequest, ProjectDto>(http, HttpMethod.Post, "/api/projects", new("E2E", "E2E"));
        var merge = await FileAsync(http, project.Id, "The story with a merge question");
        var hold = await FileAsync(http, project.Id, "The story with a hold question");

        var mergeQuestion = await AskAsync(http, merge.Key, "Q-merge: keep it or drop it?");
        await AskAsync(http, hold.Key, "Q-hold: keep it or drop it?");
        await SendAsync<QuestionLevelRequest, QuestionDto>(
            http, HttpMethod.Put, $"/api/issues/{merge.Key}/questions/{mergeQuestion.Id}/level", new(QuestionLevels.MergeName));

        var board = (await http.GetFromJsonAsync<BoardDto>("/api/board", Web))!;
        var done = board.Statuses.First(s => s.IsTerminal && !s.IsDeferred);
        foreach (var key in new[] { merge.Key, hold.Key })
            await SendAsync<IssueMoveRequest, IssueDto>(http, HttpMethod.Post, $"/api/issues/{key}/move", new(done.Id, null, null));

        return new(merge.Key, hold.Key);
    }

    private static Task<CommentDto> AskAsync(HttpClient http, string key, string body) =>
        SendAsync<CommentCreateRequest, CommentDto>(http, HttpMethod.Post, $"/api/issues/{key}/comments", new(body, "question", null, Recommended));

    private static Task<IssueDto> FileAsync(HttpClient http, int projectId, string title) =>
        SendAsync<IssueCreateRequest, IssueDto>(
            http, HttpMethod.Post, "/api/issues", new(projectId, "story", title, null, null, null, null));

    private static async Task<TResponse> SendAsync<TRequest, TResponse>(HttpClient http, HttpMethod method, string path, TRequest body)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body, options: Web) };
        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{method} {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

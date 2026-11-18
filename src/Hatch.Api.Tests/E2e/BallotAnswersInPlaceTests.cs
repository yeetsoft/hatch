using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Microsoft.Playwright;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2436's acceptance, rendered: a question on the Ballot is answered where
/// it is listed. The recommended option is one press and Enter; the row, and
/// the issues left with nothing beneath them, leave the page; the bar's
/// waiting count drops with no reload. A refused answer keeps the row, and what
/// was typed, with the sentence beside it.
///
/// <para>The real API, real bundle and a real ask: an epic holding a story
/// holding a task with the question, so the Ballot has a nested container to
/// lose, and a second epic whose question is still waiting afterwards. The
/// refusal is a stubbed answer, since the API refuses nothing an operator
/// could do here. Each look is saved as a PNG beside the case's artifacts.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class BallotAnswersInPlaceTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Refusal = "That answer was refused for the test.";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheRecommendedOption_IsOnePressAndEnter_AtTheDesktopProfile() =>
        RunAsync("BallotAnswerDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheRecommendedOption_IsOnePressAndEnter_AtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("BallotAnswerPhone", "phone", DeviceProfile.IPhone14Pro);
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
            var seeded = await deadline.BoundAsync("filing the questions", () => SeedAsync(baseUrl));

            await harness.OpenAsync(deadline, baseUrl, "/ballot");
            await harness.WaitForTopBarAsync(deadline);

            var row = (string key) => $".hatch-ballot .hatch-question:has-text(\"{key} question\")";
            await harness.WaitForVisibleAsync(deadline, row(seeded.Task));
            await harness.WaitForVisibleAsync(deadline, row(seeded.Other));

            var page = harness.Page!;
            var bar = page.Locator(E2eHarness.AttentionSelector);
            await deadline.BoundAsync("waiting for the bar", () => Assertions.Expect(bar).ToHaveAttributeAsync("aria-label", "2 questions to answer"));
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"before-{profileName}")}");

            // A refused answer first: the row stays, with what was typed and the sentence beside it.
            await deadline.BoundAsync("stubbing a refusal", () => page.RouteAsync($"**/api/issues/{seeded.Other}/comments", route =>
                route.FulfillAsync(new() { Status = 409, ContentType = "application/json", Body = JsonSerializer.Serialize(Refusal) })));

            var other = page.Locator(row(seeded.Other));
            await deadline.BoundAsync("typing", () => other.GetByLabel("Your answer").FillAsync("a sentence that will be refused"));
            await deadline.BoundAsync("pressing Enter", () => other.GetByLabel("Your answer").PressAsync("Enter"));
            await harness.WaitForVisibleAsync(deadline, $"{row(seeded.Other)} .text-danger:text-is(\"{Refusal}\")");
            Assert.Equal(
                "a sentence that will be refused",
                await deadline.BoundAsync("reading the box", () => other.GetByLabel("Your answer").InputValueAsync()));
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"refused-{profileName}")}");
            await harness.UnrouteAllAsync(deadline);

            // The recommended option: one press, then Enter.
            var task = page.Locator(row(seeded.Task));
            var keep = task.GetByRole(AriaRole.Button, new() { Name = "keep" });
            await deadline.BoundAsync("pressing the recommended option", () => keep.ClickAsync());
            Assert.Equal("keep", await deadline.BoundAsync("reading the box", () => task.GetByLabel("Your answer").InputValueAsync()));
            await deadline.BoundAsync("pressing Enter", () => task.GetByLabel("Your answer").PressAsync("Enter"));

            await harness.WaitForHiddenAsync(deadline, row(seeded.Task));
            // The story and the epic over it held nothing else, so they leave too.
            await harness.WaitForHiddenAsync(deadline, $".hatch-ballot-key:text-is(\"{seeded.Story}\")");
            await harness.WaitForHiddenAsync(deadline, $".hatch-ballot-key:text-is(\"{seeded.Epic}\")");
            Assert.True(await harness.IsVisibleAsync(deadline, row(seeded.Other)));

            // The bar drops by itself: nothing here reloads the page.
            await deadline.BoundAsync("waiting for the bar", () => Assertions.Expect(bar).ToHaveAttributeAsync("aria-label", "1 question to answer"));
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"after-{profileName}")}");

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

    private sealed record Seeded(string Epic, string Story, string Task, string Other);

    /// <summary>An epic over a story over a task with a question that offers two options, and a second epic with a question asked in prose.</summary>
    private static async Task<Seeded> SeedAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("E2E", "E2E"));
        var epic = await FileAsync(http, project.Id, "epic", "The epic", null);
        var story = await FileAsync(http, project.Id, "story", "The story", epic.Key);
        var task = await FileAsync(http, project.Id, "task", "The task", story.Key);
        var other = await FileAsync(http, project.Id, "epic", "The other epic", null);

        await PostAsync<CommentCreateRequest, CommentDto>(http, $"/api/issues/{task.Key}/comments", new(
            $"{task.Key} question: keep it or drop it?",
            "question",
            null,
            [new("keep", "Costs nothing", true), new("drop", "Costs a rewrite", false)]));
        await PostAsync<CommentCreateRequest, CommentDto>(http, $"/api/issues/{other.Key}/comments", new(
            $"{other.Key} question: which name should it have?", "question"));

        return new(epic.Key, story.Key, task.Key, other.Key);
    }

    private static Task<IssueDto> FileAsync(HttpClient http, int projectId, string type, string title, string? parentKey) =>
        PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(projectId, type, title, null, parentKey, null, null));

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

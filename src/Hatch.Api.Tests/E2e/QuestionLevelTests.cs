using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Microsoft.Playwright;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2533's acceptance, rendered: a question's level is shown in words and set
/// where the question is drawn. The issue page's Waiting card and the Ballot
/// each show a question at hold, build and merge, a question with no
/// recommendation offers only hold, a refused change keeps the level and shows
/// the sentence beside the control, and the card's first line follows the
/// strictest level.
///
/// <para>The real API, real bundle and real asks. The refusal is a stubbed
/// reply, since the API refuses nothing a person could do here that the page
/// offers. Each look is saved as a PNG beside the case's artifacts.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class QuestionLevelTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Refusal = "That level was refused for the test.";

    private const string HoldWords = "nothing moves until this is answered";
    private const string BuildWords = "built but not merged until this is answered";
    private const string MergeWords = "merges now; the answer is looked over after";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheLevel_IsShownAndSet_AtTheDesktopProfile() => RunAsync("QuestionLevelDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheLevel_IsShownAndSet_AtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("QuestionLevelPhone", "phone", DeviceProfile.IPhone14Pro);
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

            await harness.OpenAsync(deadline, baseUrl, $"/issues/{seeded.Many}");
            var page = harness.Page!;
            var row = (string tag) => $".hatch-waiting .hatch-question:has-text(\"{tag}\")";
            var level = (string tag) => page.Locator(row(tag)).Locator(".hatch-question-level-step.here");
            var press = (string tag, string word) =>
                deadline.BoundAsync($"pressing {word} on {tag}", () =>
                    page.Locator(row(tag)).GetByRole(AriaRole.Button, new() { Name = word, Exact = true }).ClickAsync());
            var lead = page.Locator(".hatch-waiting > p.text-muted");

            // The issue with four questions: a lead that follows the strictest.
            await harness.WaitForVisibleAsync(deadline, row("Q-one"));
            await Expect(deadline, "the lead at hold", lead, $"{seeded.Many} will not be picked up again until these are answered.");
            foreach (var tag in new[] { "Q-one", "Q-two", "Q-three", "Q-none" })
                await Expect(deadline, $"{tag} words", page.Locator(row(tag)).Locator(".hatch-question-level-words"), HoldWords);

            // No recommendation: only hold, and the sentence says why.
            var none = page.Locator(row("Q-none"));
            Assert.Equal(1, await deadline.BoundAsync("counting buttons", () => none.Locator(".hatch-question-level-step").CountAsync()));
            await deadline.BoundAsync("reading the note", () => Assertions.Expect(none.Locator(".hatch-question-level-note")).ToContainTextAsync("no recommendation"));

            // A refused change keeps the level and shows the sentence beside the control.
            await deadline.BoundAsync("stubbing a refusal", () => page.Context.RouteAsync("**/api/issues/*/questions/*/level", route =>
                route.FulfillAsync(new() { Status = 400, ContentType = "application/json", Body = JsonSerializer.Serialize(Refusal) })));
            await press("Q-three", "build");
            await harness.WaitForVisibleAsync(deadline, $"{row("Q-three")} .hatch-question-level .text-danger:text-is(\"{Refusal}\")");
            await Expect(deadline, "Q-three still holds", level("Q-three"), "hold");
            output.WriteLine($"saved {await ShotAsync(harness, deadline, $"refused-{profileName}")}");
            await harness.UnrouteAllAsync(deadline);

            // The real route: build, then merge.
            await press("Q-one", "build");
            await Expect(deadline, "Q-one at build", level("Q-one"), "build");
            await Expect(deadline, "Q-one words", page.Locator(row("Q-one")).Locator(".hatch-question-level-words"), BuildWords);
            await press("Q-two", "merge");
            await Expect(deadline, "Q-two at merge", level("Q-two"), "merge");
            await Expect(deadline, "Q-two words", page.Locator(row("Q-two")).Locator(".hatch-question-level-words"), MergeWords);

            // Q-three held throughout, and its refusal stays beside it until it is pressed again.
            await Expect(deadline, "Q-three at hold", level("Q-three"), "hold");
            Assert.Equal(1, await deadline.BoundAsync("counting refusals", () => page.Locator($"{row("Q-three")} .text-danger").CountAsync()));

            // Q-three and Q-none still hold, so the card still leads with hold.
            await Expect(deadline, "the lead, one question holding", lead, $"{seeded.Many} will not be picked up again until these are answered.");
            output.WriteLine($"saved {await ShotAsync(harness, deadline, $"issue-levels-{profileName}")}");
            AssertNoOverflow(await ScrollWidthsAsync(harness, deadline), profileName, "the issue page");

            // The lead follows the strictest level, one question at a time.
            await harness.GoToAsync(deadline, $"/issues/{seeded.Lone}");
            await harness.WaitForVisibleAsync(deadline, row("Q-lone"));
            await press("Q-lone", "build");
            await Expect(deadline, "the lead at build", lead, $"{seeded.Lone} will be built on the recommendation and wait in review until these are answered.");
            await press("Q-lone", "merge");
            await Expect(deadline, "the lead at merge", lead, $"{seeded.Lone} will be built and merged on the recommendation; these wait for a look back.");
            output.WriteLine($"saved {await ShotAsync(harness, deadline, $"issue-merge-{profileName}")}");

            // The Ballot draws what the issue page set, and sets it too.
            await harness.GoToAsync(deadline, "/ballot");
            await harness.WaitForTopBarAsync(deadline);
            var ballot = (string tag) => $".hatch-ballot .hatch-question:has-text(\"{tag}\")";
            await harness.WaitForVisibleAsync(deadline, ballot("Q-one"));
            await Expect(deadline, "Q-one on the Ballot", page.Locator(ballot("Q-one")).Locator(".hatch-question-level-words"), BuildWords);
            await Expect(deadline, "Q-two on the Ballot", page.Locator(ballot("Q-two")).Locator(".hatch-question-level-words"), MergeWords);
            await Expect(deadline, "Q-three on the Ballot", page.Locator(ballot("Q-three")).Locator(".hatch-question-level-words"), HoldWords);
            await deadline.BoundAsync("pressing build on the Ballot", () =>
                page.Locator(ballot("Q-three")).GetByRole(AriaRole.Button, new() { Name = "build", Exact = true }).ClickAsync());
            await Expect(deadline, "Q-three set on the Ballot", page.Locator(ballot("Q-three")).Locator(".hatch-question-level-words"), BuildWords);
            Assert.Equal(1, await deadline.BoundAsync("counting buttons", () => page.Locator(ballot("Q-none")).Locator(".hatch-question-level-step").CountAsync()));
            output.WriteLine($"saved {await ShotAsync(harness, deadline, $"ballot-levels-{profileName}")}");
            AssertNoOverflow(await ScrollWidthsAsync(harness, deadline), profileName, "the Ballot");

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

    private static Task Expect(Deadline deadline, string phase, ILocator locator, string text) =>
        deadline.BoundAsync(phase, () => Assertions.Expect(locator).ToHaveTextAsync(text));

    /// <summary>The pills fade between states, so wait out the fade: a look taken mid-fade shows two levels half marked.</summary>
    private static async Task<string> ShotAsync(E2eHarness harness, Deadline deadline, string label)
    {
        await deadline.BoundAsync("letting the pills settle", () => Task.Delay(500));
        return await harness.FullPageScreenshotAsync(deadline, label);
    }

    private static async Task<(int Scroll, int Inner)> ScrollWidthsAsync(E2eHarness harness, Deadline deadline)
    {
        var widths = await harness.EvaluateAsync<int[]>(
            deadline, "measuring the page", "() => [document.documentElement.scrollWidth, window.innerWidth]");
        return (widths[0], widths[1]);
    }

    private static void AssertNoOverflow((int Scroll, int Inner) widths, string profileName, string what) =>
        Assert.True(widths.Scroll <= widths.Inner, $"{what} scrolls sideways at the {profileName} profile: {widths.Scroll}px in {widths.Inner}px");

    private sealed record Seeded(string Many, string Lone);

    private static readonly QuestionOptionDto[] Recommended = [new("keep", "Costs nothing", true), new("drop", "Costs a rewrite", false)];

    /// <summary>One story with three questions that recommend something and one that does not, and a second with a single question.</summary>
    private static async Task<Seeded> SeedAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("E2E", "E2E"));
        var many = await FileAsync(http, project.Id, "story", "The story with four questions");
        var lone = await FileAsync(http, project.Id, "story", "The story with one question");

        foreach (var tag in new[] { "Q-one", "Q-two", "Q-three" })
            await AskAsync(http, many.Key, $"{tag}: keep it or drop it?", Recommended);
        await AskAsync(http, many.Key, "Q-none: which name should it have?", null);
        await AskAsync(http, lone.Key, "Q-lone: keep it or drop it?", Recommended);

        return new(many.Key, lone.Key);
    }

    private static Task<CommentDto> AskAsync(HttpClient http, string key, string body, IReadOnlyList<QuestionOptionDto>? options) =>
        PostAsync<CommentCreateRequest, CommentDto>(http, $"/api/issues/{key}/comments", new(body, "question", null, options));

    private static Task<IssueDto> FileAsync(HttpClient http, int projectId, string type, string title) =>
        PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(projectId, type, title, null, null, null, null));

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

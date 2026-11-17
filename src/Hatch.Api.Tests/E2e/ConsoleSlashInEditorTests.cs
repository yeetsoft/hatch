using System.Net.Http.Json;
using System.Text.Json;
using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2376: a <c>/</c> typed in a markdown box is a slash and never opens the
/// console. In Chromium Monaco focuses a div with an EditContext rather than a
/// textarea, which the page's typing check did not know, so the console took the
/// key. Driven on the issue page's Description editor and on the board peek's.
/// Both fail in Chromium without the fix.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class ConsoleSlashInEditorTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 120;

    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Prompt = ".hatch-omni-input";
    private const string EditorHost = ".hatch-md-editor";
    private const string EditorText = ".hatch-md-editor .view-lines";

    private const string PageSection = "details.hatch-issue-section:has(> summary:text-is(\"Description\"))";
    private const string PeekDescription = "[aria-modal='true'] .hatch-peek-description";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task IssuePage_SlashInTheDescriptionIsASlash() => RunAsync(peek: false);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task BoardPeek_SlashInTheDescriptionIsASlash() => RunAsync(peek: true);

    private async Task RunAsync(bool peek)
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness(peek ? "ConsoleSlashInEditor-peek" : "ConsoleSlashInEditor-page");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);

            using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
            var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("SLSH", "Slash Checks"));
            var task = await PostAsync<IssueCreateRequest, IssueDto>(
                http, "/api/issues", new(project.Id, "task", "A description to type a slash into", null, null, null, null));

            string scope;
            if (peek)
            {
                await harness.OpenBoardAsync(deadline, baseUrl);
                var card = $"[data-issue-key='{task.Key}']";
                await harness.WaitForVisibleAsync(deadline, card);
                await deadline.BoundAsync("peeking the card", () => harness.Page!.Locator(card).ClickAsync());
                await harness.WaitForVisibleAsync(deadline, PeekDescription);
                scope = PeekDescription;
            }
            else
            {
                await harness.OpenAsync(deadline, baseUrl, $"/issues/{task.Key}");
                await harness.WaitForTopBarAsync(deadline);
                scope = PageSection;
            }

            await harness.ClickAsync(deadline, $"{scope} .hatch-section-actions button:text-is(\"Edit\")");
            await harness.WaitForVisibleAsync(deadline, $"{scope} {EditorHost}");
            await harness.ClickAsync(deadline, $"{scope} {EditorText}");

            await deadline.BoundAsync("typing a slash", () => harness.Page!.Keyboard.PressAsync("/"));

            // Save is offered on a dirty draft alone, so it appearing is the slash having been typed.
            await harness.WaitForVisibleAsync(deadline, $"{scope} .hatch-section-actions button:text-is(\"Save\")");

            var text = await deadline.BoundAsync("reading the editor", () => harness.Page!.Locator($"{scope} {EditorText}").InnerTextAsync());
            Assert.Contains("/", text);
            Assert.False(
                await harness.IsVisibleAsync(deadline, Prompt),
                "The console opened over the editor: the slash was taken as a shortcut.");

            output.WriteLine("after the slash: " + await harness.ScreenshotAsync(deadline, peek ? "1-peek" : "1-page"));
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

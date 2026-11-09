using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2067's acceptance, rendered: on the issue page's edit view a person saves
/// the reason a ticket finished with no code and the facts panel's
/// <c>No code</c> row shows it; Clear takes the row away; and a ticket that has
/// a pull request shows the API's own refusal beside the control and carries no
/// declaration. Run at the desktop viewport in Chromium.
///
/// <para>The edit view replaces the facts panel, so the row is only read after
/// <c>Done</c>. Each step is saved as a PNG beside the case's other artifacts,
/// because a green assert is not a look.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class NoCodeFieldTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Reason = "The behaviour was already there";

    /// <summary>The Details panel's own Edit/Done button - not the status pill that is also called Done.</summary>
    private const string Toggle = ".hatch-section-head:has(h2:text-is(\"Details\")) button";

    private const string Field = ".hatch-field:has-text(\"Finished with no code\")";

    private const string Refusal = Field + " .text-danger";

    /// <summary>The facts panel's rows labelled <c>No code</c>, counted rather than waited for, so absence cannot pass by timing out.</summary>
    private const string CountRowsScript = """
        () => [...document.querySelectorAll('.hatch-field__label')].filter((el) => el.textContent === 'No code').length
        """;

    private const string RowTextScript = """
        () => [...document.querySelectorAll('.hatch-field__label')]
          .filter((el) => el.textContent === 'No code')
          .map((el) => el.closest('.hatch-field').textContent)
          .join('|')
        """;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheField_SavesClearsAndShowsTheRefusal_AtTheDesktopProfile() => RunAsync("NoCodeFieldDesktop");

    private async Task RunAsync(string name)
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness(name);
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            var (clean, withPullRequest) = await deadline.BoundAsync("filing two tasks", () => FileTwoTasksAsync(baseUrl));

            // (1) Save a reason on the ticket with no pull request.
            await harness.OpenAsync(deadline, baseUrl, $"/apps/hatch/issues/{clean}");
            await harness.WaitForVisibleAsync(deadline, ".hatch-issue-page");
            await harness.ClickAsync(deadline, Toggle);
            await harness.WaitForVisibleAsync(deadline, Field);
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, "edit-at-rest")}");
            Assert.False(await harness.IsVisibleAsync(deadline, $"{Field} button:text-is(\"Clear\")"), "Clear is showing with nothing declared");

            await harness.ClickAsync(deadline, $"{Field} input");
            await harness.InsertTextAsync(deadline, Reason);
            await harness.ClickAsync(deadline, $"{Field} button:text-is(\"Save\")");
            await harness.WaitForVisibleAsync(deadline, $"{Field} button:text-is(\"Clear\")");
            Assert.Equal(Reason, await harness.EvaluateOnAsync<string>(deadline, "reading the field", $"{Field} input", "(el) => el.value"));
            Assert.False(await harness.IsVisibleAsync(deadline, Refusal), "a refusal is showing after a save that went through");
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, "saved")}");

            await harness.ClickAsync(deadline, Toggle);
            await harness.WaitForVisibleAsync(deadline, ".hatch-field__label:text-is(\"No code\")");
            var row = await harness.EvaluateAsync<string>(deadline, "reading the row", RowTextScript);
            output.WriteLine($"row after save: {row}");
            Assert.Contains($"Finished with no code: {Reason}", row);
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, "read-view-with-row")}");

            // (2) Clear it.
            await harness.ClickAsync(deadline, Toggle);
            await harness.ClickAsync(deadline, $"{Field} button:text-is(\"Clear\")");
            await harness.WaitForHiddenAsync(deadline, $"{Field} button:text-is(\"Clear\")");
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, "cleared")}");

            await harness.ClickAsync(deadline, Toggle);
            await harness.WaitForHiddenAsync(deadline, Field);
            Assert.Equal(0, await harness.EvaluateAsync<int>(deadline, "counting the rows", CountRowsScript));
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, "read-view-row-gone")}");

            // (3) The ticket with a pull request: the API's sentence, and no declaration.
            await harness.GoToAsync(deadline, $"issues/{withPullRequest}");
            await harness.WaitForVisibleAsync(deadline, ".hatch-issue-page");
            await harness.ClickAsync(deadline, Toggle);
            await harness.WaitForVisibleAsync(deadline, Field);
            await harness.ClickAsync(deadline, $"{Field} input");
            await harness.InsertTextAsync(deadline, Reason);
            await harness.ClickAsync(deadline, $"{Field} button:text-is(\"Save\")");
            await harness.WaitForVisibleAsync(deadline, Refusal);
            var sentence = await harness.EvaluateOnAsync<string>(deadline, "reading the refusal", Refusal, "(el) => el.textContent");
            output.WriteLine($"refusal: {sentence}");
            Assert.Contains(withPullRequest, sentence);
            Assert.Equal(Reason, await harness.EvaluateOnAsync<string>(deadline, "reading the field", $"{Field} input", "(el) => el.value"));
            Assert.False(await harness.IsVisibleAsync(deadline, $"{Field} button:text-is(\"Clear\")"), "Clear is showing after a refused save");
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, "refused")}");

            await harness.ClickAsync(deadline, Toggle);
            await harness.WaitForHiddenAsync(deadline, Field);
            Assert.Equal(0, await harness.EvaluateAsync<int>(deadline, "counting the rows", CountRowsScript));
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, "refused-read-view")}");

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

    /// <summary>One project, filed the way any caller files one with the wall off, and two tasks under it; the second is given a pull request.</summary>
    private static async Task<(string Clean, string WithPullRequest)> FileTwoTasksAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/hatch/projects", new("NCF", "NCF"));
        var keys = new List<string>();
        foreach (var title in new[] { "A task with nothing recorded", "A task with a pull request" })
        {
            var issue = await PostAsync<IssueCreateRequest, IssueDto>(
                http, "/api/hatch/issues", new(project.Id, "task", title, null, null, null, null));
            keys.Add(issue.Key);
        }

        using var patched = await http.PatchAsJsonAsync(
            $"/api/hatch/issues/{keys[1]}", new { pullRequestUrl = "https://example.com/pull/1" }, Web);
        Assert.True(patched.IsSuccessStatusCode, $"PATCH answered {(int)patched.StatusCode}: {await patched.Content.ReadAsStringAsync()}");

        return (keys[0], keys[1]);
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

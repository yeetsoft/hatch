using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2715: an issue with a pull request written and waiting to be opened,
/// rendered against the real host and the built bundle. The subheader draws a
/// "pull request waiting" chip; the Details card's Pull request field says it
/// was written and is waiting, who wrote it and why, and holds the title and
/// the body behind a <c>&lt;details&gt;</c>. Recording a url then replaces both
/// with the link. Run at the desktop profile and at <c>iPhone 14 Pro</c>; the
/// body carries a line with no break in it, which is what would push the card
/// wider than a phone, so the document must not scroll sideways at either.
/// A screenshot of each stage is left in the case's artifact directory.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class WaitingPullRequestPageTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 120;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string WaitingTitle = "Pull requests: a waiting one the page shows";
    private const string Why = "the forge refused the push from the runner";
    private const string RecordedUrl = "https://forge.example/owner/repo/pull/42";
    private static readonly string Body =
        "## What landed\n\nOne sentence.\n\n" + new string('x', 160) + "\n\n- a list item\n";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheIssuePage_DrawsAWaitingPullRequest_AtTheDesktopProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        return RunAsync("WaitingPullRequestDesktop", null);
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheIssuePage_DrawsAWaitingPullRequest_AtThePhoneProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);

        return RunAsync("WaitingPullRequestPhone", DeviceProfile.IPhone14Pro);
    }

    private async Task RunAsync(string name, DeviceProfile? profile)
    {
        var harness = new E2eHarness(name);
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        var where = profile is null ? "desktop" : profile.Device;
        try
        {
            await harness.LaunchBrowserAsync(deadline, profile);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            var key = await deadline.BoundAsync("filing the issue and writing the pull request", () => SeedAsync(baseUrl));

            await harness.OpenAsync(deadline, baseUrl, $"/issues/{key}");
            await harness.WaitForTopBarAsync(deadline);

            // 1. The subheader chip and the Details field say it is waiting.
            const string chip = ".hatch-chip:has-text(\"waiting\")";
            await harness.WaitForVisibleAsync(deadline, chip);
            var chipText = await harness.EvaluateAsync<string>(
                deadline, "reading the chip", "() => [...document.querySelectorAll('.hatch-chip')].find(c => c.textContent.includes('waiting')).textContent");
            output.WriteLine($"chip ({where}): {chipText}");
            Assert.Contains("Pull request", chipText);

            const string fact = ".hatch-waiting-pr";
            await harness.WaitForVisibleAsync(deadline, fact);
            var factText = await harness.EvaluateAsync<string>(
                deadline, "reading the fact", $"() => document.querySelector('{fact}').innerText");
            output.WriteLine($"fact ({where}): {factText}");
            Assert.Contains("Written, waiting to be opened", factText);
            Assert.Contains(Why, factText);
            Assert.Contains(WaitingTitle, factText);
            Assert.Equal(
                0,
                await harness.EvaluateAsync<int>(
                    deadline, "counting links to a pull request", "() => document.querySelectorAll('a[href*=\"/pull/\"]').length"));
            AssertNoWriteControl(
                await harness.EvaluateAsync<int>(
                    deadline, "counting controls in the fact", $"() => document.querySelectorAll('{fact} button, {fact} input, {fact} textarea').length"));
            Assert.False(
                await harness.EvaluateAsync<bool>(
                    deadline, "reading whether the body is open", $"() => document.querySelector('{fact} details').open"),
                "the body is open before anyone asked for it");
            await AssertFitsAsync(harness, deadline, where, "closed");
            output.WriteLine("screenshot: " + await harness.ScreenshotAsync(deadline, "1-waiting"));

            // 2. Opening the title shows the branch and the body as written, wrapped inside the card.
            await harness.ClickAsync(deadline, $"{fact} details summary");
            await harness.WaitForVisibleAsync(deadline, ".hatch-waiting-pr__body");
            var bodyText = await harness.EvaluateAsync<string>(
                deadline, "reading the body", "() => document.querySelector('.hatch-waiting-pr__body').textContent");
            Assert.Equal(Body, bodyText);
            Assert.Contains(
                "ha-2715-a-waiting-one",
                await harness.EvaluateAsync<string>(
                    deadline, "reading the branch", $"() => document.querySelector('{fact} details').innerText"));
            await AssertFitsAsync(harness, deadline, where, "open");
            output.WriteLine("screenshot: " + await harness.ScreenshotAsync(deadline, "2-body-open"));

            // 3. Recording a url clears it: the link is drawn and the chip and the sentence are gone.
            await deadline.BoundAsync("recording the url", () => RecordUrlAsync(baseUrl, key));
            await harness.OpenAsync(deadline, baseUrl, $"/issues/{key}");
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForVisibleAsync(deadline, "a[href*=\"/pull/42\"]");
            Assert.Equal(
                0,
                await harness.EvaluateAsync<int>(
                    deadline, "counting waiting marks", "() => document.querySelectorAll('.hatch-waiting-pr').length"));
            Assert.Equal(
                0,
                await harness.EvaluateAsync<int>(
                    deadline, "counting waiting chips", "() => [...document.querySelectorAll('.hatch-chip')].filter(c => c.textContent.includes('waiting')).length"));
            await AssertFitsAsync(harness, deadline, where, "after the url is recorded");
            output.WriteLine("screenshot: " + await harness.ScreenshotAsync(deadline, "3-recorded"));

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

    private static void AssertNoWriteControl(int count) =>
        Assert.True(count == 0, "the waiting pull request draws a control that could write");

    /// <summary>The document does not scroll sideways, and nothing in the fact reaches past the viewport.</summary>
    private async Task AssertFitsAsync(E2eHarness harness, Deadline deadline, string where, string stage)
    {
        var measured = await harness.EvaluateAsync<string>(
            deadline,
            "measuring the page",
            """
            () => {
              const root = document.documentElement;
              const fact = document.querySelector('.hatch-waiting-pr');
              const right = fact ? Math.max(...[fact, ...fact.querySelectorAll('*')].map(e => e.getBoundingClientRect().right)) : 0;
              return JSON.stringify({ scrollWidth: root.scrollWidth, clientWidth: root.clientWidth, factRight: right });
            }
            """);
        output.WriteLine($"--- fit, {where}, {stage}: {measured}");
        var m = JsonDocument.Parse(measured).RootElement;
        Assert.True(
            m.GetProperty("scrollWidth").GetDouble() <= m.GetProperty("clientWidth").GetDouble() + 0.5,
            $"{where} {stage}: the page scrolls sideways: {measured}");
        Assert.True(
            m.GetProperty("factRight").GetDouble() <= m.GetProperty("clientWidth").GetDouble() + 0.5,
            $"{where} {stage}: the waiting pull request reaches past the viewport: {measured}");
    }

    private static async Task<string> SeedAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("E2E", "E2E"));
        var issue = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "task", "An issue whose pull request is waiting", null, null, null, null));

        using var response = await http.PutAsJsonAsync(
            $"/api/issues/{issue.Key}/waiting-pull-request",
            new WaitingPullRequestRequest(WaitingTitle, Body, "ha-2715-a-waiting-one", "2222222222222222222222222222222222222222", "forge.example/owner/repo", Why),
            Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"PUT waiting-pull-request answered {(int)response.StatusCode}: {text}");

        return issue.Key;
    }

    private static async Task RecordUrlAsync(string baseUrl, string key)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/issues/{key}")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { pullRequestUrl = RecordedUrl }), Encoding.UTF8, "application/json"),
        };
        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"PATCH answered {(int)response.StatusCode}: {text}");
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2247's acceptance, rendered: clicking the Consumption report's Download
/// CSV link downloads a file named by the server (<c>consumption-&lt;date&gt;-&lt;date&gt;.csv</c>),
/// whose first line is the CSV header row. Run at the desktop profile only -
/// the link itself draws no differently on a phone.
/// </summary>
/// <remarks>
/// The session is written straight to the database rather than posted: the
/// work-log route takes a key and nothing else, and this host has none.
/// </remarks>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class ConsumptionDownloadCsvTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task Consumption_DownloadsTheCsv()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("ConsumptionDownloadCsv");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline, profile: null);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            var key = await deadline.BoundAsync("filing an epic", () => FileAsync(baseUrl));
            await deadline.BoundAsync("logging a session", () => LogAsync(harness.Services, key));

            await harness.OpenAsync(deadline, baseUrl, "/reports/consumption");
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForVisibleAsync(deadline, "a.hatch-consumption-csv");
            output.WriteLine("screenshot: " + await harness.ScreenshotAsync(deadline, "consumption-before-download"));

            var download = await deadline.BoundAsync(
                "downloading the CSV",
                () => harness.Page!.RunAndWaitForDownloadAsync(() => harness.ClickAsync(deadline, "a.hatch-consumption-csv")));

            Assert.Matches("^consumption-\\d{4}-\\d{2}-\\d{2}-\\d{4}-\\d{2}-\\d{2}\\.csv$", download.SuggestedFilename);

            var path = await download.PathAsync();
            Assert.NotNull(path);
            var header = File.ReadLines(path!).First();
            Assert.Equal(string.Join(',', ConsumptionCsv.Header), header);

            output.WriteLine("screenshot: " + await harness.ScreenshotAsync(deadline, "consumption-after-download"));

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

    /// <summary>A project and an epic filed the way any caller files them with the wall off. Returns the epic's key.</summary>
    private static async Task<string> FileAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("CND", "CND"));
        var issue = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "epic", "An epic that consumed something", null, null, null, null));

        return issue.Key;
    }

    /// <summary>One priced session, a day ago, so the filter's window has a row to put in the CSV.</summary>
    private static async Task LogAsync(IServiceProvider services, string key)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
        var now = DateTimeOffset.UtcNow;

        Assert.True(IssueKey.TryParse(key, out var projectKey, out var number));
        var issueId = db.Issues.WithKey(projectKey, number).Select(i => i.Id).Single();

        var at = now.AddDays(-1);
        db.WorkLog.Add(new EfHatchWorkLogEntry
        {
            IssueId = issueId,
            SessionId = "cnd-1",
            StartedAt = at.AddMinutes(-30),
            EndedAt = at,
            DurationMs = 1_800_000,
            IssueType = "epic",
            CostUsd = 2.40m,
            InputTokens = 90_000,
            OutputTokens = 90_000,
            CacheCreationTokens = 90_000,
            CacheReadTokens = 630_000,
            CreatedAt = now,
        });

        await db.SaveChangesAsync();
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2450's acceptance, rendered: the issue page's Work log draws how each
/// session ended. Four sessions are logged, then three of them are released
/// through the real <c>DELETE /api/issues/{key}/claim</c> with the session id
/// the CLI sends - dropped for the quiet limit, dropped for an error, worked -
/// and the fourth is left as a row written before the outcome existed. The page
/// must say <c>dropped - quiet</c>, <c>dropped - error</c> and <c>worked</c> on
/// the first three and nothing at all on the fourth. Run at the desktop profile
/// and at <c>iPhone 14 Pro</c>; each is saved as a screenshot.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class WorkLogOutcomeTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private const string Outcome = ".hatch-worklog-outcome";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheWorkLog_SaysHowEachSessionEnded_AtTheDesktopProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        return RunAsync("WorkLogOutcomeDesktop", profile: null);
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheWorkLog_SaysHowEachSessionEnded_AtThePhoneProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);

        return RunAsync("WorkLogOutcomePhone", DeviceProfile.IPhone14Pro);
    }

    private async Task RunAsync(string name, DeviceProfile? profile)
    {
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
            var key = await deadline.BoundAsync("logging and releasing four sessions", () => SeedAsync(baseUrl, harness.Services));

            await harness.OpenAsync(deadline, baseUrl, $"/issues/{key}");
            await harness.WaitForVisibleAsync(deadline, ".hatch-worklog-entry");

            var entries = await harness.EvaluateAsync<int>(
                deadline, "counting entries", "() => document.querySelectorAll('.hatch-worklog-entry').length");
            Assert.Equal(4, entries);

            // One line per stamped row, in no particular order; the unstamped row draws none.
            var lines = await harness.EvaluateAsync<string[]>(
                deadline, "reading the outcome lines",
                "(s) => [...document.querySelectorAll(s)].map(e => e.textContent.trim()).sort()", Outcome);
            output.WriteLine("outcome lines: " + string.Join(" | ", lines));
            Assert.Equal(["dropped - error", "dropped - quiet", "worked"], lines);

            // Full page: at the phone width the log is in the shot; at desktop the page scrolls inside its frame, so the DOM assertions above are what the desktop run proves.
            output.WriteLine("screenshot: " + await harness.FullPageScreenshotAsync(deadline, "work-log-outcome"));

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

    /// <summary>
    /// Files an issue, logs four sessions straight to the database (the
    /// work-log route takes a key and this host has none), then releases three
    /// of them over HTTP the way the CLI does.
    /// </summary>
    private static async Task<string> SeedAsync(string baseUrl, IServiceProvider services)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("WLO", "WLO"));
        var issue = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "task", "A task whose sessions ended differently", null, null, null, null));

        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
            var now = DateTimeOffset.UtcNow;

            Assert.True(IssueKey.TryParse(issue.Key, out var projectKey, out var number));
            var issueId = db.Issues.WithKey(projectKey, number).Select(i => i.Id).Single();

            for (var i = 1; i <= 4; i++)
            {
                var at = now.AddHours(-i);
                db.WorkLog.Add(new EfHatchWorkLogEntry
                {
                    IssueId = issueId,
                    SessionId = $"wlo-{i}",
                    StartedAt = at.AddMinutes(-20),
                    EndedAt = at,
                    DurationMs = 1_200_000,
                    IssueType = "task",
                    Title = $"Session {i}",
                    CostUsd = 0.5m,
                    InputTokens = 1_000,
                    OutputTokens = 1_000,
                    CacheCreationTokens = 1_000,
                    CacheReadTokens = 7_000,
                    CreatedAt = now,
                });
            }

            await db.SaveChangesAsync();
        }

        // Session four is never released: a row written before the outcome existed.
        foreach (var (session, outcome, reason) in new (string, string, string?)[]
        {
            ("wlo-1", ClaimOutcomes.Dropped, ClaimLostReasons.Quiet),
            ("wlo-2", ClaimOutcomes.Dropped, ClaimLostReasons.Error),
            ("wlo-3", ClaimOutcomes.Worked, null),
        })
        {
            var taken = await PostAsync<ClaimRequest, ClaimTakenDto>(http, $"/api/issues/{issue.Key}/claim", new("wlo-runner"));
            var path = $"/api/issues/{issue.Key}/claim?token={taken.Token}&outcome={outcome}&sessionId={session}"
                + (reason is null ? "" : $"&reason={reason}");
            using var response = await http.DeleteAsync(path);
            Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);
        }

        return issue.Key;
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

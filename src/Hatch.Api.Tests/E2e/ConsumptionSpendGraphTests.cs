using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2246's acceptance, rendered: <c>/reports/consumption</c> over a window with
/// sessions on different days draws one bar per bucket, an empty day a hoverable
/// zero, and the measure control rewrites the address (the default left out)
/// and the caption without asking the server again; a window in which nothing
/// ran draws the same graph as a row of zero bars under the page's sentence.
/// Run at the desktop profile and at <c>iPhone 14 Pro</c>; each state is saved
/// as a screenshot under the case's artifact directory.
/// </summary>
/// <remarks>
/// The sessions are written straight to the database rather than posted: the
/// work-log route takes a key and nothing else, and this host has none.
/// </remarks>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class ConsumptionSpendGraphTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private const string Bar = ".hatch-spend-bar";
    private const string Caption = ".hatch-spend-axis span:nth-child(2)";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task Consumption_DrawsTheSpendGraph_AtTheDesktopProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        return RunAsync("ConsumptionSpendGraphDesktop", profile: null);
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task Consumption_DrawsTheSpendGraph_AtThePhoneProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);

        return RunAsync("ConsumptionSpendGraphPhone", DeviceProfile.IPhone14Pro);
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
            var key = await deadline.BoundAsync("filing an epic", () => FileAsync(baseUrl));
            await deadline.BoundAsync("logging three sessions", () => LogAsync(harness.Services, key));

            await harness.OpenAsync(deadline, baseUrl, "/reports/consumption");
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForVisibleAsync(deadline, ".hatch-spend-plot");

            // Tokens, the default: nothing in the address, a bar for every day of the window.
            var bars = await CountAsync(harness, deadline, Bar);
            var zero = await CountAsync(harness, deadline, $"{Bar}[height=\"0\"]");
            output.WriteLine($"tokens: {bars} bars, {zero} of them zero");
            Assert.True(bars >= 30, $"expected a bar per day of a ninety-day window, drew {bars}");
            Assert.True(zero >= bars - 3, $"three days had sessions, but {bars - zero} bars are not zero");
            Assert.True(zero < bars, "no bar has any height");
            Assert.StartsWith("Spend by day in tokens, ", await TextAsync(harness, deadline, Caption));
            Assert.Equal("", await SearchAsync(harness, deadline));
            Assert.True(
                await harness.EvaluateAsync<bool>(
                    deadline,
                    "reading the unpriced hover",
                    "() => [...document.querySelectorAll('.hatch-spend-slot title')].every(t => !t.textContent.includes('unpriced'))"),
                "the tokens measure names an unpriced count");
            output.WriteLine("screenshot: " + await harness.ScreenshotAsync(deadline, "consumption-tokens"));

            // Notional USD: the address says so and the caption follows, and the report is not asked for again.
            var reads = 0;
            harness.Page!.Request += (_, request) =>
            {
                if (request.Url.Contains("/api/reports/consumption")) Interlocked.Increment(ref reads);
            };
            await harness.ClickAsync(deadline, ".hatch-spend-measure:text-is(\"Notional USD\")");
            await harness.WaitForVisibleAsync(deadline, $"{Caption}:text-matches(\"notional USD\")");
            await harness.WaitForVisibleAsync(deadline, ".hatch-spend-measure[aria-pressed=\"true\"]:text-is(\"Notional USD\")");
            await harness.WaitForFunctionAsync(
                deadline, "letting the pressed button settle", "() => !document.getAnimations().some(a => a.playState === 'running')");
            Assert.Equal("?measure=cost", await SearchAsync(harness, deadline));
            Assert.Equal(bars, await CountAsync(harness, deadline, Bar));
            Assert.True(
                await harness.EvaluateAsync<bool>(
                    deadline,
                    "reading the unpriced hover",
                    "() => [...document.querySelectorAll('.hatch-spend-slot title')].some(t => t.textContent.includes(', 1 unpriced'))"),
                "the cost measure does not name the unpriced session's day");
            Assert.Equal(0, Volatile.Read(ref reads));
            output.WriteLine("screenshot: " + await harness.ScreenshotAsync(deadline, "consumption-cost"));

            // And back: the default is left out of the address again.
            await harness.ClickAsync(deadline, ".hatch-spend-measure:text-is(\"Tokens\")");
            await harness.WaitForVisibleAsync(deadline, $"{Caption}:text-matches(\"in tokens\")");
            Assert.Equal("", await SearchAsync(harness, deadline));
            Assert.Equal(0, Volatile.Read(ref reads));

            // A window in which nothing ran, in both measures: the sentence, a row of zero bars, no table.
            await harness.GoToAsync(deadline, "reports/consumption?from=2020-01-01T00:00:00Z&to=2020-01-08T00:00:00Z");
            await harness.WaitForVisibleAsync(deadline, ".hatch-empty__message:text-matches(\"No issue consumed anything in this window\")");
            await harness.WaitForVisibleAsync(deadline, ".hatch-spend-plot");
            // The server snaps a week onto local midnights, so it may answer eight days for seven: the count is its own.
            var flat = await CountAsync(harness, deadline, Bar);
            output.WriteLine($"empty window: {flat} bars");
            Assert.InRange(flat, 7, 8);
            Assert.Equal(flat, await CountAsync(harness, deadline, $"{Bar}[height=\"0\"]"));
            Assert.Equal(0, await CountAsync(harness, deadline, "main table"));
            output.WriteLine("screenshot: " + await harness.ScreenshotAsync(deadline, "consumption-empty-tokens"));

            await harness.ClickAsync(deadline, ".hatch-spend-measure:text-is(\"Notional USD\")");
            await harness.WaitForVisibleAsync(deadline, $"{Caption}:text-matches(\"notional USD\")");
            await harness.WaitForVisibleAsync(deadline, ".hatch-spend-measure[aria-pressed=\"true\"]:text-is(\"Notional USD\")");
            await harness.WaitForFunctionAsync(
                deadline, "letting the pressed button settle", "() => !document.getAnimations().some(a => a.playState === 'running')");
            Assert.Equal(flat, await CountAsync(harness, deadline, $"{Bar}[height=\"0\"]"));
            output.WriteLine("screenshot: " + await harness.ScreenshotAsync(deadline, "consumption-empty-cost"));

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

    private static Task<int> CountAsync(E2eHarness harness, Deadline deadline, string selector) =>
        harness.EvaluateAsync<int>(deadline, $"counting {selector}", "(s) => document.querySelectorAll(s).length", selector);

    private static Task<string> TextAsync(E2eHarness harness, Deadline deadline, string selector) =>
        harness.EvaluateAsync<string>(deadline, $"reading {selector}", "(s) => document.querySelector(s).textContent", selector);

    private static Task<string> SearchAsync(E2eHarness harness, Deadline deadline) =>
        harness.EvaluateAsync<string>(deadline, "reading the address", "() => location.search");

    /// <summary>A project and an epic filed the way any caller files them with the wall off. Returns the epic's key.</summary>
    private static async Task<string> FileAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("CNS", "CNS"));
        var issue = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "epic", "An epic that consumed something", null, null, null, null));

        return issue.Key;
    }

    /// <summary>Sessions one, three and four days ago, none two days ago, and the last of them with no price - so the window has an empty day between two busy ones and a day with an unpriced count.</summary>
    private static async Task LogAsync(IServiceProvider services, string key)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
        var now = DateTimeOffset.UtcNow;

        Assert.True(IssueKey.TryParse(key, out var projectKey, out var number));
        var issueId = db.Issues.WithKey(projectKey, number).Select(i => i.Id).Single();

        foreach (var (daysAgo, tokens, cost) in new (int, long, decimal?)[] { (1, 900_000, 2.40m), (3, 400_000, 1.10m), (4, 150_000, null) })
        {
            var at = now.AddDays(-daysAgo);
            db.WorkLog.Add(new EfHatchWorkLogEntry
            {
                IssueId = issueId,
                SessionId = $"cns-{daysAgo}",
                StartedAt = at.AddMinutes(-30),
                EndedAt = at,
                DurationMs = 1_800_000,
                IssueType = "epic",
                CostUsd = cost,
                InputTokens = tokens / 10,
                OutputTokens = tokens / 10,
                CacheCreationTokens = tokens / 10,
                CacheReadTokens = tokens - 3 * (tokens / 10),
                CreatedAt = now,
            });
        }

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

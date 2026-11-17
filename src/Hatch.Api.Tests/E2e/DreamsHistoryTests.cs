using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2398's acceptance, rendered: a bare <c>/dreams</c> draws no dream row and
/// no search box, and its one control, <b>Past dreams</b>, says it is closed and
/// shows how many dreams there are. Pressing it opens the search row and the
/// list and writes <c>open=true</c>; pressing it again closes both and leaves the
/// address clean; Back reopens. An arrival by <c>?issue=KEY</c> is open and
/// filtered, and closing clears the filter. A post made with the list closed
/// raises the count by one and offers <b>Show it</b>, which opens the list with the
/// new dream first. Run at the desktop viewport in Chromium and at the
/// <c>iPhone 14 Pro</c> profile in WebKit.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class DreamsHistoryTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private const string Toggle = ".hatch-dream-history-toggle";

    private const string SearchBox = "input[aria-label=\"Search dreams\"]";

    private const string ShowIt = ".hatch-dream-history-offer button:text-is(\"Show it\")";

    private const string Posted = "A dream posted with the list folded";

    private static readonly string[] Filed = ["First filed dream", "Second filed dream", "Third filed dream"];

    /// <summary>The drawn rows, the search boxes, the control's state and the address, read in one script so they are one moment.</summary>
    private const string StateScript = """
        () => ({
          rows: document.querySelectorAll('.hatch-dream').length,
          search: document.querySelectorAll('input[aria-label="Search dreams"]').length,
          expanded: document.querySelector('.hatch-dream-history-toggle')?.getAttribute('aria-expanded') ?? null,
          address: location.search,
        })
        """;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task History_IsFoldedUntilAskedForAtTheDesktopProfile() => RunAsync("DreamsHistoryDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task History_IsFoldedUntilAskedForAtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("DreamsHistoryPhone", "phone", DeviceProfile.IPhone14Pro);
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
            var issueKey = await deadline.BoundAsync("filing three dreams and one issue", () => FileAsync(baseUrl));

            // Folded: no row, no search box, a control that says closed and counts the three.
            await harness.OpenAsync(deadline, baseUrl, "/dreams");
            await harness.WaitForVisibleAsync(deadline, Toggle);
            await WaitForCountAsync(harness, deadline, "3");
            var folded = await harness.EvaluateAsync<JsonElement>(deadline, "reading the folded page", StateScript);
            output.WriteLine($"{profileName} folded: {folded}");
            Assert.Equal(0, folded.GetProperty("rows").GetInt32());
            Assert.Equal(0, folded.GetProperty("search").GetInt32());
            Assert.Equal("false", folded.GetProperty("expanded").GetString());
            Assert.Equal("", folded.GetProperty("address").GetString());
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"folded-{profileName}")}");

            // Pressing it opens the search row and the list together, and says so in the address.
            await harness.ClickAsync(deadline, Toggle);
            await harness.WaitForVisibleAsync(deadline, SearchBox);
            foreach (var body in Filed) await harness.WaitForVisibleAsync(deadline, $".hatch-dream:has-text(\"{body}\")");
            var opened = await harness.EvaluateAsync<JsonElement>(deadline, "reading the open page", StateScript);
            output.WriteLine($"{profileName} open: {opened}");
            Assert.Equal(3, opened.GetProperty("rows").GetInt32());
            Assert.Equal("true", opened.GetProperty("expanded").GetString());
            Assert.Equal("?open=true", opened.GetProperty("address").GetString());
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"open-{profileName}")}");

            // Again: both close and the address is clean. Back reopens.
            await harness.ClickAsync(deadline, Toggle);
            await harness.WaitForHiddenAsync(deadline, SearchBox);
            var closed = await harness.EvaluateAsync<JsonElement>(deadline, "reading the closed page", StateScript);
            Assert.Equal(0, closed.GetProperty("rows").GetInt32());
            Assert.Equal("false", closed.GetProperty("expanded").GetString());
            Assert.Equal("", closed.GetProperty("address").GetString());

            await harness.EvaluateAsync<bool>(deadline, "going back", "() => { history.back(); return true; }");
            await harness.WaitForVisibleAsync(deadline, SearchBox);
            Assert.Equal("?open=true", await harness.EvaluateAsync<string>(deadline, "reading the address", "() => location.search"));

            // Arriving with ?issue=KEY is open and filtered; closing clears the filter.
            await harness.GoToAsync(deadline, $"dreams?issue={issueKey}");
            await harness.WaitForVisibleAsync(deadline, $"button:has-text(\"Drew on {issueKey} ×\")");
            await harness.WaitForVisibleAsync(deadline, $".hatch-dream:has-text(\"{Filed[0]}\")");
            var arrived = await harness.EvaluateAsync<JsonElement>(deadline, "reading the arrival", StateScript);
            output.WriteLine($"{profileName} arrived by issue: {arrived}");
            Assert.Equal(1, arrived.GetProperty("rows").GetInt32());
            Assert.Equal("true", arrived.GetProperty("expanded").GetString());
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"issue-{profileName}")}");

            await harness.ClickAsync(deadline, Toggle);
            await harness.WaitForHiddenAsync(deadline, $"button:has-text(\"Drew on {issueKey} ×\")");
            Assert.Equal("", await harness.EvaluateAsync<string>(deadline, "reading the address", "() => location.search"));

            // A post with the list folded: the count goes up by one and the dream is offered, first once taken.
            await harness.GoToAsync(deadline, "dreams");
            await harness.WaitForVisibleAsync(deadline, ".hatch-dream-composer");
            await WaitForCountAsync(harness, deadline, "3");
            await PostFromComposerAsync(harness, deadline, profile is not null, Posted);
            await WaitForCountAsync(harness, deadline, "4");
            await harness.WaitForVisibleAsync(deadline, ShowIt);
            var offered = await harness.EvaluateAsync<JsonElement>(deadline, "reading the offer", StateScript);
            Assert.Equal(0, offered.GetProperty("rows").GetInt32());
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"offer-{profileName}")}");

            await harness.ClickAsync(deadline, ShowIt);
            await harness.WaitForVisibleAsync(deadline, $".hatch-dream:has-text(\"{Posted}\")");
            var first = await harness.EvaluateAsync<string>(deadline, "reading the first row", "() => document.querySelector('.hatch-dream')?.textContent ?? ''");
            Assert.Contains(Posted, first);
            Assert.Equal("?open=true", await harness.EvaluateAsync<string>(deadline, "reading the address", "() => location.search"));

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

    private static Task WaitForCountAsync(E2eHarness harness, Deadline deadline, string count) =>
        harness.WaitForFunctionAsync(
            deadline,
            $"waiting for the count to read {count}",
            "(want) => document.querySelector('.hatch-dream-history-toggle__count')?.textContent === want",
            count);

    /// <summary>
    /// Types into the composer's box and presses Post. A touch profile has a coarse pointer, which keeps
    /// the box a plain textarea, so there is no Monaco to wait for; the desktop waits for Monaco to take it over.
    /// </summary>
    private static async Task PostFromComposerAsync(E2eHarness harness, Deadline deadline, bool touch, string body)
    {
        if (touch)
        {
            await harness.ClickAsync(deadline, ".hatch-dream-composer__box textarea");
        }
        else
        {
            await harness.ClickAsync(deadline, ".hatch-dream-composer__box");
            await harness.WaitForVisibleAsync(deadline, ".hatch-dream-composer__box .monaco-editor");
            await harness.ClickAsync(deadline, ".hatch-dream-composer__box .monaco-editor");
        }

        await harness.InsertTextAsync(deadline, body);
        await harness.ClickAsync(deadline, ".hatch-dream-composer__actions button:text-is(\"Post\")");
    }

    /// <summary>One project, three dreams, and an issue that drew on the first. Returns the issue's key.</summary>
    private static async Task<string> FileAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var project = await SendAsync<ProjectCreateRequest, ProjectDto>(http, HttpMethod.Post, "/api/projects", new("DRH", "DRH"));
        var dreams = new List<DreamDto>();
        foreach (var body in Filed)
            dreams.Add(await SendAsync<DreamCreateRequest, DreamDto>(http, HttpMethod.Post, "/api/dreams", new(body, null, null)));

        var issue = await SendAsync<IssueCreateRequest, IssueDto>(http, HttpMethod.Post, "/api/issues", new(project.Id, "story", "Drew on a dream", null, null, null, null));
        await SendAsync<IssueDreamsWriteRequest, DreamDto[]>(http, HttpMethod.Put, $"/api/issues/{issue.Key}/dreams", new([dreams[0].Id]));

        return issue.Key;
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

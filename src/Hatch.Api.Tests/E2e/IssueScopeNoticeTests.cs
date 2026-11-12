using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-1834's acceptance, rendered: an issue whose project is outside the tab's
/// scope says so under the subheader and offers the one press that widens the
/// scope, and an issue the scope reaches - or a tab with no scope - says
/// nothing. Run at the desktop viewport in Chromium and at the
/// <c>iPhone 14 Pro</c> profile in WebKit.
///
/// <para>Two projects with an issue each: the scope is the URL
/// (<c>?scope=KEY</c>, <c>lib/scope.ts</c>), and a scope that names every
/// project is no scope, so the press that adds the other project must leave the
/// address with no <c>scope</c> at all. Each look is saved as a PNG beside the
/// case's other artifacts, because a green assert is not a look.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class IssueScopeNoticeTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    /// <summary>The smallest target a coarse pointer should be asked to hit, in CSS pixels.</summary>
    private const double MinimumTarget = 44;

    /// <summary>Sub-pixel layout is fractional; two boxes that touch are not overlapping.</summary>
    private const double Tolerance = 0.5;

    private const string Notice = ".hatch-scope-notice";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>The notice's box, the subheader's, the viewport's and the press's, read in one script so they are one moment.</summary>
    private const string MeasureScript = """
        () => {
          const box = (el) => { const r = el.getBoundingClientRect(); return { top: r.top, bottom: r.bottom, left: r.left, right: r.right, height: r.height }; };
          const notice = document.querySelector('.hatch-scope-notice');
          const sub = document.querySelector('.hatch-issue-subheader');
          const press = notice.querySelector('button');
          return {
            text: notice.textContent,
            notice: box(notice),
            subheader: box(sub),
            press: box(press),
            pressText: press.textContent,
            innerWidth: window.innerWidth,
            scrollWidth: document.documentElement.scrollWidth,
            coarse: window.matchMedia('(pointer: coarse)').matches,
          };
        }
        """;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheNotice_RendersAtTheDesktopProfile() => RunAsync("IssueScopeNoticeDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheNotice_RendersAtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("IssueScopeNoticePhone", "phone", DeviceProfile.IPhone14Pro);
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
            var (outside, inside) = await deadline.BoundAsync("filing two issues", () => FileTwoIssuesAsync(baseUrl));
            var outsideProject = ProjectOf(outside);
            var insideProject = ProjectOf(inside);

            // Out of scope: the tab is scoped to the other project.
            var path = $"/issues/{outside}?scope={insideProject}";
            await harness.OpenAsync(deadline, baseUrl, path);
            await harness.WaitForVisibleAsync(deadline, ".hatch-issue-page");
            await harness.WaitForVisibleAsync(deadline, Notice);

            var measured = await harness.EvaluateAsync<JsonElement>(deadline, "measuring the notice", MeasureScript);
            output.WriteLine($"{profileName} {path}: {measured}");
            var shot = await harness.ScreenshotAsync(deadline, $"out-of-scope-{profileName}");
            output.WriteLine($"saved {shot}");

            Assert.Contains($"{outsideProject} is outside this tab", measured.GetProperty("text").GetString());
            Assert.Contains($"Add {outsideProject} to this tab", measured.GetProperty("pressText").GetString());

            var notice = measured.GetProperty("notice");
            var press = measured.GetProperty("press");
            var innerWidth = measured.GetProperty("innerWidth").GetDouble();
            Assert.True(
                notice.GetProperty("top").GetDouble() >= measured.GetProperty("subheader").GetProperty("bottom").GetDouble() - Tolerance,
                $"the notice does not sit under the subheader: {measured}");
            Assert.True(
                notice.GetProperty("left").GetDouble() >= -Tolerance && notice.GetProperty("right").GetDouble() <= innerWidth + Tolerance,
                $"the notice lies outside [0, {innerWidth}]: {measured}");
            Assert.True(
                press.GetProperty("left").GetDouble() >= -Tolerance && press.GetProperty("right").GetDouble() <= innerWidth + Tolerance,
                $"the press lies outside [0, {innerWidth}]: {measured}");
            Assert.True(measured.GetProperty("scrollWidth").GetDouble() <= innerWidth + Tolerance, $"the page scrolls sideways: {measured}");

            if (profile is not null)
            {
                Assert.True(measured.GetProperty("coarse").GetBoolean(), $"(pointer: coarse) does not match under the device descriptor: {measured}");
                Assert.True(
                    press.GetProperty("height").GetDouble() >= MinimumTarget - Tolerance,
                    $"the press is under {MinimumTarget}px high: {measured}");
            }

            // The press: the notice goes, and the address names every project, so it names none.
            await harness.ClickAsync(deadline, $"{Notice} button");
            await harness.WaitForHiddenAsync(deadline, Notice);
            await harness.WaitForVisibleAsync(deadline, ".hatch-issue-page");
            var search = await harness.EvaluateAsync<string>(deadline, "reading the address", "() => location.search");
            output.WriteLine($"{profileName} after the press: search is \"{search}\"");
            Assert.DoesNotContain("scope", search);
            Assert.False(await harness.IsVisibleAsync(deadline, Notice), "the notice is still showing after the press");
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"widened-{profileName}")}");

            // In scope: the tab is scoped to this issue's own project.
            await harness.GoToAsync(deadline, $"issues/{outside}?scope={outsideProject}");
            await harness.WaitForVisibleAsync(deadline, ".hatch-issue-page");
            await harness.WaitForVisibleAsync(deadline, ".hatch-issue-subheader");
            Assert.Equal(0, await CountAsync(harness, deadline));
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"in-scope-{profileName}")}");

            // Unscoped: no scope param, and a fresh load carries none.
            await harness.GoToAsync(deadline, $"issues/{outside}");
            await harness.WaitForVisibleAsync(deadline, ".hatch-issue-page");
            await harness.WaitForVisibleAsync(deadline, ".hatch-issue-subheader");
            Assert.Equal(0, await CountAsync(harness, deadline));
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"unscoped-{profileName}")}");

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

    /// <summary>The notices on the page right now: a count rather than a wait, so absence cannot pass by timing out.</summary>
    private static Task<int> CountAsync(E2eHarness harness, Deadline deadline) =>
        harness.EvaluateAsync<int>(deadline, "counting the notices", $"() => document.querySelectorAll('{Notice}').length");

    /// <summary>The project part of an issue key, <c>KEY-12</c> being <c>KEY</c>.</summary>
    private static string ProjectOf(string issueKey) => issueKey[..issueKey.LastIndexOf('-')];

    /// <summary>Two projects, filed the way any caller files them with the wall off - the local person may file everything this test files - and an issue under each.</summary>
    private static async Task<(string First, string Second)> FileTwoIssuesAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var keys = new List<string>();
        foreach (var key in new[] { "SCA", "SCB" })
        {
            var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new(key, key));
            var issue = await PostAsync<IssueCreateRequest, IssueDto>(
                http, "/api/issues", new(project.Id, "task", $"A task in {key}", null, null, null, null));
            keys.Add(issue.Key);
        }

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

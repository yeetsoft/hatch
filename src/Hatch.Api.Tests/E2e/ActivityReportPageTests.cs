using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2237's acceptance, rendered: <c>/reports/activity</c> with an actor chosen
/// and a range with activity in it draws the headline, a rundown row linked to
/// the issue and the trail beneath, and an actor nobody is draws the empty
/// sentence while the address still says who was asked for. Run at the desktop
/// profile and at <c>iPhone 14 Pro</c>; the page is also saved as a screenshot
/// under the case's artifact directory, which is how a session reads it.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class ActivityReportPageTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private const string IssueTitle = "Something the actor touched";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task Activity_DrawsTheRundownAndTheTrail_AtTheDesktopProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        return RunAsync("ActivityReportDesktop", profile: null);
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task Activity_DrawsTheRundownAndTheTrail_AtThePhoneProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);

        return RunAsync("ActivityReportPhone", DeviceProfile.IPhone14Pro);
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
            var (actor, key) = await deadline.BoundAsync("filing an issue and a comment", () => FileAsync(baseUrl));

            // An actor chosen, over the default fourteen days the filing above is in.
            await harness.OpenAsync(deadline, baseUrl, "/apps/hatch/reports/activity?actor=" + Uri.EscapeDataString(actor));
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForVisibleAsync(deadline, "main strong");

            var headline = await harness.EvaluateAsync<string>(
                deadline, "reading the headline", "() => document.querySelector('main strong').textContent");
            output.WriteLine("headline: " + headline);
            Assert.Matches(@"^\d+ events?: ", headline);

            Assert.True(
                await harness.IsVisibleAsync(deadline, $"a[href$=\"/issues/{key}\"]"),
                $"no link to {key} was drawn");
            Assert.Equal(
                3,
                await harness.EvaluateAsync<int>(deadline, "counting the tables", "() => document.querySelectorAll('main table').length"));
            Assert.True(
                await harness.EvaluateAsync<bool>(
                    deadline,
                    "reading the rundown row",
                    $"() => [...document.querySelectorAll('main table:first-of-type tbody tr')].some(r => r.textContent.includes('{IssueTitle}'))"),
                "the rundown has no row carrying the issue's title");
            Assert.Equal(
                actor,
                await harness.EvaluateAsync<string>(
                    deadline, "reading the address", "() => new URLSearchParams(location.search).get('actor')"));
            Assert.True(
                await harness.EvaluateAsync<bool>(
                    deadline,
                    "checking nothing on the page writes",
                    "() => document.querySelectorAll('main button[type=submit], main form, main textarea').length === 0"),
                "the page draws a control that could write");

            output.WriteLine("screenshot: " + await harness.ScreenshotAsync(deadline, "activity-actor"));

            // HA-2241: a person is chosen, so the control that counts their own keys is offered, and
            // pressing it writes includeKeys to the address; the page still draws once it has read again.
            const string keysSelect = "label.hatch-facet:has-text(\"Include my keys\") select";
            Assert.True(await harness.IsVisibleAsync(deadline, keysSelect), "no Include my keys control was drawn for a person");
            await harness.SelectOptionAsync(deadline, keysSelect, "on");
            await harness.WaitForFunctionAsync(
                deadline, "waiting for includeKeys in the address",
                "() => new URLSearchParams(location.search).get('includeKeys') === 'true'");
            await harness.WaitForVisibleAsync(deadline, "main strong");
            output.WriteLine("screenshot: " + await harness.ScreenshotAsync(deadline, "activity-include-keys"));

            // An actor nobody is: still sent as given, the empty range a sentence, and the address unchanged.
            await harness.GoToAsync(deadline, "reports/activity?actor=nobody");
            await harness.WaitForVisibleAsync(deadline, ".hatch-empty__message:text-is(\"Nothing recorded for nobody in this window.\")");
            Assert.Equal(
                0,
                await harness.EvaluateAsync<int>(deadline, "counting the tables", "() => document.querySelectorAll('main table').length"));
            Assert.Equal(
                "nobody",
                await harness.EvaluateAsync<string>(
                    deadline, "reading the address", "() => new URLSearchParams(location.search).get('actor')"));

            Assert.False(
                await harness.IsVisibleAsync(deadline, keysSelect),
                "Include my keys is offered for an actor who is no person");

            output.WriteLine("screenshot: " + await harness.ScreenshotAsync(deadline, "activity-nobody"));

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

    /// <summary>A project and an issue filed, and a comment on it, the way any caller files them with the wall off. Returns the name the events carry and the issue's key.</summary>
    private static async Task<(string Actor, string Key)> FileAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/hatch/projects", new("ACT", "ACT"));
        var issue = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/hatch/issues", new(project.Id, "task", IssueTitle, null, null, null, null));
        await PostAsync<CommentCreateRequest, object>(http, $"/api/hatch/issues/{issue.Key}/comments", new("A comment"));

        using var response = await http.GetAsync("/api/hatch/me");
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"GET /api/hatch/me answered {(int)response.StatusCode}: {text}");
        var me = JsonSerializer.Deserialize<MeDto>(text, Web)!;

        return (me.Name, issue.Key);
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

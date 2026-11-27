using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2845's acceptance, rendered: a draft ambition's row on the Ambitions
/// page, and the header of its own page, each offer <b>Shape it</b>. Pressing
/// it moves the ambition into the column a session shapes it in, and the
/// stage reads <c>Shaping</c>. Standing in for the session - no runner is here
/// to be one - moves the ambition on into the shaping row's to-column and
/// gives it a capability, which reads <c>Ready</c> and offers <b>Start it</b>.
/// The confirmation names the capability count, how many epics run at once,
/// the budget (or that none is set) and the pre-approval level; confirming
/// moves the ambition into the implementation column, which reads
/// <c>Running</c>. Run at the desktop viewport in Chromium and at the
/// <c>iPhone 14 Pro</c> profile in WebKit, where nothing may scroll sideways.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class AmbitionPressesPageTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 120;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    /// <summary>Sub-pixel layout is fractional; two boxes that touch are not overlapping.</summary>
    private const double Tolerance = 0.5;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private const string FitScript = """
        () => ({ innerWidth: window.innerWidth, scrollWidth: document.documentElement.scrollWidth })
        """;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task ThePresses_ShapeAndStartAnAmbition_AtTheDesktopProfile() => RunAsync("AmbitionPressesDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task ThePresses_ShapeAndStartAnAmbition_AtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("AmbitionPressesPhone", "phone", DeviceProfile.IPhone14Pro);
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
            using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

            var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("APR", "APR"));
            var onList = await PostAsync<IssueCreateRequest, IssueDto>(
                http, "/api/issues", new(project.Id, "ambition", "Partners order without a phone call", null, null, null, null));
            var onItsOwnPage = await PostAsync<IssueCreateRequest, IssueDto>(
                http, "/api/issues", new(project.Id, "ambition", "Staff see every order at a glance", null, null, null, null));

            // The Ambitions page: the row shapes, then starts.
            await harness.OpenAsync(deadline, baseUrl, "/ambitions");
            if (profile is not null) await harness.SetViewportAsync(deadline, 390, 844);
            var listRow = RowScope(onList.Key);
            await harness.WaitForVisibleAsync(deadline, listRow);
            await harness.WaitForVisibleAsync(deadline, $"{listRow} button:has-text(\"Shape it\")");
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"ambition-presses-list-draft-{profileName}")}");
            AssertFits(await harness.EvaluateAsync<JsonElement>(deadline, "measuring the draft row", FitScript), profile is not null);

            await ShapeAsync(harness, deadline, http, onList.Key, listRow);
            await StandInForTheSessionAsync(http, onList.Key);

            await harness.GoToAsync(deadline, "/ambitions");
            await harness.WaitForVisibleAsync(deadline, $"{listRow} button:has-text(\"Start it\")");
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"ambition-presses-list-ready-{profileName}")}");

            await StartAsync(harness, deadline, listRow, profileName, "list");
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"ambition-presses-list-running-{profileName}")}");
            AssertFits(await harness.EvaluateAsync<JsonElement>(deadline, "measuring the running row", FitScript), profile is not null);

            // The ambition's own page: the header shapes, then starts.
            const string header = ".hatch-ambition-header";
            await harness.OpenAsync(deadline, baseUrl, $"/issues/{onItsOwnPage.Key}");
            if (profile is not null) await harness.SetViewportAsync(deadline, 390, 844);
            await harness.WaitForVisibleAsync(deadline, header);
            await harness.WaitForVisibleAsync(deadline, $"{header} button:has-text(\"Shape it\")");
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"ambition-presses-header-draft-{profileName}")}");

            await ShapeAsync(harness, deadline, http, onItsOwnPage.Key, header);
            await StandInForTheSessionAsync(http, onItsOwnPage.Key);

            await harness.GoToAsync(deadline, $"issues/{onItsOwnPage.Key}");
            await harness.WaitForVisibleAsync(deadline, $"{header} button:has-text(\"Start it\")");
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"ambition-presses-header-ready-{profileName}")}");

            await StartAsync(harness, deadline, header, profileName, "header");
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"ambition-presses-header-running-{profileName}")}");
            AssertFits(await harness.EvaluateAsync<JsonElement>(deadline, "measuring the running header", FitScript), profile is not null);

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

    /// <summary>Presses Shape it within <paramref name="scope"/> and waits for the stage to read Shaping.</summary>
    private static async Task ShapeAsync(E2eHarness harness, Deadline deadline, HttpClient http, string key, string scope)
    {
        await harness.ClickAsync(deadline, $"{scope} button:has-text(\"Shape it\")");
        await harness.WaitForFunctionAsync(
            deadline, "waiting for the stage to read Shaping",
            "(sel) => document.querySelector(sel + ' .hatch-stage-pill')?.textContent === 'Shaping'", scope);

        var row = Assert.Single((await GetAsync<JsonElement>(http, $"/api/ambitions?key={key}")).EnumerateArray());
        Assert.Equal("shaping", row.GetProperty("stage").GetString());
    }

    /// <summary>
    /// What a session would do, done by hand since no runner is here to be one:
    /// move the ambition on into the shaping row's to-column, and give it one
    /// capability, which reads the ambition Ready.
    /// </summary>
    private static async Task StandInForTheSessionAsync(HttpClient http, string key)
    {
        var row = Assert.Single((await GetAsync<JsonElement>(http, $"/api/ambitions?key={key}")).EnumerateArray());
        var shapingStatusId = row.GetProperty("shapingStatusId").GetInt32();

        var playbooks = await GetAsync<JsonElement>(http, "/api/playbooks");
        var shaping = playbooks.EnumerateArray().Single(p => p.GetProperty("fromStatusId").GetInt32() == shapingStatusId && p.GetProperty("shapesAmbition").GetBoolean());
        var toStatusId = shaping.GetProperty("toStatusId").GetInt32();

        await PostAsync<IssueMoveRequest, IssueDto>(http, $"/api/issues/{key}/move", new(toStatusId, null, null, shapingStatusId));
        await PostAsync<CapabilityCreateRequest, CapabilityDto>(
            http, $"/api/issues/{key}/capabilities", new("Partners can place an order without a phone call", "Place one as a partner and count the calls"));
    }

    /// <summary>
    /// Presses Start it within <paramref name="scope"/>, reads the dialog's four
    /// lines - the capability count, the epics at once, the budget and the
    /// pre-approval level - against what the row itself says, screenshots it,
    /// then confirms.
    /// </summary>
    private async Task StartAsync(E2eHarness harness, Deadline deadline, string scope, string profileName, string where)
    {
        await harness.ClickAsync(deadline, $"{scope} button:has-text(\"Start it\")");
        await harness.WaitForVisibleAsync(deadline, $"{scope} [role=dialog]");

        var dialogText = await harness.EvaluateOnAsync<string>(
            deadline, "reading the Start it dialog", $"{scope} [role=dialog]", "(el) => el.textContent");
        output.WriteLine($"{profileName} {where} start dialog: {dialogText}");
        Assert.Contains("A pass will check 1 capability.", dialogText);
        Assert.Contains("1 epic runs at once.", dialogText);
        Assert.Contains("No budget is set.", dialogText);
        Assert.Contains("Pre-approval: None.", dialogText);
        output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"ambition-presses-{where}-dialog-{profileName}")}");

        await harness.ClickAsync(deadline, $"{scope} [role=dialog] button:has-text(\"Start it\")");
        await harness.WaitForVisibleAsync(deadline, $"{scope} .hatch-stage-pill:has-text(\"Running\")");
    }

    /// <summary>A row is found by the ambition's own key, since the list holds more than one.</summary>
    private static string RowScope(string key) => $".hatch-ambition-row:has(a[href=\"/issues/{key}\"])";

    private static void AssertFits(JsonElement measured, bool phone)
    {
        var innerWidth = measured.GetProperty("innerWidth").GetDouble();
        Assert.True(
            measured.GetProperty("scrollWidth").GetDouble() <= innerWidth + Tolerance,
            $"the page scrolls sideways{(phone ? " on the phone" : "")}: {measured}");
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }

    private static async Task<TResponse> GetAsync<TResponse>(HttpClient http, string path)
    {
        using var response = await http.GetAsync(path);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"GET {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

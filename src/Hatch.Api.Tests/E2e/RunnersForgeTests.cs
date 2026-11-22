using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2714: the Runners page says whether each runner is signed in to its
/// forge. Three runners beat the real host's session route under their own
/// keys - one that is signed in, one whose sign-in fails with a reason holding
/// a long unbroken line, and one that never said - and the page is read on the
/// list and on each runner's own page. Nothing is stubbed: the board stores the
/// answer on the session and folds it up to the runner. A screenshot of the
/// list and of each runner's page is saved beside the case's other artifacts,
/// at the desktop viewport in Chromium and at the <c>iPhone 14 Pro</c> profile
/// in WebKit, and the document must not scroll sideways on either.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class RunnersForgeTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 120;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string SignedIn = "Works Runner";
    private const string Failing = "Failing Runner";
    private const string Silent = "Silent Runner";

    private const string SignedInWords = "Signed in to its forge";
    private const string FailsWords = "Forge sign-in fails";

    /// <summary>One line at the board's own limit, with a stretch no space breaks.</summary>
    private static readonly string LongWhy =
        "error: gh auth status: " + new string('k', 90) + " token rejected for this checkout " + new string('x', 200);

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task EveryReading_IsSaid_AtDesktop()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        await RunAsync(null, "RunnersForge", "runners-forge");
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task EveryReading_IsSaid_AtThePhoneProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);

        await RunAsync(DeviceProfile.IPhone14Pro, "RunnersForgePhone", "runners-forge-phone");
    }

    /// <summary>The phone's viewport, touch and mobile emulation in Chromium, for a machine where WebKit does not open a page.</summary>
    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task EveryReading_IsSaid_AtThePhoneProfileInChromium()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        await RunAsync(DeviceProfile.IPhone14ProChromium, "RunnersForgePhoneChromium", "runners-forge-phone-chromium");
    }

    private async Task RunAsync(DeviceProfile? profile, string name, string screenshot)
    {
        var harness = new E2eHarness(name);
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        var where = profile?.Device ?? "desktop";
        try
        {
            await harness.LaunchBrowserAsync(deadline, profile);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);

            await deadline.BoundAsync("seeding the runners", async () =>
            {
                await BeatAsync(baseUrl, SignedIn, "works", null);
                await BeatAsync(baseUrl, Failing, "fails", LongWhy);
                await BeatAsync(baseUrl, Silent, null, null);
            });

            // The list: one badge, on the runner whose forge sign-in fails.
            await harness.OpenAsync(deadline, baseUrl, "/runners");
            await harness.WaitForVisibleAsync(deadline, $"text={Failing}");
            await harness.WaitForVisibleAsync(deadline, "text=forge sign-in fails");

            var list = await harness.EvaluateAsync<string>(deadline, "reading the list", "() => document.body.innerText");
            output.WriteLine($"--- {where} /runners{Environment.NewLine}screenshot: {await harness.ScreenshotAsync(deadline, screenshot + "-list")}{Environment.NewLine}{list}");
            Assert.Equal(1, await harness.EvaluateAsync<int>(
                deadline, "counting the badges",
                "() => [...document.querySelectorAll('.hatch-badge')].filter(b => b.textContent.trim() === 'forge sign-in fails').length"));
            AssertNoOverflow(await Overflows(harness, deadline), "the list");

            // Each runner's own page.
            await AssertPageAsync(harness, deadline, baseUrl, SignedIn, where, screenshot, expects: SignedInWords, forbids: [FailsWords]);
            await AssertPageAsync(harness, deadline, baseUrl, Silent, where, screenshot, expects: null, forbids: [SignedInWords, FailsWords]);
            var failing = await AssertPageAsync(harness, deadline, baseUrl, Failing, where, screenshot, expects: $"{FailsWords}: error: gh auth status", forbids: [SignedInWords]);
            Assert.Contains("token rejected for this checkout", failing);

            // The board's answer, read straight off the wire: one line, at most 200 characters.
            using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
            var read = await http.GetFromJsonAsync<JsonElement>($"/api/runners/{Uri.EscapeDataString(Failing)}", Web);
            Assert.Equal("fails", read.GetProperty("forge").GetString());
            Assert.Equal(200, read.GetProperty("forgeWhy").GetString()!.Length);

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

    private async Task<string> AssertPageAsync(
        E2eHarness harness, Deadline deadline, string baseUrl, string runner, string where, string screenshot,
        string? expects, string[] forbids)
    {
        await harness.OpenAsync(deadline, baseUrl, $"/runners/{Uri.EscapeDataString(runner)}");
        await harness.WaitForVisibleAsync(deadline, "text=Identity and key");

        var page = await harness.EvaluateAsync<string>(deadline, "reading the page", "() => document.body.innerText");
        var slug = runner.Split(' ')[0].ToLowerInvariant();
        output.WriteLine($"--- {where} /runners/{runner}{Environment.NewLine}screenshot: {await harness.ScreenshotAsync(deadline, $"{screenshot}-{slug}")}{Environment.NewLine}{page}");

        if (expects is not null) Assert.Contains(expects, page);
        foreach (var word in forbids) Assert.DoesNotContain(word, page);
        AssertNoOverflow(await Overflows(harness, deadline), $"{runner}'s page");
        return page;
    }

    private static Task<bool> Overflows(E2eHarness harness, Deadline deadline) =>
        harness.EvaluateAsync<bool>(deadline, "measuring overflow", "() => document.documentElement.scrollWidth !== document.documentElement.clientWidth");

    private static void AssertNoOverflow(bool overflows, string what) =>
        Assert.False(overflows, $"the document scrolls sideways on {what}");

    /// <summary>Mints a runner and has its loop beat the session route under its own key.</summary>
    private static async Task BeatAsync(string baseUrl, string runner, string? forge, string? why)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var minted = await PostAsync<CreateRunnerRequest, RunnerMintedDto>(http, "/api/runners", new(runner));

        using var asRunner = new HttpClient { BaseAddress = new Uri(baseUrl) };
        asRunner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", minted.Secret);
        await PostAsync<RunnerHeartbeatRequest, RunnerInstructionDto>(
            asRunner, "/api/runners/sessions",
            new(Kind: "loop", Fresh: true, Where: $"host:/checkouts/{runner.Split(' ')[0].ToLowerInvariant()}", Forge: forge, ForgeWhy: why));
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<TResponse>(Web))!;
    }
}

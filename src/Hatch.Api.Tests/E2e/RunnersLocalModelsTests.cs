using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.Playwright;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2425: the Runners page lists the local-models table and adds and removes
/// its rows, and a runner's detail says which of those it is asked to hold.
///
/// <para>The table is the real thing - the host's own API and database - so the
/// server's refusals are the server's own sentences. Only the runners read is
/// stubbed, to give one Ollama runner a <c>hold</c> and a heartbeat that already
/// lists one of its ids.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class RunnersLocalModelsTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Holder = "e2e-local-models";

    private const string Card = ".hatch-local-models";
    private const string Rows = Card + " tbody tr";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheCard_AddsAndRemovesRows_AndTheDetailSaysWhatIsHeld()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        await RunAsync(null, "RunnersLocalModels");
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheCard_OnAPhone_AddsAndRemovesRows_AndTheDetailSaysWhatIsHeld()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);

        await RunAsync(DeviceProfile.IPhone14Pro, "RunnersLocalModelsPhone");
    }

    private async Task RunAsync(DeviceProfile? profile, string name)
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

            await harness.OpenAsync(deadline, baseUrl, "/runners", Stub);
            await harness.WaitForVisibleAsync(deadline, Card);

            var page = harness.Page!;
            Assert.Equal(0, await page.Locator(Rows).CountAsync());
            Assert.True(await page.GetByRole(AriaRole.Button, new() { Name = "Add model" }).IsDisabledAsync());

            // Add a row: three fields, and it is listed.
            await Add(harness, deadline, "  fake:1b ", "8192", "2");
            await WaitForRows(harness, deadline, 1);
            var first = await RowText(harness, deadline, 0);
            Assert.Contains("fake:1b", first);
            Assert.Contains("8,192 tokens", first);
            Assert.Contains("2GB", first);

            await Add(harness, deadline, "fake:7b", "32768", "4.5");
            await WaitForRows(harness, deadline, 2);
            var second = await RowText(harness, deadline, 1);
            Assert.Contains("fake:7b", second);
            Assert.Contains("4.5GB", second);
            output.WriteLine($"--- two rows, {where}: {await harness.ScreenshotAsync(deadline, name + "-rows")}");

            // A duplicate is the server's 409, in its own words, and nothing is added.
            await Add(harness, deadline, "fake:1b", "8192", "2");
            await harness.WaitForVisibleAsync(deadline, Card + " .text-danger");
            Assert.Contains("there is already a local model \"fake:1b\"", await Refusal(harness, deadline));
            Assert.Equal(2, await page.Locator(Rows).CountAsync());
            // The form keeps what was typed, so the id can be mended.
            Assert.Equal("fake:1b", await page.Locator(Card + " input >> nth=0").InputValueAsync());

            // An id that is not an Ollama id is the server's 400.
            await Add(harness, deadline, "gizmo", "8192", "2");
            await deadline.BoundAsync("waiting for the new refusal", () => page.WaitForFunctionAsync(
                "() => (document.querySelector('.hatch-local-models .text-danger')?.textContent ?? '').length > 0 && !document.querySelector('.hatch-local-models .text-danger').textContent.includes('already')"));
            Assert.Equal(2, await page.Locator(Rows).CountAsync());
            output.WriteLine($"--- refusal, {where}: {await harness.ScreenshotAsync(deadline, name + "-refused")}");

            // Delete the first row, and it goes.
            await harness.ClickAsync(deadline, Rows + ":nth-child(1) button:text-is(\"Delete\")");
            await WaitForRows(harness, deadline, 1);
            Assert.Contains("fake:7b", await RowText(harness, deadline, 0));

            // The runner's detail says what it holds, and which of those it already lists.
            await harness.GoToAsync(deadline, $"runners/{Holder}");
            await harness.WaitForVisibleAsync(deadline, "summary:text-is(\"Machine\")");
            await harness.ClickAsync(deadline, "summary:text-is(\"Machine\")");
            await harness.WaitForVisibleAsync(deadline, "text=Holds fake:1b, fake:7b");
            var held = await harness.EvaluateAsync<string>(deadline, "reading the held line", "() => [...document.querySelectorAll('details p, details div, details span')].map(e => e.textContent ?? '').find(t => t.startsWith('Holds')) ?? ''");
            Assert.Equal("Holds fake:1b, fake:7b (fake:1b pulled)", held.Trim());
            await harness.EvaluateAsync<bool>(deadline, "scrolling to the held line", "() => { [...document.querySelectorAll('details p, details div, details span')].find(e => (e.textContent ?? '').startsWith('Holds'))?.scrollIntoView({ block: 'center' }); return true; }");
            output.WriteLine($"--- the held line, {where}: {await harness.ScreenshotAsync(deadline, name + "-held")}");

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

    private static async Task Add(E2eHarness harness, Deadline deadline, string id, string context, string gigabytes)
    {
        var page = harness.Page!;
        await deadline.BoundAsync("typing the id", () => page.Locator(Card + " input >> nth=0").FillAsync(id));
        await deadline.BoundAsync("typing the context", () => page.Locator(Card + " input >> nth=1").FillAsync(context));
        await deadline.BoundAsync("typing the footprint", () => page.Locator(Card + " input >> nth=2").FillAsync(gigabytes));
        await harness.ClickAsync(deadline, Card + " button:text-is(\"Add model\")");
    }

    private static Task WaitForRows(E2eHarness harness, Deadline deadline, int count) =>
        deadline.BoundAsync($"waiting for {count} row(s)", () => harness.Page!.WaitForFunctionAsync(
            "n => document.querySelectorAll('.hatch-local-models tbody tr').length === n", count));

    private static Task<string> RowText(E2eHarness harness, Deadline deadline, int index) =>
        harness.EvaluateAsync<string>(deadline, "reading a row", $"() => document.querySelectorAll('.hatch-local-models tbody tr')[{index}]?.textContent ?? ''");

    private static Task<string> Refusal(E2eHarness harness, Deadline deadline) =>
        harness.EvaluateOnAsync<string>(deadline, "reading the refusal", Card + " .text-danger", "e => e.textContent ?? ''");

    /// <summary>
    /// One Ollama runner with an invented name and no real host, a key since the
    /// list draws no row without one, holding two ids of which its own heartbeat
    /// already lists one. Only the runners read is answered here.
    /// </summary>
    private static async Task Stub(IBrowserContext context)
    {
        var now = DateTimeOffset.UtcNow;

        var runner = new RunnerDto(
            Name: Holder,
            Kind: "loop",
            FirstSeenAt: now.AddHours(-2),
            LastSeenAt: now,
            ClaimKey: null,
            Line: null,
            LineAt: null,
            State: RunnerStates.Running,
            Under: null,
            MaxRuns: null,
            MaxSpend: null,
            UntilAt: null,
            GoneAfterSeconds: 300,
            Repositories: [],
            Clones: null,
            Mine: null,
            Standing: null,
            Where: null,
            Agent: "ollama",
            Drives: [],
            Tags: [],
            Projects: [],
            For: null,
            CanControl: false,
            Models: ["fake:1b"],
            RefusedModels: [],
            ApiKeyId: Guid.NewGuid(),
            Sessions:
            [
                new RunnerSessionDto(
                    Id: 1,
                    Where: "example-host.invalid:/e2e/1",
                    Kind: "loop",
                    Host: null,
                    State: RunnerStates.Running,
                    StartedAt: now.AddMinutes(-30),
                    LastSeenAt: now,
                    EndedAt: null,
                    AcceleratorBackend: "cuda",
                    AcceleratorMemoryBytes: 24L * 1024 * 1024 * 1024,
                    AcceleratorDeviceCount: 1),
            ],
            AcceleratorMemoryBytes: 24L * 1024 * 1024 * 1024,
            AcceleratorBackend: "cuda",
            Hold: ["fake:1b", "fake:7b"]);

        await context.RouteAsync(new System.Text.RegularExpressions.Regex(@"/api/runners(\?.*)?$"), route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new[] { runner }, Web),
        }));
    }
}

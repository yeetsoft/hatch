using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2616: a runner started by a launcher older than the board's build draws
/// a <b>Launcher behind</b> badge beside <b>Behind</b> and <b>Rebuild</b>.
/// The runner beats the real host with <c>launcherVersion: 0</c> and a build
/// that trails its own checkout's source, so the board stores the version and
/// makes the comparison itself, and <b>Launcher behind</b> and <b>Rebuild</b>
/// are the board's own answers. The host here is an unstamped build, which
/// cannot say <b>Behind</b>, so the browser's read of the runners list has that
/// one answer set on the way in. Each look is saved as a PNG beside the case's
/// other artifacts, at the desktop viewport in Chromium and at the
/// <c>iPhone 14 Pro</c> profile in WebKit.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class RunnersLauncherBehindTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Name = "e2e-launcher-behind";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task AllThreeBadges_DrawOnOneRunner_AtDesktop()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        await RunAsync(null, "RunnersLauncherBehind", "runners-launcher-behind");
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task AllThreeBadges_DrawOnOneRunner_AtThePhoneProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);

        await RunAsync(DeviceProfile.IPhone14Pro, "RunnersLauncherBehindPhone", "runners-launcher-behind-phone");
    }

    private async Task RunAsync(DeviceProfile? profile, string name, string screenshot)
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

            // The runner's own beat, to the real board: a launcher at version 0,
            // and a build one commit behind the source it was started from.
            using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
            var beat = await http.PostAsJsonAsync($"/api/runners/{Name}", new RunnerHeartbeatRequest(
                Kind: "loop",
                Fresh: true,
                Revision: "1111111",
                Sequence: 10,
                SourceRevision: "2222222",
                SourceSequence: 11,
                LauncherVersion: 0));
            beat.EnsureSuccessStatusCode();

            await harness.OpenAsync(deadline, baseUrl, $"/runners/{Name}", SayBehind);
            await harness.WaitForVisibleAsync(deadline, "text=Launcher behind");

            var page = await harness.EvaluateAsync<string>(deadline, "reading the page", "() => document.body.innerText");
            output.WriteLine($"--- {profile?.Device ?? "desktop"}{Environment.NewLine}screenshot: {await harness.ScreenshotAsync(deadline, screenshot)}{Environment.NewLine}{page}");

            Assert.Contains("Behind", page);
            Assert.Contains("Rebuild", page);
            Assert.Contains("Launcher behind", page);

            Assert.False(
                await harness.EvaluateAsync<bool>(deadline, "measuring overflow", "() => document.documentElement.scrollWidth !== document.documentElement.clientWidth"),
                "the document scrolls sideways");

            // The board's answer, read straight off the wire: the page only reads it.
            var read = await http.GetFromJsonAsync<JsonNode>($"/api/runners/{Name}");
            Assert.Equal(0, (int?)read!["launcherVersion"]);
            Assert.True((bool?)read["launcherBehind"]);

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

    /// <summary>Sets <c>boardDrift</c> to <c>Behind</c> on the browser's read of the runners, the one answer an unstamped host cannot give.</summary>
    private static Task SayBehind(IBrowserContext context) =>
        context.RouteAsync(new System.Text.RegularExpressions.Regex(@"/api/runners(/[^/?]+)?(\?.*)?$"), async route =>
        {
            var response = await route.FetchAsync();
            var body = JsonNode.Parse(await response.TextAsync())!;

            foreach (var runner in body is JsonArray list ? list.Select(n => n!) : [body])
                runner["boardDrift"] = "Behind";

            await route.FulfillAsync(new() { Response = response, Body = body.ToJsonString(), ContentType = "application/json" });
        });
}

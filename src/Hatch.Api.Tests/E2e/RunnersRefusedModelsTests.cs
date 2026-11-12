using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.Playwright;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2306: the runner detail's What it may work card edits the models a runner
/// refuses, rendered by a real browser from stubbed runners - an owner's holding
/// one refused model and a viewer's holding another.
///
/// <para>A bad entry must be refused in the card with nothing sent; a changed
/// list sends the PATCH and then the PUT, and a save of tags alone sends no PUT.
/// The component tests prove what is drawn; a click is only provable here, since
/// the web app has no DOM test environment.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class RunnersRefusedModelsTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Owned = "e2e-refused-owned";
    private const string Viewed = "e2e-refused-viewed";

    private const string ModelsField = ".hatch-field-grid textarea >> nth=1";
    private const string TagsField = ".hatch-field-grid textarea >> nth=0";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheSettingsCard_EditsRefusedModels()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        await RunAsync("RunnersRefusedModels", "runners-refused-models");
    }

    private async Task RunAsync(string name, string screenshot)
    {
        var harness = new E2eHarness(name);
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        var sent = new List<string>();
        try
        {
            await harness.LaunchBrowserAsync(deadline);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            // The runners read and every press are stubbed, but the host still serves the bundle.
            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);

            await harness.OpenAsync(deadline, baseUrl, $"/runners/{Owned}", context => Stub(context, sent));
            await harness.WaitForVisibleAsync(deadline, ModelsField);

            var page = harness.Page!;
            var held = await harness.EvaluateOnAsync<string>(deadline, "reading the held list", ModelsField, "e => e.value");
            Assert.Equal("fable", held);
            var screenshotBefore = await harness.ScreenshotAsync(deadline, screenshot + "-before");
            output.WriteLine($"--- owner before, desktop: {screenshotBefore}");

            // Nothing has changed, so there is nothing to save.
            Assert.True(await page.GetByRole(AriaRole.Button, new() { Name = "Save settings" }).IsDisabledAsync());

            // A bad entry is refused in the card, in the same place a server refusal draws, and nothing is sent.
            await deadline.BoundAsync("typing a bad entry", () => page.Locator(ModelsField).FillAsync("fable\ngizmo"));
            await harness.ClickAsync(deadline, "button:text-is(\"Save settings\")");
            await harness.WaitForVisibleAsync(deadline, ".text-danger");
            var refusal = await harness.EvaluateOnAsync<string>(deadline, "reading the refusal", ".text-danger", "e => e.textContent ?? ''");
            Assert.StartsWith("a model is one of haiku, sonnet, opus, fable, or a pinned id of a supported agent", refusal);
            Assert.EndsWith("- not \"gizmo\"", refusal);
            Assert.Empty(sent);

            // A changed list sends the PATCH, then the PUT with the whole list.
            await deadline.BoundAsync("typing a good list", () => page.Locator(ModelsField).FillAsync("fable\n  haiku \n\nclaude-opus-5"));
            await harness.ClickAsync(deadline, "button:text-is(\"Save settings\")");
            await deadline.BoundAsync("waiting for both requests", () => WaitForRequests(sent, 2));
            Assert.Equal(
            [
                "PATCH /api/hatch/runners/" + Owned + " {\"tags\":[],\"under\":\"\"}",
                "PUT /api/hatch/runners/" + Owned + "/refused-models {\"models\":[\"fable\",\"haiku\",\"claude-opus-5\"]}",
            ], sent);
            var screenshotAfter = await harness.ScreenshotAsync(deadline, screenshot + "-after");
            output.WriteLine($"--- owner after, desktop: {screenshotAfter}");

            // A save of tags alone, the list as the stub still holds it, sends no PUT.
            sent.Clear();
            await deadline.BoundAsync("reverting the list", () => page.Locator(ModelsField).FillAsync("fable"));
            await deadline.BoundAsync("typing a tag", () => page.Locator(TagsField).FillAsync("gpu"));
            await harness.ClickAsync(deadline, "button:text-is(\"Save settings\")");
            await deadline.BoundAsync("waiting for the patch", () => WaitForRequests(sent, 1));
            await Task.Delay(300);
            Assert.Equal(["PATCH /api/hatch/runners/" + Owned + " {\"tags\":[\"gpu\"],\"under\":\"\"}"], sent);

            // The row says what it refuses on its third line.
            var row = await harness.EvaluateAsync<string>(deadline, "reading the owner's row", "() => [...document.querySelectorAll('.hatch-runner-row')].find(r => r.textContent.includes('" + Owned + "'))?.querySelector('.hatch-runner-row__third')?.textContent ?? ''");
            Assert.EndsWith("refuses fable", row);

            // A viewer reads it, and has no field.
            await harness.GoToAsync(deadline, $"runners/{Viewed}");
            await harness.WaitForVisibleAsync(deadline, "text=Refuses: haiku");
            Assert.Equal(0, await page.Locator(".hatch-field-grid textarea").CountAsync());
            var screenshotViewer = await harness.ScreenshotAsync(deadline, screenshot + "-viewer");
            output.WriteLine($"--- viewer, desktop: {screenshotViewer}");

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

    private static async Task WaitForRequests(List<string> sent, int count)
    {
        while (true)
        {
            lock (sent) if (sent.Count >= count) return;
            await Task.Delay(50);
        }
    }

    /// <summary>
    /// Two runners with invented names and no host, each with a key since the list draws no row without one: the caller's, holding
    /// <c>fable</c> and able to edit it, and somebody else's holding
    /// <c>haiku</c>. A press is recorded, not performed: every write answers 204,
    /// and the runners read keeps answering with the list as first held.
    /// </summary>
    private static async Task Stub(IBrowserContext context, List<string> sent)
    {
        var now = DateTimeOffset.UtcNow;

        RunnerDto Runner(string runnerName, bool canControl, string? owner, string refused) => new(
            Name: runnerName,
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
            Agent: null,
            Drives: [],
            Tags: [],
            Projects: [],
            For: owner,
            CanControl: canControl,
            RefusedModels: [refused],
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
                    EndedAt: null),
            ]);

        var runners = new[]
        {
            Runner(Owned, true, null, "fable"),
            Runner(Viewed, false, "an invented owner", "haiku"),
        };

        await context.RouteAsync(new System.Text.RegularExpressions.Regex(@"/api/hatch/runners(\?.*)?$"), route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(runners, Web),
        }));

        await context.RouteAsync("**/api/hatch/runners/**", async route =>
        {
            var request = route.Request;
            var line = $"{request.Method} {new Uri(request.Url).AbsolutePath}{(request.PostData is null ? "" : " " + request.PostData)}";
            lock (sent) sent.Add(line);

            await route.FulfillAsync(new() { Status = 204 });
        });
    }
}

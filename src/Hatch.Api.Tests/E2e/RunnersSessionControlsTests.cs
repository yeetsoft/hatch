using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.Playwright;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2154: the runner detail's Live sessions card, rendered by a real browser
/// from stubbed runners - an owner's with two live loop sessions and a once
/// session, and a viewer's with one loop session.
///
/// <para>The page is read from the DOM, then pressed: Pause on the second of the
/// two loop sessions must send one PATCH to that session and never the first,
/// Stop all must ask before it sends, and a refusal must draw inside the card.
/// The component tests prove what is drawn; a click is only provable here, since
/// the web app has no DOM test environment.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class RunnersSessionControlsTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Owned = "e2e-session-controls-owned";
    private const string Viewed = "e2e-session-controls-viewed";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheLiveSessionsCard_ActsOnOneSessionAtDesktop()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        await RunAsync(null, "RunnersSessionControls", "runners-session-controls");
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheLiveSessionsCard_ActsOnOneSessionAtThePhoneProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);

        await RunAsync(DeviceProfile.IPhone14Pro, "RunnersSessionControlsPhone", "runners-session-controls-phone");
    }

    private async Task RunAsync(DeviceProfile? profile, string name, string screenshot)
    {
        var harness = new E2eHarness(name);
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        var sent = new List<string>();
        try
        {
            await harness.LaunchBrowserAsync(deadline, profile);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            // The runners read and every press are stubbed, but the host still serves the bundle.
            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);

            await harness.OpenAsync(deadline, baseUrl, $"/apps/hatch/runners/{Owned}", context => Stub(context, sent));
            await harness.WaitForVisibleAsync(deadline, ".hatch-runner-sessions");

            var owner = await harness.EvaluateAsync<string>(deadline, "reading the owner's card", Read);
            await harness.EvaluateAsync<bool>(deadline, "scrolling to the card", "() => { document.querySelector('.hatch-runner-sessions').scrollIntoView({ block: 'end' }); return true; }");
            var path = await harness.ScreenshotAsync(deadline, screenshot);
            output.WriteLine($"--- owner, {profile?.Device ?? "desktop"}{Environment.NewLine}screenshot: {path}{Environment.NewLine}{owner}");
            var card = JsonSerializer.Deserialize<CardReading>(owner, Web)!;

            Assert.Equal(2, card.SessionPause);
            Assert.Equal(2, card.SessionStop);
            Assert.Equal(1, card.OneIncrement);
            Assert.True(card.PauseAll && card.StopAll, "the heading row draws Pause all and Stop all");
            Assert.False(card.ToControl, "an owner is not told whose it is to control");
            Assert.False(card.Overflows, "the document scrolls sideways");

            // Pause on the second loop session: one PATCH, to that session, nothing to the first.
            var page = harness.Page!;
            await deadline.BoundAsync("pausing the second session", () =>
                page.Locator(".hatch-runner-session").Nth(1).GetByRole(AriaRole.Button, new() { Name = "Pause", Exact = true }).ClickAsync());
            await deadline.BoundAsync("waiting for the pause", () => WaitForRequests(sent, 1));
            Assert.Equal(["PATCH /api/hatch/runners/" + Owned + "/sessions/2 {\"state\":\"paused\"}"], sent);

            // Stop all asks first, with the live loop count; Leave it sends nothing.
            await harness.ClickAsync(deadline, ".hatch-section-actions button:text-is(\"Stop all\")");
            await harness.WaitForVisibleAsync(deadline, "text=Stop 2 sessions after their current increment?");
            await deadline.BoundAsync("leaving it", () => page.GetByRole(AriaRole.Button, new() { Name = "Leave it" }).ClickAsync());
            await harness.WaitForHiddenAsync(deadline, "text=Stop 2 sessions after their current increment?");
            Assert.Single(sent);

            // Confirming sends one POST and the refusal the stub answers with draws inside the card.
            await harness.ClickAsync(deadline, ".hatch-section-actions button:text-is(\"Stop all\")");
            await deadline.BoundAsync("confirming", () => page.Locator(".hatch-form-actions").GetByRole(AriaRole.Button, new() { Name = "Stop all" }).ClickAsync());
            await harness.WaitForVisibleAsync(deadline, ".text-danger");
            Assert.Equal(2, sent.Count);
            Assert.Equal("POST /api/hatch/runners/" + Owned + "/stop-all", sent[1]);
            var refusal = await harness.EvaluateOnAsync<string>(deadline, "reading the refusal", ".text-danger", "e => e.textContent ?? ''");
            Assert.Equal(RefusalSentence, refusal);
            var inCard = await harness.EvaluateOnAsync<bool>(deadline, "locating the refusal", ".text-danger", "e => e.closest('.hatch-card, [class*=card]')?.textContent?.includes('Live sessions') === true");
            Assert.True(inCard, "the refusal draws inside the Live sessions card");

            // A viewer sees one line, and no button anywhere in the card.
            await harness.GoToAsync(deadline, $"runners/{Viewed}");
            await harness.WaitForVisibleAsync(deadline, ".hatch-runner-sessions");
            var viewer = JsonSerializer.Deserialize<CardReading>(await harness.EvaluateAsync<string>(deadline, "reading the viewer's card", Read), Web)!;
            Assert.True(viewer.ToControl, "a viewer is told whose it is to control");
            Assert.Equal(0, viewer.CardButtons);
            Assert.Equal(2, sent.Count);

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

    private const string RefusalSentence = "No session of this runner is yours to stop.";

    private static async Task WaitForRequests(List<string> sent, int count)
    {
        while (true)
        {
            lock (sent) if (sent.Count >= count) return;
            await Task.Delay(50);
        }
    }

    /// <summary>What the Live sessions card draws, read once. The card is found by its heading, as no case should depend on a class that is not its own.</summary>
    private const string Read = """
        () => {
          const heading = [...document.querySelectorAll('h2')].find(h => h.textContent === 'Live sessions');
          const card = heading.parentElement.closest('[class*=card]') ?? heading.parentElement.parentElement;
          const buttons = [...card.querySelectorAll('button')];
          const label = text => buttons.filter(b => b.textContent === text).length;
          const sessions = [...card.querySelectorAll('.hatch-runner-session')];
          const inSessions = text => sessions.reduce((n, s) => n + [...s.querySelectorAll('button')].filter(b => b.textContent === text).length, 0);
          return JSON.stringify({
            sessionPause: inSessions('Pause'),
            sessionStop: inSessions('Stop after this one'),
            oneIncrement: sessions.filter(s => s.textContent.includes('one increment')).length,
            pauseAll: label('Pause all') === 1,
            stopAll: label('Stop all') === 1,
            toControl: card.textContent.includes("to control"),
            cardButtons: buttons.length,
            overflows: document.documentElement.scrollWidth !== document.documentElement.clientWidth,
          });
        }
        """;

    private record CardReading(int SessionPause, int SessionStop, int OneIncrement, bool PauseAll, bool StopAll, bool ToControl, int CardButtons, bool Overflows);

    /// <summary>
    /// Two runners with invented names and no host. The first is the caller's,
    /// with two live loop sessions - the second started later, so it draws second -
    /// and one once session. The second is somebody else's, with one loop session.
    /// A press is recorded, not performed: the session endpoints answer 204, except
    /// stop-all, which refuses with a sentence.
    /// </summary>
    private static async Task Stub(IBrowserContext context, List<string> sent)
    {
        var now = DateTimeOffset.UtcNow;

        RunnerSessionDto Session(int id, string kind, int startedMinutesAgo) => new(
            Id: id,
            Where: $"example-host.invalid:/e2e/{id}",
            Kind: kind,
            Host: null,
            State: RunnerStates.Running,
            StartedAt: now.AddMinutes(-startedMinutesAgo),
            LastSeenAt: now,
            EndedAt: null);

        RunnerDto Runner(string runnerName, bool canControl, string? owner, params RunnerSessionDto[] sessions) => new(
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
            Sessions: sessions);

        var runners = new[]
        {
            Runner(Owned, true, null, Session(1, "loop", 30), Session(2, "loop", 20), Session(3, "once", 10)),
            Runner(Viewed, false, "an invented owner", Session(4, "loop", 30)),
        };

        await context.RouteAsync("**/api/hatch/runners*", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(runners, Web),
        }));

        await context.RouteAsync("**/api/hatch/runners/**", async route =>
        {
            var request = route.Request;
            var line = $"{request.Method} {new Uri(request.Url).AbsolutePath}{(request.PostData is null ? "" : " " + request.PostData)}";
            lock (sent) sent.Add(line);

            if (line.Contains("/stop-all", StringComparison.Ordinal))
                await route.FulfillAsync(new() { Status = 409, ContentType = "application/json", Body = JsonSerializer.Serialize(RefusalSentence) });
            else
                await route.FulfillAsync(new() { Status = 204 });
        });
    }
}

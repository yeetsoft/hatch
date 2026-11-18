using Hatch.Api.Tests.Hatch;
using Microsoft.Playwright;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2439, rendered: with waiting questions, the attention panel's
/// <i>Questions to answer</i> opens with one row to the Ballot and its count, and
/// each question follows as a smaller entry that still goes to its issue. Pressing
/// either lands in this tab and the panel closes behind it. Run at the desktop
/// viewport and at the <c>iPhone 14 Pro</c> descriptor (in Chromium, so it opens on
/// a machine whose WebKit does not), where the panel must not overflow sideways.
///
/// <para>A scratch database has no waiting question, so the attention read is
/// fulfilled with <c>page.RouteAsync</c>, serialised from the real record so a
/// renamed field breaks this build rather than the panel. Every other route,
/// and the bundle, is real. Each look is saved as a PNG beside the case's other
/// artifacts.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class AttentionBallotRowTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string PanelSelector = ".hatch-attention-panel";

    private const string BallotRow = $"{PanelSelector} a.hatch-attention-row-call";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task SeveralQuestions_OpenWithTheBallotRow_AtTheDesktopProfile() =>
        RunAsync("AttentionBallotRowDesktop", "ballot-row-desktop", null, 3);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task OneQuestion_OpensWithTheBallotRow_AtThePhoneProfile() =>
        RunAsync("AttentionBallotRowPhone", "ballot-row-phone", DeviceProfile.IPhone14ProChromium, 1);

    private async Task RunAsync(string name, string label, DeviceProfile? profile, int questions)
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

            await harness.OpenAsync(deadline, baseUrl, "/", context => StubAttention(context, questions));
            await harness.WaitForTopBarAsync(deadline);

            await OpenPanelAsync(harness, deadline);
            await harness.WaitForVisibleAsync(deadline, BallotRow);

            // The Ballot row heads the section, and says the count in the plural or not.
            var rows = await harness.EvaluateAsync<string[]>(
                deadline,
                "reading the questions section",
                """
                () => {
                  const heading = [...document.querySelectorAll('.hatch-attention-panel h3')]
                    .find((h) => h.textContent.includes('Questions to answer'));
                  const section = heading.closest('section') ?? heading.parentElement;
                  return [...section.querySelectorAll('a.hatch-attention-row')].map((a) => a.getAttribute('href'));
                }
                """);
            Assert.Equal(questions + 1, rows.Length);
            Assert.Equal("/ballot", rows[0]);
            Assert.Equal("E2E-30", rows[1].Split('/').Last());

            var callText = await harness.Page!.Locator(BallotRow).InnerTextAsync();
            Assert.Contains("Answer on the Ballot", callText);
            Assert.Contains(questions == 1 ? "1 question" : "3 questions", callText);

            // Nothing in the panel runs past its edge, on the desk or the phone.
            var overflow = await harness.EvaluateAsync<bool>(
                deadline,
                "measuring the panel",
                "() => { const p = document.querySelector('.hatch-attention-panel'); return p.scrollWidth > p.clientWidth; }");
            Assert.False(overflow, "the attention panel scrolls sideways");

            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, label)}");

            // An entry goes to its issue, in this tab, and the panel closes behind it.
            await harness.ClickAsync(deadline, $"{PanelSelector} a.hatch-attention-row-quiet");
            await WaitForPathAsync(harness, deadline, "/issues/E2E-30");
            await harness.WaitForHiddenAsync(deadline, PanelSelector);

            // The Ballot row goes to the Ballot, the same way.
            await OpenPanelAsync(harness, deadline);
            await harness.ClickAsync(deadline, BallotRow);
            await WaitForPathAsync(harness, deadline, "/ballot");
            await harness.WaitForHiddenAsync(deadline, PanelSelector);
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"{label}-ballot")}");

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

    private static async Task OpenPanelAsync(E2eHarness harness, Deadline deadline)
    {
        await harness.ClickAsync(deadline, E2eHarness.AttentionSelector);
        await harness.WaitForVisibleAsync(deadline, PanelSelector);
        // The human group opens its sections only when pressed, on a board with nothing urgent.
        if (!await harness.IsVisibleAsync(deadline, BallotRow))
        {
            await harness.ClickAsync(deadline, $"{PanelSelector} h2 button.hatch-attention-fold:text-is(\"Human\")");
            await harness.ClickAsync(deadline, $"{PanelSelector} h3 button.hatch-attention-fold:text-is(\"Questions to answer\")");
        }
    }

    private static Task WaitForPathAsync(E2eHarness harness, Deadline deadline, string path) =>
        deadline.BoundAsync($"waiting for {path}", () => harness.Page!.WaitForURLAsync($"**{path}"));

    /// <summary>The attention read with <paramref name="count"/> questions waiting, oldest first.</summary>
    private static Task StubAttention(IBrowserContext context, int count)
    {
        var questions = Enumerable.Range(0, count)
            .Select(i => new QuestionDto(
                Id: i + 1,
                IssueKey: $"E2E-{30 + i}",
                IssueTitle: "A story with a question",
                Body: i == 0
                    ? "Should the panel show the time each question has waited, or leave that to the Ballot?"
                    : "Which of the two names should it keep?",
                AskedBy: "a-runner",
                AskedAt: DateTimeOffset.UtcNow.AddHours(-(count - i)),
                Options: null,
                Answers: []))
            .ToList();

        var attention = new AttentionDto(
            Reviews: [],
            InReviewWithoutPullRequest: 0,
            Questions: questions,
            Conflicts: []);

        return context.RouteAsync("**/api/attention", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(attention, Web),
        }));
    }
}

using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// Escape on the board turns the flow view's banding off, after anything smaller
/// has taken it: a dialog open over the board closes first and the banding stays,
/// and the board's search box clears first. Real key presses, because a press runs
/// microtasks between the document's listeners and a script-dispatched event does
/// not. The New issue dialog opens on Epic. Desktop Chromium only.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class BoardEscapeBandingTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 60;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string BandButton = ".hatch-flow-viz-control";
    private const string ModalPanel = ".hatch-modal__panel";
    private const string SearchBox = ".hatch-board-search";

    /// <summary>Runs in the page: the dialog's field labels in document order.</summary>
    private const string LabelsScript = """
        () => [...document.querySelectorAll('.hatch-modal__panel .hatch-field__label')].map((l) => l.textContent)
        """;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task Escape_TurnsTheBandingOff_AfterAnythingSmallerTakesIt()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("BoardEscapeBanding");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline, profile: null);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            await SeedProjectAsync(harness.Services);

            await harness.OpenBoardAsync(deadline, baseUrl);
            await harness.WaitForFirstColumnAsync(deadline);
            var page = harness.Page!;

            // Nothing open: one Escape turns it off.
            await harness.ClickAsync(deadline, BandButton);
            Assert.True(await IsBandingAsync(harness, deadline), "the band button did not press");
            await PressEscapeAsync(harness, deadline);
            Assert.False(await IsBandingAsync(harness, deadline), "Escape left the banding on");

            // A dialog open: the first Escape closes it alone, the second turns the banding off.
            await harness.ClickAsync(deadline, BandButton);
            await deadline.BoundAsync("pressing c", () => page.Keyboard.PressAsync("c"));
            await harness.WaitForVisibleAsync(deadline, ModalPanel);

            // The dialog opens on Epic, and so draws the Parent field an epic takes.
            var labels = await harness.EvaluateAsync<string[]>(deadline, "reading the dialog's fields", LabelsScript);
            output.WriteLine(string.Join(", ", labels));
            Assert.Contains("Parent", labels);
            Assert.Contains("epic", (await page.Locator(ModalPanel + " .hatch-type-pill-press").First.InnerTextAsync()).ToLowerInvariant());

            await PressEscapeAsync(harness, deadline);
            await harness.WaitForHiddenAsync(deadline, ModalPanel);
            Assert.True(await IsBandingAsync(harness, deadline), "the first Escape turned the banding off with the dialog");
            await PressEscapeAsync(harness, deadline);
            Assert.False(await IsBandingAsync(harness, deadline), "the second Escape left the banding on");

            // The search box takes its own Escape first.
            await harness.ClickAsync(deadline, BandButton);
            await harness.SearchBoardAsync(deadline, "nothing");
            await deadline.BoundAsync("focusing the search box", () => page.Locator(SearchBox).FocusAsync());
            await PressEscapeAsync(harness, deadline);
            Assert.True(await IsBandingAsync(harness, deadline), "Escape in the search box turned the banding off");
            output.WriteLine("banding on: " + await harness.ScreenshotAsync(deadline, "banding-on"));

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

    private static Task PressEscapeAsync(E2eHarness harness, Deadline deadline) =>
        deadline.BoundAsync("pressing Escape", () => harness.Page!.Keyboard.PressAsync("Escape"));

    private static async Task<bool> IsBandingAsync(E2eHarness harness, Deadline deadline) =>
        await harness.EvaluateOnAsync<string>(deadline, "reading the band button", BandButton, "(el) => el.getAttribute('aria-pressed')") == "true";

    /// <summary>One project and one column, written to the host's own database: the dialog files nowhere without a project, and the board draws no column without a status.</summary>
    private static async Task SeedProjectAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();

        db.AddRange(
            new EfHatchProject { Key = "ORD", Name = "Order", CreatedAt = DateTimeOffset.UtcNow },
            new EfHatchStatus { Name = "todo", SortOrder = 10 });
        await db.SaveChangesAsync();
    }
}

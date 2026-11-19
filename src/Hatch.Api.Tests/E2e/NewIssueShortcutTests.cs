using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// <c>c</c> on the board opens the New issue dialog with its Title box focused
/// and empty; in the board's search box it types the letter and opens nothing;
/// with Ctrl held it opens nothing, so copy still copies. Desktop Chromium only.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class NewIssueShortcutTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 60;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string ModalPanel = ".hatch-modal__panel";

    private const string TitleInput = ".hatch-modal__panel .hatch-field:has(> .hatch-field__control > .hatch-field__label:text-is(\"Title\")) input";

    private const string SearchBox = ".hatch-board-search";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task C_OpensTheDialog_ExceptWhereItIsTyped()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("NewIssueShortcut");
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

            // In the search box: the letter is typed, and nothing opens.
            await harness.ClickAsync(deadline, SearchBox);
            await deadline.BoundAsync("typing c in the search box", () => harness.Page!.Keyboard.PressAsync("c"));
            var typed = await harness.EvaluateOnAsync<string>(deadline, "reading the search box", SearchBox, "(el) => el.value");
            Assert.Equal("c", typed);
            Assert.False(await harness.IsVisibleAsync(deadline, ModalPanel), "the dialog opened over the search box");
            output.WriteLine("search box: " + await harness.ScreenshotAsync(deadline, "1-search-box"));

            // Out of the box, with Ctrl held: copy, not a shortcut.
            await deadline.BoundAsync("leaving the search box", () => harness.Page!.Locator(SearchBox).BlurAsync());
            await deadline.BoundAsync("pressing Ctrl+C", () => harness.Page!.Keyboard.PressAsync("Control+c"));
            Assert.False(await harness.IsVisibleAsync(deadline, ModalPanel), "Ctrl+C opened the dialog");

            // On the board: the dialog, Title focused and empty.
            await deadline.BoundAsync("pressing c", () => harness.Page!.Keyboard.PressAsync("c"));
            await harness.WaitForVisibleAsync(deadline, ModalPanel);
            var titleState = await harness.EvaluateOnAsync<string>(
                deadline,
                "checking the Title input",
                TitleInput,
                "(el) => (document.activeElement === el ? 'focused' : 'unfocused') + ':' + el.value");
            output.WriteLine("dialog: " + await harness.ScreenshotAsync(deadline, "2-dialog"));
            Assert.Equal("focused:", titleState);

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

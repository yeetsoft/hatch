using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// Where focus is when the New issue dialog opens and when it closes: the Title
/// input, which asks for focus with <c>autoFocus</c>, keeps it rather than the
/// panel taking it, and closing the dialog returns focus to the button that
/// opened it. Desktop Chromium only: WebKit does not focus a button on click, so
/// the opener there would not be the button.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class NewIssueFocusTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 60;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string ModalPanel = ".hatch-modal__panel";

    private const string TitleInput = ".hatch-modal__panel .hatch-field:has(> .hatch-field__control > .hatch-field__label:text-is(\"Title\")) input";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task Title_KeepsFocusOnOpen_AndTheOpenerGetsItBackOnClose()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("NewIssueFocus");
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
            await harness.ClickAsync(deadline, ".hatch-board-new");
            await harness.WaitForVisibleAsync(deadline, ModalPanel);

            var onTitle = await harness.EvaluateOnAsync<bool>(
                deadline,
                "checking the Title input holds focus",
                TitleInput,
                "(el) => document.activeElement === el");
            await harness.ScreenshotAsync(deadline, "NewIssueFocus-open");
            Assert.True(onTitle, "the Title input is not document.activeElement after the dialog opens");

            await harness.ClickAsync(deadline, ".hatch-modal__close");
            await harness.WaitForHiddenAsync(deadline, ModalPanel);

            var onOpener = await harness.EvaluateAsync<bool>(
                deadline,
                "checking focus returned to the opener",
                "() => document.activeElement.matches('.hatch-board-new')");
            await harness.ScreenshotAsync(deadline, "NewIssueFocus-closed");
            Assert.True(onOpener, "focus did not return to the button that opened the dialog");

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

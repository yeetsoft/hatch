using System.Net.Http.Json;
using System.Text.Json;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2835: the issue page's Parent and Filed from pickers, and the New issue
/// dialog's Parent picker, find an issue closed well before the board's done
/// horizon by title words, and taking it saves.
///
/// <para>Seeded: a todo column and a terminal one, an open task, and a closed
/// epic and a closed task sharing two title words, both closed 30 days ago -
/// long enough to be off the board under its default horizon. On the open
/// task's own page, Parent's lookup offers the closed epic (a legal parent for
/// a task) and not the closed task (not one); Filed from's offers both, since
/// it refuses only the page's own issue. The New issue dialog, opened from
/// <c>/search</c> via <c>&gt;new</c> and from the board, offers the closed
/// epic for its own Parent (an epic is a legal parent for the epic the dialog
/// opens on) and files under it either way.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class ClosedIssuePickerTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 120;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>The two words every closed issue's title shares, typed to find them.</summary>
    private const string TitleWords = "zenith horizon";

    private const string Prompt = ".hatch-omni-input";
    private const string Panel = ".hatch-modal__panel";
    private const string DialogTitle = ".hatch-modal__panel .hatch-field:has(> .hatch-field__control > .hatch-field__label:text-is(\"Title\")) input";
    private const string FileIt = ".hatch-modal__panel .hatch-newissue-actions button:has-text(\"File it\")";
    private const string DialogParentButton = ".hatch-modal__panel button[role=combobox][aria-label=Parent]";
    private const string DialogParentOptions = ".hatch-modal__panel [role=listbox][aria-label=Parent] [role=option]";

    private const string IssuePage = ".hatch-issue-page";
    private const string PageParentButton = ".hatch-issue-page button[role=combobox][aria-label=Parent]";
    private const string PageParentOptions = ".hatch-issue-page [role=listbox][aria-label=Parent] [role=option]";
    private const string PageFiledFromButton = ".hatch-issue-page button[role=combobox][aria-label=\"Filed from\"]";
    private const string PageFiledFromOptions = ".hatch-issue-page [role=listbox][aria-label=\"Filed from\"] [role=option]";

    /// <summary>The search box inside whichever picker's popup is open - only one is, at a time.</summary>
    private const string PickerSearch = ".hatch-issue-picker-popup .hatch-issue-picker-search";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task Desktop_FindsAndTakesAClosedIssue() => RunAsync("desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task Phone_FindsAndTakesAClosedIssue()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("phone", DeviceProfile.IPhone14Pro);
    }

    private async Task RunAsync(string where, DeviceProfile? profile)
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness($"ClosedIssuePicker-{where}");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline, profile);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            await SeedAsync(harness.Services);

            await harness.OpenAsync(deadline, baseUrl, "/issues/CLO-1");
            await harness.WaitForVisibleAsync(deadline, IssuePage);
            var page = harness.Page!;

            // The Parent and Filed from fields are behind the Details card's
            // own Edit press.
            await deadline.BoundAsync(
                "opening Details for editing",
                () => page.Locator(".hatch-section-title:text-is(\"Details\")")
                    .Locator("xpath=../..")
                    .GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true })
                    .ClickAsync());
            await harness.WaitForVisibleAsync(deadline, PageParentButton);

            // Parent: the closed epic shows up, the closed task - a legal type
            // for nothing a task hangs under - does not.
            await harness.ClickAsync(deadline, PageParentButton);
            await deadline.BoundAsync("typing title words", () => page.Locator(PickerSearch).FillAsync(TitleWords));
            await deadline.BoundAsync(
                "waiting for the closed epic among Parent's options",
                () => page.Locator(PageParentOptions, new() { HasTextString = "CLO-2" }).First.WaitForAsync());
            var parentOptions = string.Join("|", await page.Locator(PageParentOptions).AllInnerTextsAsync());
            output.WriteLine("page parent options: " + parentOptions);
            Assert.Contains("CLO-2", parentOptions);
            Assert.DoesNotContain("CLO-3", parentOptions);
            output.WriteLine("page-parent: " + await harness.ScreenshotAsync(deadline, "page-parent"));
            await deadline.BoundAsync(
                "taking the closed epic as parent",
                () => page.Locator(PageParentOptions, new() { HasTextString = "CLO-2" }).First.ClickAsync());
            await deadline.BoundAsync(
                "waiting for the Parent face to read CLO-2",
                () => page.Locator(PageParentButton, new() { HasTextString = "CLO-2" }).WaitForAsync());

            // Filed from: both closed issues show up, neither the page's own.
            await harness.ClickAsync(deadline, PageFiledFromButton);
            await deadline.BoundAsync("typing title words", () => page.Locator(PickerSearch).FillAsync(TitleWords));
            await deadline.BoundAsync(
                "waiting for both closed issues among Filed from's options",
                () => page.Locator(PageFiledFromOptions, new() { HasTextString = "CLO-3" }).First.WaitForAsync());
            var filedFromOptions = string.Join("|", await page.Locator(PageFiledFromOptions).AllInnerTextsAsync());
            output.WriteLine("page filed-from options: " + filedFromOptions);
            Assert.Contains("CLO-2", filedFromOptions);
            Assert.Contains("CLO-3", filedFromOptions);
            Assert.DoesNotContain("CLO-1", filedFromOptions);
            output.WriteLine("page-filed-from: " + await harness.ScreenshotAsync(deadline, "page-filed-from"));
            await deadline.BoundAsync(
                "taking the closed task as filed from",
                () => page.Locator(PageFiledFromOptions, new() { HasTextString = "CLO-3" }).First.ClickAsync());
            await deadline.BoundAsync(
                "waiting for the Filed from face to read CLO-3",
                () => page.Locator(PageFiledFromButton, new() { HasTextString = "CLO-3" }).WaitForAsync());

            using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
            var task = await http.GetFromJsonAsync<IssueDto>("/api/issues/CLO-1", Web);
            Assert.Equal("CLO-2", task!.ParentKey);
            Assert.Equal("CLO-3", task.FiledFromKey);

            // The New issue dialog, opened off the board via the console: the
            // closed epic is a legal parent for the epic it opens on.
            await harness.GoToAsync(deadline, "/search");
            await harness.WaitForTopBarAsync(deadline);
            await OpenNewIssueAsync(harness, deadline);
            await harness.WaitForVisibleAsync(deadline, Panel);
            await harness.ClickAsync(deadline, DialogParentButton);
            await deadline.BoundAsync("typing title words", () => page.Locator(PickerSearch).FillAsync(TitleWords));
            await deadline.BoundAsync(
                "waiting for the closed epic among the dialog's Parent options",
                () => page.Locator(DialogParentOptions, new() { HasTextString = "CLO-2" }).First.WaitForAsync());
            var dialogOptionsFromConsole = string.Join("|", await page.Locator(DialogParentOptions).AllInnerTextsAsync());
            output.WriteLine("console dialog parent options: " + dialogOptionsFromConsole);
            Assert.Contains("CLO-2", dialogOptionsFromConsole);
            Assert.DoesNotContain("CLO-3", dialogOptionsFromConsole);
            output.WriteLine("dialog-from-console: " + await harness.ScreenshotAsync(deadline, "dialog-from-console"));
            await deadline.BoundAsync(
                "taking the closed epic as parent",
                () => page.Locator(DialogParentOptions, new() { HasTextString = "CLO-2" }).First.ClickAsync());
            await deadline.BoundAsync("typing the title", () => page.Locator(DialogTitle).FillAsync("Filed from the console under the closed epic"));
            await harness.ClickAsync(deadline, FileIt);
            await deadline.BoundAsync("waiting for the new issue's page", () => page.WaitForURLAsync("**/issues/CLO-*"));
            Assert.Equal("/issues/CLO-4", new Uri(page.Url).AbsolutePath);

            var fromConsole = await http.GetFromJsonAsync<IssueDto>("/api/issues/CLO-4", Web);
            Assert.Equal("CLO-2", fromConsole!.ParentKey);

            // The same dialog, opened from the board.
            await harness.GoToAsync(deadline, "/?project=CLO");
            await harness.WaitForFirstColumnAsync(deadline);
            await harness.ClickAsync(deadline, "button[aria-label=\"New issue\"]");
            await harness.WaitForVisibleAsync(deadline, Panel);
            await harness.ClickAsync(deadline, DialogParentButton);
            await deadline.BoundAsync("typing title words", () => page.Locator(PickerSearch).FillAsync(TitleWords));
            await deadline.BoundAsync(
                "waiting for the closed epic among the dialog's Parent options",
                () => page.Locator(DialogParentOptions, new() { HasTextString = "CLO-2" }).First.WaitForAsync());
            var dialogOptionsFromBoard = string.Join("|", await page.Locator(DialogParentOptions).AllInnerTextsAsync());
            output.WriteLine("board dialog parent options: " + dialogOptionsFromBoard);
            Assert.Contains("CLO-2", dialogOptionsFromBoard);
            Assert.DoesNotContain("CLO-3", dialogOptionsFromBoard);
            output.WriteLine("dialog-from-board: " + await harness.ScreenshotAsync(deadline, "dialog-from-board"));
            await deadline.BoundAsync(
                "taking the closed epic as parent",
                () => page.Locator(DialogParentOptions, new() { HasTextString = "CLO-2" }).First.ClickAsync());
            const string boardTitle = "Filed from the board under the closed epic";
            await deadline.BoundAsync("typing the title", () => page.Locator(DialogTitle).FillAsync(boardTitle));
            await harness.ClickAsync(deadline, FileIt);
            await harness.WaitForHiddenAsync(deadline, Panel);
            await deadline.BoundAsync(
                "waiting for the new card",
                () => page.Locator(E2eHarness.Column, new() { HasTextString = boardTitle }).First.WaitForAsync());

            var fromBoard = await http.GetFromJsonAsync<IssueDto>("/api/issues/CLO-5", Web);
            Assert.Equal("CLO-2", fromBoard!.ParentKey);

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

    private static async Task OpenNewIssueAsync(E2eHarness harness, Deadline deadline)
    {
        var page = harness.Page!;
        await deadline.BoundAsync("opening the console", () => page.Keyboard.PressAsync("/"));
        await harness.WaitForVisibleAsync(deadline, Prompt);
        await deadline.BoundAsync("typing >new", () => page.Locator(Prompt).FillAsync(">new"));
        await deadline.BoundAsync("running New issue", () => page.Keyboard.PressAsync("Enter"));
    }

    /// <summary>
    /// One project, a todo column and a terminal one: an open task, and a
    /// closed epic and a closed task sharing two title words, both closed 30
    /// days ago - past the board's default 48-hour done horizon, so neither
    /// is among its own cards.
    /// </summary>
    private static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
        var now = DateTimeOffset.UtcNow;
        var closedAt = now.AddDays(-30);

        var project = new EfHatchProject { Key = "CLO", Name = "Closed lookup", CreatedAt = now, NextIssueNumber = 4 };
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 10 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 20, IsTerminal = true };
        db.AddRange(project, todo, done);
        await db.SaveChangesAsync();

        db.Issues.Add(new EfHatchIssue
        {
            ProjectId = project.Id, Number = 1, Type = "task", Title = "Open task on the board",
            Description = "", StatusId = todo.Id, Rank = 1024, CreatedBy = "operator", CreatedAt = now, UpdatedAt = now,
        });
        db.Issues.Add(new EfHatchIssue
        {
            ProjectId = project.Id, Number = 2, Type = "epic", Title = "Epic past the zenith horizon",
            Description = "", StatusId = done.Id, Rank = 1024, CreatedBy = "operator", CreatedAt = now, UpdatedAt = now, ClosedAt = closedAt,
        });
        db.Issues.Add(new EfHatchIssue
        {
            ProjectId = project.Id, Number = 3, Type = "task", Title = "Task past the zenith horizon",
            Description = "", StatusId = done.Id, Rank = 1024, CreatedBy = "operator", CreatedAt = now, UpdatedAt = now, ClosedAt = closedAt,
        });

        await db.SaveChangesAsync();
    }
}

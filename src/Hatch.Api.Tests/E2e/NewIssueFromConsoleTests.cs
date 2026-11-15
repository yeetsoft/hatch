using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2335's <c>&gt;new</c>, driven on the real app. Off the board, with the tab
/// scoped to two of three projects: the console mounts the New issue dialog, whose
/// project chooser lists the scope's two and opens on the first, whose parent
/// picker lists the board's issues for the chosen project, which reads the
/// assignee directory when it opens, and filing from it opens the new issue's
/// page. On the board, filtered to a project: the board's own dialog opens on that
/// project, and filing from it leaves the board where it was, with the new card on
/// it. Desktop in Chromium, and the phone profile in WebKit.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class NewIssueFromConsoleTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 120;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Prompt = ".hatch-omni-input";
    private const string Panel = ".hatch-modal__panel";
    private const string Tiles = ".hatch-modal__panel .hatch-project-chooser__tile";
    private const string CheckedTile = ".hatch-modal__panel .hatch-project-chooser__tile:has(input:checked)";
    private const string Title = ".hatch-modal__panel .hatch-field:has(> .hatch-field__control > .hatch-field__label:text-is(\"Title\")) input";
    private const string FileIt = ".hatch-modal__panel .hatch-newissue-actions button:has-text(\"File it\")";
    private const string ParentButton = ".hatch-modal__panel button[role=combobox][aria-label=Parent]";
    private const string ParentOptions = ".hatch-modal__panel [role=listbox][aria-label=Parent] [role=option]";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task Desktop_FilesFromTheConsole() => RunAsync("desktop", null, null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task Phone_FilesFromTheConsole()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("phone", DeviceProfile.IPhone14Pro, null);
    }

    /// <summary>The same flow in Chromium at the iPhone 14 Pro's width: where WebKit is frozen the profile case above skips, and a layout breakpoint is a width, not an engine.</summary>
    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task PhoneWidth_FilesFromTheConsole() => RunAsync("phone-width", null, 393);

    private async Task RunAsync(string where, DeviceProfile? profile, int? width)
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness($"NewIssueFromConsole-{where}");
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

            var directoryReads = 0;
            await harness.OpenAsync(deadline, baseUrl, "/search?scope=AAA,BBB", context =>
            {
                context.Request += (_, request) =>
                {
                    if (new Uri(request.Url).AbsolutePath == "/api/assignees") Interlocked.Increment(ref directoryReads);
                };
                return Task.CompletedTask;
            });
            await harness.WaitForTopBarAsync(deadline);
            if (width is { } w) await harness.SetViewportAsync(deadline, w, 844);
            var page = harness.Page!;

            // Off the board: `>new`, Enter closes the prompt and opens the dialog.
            await OpenNewIssueAsync(harness, deadline);
            await harness.WaitForVisibleAsync(deadline, Panel);
            Assert.Equal(0, await Count(harness, deadline, ".hatch-omni"));

            // The scope's two projects, on the first. The tab's projects load on their own clock, so wait for the chosen one.
            await harness.WaitForVisibleAsync(deadline, CheckedTile);
            Assert.Equal(2, await Count(harness, deadline, Tiles));
            Assert.StartsWith("AAA", await Text(harness, deadline, CheckedTile));
            await deadline.BoundAsync("waiting for the assignee directory", async () =>
            {
                while (Volatile.Read(ref directoryReads) == 0) await Task.Delay(50);
            });
            output.WriteLine("dialog: " + await harness.ScreenshotAsync(deadline, "dialog"));

            // The parent picker lists the board's issues for the chosen project, and no other's.
            await harness.ClickAsync(deadline, ParentButton);
            await harness.WaitForVisibleAsync(deadline, ParentOptions);
            var parents = string.Join("|", await Texts(harness, deadline, ParentOptions));
            Assert.Contains("AAA-1", parents);
            Assert.DoesNotContain("BBB-1", parents);
            output.WriteLine("parents: " + await harness.ScreenshotAsync(deadline, "parents"));
            await harness.ClickAsync(deadline, ParentButton);

            // Filing opens the new issue's page, with the scope kept.
            await deadline.BoundAsync("typing the title", () => page.Locator(Title).FillAsync("Filed from the console"));
            await harness.ClickAsync(deadline, FileIt);
            await deadline.BoundAsync("waiting for the issue's page", () => page.WaitForURLAsync("**/issues/AAA-*"));
            var url = new Uri(page.Url);
            Assert.Equal("/issues/AAA-2", url.AbsolutePath);
            Assert.Contains("scope=AAA", url.Query);
            output.WriteLine("issue: " + await harness.ScreenshotAsync(deadline, "issue"));

            // On the board, filtered to BBB: the board's own dialog, on BBB.
            await harness.GoToAsync(deadline, "/?project=BBB");
            await harness.WaitForFirstColumnAsync(deadline);
            await OpenNewIssueAsync(harness, deadline);
            await harness.WaitForVisibleAsync(deadline, Panel);
            Assert.StartsWith("BBB", await Text(harness, deadline, CheckedTile));
            output.WriteLine("board dialog: " + await harness.ScreenshotAsync(deadline, "board-dialog"));

            // Filing from it stays on the board, and the card is there.
            await deadline.BoundAsync("typing the title", () => page.Locator(Title).FillAsync("Filed on the board"));
            await harness.ClickAsync(deadline, FileIt);
            await harness.WaitForHiddenAsync(deadline, Panel);
            await deadline.BoundAsync("waiting for the new card", () => page.Locator(E2eHarness.Column, new() { HasTextString = "Filed on the board" }).First.WaitForAsync());
            Assert.Equal("/", new Uri(page.Url).AbsolutePath);
            output.WriteLine("board: " + await harness.ScreenshotAsync(deadline, "board"));

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
        Assert.Equal(["New issue"], await Texts(harness, deadline, ".hatch-omni [role=option] .hatch-omni-title"));
        await deadline.BoundAsync("running New issue", () => page.Keyboard.PressAsync("Enter"));
    }

    private static Task<int> Count(E2eHarness harness, Deadline deadline, string selector) =>
        deadline.BoundAsync($"counting {selector}", () => harness.Page!.Locator(selector).CountAsync());

    private static Task<string> Text(E2eHarness harness, Deadline deadline, string selector) =>
        deadline.BoundAsync($"reading {selector}", () => harness.Page!.Locator(selector).InnerTextAsync());

    private static async Task<IReadOnlyList<string>> Texts(E2eHarness harness, Deadline deadline, string selector) =>
        await deadline.BoundAsync($"reading {selector}", () => harness.Page!.Locator(selector).AllInnerTextsAsync());

    /// <summary>Three projects, one column, and a story in the first two: the first filing under AAA is AAA-2, and the story is what the parent picker offers.</summary>
    private static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
        var now = DateTimeOffset.UtcNow;

        var projects = new[] { "AAA", "BBB", "CCC" }.Select(key => new EfHatchProject { Key = key, Name = $"Project {key}", CreatedAt = now }).ToArray();
        var status = new EfHatchStatus { Name = "todo", SortOrder = 10 };
        db.AddRange(projects);
        db.Add(status);
        await db.SaveChangesAsync();

        for (var i = 0; i < 2; i++)
        {
            projects[i].NextIssueNumber = 2;
            db.Issues.Add(new EfHatchIssue
            {
                ProjectId = projects[i].Id,
                Number = 1,
                Type = "story",
                Title = $"A story in {projects[i].Key}",
                Description = "",
                StatusId = status.Id,
                Rank = 1024,
                CreatedBy = "operator",
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync();
    }
}

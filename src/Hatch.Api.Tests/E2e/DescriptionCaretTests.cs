using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2225: typing into a shared-editor box in Chromium while the page behind
/// it refreshes must never move the caret to the start of the box. The caret
/// reset is <c>MarkdownEditor</c>'s value-to-model sync effect running as a
/// passive effect - a later task that can see a model the user has since typed
/// past, and writes it back with <c>model.setValue()</c>, which Monaco treats
/// as a flush.
///
/// <para>Same needs as <see cref="DescriptionPasteTests"/>, whose skeleton this
/// follows. CPU throttling over CDP widens the gap a passive effect's delay
/// leaves: Chromium runs the real host unthrottled fast enough that the race
/// rarely lands without it.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class DescriptionCaretTests
{
    private const int CaseBudgetSeconds = 90;

    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string EditorHost = ".hatch-md-editor";

    private const string ModalPanel = ".hatch-modal__panel";

    private const string CommentBox = ".hatch-comment-box";

    /// <summary>
    /// Long enough to wrap onto a second line in a three- or six-row box - "the
    /// beginning of the line" the original report described - and long enough,
    /// at the per-character delay below, to carry several refreshes.
    /// </summary>
    private const string Sentence =
        "The quick brown fox jumps over the lazy dog while the board quietly refreshes behind it.";

    private const int PerCharacterDelayMs = 20;

    private const int RefreshEveryMs = 150;

    /// <summary>Verified against both sides: at this rate, the unfixed editor's sync effect
    /// loses the race reliably (four runs in a row, both cases) and the fixed one never does
    /// (four more). Lower and the unfixed editor sometimes gets lucky; higher and even the
    /// fixed editor starts missing - a React concurrent-rendering edge this ticket's fix does
    /// not claim to close, and a false positive this case must not report.</summary>
    private const double CpuThrottleRate = 3;

    /// <summary>How many times the race, once it has landed, is given a chance to be
    /// masked by a correct final render: none - the assertion is on order, not
    /// just presence, so a reset that scrambled the text cannot pass by luck.</summary>
    private const int UndoPresses = 6;

    /// <summary>Runs in the page against a resolved host element: its rendered text, with Monaco's
    /// own substitution of some spaces for a non-breaking space in its view (a rendering nuance,
    /// nothing to do with the model) undone, so this reads as what was typed.</summary>
    private const string TextContentScript = "(host) => host.textContent.replace(/\\u00A0/g, ' ')";

    private const string DispatchFocusScript = "() => { window.dispatchEvent(new Event('focus')); return true; }";

    /// <summary>How long the page is given, after the last keystroke's input event, to settle its
    /// state and redraw before the box is read.</summary>
    private const int SettleMs = 300;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TypingIntoTheNewIssueDialogsDescription_SurvivesTheBoardRefreshingBehindIt()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("DescriptionCaretNewIssue");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            await SeedProjectAsync(harness.Services);

            await harness.OpenBoardAsync(deadline, baseUrl);
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForFirstColumnAsync(deadline);

            await harness.ClickAsync(deadline, ".hatch-board-new");
            await harness.WaitForVisibleAsync(deadline, ModalPanel);
            await harness.WaitForVisibleAsync(deadline, EditorHost);
            await harness.ClickAsync(deadline, EditorHost);

            var cdp = await harness.NewCdpSessionAsync(deadline);
            try
            {
                await ThrottleCpuAsync(deadline, cdp, CpuThrottleRate);
                await TypeWhileRefreshingAsync(harness, deadline);
            }
            finally
            {
                await ThrottleCpuAsync(deadline, cdp, 1);
            }

            await SettleAsync(harness, deadline);

            var typed = await harness.EvaluateOnAsync<string>(deadline, "reading the dialog's editor", EditorHost, TextContentScript);
            Assert.Equal(Sentence, typed);

            await UndoRepeatedlyAsync(harness, deadline, UndoPresses);
            var afterUndo = await harness.EvaluateOnAsync<string>(deadline, "reading the dialog's editor after undo", EditorHost, TextContentScript);
            Assert.True(afterUndo.Length < Sentence.Length, $"Ctrl/Cmd+Z pressed {UndoPresses} times left the text unchanged: the refresh dropped the undo history.");
            Assert.StartsWith(afterUndo, Sentence, StringComparison.Ordinal);

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

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TypingIntoTheIssuePagesCommentBox_SurvivesThePageRefreshingBehindIt()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("DescriptionCaretComment");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            var key = await SeedIssueAsync(harness.Services);

            await harness.OpenAsync(deadline, baseUrl, $"/issues/{key}");
            await harness.WaitForTopBarAsync(deadline);

            var commentEditor = $"{CommentBox} {EditorHost}";
            await harness.ClickAsync(deadline, $"{CommentBox} textarea");
            await harness.WaitForVisibleAsync(deadline, commentEditor);
            await harness.ClickAsync(deadline, commentEditor);

            var cdp = await harness.NewCdpSessionAsync(deadline);
            try
            {
                await ThrottleCpuAsync(deadline, cdp, CpuThrottleRate);
                await TypeWhileRefreshingAsync(harness, deadline);
            }
            finally
            {
                await ThrottleCpuAsync(deadline, cdp, 1);
            }

            await SettleAsync(harness, deadline);

            var typed = await harness.EvaluateOnAsync<string>(deadline, "reading the comment editor", commentEditor, TextContentScript);
            Assert.Equal(Sentence, typed);

            await UndoRepeatedlyAsync(harness, deadline, UndoPresses);
            var afterUndo = await harness.EvaluateOnAsync<string>(deadline, "reading the comment editor after undo", commentEditor, TextContentScript);
            Assert.True(afterUndo.Length < Sentence.Length, $"Ctrl/Cmd+Z pressed {UndoPresses} times left the text unchanged: the refresh dropped the undo history.");
            Assert.StartsWith(afterUndo, Sentence, StringComparison.Ordinal);

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

    /// <summary>
    /// Types <see cref="Sentence"/> one key at a time, each landing <see cref="PerCharacterDelayMs"/>
    /// after the last - a real keystroke, not an inserted blob, since the race lives in the gap
    /// between a keystroke's commit and its passive effect - while a second task dispatches a
    /// <c>focus</c> event on <paramref name="harness"/>'s page every <see cref="RefreshEveryMs"/>,
    /// which is what every page under test refetches its data on.
    /// </summary>
    private static async Task TypeWhileRefreshingAsync(E2eHarness harness, Deadline deadline)
    {
        var typing = deadline.BoundAsync("typing the sentence", () =>
            harness.Page!.Keyboard.TypeAsync(Sentence, new KeyboardTypeOptions { Delay = PerCharacterDelayMs }));

        var totalMs = Sentence.Length * PerCharacterDelayMs;
        var refreshing = Task.Run(async () =>
        {
            for (var elapsed = 0; elapsed < totalMs; elapsed += RefreshEveryMs)
            {
                await Task.Delay(RefreshEveryMs);
                await harness.EvaluateAsync<bool>(deadline, "refreshing behind the box", DispatchFocusScript);
            }
        });

        await Task.WhenAll(typing, refreshing);
    }

    private static Task SettleAsync(E2eHarness harness, Deadline deadline) =>
        deadline.BoundAsync("letting the page settle", () => harness.Page!.WaitForTimeoutAsync(SettleMs));

    private static Task ThrottleCpuAsync(Deadline deadline, ICDPSession cdp, double rate) =>
        deadline.BoundAsync($"setting CPU throttling to {rate}x", () => cdp.SendAsync("Emulation.setCPUThrottlingRate", new Dictionary<string, object> { ["rate"] = rate }));

    private static async Task UndoRepeatedlyAsync(E2eHarness harness, Deadline deadline, int times)
    {
        for (var i = 0; i < times; i++)
            await deadline.BoundAsync("pressing the undo shortcut", () => harness.Page!.Keyboard.PressAsync("ControlOrMeta+Z"));
    }

    /// <summary>One project and one column, written to the host's own database - enough for the
    /// New issue dialog to offer a project and the board to draw.</summary>
    private static async Task SeedProjectAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
        var now = DateTimeOffset.UtcNow;

        db.AddRange(
            new EfHatchProject { Key = "CUR", Name = "Caret", CreatedAt = now },
            new EfHatchStatus { Name = "todo", SortOrder = 10 });
        await db.SaveChangesAsync();
    }

    /// <summary>One project, one column and one empty-described issue, written to the host's own
    /// database. Returns the issue's key.</summary>
    private static async Task<string> SeedIssueAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
        var now = DateTimeOffset.UtcNow;

        var project = new EfHatchProject { Key = "CMT", Name = "Comment", CreatedAt = now };
        var status = new EfHatchStatus { Name = "todo", SortOrder = 10 };
        db.AddRange(project, status);
        await db.SaveChangesAsync();

        db.Issues.Add(new EfHatchIssue
        {
            ProjectId = project.Id,
            Number = 1,
            Type = "story",
            Title = "A comment box to type into",
            Description = "",
            StatusId = status.Id,
            Rank = 1024,
            CreatedBy = "operator",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        return $"{project.Key}-1";
    }
}

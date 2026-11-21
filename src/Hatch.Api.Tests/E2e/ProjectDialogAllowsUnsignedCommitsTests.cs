using System.Net.Http.Json;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2654's unsigned-commits setting, rendered: the project dialog's
/// checkbox as an owner sees and saves it, and its one sentence as a caller who
/// does not own the project reads it, in headless Chromium at 1280x800 against
/// the real API serving the built bundle. A project filed through the API is
/// owned by its filer, so the unowned ones are seeded straight into
/// <see cref="HatchContext"/> with no member row.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class ProjectDialogAllowsUnsignedCommitsTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string ProjectsPath = "/projects";

    private const string BoxLabel = "Allow unsigned commits";

    private const string ReadOutSelector = "p:text-matches(\"^Allows only signed commits\")";

    /// <summary>A neutral name for the seeded By: no person's name belongs in the code.</summary>
    private const string SetBy = "Seed Author";

    /// <summary>Runs against the dialog: how many checkboxes are labelled with the given text, and how many of those are ticked.</summary>
    private const string CheckboxesScript =
        "(dialog, text) => { const boxes = Array.from(dialog.querySelectorAll('label')).filter(l => l.textContent.trim() === text).map(l => l.querySelector('input[type=checkbox]')).filter(Boolean); return [boxes.length, boxes.filter(b => b.checked).length]; }";

    /// <summary>Runs against the read-out paragraph: its text as a person reads it.</summary>
    private const string TextScript = "(p) => p.textContent";

    /// <summary>Runs against the dialog: how many paragraphs mention signed commits at all, whichever way round.</summary>
    private const string SignedMentionsScript =
        "(dialog) => Array.from(dialog.querySelectorAll('p')).filter(p => /signed commits|sign their commits/.test(p.textContent)).length";

    /// <summary>Runs against the dialog: whether any <c>set by</c> hint paragraph is shown.</summary>
    private const string SetByCountScript =
        "(dialog) => Array.from(dialog.querySelectorAll('p.hatch-section-hint')).filter(p => p.textContent.trim().startsWith('set by')).length";

    /// <summary>Runs against an element inside the dialog's own scroll: brings it to the middle, so the screenshot shows what the assertions read.</summary>
    private const string ScrollIntoViewScript = "(el) => { el.scrollIntoView({ block: 'center' }); return true; }";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task ANonOwnersDialog_ReadsOutOnlyAProjectThatAllowsSignedCommitsOnly_WithNoCheckbox()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("ProjectDialogUnsignedNonOwner");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            await deadline.BoundAsync("seeding three projects nobody owns", () => SeedUnownedProjectsAsync(harness.Services));

            await harness.OpenAsync(deadline, baseUrl, ProjectsPath);

            var violations = new List<string>();

            // Signed only with a stamp reads as a sentence; allowing unsigned,
            // stamped or never written, reads as nothing at all.
            await ReadOutAsync(harness, deadline, "UCA", violations, expectStart: $"Allows only signed commits, set by {SetBy} on ");
            await ReadOutAsync(harness, deadline, "UCB", violations, expectStart: null);
            await ReadOutAsync(harness, deadline, "UCC", violations, expectStart: null);

            Assert.True(
                violations.Count == 0,
                $"The non-owner's dialog does not read out as asked ({violations.Count} violation(s)):{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");

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
    /// The owner's half: a project filed through the API makes the caller its
    /// owner, so the checkbox is there, ticked, with no <c>set by</c> line;
    /// unticked and saved through the dialog's own Save, a reopened dialog shows
    /// it unticked, and shows who set it.
    /// </summary>
    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheOwnersDialog_SavesTheUnsignedCommitsSetting_AndShowsItUntickedOnReopening()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("ProjectDialogUnsignedOwner");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            await deadline.BoundAsync("filing a project", () => FileOwnedProjectAsync(baseUrl, "OWN"));

            await harness.OpenAsync(deadline, baseUrl, ProjectsPath);

            var violations = new List<string>();

            var dialog = await OpenDialogAsync(harness, deadline, "OWN");
            var before = await OwnerStateAsync(harness, deadline, dialog, "unsigned-owner-ticked");
            output.WriteLine($"--- before: checkboxes {before.Checkboxes}, ticked {before.Ticked}, set-by lines {before.SetByLines}");
            if (before.Checkboxes != 1) violations.Add($"before: expected the checkbox once, found {before.Checkboxes}");
            if (before.Ticked != 1) violations.Add($"before: expected the checkbox ticked by default, found {before.Ticked} ticked");
            if (before.SetByLines != 0) violations.Add($"before: the setting was never written, but {before.SetByLines} set-by line(s) are shown");

            await harness.ClickAsync(deadline, $"{dialog} label:has(input[type=checkbox]):text-is(\"{BoxLabel}\") input[type=checkbox]");
            await harness.ClickAsync(deadline, $"{dialog} button:text-is(\"Save\")");
            await harness.WaitForHiddenAsync(deadline, dialog);

            dialog = await OpenDialogAsync(harness, deadline, "OWN");
            var after = await OwnerStateAsync(harness, deadline, dialog, "unsigned-owner-unticked");
            output.WriteLine($"--- after: checkboxes {after.Checkboxes}, ticked {after.Ticked}, set-by lines {after.SetByLines}");
            if (after.Checkboxes != 1) violations.Add($"after: expected the checkbox once, found {after.Checkboxes}");
            if (after.Ticked != 0) violations.Add($"after: expected the checkbox unticked after Save, found {after.Ticked} ticked");
            if (after.SetByLines == 0) violations.Add("after: expected a set-by line once the setting was written, found none");

            Assert.True(
                violations.Count == 0,
                $"The owner's dialog does not read out as asked ({violations.Count} violation(s)):{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");

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
    /// Opens Edit on <paramref name="key"/>'s row and checks what a non-owner
    /// reads: the sentence that starts <paramref name="expectStart"/>, or, when
    /// that is null, nothing at all about signed commits. Either way there is no
    /// checkbox. Saves the look, and closes the dialog.
    /// </summary>
    private async Task ReadOutAsync(
        E2eHarness harness, Deadline deadline, string key, List<string> violations, string? expectStart)
    {
        var dialog = await OpenDialogAsync(harness, deadline, key);

        var boxes = await harness.EvaluateOnAsync<int[]>(
            deadline, $"counting {key}'s checkboxes", dialog, CheckboxesScript, BoxLabel);
        var mentions = await harness.EvaluateOnAsync<int>(
            deadline, $"counting {key}'s mentions of signed commits", dialog, SignedMentionsScript);

        if (expectStart is null)
        {
            var path = await harness.ScreenshotAsync(deadline, $"unsigned-non-owner-{key}");
            output.WriteLine($"--- {key}: no read-out, checkboxes {boxes[0]}, mentions {mentions}, screenshot {path}");
            if (mentions != 0)
                violations.Add($"{key}: the project allows unsigned commits, but the dialog has {mentions} paragraph(s) about signed commits");
        }
        else
        {
            var readOut = await harness.EvaluateOnAsync<string>(
                deadline, $"reading {key}'s read-out", $"{dialog} {ReadOutSelector}", TextScript);
            await harness.EvaluateOnAsync<bool>(deadline, $"scrolling {key}'s read-out into view", $"{dialog} {ReadOutSelector}", ScrollIntoViewScript);
            var path = await harness.ScreenshotAsync(deadline, $"unsigned-non-owner-{key}");
            output.WriteLine($"--- {key}: \"{readOut}\", checkboxes {boxes[0]}, screenshot {path}");

            if (!readOut.StartsWith(expectStart, StringComparison.Ordinal))
                violations.Add($"{key}: expected the read-out to start \"{expectStart}\", it reads \"{readOut}\"");
            if (!System.Text.RegularExpressions.Regex.IsMatch(readOut, @" on .+\.$"))
                violations.Add($"{key}: the read-out has no date after \"on\", or does not end in a full stop: \"{readOut}\"");
        }

        if (boxes[0] != 0)
            violations.Add($"{key}: a non-owner has {boxes[0]} checkbox(es) labelled \"{BoxLabel}\", expected none");

        await CloseDialogAsync(harness, deadline, dialog);
    }

    /// <summary>The owner's open dialog: how many checkboxes carry the label, how many are ticked, and how many <c>set by</c> hints are shown. Takes the screenshot, and leaves the dialog open.</summary>
    private async Task<(int Checkboxes, int Ticked, int SetByLines)> OwnerStateAsync(
        E2eHarness harness, Deadline deadline, string dialog, string label)
    {
        var boxes = await harness.EvaluateOnAsync<int[]>(deadline, "counting the checkboxes", dialog, CheckboxesScript, BoxLabel);
        var setBy = await harness.EvaluateOnAsync<int>(deadline, "counting the set-by lines", dialog, SetByCountScript);
        await harness.EvaluateOnAsync<bool>(deadline, "scrolling the checkbox into view", $"{dialog} label:has(input[type=checkbox]):text-is(\"{BoxLabel}\")", ScrollIntoViewScript);
        var path = await harness.ScreenshotAsync(deadline, label);
        output.WriteLine($"--- {label}: screenshot {path}");
        return (boxes[0], boxes[1], setBy);
    }

    /// <summary>Clicks Edit in the row holding <c>&lt;code&gt;KEY&lt;/code&gt;</c> and waits for the dialog titled <c>Edit KEY</c>; returns its selector.</summary>
    private static async Task<string> OpenDialogAsync(E2eHarness harness, Deadline deadline, string key)
    {
        await harness.ClickAsync(deadline, $"tr:has(code:text-is(\"{key}\")) button:text-is(\"Edit\")");
        var dialog = $"[role=dialog]:has(h3:text-is(\"Edit {key}\"))";
        await harness.WaitForVisibleAsync(deadline, dialog);
        return dialog;
    }

    private static async Task CloseDialogAsync(E2eHarness harness, Deadline deadline, string dialog)
    {
        await harness.ClickAsync(deadline, $"{dialog} button[aria-label=\"Close\"]");
        await harness.WaitForHiddenAsync(deadline, dialog);
    }

    /// <summary>
    /// Three projects with no <c>ProjectMembers</c> row, so the caller owns none
    /// of them: <c>UCA</c> allows only signed commits, with who and when,
    /// <c>UCB</c> was turned off and on again, and <c>UCC</c> never wrote it.
    /// </summary>
    private static async Task SeedUnownedProjectsAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
        var now = DateTimeOffset.UtcNow;

        db.Projects.AddRange(
            new EfHatchProject
            {
                Key = "UCA",
                Name = "Signed only",
                CreatedAt = now,
                AllowsUnsignedCommits = false,
                AllowsUnsignedCommitsBy = SetBy,
                AllowsUnsignedCommitsAt = now,
            },
            new EfHatchProject
            {
                Key = "UCB",
                Name = "Unsigned allowed again",
                CreatedAt = now,
                AllowsUnsignedCommits = true,
                AllowsUnsignedCommitsBy = SetBy,
                AllowsUnsignedCommitsAt = now,
            },
            new EfHatchProject { Key = "UCC", Name = "Never written", CreatedAt = now });
        await db.SaveChangesAsync();
    }

    /// <summary>A project filed the way any caller files one with the wall off: the local person is who files it, which makes the local person its owner.</summary>
    private static async Task FileOwnedProjectAsync(string baseUrl, string key)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        using var response = await http.PostAsJsonAsync("/api/projects", new ProjectCreateRequest(key, $"Project {key}"));
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST /api/projects answered {(int)response.StatusCode}: {text}");
    }
}

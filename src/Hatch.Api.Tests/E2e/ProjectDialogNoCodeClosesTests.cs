using System.Net.Http.Json;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2071's no-code-closes setting, rendered: the project dialog's checkbox as
/// an owner sees and saves it, and its one sentence as a caller who does not
/// own the project reads it, in headless Chromium at 1280x800 against the real
/// API serving the built bundle. The same two callers
/// <see cref="ProjectDialogReadOutTests"/> sets up for the check-approves-merge
/// line, and for the same reason: a project filed through the API is owned by
/// its filer, so the unowned ones are seeded straight into
/// <see cref="HatchContext"/> with no member row.
///
/// <para>The setting needs no column, so nothing here waits for the statuses
/// to load.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class ProjectDialogNoCodeClosesTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string ProjectsPath = "/apps/hatch/projects";

    private const string CloseLabel = "A no-code declaration closes the ticket";

    private const string ReadOutSelector = "p:text-matches(\"^A no-code declaration\")";

    /// <summary>A neutral name for the seeded By: no person's name belongs in the code.</summary>
    private const string SetBy = "Seed Author";

    /// <summary>Runs against the dialog: how many checkboxes are labelled with the given text, and how many of those are ticked.</summary>
    private const string CheckboxesScript =
        "(dialog, text) => { const boxes = Array.from(dialog.querySelectorAll('label')).filter(l => l.textContent.trim() === text).map(l => l.querySelector('input[type=checkbox]')).filter(Boolean); return [boxes.length, boxes.filter(b => b.checked).length]; }";

    /// <summary>Runs against the read-out paragraph: its text as a person reads it.</summary>
    private const string TextScript = "(p) => p.textContent";

    /// <summary>Runs against the dialog: whether any <c>set by</c> hint paragraph is shown.</summary>
    private const string SetByCountScript =
        "(dialog) => Array.from(dialog.querySelectorAll('p.hatch-section-hint')).filter(p => p.textContent.trim().startsWith('set by')).length";

    /// <summary>Runs against an element inside the dialog's own scroll: brings it to the middle, so the screenshot shows what the assertions read.</summary>
    private const string ScrollIntoViewScript = "(el) => { el.scrollIntoView({ block: 'center' }); return true; }";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task ANonOwnersDialog_ReadsOutTheNoCodeClosesFlagAndWhoSetIt_WithNoCheckbox()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("ProjectDialogNoCodeNonOwner");
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

            // Flag on, flag off (it was turned off, so By and At are stamped),
            // and flag never written (nothing to say it was set).
            await ReadOutAsync(harness, deadline, "NCA", violations, expectStart: $"{CloseLabel}, set by {SetBy} on ");
            await ReadOutAsync(harness, deadline, "NCB", violations, expectStart: $"A no-code declaration does not close the ticket, set by {SetBy} on ");
            await ReadOutAsync(harness, deadline, "NCC", violations, expectStart: "A no-code declaration does not close the ticket", expectNoSetBy: true);

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
    /// owner, so the checkbox is there, unticked, with no <c>set by</c> line;
    /// ticked and saved through the dialog's own Save, a reopened dialog shows it
    /// ticked, and shows who set it.
    /// </summary>
    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheOwnersDialog_SavesTheNoCodeClosesFlag_AndShowsItTickedOnReopening()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("ProjectDialogNoCodeOwner");
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
            var before = await OwnerStateAsync(harness, deadline, dialog, "owner-unticked");
            output.WriteLine($"--- before: checkboxes {before.Checkboxes}, ticked {before.Ticked}, set-by lines {before.SetByLines}");
            if (before.Checkboxes != 1) violations.Add($"before: expected the checkbox once, found {before.Checkboxes}");
            if (before.Ticked != 0) violations.Add($"before: expected the checkbox unticked, found {before.Ticked} ticked");
            if (before.SetByLines != 0) violations.Add($"before: the flag was never written, but {before.SetByLines} set-by line(s) are shown");

            await harness.ClickAsync(deadline, $"{dialog} label:has(input[type=checkbox]):text-is(\"{CloseLabel}\") input[type=checkbox]");
            await harness.ClickAsync(deadline, $"{dialog} button:text-is(\"Save\")");
            await harness.WaitForHiddenAsync(deadline, dialog);

            dialog = await OpenDialogAsync(harness, deadline, "OWN");
            var after = await OwnerStateAsync(harness, deadline, dialog, "owner-ticked");
            output.WriteLine($"--- after: checkboxes {after.Checkboxes}, ticked {after.Ticked}, set-by lines {after.SetByLines}");
            if (after.Checkboxes != 1) violations.Add($"after: expected the checkbox once, found {after.Checkboxes}");
            if (after.Ticked != 1) violations.Add($"after: expected the checkbox ticked after Save, found {after.Ticked} ticked");
            if (after.SetByLines == 0) violations.Add("after: expected a set-by line once the flag was written, found none");

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
    /// Opens Edit on <paramref name="key"/>'s row, reads the line that starts
    /// "A no-code declaration", checks it and the absence of the checkbox, saves
    /// the look, and closes the dialog.
    /// </summary>
    private async Task ReadOutAsync(
        E2eHarness harness, Deadline deadline, string key, List<string> violations, string expectStart, bool expectNoSetBy = false)
    {
        var dialog = await OpenDialogAsync(harness, deadline, key);

        var readOut = await harness.EvaluateOnAsync<string>(
            deadline, $"reading {key}'s read-out", $"{dialog} {ReadOutSelector}", TextScript);
        var boxes = await harness.EvaluateOnAsync<int[]>(
            deadline, $"counting {key}'s checkboxes", dialog, CheckboxesScript, CloseLabel);
        await harness.EvaluateOnAsync<bool>(deadline, $"scrolling {key}'s read-out into view", $"{dialog} {ReadOutSelector}", ScrollIntoViewScript);
        var path = await harness.ScreenshotAsync(deadline, $"no-code-non-owner-{key}");
        output.WriteLine($"--- {key}: \"{readOut}\", checkboxes {boxes[0]}, screenshot {path}");

        if (!readOut.StartsWith(expectStart, StringComparison.Ordinal))
            violations.Add($"{key}: expected the read-out to start \"{expectStart}\", it reads \"{readOut}\"");
        if (!readOut.EndsWith('.'))
            violations.Add($"{key}: the read-out does not end in a full stop: \"{readOut}\"");
        if (expectNoSetBy)
        {
            if (readOut.Contains(", set by", StringComparison.Ordinal))
                violations.Add($"{key}: the flag was never written, but the read-out names who set it: \"{readOut}\"");
        }
        else if (!System.Text.RegularExpressions.Regex.IsMatch(readOut, @" on .+\.$"))
        {
            violations.Add($"{key}: the read-out has no date after \"on\": \"{readOut}\"");
        }
        if (boxes[0] != 0)
            violations.Add($"{key}: a non-owner has {boxes[0]} checkbox(es) labelled \"{CloseLabel}\", expected none");

        await CloseDialogAsync(harness, deadline, dialog);
    }

    /// <summary>The owner's open dialog: how many checkboxes carry the label, how many are ticked, and how many <c>set by</c> hints are shown. Takes the screenshot, and leaves the dialog open.</summary>
    private async Task<(int Checkboxes, int Ticked, int SetByLines)> OwnerStateAsync(
        E2eHarness harness, Deadline deadline, string dialog, string label)
    {
        var boxes = await harness.EvaluateOnAsync<int[]>(deadline, "counting the checkboxes", dialog, CheckboxesScript, CloseLabel);
        var setBy = await harness.EvaluateOnAsync<int>(deadline, "counting the set-by lines", dialog, SetByCountScript);
        await harness.EvaluateOnAsync<bool>(deadline, "scrolling the checkbox into view", $"{dialog} label:has(input[type=checkbox]):text-is(\"{CloseLabel}\")", ScrollIntoViewScript);
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
    /// of them: <c>NCA</c> turned the flag on, <c>NCB</c> turned it on and off
    /// again, <c>NCC</c> never wrote it.
    /// </summary>
    private static async Task SeedUnownedProjectsAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
        var now = DateTimeOffset.UtcNow;

        db.Projects.AddRange(
            new EfHatchProject
            {
                Key = "NCA",
                Name = "Flag on",
                CreatedAt = now,
                NoCodeCloses = true,
                NoCodeClosesBy = SetBy,
                NoCodeClosesAt = now,
            },
            new EfHatchProject
            {
                Key = "NCB",
                Name = "Flag off",
                CreatedAt = now,
                NoCodeCloses = false,
                NoCodeClosesBy = SetBy,
                NoCodeClosesAt = now,
            },
            new EfHatchProject { Key = "NCC", Name = "Flag never written", CreatedAt = now });
        await db.SaveChangesAsync();
    }

    /// <summary>A project filed the way any caller files one with the wall off: the local person is who files it, which makes the local person its owner.</summary>
    private static async Task FileOwnedProjectAsync(string baseUrl, string key)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        using var response = await http.PostAsJsonAsync("/api/hatch/projects", new ProjectCreateRequest(key, $"Project {key}"));
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST /api/hatch/projects answered {(int)response.StatusCode}: {text}");
    }
}

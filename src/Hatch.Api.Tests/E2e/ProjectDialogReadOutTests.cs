using System.Net.Http.Json;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-1589's one unmet criterion, rendered: the project dialog's
/// check-approves-merge line as a caller who does not own the project sees
/// it, in headless Chromium at 1280x800 against the real API serving the
/// built bundle.
///
/// <para>A non-owner is <c>project.canAdminister === false</c>, true only for
/// a caller with an Owner row in <c>ProjectMembers</c>. Filing a project
/// through <c>POST /api/projects</c> makes the caller its owner, and the
/// last owner cannot be removed, so the API cannot make one: the rows are
/// seeded straight into <see cref="HatchContext"/> with no member row at all.
/// Such a row also draws a Claim button beside Edit; this case ignores it.</para>
///
/// <para>The checkbox and both hints exist only in the owner branch of the
/// dialog, so the second hint - <c>No column on the board is both Protected and
/// Merges PR</c> - is checked by <see cref="TheOwnersDialog_SaysSoOnlyWhileNoColumnIsBothProtectedAndMergesPr"/>
/// on a project filed through the API. Its absence proves nothing until the
/// statuses have loaded, so the page is held until the fetch has answered and
/// two frames have been drawn after it.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class ProjectDialogReadOutTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string ProjectsPath = "/projects";

    private const string CheckLabel = "A passing check approves the merge";

    private const string SecondHint = "No column on the board is both Protected and Merges PR";

    /// <summary>A neutral name for the seeded By: no person's name belongs in the code.</summary>
    private const string SetBy = "Seed Author";

    /// <summary>Runs against the dialog: how many checkboxes are labelled with the check-approves-merge text.</summary>
    private const string CheckboxCountScript =
        "(dialog, text) => Array.from(dialog.querySelectorAll('label')).filter(l => l.textContent.trim() === text && l.querySelector('input[type=checkbox]')).length";

    /// <summary>Runs against the dialog: whether any text in it contains the given sentence.</summary>
    private const string ContainsTextScript = "(dialog, text) => dialog.textContent.includes(text)";

    /// <summary>Runs against the read-out paragraph: its text as a person reads it.</summary>
    private const string TextScript = "(p) => p.textContent";

    /// <summary>Runs against an element inside the dialog's own scroll: brings it to the middle, so the screenshot shows what the assertions read.</summary>
    private const string ScrollIntoViewScript = "(el) => { el.scrollIntoView({ block: 'center' }); return true; }";

    private const string StatusesLoadedScript =
        "() => performance.getEntriesByType('resource').some(e => e.name.includes('/api/statuses'))";

    private const string TwoFramesScript =
        "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve(true))))";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task ANonOwnersDialog_ReadsOutTheCheckApprovesMergeFlagAndWhoSetIt_WithNoCheckbox()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("ProjectDialogNonOwner");
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
            await ReadOutAsync(harness, deadline, "NOA", violations, expectStart: $"{CheckLabel}, set by {SetBy} on ");
            await ReadOutAsync(harness, deadline, "NOB", violations, expectStart: $"A passing check does not approve the merge, set by {SetBy} on ");
            await ReadOutAsync(harness, deadline, "NOC", violations, expectStart: "A passing check does not approve the merge", expectNoSetBy: true);

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
    /// owner, so the checkbox is there, and the board seeded with a column that
    /// is Protected but does not merge shows the second hint until a column
    /// that is both exists.
    /// </summary>
    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheOwnersDialog_SaysSoOnlyWhileNoColumnIsBothProtectedAndMergesPr()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("ProjectDialogOwner");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            await deadline.BoundAsync("filing a project and seeding a board with no merge column", async () =>
            {
                await FileOwnedProjectAsync(baseUrl, "OWN");
                await SeedStatusesAsync(harness.Services, mergeColumn: false);
            });

            await harness.OpenAsync(deadline, baseUrl, ProjectsPath);

            var violations = new List<string>();

            var before = await OwnerDialogAsync(harness, deadline, "OWN", "owner-no-merge-column");
            output.WriteLine($"--- no merge column: checkboxes {before.Checkboxes}, second hint {before.SecondHint}");
            if (before.Checkboxes != 1) violations.Add($"no merge column: expected the checkbox once, found {before.Checkboxes}");
            if (!before.SecondHint) violations.Add("no merge column: the second hint is absent, expected it to be shown");

            await deadline.BoundAsync("adding a Protected, Merges PR column", () => SeedStatusesAsync(harness.Services, mergeColumn: true));
            await harness.GoToAsync(deadline, "projects");

            var after = await OwnerDialogAsync(harness, deadline, "OWN", "owner-merge-column");
            output.WriteLine($"--- merge column: checkboxes {after.Checkboxes}, second hint {after.SecondHint}");
            if (after.Checkboxes != 1) violations.Add($"merge column: expected the checkbox once, found {after.Checkboxes}");
            if (after.SecondHint) violations.Add("merge column: the second hint is still shown, expected it gone");

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
    /// "A passing check", checks it and the absence of the checkbox, saves the
    /// look, and closes the dialog.
    /// </summary>
    private async Task ReadOutAsync(
        E2eHarness harness, Deadline deadline, string key, List<string> violations, string expectStart, bool expectNoSetBy = false)
    {
        var dialog = await OpenDialogAsync(harness, deadline, key);

        var readOut = await harness.EvaluateOnAsync<string>(
            deadline, $"reading {key}'s read-out", $"{dialog} p:text-matches(\"^A passing check\")", TextScript);
        var checkboxes = await harness.EvaluateOnAsync<int>(
            deadline, $"counting {key}'s checkboxes", dialog, CheckboxCountScript, CheckLabel);
        await harness.EvaluateOnAsync<bool>(deadline, $"scrolling {key}'s read-out into view", $"{dialog} p:text-matches(\"^A passing check\")", ScrollIntoViewScript);
        var path = await harness.ScreenshotAsync(deadline, $"non-owner-{key}");
        output.WriteLine($"--- {key}: \"{readOut}\", checkboxes {checkboxes}, screenshot {path}");

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
        if (checkboxes != 0)
            violations.Add($"{key}: a non-owner has {checkboxes} checkbox(es) labelled \"{CheckLabel}\", expected none");

        await CloseDialogAsync(harness, deadline, dialog);
    }

    /// <summary>The owner's dialog on <paramref name="key"/>, once the statuses have loaded: how many checkboxes it has, and whether the second hint is shown.</summary>
    private async Task<(int Checkboxes, bool SecondHint)> OwnerDialogAsync(E2eHarness harness, Deadline deadline, string key, string label)
    {
        var dialog = await OpenDialogAsync(harness, deadline, key);

        await harness.WaitForFunctionAsync(deadline, "waiting for the statuses fetch to have answered", StatusesLoadedScript);
        await harness.EvaluateAsync<bool>(deadline, "waiting two frames after the statuses fetch", TwoFramesScript);

        var checkboxes = await harness.EvaluateOnAsync<int>(deadline, "counting the checkboxes", dialog, CheckboxCountScript, CheckLabel);
        var secondHint = await harness.EvaluateOnAsync<bool>(deadline, "looking for the second hint", dialog, ContainsTextScript, SecondHint);
        await harness.EvaluateOnAsync<bool>(deadline, "scrolling the checkbox into view", $"{dialog} label:has(input[type=checkbox]):text-is(\"{CheckLabel}\")", ScrollIntoViewScript);
        var path = await harness.ScreenshotAsync(deadline, label);
        output.WriteLine($"--- {label}: screenshot {path}");

        await CloseDialogAsync(harness, deadline, dialog);
        return (checkboxes, secondHint);
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
    /// of them: <c>NOA</c> turned the flag on, <c>NOB</c> turned it on and off
    /// again, <c>NOC</c> never wrote it.
    /// </summary>
    private static async Task SeedUnownedProjectsAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
        var now = DateTimeOffset.UtcNow;

        db.Projects.AddRange(
            new EfHatchProject
            {
                Key = "NOA",
                Name = "Flag on",
                CreatedAt = now,
                CheckApprovesMerge = true,
                CheckApprovesMergeBy = SetBy,
                CheckApprovesMergeAt = now,
            },
            new EfHatchProject
            {
                Key = "NOB",
                Name = "Flag off",
                CreatedAt = now,
                CheckApprovesMerge = false,
                CheckApprovesMergeBy = SetBy,
                CheckApprovesMergeAt = now,
            },
            new EfHatchProject { Key = "NOC", Name = "Flag never written", CreatedAt = now });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// A column that is neither terminal nor deferred, and - once
    /// <paramref name="mergeColumn"/> - a second that is Protected and Merges PR.
    /// The first call seeds only the plain column, so the board starts without a
    /// merge column whatever the migration left behind.
    /// </summary>
    private static async Task SeedStatusesAsync(IServiceProvider services, bool mergeColumn)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();

        if (!mergeColumn)
            db.Statuses.Add(new EfHatchStatus { Name = "todo", SortOrder = 10 });
        else
            db.Statuses.Add(new EfHatchStatus { Name = "merges pr", SortOrder = 20, IsProtected = true, MergesPullRequest = true });
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

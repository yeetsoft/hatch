using System.Net.Http.Json;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2111's one-pull-request policy, rendered: the project dialog's two radios
/// as an owner sees and saves them, and the same policy worked into one sentence
/// as a caller who does not own the project reads it, in headless Chromium at
/// 1280x800 against the real API serving the built bundle. The same two callers
/// <see cref="ProjectDialogNoCodeClosesTests"/> sets up, and for the same
/// reason: a project filed through the API is owned by its filer, so the unowned
/// ones are seeded straight into <see cref="HatchContext"/> with no member row.
///
/// <para>The policy needs no column, so nothing here waits for the statuses to
/// load.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class ProjectDialogOnePrPolicyTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string ProjectsPath = "/projects";

    private const string OptionalLabel = "Stories may set their own, either way";

    private const string EnforcedLabel = "Every story follows the project";

    private const string ReadOutSelector = "p:text-matches(\"^(By pull request|On the trunk), checks\")";

    private const string SetByParagraphSelector = "p:text-matches(\"^The one-pull-request policy\")";

    /// <summary>A neutral name for the seeded By: no person's name belongs in the code.</summary>
    private const string SetBy = "Seed Author";

    /// <summary>Runs against the dialog: how many radios sit in its choice fieldsets - the Landing pair and the policy pair. The icon picker's own radios are not a choice this case is about.</summary>
    private const string RadiosScript = "(dialog) => dialog.querySelectorAll('fieldset.hatch-landing-choice input[type=radio]').length";

    /// <summary>Runs against the dialog: how many radios are labelled with the given text, and how many of those are checked.</summary>
    private const string LabelledRadiosScript =
        "(dialog, text) => { const radios = Array.from(dialog.querySelectorAll('label')).filter(l => l.textContent.trim() === text).map(l => l.querySelector('input[type=radio]')).filter(Boolean); return [radios.length, radios.filter(r => r.checked).length]; }";

    /// <summary>Runs against the read-out paragraph: its text as a person reads it.</summary>
    private const string TextScript = "(p) => p.textContent";

    /// <summary>Runs against the dialog: whether any <c>set by</c> hint paragraph is shown.</summary>
    private const string SetByCountScript =
        "(dialog) => Array.from(dialog.querySelectorAll('p.hatch-section-hint')).filter(p => p.textContent.trim().startsWith('set by')).length";

    /// <summary>Runs against an element inside the dialog's own scroll: brings it to the middle, so the screenshot shows what the assertions read.</summary>
    private const string ScrollIntoViewScript = "(el) => { el.scrollIntoView({ block: 'center' }); return true; }";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task ANonOwnersDialog_ReadsOutTheOnePrPolicy_WithNoRadios()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("ProjectDialogOnePrPolicyNonOwner");
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

            // Enforced and set, optional and set, and never written.
            await ReadOutAsync(harness, deadline, "OPA", violations, expectContains: ", which every story follows", expectSetBy: true);
            await ReadOutAsync(harness, deadline, "OPB", violations, expectContains: ", which a story may override", expectSetBy: true);
            await ReadOutAsync(harness, deadline, "OPC", violations, expectContains: ", which a story may override", expectSetBy: false);

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
    /// owner, so the two radios are there, optional checked, with no <c>set by</c>
    /// line; enforced and saved through the dialog's own Save, a reopened dialog
    /// shows it checked, and shows who set it; and a later Save that changes only
    /// the name sends no write to the policy.
    /// </summary>
    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheOwnersDialog_SavesTheOnePrPolicy_AndSendsNoPolicyWriteWhenItIsLeftAlone()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("ProjectDialogOnePrPolicyOwner");
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
            var before = await OwnerStateAsync(harness, deadline, dialog, "owner-optional");
            output.WriteLine($"--- before: {before}");
            if (before.Radios != 4) violations.Add($"before: expected four radios in the choice fieldsets (Landing's two, the policy's two), found {before.Radios}");
            if (before.Optional != (1, 1)) violations.Add($"before: expected \"{OptionalLabel}\" once and checked, found {before.Optional}");
            if (before.Enforced != (1, 0)) violations.Add($"before: expected \"{EnforcedLabel}\" once and unchecked, found {before.Enforced}");
            if (before.SetByLines != 0) violations.Add($"before: the policy was never written, but {before.SetByLines} set-by line(s) are shown");

            await harness.ClickAsync(deadline, $"{dialog} label:has(input[type=radio]):text-is(\"{EnforcedLabel}\") input[type=radio]");
            await harness.ClickAsync(deadline, $"{dialog} button:text-is(\"Save\")");
            await harness.WaitForHiddenAsync(deadline, dialog);

            dialog = await OpenDialogAsync(harness, deadline, "OWN");
            var after = await OwnerStateAsync(harness, deadline, dialog, "owner-enforced");
            output.WriteLine($"--- after: {after}");
            if (after.Radios != 4) violations.Add($"after: expected four radios in the choice fieldsets (Landing's two, the policy's two), found {after.Radios}");
            if (after.Enforced != (1, 1)) violations.Add($"after: expected \"{EnforcedLabel}\" once and checked after Save, found {after.Enforced}");
            if (after.Optional != (1, 0)) violations.Add($"after: expected \"{OptionalLabel}\" once and unchecked after Save, found {after.Optional}");
            if (after.SetByLines != 1) violations.Add($"after: expected one set-by line once the policy was written, found {after.SetByLines}");

            // Change only the name: the policy is untouched, so Save must write the
            // project and must not write the policy. The set-by text cannot show
            // that - the API stamps it only when the value differs.
            var puts = new List<string>();
            var patches = new List<string>();
            harness.Page!.Request += (_, request) =>
            {
                if (request.Method == "PUT" && request.Url.EndsWith("/one-pr-policy", StringComparison.Ordinal)) puts.Add(request.Url);
                if (request.Method == "PATCH") patches.Add(request.Url);
            };

            await deadline.BoundAsync("renaming the project", () => harness.Page!.Locator($"{dialog} label:has-text(\"Name\") input").First.FillAsync("Renamed"));
            await harness.ClickAsync(deadline, $"{dialog} button:text-is(\"Save\")");
            await harness.WaitForHiddenAsync(deadline, dialog);
            output.WriteLine($"--- second save: {patches.Count} PATCH, {puts.Count} policy PUT");

            if (patches.Count == 0) violations.Add("second save: no PATCH was sent, so the save did nothing and the check proves nothing");
            if (puts.Count != 0) violations.Add($"second save: only the name changed, but {puts.Count} policy PUT(s) were sent");

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
    /// Opens Edit on <paramref name="key"/>'s row, reads the sentence that starts
    /// "By pull request" or "On the trunk", checks it, the set-by paragraph and the
    /// absence of any radio, saves the look, and closes the dialog.
    /// </summary>
    private async Task ReadOutAsync(
        E2eHarness harness, Deadline deadline, string key, List<string> violations, string expectContains, bool expectSetBy)
    {
        var dialog = await OpenDialogAsync(harness, deadline, key);

        var readOut = await harness.EvaluateOnAsync<string>(
            deadline, $"reading {key}'s read-out", $"{dialog} {ReadOutSelector}", TextScript);
        var setByLines = await deadline.BoundAsync(
            $"counting {key}'s set-by paragraphs", () => harness.Page!.Locator($"{dialog} {SetByParagraphSelector}").CountAsync());
        var setBy = setByLines == 0
            ? ""
            : await harness.EvaluateOnAsync<string>(deadline, $"reading {key}'s set-by paragraph", $"{dialog} {SetByParagraphSelector}", TextScript);
        var radios = await harness.EvaluateOnAsync<int>(deadline, $"counting {key}'s radios", dialog, RadiosScript);
        await harness.EvaluateOnAsync<bool>(deadline, $"scrolling {key}'s read-out into view", $"{dialog} {ReadOutSelector}", ScrollIntoViewScript);
        var path = await harness.ScreenshotAsync(deadline, $"one-pr-policy-non-owner-{key}");
        output.WriteLine($"--- {key}: \"{readOut}\", set-by \"{setBy}\", radios {radios}, screenshot {path}");

        if (!readOut.Contains(expectContains, StringComparison.Ordinal))
            violations.Add($"{key}: expected the read-out to contain \"{expectContains}\", it reads \"{readOut}\"");
        if (expectSetBy)
        {
            var expectStart = $"The one-pull-request policy was set by {SetBy} on ";
            if (setByLines != 1)
                violations.Add($"{key}: expected one set-by paragraph, found {setByLines}");
            else if (!setBy.StartsWith(expectStart, StringComparison.Ordinal) || !setBy.EndsWith('.'))
                violations.Add($"{key}: expected the set-by paragraph to start \"{expectStart}\" and end in a full stop, it reads \"{setBy}\"");
        }
        else if (setByLines != 0)
        {
            violations.Add($"{key}: the policy was never written, but {setByLines} set-by paragraph(s) are shown: \"{setBy}\"");
        }
        if (radios != 0)
            violations.Add($"{key}: a non-owner has {radios} radio(s) in the dialog's choice fieldsets, expected none");

        await CloseDialogAsync(harness, deadline, dialog);
    }

    private sealed record OwnerState(int Radios, (int Count, int Checked) Optional, (int Count, int Checked) Enforced, int SetByLines);

    /// <summary>The owner's open dialog: every radio, the two policy radios and how many are checked, and how many <c>set by</c> hints are shown. Takes the screenshot, and leaves the dialog open.</summary>
    private async Task<OwnerState> OwnerStateAsync(E2eHarness harness, Deadline deadline, string dialog, string label)
    {
        var radios = await harness.EvaluateOnAsync<int>(deadline, "counting the radios", dialog, RadiosScript);
        var optional = await harness.EvaluateOnAsync<int[]>(deadline, "counting the optional radio", dialog, LabelledRadiosScript, OptionalLabel);
        var enforced = await harness.EvaluateOnAsync<int[]>(deadline, "counting the enforced radio", dialog, LabelledRadiosScript, EnforcedLabel);
        var setBy = await harness.EvaluateOnAsync<int>(deadline, "counting the set-by lines", dialog, SetByCountScript);
        await harness.EvaluateOnAsync<bool>(deadline, "scrolling the policy radios into view", $"{dialog} label:has(input[type=radio]):text-is(\"{OptionalLabel}\")", ScrollIntoViewScript);
        var path = await harness.ScreenshotAsync(deadline, label);
        output.WriteLine($"--- {label}: screenshot {path}");
        return new OwnerState(radios, (optional[0], optional[1]), (enforced[0], enforced[1]), setBy);
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
    /// of them: <c>OPA</c> enforced the policy, <c>OPB</c> set it optional, <c>OPC</c>
    /// never wrote it.
    /// </summary>
    private static async Task SeedUnownedProjectsAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
        var now = DateTimeOffset.UtcNow;

        db.Projects.AddRange(
            new EfHatchProject
            {
                Key = "OPA",
                Name = "Policy enforced",
                CreatedAt = now,
                OnePullRequestPolicy = OnePullRequestPolicies.Enforced,
                OnePullRequestPolicyBy = SetBy,
                OnePullRequestPolicyAt = now,
            },
            new EfHatchProject
            {
                Key = "OPB",
                Name = "Policy optional",
                CreatedAt = now,
                OnePullRequestPolicy = OnePullRequestPolicies.Optional,
                OnePullRequestPolicyBy = SetBy,
                OnePullRequestPolicyAt = now,
            },
            new EfHatchProject { Key = "OPC", Name = "Policy never written", CreatedAt = now });
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

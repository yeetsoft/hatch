using System.Net.Http.Json;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2362's changelog election, rendered: the project dialog's Repositories list
/// as an owner ticks and saves it, and as a caller who does not own the project
/// reads it, in headless Chromium at 1280x800 against the real API serving the
/// built bundle. A project filed through the API is owned by its filer, so the
/// unowned one is seeded straight into <see cref="HatchContext"/> with no member
/// row, as <see cref="ProjectDialogOnePrPolicyTests"/> does.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class ProjectDialogChangelogTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string ProjectsPath = "/projects";

    private const string FirstRemote = "https://example.com/owner/one.git";

    private const string SecondRemote = "https://example.com/owner/two.git";

    private const string BoxLabel = "Writes a changelog";

    /// <summary>
    /// Runs against the dialog: one entry per bound remote, in order -
    /// <c>box:true</c> or <c>box:false</c> for a checkbox and whether it is ticked,
    /// <c>sentence</c> for the read-only line, <c>none</c> for neither.
    /// </summary>
    private const string RowsScript =
        """
        (dialog) => Array.from(dialog.querySelectorAll('.hatch-inline-form'))
          .filter(row => row.querySelector('code'))
          .map(row => {
            const box = Array.from(row.querySelectorAll('label')).find(l => l.textContent.trim() === 'Writes a changelog');
            if (box) return 'box:' + box.querySelector('input[type=checkbox]').checked;
            const sentence = Array.from(row.querySelectorAll('span')).some(s => s.textContent.trim() === 'writes a changelog');
            return sentence ? 'sentence' : 'none';
          })
        """;

    /// <summary>Runs against an element inside the dialog's own scroll: brings it to the middle, so the screenshot shows what the assertions read.</summary>
    private const string ScrollIntoViewScript = "(el) => { el.scrollIntoView({ block: 'center' }); return true; }";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheOwnersDialog_TicksAndSavesAChangelog_AndKeepsItOnReopening()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("ProjectDialogChangelogOwner");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            await deadline.BoundAsync("filing a project with two remotes", () => FileOwnedProjectAsync(baseUrl, "CHG"));

            await harness.OpenAsync(deadline, baseUrl, ProjectsPath);

            var violations = new List<string>();

            var dialog = await OpenDialogAsync(harness, deadline, "CHG");
            var before = await RowsAsync(harness, deadline, dialog, "owner-before");
            if (!before.SequenceEqual(["box:false", "box:false"]))
                violations.Add($"before: expected an unticked box beside each remote, found [{string.Join(", ", before)}]");

            await harness.ClickAsync(deadline, $"{dialog} .hatch-inline-form:has(code:text-is(\"{SecondRemote}\")) label:text-is(\"{BoxLabel}\") input[type=checkbox]");
            await harness.ClickAsync(deadline, $"{dialog} button:text-is(\"Save\")");
            await harness.WaitForHiddenAsync(deadline, dialog);

            dialog = await OpenDialogAsync(harness, deadline, "CHG");
            var after = await RowsAsync(harness, deadline, dialog, "owner-after");
            if (!after.SequenceEqual(["box:false", "box:true"]))
                violations.Add($"after: expected the second remote's box alone ticked after Save, found [{string.Join(", ", after)}]");

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

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task ANonOwnersDialog_SaysWhichRemoteWritesAChangelog_WithNoBox()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("ProjectDialogChangelogNonOwner");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            await deadline.BoundAsync("seeding a project nobody owns", () => SeedUnownedProjectAsync(harness.Services));

            await harness.OpenAsync(deadline, baseUrl, ProjectsPath);

            var dialog = await OpenDialogAsync(harness, deadline, "NOC");
            var rows = await RowsAsync(harness, deadline, dialog, "non-owner");

            Assert.True(
                rows.SequenceEqual(["sentence", "none"]),
                $"expected the sentence beside the elected remote and nothing beside the other, found [{string.Join(", ", rows)}]");

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

    /// <summary>Reads each remote's row, takes the screenshot with the list in view, and leaves the dialog open.</summary>
    private async Task<string[]> RowsAsync(E2eHarness harness, Deadline deadline, string dialog, string label)
    {
        var rows = await harness.EvaluateOnAsync<string[]>(deadline, "reading the repository rows", dialog, RowsScript);
        await harness.EvaluateOnAsync<bool>(deadline, "scrolling the repositories into view", $"{dialog} .hatch-inline-form:has(code)", ScrollIntoViewScript);
        var path = await harness.ScreenshotAsync(deadline, $"changelog-{label}");
        output.WriteLine($"--- {label}: [{string.Join(", ", rows)}], screenshot {path}");
        return rows;
    }

    /// <summary>Clicks Edit in the row holding <c>&lt;code&gt;KEY&lt;/code&gt;</c> and waits for the dialog titled <c>Edit KEY</c>; returns its selector.</summary>
    private static async Task<string> OpenDialogAsync(E2eHarness harness, Deadline deadline, string key)
    {
        await harness.ClickAsync(deadline, $"tr:has(code:text-is(\"{key}\")) button:text-is(\"Edit\")");
        var dialog = $"[role=dialog]:has(h3:text-is(\"Edit {key}\"))";
        await harness.WaitForVisibleAsync(deadline, dialog);
        return dialog;
    }

    /// <summary>A project filed the way any caller files one with the wall off - which makes the local person its owner - and two remotes bound to it.</summary>
    private static async Task FileOwnedProjectAsync(string baseUrl, string key)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        using var created = await http.PostAsJsonAsync("/api/projects", new ProjectCreateRequest(key, $"Project {key}"));
        var text = await created.Content.ReadAsStringAsync();
        Assert.True(created.IsSuccessStatusCode, $"POST /api/projects answered {(int)created.StatusCode}: {text}");
        var project = await created.Content.ReadFromJsonAsync<ProjectDto>();

        using var bound = await http.PutAsJsonAsync(
            $"/api/projects/{project!.Id}/repositories",
            new[] { new ProjectRepositoryWriteRequest(FirstRemote, null), new ProjectRepositoryWriteRequest(SecondRemote, null) });
        var boundText = await bound.Content.ReadAsStringAsync();
        Assert.True(bound.IsSuccessStatusCode, $"PUT repositories answered {(int)bound.StatusCode}: {boundText}");
    }

    /// <summary>One project with no <c>ProjectMembers</c> row, so the caller owns nothing: the first remote elects a changelog, the second does not.</summary>
    private static async Task SeedUnownedProjectAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
        var now = DateTimeOffset.UtcNow;

        var project = new EfHatchProject { Key = "NOC", Name = "Not mine", CreatedAt = now };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        db.ProjectRepositories.AddRange(
            new EfHatchProjectRepository
            {
                ProjectId = project.Id, Remote = FirstRemote, Canonical = "example.com/owner/one", Changelog = true, SortOrder = 0, CreatedAt = now,
            },
            new EfHatchProjectRepository
            {
                ProjectId = project.Id, Remote = SecondRemote, Canonical = "example.com/owner/two", SortOrder = 1, CreatedAt = now,
            });
        await db.SaveChangesAsync();
    }
}

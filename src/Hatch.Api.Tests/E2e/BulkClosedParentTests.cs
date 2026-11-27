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
/// HA-2837: Bulk edit's Parent filter, Ancestor filter and Parent edit find an
/// issue closed long ago by title words, Find lists what hangs under it, and
/// Apply makes it the parent of every ticked issue. In a scoped tab none of the
/// three offers an issue of a project outside the scope.
///
/// <para>Seeded: project CLO with an open task, a closed epic (closed 30 days
/// ago, so off the board under its default horizon) and an open task filed
/// under that epic; and project OTH with a closed epic sharing the two title
/// words.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class BulkClosedParentTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 150;
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private const string TitleWords = "zenith horizon";

    private const string PickerSearch = ".hatch-issue-picker-popup .hatch-issue-picker-search";
    private const string ParentFilterSelect = "select[aria-label=\"Parent filter\"]";
    private const string ParentChangeSelect = "select[aria-label=\"Parent change\"]";

    private static string Button(string label) => $"button[role=combobox][aria-label=\"{label}\"]";
    private static string Options(string label) => $"[role=listbox][aria-label=\"{label}\"] [role=option]";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task Desktop_FindsAClosedParent() => RunAsync("desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task Phone_FindsAClosedParent()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("phone", DeviceProfile.IPhone14Pro);
    }

    private async Task RunAsync(string where, DeviceProfile? profile)
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness($"BulkClosedParent-{where}");
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

            // Unscoped: every project is searched, so both closed epics turn up.
            await harness.OpenAsync(deadline, baseUrl, "/bulk");
            await harness.WaitForVisibleAsync(deadline, ParentFilterSelect);
            var page = harness.Page!;

            // Parent filter: choose "An issue", find the closed epic, Find lists its child.
            await harness.SelectOptionAsync(deadline, ParentFilterSelect, "issue");
            var found = await FindAsync(harness, deadline, "Parent filter issue", "CLO-2");
            Assert.Contains("OTH-1", found);
            output.WriteLine("unscoped parent filter options: " + found);
            output.WriteLine("bulk-parent-filter: " + await harness.ScreenshotAsync(deadline, "bulk-parent-filter"));
            await TakeAsync(harness, deadline, "Parent filter issue", "CLO-2");
            await FindIssuesAsync(harness, deadline);
            var rows = await ResultRowsAsync(harness, deadline, "CLO-3");
            Assert.DoesNotContain("CLO-1", rows);

            // Ancestor filter, with the parent filter put back to "any".
            await harness.SelectOptionAsync(deadline, ParentFilterSelect, "keep");
            var ancestor = await FindAsync(harness, deadline, "Ancestor", "CLO-2");
            Assert.Contains("OTH-1", ancestor);
            await TakeAsync(harness, deadline, "Ancestor", "CLO-2");
            await FindIssuesAsync(harness, deadline);
            rows = await ResultRowsAsync(harness, deadline, "CLO-3");
            Assert.DoesNotContain("CLO-1", rows);

            // The Ancestor filter keeps its "any" row.
            await harness.ClickAsync(deadline, Button("Ancestor"));
            await deadline.BoundAsync(
                "waiting for the any row",
                () => page.Locator(Options("Ancestor"), new() { HasTextString = "— any —" }).First.WaitForAsync());
            await deadline.BoundAsync(
                "taking any",
                () => page.Locator(Options("Ancestor"), new() { HasTextString = "— any —" }).First.ClickAsync());

            // Parent edit: find the open task by title, make the closed epic its parent.
            await deadline.BoundAsync("typing the title filter", () => page.GetByLabel("Title contains").FillAsync("Open task"));
            await FindIssuesAsync(harness, deadline);
            rows = await ResultRowsAsync(harness, deadline, "CLO-1");
            Assert.DoesNotContain("CLO-3", rows);
            await harness.WaitForVisibleAsync(deadline, ParentChangeSelect);
            await harness.SelectOptionAsync(deadline, ParentChangeSelect, "issue");
            var change = await FindAsync(harness, deadline, "Parent change issue", "CLO-2");
            Assert.Contains("OTH-1", change);
            output.WriteLine("bulk-parent-change: " + await harness.ScreenshotAsync(deadline, "bulk-parent-change"));
            await TakeAsync(harness, deadline, "Parent change issue", "CLO-2");
            await harness.ClickAsync(deadline, "button:has-text(\"Apply to 1\")");
            await deadline.BoundAsync(
                "waiting for the outcome line",
                () => page.Locator(".hatch-bulk-outcome", new() { HasTextString = "1 changed" }).WaitForAsync());

            using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
            var task = await http.GetFromJsonAsync<IssueDto>("/api/issues/CLO-1", Web);
            Assert.Equal("CLO-2", task!.ParentKey);

            // Scoped to CLO: the other project's closed epic is not offered, in any of the three.
            await harness.GoToAsync(deadline, "/bulk?scope=CLO");
            await harness.WaitForVisibleAsync(deadline, ParentFilterSelect);

            await harness.SelectOptionAsync(deadline, ParentFilterSelect, "issue");
            var scopedParent = await FindAsync(harness, deadline, "Parent filter issue", "CLO-2");
            output.WriteLine("scoped parent filter options: " + scopedParent);
            Assert.DoesNotContain("OTH-1", scopedParent);
            await deadline.BoundAsync("closing the picker", () => page.Keyboard.PressAsync("Escape"));

            var scopedAncestor = await FindAsync(harness, deadline, "Ancestor", "CLO-2");
            output.WriteLine("scoped ancestor options: " + scopedAncestor);
            Assert.DoesNotContain("OTH-1", scopedAncestor);
            await deadline.BoundAsync("closing the picker", () => page.Keyboard.PressAsync("Escape"));

            await FindIssuesAsync(harness, deadline);
            await harness.WaitForVisibleAsync(deadline, ParentChangeSelect);
            await harness.SelectOptionAsync(deadline, ParentChangeSelect, "issue");
            var scopedChange = await FindAsync(harness, deadline, "Parent change issue", "CLO-2");
            output.WriteLine("scoped parent change options: " + scopedChange);
            Assert.DoesNotContain("OTH-1", scopedChange);
            output.WriteLine("bulk-scoped: " + await harness.ScreenshotAsync(deadline, "bulk-scoped"));

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

    /// <summary>Opens the picker, types the shared words, waits for <paramref name="expect"/> among its options and returns them all.</summary>
    private static async Task<string> FindAsync(E2eHarness harness, Deadline deadline, string label, string expect)
    {
        var page = harness.Page!;
        await harness.ClickAsync(deadline, Button(label));
        await deadline.BoundAsync("typing title words", () => page.Locator(PickerSearch).FillAsync(TitleWords));
        await deadline.BoundAsync(
            $"waiting for {expect} among {label}'s options",
            () => page.Locator(Options(label), new() { HasTextString = expect }).First.WaitForAsync());
        // A beat for the rest of the lookup's rows to land where they are going to.
        await Task.Delay(500);
        return string.Join("|", await page.Locator(Options(label)).AllInnerTextsAsync());
    }

    private static async Task TakeAsync(E2eHarness harness, Deadline deadline, string label, string key)
    {
        var page = harness.Page!;
        await deadline.BoundAsync($"taking {key}", () => page.Locator(Options(label), new() { HasTextString = key }).First.ClickAsync());
        await deadline.BoundAsync(
            $"waiting for the {label} face to read {key}",
            () => page.Locator(Button(label), new() { HasTextString = key }).WaitForAsync());
    }

    private static Task FindIssuesAsync(E2eHarness harness, Deadline deadline) =>
        harness.ClickAsync(deadline, "button:has-text(\"Find issues\")");

    private static async Task<string> ResultRowsAsync(E2eHarness harness, Deadline deadline, string expect)
    {
        var page = harness.Page!;
        await deadline.BoundAsync(
            $"waiting for {expect} in the results",
            () => page.Locator("tbody tr", new() { HasTextString = expect }).First.WaitForAsync());
        return string.Join("|", await page.Locator("tbody tr").AllInnerTextsAsync());
    }

    private static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
        var now = DateTimeOffset.UtcNow;
        var closedAt = now.AddDays(-30);

        var clo = new EfHatchProject { Key = "CLO", Name = "Closed lookup", CreatedAt = now, NextIssueNumber = 4 };
        var oth = new EfHatchProject { Key = "OTH", Name = "Elsewhere", CreatedAt = now, NextIssueNumber = 2 };
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 10 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 20, IsTerminal = true };
        db.AddRange(clo, oth, todo, done);
        await db.SaveChangesAsync();

        EfHatchIssue Issue(EfHatchProject project, int number, string type, string title, EfHatchStatus status, DateTimeOffset? closed = null) => new()
        {
            ProjectId = project.Id, Number = number, Type = type, Title = title, Description = "", StatusId = status.Id,
            Rank = 1024 * number, CreatedBy = "operator", CreatedAt = now, UpdatedAt = now, ClosedAt = closed,
        };

        var open = Issue(clo, 1, "task", "Open task on the board", todo);
        var epic = Issue(clo, 2, "epic", "Epic past the zenith horizon", done, closedAt);
        var elsewhere = Issue(oth, 1, "epic", "Elsewhere past the zenith horizon", done, closedAt);
        db.Issues.AddRange(open, epic, elsewhere);
        await db.SaveChangesAsync();

        var child = Issue(clo, 3, "task", "Child of the closed epic", todo);
        child.ParentId = epic.Id;
        db.Issues.Add(child);
        await db.SaveChangesAsync();
    }
}

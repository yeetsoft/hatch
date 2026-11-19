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
/// HA-2494: an ambition is filed through the New issue dialog and takes no parent,
/// and an epic filed after it can hang under it. The dialog opened on an ambition
/// has no Parent field; the epic's Parent picker lists the ambition. Both are
/// filed through the page, not the API.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class NewIssueAmbitionTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private const string Panel = ".hatch-modal__panel";
    private const string TypePill = ".hatch-modal__panel .hatch-type-pill-press";
    private const string TypeBand = ".hatch-modal__panel .hatch-type-pill-band";
    private const string Title = ".hatch-modal__panel .hatch-field:has(> .hatch-field__control > .hatch-field__label:text-is(\"Title\")) input";
    private const string FileIt = ".hatch-modal__panel .hatch-newissue-actions button:has-text(\"File it\")";
    private const string ParentButton = ".hatch-modal__panel button[role=combobox][aria-label=Parent]";
    private const string ParentOptions = ".hatch-modal__panel [role=listbox][aria-label=Parent] [role=option]";

    /// <summary>Runs in the page: the dialog's field labels in document order.</summary>
    private const string LabelsScript = """
        () => [...document.querySelectorAll('.hatch-modal__panel .hatch-field__label')].map((l) => l.textContent)
        """;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task AnAmbitionTakesNoParent_AndAnEpicCanHangUnderIt()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("NewIssueAmbition");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline, null);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            await SeedProjectAsync(harness.Services);

            await harness.OpenBoardAsync(deadline, baseUrl);
            await harness.WaitForFirstColumnAsync(deadline);
            var page = harness.Page!;

            // An ambition: the dialog opens on a task, so pick the type first.
            await harness.ClickAsync(deadline, ".hatch-board-new");
            await harness.WaitForVisibleAsync(deadline, Panel);
            await PickTypeAsync(harness, deadline, "ambition");
            var labels = await harness.EvaluateAsync<string[]>(deadline, "reading the ambition's fields", LabelsScript);
            output.WriteLine(string.Join(", ", labels));
            await harness.ScreenshotAsync(deadline, "NewIssueAmbition-ambition");
            Assert.Contains("Title", labels);
            Assert.DoesNotContain("Parent", labels);
            Assert.Equal(0, await page.Locator(ParentButton).CountAsync());
            await deadline.BoundAsync("typing the title", () => page.Locator(Title).FillAsync("Open the ordering to partners"));
            await harness.ClickAsync(deadline, FileIt);
            await harness.WaitForHiddenAsync(deadline, Panel);
            await deadline.BoundAsync("waiting for the ambition's card", () => page.Locator(E2eHarness.Column, new() { HasTextString = "Open the ordering to partners" }).First.WaitForAsync());

            // An epic: the Parent field is back, and the ambition is one of its candidates.
            await harness.ClickAsync(deadline, ".hatch-board-new");
            await harness.WaitForVisibleAsync(deadline, Panel);
            await PickTypeAsync(harness, deadline, "epic");
            labels = await harness.EvaluateAsync<string[]>(deadline, "reading the epic's fields", LabelsScript);
            Assert.Contains("Parent", labels);
            await harness.ClickAsync(deadline, ParentButton);
            await harness.WaitForVisibleAsync(deadline, ParentOptions);
            var options = string.Join("|", await page.Locator(ParentOptions).AllInnerTextsAsync());
            output.WriteLine("parents: " + options);
            await harness.ScreenshotAsync(deadline, "NewIssueAmbition-epic-parents");
            Assert.Contains("ORD-1", options);
            Assert.Contains("Open the ordering to partners", options);
            await deadline.BoundAsync("choosing the ambition", () => page.Locator(ParentOptions, new() { HasTextString = "ORD-1" }).First.ClickAsync());
            await deadline.BoundAsync("typing the title", () => page.Locator(Title).FillAsync("Partner sign-up"));
            await harness.ClickAsync(deadline, FileIt);
            await harness.WaitForHiddenAsync(deadline, Panel);
            await deadline.BoundAsync("waiting for the epic's card", () => page.Locator(E2eHarness.Column, new() { HasTextString = "Partner sign-up" }).First.WaitForAsync());

            // The server took both: the epic hangs under the ambition.
            using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
            var epic = await http.GetFromJsonAsync<IssueDto>("/api/issues/ORD-2", Web);
            Assert.Equal("epic", epic!.Type);
            Assert.Equal("ORD-1", epic.ParentKey);

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

    private static async Task PickTypeAsync(E2eHarness harness, Deadline deadline, string type)
    {
        await harness.ClickAsync(deadline, TypePill);
        await harness.WaitForVisibleAsync(deadline, TypeBand);
        await deadline.BoundAsync($"pressing {type}", () => harness.Page!.Locator(TypeBand).GetByRole(AriaRole.Button, new() { Name = type, Exact = true }).ClickAsync());
        await harness.WaitForHiddenAsync(deadline, TypeBand);
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

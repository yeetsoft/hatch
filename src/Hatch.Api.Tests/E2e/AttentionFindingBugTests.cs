using System.Net.Http.Json;
using System.Text.Json;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2517's acceptance, rendered: with two open findings read from the real
/// database - one that names a project, one that names none - each row in the
/// attention panel's Findings section draws a <em>File a bug</em> button. On the
/// first, the dialog has no Project choice and <em>File it</em> files at once; on
/// the second it asks which project and keeps <em>File it</em> disabled until one
/// is chosen. After each filing the bug's key stands where the button was, with
/// no reload. Run at the desktop viewport in Chromium and at the
/// <c>iPhone 14 Pro</c> profile in WebKit; each look is saved as a PNG beside the
/// case's other artifacts.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class AttentionFindingBugTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string PanelSelector = ".hatch-attention-panel";

    private const string Bug = ".hatch-attention-bug";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task AFindingFilesItsBug_AtTheDesktopProfile() => RunAsync("AttentionFindingBugDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task AFindingFilesItsBug_AtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("AttentionFindingBugPhone", "phone", DeviceProfile.IPhone14Pro);
    }

    private async Task RunAsync(string name, string profileName, DeviceProfile? profile)
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness(name);
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline, profile);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            await SeedAsync(baseUrl, harness.Services);

            await harness.OpenBoardAsync(deadline, baseUrl);
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForVisibleAsync(deadline, $"{E2eHarness.AttentionSelector}[aria-label*=\"2 findings\"]");
            await harness.ClickAsync(deadline, E2eHarness.AttentionSelector);
            await harness.WaitForVisibleAsync(deadline, $"{PanelSelector} h3:text-is(\"Findings\")");

            var withProject = $"{PanelSelector} li:has(a:has-text(\"names its project\"))";
            var withNone = $"{PanelSelector} li:has(a:has-text(\"names no project\"))";

            // The finding that names a project: no Project choice, File it is ready once the columns load.
            await harness.ClickAsync(deadline, $"{withProject} {Bug} button:text-is(\"File a bug\")");
            await harness.WaitForVisibleAsync(deadline, "[role=dialog] button:text-is(\"File it\"):not([disabled])");
            Assert.False(await harness.IsVisibleAsync(deadline, "[role=dialog] select:has(option:text-is(\"Choose a project\"))"));
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"with-project-dialog-{profileName}")}");
            await harness.ClickAsync(deadline, "[role=dialog] button:text-is(\"File it\")");
            await harness.WaitForHiddenAsync(deadline, "[role=dialog]");
            await ReopenPanelAsync(harness, deadline);
            await harness.WaitForVisibleAsync(deadline, $"{withProject} {Bug} a[href*=\"/issues/E2E-\"]");
            Assert.False(await harness.IsVisibleAsync(deadline, $"{withProject} {Bug} button"));

            // The finding that names none: the dialog asks, and File it waits.
            await ReopenPanelAsync(harness, deadline);
            await harness.ClickAsync(deadline, $"{withNone} {Bug} button:text-is(\"File a bug\")");
            await harness.WaitForVisibleAsync(deadline, "[role=dialog] select:has(option:text-is(\"Choose a project\"))");
            Assert.True(await harness.IsVisibleAsync(deadline, "[role=dialog] button:text-is(\"File it\")[disabled]"));
            await harness.SelectOptionAsync(deadline, "[role=dialog] select:has(option:text-is(\"Choose a project\"))", "E2E");
            await harness.WaitForVisibleAsync(deadline, "[role=dialog] button:text-is(\"File it\"):not([disabled])");
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"no-project-dialog-{profileName}")}");
            await harness.ClickAsync(deadline, "[role=dialog] button:text-is(\"File it\")");
            await harness.WaitForHiddenAsync(deadline, "[role=dialog]");
            await ReopenPanelAsync(harness, deadline);
            await harness.WaitForVisibleAsync(deadline, $"{withNone} {Bug} a[href*=\"/issues/E2E-\"]");
            Assert.False(await harness.IsVisibleAsync(deadline, $"{withNone} {Bug} button"));

            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"filed-{profileName}")}");

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
    /// A press inside the dialog is outside the panel, so the panel closes under it
    /// (as it does for the trunk build's dialog) and keeps its rows. Open it again
    /// without reloading the page: the key must already be there.
    /// </summary>
    private static async Task ReopenPanelAsync(E2eHarness harness, Deadline deadline)
    {
        if (!await harness.IsVisibleAsync(deadline, PanelSelector))
            await harness.ClickAsync(deadline, E2eHarness.AttentionSelector);
        await harness.WaitForVisibleAsync(deadline, PanelSelector);
    }

    /// <summary>One column, the project, and two open findings: one about the project, one about none.</summary>
    private static async Task SeedAsync(string baseUrl, IServiceProvider services)
    {
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
            db.Statuses.Add(new EfHatchStatus { Name = "todo", SortOrder = 10, Color = "#e8eaed" });
            await db.SaveChangesAsync();
        }

        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        using var response = await http.PostAsJsonAsync("/api/projects", new ProjectCreateRequest("E2E", "Scratch project"), Web);
        var created = JsonSerializer.Deserialize<ProjectDto>(await response.Content.ReadAsStringAsync(), Web)!;
        Assert.True(response.IsSuccessStatusCode, $"POST /api/projects answered {(int)response.StatusCode}");

        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
            var now = DateTimeOffset.UtcNow;
            db.GooseFindings.Add(Finding("E2E", "E2E", created.Id, "A question that names its project has waited.", now.AddHours(-3)));
            db.GooseFindings.Add(Finding("none", "none", null, "A question that names no project has waited.", now.AddHours(-2)));
            await db.SaveChangesAsync();
        }
    }

    private static EfHatchGooseFinding Finding(string subject, string token, int? projectId, string sentence, DateTimeOffset began) => new()
    {
        // Not a check the goose owns: its sweep closes any open finding of a check
        // it runs that it no longer sees, which would close these before the page loads.
        Check = "e2e-fixture",
        Subject = subject,
        Token = token,
        ProjectId = projectId,
        BeganAt = began,
        OpenedAt = began.AddMinutes(15),
        Sentence = sentence,
        Evidence = GooseFindingEvidence.Write([new("judged", "questions")]),
    };
}

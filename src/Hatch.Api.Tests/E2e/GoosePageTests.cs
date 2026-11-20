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
/// HA-2502's acceptance, rendered: the goose's page at <c>/goose</c>, read from the
/// real database. One run with a failed check, one open finding with no bug, and
/// one closed finding whose bug has a work log row and the event that released
/// its session. The page is checked for its nav row, its sections in order, a run
/// opening to its checks, a finding opening to its evidence, the bug's key,
/// column, brief and report with the runner, the File a bug button, and no page
/// errors. Run at the desktop viewport in Chromium and at the <c>iPhone 14 Pro</c>
/// profile in WebKit; each look is saved as a PNG beside the case's other
/// artifacts.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class GoosePageTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Row = ".hatch-goose-row";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheGoosePageReads_AtTheDesktopProfile() => RunAsync("GoosePageDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheGoosePageReads_AtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("GoosePagePhone", "phone", DeviceProfile.IPhone14Pro);
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

            await harness.OpenAsync(deadline, baseUrl, "/goose");
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForVisibleAsync(deadline, $"{Row}:has-text(\"A question has waited\")");

            // The top line, and the three sections in the order the page promises.
            await harness.WaitForVisibleAsync(deadline, ".hatch-goose-state");
            var headings = await harness.EvaluateAsync<string[]>(
                deadline, "reading the section headings", "() => [...document.querySelectorAll('.hatch-goose-heading')].map((h) => h.textContent)");
            Assert.Equal(["Open findings", "Runs", "Closed findings"], headings);

            // A run opens in place to its checks, the failed one with its error.
            var run = $"{Row}:has-text(\"1 failed\")";
            await harness.ClickAsync(deadline, $"{run} .hatch-goose-toggle");
            await harness.WaitForVisibleAsync(deadline, $"{run} .hatch-goose-check-name:text-is(\"e2e-ok\")");
            await harness.WaitForVisibleAsync(deadline, $"{run} :text(\"Failed: boom\")");
            output.WriteLine($"saved {await harness.FullPageScreenshotAsync(deadline, $"run-open-{profileName}")}");

            // An open finding opens to its evidence, one label and value to a line, and carries the button.
            var open = $"{Row}:has-text(\"A question has waited\")";
            await harness.ClickAsync(deadline, $"{open} .hatch-goose-toggle");
            await harness.WaitForVisibleAsync(deadline, $"{open} dt:text-is(\"Judged\")");
            await harness.WaitForVisibleAsync(deadline, $"{open} button:text-is(\"File a bug\")");

            // A closed finding shows its bug's key, and opened, the bug's column, brief and report.
            var closed = $"{Row}:has-text(\"A train stood\")";
            await harness.WaitForVisibleAsync(deadline, $"{closed} .hatch-goose-actions a[href*=\"/issues/E2E-900\"]");
            await harness.ClickAsync(deadline, $"{closed} .hatch-goose-toggle");
            await harness.WaitForVisibleAsync(deadline, $"{closed} .hatch-goose-column:text-is(\"todo\")");
            await harness.WaitForVisibleAsync(deadline, $"{closed} :text(\"Find out why the train stood.\")");
            await harness.WaitForVisibleAsync(deadline, $"{closed} .hatch-goose-report-title:text-is(\"Found the cause\")");
            await harness.WaitForVisibleAsync(deadline, $"{closed} .hatch-goose-meta:has-text(\"here:/repo\")");
            output.WriteLine($"saved {await harness.FullPageScreenshotAsync(deadline, $"finding-open-{profileName}")}");

            // The nav names the page.
            Assert.True(await harness.EvaluateAsync<bool>(
                deadline, "looking for the nav row", "() => [...document.querySelectorAll('a[href$=\"/goose\"]')].length > 0"));

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
    /// One column and the project; a run with a failed check; an open finding with no
    /// bug; and a closed finding whose bug has a session and the release that ended it.
    /// </summary>
    private static async Task SeedAsync(string baseUrl, IServiceProvider services)
    {
        EfHatchStatus status;
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
            status = new EfHatchStatus { Name = "todo", SortOrder = 10, Color = "#e8eaed" };
            db.Statuses.Add(status);
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

            db.GooseRuns.Add(new EfHatchGooseRun
            {
                FirstRanAt = now.AddHours(-3),
                LastRanAt = now.AddMinutes(-1),
                Sweeps = 180,
                LongestMs = 1240,
                Entries = GooseEntry.Write([
                    new GooseEntry("e2e-ok", "3 projects", "nothing is standing", false, null),
                    new GooseEntry("e2e-broken", "", "", true, "boom"),
                ]),
            });

            var bug = new EfHatchIssue
            {
                ProjectId = created.Id,
                // Well clear of the number a bug filed in this run would take.
                Number = 900,
                Type = "bug",
                Title = "Why a train stood",
                Description = "Find out why the train stood.",
                StatusId = status.Id,
                Rank = 1,
                CreatedBy = "e2e",
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Issues.Add(bug);
            await db.SaveChangesAsync();

            db.WorkLog.Add(new EfHatchWorkLogEntry
            {
                IssueId = bug.Id,
                SessionId = "e2e-session",
                StartedAt = now.AddMinutes(-40),
                EndedAt = now.AddMinutes(-30),
                DurationMs = 600_000,
                Title = "Found the cause",
                Summary = "The head was waiting on a build.",
                Described = true,
                CreatedAt = now,
            });
            db.IssueEvents.Add(new EfHatchIssueEvent
            {
                IssueId = bug.Id,
                Actor = "e2e-runner",
                Kind = EfHatchIssueEvent.ClaimReleased,
                Payload = JsonSerializer.Serialize(new { from = (string?)null, to = (string?)null, runner = "here:/repo", outcome = "worked", sessionId = "e2e-session" }, Web),
                At = now.AddMinutes(-30),
            });

            db.GooseFindings.Add(Finding("open", created.Id, "A question has waited.", now.AddHours(-3), null, null));
            db.GooseFindings.Add(Finding("closed", created.Id, "A train stood.", now.AddHours(-9), now.AddHours(-2), bug.Id));
            await db.SaveChangesAsync();
        }
    }

    private static EfHatchGooseFinding Finding(
        string subject, int projectId, string sentence, DateTimeOffset began, DateTimeOffset? closed, long? bugId) => new()
    {
        // Not a check the goose owns: its sweep closes any open finding of a check
        // it runs that it no longer sees, which would close these before the page loads.
        Check = "e2e-fixture",
        Subject = subject,
        Token = subject,
        ProjectId = projectId,
        BeganAt = began,
        OpenedAt = began.AddMinutes(15),
        ClosedAt = closed,
        Sentence = sentence,
        BugIssueId = bugId,
        Evidence = GooseFindingEvidence.Write([new("Judged", "questions"), new("Waited", "3 hours")]),
    };
}

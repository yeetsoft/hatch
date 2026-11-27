using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2836's acceptance, rendered: on the Leaderboard, scoped to two projects
/// of three, the Issue filter finds an issue closed long before the board's
/// own done horizon. Typing a word shared by a closed epic in scope and one
/// out of scope lists only the one in scope; typing the out-of-scope epic's
/// whole key lists nothing, once the lookup has settled; typing the in-scope
/// closed epic's number finds it, and taking it narrows the session table to
/// its row and its closed child's, dropping the open sibling. Desktop in
/// Chromium, and the phone profile in WebKit.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class LeaderboardClosedIssueTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string IssueButton = "button[role=combobox][aria-label=Issue]";
    private const string Search = ".hatch-issue-picker-search";
    private const string Options = ".hatch-issue-picker-list [role=option]";
    private const string LookingNote = ".hatch-issue-picker-note";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheIssueFilter_FindsAClosedIssue_AtTheDesktopProfile() => RunAsync("LeaderboardClosedIssueDesktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheIssueFilter_FindsAClosedIssue_AtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("LeaderboardClosedIssuePhone", DeviceProfile.IPhone14Pro);
    }

    private async Task RunAsync(string name, DeviceProfile? profile)
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
            await deadline.BoundAsync("seeding three projects and their issues", () => SeedAsync(harness.Services));

            await harness.OpenAsync(deadline, baseUrl, "/leaderboard?scope=SCA,SCB");
            await harness.WaitForVisibleAsync(deadline, IssueButton);
            await harness.WaitForFunctionAsync(
                deadline, "waiting for the unfiltered table", "() => document.querySelectorAll('tbody tr').length === 3");

            // Typing a word shared by SCA's closed epic and SCC's lists only the one in scope.
            await harness.ClickAsync(deadline, IssueButton);
            await harness.WaitForVisibleAsync(deadline, Search);
            var page = harness.Page!;
            await deadline.BoundAsync("typing the shared word", () => page.Locator(Search).FillAsync("harbor"));
            await harness.WaitForVisibleAsync(deadline, $"{Options}:has-text(\"SCA-1\")");
            var shared = await Texts(harness, deadline, Options);
            output.WriteLine("shared-word options: " + string.Join(" | ", shared));
            Assert.Contains(shared, t => t.Contains("SCA-1"));
            Assert.DoesNotContain(shared, t => t.Contains("SCC-1"));
            output.WriteLine("screenshot: " + await harness.ScreenshotAsync(deadline, "shared-word"));

            // Typing the out-of-scope epic's whole key lists nothing, once the lookup has settled.
            await deadline.BoundAsync("typing the out-of-scope key", () => page.Locator(Search).FillAsync("SCC-1"));
            await harness.WaitForHiddenAsync(deadline, $"{LookingNote}:text-is(\"Looking…\")");
            var outOfScope = await Texts(harness, deadline, Options);
            output.WriteLine("out-of-scope options: " + string.Join(" | ", outOfScope));
            Assert.DoesNotContain(outOfScope, t => t.Contains("SCC-1"));

            // Typing the closed epic's number finds it; taking it narrows the address and the table.
            await deadline.BoundAsync("typing the closed epic's number", () => page.Locator(Search).FillAsync("1"));
            await harness.WaitForVisibleAsync(deadline, $"{Options}:has-text(\"SCA-1\")");
            output.WriteLine("open picker: " + await harness.ScreenshotAsync(deadline, "open-picker"));
            await harness.ClickAsync(deadline, $"{Options}:has-text(\"SCA-1\")");

            await harness.WaitForFunctionAsync(
                deadline, "waiting for the address to narrow", "() => location.search.includes('issue=SCA-1')");
            await harness.WaitForFunctionAsync(
                deadline, "waiting for the narrowed table", "() => document.querySelectorAll('tbody tr').length === 2");
            var narrowed = await harness.EvaluateAsync<string[]>(
                deadline, "reading the narrowed issue column",
                "() => [...document.querySelectorAll('tbody tr td:nth-child(2)')].map((e) => e.textContent.trim()).sort()");
            output.WriteLine("narrowed rows: " + string.Join(" | ", narrowed));
            Assert.Equal(["SCA-1", "SCA-2"], narrowed);
            output.WriteLine("narrowed page: " + await harness.FullPageScreenshotAsync(deadline, "narrowed"));

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

    private static Task<IReadOnlyList<string>> Texts(E2eHarness harness, Deadline deadline, string selector) =>
        deadline.BoundAsync($"reading {selector}", () => harness.Page!.Locator(selector).AllInnerTextsAsync());

    /// <summary>
    /// Three projects, a todo column and a terminal one. SCA holds a closed
    /// epic with a closed task under it and an open task; SCC holds a closed
    /// epic sharing a title word with SCA's. A work log row, ended within the
    /// last day, sits on each of the SCA issues.
    /// </summary>
    private static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
        var now = DateTimeOffset.UtcNow;
        var closedAt = now.AddDays(-30);

        var sca = new EfHatchProject { Key = "SCA", Name = "Project SCA", CreatedAt = now };
        var scb = new EfHatchProject { Key = "SCB", Name = "Project SCB", CreatedAt = now };
        var scc = new EfHatchProject { Key = "SCC", Name = "Project SCC", CreatedAt = now };
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 10 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 20, IsTerminal = true };
        db.AddRange(sca, scb, scc, todo, done);
        await db.SaveChangesAsync();

        var epic = new EfHatchIssue
        {
            ProjectId = sca.Id,
            Number = 1,
            Type = "epic",
            Title = "Harbor lighthouse survey",
            Description = "",
            StatusId = done.Id,
            ClosedAt = closedAt,
            Rank = 1024,
            CreatedBy = "operator",
            CreatedAt = closedAt,
            UpdatedAt = closedAt,
        };
        db.Add(epic);
        await db.SaveChangesAsync();

        var task = new EfHatchIssue
        {
            ProjectId = sca.Id,
            Number = 2,
            Type = "task",
            Title = "Lighthouse paint inspection",
            Description = "",
            StatusId = done.Id,
            ClosedAt = closedAt,
            ParentId = epic.Id,
            Rank = 1024,
            CreatedBy = "operator",
            CreatedAt = closedAt,
            UpdatedAt = closedAt,
        };
        var openTask = new EfHatchIssue
        {
            ProjectId = sca.Id,
            Number = 3,
            Type = "task",
            Title = "Harbor dock repair",
            Description = "",
            StatusId = todo.Id,
            Rank = 1024,
            CreatedBy = "operator",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var otherEpic = new EfHatchIssue
        {
            ProjectId = scc.Id,
            Number = 1,
            Type = "epic",
            Title = "Harbor warehouse survey",
            Description = "",
            StatusId = done.Id,
            ClosedAt = closedAt,
            Rank = 1024,
            CreatedBy = "operator",
            CreatedAt = closedAt,
            UpdatedAt = closedAt,
        };
        db.AddRange(task, openTask, otherEpic);
        sca.NextIssueNumber = 4;
        scc.NextIssueNumber = 2;
        await db.SaveChangesAsync();

        foreach (var issue in new[] { epic, task, openTask })
        {
            var at = now.AddHours(-1);
            db.WorkLog.Add(new EfHatchWorkLogEntry
            {
                IssueId = issue.Id,
                SessionId = $"sc-{issue.Number}",
                StartedAt = at.AddMinutes(-20),
                EndedAt = at,
                DurationMs = 1_200_000,
                IssueType = issue.Type,
                Title = $"Session on {issue.Title}",
                CostUsd = 0.5m,
                InputTokens = 1_000,
                OutputTokens = 1_000,
                CacheCreationTokens = 1_000,
                CacheReadTokens = 7_000,
                CreatedAt = now,
            });
        }

        await db.SaveChangesAsync();
    }
}

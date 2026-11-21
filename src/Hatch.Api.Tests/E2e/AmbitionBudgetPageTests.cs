using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2631's acceptance, rendered: an ambition's page draws a Budget card
/// after Capabilities. Against an ambition with a budget part spent it draws
/// the bar, the line "41% spent, for 1 of 3 capabilities", the stop sentence
/// and how many sessions carried no price; against one with no budget it says
/// what has been spent and that no budget is set, and says nothing of
/// stopping. Run at the desktop viewport in Chromium and at the
/// <c>iPhone 14 Pro</c> profile in WebKit, where nothing may scroll sideways.
///
/// <para>The budget itself is set over HTTP, the way a person sets it: the
/// route takes a person, and the harness's keyless caller reads as one. The
/// work-log rows are written straight to the database, since the work-log
/// route only a key may post to (<see cref="IssueWorkLogController"/>), and a
/// capability's verdict is stamped by the server as now.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class AmbitionBudgetPageTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    /// <summary>Sub-pixel layout is fractional; two boxes that touch are not overlapping.</summary>
    private const double Tolerance = 0.5;

    /// <summary>A section is a <c>details</c> whose summary is its title; the section id is a storage key, not an element id.</summary>
    private const string Budget = ".hatch-issue-section:has(> summary:text-matches(\"^Budget\"))";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static DateTimeOffset Hour(int n) => Now.AddHours(n);

    /// <summary>The Budget section's text, the fill's inline width and the page's width against the viewport's, read in one script so they are one moment.</summary>
    private const string MeasureScript = """
        (title) => {
          const section = [...document.querySelectorAll('details.hatch-issue-section')]
            .find((d) => d.querySelector(':scope > summary')?.textContent.startsWith(title));
          const fill = section?.querySelector('.hatch-budget-fill');
          return {
            text: section ? section.textContent : null,
            bars: section ? section.querySelectorAll('.hatch-budget-bar').length : 0,
            fillWidth: fill ? fill.style.width : null,
            innerWidth: window.innerWidth,
            scrollWidth: document.documentElement.scrollWidth,
          };
        }
        """;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheCard_RendersAtTheDesktopProfile() => RunAsync("AmbitionBudgetDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheCard_RendersAtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("AmbitionBudgetPhone", "phone", DeviceProfile.IPhone14Pro);
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
            var (partSpent, unbudgeted) = await deadline.BoundAsync("seeding the two ambitions", () => SeedAsync(baseUrl, harness.Services));

            // The ambition with a budget part spent.
            await harness.OpenAsync(deadline, baseUrl, $"/issues/{partSpent}");
            await harness.WaitForVisibleAsync(deadline, ".hatch-issue-page");
            await harness.WaitForVisibleAsync(deadline, $"{Budget} .hatch-budget-bar");
            await ScrollToBudgetAsync(harness, deadline);

            var measured = await harness.EvaluateAsync<JsonElement>(deadline, "measuring the Budget card", MeasureScript, "Budget");
            output.WriteLine($"{profileName} /issues/{partSpent}: {measured}");
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"ambition-budget-part-spent-{profileName}")}");

            var text = measured.GetProperty("text").GetString()!;
            Assert.Equal(1, measured.GetProperty("bars").GetInt32());
            Assert.Contains("41% spent, for 1 of 3 capabilities", text);
            Assert.Contains("$41.00 of $100.00", text);
            Assert.Contains("At 100% the ambition stops carving and asks. Work already in flight finishes.", text);
            Assert.Contains("1 session carried no price, so this reads as at least that much.", text);
            Assert.DoesNotContain("The budget is spent.", text);
            Assert.DoesNotContain("No budget is set.", text);
            Assert.Equal("41%", measured.GetProperty("fillWidth").GetString());
            AssertFits(measured, profile is not null);

            // The ambition with no budget.
            await harness.OpenAsync(deadline, baseUrl, $"/issues/{unbudgeted}");
            await harness.WaitForVisibleAsync(deadline, ".hatch-issue-page");
            await harness.WaitForVisibleAsync(deadline, $"{Budget}:has-text(\"No budget is set.\")");
            await ScrollToBudgetAsync(harness, deadline);

            measured = await harness.EvaluateAsync<JsonElement>(deadline, "measuring the Budget card", MeasureScript, "Budget");
            output.WriteLine($"{profileName} /issues/{unbudgeted}: {measured}");
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"ambition-budget-none-{profileName}")}");

            text = measured.GetProperty("text").GetString()!;
            Assert.Equal(0, measured.GetProperty("bars").GetInt32());
            Assert.Contains("$12.50 spent so far. No budget is set.", text);
            Assert.DoesNotContain("stops carving", text);
            Assert.DoesNotContain("The budget is spent.", text);
            Assert.DoesNotContain("carried no price", text);
            AssertFits(measured, profile is not null);

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

    /// <summary>Brings the Budget card to the top of the viewport, so the screenshot that follows shows what was asserted on rather than the page's header.</summary>
    private static Task ScrollToBudgetAsync(E2eHarness harness, Deadline deadline) =>
        harness.EvaluateAsync<bool>(deadline, "scrolling to Budget", """
            () => {
              const section = [...document.querySelectorAll('details.hatch-issue-section')]
                .find((d) => d.querySelector(':scope > summary')?.textContent.startsWith('Budget'));
              section?.scrollIntoView({ block: 'start' });
              return !!section;
            }
            """);

    /// <summary>The page may not scroll sideways on the phone; on the desktop the same check costs nothing, so it is made there too.</summary>
    private static void AssertFits(JsonElement measured, bool phone)
    {
        var innerWidth = measured.GetProperty("innerWidth").GetDouble();
        Assert.True(
            measured.GetProperty("scrollWidth").GetDouble() <= innerWidth + Tolerance,
            $"the page scrolls sideways{(phone ? " on the phone" : "")}: {measured}");
    }

    /// <summary>
    /// Two ambitions, each with three capabilities, filed over HTTP the way any
    /// caller files one. The first has a $100 budget - set over HTTP, as a
    /// person sets it - and $41 spent across two priced sessions and one that
    /// carried no price; one capability holds. The second has no budget and
    /// $12.50 spent. The sessions are written straight to the database.
    /// Returns the two keys, part spent first.
    /// </summary>
    private static async Task<(string PartSpent, string Unbudgeted)> SeedAsync(string baseUrl, IServiceProvider services)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("BUD", "BUD"));
        var partSpent = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "ambition", "Keep the spend in view", null, null, null, null));
        var unbudgeted = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "ambition", "Spend without a ceiling", null, null, null, null));

        using (var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/issues/{partSpent.Key}/budget")
        {
            Content = JsonContent.Create(new IssueBudgetRequest("100"), options: Web),
        })
        {
            using var response = await http.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, $"PATCH the budget answered {(int)response.StatusCode}: {text}");
        }

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();

        var implementation = await db.Statuses.OrderBy(s => s.SortOrder)
            .FirstAsync(s => !s.IsTerminal && !s.IsDeferred);
        implementation.IsImplementation = true;
        await db.SaveChangesAsync();

        var partSpentId = await IssueIdAsync(db, partSpent.Key);
        var unbudgetedId = await IssueIdAsync(db, unbudgeted.Key);

        var holds = AddCapability(db, partSpentId, 1, "capability a");
        AddCapability(db, partSpentId, 2, "capability b");
        AddCapability(db, partSpentId, 3, "capability c");
        AddCapability(db, unbudgetedId, 1, "capability a");
        AddCapability(db, unbudgetedId, 2, "capability b");
        AddCapability(db, unbudgetedId, 3, "capability c");
        await db.SaveChangesAsync();

        // One of the three holds on the first ambition; none is checked on the second.
        db.CapabilityVerdicts.Add(new EfHatchCapabilityVerdict
        {
            CapabilityId = holds.Id, Verdict = EfHatchCapabilityVerdict.Holds, LookedAt = "the staging order flow",
            Sha = "aaa1111", Author = "a person", CreatedAt = Hour(0),
        });

        db.WorkLog.Add(Session(partSpentId, Hour(0), 20.00m, implementation.Id));
        db.WorkLog.Add(Session(partSpentId, Hour(1), 21.00m, implementation.Id));
        db.WorkLog.Add(Session(partSpentId, Hour(2), null, implementation.Id));
        db.WorkLog.Add(Session(unbudgetedId, Hour(0), 12.50m, implementation.Id));

        await db.SaveChangesAsync();

        return (partSpent.Key, unbudgeted.Key);
    }

    private static async Task<long> IssueIdAsync(HatchContext db, string key)
    {
        Assert.True(IssueKey.TryParse(key, out var projectKey, out var number));
        return await db.Issues.WithKey(projectKey, number).Select(i => i.Id).SingleAsync();
    }

    private static EfHatchCapability AddCapability(HatchContext db, long issueId, int position, string statement)
    {
        var cap = new EfHatchCapability
        {
            IssueId = issueId, Position = position, Statement = statement, HowToTell = $"{statement}?",
            CreatedBy = "a person", CreatedAt = Hour(-1), UpdatedAt = Hour(-1),
        };
        db.Capabilities.Add(cap);
        return cap;
    }

    /// <summary>One finished session. A null cost is a session the agent reported no price for.</summary>
    private static EfHatchWorkLogEntry Session(long issueId, DateTimeOffset start, decimal? costUsd, int statusId) =>
        new()
        {
            IssueId = issueId, SessionId = Guid.NewGuid().ToString("N"), StartedAt = start, EndedAt = start.AddMinutes(30),
            DurationMs = (long)TimeSpan.FromMinutes(30).TotalMilliseconds, CreatedAt = start.AddMinutes(30),
            IssueType = "ambition", PlaybookFromStatusId = statusId, PlaybookToStatusId = statusId, Outcome = ClaimOutcomes.Worked,
            Title = "a session", Summary = "what it did", CostUsd = costUsd,
        };

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

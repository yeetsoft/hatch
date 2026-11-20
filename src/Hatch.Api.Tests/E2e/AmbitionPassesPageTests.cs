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
/// HA-2550's acceptance, rendered: an ambition's page draws a Passes section,
/// after Epics in flight, with one entry per pass - newest first, each dated
/// and with the sha it judged against - the verdict it gave each capability
/// marked where the word changed from the pass before, an entry whose
/// verdicts could not be recorded saying so beside its comment, and a verdict
/// recorded with no pass around it standing as its own entry. Run at the
/// desktop viewport in Chromium and at the <c>iPhone 14 Pro</c> profile in
/// WebKit, where nothing may scroll sideways.
///
/// <para>The work-log rows, verdicts and comments this needs are seeded
/// straight to the database, the way <see cref="WorkLogOutcomeTests"/> seeds
/// its sessions: the work-log route only a key may post to
/// (<see cref="IssueWorkLogController"/>), and a verdict's own route stamps
/// <c>CreatedAt</c> as now rather than taking one, so neither can place a row
/// at a chosen moment in the past over HTTP.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class AmbitionPassesPageTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    /// <summary>Sub-pixel layout is fractional; two boxes that touch are not overlapping.</summary>
    private const double Tolerance = 0.5;

    /// <summary>A section is a <c>details</c> whose summary is its title; the section id is a storage key, not an element id.</summary>
    private const string Passes = ".hatch-issue-section:has(> summary:text-matches(\"^Passes\"))";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static DateTimeOffset Hour(int n) => Now.AddHours(n);

    /// <summary>One section's text and entry count, found by its title, and the page's width against the viewport's, read in one script so they are one moment.</summary>
    private const string MeasureScript = """
        (title) => {
          const section = [...document.querySelectorAll('details.hatch-issue-section')]
            .find((d) => d.querySelector(':scope > summary')?.textContent.startsWith(title));
          return {
            text: section ? section.textContent : null,
            entries: section ? section.querySelectorAll('.hatch-pass').length : 0,
            changed: section ? section.querySelectorAll('.hatch-pass-changed').length : 0,
            innerWidth: window.innerWidth,
            scrollWidth: document.documentElement.scrollWidth,
          };
        }
        """;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheSection_RendersAtTheDesktopProfile() => RunAsync("AmbitionPassesDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheSection_RendersAtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("AmbitionPassesPhone", "phone", DeviceProfile.IPhone14Pro);
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
            var ambition = await deadline.BoundAsync("seeding the ambition's passes", () => SeedAsync(baseUrl, harness.Services));

            await harness.OpenAsync(deadline, baseUrl, $"/issues/{ambition}");
            await harness.WaitForVisibleAsync(deadline, ".hatch-issue-page");
            await harness.WaitForVisibleAsync(deadline, Passes);
            await harness.WaitForVisibleAsync(deadline, $"{Passes} .hatch-pass");

            var measured = await harness.EvaluateAsync<JsonElement>(deadline, "measuring the Passes section", MeasureScript, "Passes");
            output.WriteLine($"{profileName} /issues/{ambition}: {measured}");
            await ScrollToPassesAsync(harness, deadline);
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"ambition-passes-{profileName}")}");

            Assert.Equal(4, measured.GetProperty("entries").GetInt32());
            // Exactly one verdict carries the changed mark: capA's newer word, not capB's unchanged one.
            Assert.Equal(1, measured.GetProperty("changed").GetInt32());

            var text = measured.GetProperty("text").GetString()!;
            Assert.Contains("the newer pass", text);
            Assert.Contains("the older pass", text);
            Assert.Contains("aaa1111", text);
            Assert.Contains("bbb2222", text);
            Assert.Contains("Verdicts not recorded as data.", text);
            Assert.Contains("an epic's own note", text);
            Assert.Contains("Recorded outside a pass.", text);
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

    /// <summary>Brings the Passes section to the top of the viewport, so the screenshot that follows shows what was asserted on rather than the page's header.</summary>
    private static Task ScrollToPassesAsync(E2eHarness harness, Deadline deadline) =>
        harness.EvaluateAsync<bool>(deadline, "scrolling to Passes", """
            () => {
              const section = [...document.querySelectorAll('details.hatch-issue-section')]
                .find((d) => d.querySelector(':scope > summary')?.textContent.startsWith('Passes'));
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
    /// An ambition with three capabilities, filed over HTTP the way any caller
    /// files one. Everything that needs a timestamp in the past - the two
    /// pass rows, the row whose flag reads false, their verdicts and comments,
    /// and the loose verdict with no row around it - is written straight to
    /// the database, since neither the work-log route nor a verdict's own will
    /// take one. Returns the ambition's key.
    /// </summary>
    private static async Task<string> SeedAsync(string baseUrl, IServiceProvider services)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("PAS", "PAS"));
        var ambition = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "ambition", "Ship passes on the page", null, null, null, null));

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();

        Assert.True(IssueKey.TryParse(ambition.Key, out var projectKey, out var number));
        var issueId = await db.Issues.WithKey(projectKey, number).Select(i => i.Id).SingleAsync();

        var implementation = await db.Statuses.OrderBy(s => s.SortOrder)
            .FirstAsync(s => !s.IsTerminal && !s.IsDeferred);
        implementation.IsImplementation = true;
        await db.SaveChangesAsync();

        var capA = AddCapability(db, issueId, 1, "capability a");
        var capB = AddCapability(db, issueId, 2, "capability b");
        var capC = AddCapability(db, issueId, 3, "capability c");
        await db.SaveChangesAsync();

        // The older pass: a does not hold, b holds.
        db.WorkLog.Add(PassRow(issueId, Hour(0), Hour(1), "ambition", implementation.Id, "the older pass"));
        AddVerdict(db, capA.Id, EfHatchCapabilityVerdict.DoesNotHold, "aaa1111", Hour(0).AddMinutes(10));
        AddVerdict(db, capB.Id, EfHatchCapabilityVerdict.Holds, "aaa1111", Hour(0).AddMinutes(20));
        db.Comments.Add(new EfHatchComment { IssueId = issueId, Author = "a person", Body = "said during the older pass", CreatedAt = Hour(0).AddMinutes(5) });

        // The newer pass: a now holds (changed), b still holds (unchanged).
        db.WorkLog.Add(PassRow(issueId, Hour(2), Hour(3), "ambition", implementation.Id, "the newer pass"));
        AddVerdict(db, capA.Id, EfHatchCapabilityVerdict.Holds, "bbb2222", Hour(2).AddMinutes(10));
        AddVerdict(db, capB.Id, EfHatchCapabilityVerdict.Holds, "bbb2222", Hour(2).AddMinutes(20));
        db.Comments.Add(new EfHatchComment { IssueId = issueId, Author = "a person", Body = "said during the newer pass", CreatedAt = Hour(2).AddMinutes(5) });

        // A row whose own type is not "ambition": its flag reads false, so it carries no verdict lines, only its comment.
        db.WorkLog.Add(PassRow(issueId, Hour(5), Hour(6), "epic", implementation.Id, "an epic's own pass"));
        db.Comments.Add(new EfHatchComment { IssueId = issueId, Author = "a person", Body = "an epic's own note", CreatedAt = Hour(5).AddMinutes(5) });

        await db.SaveChangesAsync();

        // A verdict with no row's window around it - its own entry, with no session.
        AddVerdict(db, capC.Id, EfHatchCapabilityVerdict.CannotTell, "ccc3333", Hour(10));
        await db.SaveChangesAsync();

        return ambition.Key;
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

    private static void AddVerdict(HatchContext db, long capabilityId, string verdict, string sha, DateTimeOffset at) =>
        db.CapabilityVerdicts.Add(new EfHatchCapabilityVerdict
        {
            CapabilityId = capabilityId, Verdict = verdict, LookedAt = "the staging order flow", Sha = sha, Author = "a person", CreatedAt = at,
        });

    private static EfHatchWorkLogEntry PassRow(long issueId, DateTimeOffset start, DateTimeOffset end, string issueType, int statusId, string title) =>
        new()
        {
            IssueId = issueId, SessionId = Guid.NewGuid().ToString("N"), StartedAt = start, EndedAt = end,
            DurationMs = (long)(end - start).TotalMilliseconds, CreatedAt = end,
            IssueType = issueType, PlaybookFromStatusId = statusId, PlaybookToStatusId = statusId, Outcome = ClaimOutcomes.Worked,
            Title = title, Summary = "what it did",
        };

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

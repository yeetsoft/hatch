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
/// HA-2504, rendered: a card, its peek, the issue page and the plan each say in
/// one sentence where an inherited level came from and whether it is honoured.
/// Two epics in one project carry an expedited, pre-approved level; one child
/// epic in a second project inherits the level the person who set it may set
/// there too (honoured), and the other inherits one with no setter recorded,
/// which stops at the project line (passed over). Run at the desktop viewport
/// in Chromium, at the <c>iPhone 14 Pro</c> profile in WebKit, and again in
/// Chromium for a machine where WebKit does not open a page.
///
/// <para>A level from an ancestor outside the reader's scope cannot be reached
/// here: the host runs with the wall off, so nothing is outside anybody's scope.
/// Neither can a pull that is not honoured, which needs a parent in progress
/// started by somebody who lacks the child's project. Both are pinned by
/// <c>inherited.test.ts</c> and the component tests beside it.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class InheritedSentencesTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 120;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private const string HonouredTitle = "Child that honours its parent";
    private const string PassedOverTitle = "Child that does not";

    /// <summary>The marks on one card, the two pills on the issue page and the plan's mark, in one script so they are one moment.</summary>
    private const string CardScript = """
        (key) => ({
          marks: [...document.querySelectorAll(`[data-issue-key="${key}"] .hatch-card-marks > span`)].map((s) => ({
            cls: s.className, title: s.getAttribute('title'), text: s.textContent,
          })),
          scrollWidth: document.documentElement.scrollWidth,
          innerWidth: window.innerWidth,
        })
        """;

    private const string PillScript = """
        () => ({
          pills: [...document.querySelectorAll('button[aria-label^="Priority:"], button[aria-label^="Pre-approval:"]')].map((b) => b.getAttribute('aria-label')),
          scrollWidth: document.documentElement.scrollWidth,
          innerWidth: window.innerWidth,
        })
        """;

    private const string PlanScript = """
        () => ({
          rows: [...document.querySelectorAll('.hatch-plan-row')].map((row) => ({
            title: row.querySelector(':scope > .hatch-plan-head .hatch-plan-title').textContent,
            mark: row.querySelector(':scope > .hatch-plan-head .hatch-plan-inherited')?.getAttribute('title') ?? null,
            glyph: row.querySelector(':scope > .hatch-plan-head .hatch-plan-inherited')?.textContent ?? null,
          })),
          scrollWidth: document.documentElement.scrollWidth,
          innerWidth: window.innerWidth,
        })
        """;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheSentences_AreDrawnAtTheDesktopProfile() => RunAsync("InheritedDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheSentences_AreDrawnAtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("InheritedPhone", "phone", DeviceProfile.IPhone14Pro);
    }

    /// <summary>The same phone, emulated in Chromium: the case that still runs where WebKit does not open a page.</summary>
    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheSentences_AreDrawnAtThePhoneProfileInChromium() =>
        RunAsync("InheritedPhoneChromium", "phone-chromium", DeviceProfile.IPhone14ProChromium);

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
            var tree = await deadline.BoundAsync("filing the tree", () => FileTreeAsync(baseUrl, harness.Services));

            // The server's own words first, so a failure below says whether the data or the page is wrong.
            using (var http = new HttpClient { BaseAddress = new Uri(baseUrl) })
            {
                var honoured = await http.GetFromJsonAsync<IssueDto>($"/api/issues/{tree.Honoured}", Web);
                Assert.Equal(tree.HonouredParent, honoured!.PreApprovalFrom);
                Assert.Equal(tree.HonouredParent, honoured.PriorityFrom);
                Assert.Null(honoured.PreApprovalPassedOverLevel);

                var passedOver = await http.GetFromJsonAsync<IssueDto>($"/api/issues/{tree.PassedOver}", Web);
                Assert.Null(passedOver!.PreApprovalFrom);
                Assert.Equal(tree.PassedOverParent, passedOver.PreApprovalPassedOver);
                Assert.Equal("preapproved", passedOver.PreApprovalPassedOverLevel);
                Assert.Equal("expedited", passedOver.PriorityPassedOverLevel);
            }

            // The board: a card wears its marks, and the passed-over one wears dim marks of its own.
            await harness.OpenBoardAsync(deadline, baseUrl);
            await harness.WaitForVisibleAsync(deadline, $"[data-issue-key=\"{tree.PassedOver}\"] .hatch-card-marks");
            var honouredCard = await ReadAsync(harness, deadline, CardScript, tree.Honoured, "board-honoured", profileName);
            var passedCard = await ReadAsync(harness, deadline, CardScript, tree.PassedOver, "board-passedover", profileName);

            var honouredTitles = Titles(honouredCard, "marks");
            Assert.Contains($"expedited on {tree.HonouredParent}", honouredTitles);
            Assert.Contains($"pre-approved on {tree.HonouredParent}", honouredTitles);
            Assert.DoesNotContain(honouredCard.GetProperty("marks").EnumerateArray(), m => m.GetProperty("cls").GetString()!.Contains("passedover"));

            var passedMarks = passedCard.GetProperty("marks").EnumerateArray().Where(m => m.GetProperty("cls").GetString()!.Contains("passedover")).ToList();
            Assert.Equal(2, passedMarks.Count);
            Assert.Contains(passedMarks, m => m.GetProperty("title").GetString() == $"expedited on {tree.PassedOverParent}, not honoured in PXB");
            Assert.Contains(passedMarks, m => m.GetProperty("title").GetString() == $"pre-approved on {tree.PassedOverParent}, not honoured in PXB");
            AssertFits(passedCard);

            // Its peek: the same pills the issue page draws, saying the same thing. A phone
            // has no peek - a tap on a card opens the issue page - so either answers.
            await harness.ClickAsync(deadline, $"[data-issue-key=\"{tree.PassedOver}\"]");
            await harness.WaitForVisibleAsync(deadline, ".hatch-peek-priority button, .hatch-status-bar button[aria-label^=\"Priority:\"]");
            var peek = await ReadAsync(harness, deadline, PillScript, null, "peek-passedover", profileName);
            Assert.Contains(PillsOf(peek), p => p.Contains($"expedited on {tree.PassedOverParent}, not honoured in PXB"));
            Assert.Contains(PillsOf(peek), p => p.Contains($"pre-approved on {tree.PassedOverParent}, not honoured in PXB"));
            AssertFits(peek);

            // The issue page, honoured then passed over.
            await harness.GoToAsync(deadline, $"/issues/{tree.Honoured}");
            await harness.WaitForVisibleAsync(deadline, ".hatch-status-bar button[aria-label^=\"Priority:\"]");
            var honouredPage = await ReadAsync(harness, deadline, PillScript, null, "issue-honoured", profileName);
            Assert.Contains(PillsOf(honouredPage), p => p.Contains($"inherited from {tree.HonouredParent}"));
            AssertFits(honouredPage);

            await harness.GoToAsync(deadline, $"/issues/{tree.PassedOver}");
            await harness.WaitForVisibleAsync(deadline, ".hatch-status-bar button[aria-label^=\"Priority:\"]");
            var passedPage = await ReadAsync(harness, deadline, PillScript, null, "issue-passedover", profileName);
            Assert.Contains(PillsOf(passedPage), p => p.Contains($"expedited on {tree.PassedOverParent}, not honoured in PXB"));
            Assert.Contains(PillsOf(passedPage), p => p.Contains($"pre-approved on {tree.PassedOverParent}, not honoured in PXB"));
            AssertFits(passedPage);

            // The plan: each child epic wears one small mark, titled with whichever sentences apply.
            await harness.GoToAsync(deadline, "/plan");
            await harness.WaitForVisibleAsync(deadline, $".hatch-plan-title:text-is(\"{PassedOverTitle}\")");
            var plan = await ReadAsync(harness, deadline, PlanScript, null, "plan", profileName);
            var rows = plan.GetProperty("rows").EnumerateArray().ToList();
            var honouredRow = rows.Single(r => r.GetProperty("title").GetString() == HonouredTitle);
            var passedRow = rows.Single(r => r.GetProperty("title").GetString() == PassedOverTitle);
            Assert.Contains($"expedited on {tree.HonouredParent}", honouredRow.GetProperty("mark").GetString());
            Assert.Contains($"pre-approved on {tree.PassedOverParent}, not honoured in PXB", passedRow.GetProperty("mark").GetString());
            AssertFits(plan);

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

    private static string[] Titles(JsonElement read, string property) =>
        read.GetProperty(property).EnumerateArray().Select(m => m.GetProperty("title").GetString() ?? "").ToArray();

    private static string[] PillsOf(JsonElement read) =>
        read.GetProperty("pills").EnumerateArray().Select(p => p.GetString() ?? "").ToArray();

    private static void AssertFits(JsonElement read) =>
        Assert.True(
            read.GetProperty("scrollWidth").GetDouble() <= read.GetProperty("innerWidth").GetDouble() + 0.5,
            $"the page scrolls sideways: {read}");

    /// <summary>Reads the page with <paramref name="script"/> and saves the look.</summary>
    private async Task<JsonElement> ReadAsync(E2eHarness harness, Deadline deadline, string script, string? key, string label, string profileName)
    {
        var read = await harness.EvaluateAsync<JsonElement>(deadline, $"reading {label}", script, key);
        output.WriteLine($"{profileName} {label}: {read}");
        output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"{label}-{profileName}")}");
        return read;
    }

    private sealed record Tree(string HonouredParent, string Honoured, string PassedOverParent, string PassedOver);

    /// <summary>
    /// Two projects. Each of two epics in the first is set expedited and
    /// pre-approved through the API, the way a person sets them with the wall
    /// off; a child epic in the second hangs under each. The second parent's
    /// setters are then cleared in the database, which is how a level set
    /// before setters were recorded reads: it stays inside its own project.
    /// </summary>
    private static async Task<Tree> FileTreeAsync(string baseUrl, IServiceProvider services)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var first = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("PXA", "PXA"));
        var second = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("PXB", "PXB"));

        var keep = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(first.Id, "epic", "The parent whose level is honoured", null, null, null, null));
        var drop = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(first.Id, "epic", "The parent whose level is not", null, null, null, null));

        foreach (var parent in new[] { keep, drop })
        {
            await PutAsync(http, $"/api/issues/{parent.Key}/priority", new PriorityRequest("expedited"));
            await PutAsync(http, $"/api/issues/{parent.Key}/pre-approval", new PreApprovalRequest("preapproved"));
        }

        var honoured = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(second.Id, "epic", HonouredTitle, null, keep.Key, null, null));
        var passedOver = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(second.Id, "epic", PassedOverTitle, null, drop.Key, null, null));

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
        var dropNumber = int.Parse(drop.Key[(drop.Key.LastIndexOf('-') + 1)..]);
        await db.Issues.Where(x => x.ProjectId == first.Id && x.Number == dropNumber).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.PreApprovalSetBy, (Guid?)null)
            .SetProperty(x => x.PrioritySetBy, (Guid?)null));

        return new Tree(keep.Key, honoured.Key, drop.Key, passedOver.Key);
    }

    private static async Task PutAsync<TRequest>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PutAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"PUT {path} answered {(int)response.StatusCode}: {text}");
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

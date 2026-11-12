using System.Net.Http.Json;
using System.Text.Json;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// The attention panel's <em>File a bug?</em> dialog, rendered with a failing
/// trunk bound to a project: Priority and Pre-approval on one
/// <c>.hatch-field-pair</c> row, opening on Expedited and Express, and
/// Pre-approved pressable for an owner of the bound project and locked, with
/// the permission sentence, for a caller who is no member of it.
///
/// <para>The wall is off, so the caller is the local person. A project filed
/// through <c>POST /api/hatch/projects</c> makes that person its owner; one
/// seeded straight into <see cref="HatchContext"/> has no member row, so the
/// caller may not approve there. Each case then files the bug and reads back
/// the pre-approval level it was born at.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class TrunkBugDialogPreApprovalTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Canonical = "forge.example/owner/repo";

    private const string Modal = ".hatch-modal__panel";

    private const string PreApprovalPill = $"{Modal} .hatch-preapproval-pill-press";

    private const string PreApprovedStep = $"{Modal} .hatch-preapproval-band .hatch-preapproval-step.preapproved";

    /// <summary>Runs in the page: the dialog's field labels, the paired row's two labels and their boxes, and each pill's words.</summary>
    private const string MeasureScript = """
        () => {
          const panel = document.querySelector('.hatch-modal__panel');
          const box = (el) => {
            const r = el.getBoundingClientRect();
            return { top: r.top, left: r.left, width: r.width };
          };
          const fields = [...panel.querySelectorAll('.hatch-field-pair > .hatch-field')];
          return {
            labels: [...panel.querySelectorAll('.hatch-field__label')].map((l) => l.textContent),
            pairLabels: fields.map((f) => f.querySelector('.hatch-field__label').textContent),
            pairBoxes: fields.map(box),
            pills: fields.map((f) => f.querySelector('button').textContent.trim()),
          };
        }
        """;

    /// <summary>Runs in the page: whether the Pre-approved step is disabled, and its title.</summary>
    private const string StepScript = """
        () => {
          const step = document.querySelector('.hatch-modal__panel .hatch-preapproval-step.preapproved');
          return { disabled: step.disabled, title: step.title };
        }
        """;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task AnOwner_CanPressPreApproved_AndTheBugIsFiledAtThatLevel() =>
        RunAsync("TrunkBugDialogOwner", owner: true);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task ANonMember_SeesPreApprovedLocked_AndTheDefaultFilesExpress() =>
        RunAsync("TrunkBugDialogNonMember", owner: false);

    private async Task RunAsync(string name, bool owner)
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness(name);
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            await SeedBoardAsync(harness.Services);
            if (owner) await FileOwnedProjectAsync(baseUrl, "OWN");
            await SeedProjectAndFailingTrunkAsync(harness.Services, owner);

            await harness.OpenBoardAsync(deadline, baseUrl);
            await harness.WaitForTopBarAsync(deadline);
            await harness.ClickAsync(deadline, E2eHarness.AttentionSelector);
            await harness.ClickAsync(deadline, ".hatch-attention-row-actions button:text-is(\"File a bug\")");
            await harness.WaitForVisibleAsync(deadline, $"{Modal} .hatch-field-pair");

            var shown = await harness.EvaluateAsync<JsonElement>(deadline, "measuring the dialog", MeasureScript);
            output.WriteLine(shown.ToString());
            await harness.ScreenshotAsync(deadline, $"{name}-open");

            Assert.Equal(["Priority", "Pre-approval"], Strings(shown, "pairLabels"));
            var boxes = shown.GetProperty("pairBoxes").EnumerateArray().ToArray();
            Assert.InRange(Math.Abs(boxes[0].GetProperty("top").GetDouble() - boxes[1].GetProperty("top").GetDouble()), 0, 1);
            Assert.True(boxes[1].GetProperty("left").GetDouble() > boxes[0].GetProperty("left").GetDouble(), $"Pre-approval is not to the right of Priority: {shown}");
            Assert.Contains("Expedited", Strings(shown, "pills")[0]);
            Assert.Contains("Express", Strings(shown, "pills")[1]);

            // The pill's own band, to see whether Pre-approved is pressable here.
            await harness.ClickAsync(deadline, PreApprovalPill);
            await harness.WaitForVisibleAsync(deadline, PreApprovedStep);
            var step = await harness.EvaluateAsync<JsonElement>(deadline, "reading the Pre-approved step", StepScript);
            output.WriteLine(step.ToString());
            await harness.ScreenshotAsync(deadline, $"{name}-band");

            if (owner)
            {
                Assert.False(step.GetProperty("disabled").GetBoolean(), $"an owner cannot press Pre-approved: {step}");
                await harness.ClickAsync(deadline, PreApprovedStep);
                await harness.WaitForVisibleAsync(deadline, $"{PreApprovalPill}:has-text(\"Pre-approved\")");
            }
            else
            {
                Assert.True(step.GetProperty("disabled").GetBoolean(), $"a non-member can press Pre-approved: {step}");
                Assert.Contains("owner or approver", step.GetProperty("title").GetString());
                await harness.ClickAsync(deadline, PreApprovalPill);
            }

            await harness.ClickAsync(deadline, $"{Modal} button:text-is(\"File it\")");
            await harness.WaitForHiddenAsync(deadline, Modal);

            await harness.ScreenshotAsync(deadline, $"{name}-filed");
            Assert.Equal(owner ? PreApprovalLevels.PreApproved : PreApprovalLevels.Express, await FiledBugLevelAsync(harness.Services));

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

    private static List<string> Strings(JsonElement m, string property) =>
        m.GetProperty(property).EnumerateArray().Select(l => l.GetString()!).ToList();

    /// <summary>
    /// A column the bug can be sent back to: the scratch database's own board
    /// flags no implementation column, so <c>doing</c> is one, and the column
    /// before it is where a bug is filed.
    /// </summary>
    private static async Task SeedBoardAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();

        db.AddRange(
            new EfHatchStatus { Name = "todo-e2e", SortOrder = 1000 },
            new EfHatchStatus { Name = "doing-e2e", SortOrder = 1010, IsImplementation = true });
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

    /// <summary>
    /// Binds the repository to the owner's project, or to a project seeded with
    /// no member row, and records its trunk as failing.
    /// </summary>
    private static async Task SeedProjectAndFailingTrunkAsync(IServiceProvider services, bool owner)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
        var now = DateTimeOffset.UtcNow;

        var project = owner
            ? await db.Projects.SingleAsync(p => p.Key == "OWN")
            : db.Projects.Add(new EfHatchProject { Key = "PLN", Name = "Plain", CreatedAt = now }).Entity;
        await db.SaveChangesAsync();

        db.ProjectRepositories.Add(new EfHatchProjectRepository
        {
            ProjectId = project.Id, Remote = $"https://{Canonical}", Canonical = Canonical, SortOrder = 0, CreatedAt = now,
        });
        db.TrunkBuilds.Add(new EfHatchTrunkBuild
        {
            Remote = Canonical,
            Canonical = Canonical,
            Trunk = "main",
            Sha = new string('2', 40),
            ShaSince = now,
            Verdict = BuildVerdicts.Failed,
            Failing = EfHatchBuildCheck.WriteFailing([new FailingCheckDto("CI")]),
            CheckedAt = now,
            Runner = "box:/work/repo",
            CheckedBy = "runner",
        });
        await db.SaveChangesAsync();
    }

    /// <summary>The pre-approval level the one bug was born at, read once it exists: the row is polled for briefly rather than read once.</summary>
    private static async Task<int> FiledBugLevelAsync(IServiceProvider services)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
            var bugs = await db.Issues.AsNoTracking().Where(i => i.Type == "bug").ToListAsync();
            if (bugs.Count == 1) return bugs[0].PreApproval;
            Assert.True(DateTime.UtcNow < until && bugs.Count == 0, $"expected one bug, found {bugs.Count}");
            await Task.Delay(250);
        }
    }
}

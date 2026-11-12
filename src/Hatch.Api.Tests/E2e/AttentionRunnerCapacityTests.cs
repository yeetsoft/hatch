using Hatch.Api.Tests.Hatch;
using Microsoft.Playwright;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2099's acceptance, rendered: with an attention answer whose only
/// non-empty list is <c>RunnerCapacity</c>, the bar's attention panel's Human
/// group shows a <em>Runner capacity</em> section holding the row, its
/// sentence, and a link for each of its issue keys. With every list empty,
/// the section shows its own empty sentence and no rows. Run at the desktop
/// viewport in Chromium and at the <c>iPhone 14 Pro</c> profile in WebKit.
///
/// <para>Two more cases, desktop Chromium only, prove the queue modal's own
/// sentences: a one-taker and a no-taker <c>QueueEntryDto</c> stubbed on
/// <c>/api/work/queue</c>, opened from the board's queue control.</para>
///
/// <para>A scratch database has no runner and no project whose steps a live
/// runner could be short on, so both reads are fulfilled with
/// <c>page.RouteAsync</c>, serialised from the real record so a renamed field
/// breaks this build rather than the panel. Every other route, and the
/// bundle, is real. Each look is saved as a PNG beside the case's other
/// artifacts.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class AttentionRunnerCapacityTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string EmptySentence = "Every project's next steps have two or more live runners.";

    private const string PanelSelector = ".hatch-attention-panel";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly RunnerCapacityDto Row = new(
        "E2E", 2, ["E2E-30", "E2E-31"], ["Nathan-PC"], []);

    private const string RowWords = "2 clear steps: only Nathan-PC can take them";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task ARunnerCapacityRow_IsListed_AtTheDesktopProfile() =>
        RunAsync("RunnerCapacityDesktop", "desktop", null, hasRow: true);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task NoRunnerCapacityRow_SaysSo_AtTheDesktopProfile() =>
        RunAsync("RunnerCapacityClearDesktop", "desktop", null, hasRow: false);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task ARunnerCapacityRow_IsListed_AtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("RunnerCapacityPhone", "phone", DeviceProfile.IPhone14Pro, hasRow: true);
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task NoRunnerCapacityRow_SaysSo_AtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("RunnerCapacityClearPhone", "phone", DeviceProfile.IPhone14Pro, hasRow: false);
    }

    private async Task RunAsync(string name, string profileName, DeviceProfile? profile, bool hasRow)
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

            await harness.OpenAsync(deadline, baseUrl, "/", context => StubAttention(context, hasRow));
            await harness.WaitForTopBarAsync(deadline);

            await harness.ClickAsync(deadline, E2eHarness.AttentionSelector);
            // With no row every Human section is empty, so HA-2193 folds the
            // whole group behind its own heading button - open it first.
            if (!hasRow)
                await harness.ClickAsync(deadline, $"{PanelSelector} h2 button.hatch-attention-fold:text-is(\"Human\")");
            // :has-text, not :text-is - HA-2192 folds an empty, factless section
            // behind its own heading button, and the button's own text is the
            // smallest exact match then, not the h3 around it.
            await harness.WaitForVisibleAsync(deadline, $"{PanelSelector} h3:has-text(\"Runner capacity\")");

            if (hasRow)
            {
                await harness.WaitForVisibleAsync(
                    deadline,
                    $"{PanelSelector} .hatch-attention-row-plain:has(.hatch-attention-key:text-is(\"{Row.ProjectKey}\")):has-text(\"{RowWords}\")");
                // The router's own basename lands in front of the path, so the
                // match is on the suffix - see lib/basename.ts.
                foreach (var key in Row.Keys)
                    await harness.WaitForVisibleAsync(deadline, $"{PanelSelector} a[href$=\"/issues/{key}\"]");
                Assert.False(await harness.IsVisibleAsync(deadline, $"{PanelSelector} .hatch-attention-empty:text-is(\"{EmptySentence}\")"));
            }
            else
            {
                // Empty and factless, so HA-2192 folds it behind its own
                // heading button - open it before looking for its sentence.
                await harness.ClickAsync(deadline, $"{PanelSelector} h3:has-text(\"Runner capacity\") button.hatch-attention-fold");
                await harness.WaitForVisibleAsync(deadline, $"{PanelSelector} .hatch-attention-empty:text-is(\"{EmptySentence}\")");
                Assert.False(await harness.IsVisibleAsync(deadline, $"{PanelSelector} .hatch-attention-key:text-is(\"{Row.ProjectKey}\")"));
            }

            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"{(hasRow ? "capacity" : "clear")}-{profileName}")}");

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

    /// <summary>The attention read with the one row, or with every list empty.</summary>
    private static Task StubAttention(IBrowserContext context, bool hasRow)
    {
        var attention = new AttentionDto(
            Reviews: [],
            InReviewWithoutPullRequest: 0,
            Questions: [],
            Conflicts: [],
            RunnerCapacity: hasRow ? [Row] : []);

        return context.RouteAsync("**/api/attention", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(attention, Web),
        }));
    }

    // ---- The queue modal's own sentences, desktop Chromium only ----

    private const int QueueCaseBudgetSeconds = 90;

    private const int QueueCaseTimeoutMs = (QueueCaseBudgetSeconds + 10) * 1000;

    private static readonly StatusDto FromStatus = new(0, "To Do", 0, false, false, false, "#888888");

    private static IssueDto MinimalIssue(string key) => new(
        Key: key,
        ProjectId: 0,
        ProjectKey: "",
        Type: "task",
        Title: "",
        Description: "",
        StatusId: 0,
        Rank: 0,
        ParentKey: null,
        ChildKeys: [],
        DependsOnKeys: [],
        DependentKeys: [],
        ReadyAt: null,
        DueAt: null,
        PullRequestUrl: null,
        ModelOverride: null,
        EffortOverride: null,
        Assignee: null,
        CreatedBy: "",
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow);

    [SkippableFact(Timeout = QueueCaseTimeoutMs)]
    public async Task TheQueueModal_ShowsBothTakerSentences_AtTheDesktopProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("RunnerCapacityQueueModal");
        var deadline = new Deadline(TimeSpan.FromSeconds(QueueCaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);

            await harness.OpenAsync(deadline, baseUrl, "/", StubQueue);
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForFirstColumnAsync(deadline);

            await harness.ClickAsync(deadline, ".hatch-queue-control");
            await harness.WaitForVisibleAsync(deadline, "[role=\"dialog\"]:has-text(\"The work queue\")");

            await harness.WaitForVisibleAsync(deadline, ".hatch-queue-words:has-text(\"only Nathan-PC can take this\")");
            await harness.WaitForVisibleAsync(deadline, ".hatch-queue-words:has-text(\"no live runner can take this\")");

            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, "queue-modal-desktop")}");

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

    /// <summary>The queue read with a one-taker row and a no-taker row.</summary>
    private static Task StubQueue(IBrowserContext context)
    {
        var queue = new[]
        {
            new QueueEntryDto(MinimalIssue("E2E-40"), FromStatus, null, Blocked: null, Takers: ["Nathan-PC"], Untaken: []),
            new QueueEntryDto(MinimalIssue("E2E-41"), FromStatus, null, Blocked: null, Takers: [], Untaken: [new RunnerReasonDto("Nathan-PC", "out of usage")]),
        };

        return context.RouteAsync("**/api/work/queue**", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(queue, Web),
        }));
    }
}

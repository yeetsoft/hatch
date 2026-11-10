using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.Playwright;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2122's acceptance, rendered: on the Playbooks page a row whose model no
/// live runner offers carries a danger badge holding its
/// <c>UnreachableReason</c> sentence, and a reachable row carries none; the
/// same badge is drawn for a row of a repo's trunk file. Run at the desktop
/// viewport in Chromium and at the <c>iPhone 14 Pro</c> profile in WebKit.
///
/// <para>A scratch database has no repo-sourced project and no runner, so no
/// row could be unreachable: the playbook list and the trunk-file read are
/// fulfilled with <c>context.RouteAsync</c>, serialised from the real records
/// so a renamed field breaks this build rather than the page. The two rows'
/// columns are real ones, read from the board before the page opens, so each
/// draws a real status pill. Every other route, and the bundle, is real. Each
/// look is saved as a PNG beside the case's other artifacts.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class PlaybooksUnreachableRowTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Reason = "No runner offers this model, so nothing can pick it up.";

    private const string FileReason = "The trunk's copy names a model no runner offers.";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task AnUnreachableRow_IsBadgedWithItsSentence_AtTheDesktopProfile() =>
        RunAsync("PlaybooksUnreachableDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task AnUnreachableRow_IsBadgedWithItsSentence_AtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("PlaybooksUnreachablePhone", "phone", DeviceProfile.IPhone14Pro);
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

            using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
            var board = await http.GetFromJsonAsync<BoardDto>("/api/hatch/board", Web);
            var columns = board!.Statuses.Where(s => !s.IsDeferred && !s.IsTerminal).OrderBy(s => s.SortOrder).ToList();
            Assert.True(columns.Count >= 3, $"The board has {columns.Count} live columns; the case needs three to draw two moves between them.");

            await harness.OpenAsync(
                deadline, baseUrl, "/apps/hatch/playbooks", context => StubPlaybooks(context, columns[0], columns[1], columns[2]));

            await harness.WaitForVisibleAsync(deadline, "tbody tr:has-text(\"fable\")");
            await harness.WaitForVisibleAsync(deadline, "tbody tr:has-text(\"sonnet\")");

            await harness.WaitForVisibleAsync(deadline, $"tr:has(.hatch-badge--danger:text-is(\"{Reason}\")):has-text(\"fable\")");
            Assert.False(await harness.IsVisibleAsync(deadline, "tr:has-text(\"sonnet\") .hatch-badge--danger"));

            await harness.WaitForVisibleAsync(deadline, $".hatch-badge--danger:text-is(\"{FileReason}\")");

            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"playbooks-{profileName}")}");

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
    /// The playbook list with an unreachable row (<c>fable</c>, first column to second) and a reachable one
    /// (<c>sonnet</c>, second to third), and the trunk-file read with one parsed file whose one row is unreachable.
    /// </summary>
    private static async Task StubPlaybooks(IBrowserContext context, StatusDto first, StatusDto second, StatusDto third)
    {
        var playbooks = new List<PlaybookDto>
        {
            Playbook(9001, first, second, "fable", Reason),
            Playbook(9002, second, third, "sonnet", null),
        };

        var files = new PlaybookFilesDto(
            ".hatch/playbooks.yaml",
            [
                new PlaybookFileDto(
                    Id: 9101,
                    Remote: "git@example-host.invalid:e2e/playbooks.git",
                    Canonical: "example-host.invalid/e2e/playbooks",
                    Trunk: "main",
                    Sha: "0123456789abcdef0123456789abcdef01234567",
                    State: "parsed",
                    Hash: null,
                    Rows:
                    [
                        new PlaybookFileRowDto(
                            FromStatusId: first.Id,
                            FromStatusName: first.Name,
                            ToStatusId: second.Id,
                            ToStatusName: second.Name,
                            Types: [],
                            Shape: "any",
                            Prompt: "e2e",
                            Model: "haiku",
                            Effort: "medium",
                            Budget: null,
                            BriefLimit: null,
                            DeniedTools: [],
                            IsCheck: false,
                            UnreachableReason: FileReason),
                    ],
                    Problem: null,
                    LastGoodSha: null,
                    LastGoodHash: null,
                    Url: null,
                    CheckedAt: DateTimeOffset.UtcNow,
                    Runner: "e2e-runner",
                    CheckedBy: "e2e-runner"),
            ]);

        await context.RouteAsync("**/api/hatch/playbooks", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(playbooks, Web),
        }));

        await context.RouteAsync("**/api/hatch/playbook-files", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(files, Web),
        }));
    }

    private static PlaybookDto Playbook(int id, StatusDto from, StatusDto to, string model, string? unreachableReason) => new(
        Id: id,
        FromStatusId: from.Id,
        FromStatusName: from.Name,
        ToStatusId: to.Id,
        ToStatusName: to.Name,
        Types: [],
        Shape: "any",
        Prompt: "e2e",
        DefaultPrompt: null,
        Model: model,
        Effort: "medium",
        Budget: null,
        BriefLimit: null,
        DeniedTools: [],
        IsCheck: false,
        UpdatedAt: DateTimeOffset.UtcNow,
        ProjectId: null,
        ProjectKey: null,
        ProjectName: null,
        UnreachableReason: unreachableReason);
}

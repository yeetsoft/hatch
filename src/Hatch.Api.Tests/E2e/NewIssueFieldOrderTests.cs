using System.Text.Json;
using Hatch.Api.Tests.Hatch;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// The New issue dialog's field order and its paired row, rendered: Project,
/// Type, One pull request (a story or a bug), Priority and Pre-approval on one
/// <c>.hatch-field-pair</c> row, Title, Parent, Description, Ready, Due. At a
/// desktop width the two levels share a row at half the form's width each; at
/// <c>iPhone 14 Pro</c> they stack, one per row. Title holds focus on open. The
/// phone case is skipped where WebKit is frozen, the same as
/// <see cref="TypesOnlyFitsPhoneTests"/>.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class NewIssueFieldOrderTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 60;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    /// <summary>Sub-pixel layout is fractional; two boxes within a pixel of each other are the same size.</summary>
    private const double Tolerance = 1;

    private const string ModalPanel = ".hatch-modal__panel";

    private const string TypeSelect = ".hatch-modal__panel .hatch-field:has(> .hatch-field__control > .hatch-field__label:text-is(\"Type\")) select";

    /// <summary>
    /// Runs in the page: the dialog's field labels in document order, the paired
    /// row's box and column gap, each level's box, and what holds focus.
    /// </summary>
    private const string MeasureScript = """
        () => {
          const panel = document.querySelector('.hatch-modal__panel');
          const box = (el) => {
            const r = el.getBoundingClientRect();
            return { top: r.top, bottom: r.bottom, left: r.left, width: r.width };
          };
          const pair = panel.querySelector('.hatch-field-pair');
          const fields = [...pair.querySelectorAll(':scope > .hatch-field')];
          const active = document.activeElement;
          return {
            labels: [...panel.querySelectorAll('.hatch-field__label')].map((l) => l.textContent),
            pair: box(pair),
            gap: parseFloat(getComputedStyle(pair).columnGap),
            fields: fields.map(box),
            fieldLabels: fields.map((f) => f.querySelector('.hatch-field__label').textContent),
            focusTag: active.tagName,
            focusInTitle: active.closest('.hatch-field')?.querySelector('.hatch-field__label')?.textContent ?? null,
            scrollWidth: document.documentElement.scrollWidth,
            innerWidth: window.innerWidth,
          };
        }
        """;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheLevels_ShareOneRowAtHalfWidthEachAtADesktopWidth() =>
        RunAsync("NewIssueFieldOrderDesktop", profile: null, stacked: false);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheLevels_StackOnePerRowAtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("NewIssueFieldOrderPhone", DeviceProfile.IPhone14Pro, stacked: true);
    }

    private async Task RunAsync(string name, DeviceProfile? profile, bool stacked)
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

            await harness.OpenBoardAsync(deadline, baseUrl);
            await harness.WaitForFirstColumnAsync(deadline);
            await harness.ClickAsync(deadline, ".hatch-board-new");
            await harness.WaitForVisibleAsync(deadline, ModalPanel);
            await harness.WaitForVisibleAsync(deadline, ".hatch-field-pair");

            // A task, as the dialog opens: no One pull request.
            var task = await harness.EvaluateAsync<JsonElement>(deadline, "measuring the dialog as a task", MeasureScript);
            output.WriteLine(task.ToString());
            await harness.ScreenshotAsync(deadline, $"{name}-task");
            AssertOrder(task, "task", ["Type", "Priority", "Pre-approval", "Title", "Parent", "Description", "Ready", "Due"]);
            Assert.DoesNotContain("One pull request", Labels(task));
            AssertPair(task, stacked);
            Assert.Equal("Title", task.GetProperty("focusInTitle").GetString());
            Assert.Equal("INPUT", task.GetProperty("focusTag").GetString());

            // A story: One pull request sits right after Type, before the levels.
            await harness.SelectOptionAsync(deadline, TypeSelect, "story");
            await harness.WaitForVisibleAsync(deadline, ".hatch-modal__panel .hatch-one-pr-steps");
            var story = await harness.EvaluateAsync<JsonElement>(deadline, "measuring the dialog as a story", MeasureScript);
            output.WriteLine(story.ToString());
            await harness.ScreenshotAsync(deadline, $"{name}-story");
            AssertOrder(story, "story", ["Type", "One pull request", "Priority", "Pre-approval", "Title", "Parent", "Description", "Ready", "Due"]);
            AssertPair(story, stacked);

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

    private static List<string> Labels(JsonElement m) =>
        m.GetProperty("labels").EnumerateArray().Select(l => l.GetString()!).ToList();

    /// <summary>The expected labels appear in the dialog in this order, with nothing from the list missing.</summary>
    private static void AssertOrder(JsonElement m, string type, string[] expected)
    {
        var labels = Labels(m);
        var at = expected.Select(l => labels.IndexOf(l)).ToArray();
        Assert.True(at.All(i => i >= 0), $"a {type}'s dialog lacks one of {string.Join(", ", expected)}: {string.Join(", ", labels)}");
        Assert.True(at.SequenceEqual(at.Order()), $"a {type}'s fields are out of order: {string.Join(", ", labels)}");
        if (expected.Contains("One pull request"))
            Assert.Equal(labels.IndexOf("Type") + 1, labels.IndexOf("One pull request"));
    }

    /// <summary>Side by side at half each on a wide dialog; one under the other, each the full width, on a narrow one.</summary>
    private static void AssertPair(JsonElement m, bool stacked)
    {
        var pair = m.GetProperty("pair");
        var gap = m.GetProperty("gap").GetDouble();
        var fields = m.GetProperty("fields").EnumerateArray().ToArray();
        var labels = m.GetProperty("fieldLabels").EnumerateArray().Select(l => l.GetString()).ToArray();
        Assert.Equal(["Priority", "Pre-approval"], labels);
        Assert.Equal(2, fields.Length);

        var width = pair.GetProperty("width").GetDouble();
        var (a, b) = (fields[0], fields[1]);
        var detail = m.ToString();

        if (stacked)
        {
            Assert.True(b.GetProperty("top").GetDouble() >= a.GetProperty("bottom").GetDouble() - Tolerance, $"the levels share a row at a phone width: {detail}");
            Assert.InRange(a.GetProperty("width").GetDouble(), width - Tolerance, width + Tolerance);
            Assert.InRange(b.GetProperty("width").GetDouble(), width - Tolerance, width + Tolerance);
        }
        else
        {
            var half = (width - gap) / 2;
            Assert.InRange(Math.Abs(a.GetProperty("top").GetDouble() - b.GetProperty("top").GetDouble()), 0, Tolerance);
            Assert.InRange(a.GetProperty("width").GetDouble(), half - Tolerance, half + Tolerance);
            Assert.InRange(b.GetProperty("width").GetDouble(), half - Tolerance, half + Tolerance);
            Assert.True(b.GetProperty("left").GetDouble() > a.GetProperty("left").GetDouble(), $"Pre-approval is not to the right of Priority: {detail}");
        }

        Assert.True(m.GetProperty("scrollWidth").GetDouble() <= m.GetProperty("innerWidth").GetDouble() + Tolerance, $"the page scrolls sideways: {detail}");
    }
}

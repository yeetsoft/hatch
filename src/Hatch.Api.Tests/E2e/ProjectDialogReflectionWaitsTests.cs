using System.Net.Http.Json;
using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2217's line under the reflection cadence box, rendered: the owner's
/// project dialog in headless Chromium at 1280x800 against the real API serving
/// the built bundle. It is read from the draft, so it comes and goes as the
/// cadence box and the no-code checkbox change, before anything is saved: a
/// cadence typed with the setting off shows it, ticking the setting or blanking
/// the box takes it away, and unticking brings it back.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class ProjectDialogReflectionWaitsTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string ProjectsPath = "/apps/hatch/projects";

    private const string CloseLabel = "A no-code declaration closes the ticket";

    private const string Wording = "will wait in review for a person";

    /// <summary>Runs against the dialog: how many hint paragraphs carry the line, and the text of the first.</summary>
    private const string LineScript =
        "(dialog, wording) => { const lines = Array.from(dialog.querySelectorAll('p.hatch-section-hint')).filter(p => p.textContent.includes(wording)); return [lines.length, lines.length ? lines[0].textContent.trim() : '']; }";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheOwnersDialog_SaysReflectionsWillWait_OnlyWhileACadenceIsSetAndTheSettingIsOff()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("ProjectDialogReflectionWaits");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            await deadline.BoundAsync("filing a project", () => FileOwnedProjectAsync(baseUrl, "RFW"));

            await harness.OpenAsync(deadline, baseUrl, ProjectsPath);

            var dialog = "[role=dialog]:has(h3:text-is(\"Edit RFW\"))";
            await harness.ClickAsync(deadline, "tr:has(code:text-is(\"RFW\")) button:text-is(\"Edit\")");
            await harness.WaitForVisibleAsync(deadline, dialog);

            var cadenceBox = $"{dialog} label:has-text(\"Reflects every\") input";
            var closeBox = $"{dialog} label:has(input[type=checkbox]):text-is(\"{CloseLabel}\") input[type=checkbox]";

            var violations = new List<string>();

            await ExpectAsync(harness, deadline, dialog, "blank cadence, setting off", shown: false, violations);

            await ClickAndTypeAsync(harness, deadline, cadenceBox, "5");
            await ExpectAsync(harness, deadline, dialog, "cadence 5, setting off", shown: true, violations);

            await harness.ClickAsync(deadline, closeBox);
            await ExpectAsync(harness, deadline, dialog, "cadence 5, setting ticked", shown: false, violations);

            await harness.ClickAsync(deadline, closeBox);
            await ExpectAsync(harness, deadline, dialog, "cadence 5, setting unticked again", shown: true, violations);

            await ClickAndTypeAsync(harness, deadline, cadenceBox, "0");
            await ExpectAsync(harness, deadline, dialog, "cadence 0, setting off", shown: false, violations);

            Assert.True(
                violations.Count == 0,
                $"The cadence line does not come and go as asked ({violations.Count} violation(s)):{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");

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

    /// <summary>Replaces what the box holds with <paramref name="text"/>, firing the events a person's typing would.</summary>
    private static async Task ClickAndTypeAsync(E2eHarness harness, Deadline deadline, string box, string text)
    {
        await harness.ClickAsync(deadline, box);
        await harness.SelectAllAsync(deadline);
        await harness.InsertTextAsync(deadline, text);
    }

    /// <summary>Reads the line in the open dialog, scrolls the cadence box into view, takes the screenshot, and records a violation if the line is not as asked.</summary>
    private async Task ExpectAsync(
        E2eHarness harness, Deadline deadline, string dialog, string state, bool shown, List<string> violations)
    {
        var line = await harness.EvaluateOnAsync<object[]>(deadline, $"reading the line, {state}", dialog, LineScript, Wording);
        var count = Convert.ToInt32(line[0]);
        var text = line[1]?.ToString() ?? "";
        await harness.EvaluateOnAsync<bool>(
            deadline, "scrolling the cadence box into view", $"{dialog} label:has-text(\"Reflects every\")",
            "(el) => { el.scrollIntoView({ block: 'center' }); return true; }");
        var path = await harness.ScreenshotAsync(deadline, $"reflection-waits-{state.Replace(", ", "-").Replace(' ', '-')}");
        output.WriteLine($"--- {state}: {count} line(s) \"{text}\", screenshot {path}");

        if (shown && count != 1)
            violations.Add($"{state}: expected the line once, found {count}");
        if (shown && count == 1 && !text.Contains(CloseLabel, StringComparison.Ordinal))
            violations.Add($"{state}: the line does not name the setting \"{CloseLabel}\": \"{text}\"");
        if (!shown && count != 0)
            violations.Add($"{state}: expected no line, found {count}");
    }

    /// <summary>A project filed the way any caller files one with the wall off: the local person is who files it, which makes the local person its owner.</summary>
    private static async Task FileOwnedProjectAsync(string baseUrl, string key)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        using var response = await http.PostAsJsonAsync("/api/hatch/projects", new ProjectCreateRequest(key, $"Project {key}"));
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST /api/hatch/projects answered {(int)response.StatusCode}: {text}");
    }
}

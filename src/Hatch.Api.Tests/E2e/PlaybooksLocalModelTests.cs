using Hatch.Api.Tests.Hatch;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2408's acceptance, rendered: on the Playbooks page an operator opens a row,
/// types a local model, saves, and the row shows it - still there after a reload;
/// clearing the field and saving takes it off the row; an alias is refused with
/// the server's sentence and the dialog stays open. Run against the real API and
/// the seeded rows at the desktop viewport in Chromium, with each look saved as a
/// PNG beside the case's other artifacts.
///
/// <para>The id is made up: no model id ships in the repository.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class PlaybooksLocalModelTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 120;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string LocalModelInput = ".hatch-field:has(.hatch-field__label:text-is(\"Local model\")) input";

    private const string Open = "tbody tr .hatch-playbook-row-open";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task ALocalModel_IsSetShownKeptOnReloadClearedAndAnAliasRefused_AtTheDesktopProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("PlaybooksLocalModel");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);

            await harness.OpenAsync(deadline, baseUrl, "/playbooks");
            await harness.WaitForVisibleAsync(deadline, Open);

            // A seeded row has none.
            Assert.False(await harness.IsVisibleAsync(deadline, "text=local: "));

            await harness.ClickAsync(deadline, Open);
            await harness.WaitForVisibleAsync(deadline, LocalModelInput);
            await harness.SetValuePastMaxLengthAsync(deadline, LocalModelInput, "fake:1b");
            await harness.ClickAsync(deadline, "button:text-is(\"Save\")");
            await harness.WaitForVisibleAsync(deadline, "text=local: fake:1b");
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, "playbooks-local-model-set")}");

            // It is stored, not only drawn.
            await harness.GoToAsync(deadline, "/playbooks");
            await harness.WaitForVisibleAsync(deadline, "text=local: fake:1b");

            // An alias is refused, and the dialog stays open on the sentence.
            await harness.ClickAsync(deadline, Open);
            await harness.WaitForVisibleAsync(deadline, LocalModelInput);
            await harness.SetValuePastMaxLengthAsync(deadline, LocalModelInput, "sonnet");
            await harness.ClickAsync(deadline, "button:text-is(\"Save\")");
            await harness.WaitForVisibleAsync(deadline, ".text-danger:has-text(\"not an alias or another agent's id\")");
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, "playbooks-local-model-refused")}");

            // Clearing it and saving removes it.
            await harness.SetValuePastMaxLengthAsync(deadline, LocalModelInput, "");
            await harness.ClickAsync(deadline, "button:text-is(\"Save\")");
            await harness.WaitForHiddenAsync(deadline, "text=local: ");

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
}

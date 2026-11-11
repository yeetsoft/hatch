using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.Playwright;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2159: the one Runners page state a local browser session never draws on
/// its own - the admin's create flow through the secret panel -
/// rendered by stubbing <c>/api/hatch/me</c>, the way
/// <c>SettingsTokenBudgetTests</c> renders its own admin card. The
/// <c>POST /api/hatch/runners</c> the dialog makes is real; only the caller's
/// own identity is stubbed.
///
/// <para>The dialog's own <c>getAuthMe</c> call, defaulting Owner to the
/// caller, is not stubbed here on purpose: against a scratch host with no
/// signed-in grant, <c>/api/auth/me</c> genuinely answers 401, which is
/// exactly the wall-off shape <c>getAuthMe</c> is written to read as "no
/// grant" rather than as a refusal to redirect away from. A regression there
/// would bounce this case to the sign-in shell before Create ever becomes
/// pressable.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class RunnersAdminCreateFlowTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheCreateFlow_ShowsTheDialogThenTheSecret()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("RunnersAdminCreateFlow");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline, null);
            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);

            await harness.OpenAsync(deadline, baseUrl, "/runners", StubMe);
            await harness.WaitForVisibleAsync(deadline, ".hatch-page-header__actions button:text-is(\"Create runner\")");

            await harness.ClickAsync(deadline, ".hatch-page-header__actions button:text-is(\"Create runner\")");
            await harness.WaitForVisibleAsync(deadline, "text=A name from the cast list");
            await harness.ScreenshotAsync(deadline, "1-create-dialog");

            await harness.ClickAsync(deadline, "button:text-is(\"Create\")");
            await harness.WaitForVisibleAsync(deadline, "text=Shown once. Copy it now");
            await harness.WaitForVisibleAsync(deadline, "text=hatch config --origin");
            await harness.WaitForVisibleAsync(deadline, "text=hatch config --key");
            var path = await harness.ScreenshotAsync(deadline, "2-secret-panel");
            output.WriteLine($"screenshot: {path}");

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

    private static async Task StubMe(IBrowserContext context)
    {
        await context.RouteAsync("**/api/hatch/me", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new MeDto("person", "Admin", "admin", true, true, false), Web),
        }));
    }
}

using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.Playwright;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2651: a signed-in <c>user</c> creates a runner from the Runners page with
/// no admin involved. The dialog has no Owner select and says the runner will
/// be theirs; Create shows the secret panel; and the new runner's card then
/// offers Rotate key, since the scratch host is wall off and reports the
/// runner as the caller's own. Rendered by stubbing <c>/api/me</c>, as
/// <see cref="RunnersAdminCreateFlowTests"/> does for an admin. The
/// <c>POST /api/runners</c> the dialog makes is real; only the caller's own
/// identity is stubbed.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class RunnersUserCreateFlowTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheUserCreateFlow_HasNoOwnerSelectAndEndsWithRotateKey() => RunCreateFlowAsync("RunnersUserCreateFlow", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheUserCreateFlow_OnAPhone_HasNoOwnerSelectAndEndsWithRotateKey() =>
        RunCreateFlowAsync("RunnersUserCreateFlowPhone", DeviceProfile.IPhone14ProChromium);

    private async Task RunCreateFlowAsync(string name, DeviceProfile? profile)
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness(name);
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline, profile);
            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);

            await harness.OpenAsync(deadline, baseUrl, "/runners", StubMe);
            await harness.WaitForVisibleAsync(deadline, ".hatch-page-header__actions button:text-is(\"Create runner\")");

            await harness.ClickAsync(deadline, ".hatch-page-header__actions button:text-is(\"Create runner\")");
            await harness.WaitForVisibleAsync(deadline, "text=A name from the cast list");

            await harness.WaitForVisibleAsync(deadline, "text=The runner will be yours");
            await harness.WaitForVisibleAsync(deadline, "text=The key sees only the projects its owner sees");
            if (await harness.IsVisibleAsync(deadline, "[role=dialog] select"))
            {
                throw new InvalidOperationException("a user's Create dialog must not draw an Owner select");
            }

            await harness.ScreenshotAsync(deadline, "1-create-dialog");

            await harness.ClickAsync(deadline, "button:text-is(\"Create\")");
            await harness.WaitForVisibleAsync(deadline, "text=Shown once. Copy it now");
            await harness.WaitForVisibleAsync(deadline, "text=hatch config --origin");
            await harness.WaitForVisibleAsync(deadline, "text=hatch config --key");
            await harness.ScreenshotAsync(deadline, "2-secret-panel");

            // The new runner's own card: Rotate key and Revoke, with the owner as text.
            await harness.WaitForVisibleAsync(deadline, "button:text-is(\"Rotate key\")");
            await harness.WaitForVisibleAsync(deadline, "button:text-is(\"Revoke\")");
            if (await harness.IsVisibleAsync(deadline, "select[aria-label^=\"Owner of\"]"))
            {
                throw new InvalidOperationException("a user's Identity card must not draw an Owner select");
            }

            var path = await harness.ScreenshotAsync(deadline, "3-identity-card");
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

    /// <summary>
    /// A runner somebody else owns: the list answers <c>yours: false</c> for it, so a
    /// user's card draws the owner as text and neither Rotate key nor Revoke.
    /// </summary>
    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task ARunnerThatIsNotTheirs_HasNeitherRotateKeyNorRevoke()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("RunnersUserNotTheirs");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline, null);
            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);

            await harness.OpenAsync(deadline, baseUrl, "/runners", async context =>
            {
                await StubMe(context);
                await context.RouteAsync(url => new Uri(url).AbsolutePath == "/api/runners", async route =>
                {
                    if (route.Request.Method != "GET")
                    {
                        await route.ContinueAsync();
                        return;
                    }

                    var response = await route.FetchAsync();
                    var runners = JsonNode.Parse(await response.TextAsync())!.AsArray();
                    foreach (var runner in runners) runner!["yours"] = false;
                    await route.FulfillAsync(new() { Response = response, Body = runners.ToJsonString() });
                });
            });
            await harness.WaitForVisibleAsync(deadline, ".hatch-page-header__actions button:text-is(\"Create runner\")");

            await harness.EvaluateAsync<bool>(deadline, "making a runner",
                "async () => (await fetch('/api/runners', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name: 'Quill' }) })).ok");
            await harness.GoToAsync(deadline, "/runners/Quill");

            await harness.WaitForVisibleAsync(deadline, "text=Identity and key");
            await harness.WaitForVisibleAsync(deadline, "text=no owner");
            if (await harness.IsVisibleAsync(deadline, "button:text-is(\"Rotate key\")")
                || await harness.IsVisibleAsync(deadline, "button:text-is(\"Revoke\")"))
            {
                throw new InvalidOperationException("a runner that is not theirs must draw neither Rotate key nor Revoke");
            }

            var path = await harness.ScreenshotAsync(deadline, "1-not-theirs");
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
        await context.RouteAsync("**/api/me", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new MeDto("person", "User", "user", true, true, false), Web),
        }));
    }
}

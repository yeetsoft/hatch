using Hatch.Api.Services.Chat;
using Hatch.Api.Tests.Chat;
using Hatch.Api.Tests.Hatch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// The Chat screen, rendered: <c>/chat</c> on the wall-off host, where the local
/// person is the admin, with a fake chat provider standing in for Slack. Two
/// workspaces are connected by typing tokens into the form, a refused token
/// shows the provider's sentence, a token is replaced, and a workspace is removed
/// only after its dialog is confirmed. At no point is a token on the page. Run at
/// the desktop profile and at <c>iPhone 14 Pro</c>, where nothing may run off the
/// screen; the page is saved as a screenshot under the case's artifact directory.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class ChatPageTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Form = "form.hatch-chat-add";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task Chat_ConnectsReplacesAndRemoves_AtTheDesktopProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        return RunAsync("ChatDesktop", profile: null);
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task Chat_ConnectsReplacesAndRemoves_AtThePhoneProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);

        return RunAsync("ChatPhone", DeviceProfile.IPhone14Pro);
    }

    private async Task RunAsync(string name, DeviceProfile? profile)
    {
        var harness = new E2eHarness(name);
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline, profile);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline, services =>
            {
                services.RemoveAll<IChatProvider>();
                services.AddSingleton<IChatProvider>(new FakeChatProvider());
            });

            await harness.OpenAsync(deadline, baseUrl, "/chat");
            await harness.WaitForTopBarAsync(deadline);

            // Nothing connected yet, and the manifest is there to copy.
            await harness.WaitForVisibleAsync(deadline, ".hatch-empty__message:text-is(\"No workspace is connected.\")");
            await harness.WaitForVisibleAsync(deadline, "pre.hatch-chat-manifest");
            Assert.Contains(
                FakeChatProvider.ManifestText,
                await harness.EvaluateAsync<string>(deadline, "reading the manifest", "() => document.querySelector('pre.hatch-chat-manifest').textContent"));
            output.WriteLine("screenshot: " + await harness.ScreenshotAsync(deadline, "chat-empty"));

            await ConnectAsync(harness, deadline, FakeChatProvider.AcmeToken);
            await harness.WaitForVisibleAsync(deadline, "td strong:text-is(\"Acme\")");
            await ConnectAsync(harness, deadline, FakeChatProvider.GlobexToken);
            await harness.WaitForVisibleAsync(deadline, "td strong:text-is(\"Globex\")");

            Assert.Equal(
                2,
                await harness.EvaluateAsync<int>(deadline, "counting the rows", "() => document.querySelectorAll('main tbody tr').length"));
            Assert.Equal(
                2,
                await harness.EvaluateAsync<int>(
                    deadline, "counting Token set",
                    "() => [...document.querySelectorAll('main tbody td')].filter(td => td.textContent === 'Token set').length"));
            await AssertNoTokenOnPageAsync(harness, deadline);

            // A token the provider refuses says the provider's sentence, adds nothing, and leaves the box empty.
            await ConnectAsync(harness, deadline, FakeChatProvider.RefusedToken);
            await harness.WaitForVisibleAsync(deadline, $"{Form} .text-danger:text-is(\"{FakeChatProvider.RefusalSentence}\")");
            Assert.Equal(
                2,
                await harness.EvaluateAsync<int>(deadline, "counting the rows", "() => document.querySelectorAll('main tbody tr').length"));
            await AssertNoTokenOnPageAsync(harness, deadline);

            // The same workspace's new token is taken.
            await deadline.BoundAsync(
                "typing the replacement token",
                () => harness.Page!.FillAsync("input[aria-label=\"New token for Acme\"]", FakeChatProvider.AcmeNewToken));
            await harness.ClickAsync(deadline, "tr:has-text(\"Acme\") button:has-text(\"Replace token\")");
            await harness.WaitForFunctionAsync(
                deadline, "waiting for the replacement box to empty",
                "() => document.querySelector('input[aria-label=\"New token for Acme\"]').value === ''");
            await AssertNoTokenOnPageAsync(harness, deadline);

            if (profile is not null)
            {
                var measured = await harness.EvaluateAsync<double[]>(
                    deadline, "measuring the page",
                    "() => [document.documentElement.scrollWidth, window.innerWidth]");
                output.WriteLine($"scrollWidth {measured[0]}, innerWidth {measured[1]}");
                Assert.True(measured[0] <= measured[1], $"the page runs off the screen: scrollWidth {measured[0]} > innerWidth {measured[1]}");
            }

            output.WriteLine("screenshot: " + await harness.ScreenshotAsync(deadline, "chat-two-workspaces"));

            // Remove asks first, names the workspace, and removes nothing until it is confirmed.
            await harness.ClickAsync(deadline, "tr:has-text(\"Globex\") button:text-is(\"Remove\")");
            await harness.WaitForVisibleAsync(deadline, "[aria-modal=\"true\"]:has-text(\"Remove Globex?\")");
            await harness.ClickAsync(deadline, "[aria-modal=\"true\"] button:text-is(\"Leave it\")");
            await harness.WaitForHiddenAsync(deadline, "[aria-modal=\"true\"]");
            Assert.True(await harness.IsVisibleAsync(deadline, "td strong:text-is(\"Globex\")"), "Globex was removed without being confirmed");

            await harness.ClickAsync(deadline, "tr:has-text(\"Globex\") button:text-is(\"Remove\")");
            await harness.WaitForVisibleAsync(deadline, "[aria-modal=\"true\"]:has-text(\"Remove Globex?\")");
            output.WriteLine("screenshot: " + await harness.ScreenshotAsync(deadline, "chat-remove-asks"));
            await harness.ClickAsync(deadline, "[aria-modal=\"true\"] button:text-is(\"Remove\")");
            await harness.WaitForHiddenAsync(deadline, "td strong:text-is(\"Globex\")");
            Assert.True(await harness.IsVisibleAsync(deadline, "td strong:text-is(\"Acme\")"), "Acme went with Globex");

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

    /// <summary>Types a token into the add form's box, the way a person would, and presses Connect. The box is empty again once the page has answered.</summary>
    private static async Task ConnectAsync(E2eHarness harness, Deadline deadline, string token)
    {
        await deadline.BoundAsync("typing the token", () => harness.Page!.FillAsync($"{Form} input[type=password]", token));
        await harness.ClickAsync(deadline, $"{Form} button:has-text(\"Connect\")");
        await harness.WaitForFunctionAsync(
            deadline, "waiting for the box to empty",
            "(form) => document.querySelector(form + ' input[type=password]').value === ''", Form);
    }

    /// <summary>No token, or part of one, is in the text of the page or in any box on it.</summary>
    private static async Task AssertNoTokenOnPageAsync(E2eHarness harness, Deadline deadline)
    {
        var shown = await harness.EvaluateAsync<string>(
            deadline, "reading everything the page shows",
            "() => document.body.innerText + ' ' + [...document.querySelectorAll('input')].map(i => i.value).join(' ') + ' ' + document.body.innerHTML");

        foreach (var token in new[]
        {
            FakeChatProvider.AcmeToken, FakeChatProvider.AcmeNewToken, FakeChatProvider.GlobexToken, FakeChatProvider.RefusedToken,
        })
        {
            Assert.DoesNotContain(token, shown);
            Assert.DoesNotContain(token["xoxb-".Length..], shown);
        }
    }
}

using Hatch.Api.Tests.Hatch;
using System.Security.Cryptography;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2748: the Settings page on a Hatch with no secret key. Beside each of
/// its three secret fields the page says, before anyone presses Save, that a
/// secret cannot be saved until <c>HATCH_SECRET_KEY</c> is set, with a link to
/// the Environment variables page; that field's Save is off even with something
/// typed. With a key set the sentence is gone and Save works as it always did.
///
/// <para>The real host, the real <c>GET /api/settings</c> - nothing stubbed - so
/// <c>canSaveSecrets</c> is what the API answers for the key the host was
/// started with. Run in Chromium at the desktop profile and on the
/// <c>iPhone 14 Pro</c> profile, where nothing may scroll sideways. The
/// screenshot is taken before the assertions, so a failing case still leaves
/// its picture.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class SettingsSecretsTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Sentence = "Secrets cannot be saved until HATCH_SECRET_KEY is set.";

    /// <summary>The three secret cards, by the text on their Save buttons. Two share a noun, so they are told apart by position.</summary>
    private static readonly string[] Saves = ["Save token", "Save API key", "Save API key"];

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task WithNoKey_TheSentenceIsBesideEachSecretField_AtTheDesktopProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        return RunAsync("SettingsSecretsNoKeyDesktop", profile: null, secretKey: null);
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task WithNoKey_TheSentenceIsBesideEachSecretField_AtThePhoneProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);

        return RunAsync("SettingsSecretsNoKeyPhone", DeviceProfile.IPhone14Pro, secretKey: null);
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task WithNoKey_TheSentenceIsBesideEachSecretField_OnThePhoneProfileInChromium()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        return RunAsync("SettingsSecretsNoKeyPhoneChromium", DeviceProfile.IPhone14ProChromium, secretKey: null);
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task WithAKey_ThePageSaysNothingAboutOne_AndSaveWorks()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        // Made here at run time: no key, real or example, goes in a tracked file.
        return RunAsync("SettingsSecretsWithKey", profile: null, secretKey: Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    }

    private async Task RunAsync(string name, DeviceProfile? profile, string? secretKey)
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
            var baseUrl = await harness.StartHostAsync(deadline, secretKey: secretKey);

            await harness.OpenAsync(deadline, baseUrl, "/settings");
            await harness.WaitForVisibleAsync(deadline, "h2.hatch-section-title:has-text(\"Codex API key\")");

            var page = harness.Page!;
            output.WriteLine($"screenshot: {await harness.FullPageScreenshotAsync(deadline, "settings-secrets")}");

            var notes = page.GetByText(Sentence);
            var saves = page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Save token" })
                .Or(page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Save API key" }));
            var fields = page.Locator("input[type=password]");

            Assert.Equal(3, await deadline.BoundAsync("counting the secret fields", () => fields.CountAsync()));
            Assert.Equal(3, await deadline.BoundAsync("counting the Save buttons", () => saves.CountAsync()));

            if (secretKey is null)
            {
                Assert.Equal(3, await deadline.BoundAsync("counting the sentences", () => notes.CountAsync()));
                for (var i = 0; i < 3; i++)
                {
                    var card = page.Locator(".hatch-card:has(input[type=password])").Nth(i);
                    var link = card.GetByRole(Microsoft.Playwright.AriaRole.Link, new() { Name = "Environment variables" });
                    Assert.Equal("/docs/environment-variables", await deadline.BoundAsync($"reading card {i}'s link", () => link.GetAttributeAsync("href")));

                    // Typing does not turn Save on: the page already knows the server would refuse.
                    await deadline.BoundAsync($"typing into field {i}", () => fields.Nth(i).FillAsync("sk-typed"));
                    Assert.True(
                        await deadline.BoundAsync($"checking Save {i}", () => saves.Nth(i).IsDisabledAsync()),
                        $"{Saves[i]} (card {i}) is on with no key");
                }

                output.WriteLine("with no key: three sentences, three links to /docs/environment-variables, three Save buttons off after typing");
            }
            else
            {
                Assert.Equal(0, await deadline.BoundAsync("counting the sentences", () => notes.CountAsync()));
                Assert.True(
                    await deadline.BoundAsync("checking Save before typing", () => saves.Nth(0).IsDisabledAsync()),
                    "Save is on with nothing typed");

                await deadline.BoundAsync("typing into field 0", () => fields.Nth(0).FillAsync("sk-typed"));
                Assert.True(
                    await deadline.BoundAsync("checking Save after typing", () => saves.Nth(0).IsEnabledAsync()),
                    "Save is off with a key set and something typed");

                output.WriteLine("with a key: no sentence, and Save turns on once something is typed");
            }

            if (profile is not null)
            {
                var overflow = await harness.EvaluateAsync<int>(
                    deadline, "measuring sideways overflow",
                    "() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
                output.WriteLine($"sideways overflow: {overflow}px");
                Assert.True(overflow <= 0, $"the page scrolls sideways by {overflow}px");
            }

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

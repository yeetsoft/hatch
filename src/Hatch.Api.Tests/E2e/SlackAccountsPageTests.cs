using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Chat;
using Hatch.Api.Tests.Chat;
using Hatch.Api.Tests.Hatch;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// The Slack accounts section on <c>/settings</c>, rendered, with a fake chat provider standing in for
/// Slack. Two workspaces are connected through the API. Acme is linked and confirmed through the API,
/// with the secret taken from the message the fake recorded. Acme's Runner failures switch is flipped
/// off and back on, each saved and surviving a reload, and a seeded refusal is shown under its row.
/// Globex is linked through the page by typing into the picker, and left waiting. Then
/// <c>/settings?chatLink=...</c> is opened with Globex's secret, and the page reads it as linked and
/// takes the secret out of the address.
/// <para>
/// The wall is off, so the person is the local person, who is never offered a member by email: only the
/// picker path is shown here. The email suggestion and the signed-out redirect are covered by the
/// controller and database tests. Run at the desktop profile and at <c>iPhone 14 Pro</c>, where nothing
/// may run off the screen; the page is saved as a screenshot under the case's artifact directory.
/// </para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class SlackAccountsPageTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 180;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Row = ".hatch-slack-account";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task Settings_LinksConfirmsAndShowsSlackAccounts_AtTheDesktopProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        return RunAsync("SlackAccountsDesktop", profile: null);
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task Settings_LinksConfirmsAndShowsSlackAccounts_AtThePhoneProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);

        return RunAsync("SlackAccountsPhone", DeviceProfile.IPhone14Pro);
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

            var provider = new FakeChatProvider();
            provider.Members[FakeChatProvider.AcmeToken] =
            [
                new("UA1", "Ada Reed", "ada"),
                new("UA2", "Cy Lo", null),
            ];
            provider.Members[FakeChatProvider.GlobexToken] =
            [
                new("UG1", "Bo Tan", "bobby"),
                new("UG2", "Dee Ray", null),
                new("UG3", "Eli Fox", "elif"),
            ];

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline, services =>
            {
                services.RemoveAll<IChatProvider>();
                services.AddSingleton<IChatProvider>(provider);
                // A message's link needs an address to be built from. An example one: the page is opened at the host's own.
                services.Configure<HatchOptions>(o => o.PublicBaseUrl = "https://hatch.example");
            },
            // A token is sealed under a key, and a host with none refuses to connect a workspace.
            // Made here at run time: no key, real or example, goes in a tracked file.
            secretKey: Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

            using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
            var ids = await deadline.BoundAsync("connecting the workspaces", async () =>
            {
                foreach (var token in new[] { FakeChatProvider.AcmeToken, FakeChatProvider.GlobexToken })
                {
                    using var connected = await http.PostAsJsonAsync("/api/chat/workspaces", new { token });
                    Assert.True(connected.IsSuccessStatusCode, $"POST /api/chat/workspaces answered {(int)connected.StatusCode}: {await connected.Content.ReadAsStringAsync()}");
                }

                return await WorkspaceIdsAsync(http);
            });

            // Acme is linked and confirmed through the API, the way the message's link would be.
            var acmeLink = await deadline.BoundAsync("linking Acme", () => PostAsync(http, "/api/me/chat/links", new { workspaceId = ids["Acme"], memberId = "UA1" }));
            Assert.Equal("waiting", acmeLink.GetProperty("state").GetString());
            var acmeSecret = SecretFromLastMessage(provider);
            var confirmed = await deadline.BoundAsync("confirming Acme", () => PostAsync(http, "/api/me/chat/links/confirm", new { secret = acmeSecret }));
            Assert.Equal("linked", confirmed.GetProperty("state").GetString());

            await harness.OpenAsync(deadline, baseUrl, "/settings");
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForVisibleAsync(deadline, "h2.hatch-section-title:text-is(\"Slack accounts\")");

            // Acme reads linked, as its member; Globex has nothing yet and offers Link.
            await harness.WaitForVisibleAsync(deadline, $"{Row}:has-text(\"Acme\"):has-text(\"Linked as Ada Reed\")");
            await harness.WaitForVisibleAsync(deadline, $"{Row}:has-text(\"Globex\"):has-text(\"Not linked\")");
            Assert.Equal(0, await CountAsync(harness, deadline, $"{Row}:has-text(\"Acme\") button:text-is(\"Link\")"));
            Assert.Equal(1, await CountAsync(harness, deadline, $"{Row}:has-text(\"Acme\") button:text-is(\"Unlink\")"));
            await AssertFitsAsync(harness, deadline, profile);
            output.WriteLine("screenshot: " + await harness.ScreenshotAsync(deadline, "settings-slack-linked"));

            // Acme's Runner failures switch is on by default; Globex, still waiting for a member, has none.
            Assert.Equal(1, await CountAsync(harness, deadline, $"{Row}:has-text(\"Acme\") input:checked"));
            Assert.Equal(0, await CountAsync(harness, deadline, $"{Row}:has-text(\"Globex\") input[type=checkbox]"));

            // Switched off, saved, and still off after a reload.
            await SetRunnerFailuresAsync(harness, deadline, "Acme", wanted: false);
            await harness.GoToAsync(deadline, "/settings");
            await harness.WaitForVisibleAsync(deadline, $"{Row}:has-text(\"Acme\")");
            Assert.Equal(0, await CountAsync(harness, deadline, $"{Row}:has-text(\"Acme\") input:checked"));

            // Switched on again, saved, and still on after a reload.
            await SetRunnerFailuresAsync(harness, deadline, "Acme", wanted: true);
            await harness.GoToAsync(deadline, "/settings");
            await harness.WaitForVisibleAsync(deadline, $"{Row}:has-text(\"Acme\")");
            Assert.Equal(1, await CountAsync(harness, deadline, $"{Row}:has-text(\"Acme\") input:checked"));

            // A refusal on file is shown under the row, with Slack's own sentence.
            await SeedRefusalAsync(harness.Services, "Acme", "channel_not_found");
            await harness.GoToAsync(deadline, "/settings");
            await harness.WaitForVisibleAsync(
                deadline, $"{Row}:has-text(\"Acme\"):has-text(\"Slack could not send to this account: channel_not_found\")");
            await AssertFitsAsync(harness, deadline, profile);
            // The section sits below the token cards: bring it into the frame so the picture shows the switch and the refusal.
            await harness.EvaluateAsync<bool>(
                deadline, "scrolling the Slack accounts section into view",
                "() => { document.querySelector('.hatch-slack-accounts')?.scrollIntoView({ block: 'center' }); return true; }");
            output.WriteLine("screenshot: " + await harness.ScreenshotAsync(deadline, "settings-slack-refusal"));

            // Link Globex through the page: the local person is offered nobody, so the picker opens at once.
            // The link call above listed members itself, to check the one chosen, so count from here.
            var readsBefore = provider.MemberListReads;
            await harness.ClickAsync(deadline, $"{Row}:has-text(\"Globex\") button:text-is(\"Link\")");
            await harness.WaitForVisibleAsync(deadline, $"{Row}:has-text(\"Globex\") .hatch-slack-picker");
            Assert.Equal(3, await CountAsync(harness, deadline, $"{Row} .hatch-slack-members button"));
            Assert.Equal(readsBefore + 1, provider.MemberListReads);

            await deadline.BoundAsync("typing into the picker", () => harness.Page!.FillAsync($"{Row} .hatch-slack-picker input", "BOB"));
            await harness.WaitForFunctionAsync(
                deadline, "waiting for the picker to filter",
                "() => document.querySelectorAll('.hatch-slack-members button').length === 1");
            Assert.Equal(readsBefore + 1, provider.MemberListReads);
            Assert.DoesNotContain("Dee Ray", await TextAsync(harness, deadline, ".hatch-slack-picker"));
            await AssertFitsAsync(harness, deadline, profile);
            output.WriteLine("screenshot: " + await harness.ScreenshotAsync(deadline, "settings-slack-picker"));

            await harness.ClickAsync(deadline, $"{Row} .hatch-slack-members button:has-text(\"Bo Tan\")");
            await harness.WaitForVisibleAsync(deadline, $"{Row}:has-text(\"Globex\"):has-text(\"Waiting\"):has-text(\"Bo Tan\")");
            Assert.Equal(0, await CountAsync(harness, deadline, ".hatch-slack-picker"));
            Assert.Equal(2, provider.Sends.Count);
            Assert.Contains(
                "Linked as Ada Reed",
                await TextAsync(harness, deadline, $"{Row}:has-text(\"Acme\")"));
            await AssertFitsAsync(harness, deadline, profile);
            output.WriteLine("screenshot: " + await harness.ScreenshotAsync(deadline, "settings-slack-waiting"));

            // The message's link, opened: the page confirms it, says so, and takes the secret out of the address.
            var globexSecret = SecretFromLastMessage(provider);
            await harness.GoToAsync(deadline, "/settings?chatLink=" + Uri.EscapeDataString(globexSecret));
            await harness.WaitForVisibleAsync(deadline, "p[role=status]:text-is(\"Linked as Bo Tan.\")");
            await harness.WaitForVisibleAsync(deadline, $"{Row}:has-text(\"Globex\"):has-text(\"Linked as Bo Tan\")");
            var address = await harness.EvaluateAsync<string>(deadline, "reading the address", "() => location.pathname + location.search");
            Assert.Equal("/settings", address);
            Assert.DoesNotContain("chatLink", address);
            Assert.DoesNotContain(globexSecret, await harness.EvaluateAsync<string>(deadline, "reading the page", "() => document.body.innerHTML"));
            await AssertFitsAsync(harness, deadline, profile);
            output.WriteLine("screenshot: " + await harness.ScreenshotAsync(deadline, "settings-slack-confirmed"));

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

    /// <summary>The workspaces the person may see, by name.</summary>
    private static async Task<Dictionary<string, int>> WorkspaceIdsAsync(HttpClient http)
    {
        using var response = await http.GetAsync("/api/me/chat");
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"GET /api/me/chat answered {(int)response.StatusCode}: {text}");
        using var doc = JsonDocument.Parse(text);

        return doc.RootElement.GetProperty("workspaces").EnumerateArray()
            .ToDictionary(w => w.GetProperty("name").GetString()!, w => w.GetProperty("id").GetInt32());
    }

    private static async Task<JsonElement> PostAsync(HttpClient http, string path, object body)
    {
        using var response = await http.PostAsJsonAsync(path, body);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");

        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>The secret in the link of the last message the fake was asked to send.</summary>
    private static string SecretFromLastMessage(FakeChatProvider provider) =>
        QueryHelpers.ParseQuery(new Uri(provider.Sends[^1].Content.Links[0].Url!).Query)["chatLink"].ToString();

    /// <summary>Clicks the Runner failures checkbox on the named workspace's row, and waits for it to be in the
    /// wanted state and enabled again: a saved change.</summary>
    private static async Task SetRunnerFailuresAsync(E2eHarness harness, Deadline deadline, string workspaceName, bool wanted)
    {
        var row = $"{Row}:has-text(\"{workspaceName}\")";
        await harness.ClickAsync(deadline, $"{row} input[type=checkbox]");
        await harness.WaitForFunctionAsync(
            deadline, $"waiting for {workspaceName}'s Runner failures switch to be {(wanted ? "ticked" : "unticked")}",
            "([r, w]) => { const rows = [...document.querySelectorAll('.hatch-slack-account')]; " +
            "const row = rows.find(x => x.textContent.includes(r)); const i = row && row.querySelector('input[type=checkbox]'); " +
            "return !!i && i.checked === w && !i.disabled; }",
            new object[] { workspaceName, wanted });
    }

    /// <summary>Sets a workspace's Slack link's last refusal, as a real refusal would leave it.</summary>
    private static async Task SeedRefusalAsync(IServiceProvider services, string workspaceName, string refusal)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
        var workspace = await db.ChatWorkspaces.SingleAsync(w => w.Name == workspaceName);
        var link = await db.ChatLinks.SingleAsync(l => l.WorkspaceId == workspace.Id);
        link.LastRefusal = refusal;
        link.LastRefusalAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>How many elements match a Playwright selector, so <c>:text-is</c> works.</summary>
    private static Task<int> CountAsync(E2eHarness harness, Deadline deadline, string selector) =>
        deadline.BoundAsync($"counting {selector}", () => harness.Page!.Locator(selector).CountAsync());

    /// <summary>The text of the first element matching a Playwright selector, so <c>:has-text</c> works.</summary>
    private static async Task<string> TextAsync(E2eHarness harness, Deadline deadline, string selector) =>
        await deadline.BoundAsync($"reading {selector}", () => harness.Page!.Locator(selector).First.TextContentAsync()) ?? "";

    /// <summary>On a phone, nothing may run off the screen.</summary>
    private async Task AssertFitsAsync(E2eHarness harness, Deadline deadline, DeviceProfile? profile)
    {
        if (profile is null) return;

        var measured = await harness.EvaluateAsync<double[]>(
            deadline, "measuring the page",
            "() => [document.documentElement.scrollWidth, window.innerWidth]");
        output.WriteLine($"scrollWidth {measured[0]}, innerWidth {measured[1]}");
        Assert.True(measured[0] <= measured[1], $"the page runs off the screen: scrollWidth {measured[0]} > innerWidth {measured[1]}");
    }
}

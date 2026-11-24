using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Hatch.Contracts;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Chat;
using Hatch.Api.Tests.Chat;
using Hatch.Api.Tests.Hatch;
using Microsoft.EntityFrameworkCore;
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
    private const int CaseBudgetSeconds = 180;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Form = "form.hatch-chat-add";

    /// <summary>The sentence <c>SlackChatProvider</c> gives for <c>not_in_channel</c>; the fake refuses C2 with it.</summary>
    private const string NotInvitedSentence = "the app has not been invited to that channel; in Slack, add Hatch to it";

    /// <summary>What Slack says when it will not open Globex's connection. Holds no token.</summary>
    private const string SocketModeOffSentence = "Socket Mode is turned off for this app";

    private const string NotInvitedHint = "Not invited yet. In Slack, open the channel and add Hatch.";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task Chat_BindsSetsUpRoomsAndSendsATest_AtTheDesktopProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        return RunAsync("ChatDesktop", profile: null);
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task Chat_BindsSetsUpRoomsAndSendsATest_AtThePhoneProfile()
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

            // Held, so the case can say what each workspace's token can see and which channel refuses a post.
            var provider = new FakeChatProvider();
            // Acme's token is replaced part way through the case, so both of its tokens see the same channels.
            provider.Channels[FakeChatProvider.AcmeToken] =
            [
                new("C1", "general", false, true),
                new("C2", "announcements", false, false),
                new("C3", "leaders", true, true),
            ];
            provider.Channels[FakeChatProvider.AcmeNewToken] = provider.Channels[FakeChatProvider.AcmeToken];
            provider.Channels[FakeChatProvider.GlobexToken] = [new("C9", "lobby", false, true)];
            provider.PostRefusals["C2"] = NotInvitedSentence;
            // Acme's app token listens with the fake's default script. Globex's is refused. Initech's never opens and never
            // refuses, so its row stays at Not listening yet.
            provider.Listens[FakeChatProvider.GlobexAppToken] = new ListenScript { End = ChatListenEnd.Refused(SocketModeOffSentence) };
            provider.Listens[FakeChatProvider.InitechAppToken] = new ListenScript { CallOpened = false };

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline, services =>
            {
                services.RemoveAll<IChatProvider>();
                services.AddSingleton<IChatProvider>(provider);
            },
            // A token is sealed under a key, and a host with none refuses to connect a workspace.
            // Made here at run time: no key, real or example, goes in a tracked file.
            secretKey: Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

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

            // Both start with no app-level token. Acme is given one; Globex stays without, so the page
            // shows one of each.
            await harness.WaitForVisibleAsync(deadline, "tr:has-text(\"Acme\") .hatch-chat-apptoken__state:text-is(\"No app token\")");
            await harness.WaitForVisibleAsync(deadline, "tr:has-text(\"Globex\") .hatch-chat-apptoken__state:text-is(\"No app token\")");
            // With no app token there is nothing to listen with, and no listening state in either row.
            Assert.Equal(0, await CountAsync(harness, deadline, "main tbody .hatch-chat-listen"));
            await deadline.BoundAsync(
                "typing the app-level token",
                () => harness.Page!.FillAsync("input[aria-label=\"App-level token for Acme\"]", FakeChatProvider.AcmeAppToken));
            await harness.ClickAsync(deadline, "tr:has-text(\"Acme\") button:has-text(\"Set app token\")");
            await harness.WaitForVisibleAsync(deadline, "tr:has-text(\"Acme\") .hatch-chat-apptoken__state:text-is(\"App token set\")");
            await harness.WaitForFunctionAsync(
                deadline, "waiting for the app token box to empty",
                "() => document.querySelector('input[aria-label=\"App-level token for Acme\"]').value === ''");
            await AssertNoTokenOnPageAsync(harness, deadline);

            // A refused app-level token says the provider's sentence, leaves the box empty, and changes nothing.
            await deadline.BoundAsync(
                "typing the refused app-level token",
                () => harness.Page!.FillAsync("input[aria-label=\"App-level token for Acme\"]", FakeChatProvider.RefusedAppToken));
            await harness.ClickAsync(deadline, "tr:has-text(\"Acme\") button:has-text(\"Replace app token\")");
            await harness.WaitForVisibleAsync(deadline, $"tr:has-text(\"Acme\") .text-danger:text-is(\"{FakeChatProvider.AppRefusalSentence}\")");
            await harness.WaitForFunctionAsync(
                deadline, "waiting for the app token box to empty after the refusal",
                "() => document.querySelector('input[aria-label=\"App-level token for Acme\"]').value === ''");
            await harness.WaitForVisibleAsync(deadline, "tr:has-text(\"Acme\") .hatch-chat-apptoken__state:text-is(\"App token set\")");
            await harness.WaitForVisibleAsync(deadline, "tr:has-text(\"Globex\") .hatch-chat-apptoken__state:text-is(\"No app token\")");
            await AssertNoTokenOnPageAsync(harness, deadline);

            await AssertFitsAsync(harness, deadline, profile);
            output.WriteLine("screenshot: " + await harness.ScreenshotAsync(deadline, "chat-two-workspaces"));

            await AssertListeningShownAsync(harness, deadline, profile, provider);

            await BindAndSetUpRoomsAsync(harness, deadline, baseUrl, provider, profile);

            await AssertDeliveryShownAsync(harness, deadline, profile);

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

    /// <summary>
    /// Binds projects to the workspaces, sets channels up as rooms, narrows a room to chosen projects, and sends
    /// test messages: the part of the page that does something with a connected workspace. Run through the page the
    /// way an admin would, with the fake provider standing in for Slack.
    /// </summary>
    private async Task BindAndSetUpRoomsAsync(E2eHarness harness, Deadline deadline, string baseUrl, FakeChatProvider provider, DeviceProfile? profile)
    {
        var seeded = await deadline.BoundAsync("filing three projects", () => SeedProjectsAsync(baseUrl));
        Assert.Equal(3, seeded.Length);

        // The page reads the projects when it opens, so open it again.
        await harness.GoToAsync(deadline, "/chat");
        await harness.WaitForVisibleAsync(deadline, "td strong:text-is(\"Acme\")");
        await harness.WaitForVisibleAsync(deadline, "td strong:text-is(\"Globex\")");

        const string acmeBound = "[aria-label=\"Projects bound to Acme\"]";
        const string globexBound = "[aria-label=\"Projects bound to Globex\"]";
        const string acmeRooms = "[aria-label=\"Rooms on Acme\"]";
        const string globexRooms = "[aria-label=\"Rooms on Globex\"]";
        const string general = acmeRooms + " section[aria-label=\"Room general\"]";
        const string generalChosen = general + " [aria-label=\"Projects general carries\"]";

        // Every project is offered, none is bound yet, and there is no room.
        Assert.Equal(3, await CountAsync(harness, deadline, acmeBound + " input[type=checkbox]"));
        Assert.Equal(0, await CountAsync(harness, deadline, acmeBound + " input:checked"));
        await harness.WaitForVisibleAsync(deadline, acmeRooms + " :text(\"No room is set up on Acme.\")");

        // Bind two projects to Acme and one to Globex.
        await SetCheckboxAsync(harness, deadline, acmeBound, "CHA", true);
        await SetCheckboxAsync(harness, deadline, acmeBound, "CHB", true);
        await SetCheckboxAsync(harness, deadline, globexBound, "CHC", true);
        Assert.Equal(2, await CountAsync(harness, deadline, acmeBound + " input:checked"));
        Assert.Equal(1, await CountAsync(harness, deadline, globexBound + " input:checked"));
        await AssertNoTokenOnPageAsync(harness, deadline);

        // Add a room on Acme: the channels are read now, and the one the app has not joined says so.
        await harness.ClickAsync(deadline, acmeRooms + " button:text-is(\"Add a room\")");
        await harness.WaitForVisibleAsync(deadline, acmeRooms + " ul.hatch-chat-picker");
        Assert.Equal(3, await CountAsync(harness, deadline, acmeRooms + " li.hatch-chat-channel"));
        Assert.Contains(NotInvitedHint, await TextAsync(harness, deadline, acmeRooms + " li.hatch-chat-channel:has-text(\"announcements\")"));
        Assert.DoesNotContain(NotInvitedHint, await TextAsync(harness, deadline, acmeRooms + " li.hatch-chat-channel:has-text(\"general\")"));
        Assert.Contains("Private", await TextAsync(harness, deadline, acmeRooms + " li.hatch-chat-channel:has-text(\"leaders\")"));
        output.WriteLine("screenshot: " + await harness.FullPageScreenshotAsync(deadline, "chat-channel-picker"));

        await harness.ClickAsync(deadline, acmeRooms + " button[aria-label=\"Add general\"]");
        await harness.WaitForVisibleAsync(deadline, general);
        await harness.WaitForVisibleAsync(deadline, acmeRooms + " li.hatch-chat-channel:has-text(\"general\") :text(\"Already a room\")");
        Assert.True(
            await harness.EvaluateAsync<bool>(
                deadline, "reading the Add general button",
                "() => document.querySelector('[aria-label=\"Rooms on Acme\"] button[aria-label=\"Add general\"]').disabled"),
            "a channel that is already a room can be added again");

        // A new room starts with every switch off, carrying every bound project.
        Assert.Equal(0, await CountAsync(harness, deadline, general + " [aria-label=\"What it posts\"] input:checked"));
        Assert.Equal(1, await CountAsync(harness, deadline, general + " input[type=radio]:checked"));
        Assert.Equal(0, await CountAsync(harness, deadline, generalChosen));

        // The switch for problems that belong to no project says what else it carries.
        Assert.Equal(
            "Also the goose stopping, backups needing a person, and runner failures.",
            (await TextAsync(harness, deadline, general + " .hatch-chat-room__hint")).Trim());

        // And on Globex: a room of its own, whose chosen list could only ever be Globex's projects.
        await harness.ClickAsync(deadline, globexRooms + " button:text-is(\"Add a room\")");
        await harness.WaitForVisibleAsync(deadline, globexRooms + " li.hatch-chat-channel:has-text(\"lobby\")");
        await harness.ClickAsync(deadline, globexRooms + " button[aria-label=\"Add lobby\"]");
        await harness.WaitForVisibleAsync(deadline, globexRooms + " section[aria-label=\"Room lobby\"]");
        await SetCheckboxAsync(harness, deadline, globexRooms + " [aria-label=\"What it posts\"]", "Questions", true);
        await SetCheckboxAsync(harness, deadline, globexRooms + " [aria-label=\"What it posts\"]", "Problems", true);
        await AssertNoTokenOnPageAsync(harness, deadline);

        // A switch saves on its own.
        await SetCheckboxAsync(harness, deadline, general + " [aria-label=\"What it posts\"]", "Changelog", true);
        Assert.Equal(1, await CountAsync(harness, deadline, general + " [aria-label=\"What it posts\"] input:checked"));

        // Chosen projects, none ticked: the room says it carries nothing.
        await ChooseScopeAsync(harness, deadline, general, "Chosen projects");
        await harness.WaitForVisibleAsync(deadline, general + " p:text-is(\"This room carries no project.\")");

        // Only Acme's own bound projects are offered, not Globex's.
        Assert.Equal(2, await CountAsync(harness, deadline, generalChosen + " input[type=checkbox]"));
        Assert.DoesNotContain("CHC", await TextAsync(harness, deadline, generalChosen));

        await SetCheckboxAsync(harness, deadline, generalChosen, "CHA", true);
        await SetCheckboxAsync(harness, deadline, generalChosen, "CHB", true);
        await harness.WaitForHiddenAsync(deadline, general + " p:text-is(\"This room carries no project.\")");
        Assert.Equal(2, await CountAsync(harness, deadline, generalChosen + " input:checked"));
        await AssertNoTokenOnPageAsync(harness, deadline);
        await AssertFitsAsync(harness, deadline, profile);
        output.WriteLine("screenshot: " + await harness.FullPageScreenshotAsync(deadline, "chat-rooms"));

        // Unbinding the second project takes it from the room's chosen list; the page re-reads, it does not guess.
        await SetCheckboxAsync(harness, deadline, acmeBound, "CHB", false);
        await harness.WaitForFunctionAsync(
            deadline, "waiting for the chosen list to lose CHB",
            "(s) => document.querySelectorAll(s + ' input[type=checkbox]').length === 1", generalChosen);
        Assert.Equal(1, await CountAsync(harness, deadline, generalChosen + " input:checked"));
        Assert.DoesNotContain("CHB", await TextAsync(harness, deadline, generalChosen));

        // Unbinding the first leaves a room that carries nothing, and says that, never "every project".
        await SetCheckboxAsync(harness, deadline, acmeBound, "CHA", false);
        await harness.WaitForVisibleAsync(deadline, general + " p:text-is(\"This room carries no project.\")");
        Assert.Equal(0, await CountAsync(harness, deadline, generalChosen + " input[type=checkbox]"));
        var roomText = (await TextAsync(harness, deadline, general)).Replace("Every bound project", "");
        Assert.DoesNotContain("every project", roomText, StringComparison.OrdinalIgnoreCase);
        await AssertNoTokenOnPageAsync(harness, deadline);
        await AssertFitsAsync(harness, deadline, profile);
        output.WriteLine("screenshot: " + await harness.FullPageScreenshotAsync(deadline, "chat-carries-none"));

        // Binding it again offers it back, unticked: what was chosen before is gone.
        await SetCheckboxAsync(harness, deadline, acmeBound, "CHA", true);
        await harness.WaitForFunctionAsync(
            deadline, "waiting for the chosen list to offer CHA",
            "(s) => document.querySelectorAll(s + ' input[type=checkbox]').length === 1", generalChosen);
        Assert.Equal(0, await CountAsync(harness, deadline, generalChosen + " input:checked"));
        Assert.True(await harness.IsVisibleAsync(deadline, general + " p:text-is(\"This room carries no project.\")"));

        // A test message is sent, says Sent, and reached the channel with Acme's current token.
        await harness.ClickAsync(deadline, general + " button:text-is(\"Send a test message\")");
        await harness.WaitForVisibleAsync(deadline, general + " p[role=status]:text-is(\"Sent\")");
        Assert.Contains(provider.Posts, p => p.ChannelId == "C1" && p.Token == FakeChatProvider.AcmeNewToken);
        await AssertNoTokenOnPageAsync(harness, deadline);

        // A channel the app has not joined can still be added, and its test says why it did not arrive.
        await harness.ClickAsync(deadline, acmeRooms + " button:text-is(\"Add a room\")");
        await harness.WaitForVisibleAsync(deadline, acmeRooms + " li.hatch-chat-channel:has-text(\"announcements\")");
        await harness.ClickAsync(deadline, acmeRooms + " button[aria-label=\"Add announcements\"]");
        const string announcements = acmeRooms + " section[aria-label=\"Room announcements\"]";
        await harness.WaitForVisibleAsync(deadline, announcements);
        await harness.ClickAsync(deadline, announcements + " button:text-is(\"Send a test message\")");
        await harness.WaitForVisibleAsync(deadline, announcements + $" p[role=status]:text-is(\"{NotInvitedSentence}\")");
        Assert.Equal(
            0,
            await CountAsync(harness, deadline, announcements + " p[role=status]:text-is(\"Sent\")"));
        await AssertNoTokenOnPageAsync(harness, deadline);
        await AssertFitsAsync(harness, deadline, profile);
        output.WriteLine("screenshot: " + await harness.FullPageScreenshotAsync(deadline, "chat-test-refused"));

        // Remove room asks first, and names the channel.
        await harness.ClickAsync(deadline, announcements + " button:text-is(\"Remove room\")");
        await harness.WaitForVisibleAsync(deadline, "[aria-modal=\"true\"]:has-text(\"Remove room #announcements?\")");
        await harness.ClickAsync(deadline, "[aria-modal=\"true\"] button:text-is(\"Leave it\")");
        await harness.WaitForHiddenAsync(deadline, "[aria-modal=\"true\"]");
        Assert.True(await harness.IsVisibleAsync(deadline, announcements), "the room was removed without being confirmed");
        await harness.ClickAsync(deadline, announcements + " button:text-is(\"Remove room\")");
        await harness.WaitForVisibleAsync(deadline, "[aria-modal=\"true\"]:has-text(\"Remove room #announcements?\")");
        await harness.ClickAsync(deadline, "[aria-modal=\"true\"] button:text-is(\"Remove room\")");
        await harness.WaitForHiddenAsync(deadline, announcements);
        Assert.True(await harness.IsVisibleAsync(deadline, general), "the other room went with it");
    }

    /// <summary>
    /// Whether Hatch is listening is shown in the app token cell. Acme already holds an app token and the fake
    /// provider lets it listen, so it reads Listening. Globex is given one through the page, and the fake refuses
    /// it, so it reads Not listening with the sentence it was refused with. Initech is connected here and given one,
    /// and the fake neither opens nor refuses it, so it reads Not listening yet. Nothing is seeded: the listener runs
    /// in this host and would write over the columns within one tick.
    /// </summary>
    private async Task AssertListeningShownAsync(E2eHarness harness, Deadline deadline, DeviceProfile? profile, FakeChatProvider provider)
    {
        // Initech joins only now, so the row counts asserted earlier in the case stay as they were.
        await ConnectAsync(harness, deadline, FakeChatProvider.InitechToken);
        await harness.WaitForVisibleAsync(deadline, "td strong:text-is(\"Initech\")");
        await deadline.BoundAsync(
            "typing Initech's app-level token",
            () => harness.Page!.FillAsync("input[aria-label=\"App-level token for Initech\"]", FakeChatProvider.InitechAppToken));
        await harness.ClickAsync(deadline, "tr:has-text(\"Initech\") button:has-text(\"Set app token\")");
        await harness.WaitForVisibleAsync(deadline, "tr:has-text(\"Initech\") .hatch-chat-apptoken__state:text-is(\"App token set\")");

        await deadline.BoundAsync(
            "typing Globex's app-level token",
            () => harness.Page!.FillAsync("input[aria-label=\"App-level token for Globex\"]", FakeChatProvider.GlobexAppToken));
        await harness.ClickAsync(deadline, "tr:has-text(\"Globex\") button:has-text(\"Set app token\")");
        await harness.WaitForVisibleAsync(deadline, "tr:has-text(\"Globex\") .hatch-chat-apptoken__state:text-is(\"App token set\")");

        // The listener looks every few seconds and the page asks every thirty; open the page again until both have moved.
        string acme = "", globex = "", initech = "";
        while (true)
        {
            await harness.GoToAsync(deadline, "/chat");
            await harness.WaitForVisibleAsync(deadline, "td strong:text-is(\"Globex\")");
            acme = await TextAsync(harness, deadline, "main tbody tr:has-text(\"Acme\") td.hatch-chat-apptoken-cell");
            globex = await TextAsync(harness, deadline, "main tbody tr:has-text(\"Globex\") td.hatch-chat-apptoken-cell");
            initech = await TextAsync(harness, deadline, "main tbody tr:has-text(\"Initech\") td.hatch-chat-apptoken-cell");
            bool initechLooked;
            lock (provider.ListenCalls) initechLooked = provider.ListenCalls.Contains(FakeChatProvider.InitechAppToken);
            if (acme.Contains("opened") && globex.Contains($"Slack said: {SocketModeOffSentence}") && initechLooked) break;
            await deadline.BoundAsync("waiting for the listener", () => Task.Delay(500));
        }

        Assert.Contains("Listening", acme);
        Assert.DoesNotContain("Not listening", acme);
        Assert.Matches(@"opened (just now|\d+ (second|minute)s? ago)", acme);

        Assert.Contains("Not listening", globex);
        Assert.DoesNotContain("Not listening yet", globex);
        Assert.Matches(@"first failed (just now|\d+ (second|minute)s? ago)", globex);
        Assert.DoesNotContain("opened", globex);

        // The listener has looked at Initech and heard nothing, so it has neither opened nor failed.
        Assert.Contains("Not listening yet", initech);
        Assert.DoesNotContain("first failed", initech);
        Assert.DoesNotContain("opened", initech);
        Assert.DoesNotContain("Slack said", initech);
        Assert.Equal(0, await CountAsync(harness, deadline, "main tbody tr:has-text(\"Initech\") td.hatch-chat-apptoken-cell .text-danger"));
        Assert.Equal(3, await CountAsync(harness, deadline, "main tbody .hatch-chat-listen"));

        await AssertNoTokenOnPageAsync(harness, deadline);
        await AssertFitsAsync(harness, deadline, profile);
        output.WriteLine("screenshot: " + await harness.FullPageScreenshotAsync(deadline, "chat-listening"));

        // On a wide screen the page scrolls inside its frame, so the full-page shot ends at the fold. Bring the rows up and take the screen again.
        await harness.EvaluateAsync<bool>(
            deadline, "bringing the workspaces into view",
            "() => { document.querySelector('main tbody').scrollIntoView(); return true; }");
        output.WriteLine("screenshot: " + await harness.ScreenshotAsync(deadline, "chat-listening-rows"));
    }

    /// <summary>
    /// What waits to be sent is shown in the workspace's own row. Acme is given two messages waiting, one dropped and
    /// a refusal; Globex is given one waiting, two dropped and a refusal of its own. The host has no public address, so the page says messages carry no link and
    /// names the variable that sets one.
    /// </summary>
    private async Task AssertDeliveryShownAsync(E2eHarness harness, Deadline deadline, DeviceProfile? profile)
    {
        await deadline.BoundAsync("seeding what waits to be sent", () => SeedDeliveryAsync(harness.Services));

        // The page reads the delivery state when it opens, and on a timer; open it again.
        await harness.GoToAsync(deadline, "/chat");
        await harness.WaitForVisibleAsync(deadline, "td strong:text-is(\"Acme\")");
        await harness.WaitForVisibleAsync(deadline, "td.hatch-chat-delivery-cell:has-text(\"2 waiting\")");

        var acme = await TextAsync(harness, deadline, "main tbody tr:has-text(\"Acme\") td.hatch-chat-delivery-cell");
        Assert.Contains("2 waiting", acme);
        Assert.Contains("1 dropped", acme);
        Assert.Contains("Slack said: ratelimited", acme);
        Assert.Matches("minutes? ago", acme);

        // Globex has its own counts, not Acme's, and the page adds no row of its own.
        var globex = await TextAsync(harness, deadline, "main tbody tr:has-text(\"Globex\") td.hatch-chat-delivery-cell");
        Assert.Contains("1 waiting", globex);
        Assert.Contains("2 dropped", globex);
        Assert.Contains("Slack said: channel_not_found", globex);
        Assert.Matches("minutes? ago", globex);
        Assert.DoesNotContain("ratelimited", globex);
        Assert.DoesNotContain("2 waiting", globex);

        // Each workspace has a room with its switches set: Acme's changelog, Globex's questions and problems.
        Assert.Equal(
            1,
            await CountAsync(harness, deadline, "[aria-label=\"Rooms on Acme\"] section[aria-label=\"Room general\"] [aria-label=\"What it posts\"] input:checked"));
        Assert.Equal(
            2,
            await CountAsync(harness, deadline, "[aria-label=\"Rooms on Globex\"] section[aria-label=\"Room lobby\"] [aria-label=\"What it posts\"] input:checked"));
        Assert.Equal(
            3,
            await harness.EvaluateAsync<int>(deadline, "counting the rows", "() => document.querySelectorAll('main tbody tr').length"));
        Assert.Equal(
            3,
            await harness.EvaluateAsync<int>(
                deadline, "counting Token set",
                "() => [...document.querySelectorAll('main tbody td')].filter(td => td.textContent === 'Token set').length"));

        // No public address is set on this host.
        var note = await TextAsync(harness, deadline, "p.hatch-chat-links-note");
        Assert.Contains("no link", note);
        Assert.Contains("HATCH_PUBLIC_URL", note);

        await AssertNoTokenOnPageAsync(harness, deadline);
        await AssertFitsAsync(harness, deadline, profile);
        output.WriteLine("screenshot: " + await harness.FullPageScreenshotAsync(deadline, "chat-delivery"));
    }

    /// <summary>Gives the Acme workspace a room with two messages waiting and one dropped, and the last thing Slack said; gives Globex's room one waiting and two dropped, with its own last refusal.</summary>
    private static async Task SeedDeliveryAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
        var now = DateTimeOffset.UtcNow;

        var acme = await db.ChatWorkspaces.SingleAsync(w => w.Name == "Acme");
        var room = new EfHatchChatRoom { WorkspaceId = acme.Id, ExternalId = "C7", Name = "seeded", CreatedAt = now, UpdatedAt = now };
        db.ChatRooms.Add(room);
        await db.SaveChangesAsync();

        EfHatchChatMessage Message(string subject, bool dropped = false) => new()
        {
            RoomId = room.Id, WorkspaceId = acme.Id, Kind = "problem", Subject = subject, Content = "{}",
            QueuedAt = now, PendingSince = now, DroppedAt = dropped ? now : null,
            // Not due for a day, so the host's own sender leaves them waiting for the case to see.
            NextAttemptAt = now.AddDays(1),
        };

        db.ChatMessages.AddRange(Message("finding:1"), Message("finding:2"), Message("finding:3", dropped: true));
        acme.LastRefusal = "ratelimited";
        acme.LastRefusalAt = now.AddMinutes(-3);
        acme.SendFailingSince = now.AddMinutes(-3);

        // Globex already has the lobby room the page made: one message waiting there, two dropped.
        var globex = await db.ChatWorkspaces.SingleAsync(w => w.Name == "Globex");
        var lobby = await db.ChatRooms.SingleAsync(r => r.WorkspaceId == globex.Id && r.Name == "lobby");
        EfHatchChatMessage GlobexMessage(string subject, bool dropped = false) => new()
        {
            RoomId = lobby.Id, WorkspaceId = globex.Id, Kind = "question", Subject = subject, Content = "{}",
            QueuedAt = now, PendingSince = now, DroppedAt = dropped ? now : null,
            NextAttemptAt = now.AddDays(1),
        };

        db.ChatMessages.AddRange(GlobexMessage("question:1"), GlobexMessage("question:2", dropped: true), GlobexMessage("question:3", dropped: true));
        globex.LastRefusal = "channel_not_found";
        globex.LastRefusalAt = now.AddMinutes(-7);
        globex.SendFailingSince = now.AddMinutes(-7);
        await db.SaveChangesAsync();
    }

    /// <summary>Files three projects, as the Projects screen would.</summary>
    private static async Task<ProjectDto[]> SeedProjectsAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var projects = new List<ProjectDto>();

        foreach (var key in new[] { "CHA", "CHB", "CHC" })
        {
            using var response = await http.PostAsJsonAsync("/api/projects", new ProjectCreateRequest(key, key), web);
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, $"POST /api/projects answered {(int)response.StatusCode}: {text}");
            projects.Add(JsonSerializer.Deserialize<ProjectDto>(text, web)!);
        }

        return [.. projects];
    }

    /// <summary>Clicks the checkbox in <paramref name="group"/> whose label holds <paramref name="key"/>, and waits for it to be in the wanted state and enabled again: a saved change.</summary>
    private static async Task SetCheckboxAsync(E2eHarness harness, Deadline deadline, string group, string key, bool wanted)
    {
        await harness.ClickAsync(deadline, $"{group} label:has-text(\"{key}\") input");
        await harness.WaitForFunctionAsync(
            deadline, $"waiting for {key} to be {(wanted ? "ticked" : "unticked")} in {group}",
            "([g, k, w]) => { const l = [...document.querySelectorAll(g + ' label')].find(x => x.textContent.includes(k)); " +
            "if (!l) return false; const i = l.querySelector('input'); return i.checked === w && !i.disabled; }",
            new object[] { group, key, wanted });
    }

    /// <summary>Picks "Every bound project" or "Chosen projects" on a room, and waits for the save.</summary>
    private static async Task ChooseScopeAsync(E2eHarness harness, Deadline deadline, string room, string label)
    {
        await harness.ClickAsync(deadline, $"{room} [aria-label=\"Which projects\"] label:has-text(\"{label}\") input");
        await harness.WaitForFunctionAsync(
            deadline, $"waiting for {label} to be chosen",
            "([r, l]) => { const x = [...document.querySelectorAll(r + ' [aria-label=\"Which projects\"] label')].find(y => y.textContent.includes(l)); " +
            "const i = x && x.querySelector('input'); return !!i && i.checked && !i.disabled; }",
            new object[] { room, label });
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
            FakeChatProvider.AcmeToken, FakeChatProvider.AcmeNewToken, FakeChatProvider.GlobexToken, FakeChatProvider.InitechToken, FakeChatProvider.RefusedToken,
        })
        {
            Assert.DoesNotContain(token, shown);
            Assert.DoesNotContain(token["xoxb-".Length..], shown);
        }

        foreach (var appToken in new[]
        {
            FakeChatProvider.AcmeAppToken, FakeChatProvider.AcmeNewAppToken, FakeChatProvider.GlobexAppToken, FakeChatProvider.InitechAppToken, FakeChatProvider.RefusedAppToken,
        })
        {
            Assert.DoesNotContain(appToken, shown);
            Assert.DoesNotContain(appToken["xapp-".Length..], shown);
        }

        // Nothing on the page is shaped like an app-level token, so no placeholder or hint can have one.
        Assert.DoesNotContain("xapp-", shown);
    }
}

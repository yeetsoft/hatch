using System.Security.Cryptography;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Api.Tests.Hatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// The Slack column on <c>/users</c>, rendered. Two people are seeded straight into the databases: Ada
/// holds a linked link in Acme and a waiting one in Globex, Bob holds none. A third link belongs to a
/// person who is not in the people table, as a removed person's would be; opening the page must not
/// show it and must have deleted it. The case opens Ada's links, backs out of a removal, then removes
/// the waiting one, and checks the row and the database.
/// <para>
/// Run at the desktop profile and at <c>iPhone 14 Pro</c>, where nothing may run off the screen; the
/// page is saved as a screenshot under the case's artifact directory.
/// </para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class UsersSlackLinksPageTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 180;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string AdaRow = "tr:has(strong:text-is(\"Ada\"))";
    private const string BobRow = "tr:has(strong:text-is(\"Bob\"))";
    private const string SlackCell = "td:nth-child(8)";
    private const string Panel = "td[colspan=\"9\"]";
    private const string Dialog = ".hatch-claim-clear";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task Users_ShowsAndRemovesSlackLinks_AtTheDesktopProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        return RunAsync("UsersSlackDesktop", profile: null);
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task Users_ShowsAndRemovesSlackLinks_AtThePhoneProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);

        return RunAsync("UsersSlackPhone", DeviceProfile.IPhone14Pro);
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
            var baseUrl = await harness.StartHostAsync(deadline);
            var removedPerson = Guid.NewGuid();
            await deadline.BoundAsync("seeding people and links", () => SeedAsync(harness.Services, removedPerson));

            await harness.OpenAsync(deadline, baseUrl, "/users");
            await harness.WaitForTopBarAsync(deadline);

            // The column is drawn, counts each person's links, and does not count the removed person's.
            await harness.WaitForVisibleAsync(deadline, "th:text-is(\"Slack\")");
            await harness.WaitForVisibleAsync(deadline, $"{AdaRow} {SlackCell} button:has-text(\"2\")");
            await harness.WaitForVisibleAsync(deadline, $"{BobRow} {SlackCell} button:has-text(\"0\")");
            Assert.Equal(2, await CountAsync(harness, deadline, "tbody > tr:has(strong)"));
            await AssertFitsAsync(harness, deadline, profile);
            output.WriteLine("screenshot: " + await harness.ScreenshotAsync(deadline, "users-slack-column"));

            // Opening Ada's links lists the workspace, the member and the state, and offers only Remove.
            await harness.ClickAsync(deadline, $"{AdaRow} {SlackCell} button");
            await harness.WaitForVisibleAsync(deadline, $"{Panel} tr:has-text(\"Acme\"):has-text(\"Ada Reed\"):has-text(\"linked\")");
            await harness.WaitForVisibleAsync(deadline, $"{Panel} tr:has-text(\"Globex\"):has-text(\"Bo Tan\"):has-text(\"waiting\")");
            Assert.Equal(2, await CountAsync(harness, deadline, $"{Panel} button:text-is(\"Remove\")"));
            Assert.Equal(0, await CountAsync(harness, deadline, $"{Panel} button:text-is(\"Link\")"));
            Assert.Equal(0, await CountAsync(harness, deadline, $"{Panel} button:text-is(\"Confirm\")"));
            await AssertFitsAsync(harness, deadline, profile);
            output.WriteLine("screenshot: " + await harness.ScreenshotAsync(deadline, "users-slack-panel"));

            // Remove asks first; leaving it removes nothing.
            await harness.ClickAsync(deadline, $"{Panel} tr:has-text(\"Globex\") button:text-is(\"Remove\")");
            await harness.WaitForVisibleAsync(deadline, $"{Dialog}:has-text(\"Ada's link to Bo Tan in Globex will be removed.\"):has-text(\"Nothing is sent to it afterwards.\")");
            await AssertFitsAsync(harness, deadline, profile);
            output.WriteLine("screenshot: " + await harness.ScreenshotAsync(deadline, "users-slack-confirm"));
            await harness.ClickAsync(deadline, $"{Dialog} button:text-is(\"Leave it\")");
            await harness.WaitForHiddenAsync(deadline, Dialog);
            Assert.Equal(2, await LinkCountAsync(harness.Services));

            // Confirming removes that one only, and the count and the panel follow.
            await harness.ClickAsync(deadline, $"{Panel} tr:has-text(\"Globex\") button:text-is(\"Remove\")");
            await harness.ClickAsync(deadline, $"{Dialog} button:text-is(\"Remove\")");
            await harness.WaitForHiddenAsync(deadline, $"{Panel} tr:has-text(\"Globex\")");
            await harness.WaitForVisibleAsync(deadline, $"{AdaRow} {SlackCell} button:has-text(\"1\")");
            Assert.Equal(1, await CountAsync(harness, deadline, $"{Panel} button:text-is(\"Remove\")"));
            Assert.Equal(1, await LinkCountAsync(harness.Services));
            await AssertFitsAsync(harness, deadline, profile);
            output.WriteLine("screenshot: " + await harness.ScreenshotAsync(deadline, "users-slack-removed"));

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

    private static async Task SeedAsync(IServiceProvider services, Guid removedPerson)
    {
        var now = new DateTimeOffset(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);
        await using var scope = services.CreateAsyncScope();
        var app = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();

        var ada = new EfPerson { Id = Guid.NewGuid(), Name = "Ada", Role = PersonRole.Admin, CreatedAt = now, UpdatedAt = now };
        var bob = new EfPerson { Id = Guid.NewGuid(), Name = "Bob", Role = PersonRole.User, CreatedAt = now, UpdatedAt = now };
        app.People.AddRange(ada, bob);
        await app.SaveChangesAsync();

        var acme = new EfHatchChatWorkspace { Provider = "fake", ExternalId = "TACME", Name = "Acme", Token = "sealed", CreatedAt = now, UpdatedAt = now };
        var globex = new EfHatchChatWorkspace { Provider = "fake", ExternalId = "TGLOBEX", Name = "Globex", Token = "sealed", CreatedAt = now, UpdatedAt = now };
        db.ChatWorkspaces.AddRange(acme, globex);
        await db.SaveChangesAsync();

        db.ChatLinks.AddRange(
            Link(acme.Id, ada.Id, "UA1", "Ada Reed", now, confirmed: true),
            Link(globex.Id, ada.Id, "UG1", "Bo Tan", now, confirmed: false),
            Link(acme.Id, removedPerson, "UA2", "Cy Lo", now, confirmed: true));
        await db.SaveChangesAsync();
    }

    private static EfHatchChatLink Link(int workspaceId, Guid personId, string memberId, string memberName, DateTimeOffset now, bool confirmed) => new()
    {
        WorkspaceId = workspaceId, PersonId = personId, MemberId = memberId, MemberName = memberName, ConversationId = "D-" + memberId,
        CreatedAt = now,
        ConfirmedAt = confirmed ? now : null,
        SecretHash = confirmed ? null : RandomNumberGenerator.GetBytes(AuthHash.Length),
        SecretExpiresAt = confirmed ? null : now.AddYears(10),
    };

    private static async Task<int> LinkCountAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<HatchContext>().ChatLinks.CountAsync();
    }

    /// <summary>How many elements match a Playwright selector, so <c>:text-is</c> works.</summary>
    private static Task<int> CountAsync(E2eHarness harness, Deadline deadline, string selector) =>
        deadline.BoundAsync($"counting {selector}", () => harness.Page!.Locator(selector).CountAsync());

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

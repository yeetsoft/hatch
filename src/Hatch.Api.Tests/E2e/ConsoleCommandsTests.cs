using System.Text.Json;
using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2334's commands behind the console's <c>&gt;</c> prefix, driven on the
/// real app: a bare <c>&gt;</c> shows the first eight with "8 of N", the
/// matching reaches the same row however it is worded, Enter runs a page or a
/// theme, <c>?</c> lists the prefixes and Enter on them does nothing, and Users
/// is a command for an Admin only. The wall is off, which reads as a person with
/// no role - the non-admin - and the admin is that same person with
/// <c>/api/me</c> fulfilled as one. Screenshots of each state land in the case's
/// artifact directory for a person to look at.
///
/// <para>Desktop in Chromium, and the phone profile in WebKit.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class ConsoleCommandsTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 120;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Prompt = ".hatch-omni-input";
    private const string Verb = ".hatch-omni-verb";
    private const string Status = ".hatch-omni-status [aria-live]";
    private const string Options = ".hatch-omni [role=option]";
    private const string ThemeKey = "hatch.theme";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableTheory(Timeout = CaseTimeoutMs)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Desktop_TheCommands(bool admin) => await RunAsync("desktop", null, admin);

    [SkippableTheory(Timeout = CaseTimeoutMs)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Phone_TheCommands(bool admin)
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        await RunAsync("phone", DeviceProfile.IPhone14Pro, admin);
    }

    private async Task RunAsync(string where, DeviceProfile? profile, bool admin)
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var role = admin ? "admin" : "user";
        var harness = new E2eHarness($"ConsoleCommands-{where}-{role}");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline, profile);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);

            await harness.OpenAsync(deadline, baseUrl, "/plan", admin ? AsAdminAsync : null);
            await harness.WaitForTopBarAsync(deadline);
            var page = harness.Page!;

            // `/` opens it; a bare `>` shows eight commands and says so.
            await deadline.BoundAsync("opening the console", () => page.Keyboard.PressAsync("/"));
            await harness.WaitForVisibleAsync(deadline, Prompt);
            Assert.Equal("go", await Text(harness, deadline, Verb));
            await Type(harness, deadline, ">");
            Assert.Equal("run", await Text(harness, deadline, Verb));
            Assert.Equal(8, await Count(harness, deadline, Options));
            Assert.Matches(@"^8 of \d+ — keep typing$", await Text(harness, deadline, Status));
            output.WriteLine("bare >: " + await harness.ScreenshotAsync(deadline, "bare"));

            // Users is a command for an Admin only.
            await Type(harness, deadline, ">users");
            Assert.Equal(admin ? ["Go to Users"] : [], await Names(harness, deadline));
            Assert.Equal(admin ? "1 command" : "no command matches", await Text(harness, deadline, Status));
            output.WriteLine("users: " + await harness.ScreenshotAsync(deadline, "users"));

            // The same row however it is worded.
            await Type(harness, deadline, ">board");
            var board = await Names(harness, deadline);
            Assert.Equal("Go to Board", board[0]);
            await Type(harness, deadline, ">go board");
            Assert.Equal(board, await Names(harness, deadline));
            await Type(harness, deadline, ">goto board");
            Assert.Equal(board, await Names(harness, deadline));

            // Enter runs it: the board, from the plan.
            await deadline.BoundAsync("running Go to Board", () => page.Keyboard.PressAsync("Enter"));
            await harness.WaitForVisibleAsync(deadline, E2eHarness.Column);
            Assert.Equal(0, await Count(harness, deadline, ".hatch-omni"));
            Assert.Equal("/", new Uri(page.Url).AbsolutePath);

            // The themes.
            foreach (var choice in new[] { "dark", "auto", "light" })
            {
                await deadline.BoundAsync("opening the console", () => page.Keyboard.PressAsync("/"));
                await harness.WaitForVisibleAsync(deadline, Prompt);
                await Type(harness, deadline, $">{choice}");
                Assert.Equal([$"Theme → {char.ToUpper(choice[0])}{choice[1..]}"], await Names(harness, deadline));
                await deadline.BoundAsync("running the theme", () => page.Keyboard.PressAsync("Enter"));
                await harness.WaitForHiddenAsync(deadline, ".hatch-omni");
                Assert.Equal(choice, await deadline.BoundAsync("reading the theme", () => page.EvaluateAsync<string?>($"localStorage.getItem('{ThemeKey}')")));
                output.WriteLine($"{choice}: " + await harness.ScreenshotAsync(deadline, choice));
            }

            // `?` lists the prefixes, and Enter on them does nothing.
            await deadline.BoundAsync("opening the console", () => page.Keyboard.PressAsync("/"));
            await harness.WaitForVisibleAsync(deadline, Prompt);
            await Type(harness, deadline, "?");
            Assert.Equal("help", await Text(harness, deadline, Verb));
            Assert.Equal(3, await Count(harness, deadline, ".hatch-omni-row--help"));
            Assert.Equal(0, await Count(harness, deadline, Options));
            Assert.Equal(["/", ">", "?"], await Texts(harness, deadline, ".hatch-omni-prefix"));
            output.WriteLine("help: " + await harness.ScreenshotAsync(deadline, "help"));
            await deadline.BoundAsync("pressing Enter on help", () => page.Keyboard.PressAsync("Enter"));
            Assert.Equal(1, await Count(harness, deadline, ".hatch-omni"));

            // No prefix, or `/`, is the issue search.
            await Type(harness, deadline, "/ha");
            Assert.Equal("go", await Text(harness, deadline, Verb));
            await Type(harness, deadline, "ha");
            Assert.Equal("go", await Text(harness, deadline, Verb));
            output.WriteLine("issues: " + await harness.ScreenshotAsync(deadline, "issues"));

            // Escape puts it away.
            await deadline.BoundAsync("pressing Escape", () => page.Keyboard.PressAsync("Escape"));
            await harness.WaitForHiddenAsync(deadline, ".hatch-omni");

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

    private static Task AsAdminAsync(IBrowserContext context) =>
        context.RouteAsync("**/api/me", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new MeDto("person", "An Admin", "admin", true, false, false), Web),
        }));

    private static Task Type(E2eHarness harness, Deadline deadline, string text) =>
        deadline.BoundAsync($"typing {text}", () => harness.Page!.Locator(Prompt).FillAsync(text));

    private static Task<string> Text(E2eHarness harness, Deadline deadline, string selector) =>
        deadline.BoundAsync($"reading {selector}", () => harness.Page!.Locator(selector).InnerTextAsync());

    private static Task<int> Count(E2eHarness harness, Deadline deadline, string selector) =>
        deadline.BoundAsync($"counting {selector}", () => harness.Page!.Locator(selector).CountAsync());

    private static async Task<IReadOnlyList<string>> Texts(E2eHarness harness, Deadline deadline, string selector) =>
        await deadline.BoundAsync($"reading {selector}", () => harness.Page!.Locator(selector).AllInnerTextsAsync());

    /// <summary>The command names listed, without the hint at each row's right.</summary>
    private static Task<IReadOnlyList<string>> Names(E2eHarness harness, Deadline deadline) =>
        Texts(harness, deadline, $"{Options} .hatch-omni-title");
}

using Hatch.Api.Tests.Hatch;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using System.Net;
using QuartzLogProvider = Quartz.Logging.LogProvider;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// What a browser case needs and no case should write twice: a scratch pair of
/// databases, the real host listening on a loopback port Kestrel picks, a
/// headless Chromium pointed at it, and - when the case fails - the evidence
/// left under <c>TestResults/e2e/</c>.
///
/// <para>Every phase is a method taking the case's <see cref="Deadline"/>, and
/// Playwright's own timeouts are set to zero (none), so a stall is reported by
/// the phase that stalled, with the host's log, rather than by a Playwright
/// timeout that knows nothing of either.</para>
///
/// <para>A case calls the phases in order inside a <c>try</c>, calls
/// <see cref="Fail"/> from the <c>catch</c>, and disposes in the <c>finally</c>;
/// disposal runs under a budget of its own, so a case that spent all of its
/// own is still reported for what it was.</para>
/// </summary>
internal sealed class E2eHarness(string name) : IAsyncDisposable
{
    /// <summary>The top bar, <c>packages/ui/src/components/TopBar.tsx</c>. Constants so "point at a missing class" is one edit.</summary>
    public const string TopBar = "header.hatch-topbar";

    /// <summary>A column of the board, <c>apps/hatch/src/pages/BoardPage.tsx</c>.</summary>
    public const string Column = ".hatch-column";

    /// <summary>What the board says when the module migrations did not run (BoardPage.tsx).</summary>
    public const string EmptyBoard = "This board has no columns yet.";

    /// <summary>Wider than the phone breakpoint: a phone takes the stacked <c>details</c> branch, a different render.</summary>
    private const int ViewportWidth = 1280;

    private const int ViewportHeight = 800;

    private static readonly TimeSpan CleanupBudget = TimeSpan.FromSeconds(30);

    private readonly CapturedLogs logs = new();
    private readonly List<string> pageErrors = [];

    private string? hatch;
    private string? quartz;
    private MinimalFactory? factory;
    private IPlaywright? playwright;
    private IBrowser? browser;
    private IBrowserContext? context;
    private IPage? page;
    private bool failed;
    private DeviceProfile? profile;

    /// <summary>The host's log so far, for <see cref="Deadline"/>'s failure message.</summary>
    public Func<string> LogsSoFar => () => string.Join(Environment.NewLine, logs.Messages);

    /// <summary>Marks the case failed, so disposal leaves a screenshot and a trace behind.</summary>
    public void Fail() => failed = true;

    public static string BundleIndex() =>
        Path.Combine(WebRoot(), "apps", "hatch", "index.html");

    private static string WebRoot() =>
        Path.Combine(MinimalDatabases.RepositoryRoot(), "src", "Hatch.Api", "wwwroot");

    private string ArtifactDirectory() =>
        Path.Combine(MinimalDatabases.RepositoryRoot(), "src", "Hatch.Api.Tests", "TestResults", "e2e", $"{name}-{Environment.ProcessId}");

    /// <summary>
    /// Headless, on the profile's engine (Chromium with no profile). A browser
    /// nobody has installed is a skip naming the command that installs it, not
    /// a failure: CI's api job has none until the job that runs these cases
    /// does. Anything else propagates.
    /// </summary>
    public async Task LaunchBrowserAsync(Deadline deadline, DeviceProfile? profile = null)
    {
        this.profile = profile;
        var engine = profile?.Engine ?? "chromium";

        playwright = await deadline.BoundAsync("starting Playwright", Playwright.CreateAsync);
        var type = playwright[engine];

        try
        {
            browser = await deadline.BoundAsync($"launching {engine}", () => type.LaunchAsync(new() { Headless = true }));
        }
        catch (PlaywrightException e) when (e.Message.Contains("Executable doesn't exist", StringComparison.Ordinal))
        {
            throw new SkipException($"{type.Name} is not installed for Playwright: run `make browsers`");
        }
    }

    public async Task CreateDatabasesAsync(Deadline deadline)
    {
        (hatch, quartz) = await deadline.BoundAsync("creating the scratch databases", MinimalDatabases.CreateAsync);
        await deadline.BoundAsync("migrating", () => MinimalDatabases.MigrateAsync(hatch));
    }

    /// <summary>
    /// The real host on a loopback port of Kestrel's choosing - never its
    /// default port and never an inherited <c>ASPNETCORE_URLS</c> - serving
    /// the built SPA out of the repository's own <c>wwwroot</c>. Returns the
    /// address it is listening on.
    /// </summary>
    public async Task<string> StartHostAsync(Deadline deadline)
    {
        // Quartz keeps its logging provider in a static bound to whichever
        // host built a scheduler first; see MinimalStartupTests for why it is
        // cleared on the way in and again on the way out.
        QuartzLogProvider.SetCurrentLogProvider(null);

        factory = new MinimalFactory(hatch!, quartz!, logs, WebRoot());
        factory.UseKestrel(o => o.Listen(IPAddress.Loopback, 0));

        // StartServer blocks, so Task.Run is what gives Deadline something to race.
        await deadline.BoundAsync("starting the host", () => Task.Run(factory.StartServer));

        return factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
    }

    /// <summary>A fresh context with tracing on and <c>PageError</c> recorded from before the first navigation, then the board's address.</summary>
    public Task OpenBoardAsync(Deadline deadline, string baseUrl) =>
        OpenAsync(deadline, baseUrl, "/apps/hatch/");

    /// <summary>
    /// A fresh context - the launch profile's device, or 1280x800 with none -
    /// with tracing on and <c>PageError</c> recorded from before the first
    /// navigation, then the given path under the SPA's own address.
    /// </summary>
    public async Task OpenAsync(Deadline deadline, string baseUrl, string path)
    {
        await deadline.BoundAsync("opening the page", async () =>
        {
            var options = profile is null
                ? new BrowserNewContextOptions { BaseURL = baseUrl, ViewportSize = new() { Width = ViewportWidth, Height = ViewportHeight } }
                : new BrowserNewContextOptions(playwright!.Devices[profile.Device]) { BaseURL = baseUrl };

            context = await browser!.NewContextAsync(options);
            await context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true });

            page = await context.NewPageAsync();
            page.SetDefaultTimeout(0);
            page.SetDefaultNavigationTimeout(0);
            page.PageError += (_, message) => { lock (pageErrors) pageErrors.Add(message); };

            await page.GotoAsync(path);
        });
    }

    /// <summary>Any path under <c>/apps/hatch/</c>, in the context <see cref="OpenBoardAsync"/> or <see cref="OpenAsync"/> already opened.</summary>
    public Task GoToAsync(Deadline deadline, string path) =>
        deadline.BoundAsync($"going to {path}", () => page!.GotoAsync("/apps/hatch/" + path.TrimStart('/')));

    public Task WaitForTopBarAsync(Deadline deadline) =>
        deadline.BoundAsync("waiting for the top bar", () => page!.Locator(TopBar).WaitForAsync(new() { State = WaitForSelectorState.Visible }));

    /// <summary>
    /// The first column, or the board's own statement that it has none -
    /// which is not a render failure but a database one, and says so.
    /// </summary>
    public async Task WaitForFirstColumnAsync(Deadline deadline)
    {
        var columns = page!.Locator(Column);

        await deadline.BoundAsync("waiting for the first column", () =>
            columns.Or(page.GetByText(EmptyBoard)).First.WaitForAsync(new() { State = WaitForSelectorState.Visible }));

        if (await columns.CountAsync() == 0)
            throw new InvalidOperationException($"The board said \"{EmptyBoard}\": the module migrations did not run against the scratch database.");
    }

    /// <summary>Uncaught exceptions only: console messages and failed requests are not render failures, and an offline runner would make the latter flaky.</summary>
    public void AssertNoPageErrors()
    {
        lock (pageErrors)
            Assert.True(pageErrors.Count == 0, $"The page threw {pageErrors.Count} uncaught exception(s):{Environment.NewLine}{string.Join(Environment.NewLine, pageErrors)}");
    }

    /// <summary>
    /// Evidence first, when the case failed, then everything closed in order:
    /// context, browser, Playwright, host. The drop sits in its own
    /// <c>finally</c> so a close that fails is not a reason to leave the pair
    /// on the server beside it.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        var deadline = new Deadline(CleanupBudget, LogsSoFar);

        try
        {
            try
            {
                await LeaveEvidenceAsync(deadline);

                if (context is not null) await deadline.BoundAsync("closing the context", () => context.CloseAsync());
                if (browser is not null) await deadline.BoundAsync("closing the browser", () => browser.CloseAsync());
                playwright?.Dispose();
                if (factory is not null) await deadline.BoundAsync("stopping the host", () => factory.DisposeAsync().AsTask());
            }
            finally
            {
                QuartzLogProvider.SetCurrentLogProvider(null);
            }
        }
        finally
        {
            if (hatch is not null && quartz is not null)
                await deadline.BoundAsync("dropping the scratch databases", () => MinimalDatabases.DropAsync(hatch, quartz));
        }
    }

    private async Task LeaveEvidenceAsync(Deadline deadline)
    {
        if (context is null) return;

        if (!failed)
        {
            await deadline.BoundAsync("stopping the trace", () => context.Tracing.StopAsync());
            return;
        }

        var directory = ArtifactDirectory();
        Directory.CreateDirectory(directory);

        if (page is not null)
            await deadline.BoundAsync("taking the screenshot", () => page.ScreenshotAsync(new() { Path = Path.Combine(directory, "page.png"), FullPage = true }));

        await deadline.BoundAsync("saving the trace", () => context.Tracing.StopAsync(new() { Path = Path.Combine(directory, "trace.zip") }));
    }
}

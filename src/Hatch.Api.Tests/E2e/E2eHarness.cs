using Hatch.Api.Tests.Hatch;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using System.Net;
using System.Text.Json;
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

    /// <summary>The bar's controls, named by <see cref="BarMeasurement.Controls"/> and in bar order: <c>TopBar.tsx</c>, <c>UtilizationBattery.tsx</c>, <c>NavOvernight.tsx</c>, <c>NavAttention.tsx</c>, and the gear <c>Menu</c> trigger.</summary>
    public const string BrandSelector = ".hatch-topbar__brand";

    public const string BatterySelector = ".hatch-battery";

    public const string OvernightSelector = ".hatch-overnight-control";

    public const string AttentionSelector = ".hatch-attention-control";

    public const string GearSelector = "header.hatch-topbar button[aria-label=\"Settings and theme\"]";

    /// <summary>What the board says when the module migrations did not run (BoardPage.tsx).</summary>
    public const string BoardTrailingSelector = ".hatch-filter-trailing";

    public const string RunnersCardSelector = ".hatch-card--flush";

    /// <summary>Every control a Runners row draws in its group: the buttons, and the ticket input.</summary>
    public const string RunnerControlSelector = ".hatch-reorder--wrap button, .hatch-reorder--wrap input";

    public const string EmptyBoard = "This board has no columns yet.";

    /// <summary>Wider than the phone breakpoint: a phone takes the stacked <c>details</c> branch, a different render.</summary>
    private const int ViewportWidth = 1280;

    private const int ViewportHeight = 800;

    /// <summary>
    /// Runs in the page and reads it as it stands. A <c>DOMRect</c> does not
    /// serialise, so each box is copied to a plain object; a selector that finds
    /// nothing is <c>null</c>. <c>PrimaryNav</c> is left out of the tree on a
    /// phone, so its presence is a fact worth reading (<c>lib/viewport.ts</c>).
    /// </summary>
    private const string MeasureBarScript = """
        (selectors) => {
          const box = (selector) => {
            const element = document.querySelector(selector);
            if (element === null) return null;
            const r = element.getBoundingClientRect();
            return { x: r.x, y: r.y, width: r.width, height: r.height };
          };
          const root = document.scrollingElement;
          return {
            innerWidth: window.innerWidth,
            scrollWidth: root.scrollWidth,
            clientWidth: root.clientWidth,
            phoneQueryMatches: window.matchMedia('(max-width: 40rem)').matches,
            coarsePointerMatches: window.matchMedia('(pointer: coarse)').matches,
            primaryNavDrawn: document.querySelector('.hatch-primary-nav') !== null,
            controls: selectors.map(([name, selector]) => [name, box(selector)]),
          };
        }
        """;

    /// <summary>
    /// Runs in the page and reads the board's filter bar trailing group: every
    /// element child, named by its <c>aria-label</c> or text. The Go to Work
    /// button is drawn inside a wrapper, so it is read as the wrapper: a span
    /// named for its visible label, <c>Go</c> on a phone.
    /// </summary>
    private const string MeasureBoardBarScript = """
        (trailingSelector) => {
          const plain = (r) => ({ x: r.x, y: r.y, width: r.width, height: r.height });
          const trailing = document.querySelector(trailingSelector);
          if (trailing === null) return null;
          const root = document.scrollingElement;
          return {
            innerWidth: window.innerWidth,
            scrollWidth: root.scrollWidth,
            clientWidth: root.clientWidth,
            coarsePointerMatches: window.matchMedia('(pointer: coarse)').matches,
            trailing: [...trailing.children].map((element) => ({
              name: element.tagName.toLowerCase() + ' "' + (element.getAttribute('aria-label') || element.textContent.trim()) + '"',
              box: plain(element.getBoundingClientRect()),
            })),
          };
        }
        """;

    /// <summary>
    /// Runs in the page and reads the Runners page as it stands. Each control
    /// carries its own box and its cell's; the card is reported by its border
    /// box, and <see cref="MeasureRunnersAsync"/> takes the border off.
    /// </summary>
    private const string MeasureRunnersScript = """
        ([cardSelector, controlSelector]) => {
          const plain = (r) => ({ x: r.x, y: r.y, width: r.width, height: r.height });
          const card = document.querySelector(cardSelector);
          const table = card === null ? null : card.querySelector('.hatch-table');
          if (card === null || table === null) return null;
          const root = document.scrollingElement;
          return {
            innerWidth: window.innerWidth,
            scrollWidth: root.scrollWidth,
            clientWidth: root.clientWidth,
            coarsePointerMatches: window.matchMedia('(pointer: coarse)').matches,
            card: plain(card.getBoundingClientRect()),
            table: plain(table.getBoundingClientRect()),
            controls: [...document.querySelectorAll(controlSelector)].map((element) => ({
              name: element.tagName.toLowerCase() + ' "' + (element.getAttribute('aria-label') || element.textContent.trim()) + '"',
              box: plain(element.getBoundingClientRect()),
              cell: plain(element.closest('td').getBoundingClientRect()),
              scrollWidth: element.scrollWidth,
              clientWidth: element.clientWidth,
            })),
          };
        }
        """;

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
    /// <paramref name="beforeNavigate"/> runs once the context exists and before
    /// the first request, so a case's routes are in place for it.
    /// </summary>
    public async Task OpenAsync(Deadline deadline, string baseUrl, string path, Func<IBrowserContext, Task>? beforeNavigate = null)
    {
        await deadline.BoundAsync("opening the page", async () =>
        {
            var options = profile is null
                ? new BrowserNewContextOptions { BaseURL = baseUrl, ViewportSize = new() { Width = ViewportWidth, Height = ViewportHeight } }
                : new BrowserNewContextOptions(playwright!.Devices[profile.Device]) { BaseURL = baseUrl };

            context = await browser!.NewContextAsync(options);
            await context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true });

            if (beforeNavigate is not null) await beforeNavigate(context);

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

    /// <summary>Drops every route a case installed, so the next load meets the real host: one context goes from a stubbed state to the state at rest.</summary>
    public Task UnrouteAllAsync(Deadline deadline) =>
        deadline.BoundAsync("removing the routes", () => context!.UnrouteAllAsync());

    /// <summary>The first element matching <paramref name="selector"/>, visible; the selector is in the phase so a stall names what it waited for.</summary>
    public Task WaitForVisibleAsync(Deadline deadline, string selector) =>
        deadline.BoundAsync($"waiting for {selector}", () => page!.Locator(selector).First.WaitForAsync(new() { State = WaitForSelectorState.Visible }));

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

    /// <summary>
    /// The top bar's geometry as the page stands: nothing is scrolled or
    /// resized first, so a case that wants a settled page waits for it before
    /// calling.
    /// </summary>
    public async Task<BarMeasurement> MeasureBarAsync(Deadline deadline)
    {
        (string Name, string Selector)[] controls =
        [
            ("brand", BrandSelector),
            ("battery", BatterySelector),
            ("overnight", OvernightSelector),
            ("attention", AttentionSelector),
            ("gear", GearSelector),
        ];

        var read = await deadline.BoundAsync("measuring the top bar", () =>
            page!.EvaluateAsync<JsonElement>(MeasureBarScript, controls.Select(c => new[] { c.Name, c.Selector }).ToArray()));

        return new BarMeasurement(
            read.GetProperty("innerWidth").GetInt32(),
            read.GetProperty("scrollWidth").GetInt32(),
            read.GetProperty("clientWidth").GetInt32(),
            read.GetProperty("phoneQueryMatches").GetBoolean(),
            read.GetProperty("coarsePointerMatches").GetBoolean(),
            read.GetProperty("primaryNavDrawn").GetBoolean(),
            read.GetProperty("controls").EnumerateArray().ToDictionary(
                pair => pair[0].GetString()!,
                pair => pair[1].ValueKind == JsonValueKind.Null ? null : ReadBox(pair[1])));
    }

    /// <summary>The board filter bar's trailing group as the page stands; the board must be drawn first.</summary>
    public async Task<BoardBarMeasurement> MeasureBoardBarAsync(Deadline deadline)
    {
        var read = await deadline.BoundAsync("measuring the board filter bar", () =>
            page!.EvaluateAsync<JsonElement>(MeasureBoardBarScript, BoardTrailingSelector));

        return new BoardBarMeasurement(
            read.GetProperty("innerWidth").GetInt32(),
            read.GetProperty("scrollWidth").GetInt32(),
            read.GetProperty("clientWidth").GetInt32(),
            read.GetProperty("coarsePointerMatches").GetBoolean(),
            read.GetProperty("trailing").EnumerateArray()
                .Select(c => (c.GetProperty("name").GetString()!, ReadBox(c.GetProperty("box"))))
                .ToList());
    }

    /// <summary>The Runners page's card, table and row controls as the page stands; the card is reported inside its 1px border.</summary>
    public async Task<RunnersMeasurement> MeasureRunnersAsync(Deadline deadline)
    {
        var read = await deadline.BoundAsync("measuring the Runners page", () =>
            page!.EvaluateAsync<JsonElement>(MeasureRunnersScript, new[] { RunnersCardSelector, RunnerControlSelector }));

        Assert.True(read.ValueKind != JsonValueKind.Null, $"{RunnersCardSelector} with a .hatch-table inside it is not on the page");

        const double border = 1;
        var card = ReadBox(read.GetProperty("card"));

        return new RunnersMeasurement(
            read.GetProperty("innerWidth").GetInt32(),
            read.GetProperty("scrollWidth").GetInt32(),
            read.GetProperty("clientWidth").GetInt32(),
            read.GetProperty("coarsePointerMatches").GetBoolean(),
            new Box(card.X + border, card.Y + border, card.Width - 2 * border, card.Height - 2 * border),
            ReadBox(read.GetProperty("table")),
            read.GetProperty("controls").EnumerateArray().Select(c => new ControlMeasurement(
                c.GetProperty("name").GetString()!,
                ReadBox(c.GetProperty("box")),
                ReadBox(c.GetProperty("cell")),
                c.GetProperty("scrollWidth").GetInt32(),
                c.GetProperty("clientWidth").GetInt32())).ToList());
    }

    private static Box ReadBox(JsonElement box) => new(
        box.GetProperty("x").GetDouble(),
        box.GetProperty("y").GetDouble(),
        box.GetProperty("width").GetDouble(),
        box.GetProperty("height").GetDouble());

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

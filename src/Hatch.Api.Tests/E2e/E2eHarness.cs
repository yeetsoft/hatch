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

    public const string TypesOnlySelector = ".hatch-types-only";

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
            search: (() => {
              const search = document.querySelector('.hatch-board-search');
              return search === null ? null : plain(search.getBoundingClientRect());
            })(),
            trailing: [...trailing.children].map((element) => ({
              name: element.tagName.toLowerCase() + ' "' + (element.getAttribute('aria-label') || element.textContent.trim()) + '"',
              box: plain(element.getBoundingClientRect()),
            })),
          };
        }
        """;

    /// <summary>
    /// Runs in the page and reads the open Types panel: every Only button's box
    /// and the box of the menu panel that holds them. A <c>DOMRect</c> does not
    /// serialise, so each is copied to a plain object.
    /// </summary>
    private const string MeasureTypesOnlyScript = """
        (onlySelector) => {
          const plain = (r) => ({ x: r.x, y: r.y, width: r.width, height: r.height });
          const only = [...document.querySelectorAll(onlySelector)];
          const panel = only.length === 0 ? null : only[0].closest('.hatch-menu__panel');
          const root = document.scrollingElement;
          return {
            innerWidth: window.innerWidth,
            scrollWidth: root.scrollWidth,
            clientWidth: root.clientWidth,
            coarsePointerMatches: window.matchMedia('(pointer: coarse)').matches,
            panel: panel === null ? null : plain(panel.getBoundingClientRect()),
            only: only.map((element) => ({
              name: element.getAttribute('aria-label') || element.textContent.trim(),
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

    /// <summary>
    /// Runs in the page and reads the card holding the Omitted runners section
    /// and every element inside its rows and its add form. Null when the
    /// section is not drawn.
    /// </summary>
    private const string MeasureOmittedRunnersScript = """
        () => {
          const plain = (r) => ({ x: r.x, y: r.y, width: r.width, height: r.height });
          const anchor = document.querySelector('.hatch-omission-add');
          const card = anchor === null ? null : anchor.closest('.hatch-card');
          if (card === null) return null;
          const root = document.scrollingElement;
          const named = (element) => {
            const tag = element.tagName.toLowerCase();
            const cls = [...element.classList].filter((c) => c.startsWith('hatch-omission')).map((c) => '.' + c).join('');
            const label = element.getAttribute('aria-label') || element.textContent.trim().slice(0, 40);
            return tag + cls + ' "' + label + '"';
          };
          const elements = [...document.querySelectorAll('.hatch-omission, .hatch-omission *, .hatch-omission-add, .hatch-omission-add *')]
            .filter((element) => element.tagName !== 'DATALIST' && element.tagName !== 'OPTION')
            .map((element) => ({
              name: named(element),
              control: element.tagName === 'BUTTON' || element.tagName === 'INPUT',
              box: plain(element.getBoundingClientRect()),
              scrollWidth: element.scrollWidth,
              clientWidth: element.clientWidth,
            }));
          return {
            innerWidth: window.innerWidth,
            scrollWidth: root.scrollWidth,
            clientWidth: root.clientWidth,
            card: plain(card.getBoundingClientRect()),
            elements,
          };
        }
        """;

    /// <summary>
    /// Runs in the page and reads each Runners row's Token budget cell and
    /// activity badge, found by the runner's name rather than by position. The
    /// cell is the one under the <c>Token budget</c> heading, and the bounds
    /// text the one under <c>Bounds</c>, so a column added before either does
    /// not move the read. A fill's track is measured by
    /// <c>clientWidth</c>: the track has a 1px border, and the fill is a percent
    /// of its content box.
    /// </summary>
    private const string MeasureRunnerBudgetsScript = """
        () => {
          const headings = [...document.querySelectorAll('.hatch-table thead th')];
          const column = headings.findIndex((th) => th.textContent.trim() === 'Token budget');
          const boundsColumn = headings.findIndex((th) => th.textContent.trim() === 'Bounds');
          if (column < 0 || boundsColumn < 0) return null;
          const root = document.scrollingElement;
          const rows = [...document.querySelectorAll('.hatch-table tbody tr')]
            .filter((tr) => tr.querySelector('.hatch-runner-name') !== null)
            .map((tr) => {
              const cell = tr.children[column];
              return {
                name: tr.querySelector('.hatch-runner-name').textContent.trim(),
                cellText: cell.textContent.trim(),
                boundsText: tr.children[boundsColumn].textContent.trim(),
                lines: [...cell.querySelectorAll('.hatch-token-budget-line > span')].map((span) => span.textContent.trim()),
                bars: [...cell.querySelectorAll('.hatch-usage-bar-fill')].map((fill) => ({
                  fillWidth: fill.getBoundingClientRect().width,
                  trackWidth: fill.parentElement.clientWidth,
                  styleWidth: fill.style.width,
                  usageColor: fill.style.getPropertyValue('--usage-color'),
                  color: getComputedStyle(fill).backgroundColor,
                })),
                badge: [...tr.children[1].querySelectorAll('.hatch-badge')].map((b) => b.textContent.trim()).join(' | '),
              };
            });
          return { scrollWidth: root.scrollWidth, clientWidth: root.clientWidth, rows };
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
    public async Task OpenAsync(
        Deadline deadline, string baseUrl, string path, Func<IBrowserContext, Task>? beforeNavigate = null, bool reducedMotion = false)
    {
        await deadline.BoundAsync("opening the page", async () =>
        {
            var options = profile is null
                ? new BrowserNewContextOptions { BaseURL = baseUrl, ViewportSize = new() { Width = ViewportWidth, Height = ViewportHeight } }
                : new BrowserNewContextOptions(playwright!.Devices[profile.Device]) { BaseURL = baseUrl };

            if (reducedMotion) options.ReducedMotion = ReducedMotion.Reduce;

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

    /// <summary>The running host's services, for a case that seeds its own rows once <see cref="StartHostAsync"/> has returned.</summary>
    public IServiceProvider Services => factory!.Services;

    /// <summary>Clicks the first element matching <paramref name="selector"/>, once it is visible and enabled.</summary>
    public Task ClickAsync(Deadline deadline, string selector) =>
        deadline.BoundAsync($"clicking {selector}", () => page!.Locator(selector).First.ClickAsync());

    /// <summary>Picks the option whose value is <paramref name="value"/> in the first <c>select</c> matching <paramref name="selector"/>, firing the events a person's pick would.</summary>
    public Task SelectOptionAsync(Deadline deadline, string selector, string value) =>
        deadline.BoundAsync($"choosing {value} in {selector}", () => page!.Locator(selector).First.SelectOptionAsync(value));

    /// <summary>Selects everything in whichever element holds the page's focus.</summary>
    public Task SelectAllAsync(Deadline deadline) =>
        deadline.BoundAsync("selecting all", () => page!.Keyboard.PressAsync("ControlOrMeta+A"));

    /// <summary>
    /// Inserts <paramref name="text"/> as one operation, with no per-key
    /// bracket pairing - unlike <c>Keyboard.TypeAsync</c>, which Monaco would
    /// meet one key at a time, auto-closing <c>[</c>/<c>(</c> as they land.
    /// </summary>
    public Task InsertTextAsync(Deadline deadline, string text) =>
        deadline.BoundAsync("inserting text", () => page!.Keyboard.InsertTextAsync(text));

    /// <summary>
    /// Arms a one-shot handler that accepts the next browser dialog
    /// (<c>confirm</c>, <c>alert</c> or <c>prompt</c>) the page raises. Call
    /// immediately before the click that raises it - a dialog fired before
    /// this is armed is unhandled and the case hangs.
    /// </summary>
    public Task AcceptNextDialogAsync(Deadline deadline) =>
        deadline.BoundAsync("arming the next dialog to accept", () =>
        {
            void Handler(object? s, IDialog d) { page!.Dialog -= Handler; _ = d.AcceptAsync(); }
            page!.Dialog += Handler;
            return Task.CompletedTask;
        });

    /// <summary><see cref="AcceptNextDialogAsync"/>'s mirror: dismisses the next dialog instead.</summary>
    public Task DismissNextDialogAsync(Deadline deadline) =>
        deadline.BoundAsync("arming the next dialog to dismiss", () =>
        {
            void Handler(object? s, IDialog d) { page!.Dialog -= Handler; _ = d.DismissAsync(); }
            page!.Dialog += Handler;
            return Task.CompletedTask;
        });

    /// <summary>
    /// Sets the first input matching <paramref name="selector"/> to <paramref name="value"/> through the DOM's own
    /// <c>value</c> setter and fires <c>input</c>, which a <c>maxlength</c> does not hold back - unlike <c>FillAsync</c>,
    /// which stops at it - so a case can send the server more than the box lets a person type.
    /// </summary>
    public Task SetValuePastMaxLengthAsync(Deadline deadline, string selector, string value) =>
        deadline.BoundAsync($"setting {selector} past its maxlength", () => page!.Locator(selector).First.EvaluateAsync(
            """
            (input, value) => {
              Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, value);
              input.dispatchEvent(new Event('input', { bubbles: true }));
            }
            """,
            value));

    /// <summary>Runs <paramref name="script"/> in the page, a function taking <paramref name="arg"/>.</summary>
    public Task<T> EvaluateAsync<T>(Deadline deadline, string phase, string script, object? arg = null) =>
        deadline.BoundAsync(phase, () => page!.EvaluateAsync<T>(script, arg));

    /// <summary>
    /// Runs <paramref name="script"/> in the page against the first element matching
    /// <paramref name="selector"/> - found by Playwright's own selector engine, which
    /// knows <c>:has()</c> and <c>:text-is()</c> where a page script's own
    /// <c>document.querySelector</c> does not. <paramref name="script"/> is a function
    /// taking the matched element and then <paramref name="arg"/>.
    /// </summary>
    public Task<T> EvaluateOnAsync<T>(Deadline deadline, string phase, string selector, string script, object? arg = null) =>
        deadline.BoundAsync(phase, () => page!.Locator(selector).First.EvaluateAsync<T>(script, arg));

    /// <summary>Polls <paramref name="script"/> in the page until it answers truthy.</summary>
    public Task WaitForFunctionAsync(Deadline deadline, string phase, string script, object? arg = null) =>
        deadline.BoundAsync(phase, () => page!.WaitForFunctionAsync(script, arg));

    /// <summary>Drops every route a case installed, so the next load meets the real host: one context goes from a stubbed state to the state at rest.</summary>
    public Task UnrouteAllAsync(Deadline deadline) =>
        deadline.BoundAsync("removing the routes", () => context!.UnrouteAllAsync());

    /// <summary>The first element matching <paramref name="selector"/>, visible; the selector is in the phase so a stall names what it waited for.</summary>
    public Task WaitForVisibleAsync(Deadline deadline, string selector) =>
        deadline.BoundAsync($"waiting for {selector}", () => page!.Locator(selector).First.WaitForAsync(new() { State = WaitForSelectorState.Visible }));

    /// <summary>The first element matching <paramref name="selector"/>, hidden or gone.</summary>
    public Task WaitForHiddenAsync(Deadline deadline, string selector) =>
        deadline.BoundAsync($"waiting for {selector} to hide", () => page!.Locator(selector).First.WaitForAsync(new() { State = WaitForSelectorState.Hidden }));

    /// <summary>
    /// Whether <paramref name="selector"/> matches a visible element right now - no
    /// wait, so a negative assertion ("the overlay never appears") cannot pass
    /// just by outrunning a redraw with a wait that times out and is swallowed.
    /// False when nothing matches at all.
    /// </summary>
    public Task<bool> IsVisibleAsync(Deadline deadline, string selector) =>
        deadline.BoundAsync($"checking {selector}", () => page!.Locator(selector).First.IsVisibleAsync());

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
    /// Waits until exactly the columns at <paramref name="narrowIndices"/> (zero-based, board order)
    /// carry <c>hatch-column--narrow</c>, then reads every column's drawn width. The wait is on
    /// the class, which React sets in the render that changes the width; run with reduced motion
    /// the width follows at once.
    /// </summary>
    public async Task<IReadOnlyList<double>> MeasureColumnWidthsAsync(Deadline deadline, IReadOnlyList<int> narrowIndices)
    {
        await deadline.BoundAsync($"waiting for narrow columns [{string.Join(", ", narrowIndices)}]", () =>
            page!.WaitForFunctionAsync(
                """
                ([selector, expected]) => {
                  const narrow = [...document.querySelectorAll(selector)]
                    .map((c, i) => (c.classList.contains('hatch-column--narrow') ? i : -1))
                    .filter((i) => i >= 0);
                  return narrow.length === expected.length && narrow.every((i, n) => i === expected[n]);
                }
                """,
                new object[] { Column, narrowIndices }));

        var read = await deadline.BoundAsync("measuring the columns", () =>
            page!.EvaluateAsync<JsonElement>(
                "(selector) => [...document.querySelectorAll(selector)].map((c) => c.getBoundingClientRect().width)", Column));

        return read.EnumerateArray().Select(w => w.GetDouble()).ToList();
    }

    /// <summary>Types into the board's search box, replacing what is there; empty clears it.</summary>
    public Task SearchBoardAsync(Deadline deadline, string text) =>
        deadline.BoundAsync($"searching the board for \"{text}\"", () =>
            page!.Locator("input[aria-label=\"Search the board\"]").FillAsync(text));

    /// <summary>Whether the <paramref name="index"/>th column (zero-based) is saying "N hidden by the filter".</summary>
    public Task<bool> ColumnSaysHiddenAsync(Deadline deadline, int index) =>
        deadline.BoundAsync("reading the column's hidden note", () =>
            page!.Locator(Column).Nth(index).Locator(".hatch-column-hidden").IsVisibleAsync());

    /// <summary>The open page, for a case that fills, clicks or reads it directly; null until <see cref="OpenAsync"/> has run. Wrap each call in the case's own <c>deadline.BoundAsync</c>.</summary>
    public IPage? Page => page;

    /// <summary>Saves the page as <c>{label}.png</c> in the case's artifact directory, whether or not the case fails, and returns the path.</summary>
    public async Task<string> ScreenshotAsync(Deadline deadline, string label)
    {
        var directory = ArtifactDirectory();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{label}.png");
        await deadline.BoundAsync($"taking the {label} screenshot", () => page!.ScreenshotAsync(new() { Path = path }));
        return path;
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
                .ToList(),
            read.GetProperty("search").ValueKind == JsonValueKind.Null ? null : ReadBox(read.GetProperty("search")));
    }

    /// <summary>The open Types panel's Only buttons and the panel itself as the page stands; the panel must be open.</summary>
    public async Task<TypesOnlyMeasurement> MeasureTypesOnlyAsync(Deadline deadline)
    {
        var read = await deadline.BoundAsync("measuring the Types panel", () =>
            page!.EvaluateAsync<JsonElement>(MeasureTypesOnlyScript, TypesOnlySelector));

        return new TypesOnlyMeasurement(
            read.GetProperty("innerWidth").GetInt32(),
            read.GetProperty("scrollWidth").GetInt32(),
            read.GetProperty("clientWidth").GetInt32(),
            read.GetProperty("coarsePointerMatches").GetBoolean(),
            read.GetProperty("panel").ValueKind == JsonValueKind.Null ? null : ReadBox(read.GetProperty("panel")),
            read.GetProperty("only").EnumerateArray()
                .Select(c => (c.GetProperty("name").GetString()!, ReadBox(c.GetProperty("box"))))
                .ToList());
    }

    /// <summary>Resizes the open page, for a case that asks about a width other than the launch profile's.</summary>
    public Task SetViewportAsync(Deadline deadline, int width, int height) =>
        deadline.BoundAsync($"resizing the viewport to {width}x{height}", () =>
            page!.SetViewportSizeAsync(width, height));

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

    /// <summary>The Details card, every Omitted runners row and the add form's elements as the page stands; the card is its border box.</summary>
    public async Task<OmittedRunnersMeasurement> MeasureOmittedRunnersAsync(Deadline deadline)
    {
        var read = await deadline.BoundAsync("measuring the Omitted runners section", () =>
            page!.EvaluateAsync<JsonElement>(MeasureOmittedRunnersScript));

        Assert.True(read.ValueKind != JsonValueKind.Null, "the Omitted runners add form is not inside a card on the page");

        return new OmittedRunnersMeasurement(
            read.GetProperty("innerWidth").GetInt32(),
            read.GetProperty("scrollWidth").GetInt32(),
            read.GetProperty("clientWidth").GetInt32(),
            ReadBox(read.GetProperty("card")),
            read.GetProperty("elements").EnumerateArray().Select(e => new OmissionElement(
                e.GetProperty("name").GetString()!,
                e.GetProperty("control").GetBoolean(),
                ReadBox(e.GetProperty("box")),
                e.GetProperty("scrollWidth").GetInt32(),
                e.GetProperty("clientWidth").GetInt32())).ToList());
    }

    /// <summary>Each Runners row's Token budget cell and activity badge as the page stands.</summary>
    public async Task<RunnerBudgetsMeasurement> MeasureRunnerBudgetsAsync(Deadline deadline)
    {
        var read = await deadline.BoundAsync("measuring the Runners token budgets", () =>
            page!.EvaluateAsync<JsonElement>(MeasureRunnerBudgetsScript));

        Assert.True(read.ValueKind != JsonValueKind.Null, "the Runners table has no Token budget or Bounds column");

        return new RunnerBudgetsMeasurement(
            read.GetProperty("scrollWidth").GetInt32(),
            read.GetProperty("clientWidth").GetInt32(),
            read.GetProperty("rows").EnumerateArray().Select(r => new RunnerBudgetRow(
                r.GetProperty("name").GetString()!,
                r.GetProperty("cellText").GetString()!,
                r.GetProperty("boundsText").GetString()!,
                r.GetProperty("lines").EnumerateArray().Select(l => l.GetString()!).ToList(),
                r.GetProperty("bars").EnumerateArray().Select(b => new BudgetBar(
                    b.GetProperty("fillWidth").GetDouble(),
                    b.GetProperty("trackWidth").GetDouble(),
                    b.GetProperty("styleWidth").GetString()!,
                    b.GetProperty("usageColor").GetString()!,
                    b.GetProperty("color").GetString()!)).ToList(),
                r.GetProperty("badge").GetString()!)).ToList());
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

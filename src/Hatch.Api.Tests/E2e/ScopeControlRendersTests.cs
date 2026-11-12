using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-1825's floor, rendered: the topbar's scope control on the desk
/// (Chromium, 1280x800), unscoped and scoped, in both themes, and absent on a
/// one-project install. The script is the one the ticket gives, in order -
/// one project, three projects unscoped, the panel open, the scope walked down
/// to All again, then the dark theme - and every violation across it is
/// collected and asserted together, so one run shows every failure rather than
/// the first.
///
/// <para>Three projects are seeded, not two, because <c>knownKeys</c> in
/// <c>lib/scope.ts</c> turns "every project named" into All: with two, ticking
/// one off and back on could never be told from never having scoped.</para>
///
/// <para>The four screenshots land in the case's artifact directory through
/// <see cref="E2eHarness.ScreenshotAsync"/>; whether the chips read in both
/// themes is the screenshot's call, not an assertion. The phone fact
/// (<see cref="TheScopeControl_RendersOnThePhone"/>, the <c>iPhone 14 Pro</c>
/// WebKit profile) is the same walk: the steps are methods taking the harness,
/// the deadline, the base address and the violation list, with the phone's own
/// steps beside them.</para>
///
/// <para><see cref="Trigger"/> is not prefixed with the top bar because the
/// control is not always in it: on the desk it leads the bar, on a phone
/// <c>App.tsx</c> draws it in <c>.hatch-scope-strip</c>, a sibling after the bar.
/// Only <see cref="NavSelector"/> keeps the prefix, and on a phone it must match
/// nothing.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class ScopeControlRendersTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 120;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    /// <summary>Sub-pixel layout is fractional; two boxes that touch are not overlapping.</summary>
    private const double Tolerance = 0.5;

    /// <summary>How far apart two vertical centres may sit and still be on one line, in CSS pixels.</summary>
    private const double CentreTolerance = 1;

    /// <summary>How far the board's height arithmetic may be off, in CSS pixels.</summary>
    private const double BoardFitsTolerance = 1;

    /// <summary>The strip's <c>min-height</c> in <c>App.css</c>, in CSS pixels.</summary>
    private const double StripHeight = 44;

    /// <summary>The trigger in either state, in the bar on the desk and in the strip on the phone: <c>ScopeControl.tsx</c> gives both states the Menu's label as its accessible name (scoped, that is "Projects in scope: " and the names), and no other trigger starts with it.</summary>
    private const string Trigger = "button.hatch-menu__trigger[aria-label^=\"Projects in scope\"]";

    /// <summary>The phone's strip, a sibling after the top bar in <c>App.tsx</c>, mounted only with two or more projects.</summary>
    private const string Strip = ".hatch-scope-strip";

    /// <summary>The board page, which exists once the board has loaded.</summary>
    private const string Board = ".hatch-board-page";

    private const string Chip = ".hatch-scope-control__chip";

    private const string Panel = ".hatch-types-panel";

    private const string Row = ".hatch-types-item";

    private const string NavSelector = E2eHarness.TopBar + " .hatch-primary-nav";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheScopeControl_RendersOnTheDesk()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("ScopeControlDesk");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);

            var violations = new List<string>();

            await deadline.BoundAsync("filing the first project", () => FileProjectsAsync(baseUrl, ("AAA", null)));
            await OneProjectDrawsNoControlAsync(harness, deadline, baseUrl, violations);

            await deadline.BoundAsync("filing two more projects", () => FileProjectsAsync(baseUrl, ("BBB", "#2f9e44"), ("CCC", "#d6336c")));
            await UnscopedAsync(harness, deadline, baseUrl, violations);
            await OpenThePanelAsync(harness, deadline, baseUrl, violations);
            await OnlyNarrowsToOneProjectAsync(harness, deadline, baseUrl, violations);
            await WalkTheScopeDownAsync(harness, deadline, baseUrl, violations);
            await DarkAsync(harness, deadline, baseUrl, violations);

            Assert.True(
                violations.Count == 0,
                $"The scope control does not render as asked ({violations.Count} violation(s)):{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");

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
    /// The same script on the phone (393x660): the strip under the bar rather than
    /// the control in it, and the board's height arithmetic. Every shared step
    /// runs unchanged; the phone's own steps sit between them.
    /// </summary>
    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheScopeControl_RendersOnThePhone()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);

        var harness = new E2eHarness("ScopeControlPhone");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline, DeviceProfile.IPhone14Pro);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);

            var violations = new List<string>();

            await deadline.BoundAsync("filing the first project", () => FileProjectsAsync(baseUrl, ("AAA", null)));
            await OneProjectDrawsNoControlAsync(harness, deadline, baseUrl, violations);
            await NoStripOnOneProjectAsync(harness, deadline, baseUrl, violations);

            await deadline.BoundAsync("filing two more projects", () => FileProjectsAsync(baseUrl, ("BBB", "#2f9e44"), ("CCC", "#d6336c")));
            await UnscopedOnThePhoneAsync(harness, deadline, baseUrl, violations);
            await OpenThePanelAsync(harness, deadline, baseUrl, violations);
            await PanelBelowTheStripAsync(harness, deadline, baseUrl, violations);
            await WalkTheScopeDownAsync(harness, deadline, baseUrl, violations);
            await ScopedOnThePhoneAsync(harness, deadline, baseUrl, violations);
            await DarkAsync(harness, deadline, baseUrl, violations);

            Assert.True(
                violations.Count == 0,
                $"The scope control does not render as asked on the phone ({violations.Count} violation(s)):{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");

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
    /// Look 4. A negative assertion must not pass by outrunning the project
    /// fetch, so the page is held until the fetch has answered and two frames
    /// have been drawn after it.
    /// </summary>
    private async Task OneProjectDrawsNoControlAsync(E2eHarness harness, Deadline deadline, string baseUrl, List<string> violations)
    {
        await harness.OpenAsync(deadline, baseUrl, "/");
        await harness.WaitForTopBarAsync(deadline);
        await harness.WaitForFirstColumnAsync(deadline);

        await harness.WaitForFunctionAsync(
            deadline,
            "waiting for the project fetch to have answered",
            "() => performance.getEntriesByType('resource').some(e => e.name.includes('/api/projects'))");
        await harness.EvaluateAsync<bool>(
            deadline,
            "waiting two frames after the project fetch",
            "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve(true))))");

        var visible = await harness.IsVisibleAsync(deadline, Trigger);
        var title = await TitleAsync(harness, deadline);
        output.WriteLine($"--- one project: trigger visible {visible}, title \"{title}\"");

        if (visible) violations.Add("one project: the scope control is drawn on a one-project install");
        if (title != "Hatch") violations.Add($"one project: the tab title is \"{title}\", not \"Hatch\"");
    }

    /// <summary>Looks 1 and 3 unscoped: beside the nav, quiet, no chip, the plain title.</summary>
    private async Task UnscopedAsync(E2eHarness harness, Deadline deadline, string baseUrl, List<string> violations)
    {
        await harness.GoToAsync(deadline, "");
        await harness.WaitForTopBarAsync(deadline);
        await harness.WaitForVisibleAsync(deadline, Trigger);

        var m = await harness.EvaluateAsync<JsonElement>(
            deadline,
            "measuring the unscoped trigger",
            $$"""
            () => {
              const trigger = document.querySelector('{{Trigger}}');
              const nav = document.querySelector('{{NavSelector}}');
              const link = nav.querySelector('.hatch-menu__trigger:not(.hatch-menu__trigger--active)');
              const t = trigger.getBoundingClientRect();
              const n = nav.getBoundingClientRect();
              return {
                triggerCentre: t.top + t.height / 2,
                navCentre: n.top + n.height / 2,
                triggerLeft: t.left,
                navRight: n.right,
                triggerColor: getComputedStyle(trigger).color,
                linkColor: getComputedStyle(link).color,
                text: trigger.textContent.trim(),
                scoped: trigger.classList.contains('hatch-scope-control--scoped'),
                chips: document.querySelectorAll('{{Chip}}').length,
                caret: trigger.querySelectorAll('.hatch-menu__caret').length,
              };
            }
            """);
        var title = await TitleAsync(harness, deadline);
        output.WriteLine($"--- three projects, unscoped: {m}, title \"{title}\"");

        var triggerCentre = m.GetProperty("triggerCentre").GetDouble();
        var navCentre = m.GetProperty("navCentre").GetDouble();
        var triggerLeft = m.GetProperty("triggerLeft").GetDouble();
        var navRight = m.GetProperty("navRight").GetDouble();

        if (Math.Abs(triggerCentre - navCentre) > CentreTolerance)
            violations.Add($"unscoped: the trigger's vertical centre ({triggerCentre}) is not within {CentreTolerance}px of the nav's ({navCentre})");
        if (triggerLeft < navRight - Tolerance)
            violations.Add($"unscoped: the trigger starts at {triggerLeft}, left of the nav's right edge {navRight}");
        if (m.GetProperty("text").GetString() != "All projects" || m.GetProperty("scoped").GetBoolean())
            violations.Add($"unscoped: the trigger reads \"{m.GetProperty("text").GetString()}\" and is not the unscoped one");
        if (m.GetProperty("triggerColor").GetString() != m.GetProperty("linkColor").GetString())
            violations.Add($"unscoped: the trigger's colour {m.GetProperty("triggerColor")} is not a nav link's {m.GetProperty("linkColor")}");
        if (m.GetProperty("chips").GetInt32() != 0)
            violations.Add($"unscoped: {m.GetProperty("chips").GetInt32()} chip(s) drawn with nothing scoped");
        if (m.GetProperty("caret").GetInt32() == 0)
            violations.Add("unscoped: the trigger draws no caret");
        if (title != "Hatch") violations.Add($"unscoped: the tab title is \"{title}\", not \"Hatch\"");

        output.WriteLine($"screenshot: {await harness.ScreenshotAsync(deadline, "unscoped-light")}");
    }

    /// <summary>
    /// One project draws no strip. Called straight after
    /// <see cref="OneProjectDrawsNoControlAsync"/>, whose waits have already
    /// outrun the project fetch, so the absence is not an early look.
    /// </summary>
    private async Task NoStripOnOneProjectAsync(E2eHarness harness, Deadline deadline, string baseUrl, List<string> violations)
    {
        var visible = await harness.IsVisibleAsync(deadline, Strip);
        output.WriteLine($"--- one project: strip visible {visible}");

        if (visible) violations.Add("one project: the scope strip is drawn on a one-project install");
    }

    /// <summary>
    /// Looks 1 and 3 unscoped, on the phone: no nav in the bar, the strip flush
    /// under the bar and 44px tall with the trigger inside it, quiet, no chip,
    /// the plain title, and the board filling what the bar and strip leave.
    /// </summary>
    private async Task UnscopedOnThePhoneAsync(E2eHarness harness, Deadline deadline, string baseUrl, List<string> violations)
    {
        await harness.GoToAsync(deadline, "");
        await harness.WaitForTopBarAsync(deadline);
        await harness.WaitForVisibleAsync(deadline, Trigger);

        var m = await harness.EvaluateAsync<JsonElement>(
            deadline,
            "measuring the unscoped strip",
            $$"""
            () => {
              const box = el => {
                const r = el.getBoundingClientRect();
                return { top: r.top, left: r.left, right: r.right, bottom: r.bottom, height: r.height };
              };
              const trigger = document.querySelector('{{Trigger}}');
              return {
                barBottom: document.querySelector('{{E2eHarness.TopBar}}').getBoundingClientRect().bottom,
                strip: box(document.querySelector('{{Strip}}')),
                trigger: box(trigger),
                text: trigger.textContent.trim(),
                scoped: trigger.classList.contains('hatch-scope-control--scoped'),
                chips: document.querySelectorAll('{{Chip}}').length,
                caret: trigger.querySelectorAll('.hatch-menu__caret').length,
                nav: document.querySelectorAll('{{NavSelector}}').length,
              };
            }
            """);
        var title = await TitleAsync(harness, deadline);
        output.WriteLine($"--- three projects, unscoped, phone: {m}, title \"{title}\"");

        var barBottom = m.GetProperty("barBottom").GetDouble();
        var strip = m.GetProperty("strip");
        var stripTop = strip.GetProperty("top").GetDouble();
        var stripHeight = strip.GetProperty("height").GetDouble();

        if (m.GetProperty("nav").GetInt32() != 0)
            violations.Add($"unscoped: {m.GetProperty("nav").GetInt32()} primary nav(s) in the bar on a phone");
        if (Math.Abs(stripTop - barBottom) > Tolerance)
            violations.Add($"unscoped: the strip starts at {stripTop}, not at the bar's bottom {barBottom}");
        if (Math.Abs(stripHeight - StripHeight) > Tolerance)
            violations.Add($"unscoped: the strip is {stripHeight}px tall, not {StripHeight}");
        if (!Inside(m.GetProperty("trigger"), strip))
            violations.Add($"unscoped: the trigger {m.GetProperty("trigger")} is not inside the strip {strip}");
        if (m.GetProperty("text").GetString() != "All projects" || m.GetProperty("scoped").GetBoolean())
            violations.Add($"unscoped: the trigger reads \"{m.GetProperty("text").GetString()}\" and is not the unscoped one");
        if (m.GetProperty("chips").GetInt32() != 0)
            violations.Add($"unscoped: {m.GetProperty("chips").GetInt32()} chip(s) drawn with nothing scoped");
        if (m.GetProperty("caret").GetInt32() == 0)
            violations.Add("unscoped: the trigger draws no caret");
        if (title != "Hatch") violations.Add($"unscoped: the tab title is \"{title}\", not \"Hatch\"");

        await BoardFitsAsync(harness, deadline, "unscoped", violations);

        output.WriteLine($"screenshot: {await harness.ScreenshotAsync(deadline, "unscoped-light")}");
    }

    /// <summary>The open panel hangs below the strip, not over it. Called straight after <see cref="OpenThePanelAsync"/>, with the panel still open.</summary>
    private async Task PanelBelowTheStripAsync(E2eHarness harness, Deadline deadline, string baseUrl, List<string> violations)
    {
        var m = await harness.EvaluateAsync<JsonElement>(
            deadline,
            "measuring the panel against the strip",
            $$"""
            () => ({
              panelTop: document.querySelector('{{Panel}}').getBoundingClientRect().top,
              stripBottom: document.querySelector('{{Strip}}').getBoundingClientRect().bottom,
            })
            """);
        output.WriteLine($"--- panel against the strip: {m}");

        var panelTop = m.GetProperty("panelTop").GetDouble();
        var stripBottom = m.GetProperty("stripBottom").GetDouble();
        if (panelTop < stripBottom - Tolerance)
            violations.Add($"panel: starts at {panelTop}, above the strip's bottom {stripBottom}");
    }

    /// <summary>
    /// Look 1 scoped, on the phone: a load with a scope, the trigger within the
    /// screen with its chips, and the board's arithmetic again. Called after
    /// <see cref="WalkTheScopeDownAsync"/>, which ends unscoped with the panel
    /// shut, so the strip is measured undisturbed; the theme is still light and
    /// the walk and <see cref="DarkAsync"/> already write the scoped screenshots.
    /// </summary>
    private async Task ScopedOnThePhoneAsync(E2eHarness harness, Deadline deadline, string baseUrl, List<string> violations)
    {
        await harness.GoToAsync(deadline, "?scope=AAA,BBB");
        await harness.WaitForVisibleAsync(deadline, Chip);

        var m = await harness.EvaluateAsync<JsonElement>(
            deadline,
            "measuring the scoped trigger",
            $$"""
            () => {
              const r = document.querySelector('{{Trigger}}').getBoundingClientRect();
              return { left: r.left, right: r.right, innerWidth: window.innerWidth };
            }
            """);
        output.WriteLine($"--- scoped, phone: {m}");

        var left = m.GetProperty("left").GetDouble();
        var right = m.GetProperty("right").GetDouble();
        var innerWidth = m.GetProperty("innerWidth").GetDouble();
        if (left < -Tolerance || right > innerWidth + Tolerance)
            violations.Add($"scoped: the trigger lies at [{left}, {right}], outside [0, {innerWidth}]");

        await BoardFitsAsync(harness, deadline, "scoped", violations);
    }

    /// <summary>
    /// The board's <c>min-height</c> is <c>100dvh - 48px</c>, less the strip's 44px
    /// when it is drawn (<c>App.css</c>): the bar, the strip and the board must
    /// add up to the viewport. scrollHeight and clientHeight are logged and not
    /// asserted, since a stacked board can legitimately scroll.
    /// </summary>
    private async Task BoardFitsAsync(E2eHarness harness, Deadline deadline, string state, List<string> violations)
    {
        await harness.WaitForVisibleAsync(deadline, Board);

        var m = await harness.EvaluateAsync<JsonElement>(
            deadline,
            $"measuring the board's height ({state})",
            $$"""
            () => {
              const raw = getComputedStyle(document.querySelector('{{Board}}')).minHeight;
              const px = parseFloat(raw);
              return {
                barBottom: document.querySelector('{{E2eHarness.TopBar}}').getBoundingClientRect().bottom,
                stripHeight: document.querySelector('{{Strip}}').getBoundingClientRect().height,
                minHeightRaw: raw,
                minHeight: Number.isFinite(px) ? px : null,
                innerHeight: window.innerHeight,
                scrollHeight: document.documentElement.scrollHeight,
                clientHeight: document.documentElement.clientHeight,
              };
            }
            """);
        output.WriteLine($"--- board fits, {state}: {m}");

        var minHeightRaw = m.GetProperty("minHeightRaw").GetString();
        if (m.GetProperty("minHeight").ValueKind != JsonValueKind.Number)
        {
            violations.Add($"board {state}: the board's min-height is \"{minHeightRaw}\", not a length in pixels");
            return;
        }

        var barBottom = m.GetProperty("barBottom").GetDouble();
        var stripHeight = m.GetProperty("stripHeight").GetDouble();
        var minHeight = m.GetProperty("minHeight").GetDouble();
        var innerHeight = m.GetProperty("innerHeight").GetDouble();
        if (Math.Abs(barBottom + stripHeight + minHeight - innerHeight) > BoardFitsTolerance)
            violations.Add(
                $"board {state}: bar bottom {barBottom} + strip {stripHeight} + board min-height {minHeight} (\"{minHeightRaw}\") " +
                $"is {barBottom + stripHeight + minHeight}, not the viewport's {innerHeight}");
    }

    /// <summary>Whether <paramref name="inner"/> lies within <paramref name="outer"/>, both <c>{ top, left, right, bottom }</c> boxes, to <see cref="Tolerance"/>.</summary>
    private static bool Inside(JsonElement inner, JsonElement outer) =>
        inner.GetProperty("left").GetDouble() >= outer.GetProperty("left").GetDouble() - Tolerance
        && inner.GetProperty("right").GetDouble() <= outer.GetProperty("right").GetDouble() + Tolerance
        && inner.GetProperty("top").GetDouble() >= outer.GetProperty("top").GetDouble() - Tolerance
        && inner.GetProperty("bottom").GetDouble() <= outer.GetProperty("bottom").GetDouble() + Tolerance;

    /// <summary>Look 2, the opening: a row per project, all ticked, the footer All button disabled, the panel on screen.</summary>
    private async Task OpenThePanelAsync(E2eHarness harness, Deadline deadline, string baseUrl, List<string> violations)
    {
        await harness.ClickAsync(deadline, Trigger);
        await harness.WaitForVisibleAsync(deadline, Panel);

        var m = await harness.EvaluateAsync<JsonElement>(
            deadline,
            "measuring the open panel",
            $$"""
            () => {
              const panel = document.querySelector('{{Panel}}');
              const box = panel.getBoundingClientRect();
              return {
                rows: panel.querySelectorAll('{{Row}}').length,
                checked: [...panel.querySelectorAll('{{Row}} input[type=checkbox]')].map(c => c.checked),
                allDisabled: panel.querySelector('.hatch-types-footer .hatch-filter-clear').disabled,
                left: box.left,
                right: box.right,
                innerWidth: window.innerWidth,
              };
            }
            """);
        output.WriteLine($"--- panel open: {m}");

        if (m.GetProperty("rows").GetInt32() != 3)
            violations.Add($"panel: {m.GetProperty("rows").GetInt32()} row(s), not one per project (3)");
        if (m.GetProperty("checked").EnumerateArray().Any(c => !c.GetBoolean()) || m.GetProperty("checked").GetArrayLength() != 3)
            violations.Add($"panel: not every checkbox is ticked with nothing scoped ({m.GetProperty("checked")})");
        if (!m.GetProperty("allDisabled").GetBoolean())
            violations.Add("panel: the footer All button is enabled with nothing scoped");

        var left = m.GetProperty("left").GetDouble();
        var right = m.GetProperty("right").GetDouble();
        var innerWidth = m.GetProperty("innerWidth").GetDouble();
        if (left < -Tolerance || right > innerWidth + Tolerance)
            violations.Add($"panel: lies at [{left}, {right}], outside [0, {innerWidth}]");

        // From here a reload would be a failure: the scope is written by replace, in place.
        await harness.EvaluateAsync<bool>(deadline, "marking the page", "() => { window.__noReload = 1; return true; }");
    }

    /// <summary>
    /// HA-2201: each row carries its own Only, the footer carries no global None
    /// beside All (one here means every project, so a None would just be
    /// another All), and a click on a row's Only narrows the scope to that one
    /// project alone, by replace, with the panel left open. Called with the
    /// panel open and every project ticked, and leaves it that way for
    /// <see cref="WalkTheScopeDownAsync"/>.
    /// </summary>
    private async Task OnlyNarrowsToOneProjectAsync(E2eHarness harness, Deadline deadline, string baseUrl, List<string> violations)
    {
        var footer = await harness.EvaluateAsync<JsonElement>(
            deadline,
            "reading the footer and the rows' Only buttons",
            $$"""
            () => {
              const panel = document.querySelector('{{Panel}}');
              return {
                footerButtons: [...panel.querySelectorAll('.hatch-types-footer button')].map(b => b.textContent.trim()),
                onlyLabels: [...panel.querySelectorAll('{{Row}} .hatch-types-only')].map(b => b.getAttribute('aria-label')),
              };
            }
            """);
        output.WriteLine($"--- footer and Only buttons: {footer}");

        var footerButtons = footer.GetProperty("footerButtons").EnumerateArray().Select(b => b.GetString()).ToList();
        if (!footerButtons.SequenceEqual(["All"]))
            violations.Add($"only: the footer reads [{string.Join(", ", footerButtons)}], not just [\"All\"] (no global None)");

        var onlyLabels = footer.GetProperty("onlyLabels").EnumerateArray().Select(b => b.GetString()).ToList();
        if (!onlyLabels.SequenceEqual(["Only AAA — Project AAA", "Only BBB — Project BBB", "Only CCC — Project CCC"]))
            violations.Add($"only: the rows' Only buttons read [{string.Join(", ", onlyLabels)}]");

        var history = await harness.EvaluateAsync<int>(deadline, "reading the history length before Only", "() => history.length");

        await harness.ClickAsync(deadline, $"{Panel} >> {Row} >> nth=1 >> .hatch-types-only");
        await WaitForScopeAsync(harness, deadline, "BBB", "BBB - Hatch");
        await CheckScopedAsync(harness, deadline, "only BBB", ["BBB"], history, violations);

        var panelOpen = await harness.IsVisibleAsync(deadline, Panel);
        if (!panelOpen) violations.Add("only: the panel closed on its own click");

        // Back to every project ticked, so the walk that follows starts from the state it expects.
        await EnsurePanelOpenAsync(harness, deadline);
        await harness.ClickAsync(deadline, $"{Panel} >> .hatch-types-footer button:text-is(\"All\")");
        await WaitForScopeAsync(harness, deadline, "", "Hatch");
        await EnsurePanelOpenAsync(harness, deadline);
    }

    /// <summary>Looks 2 and 3: untick down to one project and then to none, the address and the title following each tick, with no reload and no history entry.</summary>
    private async Task WalkTheScopeDownAsync(E2eHarness harness, Deadline deadline, string baseUrl, List<string> violations)
    {
        var history = await harness.EvaluateAsync<int>(deadline, "reading the history length", "() => history.length");
        output.WriteLine($"--- history.length before the walk: {history}");

        await UntickAsync(harness, deadline, 2);
        await WaitForScopeAsync(harness, deadline, "AAA,BBB", "AAA, BBB - Hatch");
        await CheckScopedAsync(harness, deadline, "AAA, BBB", ["AAA", "BBB"], history, violations);
        await HintCoversTheWholeTriggerAsync(harness, deadline, "AAA, BBB", "Projects in scope: Project AAA, Project BBB", violations);
        output.WriteLine($"screenshot: {await harness.ScreenshotAsync(deadline, "scoped-light")}");

        // The panel may or may not have stayed open under the tick; the case copes with either.
        await EnsurePanelOpenAsync(harness, deadline);
        await UntickAsync(harness, deadline, 1);
        await WaitForScopeAsync(harness, deadline, "AAA", "AAA - Hatch");
        await CheckScopedAsync(harness, deadline, "AAA", ["AAA"], history, violations);

        await EnsurePanelOpenAsync(harness, deadline);
        await UntickAsync(harness, deadline, 0);
        await WaitForScopeAsync(harness, deadline, "", "Hatch");

        var m = await harness.EvaluateAsync<JsonElement>(
            deadline,
            "reading the unscoped state again",
            $$"""
            () => ({
              hasScope: new URLSearchParams(location.search).has('scope'),
              text: document.querySelector('{{Trigger}}').textContent.trim(),
              chips: document.querySelectorAll('{{Chip}}').length,
              reload: window.__noReload ?? null,
              history: history.length,
            })
            """);
        output.WriteLine($"--- back to All: {m}");

        if (m.GetProperty("hasScope").GetBoolean()) violations.Add("walk: unticking the last project left a scope in the address");
        if (m.GetProperty("text").GetString() != "All projects")
            violations.Add($"walk: unticking the last project left the trigger reading \"{m.GetProperty("text").GetString()}\", not \"All projects\"");
        if (m.GetProperty("chips").GetInt32() != 0)
            violations.Add($"walk: {m.GetProperty("chips").GetInt32()} chip(s) remain after unticking the last project");
        if (m.GetProperty("reload").ValueKind != JsonValueKind.Number || m.GetProperty("reload").GetInt32() != 1)
            violations.Add("walk: the page reloaded back to All (window.__noReload is gone)");
        if (m.GetProperty("history").GetInt32() != history)
            violations.Add($"walk: history.length is {m.GetProperty("history").GetInt32()} after the walk, not {history}: the scope was pushed, not replaced");
    }

    /// <summary>
    /// The hint is the button's own <c>title</c>, so it shows over every part of the
    /// box. A headless screenshot cannot show a native tooltip, so this stands in for
    /// the hover: at each corner (2px in), each edge midpoint and the caret's centre,
    /// the innermost titled element under the point must be the trigger.
    /// </summary>
    private async Task HintCoversTheWholeTriggerAsync(
        E2eHarness harness, Deadline deadline, string step, string words, List<string> violations)
    {
        var m = await harness.EvaluateAsync<JsonElement>(
            deadline,
            $"probing the trigger's hint ({step})",
            $$"""
            () => {
              window.scrollTo(0, 0);
              const trigger = document.querySelector('{{Trigger}}');
              const r = trigger.getBoundingClientRect();
              const caret = trigger.querySelector('.hatch-menu__caret').getBoundingClientRect();
              const l = r.left + 2, t = r.top + 2, rt = r.right - 2, b = r.bottom - 2;
              const cx = r.left + r.width / 2, cy = r.top + r.height / 2;
              const points = {
                topLeft: [l, t], top: [cx, t], topRight: [rt, t], right: [rt, cy],
                bottomRight: [rt, b], bottom: [cx, b], bottomLeft: [l, b], left: [l, cy],
                caret: [caret.left + caret.width / 2, caret.top + caret.height / 2],
              };
              const missed = Object.entries(points)
                .filter(([, [x, y]]) => document.elementFromPoint(x, y)?.closest('[title]') !== trigger)
                .map(([name, [x, y]]) => {
                  const hit = document.elementFromPoint(x, y);
                  return `${name} (hit <${hit?.tagName.toLowerCase()} class="${hit?.className}">)`;
                });
              return { title: trigger.title, missed };
            }
            """);
        output.WriteLine($"--- hint probe {step}: {m}");

        if (m.GetProperty("title").GetString() != words)
            violations.Add($"hint {step}: the trigger's title is \"{m.GetProperty("title").GetString()}\", not \"{words}\"");
        foreach (var point in m.GetProperty("missed").EnumerateArray())
            violations.Add($"hint {step}: the trigger's own title is not the one under its {point.GetString()} point");
    }

    /// <summary>Look 1 scoped, and the no-reload and replace-not-push claims, at one step of the walk.</summary>
    private async Task CheckScopedAsync(
        E2eHarness harness, Deadline deadline, string step, string[] keys, int history, List<string> violations)
    {
        var m = await harness.EvaluateAsync<JsonElement>(
            deadline,
            $"reading the scoped state ({step})",
            $$"""
            () => ({
              title: document.title,
              chips: [...document.querySelectorAll('{{Chip}}')].map(c => ({ text: c.textContent.trim(), hasMark: c.querySelector('.hatch-project-mark') !== null })),
              triggerScoped: document.querySelector('{{Trigger}}').classList.contains('hatch-scope-control--scoped'),
              reload: window.__noReload ?? null,
              history: history.length,
            })
            """);
        output.WriteLine($"--- scoped {step}: {m}");

        var chips = m.GetProperty("chips").EnumerateArray().ToList();
        if (chips.Count != keys.Length)
            violations.Add($"scoped {step}: {chips.Count} chip(s), not {keys.Length}");
        else
            for (var i = 0; i < keys.Length; i++)
            {
                var text = chips[i].GetProperty("text").GetString() ?? "";
                if (text != keys[i])
                    violations.Add($"scoped {step}: chip {i} reads \"{text}\", not exactly {keys[i]}");
                if (chips[i].GetProperty("hasMark").GetBoolean())
                    violations.Add($"scoped {step}: chip {i} ({keys[i]}) draws a project mark inside a same-colour chip");
            }

        if (!m.GetProperty("triggerScoped").GetBoolean())
            violations.Add($"scoped {step}: the trigger does not carry hatch-scope-control--scoped");
        if (m.GetProperty("reload").ValueKind != JsonValueKind.Number || m.GetProperty("reload").GetInt32() != 1)
            violations.Add($"scoped {step}: the page reloaded (window.__noReload is gone)");
        if (m.GetProperty("history").GetInt32() != history)
            violations.Add($"scoped {step}: history.length is {m.GetProperty("history").GetInt32()}, not {history}: the scope was pushed, not replaced");
        if (m.GetProperty("title").GetString() != $"{string.Join(", ", keys)} - Hatch")
            violations.Add($"scoped {step}: the tab title is \"{m.GetProperty("title").GetString()}\"");
    }

    /// <summary>Look 1 in the dark theme: the stored choice, a load with a scope and then one without. Legibility is the screenshots' call.</summary>
    private async Task DarkAsync(E2eHarness harness, Deadline deadline, string baseUrl, List<string> violations)
    {
        // The key and values are THEME_STORAGE_KEY's, in packages/ui/src/theme/themeStore.ts.
        await harness.EvaluateAsync<bool>(deadline, "choosing the dark theme", "() => { localStorage.setItem('hatch.theme', 'dark'); return true; }");

        await harness.GoToAsync(deadline, "?scope=AAA,BBB");
        await harness.WaitForTopBarAsync(deadline);
        await harness.WaitForVisibleAsync(deadline, Chip);
        var scoped = await VisibleAsync(harness, deadline, Trigger, Chip);
        output.WriteLine($"--- dark, scoped: trigger visible {scoped.trigger}, chip visible {scoped.chip}");
        if (!scoped.trigger || !scoped.chip) violations.Add("dark, scoped: the trigger or its chips are not visible");
        output.WriteLine($"screenshot: {await harness.ScreenshotAsync(deadline, "scoped-dark")}");

        await harness.GoToAsync(deadline, "");
        await harness.WaitForTopBarAsync(deadline);
        await harness.WaitForVisibleAsync(deadline, Trigger);
        var unscoped = await harness.IsVisibleAsync(deadline, Trigger);
        output.WriteLine($"--- dark, unscoped: trigger visible {unscoped}");
        if (!unscoped) violations.Add("dark, unscoped: the trigger is not visible");
        output.WriteLine($"screenshot: {await harness.ScreenshotAsync(deadline, "unscoped-dark")}");
    }

    private static async Task<(bool trigger, bool chip)> VisibleAsync(E2eHarness harness, Deadline deadline, string trigger, string chip) =>
        (await harness.IsVisibleAsync(deadline, trigger), await harness.IsVisibleAsync(deadline, chip));

    private static Task<string> TitleAsync(E2eHarness harness, Deadline deadline) =>
        harness.EvaluateAsync<string>(deadline, "reading the tab title", "() => document.title");

    /// <summary>Unticks the <paramref name="index"/>th row's checkbox, in the panel as it stands.</summary>
    private static Task UntickAsync(E2eHarness harness, Deadline deadline, int index) =>
        harness.ClickAsync(deadline, $"{Panel} >> {Row} >> nth={index} >> input[type=checkbox]");

    /// <summary>Opens the panel if the last tick closed it; leaves it alone if it stayed open.</summary>
    private static async Task EnsurePanelOpenAsync(E2eHarness harness, Deadline deadline)
    {
        if (await harness.IsVisibleAsync(deadline, Panel)) return;
        await harness.ClickAsync(deadline, Trigger);
        await harness.WaitForVisibleAsync(deadline, Panel);
    }

    /// <summary>
    /// Waits for the address to carry <paramref name="scope"/> (empty for none)
    /// and then for the title to follow it: the tick writes the address first
    /// and the title is an effect of it, so the title is waited for, not raced.
    /// </summary>
    private static async Task WaitForScopeAsync(E2eHarness harness, Deadline deadline, string scope, string title)
    {
        await harness.WaitForFunctionAsync(
            deadline,
            $"waiting for the scope to read \"{scope}\"",
            "expected => (new URLSearchParams(location.search).get('scope') ?? '') === expected",
            scope);
        await harness.WaitForFunctionAsync(
            deadline,
            $"waiting for the title to read \"{title}\"",
            "expected => document.title === expected",
            title);
    }

    /// <summary>Projects filed the way any caller files them with the wall off: the local person may file everything this test files.</summary>
    private static async Task FileProjectsAsync(string baseUrl, params (string Key, string? Color)[] projects)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        foreach (var (key, color) in projects)
            await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new(key, $"Project {key}", color));
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

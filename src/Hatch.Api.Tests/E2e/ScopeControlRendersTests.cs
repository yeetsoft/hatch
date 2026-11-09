using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
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
/// themes is the screenshot's call, not an assertion. A phone fact is the same
/// walk with a different profile: the steps are methods taking the harness, the
/// deadline, the base address and the violation list.</para>
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

    /// <summary>The trigger in either state: <c>ScopeControl.tsx</c> gives both the Menu's label as its accessible name.</summary>
    private const string Trigger = E2eHarness.TopBar + " button.hatch-menu__trigger[aria-label=\"Projects in scope\"]";

    private const string Chip = ".hatch-scope-control__chip";

    private const string Panel = ".hatch-types-panel";

    private const string Row = ".hatch-types-row";

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
    /// Look 4. A negative assertion must not pass by outrunning the project
    /// fetch, so the page is held until the fetch has answered and two frames
    /// have been drawn after it.
    /// </summary>
    private async Task OneProjectDrawsNoControlAsync(E2eHarness harness, Deadline deadline, string baseUrl, List<string> violations)
    {
        await harness.OpenAsync(deadline, baseUrl, "/apps/hatch/");
        await harness.WaitForTopBarAsync(deadline);
        await harness.WaitForFirstColumnAsync(deadline);

        await harness.WaitForFunctionAsync(
            deadline,
            "waiting for the project fetch to have answered",
            "() => performance.getEntriesByType('resource').some(e => e.name.includes('/api/hatch/projects'))");
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
        if (title != "Hatch") violations.Add($"unscoped: the tab title is \"{title}\", not \"Hatch\"");

        output.WriteLine($"screenshot: {await harness.ScreenshotAsync(deadline, "unscoped-light")}");
    }

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
                allDisabled: panel.querySelector('.hatch-filter-clear').disabled,
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

    /// <summary>Looks 2 and 3: untick down to one project and then to none, the address and the title following each tick, with no reload and no history entry.</summary>
    private async Task WalkTheScopeDownAsync(E2eHarness harness, Deadline deadline, string baseUrl, List<string> violations)
    {
        var history = await harness.EvaluateAsync<int>(deadline, "reading the history length", "() => history.length");
        output.WriteLine($"--- history.length before the walk: {history}");

        await UntickAsync(harness, deadline, 2);
        await WaitForScopeAsync(harness, deadline, "AAA,BBB", "AAA, BBB - Hatch");
        await CheckScopedAsync(harness, deadline, "AAA, BBB", ["AAA", "BBB"], history, violations);
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
              chips: [...document.querySelectorAll('{{Chip}}')].map(c => ({ text: c.textContent.trim(), hasMark: c.firstElementChild !== null })),
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
                if (!text.Contains(keys[i], StringComparison.Ordinal))
                    violations.Add($"scoped {step}: chip {i} reads \"{text}\" and does not carry {keys[i]}");
                if (!chips[i].GetProperty("hasMark").GetBoolean())
                    violations.Add($"scoped {step}: chip {i} ({keys[i]}) has no project mark");
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

    /// <summary>Projects filed the way any caller files them with the wall off: naming itself with the runner header.</summary>
    private static async Task FileProjectsAsync(string baseUrl, params (string Key, string? Color)[] projects)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        http.DefaultRequestHeaders.Add(LocalCaller.RunnerHeader, "e2e");

        foreach (var (key, color) in projects)
            await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/hatch/projects", new(key, $"Project {key}", color));
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

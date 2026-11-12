using System.Text.Json;
using Hatch.Api.Tests.Hatch;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2262's sixth criterion, "every control reachable and operable without a
/// pointer", read in the driver with real key presses on the palette
/// configurator: the Tab order, the dial's arrows, Shift, Home and End, Escape
/// from a changed candidate and where focus lands, the focus ring a Tab draws,
/// and what the seed field shows when the dialog opens with Random on and off.
/// Run in Chromium and in desktop WebKit, each in light and in dark.
///
/// <para>The keys are <c>Keyboard.PressAsync</c>, never <c>focus()</c>: the
/// things under test are what a Tab and a key do. The one exception is the
/// start of a pass, where the dialog's own panel holds focus on open and the
/// first Tab moves off it.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class ThemeConfiguratorKeyboardTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 150;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Tune = ".hatch-theme-picker__button--text";
    private const string Dialog = "[role=dialog]";
    private const string Palette = "style[data-theme-palette]";
    private const string GearRandom = ".hatch-theme-picker__random";
    private const string GearRandomInput = ".hatch-theme-picker__random input";
    private const string GearSeed = ".hatch-theme-picker input[aria-label=\"Theme seed\"]";
    private const string DialogSeed = "[role=dialog] input[aria-label=\"Theme seed\"]";
    private const string Dial = ".hatch-hue-dial__face";
    private const string Slider = ".hatch-parameter-slider__input";

    /// <summary>The seed grid is 255 steps across the dial's 360 degrees (seed.ts).</summary>
    private const double Steps = 255;

    /// <summary>
    /// What a person presses to move to the next control. Plain Tab in WebKit (Safari's default, which Playwright's build keeps)
    /// stops only on text fields and tabindex elements - it passed Close, the sliders and every button - and Option+Tab is
    /// Safari's own key for "every control", so that is what WebKit presses; Chromium presses Tab.
    /// </summary>
    private string next = "Tab";

    private string back => next.Replace("Tab", "Shift+Tab");

    private static readonly string[] TabOrder =
    [
        "Close", "Hue", "Spread", "Chroma", "Lift", "Tint", "Tint hue",
        "Theme seed", "Copy seed", "Roll", "Revert", "Apply",
    ];

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheConfigurator_IsOperableByKeyboardAlone_InChromium() =>
        PassAsync("ThemeConfiguratorKeyboardChromium", "chromium", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheConfigurator_IsOperableByKeyboardAlone_InWebKit()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return PassAsync("ThemeConfiguratorKeyboardWebKit", "webkit", DeviceProfile.DesktopSafari);
    }

    private async Task PassAsync(string name, string engine, DeviceProfile? profile)
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        next = engine == "webkit" ? "Alt+Tab" : "Tab";
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

            await harness.OpenBoardAsync(deadline, baseUrl);
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForFirstColumnAsync(deadline);

            string? houseSeed = null;
            foreach (var scheme in new[] { "Light", "Dark" })
            {
                await OpenGearAsync(harness, deadline);
                await deadline.BoundAsync($"choosing {scheme}", () =>
                    harness.Page!.Locator(".hatch-theme-switch__option", new() { HasText = scheme }).ClickAsync());

                foreach (var random in new[] { false, true })
                {
                    var label = $"{engine}-{scheme.ToLowerInvariant()}-random-{(random ? "on" : "off")}";

                    // Random on: the seed in the gear. Random off: none, and the dialog starts from the house palette's seed,
                    // which has to be one string on every opening.
                    var expectedSeed = await SetRandomAsync(harness, deadline, random);

                    houseSeed = await OpenedPassAsync(harness, deadline, label, expectedSeed, houseSeed);
                    await DialPassAsync(harness, deadline, label, expectedSeed, houseSeed);
                }
            }

            output.WriteLine($"{engine}: house seed {houseSeed}");
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
    /// Opened with the seed field's value checked (criteria 5 and 1), then the
    /// twelve Tab stops in order, then Escape from the last of them.
    /// Returns the house seed as read.
    /// </summary>
    private async Task<string?> OpenedPassAsync(
        E2eHarness harness, Deadline deadline, string label, string? storedSeed, string? houseSeed)
    {
        var before = await SnapshotAsync(harness, deadline);
        await OpenDialogAsync(harness, deadline);

        var shown = await DialogSeedAsync(harness, deadline);
        houseSeed = CheckSeed(label, shown, storedSeed, houseSeed);

        var stops = new List<string>();
        for (var i = 0; i < TabOrder.Length; i++)
        {
            await PressAsync(harness, deadline, next);
            stops.Add(await FocusLabelAsync(harness, deadline));
        }
        output.WriteLine($"{label}: tab order {string.Join(" > ", stops)}");
        Assert.Equal(TabOrder, stops);

        await PressAsync(harness, deadline, "Escape");
        await AssertEscapedAsync(harness, deadline, label + " (Escape from Apply)", before);
        return houseSeed;
    }

    /// <summary>
    /// A second opening: Tab onto the dial (the ring), the keys on it, Tab onto
    /// a slider (the ring), Shift+Tab back, a changed candidate, Escape.
    /// </summary>
    private async Task DialPassAsync(
        E2eHarness harness, Deadline deadline, string label, string? storedSeed, string? houseSeed)
    {
        var before = await SnapshotAsync(harness, deadline);
        await OpenDialogAsync(harness, deadline);
        CheckSeed(label + " (second opening)", await DialogSeedAsync(harness, deadline), storedSeed, houseSeed);

        await PressAsync(harness, deadline, next);
        Assert.Equal("Close", await FocusLabelAsync(harness, deadline));
        await PressAsync(harness, deadline, next);
        Assert.Equal("Hue", await FocusLabelAsync(harness, deadline));
        var dialFocused = await FocusRingAsync(harness, deadline, label + " dial", Dial);

        // A real Tab to a slider, then past it, so each control is also read unfocused in the very same state.
        await PressAsync(harness, deadline, next);
        Assert.Equal("Spread", await FocusLabelAsync(harness, deadline));
        var sliderFocused = await FocusRingAsync(harness, deadline, label + " slider", Slider);
        var dialBlurred = await ClipAsync(harness, deadline, Dial);
        Assert.False(dialFocused.SequenceEqual(dialBlurred), $"{label}: the dial looks the same focused and not (no focus ring)");

        await PressAsync(harness, deadline, next);
        Assert.Equal("Chroma", await FocusLabelAsync(harness, deadline));
        var sliderBlurred = await ClipAsync(harness, deadline, Slider);
        Assert.False(sliderFocused.SequenceEqual(sliderBlurred), $"{label}: the Spread slider looks the same focused and not (no focus ring)");

        // Shift+Tab back to the dial, so Escape is pressed with the dial focused and a changed candidate on screen.
        await PressAsync(harness, deadline, back);
        Assert.Equal("Spread", await FocusLabelAsync(harness, deadline));
        await PressAsync(harness, deadline, back);
        Assert.Equal("Hue", await FocusLabelAsync(harness, deadline));

        // From a known value: Home is 0, then one seed step an arrow, eleven a Shift+arrow, End is 360, and both ends clamp.
        var step = 0;
        await AssertHueAsync(harness, deadline, "Home", 0, step);
        foreach (var (key, delta) in new[]
        {
            ("ArrowRight", 1), ("ArrowRight", 1), ("ArrowUp", 1),
            ("Shift+ArrowRight", 11), ("ArrowLeft", -1), ("ArrowDown", -1), ("Shift+ArrowLeft", -11),
        })
        {
            var was = await ValueTextAsync(harness, deadline);
            step += delta;
            await AssertHueAsync(harness, deadline, key, step, step);
            Assert.NotEqual(was, await ValueTextAsync(harness, deadline));
        }
        step = (int)Steps;
        await AssertHueAsync(harness, deadline, "End", step, step);
        await AssertHueAsync(harness, deadline, "ArrowRight", step, step);
        await AssertHueAsync(harness, deadline, "Home", 0, 0);
        await AssertHueAsync(harness, deadline, "ArrowLeft", 0, 0);
        await AssertHueAsync(harness, deadline, "Shift+ArrowRight", 11, 11);

        // The candidate is on the page behind the dialog by now, or the Escape below proves nothing.
        var candidate = await WaitForCandidateAsync(harness, deadline, before.PaletteText);
        output.WriteLine($"{label}: candidate on the page: {candidate.Length} characters of palette style, was {before.PaletteText.Length}");

        Assert.Equal("Hue", await FocusLabelAsync(harness, deadline));
        await PressAsync(harness, deadline, "Escape");
        await AssertEscapedAsync(harness, deadline, label + " (Escape from a changed candidate)", before);
    }

    private async Task AssertHueAsync(E2eHarness harness, Deadline deadline, string key, int step, int expectedStep)
    {
        await PressAsync(harness, deadline, key);
        var degrees = Math.Round(expectedStep * 360 / Steps, MidpointRounding.AwayFromZero);
        var expected = $"{degrees:0} degrees";

        // The session settles on the next frame at the latest; read until it has or a second has passed, then say what it was.
        string text = "";
        for (var i = 0; i < 10; i++)
        {
            text = await ValueTextAsync(harness, deadline);
            if (text == expected) break;
            await Task.Delay(100);
        }
        Assert.True(text == expected, $"after {key} (step {step}) aria-valuetext was \"{text}\", expected \"{expected}\"");
        var now = await deadline.BoundAsync("aria-valuenow", () => harness.Page!.Locator(Dial).GetAttributeAsync("aria-valuenow"));
        Assert.Equal($"{degrees:0}", now);
    }

    /// <summary>
    /// The focused control as a Tab left it: it matches <c>:focus-visible</c>, its outline and shadow are printed for the
    /// record, a full-page screenshot is kept for a person, and its settled pixels are returned so the caller can hold them
    /// against the same control unfocused. A computed outline would say nothing for a slider, whose ring is the thumb's halo
    /// (a shadow on a pseudo-element script cannot read), so what is asserted is that the control draws differently focused.
    /// </summary>
    private async Task<byte[]> FocusRingAsync(E2eHarness harness, Deadline deadline, string label, string selector)
    {
        // Settled first: the outline eases in, and a read mid-transition is a half-drawn colour.
        var pixels = await ClipAsync(harness, deadline, selector);
        var json = await harness.EvaluateAsync<string>(deadline, "reading the focus ring", """
            () => {
              const e = document.activeElement;
              const cs = getComputedStyle(e);
              return JSON.stringify({
                tag: e.tagName.toLowerCase(),
                focusVisible: e.matches(':focus-visible'),
                outlineStyle: cs.outlineStyle,
                outlineWidth: cs.outlineWidth,
                outlineColor: cs.outlineColor,
                boxShadow: cs.boxShadow,
              });
            }
            """);
        var ring = JsonDocument.Parse(json).RootElement;
        output.WriteLine($"{label}: {ring}");
        Assert.True(ring.GetProperty("focusVisible").GetBoolean(), $"{label} was reached by a key but does not match :focus-visible: {ring}");

        output.WriteLine($"{label}: " + await harness.ScreenshotAsync(deadline, label.Replace(' ', '-')));
        return pixels;
    }

    /// <summary>The control and ten pixels around it, once two frames in a row agree, so a transition has finished easing in or out.</summary>
    private static async Task<byte[]> ClipAsync(E2eHarness harness, Deadline deadline, string selector)
    {
        var box = await deadline.BoundAsync("measuring the control", () => harness.Page!.Locator(selector).First.BoundingBoxAsync());
        Assert.NotNull(box);
        const float pad = 10;
        var clip = new Clip
        {
            X = Math.Max(0, box.X - pad), Y = Math.Max(0, box.Y - pad), Width = box.Width + 2 * pad, Height = box.Height + 2 * pad,
        };

        byte[] last = [];
        for (var i = 0; i < 20; i++)
        {
            var shot = await deadline.BoundAsync("shooting the control", () => harness.Page!.ScreenshotAsync(new() { Clip = clip }));
            if (shot.SequenceEqual(last)) return shot;
            last = shot;
            await Task.Delay(100);
        }
        return last;
    }

    /// <summary>The seed the dialog shows, held against Random's stored seed or, with Random off, against the one house seed.</summary>
    private string? CheckSeed(string label, string shown, string? storedSeed, string? houseSeed)
    {
        Assert.StartsWith("hx1-", shown);
        if (storedSeed is not null)
        {
            Assert.Equal(storedSeed, shown);
            return houseSeed;
        }
        if (houseSeed is not null) Assert.Equal(houseSeed, shown);
        output.WriteLine($"{label}: Random off, the dialog's seed field shows {shown}");
        return shown;
    }

    /// <summary>
    /// Escape closed the dialog, the page is as it was, and focus is on the gear's
    /// trigger. Both of the dialog's own hand-backs and ThemeSwitch's effect try
    /// to take it, and this is the first read of which wins.
    /// </summary>
    private async Task AssertEscapedAsync(E2eHarness harness, Deadline deadline, string label, Snapshot before)
    {
        await harness.WaitForHiddenAsync(deadline, Dialog);

        Snapshot after = default!;
        for (var i = 0; i < 10; i++)
        {
            after = await SnapshotAsync(harness, deadline);
            if (after == before) break;
            await Task.Delay(100);
        }
        Assert.True(after == before, $"{label}: the page is not as it was.{Environment.NewLine}before {before}{Environment.NewLine}after  {after}");

        // The gear's panel closes on a visibility transition, and in WebKit the
        // dialog's hand-back can land on Tune while that panel is still visible
        // for a few frames; focus is read until it settles, and every place it
        // sat on the way is logged.
        var active = "";
        var seen = new List<string>();
        for (var i = 0; i < 20; i++)
        {
            active = await harness.EvaluateAsync<string>(
                deadline, "reading focus after Escape",
                "sel => document.activeElement && document.activeElement.matches(sel) ? 'the gear trigger' : (document.activeElement ? document.activeElement.tagName.toLowerCase() + ' ' + (document.activeElement.getAttribute('aria-label') ?? document.activeElement.textContent.trim().slice(0, 30)) : 'nothing')",
                E2eHarness.GearSelector);
            if (seen.Count == 0 || seen[^1] != active) seen.Add(active);
            if (active == "the gear trigger") break;
            await Task.Delay(100);
        }
        output.WriteLine($"{label}: focus is on {active} (it sat on: {string.Join(" > ", seen)})");
        if (active != "the gear trigger")
        {
            var where = await harness.EvaluateAsync<string>(deadline, "describing where focus went", """
                () => {
                  const a = document.activeElement;
                  const trigger = document.querySelector('.hatch-menu__trigger[aria-expanded]');
                  const panel = a && a.closest('.hatch-menu__panel');
                  const box = a ? a.getBoundingClientRect() : null;
                  return JSON.stringify({
                    active: a ? a.outerHTML.slice(0, 120) : null,
                    visibility: a ? getComputedStyle(a).visibility : null,
                    panelVisibility: panel ? getComputedStyle(panel).visibility : null,
                    panelClass: panel ? panel.className : null,
                    gearExpanded: document.querySelector('header.hatch-topbar button[aria-label="Settings and theme"]')?.getAttribute('aria-expanded'),
                    focusVisibleMatches: a ? a.matches(':focus') : null,
                    box: box ? [box.x, box.y, box.width, box.height] : null,
                  });
                }
                """);
            output.WriteLine($"{label}: {where}");
        }
        Assert.Equal("the gear trigger", active);
    }

    private async Task<string> WaitForCandidateAsync(E2eHarness harness, Deadline deadline, string was)
    {
        var text = "";
        for (var i = 0; i < 20; i++)
        {
            text = (await SnapshotAsync(harness, deadline)).PaletteText;
            if (text != was) return text;
            await Task.Delay(100);
        }
        Assert.Fail("the dial's keys changed nothing on the page behind the dialog");
        return text;
    }

    private sealed record Snapshot(int Count, string PaletteText, string? Stored, string? Last);

    private static async Task<Snapshot> SnapshotAsync(E2eHarness harness, Deadline deadline)
    {
        var json = await harness.EvaluateAsync<string>(deadline, "snapshotting the palette", """
            () => JSON.stringify({
              count: document.querySelectorAll('style[data-theme-palette]').length,
              text: [...document.querySelectorAll('style[data-theme-palette]')].map(s => s.textContent).join('|'),
              stored: window.localStorage.getItem('hatch.palette'),
              last: window.localStorage.getItem('hatch.palette.last'),
            })
            """);
        var root = JsonDocument.Parse(json).RootElement;
        return new Snapshot(
            root.GetProperty("count").GetInt32(),
            root.GetProperty("text").GetString()!,
            root.GetProperty("stored").GetString(),
            root.GetProperty("last").GetString());
    }

    /// <summary>Opens the gear if it is not open: the gear is a toggle, so clicking it blind could close it.</summary>
    private static async Task OpenGearAsync(E2eHarness harness, Deadline deadline)
    {
        if (!await harness.IsVisibleAsync(deadline, Tune))
            await harness.ClickAsync(deadline, E2eHarness.GearSelector);
        await harness.WaitForVisibleAsync(deadline, Tune);
    }

    /// <summary>Turns Random on or off from the gear, as a person would; with it on, the seed in the gear's field.</summary>
    private static async Task<string?> SetRandomAsync(E2eHarness harness, Deadline deadline, bool random)
    {
        await OpenGearAsync(harness, deadline);
        var on = await deadline.BoundAsync("reading Random", () => harness.Page!.Locator(GearRandomInput).First.IsCheckedAsync());
        if (on != random) await harness.ClickAsync(deadline, GearRandom);
        if (!random) return null;
        var seed = await deadline.BoundAsync("reading the gear's seed", () => harness.Page!.Locator(GearSeed).InputValueAsync());
        Assert.StartsWith("hx1-", seed);
        return seed;
    }

    private static async Task OpenDialogAsync(E2eHarness harness, Deadline deadline)
    {
        await OpenGearAsync(harness, deadline);
        await harness.ClickAsync(deadline, Tune);
        await harness.WaitForVisibleAsync(deadline, Dialog);

        // Focus starts on the panel (tabindex -1), so the first Tab is what reaches Close.
        await harness.WaitForFunctionAsync(
            deadline, "waiting for the dialog's panel to take focus",
            "() => document.activeElement && document.activeElement.getAttribute('role') === 'dialog'");
    }

    private static Task<string> DialogSeedAsync(E2eHarness harness, Deadline deadline) =>
        deadline.BoundAsync("reading the dialog's seed", () => harness.Page!.Locator(DialogSeed).InputValueAsync());

    private static Task<string> ValueTextAsync(E2eHarness harness, Deadline deadline) =>
        deadline.BoundAsync("aria-valuetext", async () => await harness.Page!.Locator(Dial).GetAttributeAsync("aria-valuetext") ?? "");

    private static Task PressAsync(E2eHarness harness, Deadline deadline, string key) =>
        deadline.BoundAsync($"pressing {key}", () => harness.Page!.Keyboard.PressAsync(key));

    /// <summary>What <c>document.activeElement</c> is named, in the words the dialog gives it, or where it is if it is outside the dialog.</summary>
    private static Task<string> FocusLabelAsync(E2eHarness harness, Deadline deadline) =>
        harness.EvaluateAsync<string>(deadline, "reading document.activeElement", """
            () => {
              const e = document.activeElement;
              if (!e || e === document.body) return 'nothing (body)';
              const name = e.getAttribute('role') === 'slider' && e.getAttribute('aria-labelledby')
                ? document.getElementById(e.getAttribute('aria-labelledby')).textContent
                : e.labels && e.labels.length ? e.labels[0].textContent
                : e.getAttribute('aria-label') ?? e.textContent;
              const text = name.trim();
              return e.closest('[role=dialog]') ? text : 'OUTSIDE THE DIALOG: ' + e.tagName.toLowerCase() + ' ' + text.slice(0, 30);
            }
            """);
}

using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2570's acceptance, rendered: the Ambitions page draws two ambitions in
/// two projects, every count non-zero somewhere, and a question waiting above
/// the rows. HA-2843's beside it: a draft, a ready, a running and a done
/// ambition, each read by its stage pill and sentence, grouped under headings
/// with the finished one folded at the foot. Run at the desktop viewport in Chromium and at the
/// <c>iPhone 14 Pro</c> profile in WebKit, where nothing may scroll sideways
/// once the strip and each row are brought into view.
///
/// <para>The first ambition has four capabilities that land in all four
/// segments of its bar - one held, one served by an epic that waits on a
/// question, one served by an epic that runs, one served by nothing - and runs
/// two epics at a limit of one. The second sits in another project, runs one
/// epic filed in the first project at a limit of two, and has a capability
/// whose newest verdict does not hold. Both are left in the column they were filed in,
/// past the one the shaping row shapes in, so both are ready. A third is moved
/// into the implementation column, a fourth into a terminal one, and a fifth is
/// filed with no capability. Everything is filed over plain HTTP, except the
/// work log rows, which are written through the host's services as
/// <see cref="AmbitionBudgetPageTests"/> writes its own.</para>
///
/// <para>HA-2846 adds what a row says beside its stage: the first ambition has a
/// description, a budget of 100 and 41.00 spent, so it reads its summary, one
/// decision (the epic's question) and "41% spent"; the second has no description, no
/// budget and 12.50 spent, so it reads "No decision waits on you." and "$12.50 spent"; the
/// done one has a budget of 1 and 2.00 spent, and reads "Budget used up: 200% spent".
/// Each row has four parts, which start at the same places on every row at the desktop
/// profile and stack on the phone.</para>
///
/// <para>HA-2842 adds two more cases, each at both profiles: a person presses
/// <b>New ambition</b> in the heading, sees the one project picked, types the
/// words, presses <b>Shape</b> and lands on the draft, which reads back over
/// HTTP as an ambition holding the words; and a page with a project and no
/// ambition says what an ambition is, offers the same press and does not scroll
/// sideways.</para>
///
/// <para>HA-2851 adds <b>Since you last looked</b>, at both profiles: one line of
/// each kind is read below the rows, the visit is written exactly once however
/// often the page reads again, and a reload - a second visit - reads the section
/// empty.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class AmbitionsPageTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    /// <summary>Sub-pixel layout is fractional; two boxes that touch are not overlapping.</summary>
    private const double Tolerance = 0.5;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>What the seed filed, and the column names read back for its epics, so no assertion guesses the default column.</summary>
    private sealed record Seeded(
        string AmbitionA, string TitleA, string AmbitionB, string TitleB,
        string Epic1, string Epic2, string Epic3,
        string EpicTitle1, string EpicTitle2, string EpicTitle3,
        string EpicColumn1, string EpicColumn2, string EpicColumn3,
        string Implementation, string Terminal);

    private const string TitleDraft = "Returns are handled without an email";

    private const string TitleRunning = "Stock counts are never a day old";

    private const string TitleDone = "Invoices go out the day an order ships";

    private const string DescriptionA = "Partners place and change orders on their own. Staff only step in when asked.";

    private const string SummaryA = "Partners place and change orders on their own.";

    /// <summary>
    /// Every row, both tiles and the page's width against the viewport's, read in
    /// one script so they are one moment. A row is found by its title, never by
    /// its place.
    /// </summary>
    private const string MeasureScript = """
        () => {
          const texts = (root, selector) => [...root.querySelectorAll(selector)].map((e) => (e.textContent ?? '').trim());
          const strip = document.querySelector('.hatch-ambition-strip');
          const capabilities = document.querySelector('.hatch-ambition-capabilities-tile');
          const tiles = document.querySelector('.hatch-ambition-tiles');
          const place = (e) => {
            if (!e) return null;
            const r = e.getBoundingClientRect();
            return { left: r.left, top: r.top };
          };
          const edges = (e) => {
            if (!e) return null;
            const r = e.getBoundingClientRect();
            return { left: r.left, right: r.right };
          };
          const tile = (e) => ({
            heading: e.querySelector('h2')?.textContent?.trim() ?? '',
            value: e.querySelector('.hatch-health-value')?.textContent?.trim() ?? '',
            note: e.querySelector('.hatch-ambition-tile-note')?.textContent?.trim() ?? '',
            text: e.textContent ?? '',
            place: place(e),
          });
          return {
            strip: strip
              ? {
                  ...tile(strip),
                  hrefs: [...strip.querySelectorAll('a')].map((a) => a.getAttribute('href') ?? ''),
                  strong: texts(strip, 'strong'),
                  keys: texts(strip, '.hatch-plan-key'),
                  link: texts(strip, '.hatch-ambition-tile-link'),
                  linkUnderline: (() => {
                    const a = strip.querySelector('.hatch-ambition-tile-link');
                    return a ? getComputedStyle(a).textDecorationLine : '';
                  })(),
                }
              : null,
            capabilities: capabilities ? tile(capabilities) : null,
            tiles: tiles ? { edges: edges(tiles) } : null,
            rows: [...document.querySelectorAll('.hatch-ambition-row')].map((row) => ({
              edges: edges(row),
              title: row.querySelector('.hatch-ambition-title')?.textContent?.trim() ?? '',
              stage: texts(row, '.hatch-stage-pill').join('|'),
              why: texts(row, '.hatch-ambition-why').join('|'),
              group: row.closest('section')?.querySelector('h2')?.textContent?.trim() ?? '',
              folded: !!row.closest('details'),
              shown: row.checkVisibility(),
              project: row.querySelector('.hatch-plan-project')?.textContent?.trim() ?? '',
              headings: texts(row, 'h3'),
              summary: row.querySelector('.hatch-ambition-summary')?.textContent?.trim() ?? null,
              summaryBelowWhy: (() => {
                const why = row.querySelector('.hatch-ambition-why');
                const summary = row.querySelector('.hatch-ambition-summary');
                return why && summary ? summary.getBoundingClientRect().top >= why.getBoundingClientRect().bottom - 0.5 : null;
              })(),
              decisions: (() => {
                const a = row.querySelector('.hatch-ambition-decisions a');
                if (!a) return null;
                const box = a.getBoundingClientRect();
                return {
                  text: (a.textContent ?? '').trim(),
                  href: a.getAttribute('href') ?? '',
                  height: box.height,
                  underline: getComputedStyle(a).textDecorationLine,
                };
              })(),
              none: row.querySelector('.hatch-ambition-none')?.textContent?.trim() ?? null,
              budget: row.querySelector('.hatch-ambition-budget')?.textContent?.trim() ?? null,
              budgetSpent: !!row.querySelector('.hatch-ambition-budget-spent'),
              legend: texts(row, '.hatch-ambition-legend li'),
              parts: [...row.querySelectorAll(':scope > .hatch-ambition-part')].map(place),
              firstLines: [row.querySelector('.hatch-ambition-title'), ...row.querySelectorAll('h3')].map(place),
              pills: [...row.querySelectorAll('.hatch-ambition-epics .hatch-epics-in-flight-meta > :first-child')].map(place),
              firstEpicBorderTop: (() => {
                const first = row.querySelector('.hatch-ambition-epics > li');
                return first ? parseFloat(getComputedStyle(first).borderTopWidth) : null;
              })(),
              note: texts(row, 'p.hatch-ambition-tally').find((t) => t.includes('open, runs')) ?? '',
              epics: [...row.querySelectorAll('.hatch-ambition-epics li')].map((li) => ({
                key: li.querySelector('.hatch-plan-key')?.textContent?.trim() ?? '',
                title: li.querySelector('.hatch-progress-title')?.textContent?.trim() ?? '',
                meta: li.querySelector('.hatch-epics-in-flight-meta')?.textContent?.trim() ?? '',
              })),
            })),
            groups: texts(document, '.hatch-ambition-group'),
            fold: (() => {
              const details = document.querySelector('.hatch-ambition-finished');
              return details
                ? { summary: details.querySelector('summary')?.textContent?.trim() ?? '', open: details.open }
                : null;
            })(),
            innerWidth: window.innerWidth,
            scrollWidth: document.documentElement.scrollWidth,
          };
        }
        """;

    /// <summary>
    /// Every row's title as it is drawn under one theme, beside what the app's
    /// ink resolves to there: a probe reads the token, so no colour is written
    /// down here.
    /// </summary>
    private const string TitleStyleScript = """
        (theme) => {
          document.documentElement.setAttribute('data-theme', theme);
          const probe = document.body.appendChild(document.createElement('span'));
          probe.style.color = 'var(--ink)';
          const ink = getComputedStyle(probe).color;
          probe.remove();
          return {
            ink,
            titles: [...document.querySelectorAll('.hatch-ambition-title')].map((a) => {
              const s = getComputedStyle(a);
              return { color: s.color, decoration: s.textDecorationLine };
            }),
          };
        }
        """;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheAmbitions_RenderAtTheDesktopProfile() => RunAsync("AmbitionsDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheAmbitions_RenderAtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("AmbitionsPhone", "phone", DeviceProfile.IPhone14Pro);
    }

    private const string HeadingPress = ".hatch-page-header__actions button:has-text(\"New ambition\")";

    private const string DialogWords = "[role=dialog] textarea[aria-label=\"Words\"]";

    private const string DialogPicked = "[role=dialog] .hatch-project-chooser input:checked";

    private const string DialogShape = "[role=dialog] .hatch-gradient-fill";

    private const string Typed = "Let a partner see where their order stands";

    private const string WhatItIs = "An ambition is what you are driving at, broken into capabilities";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task NewAmbition_FilesADraftAtTheDesktopProfile() => FileFromThePageAsync("NewAmbitionDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task NewAmbition_FilesADraftAtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return FileFromThePageAsync("NewAmbitionPhone", "phone", DeviceProfile.IPhone14Pro);
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheEmptyPage_SaysWhatAnAmbitionIsAtTheDesktopProfile() => EmptyPageAsync("AmbitionsEmptyDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task TheEmptyPage_SaysWhatAnAmbitionIsAtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return EmptyPageAsync("AmbitionsEmptyPhone", "phone", DeviceProfile.IPhone14Pro);
    }

    /// <summary>One project and one ambition: press the heading's New ambition, see the project picked, type, press Shape, land on the draft.</summary>
    private async Task FileFromThePageAsync(string name, string profileName, DeviceProfile? profile)
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

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
            await deadline.BoundAsync("filing a project and an ambition", async () =>
            {
                using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
                var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("NAP", "NAP"));
                await PostAsync<IssueCreateRequest, IssueDto>(
                    http, "/api/issues", new(project.Id, "ambition", "Partners order without a phone call", null, null, null, null));
            });

            await harness.OpenAsync(deadline, baseUrl, "/ambitions");
            if (profile is not null) await harness.SetViewportAsync(deadline, 390, 844);
            await harness.WaitForVisibleAsync(deadline, ".hatch-ambition-row");
            await harness.WaitForVisibleAsync(deadline, HeadingPress);
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"new-ambition-page-{profileName}")}");

            await harness.ClickAsync(deadline, HeadingPress);
            await harness.WaitForVisibleAsync(deadline, "[role=dialog] h3:has-text(\"New ambition\")");
            await harness.WaitForVisibleAsync(deadline, DialogPicked);

            // The one project is already picked, so Shape waits only on words.
            var resting = await harness.EvaluateAsync<JsonElement>(deadline, "measuring the dialog", DialogScript);
            output.WriteLine($"{profileName} dialog: {resting}");
            Assert.Equal(1, resting.GetProperty("picked").GetInt32());
            Assert.True(resting.GetProperty("shapeDisabled").GetBoolean());
            Assert.Equal("Give some words or a file first.", resting.GetProperty("why").GetString());
            Assert.False(resting.GetProperty("mentionsDreams").GetBoolean());
            Assert.True(
                resting.GetProperty("scrollWidth").GetDouble() <= resting.GetProperty("innerWidth").GetDouble() + Tolerance,
                $"the page scrolls sideways: {resting}");
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"new-ambition-dialog-{profileName}")}");

            await harness.ClickAsync(deadline, DialogWords);
            await harness.InsertTextAsync(deadline, Typed);
            await harness.WaitForFunctionAsync(
                deadline,
                "waiting for Shape to light",
                "() => !document.querySelector('[role=dialog] .hatch-gradient-fill').disabled");
            await harness.ClickAsync(deadline, DialogShape);
            await harness.WaitForFunctionAsync(deadline, "waiting for the draft's page", "() => location.pathname.startsWith('/issues/')");

            var key = (await harness.EvaluateAsync<string>(deadline, "reading the address", "() => location.pathname")).Split('/')[^1];
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"new-ambition-draft-{profileName}")}");

            using var read = new HttpClient { BaseAddress = new Uri(baseUrl) };
            var issue = await read.GetFromJsonAsync<JsonElement>($"/api/issues/{key}", Web);
            output.WriteLine($"{key}: type {issue.GetProperty("type")}");
            Assert.Equal("ambition", issue.GetProperty("type").GetString());
            Assert.Contains(Typed, issue.GetProperty("description").GetString() ?? "");

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

    /// <summary>The dialog's picked project, the press and its reason, and the page's width, read in one script.</summary>
    private const string DialogScript = """
        () => {
          const dialog = document.querySelector('[role=dialog]');
          const picked = dialog.querySelectorAll('.hatch-project-chooser input:checked').length;
          return {
            picked,
            shapeDisabled: dialog.querySelector('.hatch-gradient-fill').disabled,
            why: dialog.querySelector('.hatch-dream-weave__why')?.textContent?.trim() ?? null,
            mentionsDreams: /dream/i.test(dialog.querySelector('.hatch-dream-shape').textContent ?? ''),
            innerWidth: window.innerWidth,
            scrollWidth: document.documentElement.scrollWidth,
          };
        }
        """;

    /// <summary>One project and no ambition: the two sentences and the press show, the old sentence does not, and nothing scrolls sideways.</summary>
    private async Task EmptyPageAsync(string name, string profileName, DeviceProfile? profile)
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

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
            await deadline.BoundAsync("filing a project", async () =>
            {
                using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
                await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("NAP", "NAP"));
            });

            await harness.OpenAsync(deadline, baseUrl, "/ambitions");
            if (profile is not null) await harness.SetViewportAsync(deadline, 390, 844);
            await harness.WaitForVisibleAsync(deadline, $".hatch-empty:has-text(\"{WhatItIs}\")");

            var measured = await harness.EvaluateAsync<JsonElement>(deadline, "measuring the empty page", """
                () => {
                  const empty = [...document.querySelectorAll('.hatch-empty')].find((e) => (e.textContent ?? '').includes('An ambition is what'));
                  return {
                    text: empty?.textContent ?? '',
                    presses: [...document.querySelectorAll('button')].filter((b) => (b.textContent ?? '').trim() === 'New ambition').length,
                    inEmpty: empty ? [...empty.querySelectorAll('button')].filter((b) => (b.textContent ?? '').trim() === 'New ambition').length : 0,
                    body: document.body.textContent ?? '',
                    innerWidth: window.innerWidth,
                    scrollWidth: document.documentElement.scrollWidth,
                  };
                }
                """);
            output.WriteLine($"{profileName} empty /ambitions: {measured}");
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"ambitions-empty-{profileName}")}");

            Assert.Contains("A person starts one by saying what they want, and a session shapes it.", measured.GetProperty("text").GetString());
            Assert.DoesNotContain("No ambition in scope.", measured.GetProperty("body").GetString());
            Assert.Equal(1, measured.GetProperty("inEmpty").GetInt32());
            Assert.Equal(2, measured.GetProperty("presses").GetInt32());
            Assert.True(
                measured.GetProperty("scrollWidth").GetDouble() <= measured.GetProperty("innerWidth").GetDouble() + Tolerance,
                $"the page scrolls sideways: {measured}");

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

    private const string VisitRoute = "/api/me/visits/ambitions";

    private const string ChangesRoute = "/api/ambitions/changes";

    private const string ChangeLines = ".hatch-ambition-changes-line";

    private const string NothingChanged = "Nothing has changed since you last looked.";

    private const string FirstVisit = "This is your first visit, so this covers the last seven days.";

    /// <summary>What the changes seed filed: one issue behind each kind of line.</summary>
    private sealed record SeededChanges(string Ambition, string Epic, string Decided, string Asked);

    /// <summary>The section's words and each line's two boxes, with the page's width against the viewport's.</summary>
    private const string ChangesScript = """
        () => {
          const section = document.querySelector('.hatch-ambition-changes');
          const box = (e) => {
            const r = e.getBoundingClientRect();
            return { left: r.left, top: r.top, right: r.right, bottom: r.bottom };
          };
          return {
            text: section.textContent ?? '',
            heading: section.querySelector('h2')?.textContent?.trim() ?? '',
            top: box(section).top,
            rowsBottom: box(document.querySelector('.hatch-ambition-groups')).bottom,
            lines: [...section.querySelectorAll('.hatch-ambition-changes-line')].map((a) => {
              const kind = a.querySelector('.hatch-ambition-changes-kind');
              const words = a.querySelector('.hatch-ambition-changes-words');
              return {
                kind: kind.textContent.trim(),
                words: [...words.children].map((c) => c.textContent.trim()),
                href: a.getAttribute('href') ?? '',
                kindBox: box(kind),
                wordsBox: box(words),
              };
            }),
            innerWidth: window.innerWidth,
            scrollWidth: document.documentElement.scrollWidth,
          };
        }
        """;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task SinceYouLastLooked_ReadsFourKindsThenNothingAtTheDesktopProfile() => ChangesAsync("AmbitionChangesDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task SinceYouLastLooked_ReadsFourKindsThenNothingAtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return ChangesAsync("AmbitionChangesPhone", "phone", DeviceProfile.IPhone14Pro);
    }

    /// <summary>
    /// A first visit reads one line of each kind below the rows and records
    /// itself once, even after the page reads again on focus; a reload, which
    /// is a second visit, reads the section empty.
    /// </summary>
    private async Task ChangesAsync(string name, string profileName, DeviceProfile? profile)
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

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
            var seeded = await deadline.BoundAsync("filing one change of each kind", () => SeedChangesAsync(baseUrl));

            // Every visit written, and every one the server has answered.
            var sent = new List<string>();
            var answered = new List<int>();
            var rereads = new List<string>();
            await harness.OpenAsync(deadline, baseUrl, "/ambitions", context =>
            {
                context.Request += (_, request) =>
                {
                    if (request.Method == "PUT" && request.Url.Contains(VisitRoute))
                        lock (sent) sent.Add(request.Url);
                };
                context.Response += (_, response) =>
                {
                    if (response.Request.Method == "PUT" && response.Url.Contains(VisitRoute))
                        lock (answered) answered.Add(response.Status);
                    if (response.Url.Contains(ChangesRoute) && response.Url.Contains("since="))
                        lock (rereads) rereads.Add(response.Url);
                };
                return Task.CompletedTask;
            });
            if (profile is not null) await harness.SetViewportAsync(deadline, 390, 844);
            await harness.WaitForVisibleAsync(deadline, ChangeLines);
            await UntilAsync(deadline, "waiting for the visit to be stored", () => { lock (answered) return answered.Count > 0; });

            // The page reads again when the tab is looked back at. It asks from
            // the instant the first read ran from, so the lines stay.
            await harness.EvaluateAsync<bool>(deadline, "focusing the tab", "() => { window.dispatchEvent(new Event('focus')); return true; }");
            await UntilAsync(deadline, "waiting for the second read", () => { lock (rereads) return rereads.Count > 0; });
            await Task.Delay(500);

            var measured = await harness.EvaluateAsync<JsonElement>(deadline, "measuring the changes section", ChangesScript);
            output.WriteLine($"{profileName} /ambitions changes: {measured}");
            output.WriteLine($"saved {await harness.FullPageScreenshotAsync(deadline, $"ambition-changes-{profileName}")}");

            lock (sent) Assert.True(sent.Count == 1, $"the visit was written {sent.Count} times on one opening");
            lock (answered) Assert.Equal([200], answered);
            AssertChangeLines(measured, seeded, stacked: profile is not null);
            AssertFits(measured, profile is not null);
            AssertFits(await ScrollIntoViewAsync(harness, deadline, ".hatch-ambition-changes", 0), profile is not null);

            await deadline.BoundAsync("reloading", () => harness.Page!.ReloadAsync());
            await harness.WaitForVisibleAsync(deadline, $".hatch-ambition-changes .hatch-empty:has-text(\"{NothingChanged}\")");
            await UntilAsync(deadline, "waiting for the second visit to be stored", () => { lock (answered) return answered.Count > 1; });

            var second = await harness.EvaluateAsync<JsonElement>(deadline, "measuring the second visit", ChangesScript);
            output.WriteLine($"{profileName} /ambitions second visit: {second}");
            output.WriteLine($"saved {await harness.FullPageScreenshotAsync(deadline, $"ambition-changes-second-visit-{profileName}")}");

            Assert.Empty(second.GetProperty("lines").EnumerateArray());
            Assert.Contains("ago", second.GetProperty("text").GetString());
            Assert.DoesNotContain(FirstVisit, second.GetProperty("text").GetString());
            lock (sent) Assert.True(sent.Count == 2, $"two openings wrote the visit {sent.Count} times");
            AssertFits(second, profile is not null);

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

    private static Task UntilAsync(Deadline deadline, string what, Func<bool> done) =>
        deadline.BoundAsync(what, async () =>
        {
            while (!done()) await Task.Delay(50);
        });

    /// <summary>
    /// The first visit's section: below the rows, one line of each kind with its
    /// words and its link, and the kind beside the words on a desk or above them
    /// on a phone.
    /// </summary>
    private static void AssertChangeLines(JsonElement section, SeededChanges s, bool stacked)
    {
        Assert.Equal("Since you last looked", section.GetProperty("heading").GetString());
        Assert.Contains(FirstVisit, section.GetProperty("text").GetString());
        Assert.True(section.GetProperty("top").GetDouble() >= section.GetProperty("rowsBottom").GetDouble(), "the section is not below the rows");

        var lines = section.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(4, lines.Count);
        JsonElement Line(string kind) => lines.Single(l => l.GetProperty("kind").GetString() == kind);

        var holds = Line("Now holds");
        Assert.Equal(
            ["A partner can place an order without a phone call", "Partners order without a phone call", "Looked at: the staging flow"],
            Strings(holds, "words"));
        Assert.Equal($"/issues/{s.Ambition}", holds.GetProperty("href").GetString());

        var carved = Line("Carved");
        Assert.Equal([s.Epic, "A partner order form", "serves #1"], Strings(carved, "words"));
        Assert.Equal($"/issues/{s.Epic}", carved.GetProperty("href").GetString());

        var decided = Line("Decided without you");
        Assert.Equal([s.Decided, "Went with the first store."], Strings(decided, "words"));
        Assert.Equal("/ballot", decided.GetProperty("href").GetString());

        var asked = Line("Asked you");
        Assert.Equal([s.Asked, "Which form should it take?"], Strings(asked, "words"));
        Assert.Equal($"/issues/{s.Asked}", asked.GetProperty("href").GetString());

        double At(JsonElement line, string box, string edge) => line.GetProperty(box).GetProperty(edge).GetDouble();

        foreach (var line in lines)
        {
            if (stacked)
            {
                Assert.True(At(line, "wordsBox", "top") >= At(line, "kindBox", "bottom") - Tolerance, $"the kind is not above the words: {line}");
                Assert.True(Math.Abs(At(line, "wordsBox", "left") - At(line, "kindBox", "left")) <= Tolerance, $"the kind and the words do not share a left: {line}");
            }
            else
            {
                Assert.True(At(line, "wordsBox", "left") >= At(line, "kindBox", "right") - Tolerance, $"the words are not beside the kind: {line}");
                Assert.True(Math.Abs(At(line, "wordsBox", "left") - At(lines[0], "wordsBox", "left")) <= Tolerance, $"the words do not share a column: {line}");
            }
        }
    }

    /// <summary>
    /// One change of each kind beneath one ambition, over plain HTTP: an epic
    /// filed under it; a story whose question a session answered; a story with a
    /// question still waiting at hold; and, last, a capability that holds.
    /// </summary>
    private static async Task<SeededChanges> SeedChangesAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        // Read as a document: the whole runner record needs converters these options do not carry.
        var minted = await PostAsync<CreateRunnerRequest, JsonElement>(http, "/api/runners", new CreateRunnerRequest("Marsh"));
        using var asRunner = new HttpClient { BaseAddress = new Uri(baseUrl) };
        asRunner.DefaultRequestHeaders.Authorization = new("Bearer", minted.GetProperty("secret").GetString());

        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("SIN", "SIN"));
        var ambition = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "ambition", "Partners order without a phone call", null, null, null, null));
        var capability = await PostAsync<CapabilityCreateRequest, CapabilityDto>(
            http, $"/api/issues/{ambition.Key}/capabilities",
            new("A partner can place an order without a phone call", "Place one as a partner and count the calls"));

        var epic = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "epic", "A partner order form", null, ambition.Key, null, null));
        await ServesAsync(http, epic.Key, capability.Id);

        var decided = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "story", "Keep partner orders", null, epic.Key, null, null));
        var settled = await PostAsync<CommentCreateRequest, CommentDto>(
            http, $"/api/issues/{decided.Key}/comments",
            new("Which store should it use?", "question", null, [new("first store", "Already wired in", true), new("second store", "Needs a rewrite")]));
        using (var level = await http.PutAsJsonAsync(
                   $"/api/issues/{decided.Key}/questions/{settled.Id}/level", new QuestionLevelRequest(QuestionLevels.BuildName), Web))
            Assert.True(level.IsSuccessStatusCode, $"setting the question's level answered {(int)level.StatusCode}");
        await PostAsync<CommentCreateRequest, CommentDto>(
            asRunner, $"/api/issues/{decided.Key}/comments", new("Went with the first store.", "answer", settled.Id));

        var asked = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/issues", new(project.Id, "story", "Draw the order form", null, epic.Key, null, null));
        await PostAsync<CommentCreateRequest, CommentDto>(
            http, $"/api/issues/{asked.Key}/comments", new("Which form should it take?", "question"));

        await PostAsync<CapabilityVerdictRequest, CapabilityVerdictDto>(
            http, $"/api/issues/{ambition.Key}/capabilities/{capability.Id}/verdicts", new("holds", "the staging flow", "aaa1111"));

        return new SeededChanges(ambition.Key, epic.Key, decided.Key, asked.Key);
    }

    private async Task RunAsync(string name, string profileName, DeviceProfile? profile)
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

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
            var seeded = await deadline.BoundAsync("filing five ambitions in two projects", () => SeedAsync(baseUrl, harness.Services));

            await harness.OpenAsync(deadline, baseUrl, "/ambitions");
            await harness.WaitForVisibleAsync(deadline, ".hatch-ambition-strip");
            await harness.WaitForVisibleAsync(deadline, ".hatch-ambition-capabilities-tile");
            await harness.WaitForVisibleAsync(deadline, ".hatch-ambition-row");
            await harness.WaitForFunctionAsync(
                deadline, "waiting for every ambition row", "() => document.querySelectorAll('.hatch-ambition-row').length === 5");

            var measured = await harness.EvaluateAsync<JsonElement>(deadline, "measuring the Ambitions page", MeasureScript);
            output.WriteLine($"{profileName} /ambitions: {measured}");
            output.WriteLine($"saved {await harness.FullPageScreenshotAsync(deadline, $"ambitions-{profileName}")}");

            var rows = measured.GetProperty("rows").EnumerateArray().ToList();
            Assert.Equal(5, rows.Count);
            var rowA = rows.Single(r => r.GetProperty("title").GetString() == seeded.TitleA);
            var rowB = rows.Single(r => r.GetProperty("title").GetString() == seeded.TitleB);
            AssertRowA(rowA, seeded);
            AssertRowB(rowB, seeded);
            AssertWhatIsOnYou(rows, seeded, phone: profile is not null);
            if (profile is null)
                AssertRowsLineUp(rowA, rowB);
            else
                AssertRowsStack(rowA, rowB);
            AssertStages(measured, rows, seeded);
            AssertStrip(measured.GetProperty("strip"), seeded);
            AssertCapabilitiesTile(measured.GetProperty("capabilities"));
            AssertRowsMeetTheTiles(measured.GetProperty("tiles"), rows.Where(r => r.GetProperty("shown").GetBoolean()).ToList());
            AssertTilesPlaced(measured.GetProperty("strip"), measured.GetProperty("capabilities"), stacked: profile is not null);
            AssertFits(measured, profile is not null);

            // Nothing may scroll sideways once the strip and each row are the
            // thing at the top of the view.
            AssertFits(await ScrollIntoViewAsync(harness, deadline, ".hatch-ambition-strip", 0), profile is not null);
            AssertFits(await ScrollIntoViewAsync(harness, deadline, ".hatch-ambition-row", 0), profile is not null);
            AssertFits(await ScrollIntoViewAsync(harness, deadline, ".hatch-ambition-row", 1), profile is not null);

            // The fold opens on a press and shows the finished row.
            await harness.ClickAsync(deadline, ".hatch-ambition-finished > summary");
            await harness.WaitForVisibleAsync(deadline, ".hatch-ambition-finished .hatch-ambition-row");
            var opened = await harness.EvaluateAsync<JsonElement>(deadline, "measuring the opened fold", MeasureScript);
            output.WriteLine($"{profileName} /ambitions, fold open: {opened}");
            output.WriteLine($"saved {await harness.FullPageScreenshotAsync(deadline, $"ambitions-{profileName}-finished")}");
            Assert.True(opened.GetProperty("fold").GetProperty("open").GetBoolean());
            Assert.All(opened.GetProperty("rows").EnumerateArray(), r => Assert.True(r.GetProperty("shown").GetBoolean()));
            AssertFits(opened, profile is not null);
            AssertRowsMeetTheTiles(opened.GetProperty("tiles"), opened.GetProperty("rows").EnumerateArray().ToList());

            // Last, since it sets the theme by hand and leaves it set.
            foreach (var theme in new[] { "light", "dark" })
            {
                var style = await harness.EvaluateAsync<JsonElement>(deadline, $"reading the titles in the {theme} theme", TitleStyleScript, theme);
                output.WriteLine($"{profileName} /ambitions {theme} titles: {style}");
                output.WriteLine($"saved {await harness.FullPageScreenshotAsync(deadline, $"ambitions-{profileName}-{theme}")}");
                AssertTitlesAreTheApps(style, theme);
            }

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
    /// HA-2843: each stage's pill and sentence, the group each row sits under in
    /// the order the page draws them, and the done one folded and out of sight.
    /// </summary>
    private static void AssertStages(JsonElement measured, List<JsonElement> rows, Seeded s)
    {
        var ready = $"Nothing runs until a person starts it - move it into \"{s.Implementation}\".";

        Assert.Equal(["Running, 1", "Ready, 2", "Draft, 1"], Strings(measured, "groups"));
        Assert.Equal(
            [TitleRunning, s.TitleA, s.TitleB, TitleDraft, TitleDone],
            rows.Select(r => r.GetProperty("title").GetString()));

        AssertStage(TitleRunning, "Running", "A pass runs as soon as a runner takes it.", "Running, 1");
        AssertStage(s.TitleA, "Ready", ready, "Ready, 2");
        AssertStage(s.TitleB, "Ready", ready, "Ready, 2");
        AssertStage(TitleDraft, "Draft", "It has no capabilities yet.", "Draft, 1");
        AssertStage(TitleDone, "Done", $"It stands in \"{s.Terminal}\", which closes work.", "");

        var fold = measured.GetProperty("fold");
        Assert.Equal("1 finished", fold.GetProperty("summary").GetString());
        Assert.False(fold.GetProperty("open").GetBoolean());

        void AssertStage(string title, string stage, string why, string group)
        {
            var row = rows.Single(r => r.GetProperty("title").GetString() == title);
            Assert.Equal(
                (stage, why, group),
                (row.GetProperty("stage").GetString(), row.GetProperty("why").GetString(), row.GetProperty("group").GetString()));
            var finished = title == TitleDone;
            Assert.Equal(finished, row.GetProperty("folded").GetBoolean());
            Assert.Equal(!finished, row.GetProperty("shown").GetBoolean());
        }
    }

    /// <summary>The first ambition: four capabilities, one in each segment, and two epics at a limit of one.</summary>
    private static void AssertRowA(JsonElement row, Seeded s)
    {
        Assert.Equal("AMA", row.GetProperty("project").GetString());
        Assert.Equal(["4 capabilities", "In flight, 2 of 1", "On you"], Strings(row, "headings"));
        Assert.Equal(["1 hold", "1 in flight", "1 waiting on a person", "1 not started"], Strings(row, "legend"));
        Assert.Equal("2 open, runs 1 at once", row.GetProperty("note").GetString());

        var epics = row.GetProperty("epics").EnumerateArray().ToList();
        Assert.Equal(2, epics.Count);
        AssertEpic(epics.Single(e => e.GetProperty("key").GetString() == s.Epic1), s.EpicTitle1, s.EpicColumn1, "#2");
        AssertEpic(epics.Single(e => e.GetProperty("key").GetString() == s.Epic2), s.EpicTitle2, s.EpicColumn2, "#3");
    }

    /// <summary>The second ambition, in the other project: two capabilities, one epic filed in the first project, a limit of two.</summary>
    private static void AssertRowB(JsonElement row, Seeded s)
    {
        Assert.Equal("AMB", row.GetProperty("project").GetString());
        Assert.Equal(["2 capabilities", "In flight, 1 of 2", "On you"], Strings(row, "headings"));
        Assert.Equal(["1 in flight", "1 not started"], Strings(row, "legend"));
        Assert.Equal("1 open, runs 2 at once", row.GetProperty("note").GetString());

        var epic = Assert.Single(row.GetProperty("epics").EnumerateArray());
        Assert.StartsWith("AMA-", epic.GetProperty("key").GetString());
        AssertEpic(epic, s.EpicTitle3, s.EpicColumn3, "#2");
    }

    /// <summary>
    /// HA-2846: each row's summary, its count of decisions and the link to the ballot, the
    /// words when none waits, and the one budget line, read from what the page drew. The
    /// ballot link is a finger-sized target on the phone and underlined at both profiles.
    /// </summary>
    private void AssertWhatIsOnYou(List<JsonElement> rows, Seeded s, bool phone)
    {
        JsonElement RowOf(string title) => rows.Single(r => r.GetProperty("title").GetString() == title);
        string? Text(JsonElement row, string property) =>
            row.GetProperty(property).ValueKind == JsonValueKind.Null ? null : row.GetProperty(property).GetString();

        var a = RowOf(s.TitleA);
        Assert.Equal(SummaryA, Text(a, "summary"));
        Assert.True(a.GetProperty("summaryBelowWhy").GetBoolean(), "the summary is not under the stage sentence");
        var link = a.GetProperty("decisions");
        Assert.Equal("1 decision", link.GetProperty("text").GetString());
        Assert.EndsWith("/ballot", link.GetProperty("href").GetString());
        Assert.Equal("underline", link.GetProperty("underline").GetString());
        if (phone)
            Assert.True(link.GetProperty("height").GetDouble() >= 44 - Tolerance, $"the ballot link is {link.GetProperty("height")} tall");
        Assert.Null(Text(a, "none"));
        Assert.Equal("41% spent", Text(a, "budget"));
        Assert.False(a.GetProperty("budgetSpent").GetBoolean());

        var b = RowOf(s.TitleB);
        Assert.Null(Text(b, "summary"));
        Assert.Equal(JsonValueKind.Null, b.GetProperty("decisions").ValueKind);
        Assert.Equal("No decision waits on you.", Text(b, "none"));
        Assert.Equal("$12.50 spent", Text(b, "budget"));
        Assert.False(b.GetProperty("budgetSpent").GetBoolean());

        var done = RowOf(TitleDone);
        Assert.Equal("Budget used up: 200% spent", Text(done, "budget"));
        Assert.True(done.GetProperty("budgetSpent").GetBoolean());

        foreach (var title in new[] { TitleRunning, TitleDraft })
        {
            var quiet = RowOf(title);
            Assert.Equal("No decision waits on you.", Text(quiet, "none"));
            Assert.Equal("$0 spent", Text(quiet, "budget"));
        }

        output.WriteLine(
            $"on you: A {link.GetProperty("text")} -> {link.GetProperty("href")}, {Text(a, "budget")}; B {Text(b, "none")} {Text(b, "budget")}; done {Text(done, "budget")}");
    }

    /// <summary>HA-2839, HA-2847: each row's left and right edges are the two tiles', taken together, from the elements' own boxes.</summary>
    private void AssertRowsMeetTheTiles(JsonElement tiles, IReadOnlyList<JsonElement> rows)
    {
        var (left, right) = Edges(tiles);
        foreach (var row in rows)
        {
            var (rowLeft, rowRight) = Edges(row);
            output.WriteLine($"edges: tiles {left} to {right}, row {rowLeft} to {rowRight}");
            Assert.True(
                Math.Abs(rowLeft - left) <= Tolerance && Math.Abs(rowRight - right) <= Tolerance,
                $"a row runs from {rowLeft} to {rowRight} and the tiles from {left} to {right}");
        }
    }

    private static (double Left, double Right) Edges(JsonElement element)
    {
        var edges = element.GetProperty("edges");
        return (edges.GetProperty("left").GetDouble(), edges.GetProperty("right").GetDouble());
    }

    /// <summary>HA-2839: at rest a title is drawn in the app's ink with no underline, not in the browser's style for a link.</summary>
    private static void AssertTitlesAreTheApps(JsonElement style, string theme)
    {
        var ink = style.GetProperty("ink").GetString();
        var titles = style.GetProperty("titles").EnumerateArray().ToList();
        Assert.Equal(5, titles.Count);
        Assert.All(titles, title =>
        {
            Assert.True(
                title.GetProperty("color").GetString() == ink,
                $"a title in the {theme} theme is {title.GetProperty("color")} and the ink is {ink}");
            Assert.Equal("none", title.GetProperty("decoration").GetString());
        });
    }

    /// <summary>
    /// HA-2701 and HA-2846 at the desktop profile: each of the four parts starts at the
    /// same left on both rows, the four first lines share a top within a row, the column
    /// pills of one row's epics share a left, and the first epic line draws no
    /// rule above it. The numbers go to the output so a reader sees them.
    /// </summary>
    private void AssertRowsLineUp(JsonElement rowA, JsonElement rowB)
    {
        var partsA = Places(rowA, "parts");
        var partsB = Places(rowB, "parts");
        Assert.Equal(4, partsA.Count);
        Assert.Equal(4, partsB.Count);
        output.WriteLine($"part lefts, row A: {string.Join(", ", partsA.Select(p => p.Left))}; row B: {string.Join(", ", partsB.Select(p => p.Left))}");

        for (var i = 0; i < 4; i++)
            Assert.True(
                Math.Abs(partsA[i].Left - partsB[i].Left) <= Tolerance,
                $"part {i} starts at {partsA[i].Left} on the first row and {partsB[i].Left} on the second");
        Assert.True(
            partsA[0].Left < partsA[1].Left && partsA[1].Left < partsA[2].Left && partsA[2].Left < partsA[3].Left,
            "the four parts do not sit side by side");

        foreach (var (name, row) in new[] { ("A", rowA), ("B", rowB) })
        {
            var tops = Places(row, "firstLines");
            Assert.Equal(4, tops.Count);
            output.WriteLine($"first line tops, row {name}: {string.Join(", ", tops.Select(p => p.Top))}");
            Assert.All(tops, p => Assert.True(Math.Abs(p.Top - tops[0].Top) <= Tolerance, $"the first lines of row {name} are not level: {string.Join(", ", tops.Select(t => t.Top))}"));

            Assert.Equal(0, row.GetProperty("firstEpicBorderTop").GetDouble());
        }

        var pills = Places(rowA, "pills");
        Assert.Equal(2, pills.Count);
        output.WriteLine($"pill lefts, row A: {string.Join(", ", pills.Select(p => p.Left))}");
        Assert.All(pills, p => Assert.True(Math.Abs(p.Left - pills[0].Left) <= Tolerance, $"the column pills of row A start at {string.Join(", ", pills.Select(t => t.Left))}"));
    }

    /// <summary>HA-2701 and HA-2846 at the phone profile: head, capabilities, epics in flight, on you, each below the one before and at the same left.</summary>
    private void AssertRowsStack(JsonElement rowA, JsonElement rowB)
    {
        foreach (var (name, row) in new[] { ("A", rowA), ("B", rowB) })
        {
            var parts = Places(row, "parts");
            Assert.Equal(4, parts.Count);
            output.WriteLine($"part places, row {name}: {string.Join("; ", parts.Select(p => $"({p.Left}, {p.Top})"))}");
            Assert.True(
                parts[1].Top > parts[0].Top && parts[2].Top > parts[1].Top && parts[3].Top > parts[2].Top,
                $"the parts of row {name} do not stack head, capabilities, epics, on you");
            Assert.All(parts, p => Assert.True(Math.Abs(p.Left - parts[0].Left) <= Tolerance, $"the parts of row {name} do not share a left"));
        }
    }

    private readonly record struct Place(double Left, double Top);

    private static List<Place> Places(JsonElement row, string property) =>
        row.GetProperty(property).EnumerateArray()
            .Select(e => new Place(e.GetProperty("left").GetDouble(), e.GetProperty("top").GetDouble()))
            .ToList();

    private static void AssertEpic(JsonElement epic, string title, string column, string badge)
    {
        Assert.Equal(title, epic.GetProperty("title").GetString());
        var meta = epic.GetProperty("meta").GetString()!;
        Assert.Contains(column, meta);
        Assert.Contains(badge, meta);
    }

    /// <summary>One decision waiting, on the first ambition and the epic carrying the question, linking to the ballot; the second ambition is not in it.</summary>
    private static void AssertStrip(JsonElement strip, Seeded s)
    {
        Assert.Equal("Waiting on you", strip.GetProperty("heading").GetString());
        Assert.Equal("1 decision", strip.GetProperty("value").GetString());
        Assert.Equal("Holding 1 open issue. The oldest was just asked.", strip.GetProperty("note").GetString());
        Assert.Equal(["Open the ballot"], Strings(strip, "link"));
        Assert.Equal("underline", strip.GetProperty("linkUnderline").GetString());
        Assert.Equal([s.TitleA], Strings(strip, "strong"));
        Assert.Equal([s.Epic1], Strings(strip, "keys"));
        Assert.DoesNotContain(s.TitleB, strip.GetProperty("text").GetString());
        Assert.All(Strings(strip, "hrefs"), href => Assert.EndsWith("/ballot", href));
        Assert.NotEmpty(Strings(strip, "hrefs"));
    }

    /// <summary>One of the six capabilities holds, two more are in flight, and the one that holds only just began.</summary>
    private static void AssertCapabilitiesTile(JsonElement tile)
    {
        Assert.Equal("Capabilities that hold", tile.GetProperty("heading").GetString());
        Assert.Equal("1 of 7", tile.GetProperty("value").GetString());
        Assert.Equal("2 more in flight. 1 newly holding this week.", tile.GetProperty("note").GetString());
    }

    /// <summary>Side by side they share a top; stacked they share a left, the second below the first.</summary>
    private void AssertTilesPlaced(JsonElement first, JsonElement second, bool stacked)
    {
        var a = first.GetProperty("place");
        var b = second.GetProperty("place");
        output.WriteLine($"tile places: ({a.GetProperty("left")}, {a.GetProperty("top")}) and ({b.GetProperty("left")}, {b.GetProperty("top")})");

        if (stacked)
        {
            Assert.True(Math.Abs(a.GetProperty("left").GetDouble() - b.GetProperty("left").GetDouble()) <= Tolerance, "the stacked tiles do not share a left");
            Assert.True(b.GetProperty("top").GetDouble() > a.GetProperty("top").GetDouble(), "the second tile is not below the first");
        }
        else
        {
            Assert.True(Math.Abs(a.GetProperty("top").GetDouble() - b.GetProperty("top").GetDouble()) <= Tolerance, "the tiles do not share a top");
            Assert.True(b.GetProperty("left").GetDouble() > a.GetProperty("left").GetDouble(), "the second tile is not beside the first");
        }
    }

    private static List<string> Strings(JsonElement element, string property) =>
        element.GetProperty(property).EnumerateArray().Select(e => e.GetString()!).ToList();

    /// <summary>Brings the <paramref name="index"/>th match of <paramref name="selector"/> to the top of the viewport and reads the page's width against it, so the check is made with that part in view.</summary>
    private static Task<JsonElement> ScrollIntoViewAsync(E2eHarness harness, Deadline deadline, string selector, int index) =>
        harness.EvaluateAsync<JsonElement>(deadline, $"scrolling {selector} #{index} into view", """
            ([sel, i]) => {
              document.querySelectorAll(sel)[i]?.scrollIntoView({ block: 'start' });
              return { innerWidth: window.innerWidth, scrollWidth: document.documentElement.scrollWidth };
            }
            """, new object[] { selector, index });

    /// <summary>The page may not scroll sideways on the phone; on the desktop the same check costs nothing, so it is made there too.</summary>
    private static void AssertFits(JsonElement measured, bool phone)
    {
        var innerWidth = measured.GetProperty("innerWidth").GetDouble();
        Assert.True(
            measured.GetProperty("scrollWidth").GetDouble() <= innerWidth + Tolerance,
            $"the page scrolls sideways{(phone ? " on the phone" : "")}: {measured}");
    }

    /// <summary>
    /// Two projects. The first ambition, in the first, with four capabilities:
    /// the first holds, the second is served by an epic carrying a question,
    /// the third by an epic with none, the fourth by nothing. The second
    /// ambition, in the second project, with two: the first does not hold and
    /// nothing serves it, the second is served by an epic filed in the first
    /// project; its limit is two. Every epic is left in the column it was filed
    /// in, whose name is read back from the statuses. Three more in the first
    /// project: one with no capability, one moved into the implementation column
    /// and one into a terminal column. The first ambition has a description, a budget
    /// of 100 and a 41.00 session; the second a 12.50 session and no budget; the
    /// terminal one a budget of 1 and a 2.00 session.
    /// </summary>
    private static async Task<Seeded> SeedAsync(string baseUrl, IServiceProvider services)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var statuses = await GetAsync<StatusDto[]>(http, "/api/statuses");
        string ColumnOf(IssueDto issue) => statuses.Single(s => s.Id == issue.StatusId).Name;

        var projectA = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("AMA", "AMA"));
        var projectB = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/projects", new("AMB", "AMB"));

        const string titleA = "Partners order without a phone call";
        const string titleB = "Staff see every order at a glance";

        var a = await PostAsync<IssueCreateRequest, IssueDto>(http, "/api/issues", new(projectA.Id, "ambition", titleA, DescriptionA, null, null, null));
        var capsA = new List<long>();
        foreach (var (statement, howToTell) in new[]
        {
            ("Partners can place an order without a phone call", "Place one as a partner and count the calls"),
            ("An order is confirmed within a minute", "Time an order from the button to the email"),
            ("A partner sees only their own orders", "Sign in as two partners and compare the lists"),
            ("A partner can change an order until it ships", "Edit one before it ships and again after"),
        })
            capsA.Add((await PostAsync<CapabilityCreateRequest, CapabilityDto>(http, $"/api/issues/{a.Key}/capabilities", new(statement, howToTell))).Id);

        await PostAsync<CapabilityVerdictRequest, CapabilityVerdictDto>(
            http, $"/api/issues/{a.Key}/capabilities/{capsA[0]}/verdicts", new("holds", "the staging flow", "aaa1111"));

        const string epicTitle1 = "A partner order form";
        const string epicTitle2 = "Order notices for staff";
        var epic1 = await PostAsync<IssueCreateRequest, IssueDto>(http, "/api/issues", new(projectA.Id, "epic", epicTitle1, null, a.Key, null, null));
        await ServesAsync(http, epic1.Key, capsA[1]);
        // A question's level is hold unless it says otherwise.
        await PostAsync<CommentCreateRequest, CommentDto>(http, $"/api/issues/{epic1.Key}/comments", new("Which form should it take?", "question"));

        var epic2 = await PostAsync<IssueCreateRequest, IssueDto>(http, "/api/issues", new(projectA.Id, "epic", epicTitle2, null, a.Key, null, null));
        await ServesAsync(http, epic2.Key, capsA[2]);

        var b = await PostAsync<IssueCreateRequest, IssueDto>(http, "/api/issues", new(projectB.Id, "ambition", titleB, null, null, null, null));
        var capsB = new List<long>();
        foreach (var (statement, howToTell) in new[]
        {
            ("Staff see a new order the moment it lands", "File one and watch the staff list"),
            ("Staff can filter orders by partner", "Pick a partner and count the rows"),
        })
            capsB.Add((await PostAsync<CapabilityCreateRequest, CapabilityDto>(http, $"/api/issues/{b.Key}/capabilities", new(statement, howToTell))).Id);

        await PostAsync<CapabilityVerdictRequest, CapabilityVerdictDto>(
            http, $"/api/issues/{b.Key}/capabilities/{capsB[0]}/verdicts", new("does_not_hold", "the staff list", "bbb2222"));

        const string epicTitle3 = "A live list for staff";
        var epic3 = await PostAsync<IssueCreateRequest, IssueDto>(http, "/api/issues", new(projectA.Id, "epic", epicTitle3, null, b.Key, null, null));
        await ServesAsync(http, epic3.Key, capsB[1]);

        using var wip = await http.PatchAsJsonAsync($"/api/issues/{b.Key}/wip", new IssueWipLimitRequest("2"), Web);
        var wipText = await wip.Content.ReadAsStringAsync();
        Assert.True(wip.IsSuccessStatusCode, $"PATCH /api/issues/{b.Key}/wip answered {(int)wip.StatusCode}: {wipText}");

        // The stock board ticks no column, so the one a pass runs in is measured: two
        // left of the first terminal column, the one whose next move is into review.
        var board = statuses.Where(s => !s.IsDeferred).OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToList();
        var terminal = board.First(s => s.IsTerminal);
        var implementation = board[board.IndexOf(terminal) - 2];

        await PostAsync<IssueCreateRequest, IssueDto>(http, "/api/issues", new(projectA.Id, "ambition", TitleDraft, null, null, null, null));
        string? doneKey = null;
        foreach (var (title, column) in new[] { (TitleRunning, implementation), (TitleDone, terminal) })
        {
            var ambition = await PostAsync<IssueCreateRequest, IssueDto>(http, "/api/issues", new(projectA.Id, "ambition", title, null, null, null, null));
            await PostAsync<CapabilityCreateRequest, CapabilityDto>(http, $"/api/issues/{ambition.Key}/capabilities", new("It works", "Look at it"));
            await PostAsync<IssueMoveRequest, IssueDto>(http, $"/api/issues/{ambition.Key}/move", new(column.Id, null, null));
            if (title == TitleDone) doneKey = ambition.Key;
        }

        await SetBudgetAsync(http, a.Key, "100");
        await SetBudgetAsync(http, doneKey!, "1");

        // What each has cost is written straight to the work log, as the budget page's test writes it.
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
            foreach (var (key, cost) in new[] { (a.Key, 41.00m), (b.Key, 12.50m), (doneKey!, 2.00m) })
            {
                Assert.True(IssueKey.TryParse(key, out var projectKey, out var number));
                var issueId = await db.Issues.WithKey(projectKey, number).Select(i => i.Id).SingleAsync();
                db.WorkLog.Add(Session(issueId, new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero), cost, implementation.Id));
            }

            await db.SaveChangesAsync();
        }

        return new Seeded(
            a.Key, titleA, b.Key, titleB, epic1.Key, epic2.Key, epic3.Key,
            epicTitle1, epicTitle2, epicTitle3, ColumnOf(epic1), ColumnOf(epic2), ColumnOf(epic3),
            implementation.Name, terminal.Name);
    }

    private static async Task SetBudgetAsync(HttpClient http, string key, string amount)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/issues/{key}/budget")
        {
            Content = JsonContent.Create(new IssueBudgetRequest(amount), options: Web),
        };
        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"PATCH /api/issues/{key}/budget answered {(int)response.StatusCode}: {text}");
    }

    /// <summary>One finished session. Written to the work log directly, as the budget page's test writes its own.</summary>
    private static EfHatchWorkLogEntry Session(long issueId, DateTimeOffset start, decimal costUsd, int statusId) =>
        new()
        {
            IssueId = issueId, SessionId = Guid.NewGuid().ToString("N"), StartedAt = start, EndedAt = start.AddMinutes(30),
            DurationMs = (long)TimeSpan.FromMinutes(30).TotalMilliseconds, CreatedAt = start.AddMinutes(30),
            IssueType = "ambition", PlaybookFromStatusId = statusId, PlaybookToStatusId = statusId, Outcome = ClaimOutcomes.Worked,
            Title = "a session", Summary = "what it did", CostUsd = costUsd,
        };

    private static async Task ServesAsync(HttpClient http, string epicKey, long capabilityId)
    {
        using var response = await http.PutAsJsonAsync($"/api/issues/{epicKey}/serves", new IssueServesWriteRequest([capabilityId]), Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"PUT /api/issues/{epicKey}/serves answered {(int)response.StatusCode}: {text}");
    }

    private static async Task<TResponse> GetAsync<TResponse>(HttpClient http, string path)
    {
        using var response = await http.GetAsync(path);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"GET {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}

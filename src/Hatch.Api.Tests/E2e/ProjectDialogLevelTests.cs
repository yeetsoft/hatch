using System.Net.Http.Json;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2360's level in the project dialog, rendered: an owner presses pause and
/// Save writes it, and a caller who does not own the project reads it as a
/// sentence, in headless Chromium at 1280x800, light theme, against the real API
/// serving the built bundle. A project filed through the API is owned by its
/// filer, so the unowned ones are seeded straight into <see cref="HatchContext"/>
/// with no member row, as <see cref="ProjectDialogChangelogTests"/> does.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class ProjectDialogLevelTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string ProjectsPath = "/projects";

    private const string ScopeControl = "button[aria-label=\"Projects in scope\"], button:text-is(\"All projects\")";

    /// <summary>
    /// Runs against the dialog: <c>heading</c> whether a Level heading is drawn,
    /// <c>sentences</c> the Paused. / Expedited. paragraphs, <c>buttons</c> each
    /// level button as <c>glyph:pressed</c>.
    /// </summary>
    private const string LevelScript =
        """
        (dialog) => ({
          heading: Array.from(dialog.querySelectorAll('h2')).some(h => h.textContent.trim() === 'Level'),
          sentences: Array.from(dialog.querySelectorAll('p')).map(p => p.textContent.trim()).filter(t => t === 'Paused.' || t === 'Expedited.'),
          buttons: Array.from(dialog.querySelectorAll('.hatch-scope-control__level'))
            .map(b => (b.classList.contains('hatch-scope-control__level--expedite') ? 'expedite' : 'pause') + ':' + b.getAttribute('aria-pressed'))
        })
        """;

    /// <summary>Runs against an element inside the dialog's own scroll: brings it to the middle, so the screenshot shows what the assertions read.</summary>
    private const string ScrollIntoViewScript = "(el) => { el.scrollIntoView({ block: 'center' }); return true; }";

    /// <summary>What <see cref="LevelScript"/> returns; Playwright builds it by property, so it needs a parameterless constructor.</summary>
    private sealed class LevelRead
    {
        public bool Heading { get; set; }

        public string[] Sentences { get; set; } = [];

        public string[] Buttons { get; set; } = [];
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task AnOwnersDialog_PausesAndResumesTheOnlyProject_AndWritesNothingWhenTheLevelIsUntouched()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("ProjectDialogLevelOwner");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            await deadline.BoundAsync("filing the only project", () => FileOwnedProjectAsync(baseUrl, "OWN"));

            await harness.OpenAsync(deadline, baseUrl, ProjectsPath);

            var violations = new List<string>();

            if (await harness.IsVisibleAsync(deadline, ScopeControl))
                violations.Add("the scope control is drawn on a single-project install");

            var dialog = await OpenDialogAsync(harness, deadline, "OWN");
            var before = await ReadAsync(harness, deadline, dialog, "owner-before");
            if (!before.Heading || !before.Buttons.SequenceEqual(["expedite:false", "pause:false"]))
                violations.Add($"before: expected a Level heading and both buttons unpressed, found {Describe(before)}");

            await harness.ClickAsync(deadline, $"{dialog} .hatch-scope-control__level--pause");
            var pressed = await ReadAsync(harness, deadline, dialog, "owner-pressed");
            if (!pressed.Buttons.SequenceEqual(["expedite:false", "pause:true"]))
                violations.Add($"pressed: expected pause pressed alone, found {Describe(pressed)}");
            if (await PriorityAsync(baseUrl, "OWN") != ProjectPriorities.Normal)
                violations.Add("pressed: the press wrote the level before Save");

            // Cancel discards the press.
            await harness.ClickAsync(deadline, $"{dialog} button:text-is(\"Cancel\")");
            await harness.WaitForHiddenAsync(deadline, dialog);
            dialog = await OpenDialogAsync(harness, deadline, "OWN");
            var cancelled = await ReadAsync(harness, deadline, dialog, "owner-cancelled");
            if (!cancelled.Buttons.SequenceEqual(["expedite:false", "pause:false"]))
                violations.Add($"cancelled: expected the press discarded, found {Describe(cancelled)}");

            await harness.ClickAsync(deadline, $"{dialog} .hatch-scope-control__level--pause");
            await harness.ClickAsync(deadline, $"{dialog} button:text-is(\"Save\")");
            await harness.WaitForHiddenAsync(deadline, dialog);

            dialog = await OpenDialogAsync(harness, deadline, "OWN");
            var paused = await ReadAsync(harness, deadline, dialog, "owner-paused");
            if (!paused.Buttons.SequenceEqual(["expedite:false", "pause:true"]))
                violations.Add($"paused: expected pause pressed after Save, found {Describe(paused)}");
            var savedPaused = await PriorityAsync(baseUrl, "OWN");
            if (savedPaused != ProjectPriorities.Paused)
                violations.Add($"paused: GET /api/projects says \"{savedPaused}\", not \"paused\"");

            await harness.ClickAsync(deadline, $"{dialog} .hatch-scope-control__level--pause");
            await harness.ClickAsync(deadline, $"{dialog} button:text-is(\"Save\")");
            await harness.WaitForHiddenAsync(deadline, dialog);

            dialog = await OpenDialogAsync(harness, deadline, "OWN");
            var resumed = await ReadAsync(harness, deadline, dialog, "owner-resumed");
            if (!resumed.Buttons.SequenceEqual(["expedite:false", "pause:false"]))
                violations.Add($"resumed: expected neither pressed after Save, found {Describe(resumed)}");
            var savedNormal = await PriorityAsync(baseUrl, "OWN");
            if (savedNormal != ProjectPriorities.Normal)
                violations.Add($"resumed: GET /api/projects says \"{savedNormal}\", not \"normal\"");

            // Change only the name: the level is untouched, so Save must write the
            // project and must not write the level.
            var puts = new List<string>();
            var patches = new List<string>();
            harness.Page!.Request += (_, request) =>
            {
                if (request.Method == "PUT" && request.Url.EndsWith("/priority", StringComparison.Ordinal)) puts.Add(request.Url);
                if (request.Method == "PATCH") patches.Add(request.Url);
            };

            await deadline.BoundAsync("renaming the project", () => harness.Page!.Locator($"{dialog} label:has-text(\"Name\") input").First.FillAsync("Renamed"));
            await harness.ClickAsync(deadline, $"{dialog} button:text-is(\"Save\")");
            await harness.WaitForHiddenAsync(deadline, dialog);
            output.WriteLine($"--- rename save: {patches.Count} PATCH, {puts.Count} priority PUT");

            if (patches.Count == 0) violations.Add("rename save: no PATCH was sent, so the save did nothing and the check proves nothing");
            if (puts.Count != 0) violations.Add($"rename save: only the name changed, but {puts.Count} level PUT(s) were sent");

            Assert.True(
                violations.Count == 0,
                $"The owner's dialog does not carry the level as asked ({violations.Count} violation(s)):{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");

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

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task ANonOwnersDialog_ReadsTheLevelAsASentence_WithNoButton_AndNothingAtNormal()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("ProjectDialogLevelNonOwner");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            await deadline.BoundAsync("seeding three projects nobody owns", () => SeedUnownedProjectsAsync(harness.Services));

            await harness.OpenAsync(deadline, baseUrl, ProjectsPath);

            var violations = new List<string>();
            foreach (var (key, sentence) in new (string, string?)[] { ("PSD", "Paused."), ("EXP", "Expedited."), ("NRM", null) })
            {
                var dialog = await OpenDialogAsync(harness, deadline, key);
                var read = await ReadAsync(harness, deadline, dialog, $"non-owner-{key}");

                var expected = sentence is null ? [] : new[] { sentence };
                if (!read.Sentences.SequenceEqual(expected))
                    violations.Add($"{key}: expected [{string.Join(", ", expected)}], found {Describe(read)}");
                if (read.Buttons.Length != 0) violations.Add($"{key}: a non-owner was given a level button: {Describe(read)}");
                if (read.Heading != (sentence is not null))
                    violations.Add($"{key}: expected the Level heading {(sentence is null ? "absent" : "present")}, found {Describe(read)}");

                await harness.ClickAsync(deadline, $"{dialog} button:text-is(\"Cancel\")");
                await harness.WaitForHiddenAsync(deadline, dialog);
            }

            Assert.True(
                violations.Count == 0,
                $"The non-owner's dialog does not read the level as asked ({violations.Count} violation(s)):{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");

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

    private static string Describe(LevelRead read) =>
        $"heading={read.Heading}, sentences=[{string.Join(", ", read.Sentences)}], buttons=[{string.Join(", ", read.Buttons)}]";

    /// <summary>Reads the Level part, takes the screenshot with it in view, and leaves the dialog open.</summary>
    private async Task<LevelRead> ReadAsync(E2eHarness harness, Deadline deadline, string dialog, string label)
    {
        var read = await harness.EvaluateOnAsync<LevelRead>(deadline, "reading the level part", dialog, LevelScript);
        // A normal project read by a non-owner draws no heading, so scroll to Members instead.
        var anchor = read.Heading ? "h2:text-is(\"Level\")" : "h2:text-is(\"Members\")";
        await harness.EvaluateOnAsync<bool>(deadline, "scrolling the level into view", $"{dialog} {anchor}", ScrollIntoViewScript);
        var path = await harness.ScreenshotAsync(deadline, $"level-{label}");
        output.WriteLine($"--- {label}: {Describe(read)}, screenshot {path}");
        return read;
    }

    /// <summary>Clicks Edit in the row holding <c>&lt;code&gt;KEY&lt;/code&gt;</c> and waits for the dialog titled <c>Edit KEY</c>; returns its selector.</summary>
    private static async Task<string> OpenDialogAsync(E2eHarness harness, Deadline deadline, string key)
    {
        await harness.ClickAsync(deadline, $"tr:has(code:text-is(\"{key}\")) button:text-is(\"Edit\")");
        var dialog = $"[role=dialog]:has(h3:text-is(\"Edit {key}\"))";
        await harness.WaitForVisibleAsync(deadline, dialog);
        return dialog;
    }

    /// <summary>What the API says a project's level is, as a second reader beside the page.</summary>
    private static async Task<string> PriorityAsync(string baseUrl, string key)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var projects = await http.GetFromJsonAsync<List<ProjectDto>>("/api/projects");
        return projects!.Single(p => p.Key == key).Priority;
    }

    /// <summary>A project filed the way any caller files one with the wall off: the local person is who files it, which makes the local person its owner.</summary>
    private static async Task FileOwnedProjectAsync(string baseUrl, string key)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        using var response = await http.PostAsJsonAsync("/api/projects", new ProjectCreateRequest(key, $"Project {key}"));
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST /api/projects answered {(int)response.StatusCode}: {text}");
    }

    /// <summary>Three projects with no <c>ProjectMembers</c> row, so the caller owns none: one paused, one expedited, one at the default.</summary>
    private static async Task SeedUnownedProjectsAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
        var now = DateTimeOffset.UtcNow;

        db.Projects.AddRange(
            new EfHatchProject { Key = "PSD", Name = "Set aside", CreatedAt = now, Priority = ProjectPriorities.Paused },
            new EfHatchProject { Key = "EXP", Name = "Comes first", CreatedAt = now, Priority = ProjectPriorities.Expedited },
            new EfHatchProject { Key = "NRM", Name = "Untouched", CreatedAt = now });
        await db.SaveChangesAsync();
    }
}

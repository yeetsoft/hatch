using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2695's acceptance, rendered: on the Dreams page a person types words
/// into the <b>Shape an ambition</b> section, adds an image, presses
/// <b>Shape</b> and lands on a draft typed <c>ambition</c> whose description
/// links the image and whose attachment list shows it. Run at the desktop
/// viewport in Chromium and at the <c>iPhone 14 Pro</c> profile in WebKit,
/// where the pill spans the section and nothing scrolls sideways at 390 pixels.
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class DreamsShapeTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private const string Section = ".hatch-dream-shape";

    private const string Words = ".hatch-dream-shape textarea[aria-label=\"Words\"]";

    private const string Picker = ".hatch-dream-shape input[type=file]";

    private const string Pill = ".hatch-dream-shape .hatch-gradient-fill";

    private const string FileRow = ".hatch-dream-shape__file";

    private const string Typed = "Let a person shape an ambition from the Dreams page";

    private const string FileName = "sketch.png";

    /// <summary>A real 1x1 PNG, so the page can decode it as it would a picture a person picked.</summary>
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    /// <summary>The section's and the pill's boxes, and the page's own width, read in one script so they are one moment.</summary>
    private const string MeasureScript = """
        () => {
          const section = document.querySelector('.hatch-dream-shape').getBoundingClientRect();
          const pill = document.querySelector('.hatch-dream-shape .hatch-gradient-fill').getBoundingClientRect();
          const row = document.querySelector('.hatch-dream-shape__file');
          return {
            sectionWidth: section.width,
            pillWidth: pill.width,
            scrollWidth: document.documentElement.scrollWidth,
            clientWidth: document.documentElement.clientWidth,
            rowStacked: row ? getComputedStyle(row).flexDirection : null,
            pillDisabled: document.querySelector('.hatch-dream-shape .hatch-gradient-fill').disabled,
          };
        }
        """;

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task Shaping_FilesADraftAmbitionAtTheDesktopProfile() => RunAsync("DreamsShapeDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task Shaping_FilesADraftAmbitionAtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("DreamsShapePhone", "phone", DeviceProfile.IPhone14Pro);
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
            await deadline.BoundAsync("filing a project", () => FileProjectAsync(baseUrl));

            await harness.OpenAsync(deadline, baseUrl, "/dreams");
            if (profile is not null) await harness.SetViewportAsync(deadline, 390, 844);
            await harness.WaitForVisibleAsync(deadline, Section);

            // Nothing given yet: the pill is dimmed.
            var resting = await harness.EvaluateAsync<JsonElement>(deadline, "measuring the resting section", MeasureScript);
            output.WriteLine($"{profileName} resting: {resting}");
            Assert.True(resting.GetProperty("pillDisabled").GetBoolean());
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"shape-resting-{profileName}")}");

            // Words and an image: a row for the file, and the pill is lit.
            await harness.ClickAsync(deadline, Words);
            await harness.InsertTextAsync(deadline, Typed);
            await harness.SetInputFilesAsync(deadline, Picker, FileName, "image/png", OnePixelPng);
            await harness.WaitForVisibleAsync(deadline, $"{FileRow}:has-text(\"{FileName}\")");
            await harness.WaitForFunctionAsync(
                deadline,
                "waiting for the pill to light",
                "() => !document.querySelector('.hatch-dream-shape .hatch-gradient-fill').disabled");

            var ready = await harness.EvaluateAsync<JsonElement>(deadline, "measuring the ready section", MeasureScript);
            output.WriteLine($"{profileName} ready: {ready}");
            Assert.False(ready.GetProperty("pillDisabled").GetBoolean());
            if (profile is not null)
            {
                Assert.True(ready.GetProperty("scrollWidth").GetInt32() <= 390, $"the page scrolls sideways: {ready}");
                Assert.True(
                    ready.GetProperty("pillWidth").GetDouble() >= ready.GetProperty("sectionWidth").GetDouble() - 1,
                    $"the pill does not span the section: {ready}");
                Assert.Equal("column", ready.GetProperty("rowStacked").GetString());
            }

            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"shape-ready-{profileName}")}");

            // Press: the draft's page opens.
            await harness.ClickAsync(deadline, Pill);
            await harness.WaitForFunctionAsync(deadline, "waiting for the draft's page", "() => location.pathname.startsWith('/issues/')");
            await harness.WaitForVisibleAsync(deadline, $".hatch-attachments:has-text(\"{FileName}\")");
            output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"shape-draft-{profileName}")}");

            var key = (await harness.EvaluateAsync<string>(deadline, "reading the address", "() => location.pathname")).Split('/')[^1];
            using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
            var issue = await http.GetFromJsonAsync<JsonElement>($"/api/issues/{key}", Web);
            output.WriteLine($"{key}: type {issue.GetProperty("type")}");
            Assert.Equal("ambition", issue.GetProperty("type").GetString());
            var description = issue.GetProperty("description").GetString() ?? "";
            Assert.Contains("/api/images/", description);
            Assert.Contains(Typed, description);

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

    /// <summary>One project, so the page's one Project picker has a project to default to.</summary>
    private static async Task FileProjectAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        using var response = await http.PostAsJsonAsync("/api/projects", new ProjectCreateRequest("DSH", "DSH"), Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST /api/projects answered {(int)response.StatusCode}: {text}");
    }
}

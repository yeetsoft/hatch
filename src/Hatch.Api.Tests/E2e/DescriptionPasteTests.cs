using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Microsoft.Extensions.DependencyInjection;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// An image pasted into an issue's description, end to end in headless
/// Chromium: it uploads through <c>POST /api/hatch/attachments</c>, the editor
/// inserts <c>![image](/api/hatch/images/{id})</c>, Save writes it, and the
/// preview draws the picture. Nothing else would notice the paste, the route
/// and the image route each working and the three not meeting.
///
/// <para>Same needs as <see cref="BoardRendersTests"/>, whose skeleton this
/// follows. It seeds its own project, column and issue, since a scratch
/// database holds none.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public partial class DescriptionPasteTests
{
    /// <summary>
    /// <see cref="BoardRendersTests"/>' budget and then some: the editor is
    /// Monaco, fetched as a chunk when Edit is clicked, and the paste is an
    /// upload and a save besides.
    /// </summary>
    private const int CaseBudgetSeconds = 90;

    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    /// <summary>
    /// The description's own section: <c>#description</c> is a storage key and
    /// never an element id (<c>IssueSection.tsx</c>), so it is found by its heading.
    /// </summary>
    private const string DescriptionSection = "details.hatch-issue-section:has(> summary:text-is(\"Description\"))";

    private const string EditorHost = ".hatch-md-editor";

    /// <summary>A real 1x1 PNG: the page decodes a pasted image before it uploads it, so arbitrary bytes would end in a note and no insert.</summary>
    private const string OnePixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

    /// <summary>
    /// Runs in the page: a clipboard carrying the PNG as a file, pasted on the
    /// editor's host. Dispatched on the host itself because the page listens
    /// there in the capture phase, ahead of Monaco's own handler.
    /// </summary>
    private const string PasteScript = """
        ([selector, base64]) => {
          const bytes = Uint8Array.from(atob(base64), (c) => c.charCodeAt(0));
          const transfer = new DataTransfer();
          transfer.items.add(new File([bytes], 'pasted.png', { type: 'image/png' }));
          const host = document.querySelector(selector);
          return host.dispatchEvent(new ClipboardEvent('paste', { clipboardData: transfer, bubbles: true, cancelable: true }));
        }
        """;

    /// <summary>Plain CSS, since the page reads it with <c>querySelector</c>, which knows none of Playwright's extensions.</summary>
    private const string Picture = ".hatch-markdown img[src^=\"/api/hatch/images/\"]";

    /// <summary>Runs in the page: whether the image has been fetched and decoded, rather than merely put in the DOM.</summary>
    private const string RenderedScript = """
        (selector) => {
          const img = document.querySelector(selector);
          return img !== null && img.complete && img.naturalWidth > 0;
        }
        """;

    private const string ConflictNoteScript = "() => document.body.innerText.includes('changed while this was being edited')";

    [GeneratedRegex(@"!\[image\]\(/api/hatch/images/(?<id>[0-9a-fA-F-]{36})\)")]
    private static partial Regex InsertedImage();

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task APastedImage_UploadsInsertsAndRendersInTheDescription()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("DescriptionPaste");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            var key = await SeedIssueAsync(harness.Services);

            await harness.OpenAsync(deadline, baseUrl, $"/apps/hatch/issues/{key}");
            await harness.WaitForTopBarAsync(deadline);

            await harness.ClickAsync(deadline, $"{DescriptionSection} button:text-is(\"Edit\")");
            await harness.WaitForVisibleAsync(deadline, $"{DescriptionSection} {EditorHost}");

            await harness.EvaluateAsync<bool>(deadline, "pasting the image", PasteScript, new object[] { EditorHost, OnePixelPng });

            // Save is offered on a dirty draft alone, so it appearing is the insert having happened.
            var save = $"{DescriptionSection} .hatch-section-actions button:text-is(\"Save\")";
            await harness.WaitForVisibleAsync(deadline, save);
            await harness.ClickAsync(deadline, save);

            await harness.WaitForVisibleAsync(deadline, $"{DescriptionSection} {Picture}");
            await harness.WaitForFunctionAsync(deadline, "waiting for the image to render", RenderedScript, Picture);

            // Our own save landing is not somebody else's edit: the page must not report a conflict.
            Assert.False(
                await harness.EvaluateAsync<bool>(deadline, "reading the page for a conflict note", ConflictNoteScript),
                "The page says the description changed while it was being edited, after the editor's own save.");

            using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

            var issue = await http.GetAsync($"/api/hatch/issues/{key}");
            Assert.Equal(HttpStatusCode.OK, issue.StatusCode);
            var description = JsonDocument.Parse(await issue.Content.ReadAsStringAsync()).RootElement.GetProperty("description").GetString()!;

            var inserted = InsertedImage().Match(description);
            Assert.True(inserted.Success, $"The saved description holds no ![image](/api/hatch/images/{{id}}): {description}");
            var id = inserted.Groups["id"].Value;

            var download = await http.GetAsync($"/api/hatch/attachments/{id}");
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Equal("image/png", download.Content.Headers.ContentType?.MediaType);
            Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
            Assert.Equal("pasted.png", download.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
            Assert.Equal(Convert.FromBase64String(OnePixelPng), await download.Content.ReadAsByteArrayAsync());

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

    /// <summary>One project, one column and one empty-described issue, written to the host's own database. Returns the issue's key.</summary>
    private static async Task<string> SeedIssueAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
        var now = DateTimeOffset.UtcNow;

        var project = new EfHatchProject { Key = "PST", Name = "Paste", CreatedAt = now };
        var status = new EfHatchStatus { Name = "todo", SortOrder = 10 };
        db.AddRange(project, status);
        await db.SaveChangesAsync();

        db.Issues.Add(new EfHatchIssue
        {
            ProjectId = project.Id,
            Number = 1,
            Type = "story",
            Title = "A description to paste into",
            Description = "",
            StatusId = status.Id,
            Rank = 1024,
            CreatedBy = "operator",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        return $"{project.Key}-1";
    }
}

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Microsoft.Extensions.DependencyInjection;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// A file dropped on the description's own drop zone, end to end in headless
/// Chromium: the zone's capture-phase listeners beat Monaco's own, the file
/// uploads through <c>POST /api/attachments</c>, the editor inserts the
/// link, Save writes it, and - for an image - the preview draws it. The same
/// zone wraps the New issue dialog's body, so one case closes there too.
///
/// <para>Same needs as <see cref="DescriptionPasteTests"/>, whose skeleton this
/// follows, down to the budget: Monaco is a chunk fetched on first Edit, and a
/// drop is an upload and a save besides.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public partial class DescriptionDropTests
{
    private const int CaseBudgetSeconds = 90;

    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    /// <summary>
    /// <see cref="DescriptionPasteTests"/>'s own: <c>#description</c> is a storage
    /// key and never an element id (<c>IssueSection.tsx</c>), so it is found by
    /// its heading.
    /// </summary>
    private const string DescriptionSection = "details.hatch-issue-section:has(> summary:text-is(\"Description\"))";

    private const string EditorHost = ".hatch-md-editor";

    /// <summary><c>AttachmentDropZone.tsx</c>'s own element - the host its listeners are bound to, an ancestor of <see cref="EditorHost"/> and of Preview's rendered markdown alike.</summary>
    private const string DropZone = ".hatch-dropzone";

    private const string Overlay = ".hatch-dropzone__overlay";

    private const string FailedNote = ".hatch-md-editor__failed";

    private const string ModalPanel = ".hatch-modal__panel";

    /// <summary>A real 1x1 PNG: the page decodes a pasted image before it uploads it, so arbitrary bytes would end in a note and no insert.</summary>
    private const string OnePixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

    /// <summary>Never examined for a signature: <c>AttachmentsController</c> only sniffs image bytes, so a fake PDF is stored and served as <c>application/pdf</c> exactly as declared.</summary>
    private static readonly byte[] FakePdfBytes = Encoding.UTF8.GetBytes("not really a pdf, just bytes the server never looks inside");

    /// <summary>Plain CSS, since the page reads it with <c>querySelector</c>, which knows none of Playwright's extensions.</summary>
    private const string Picture = ".hatch-markdown img[src^=\"/api/images/\"]";

    /// <summary>Runs in the page: whether the image has been fetched and decoded, rather than merely put in the DOM.</summary>
    private const string RenderedScript = """
        (selector) => {
          const img = document.querySelector(selector);
          return img !== null && img.complete && img.naturalWidth > 0;
        }
        """;

    /// <summary>
    /// Runs in the page: whether the drop zone's note has moved past its own
    /// synchronous "Uploading…" placeholder - set the instant a file is
    /// dropped, before the upload's own refusal or success ever lands. Both
    /// states share <see cref="FailedNote"/>'s class, so the note being
    /// visible is not by itself the refusal having arrived.
    /// </summary>
    private const string NoteSettledScript = """
        (selector) => {
          const el = document.querySelector(selector);
          return el !== null && !el.textContent.startsWith('Uploading');
        }
        """;

    /// <summary>
    /// Runs in the page against a resolved host element (<see cref="E2eHarness.EvaluateOnAsync{T}"/>):
    /// its <c>textContent</c>.
    /// </summary>
    private const string TextContentScript = "(host) => host.textContent";

    /// <summary>
    /// Runs in the page: whether the first element matching <c>selector</c>
    /// holds <c>substring</c> - Monaco mounts its host before painting the
    /// lines into it, so a newly-remounted editor (case 2, whose preview
    /// toggle unmounts and remounts it) is visible before its text exists.
    /// </summary>
    private const string TextIncludesScript = "([selector, substring]) => (document.querySelector(selector)?.textContent ?? '').includes(substring)";

    /// <summary>
    /// Runs in the page against a resolved host element: a drag carrying one
    /// file built from base64-decoded bytes, or - when <c>name</c> is
    /// <c>null</c> - a text/plain drag with no file at all (case 3).
    /// Dispatched as a single <c>DragEvent</c> of the given type; returns
    /// <c>dispatchEvent</c>'s own result, false meaning the page cancelled it.
    /// </summary>
    private const string DragScript = """
        (host, [eventType, name, type, base64]) => {
          const transfer = new DataTransfer();
          if (name === null) {
            transfer.setData('text/plain', 'x');
          } else {
            const bytes = Uint8Array.from(atob(base64), (c) => c.charCodeAt(0));
            transfer.items.add(new File([bytes], name, { type }));
          }
          return host.dispatchEvent(new DragEvent(eventType, { dataTransfer: transfer, bubbles: true, cancelable: true }));
        }
        """;

    /// <summary>
    /// Case 4 alone: the File's bytes are built inside the page from a byte
    /// count, so a 25 MB-plus body is never serialised across the CDP bridge
    /// as base64.
    /// </summary>
    private const string DragOversizeScript = """
        (host, [eventType, name, type, size]) => {
          const transfer = new DataTransfer();
          transfer.items.add(new File([new Uint8Array(size)], name, { type }));
          return host.dispatchEvent(new DragEvent(eventType, { dataTransfer: transfer, bubbles: true, cancelable: true }));
        }
        """;

    [GeneratedRegex(@"\[name\.pdf\]\(/api/attachments/(?<id>[0-9a-fA-F-]{36})\)")]
    private static partial Regex InsertedAttachment();

    [GeneratedRegex(@"!\[name\.png\]\(/api/images/(?<id>[0-9a-fA-F-]{36})\)")]
    private static partial Regex InsertedImage();

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task ADroppedFile_UploadsInsertsAndRendersInTheDescriptionAndTheDialog()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("DescriptionDrop");
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

            await harness.OpenAsync(deadline, baseUrl, $"/issues/{key}");
            await harness.WaitForTopBarAsync(deadline);

            await harness.ClickAsync(deadline, $"{DescriptionSection} button:text-is(\"Edit\")");
            await harness.WaitForVisibleAsync(deadline, $"{DescriptionSection} {EditorHost}");

            var editorSelector = $"{DescriptionSection} {EditorHost}";
            var dropZoneSelector = $"{DescriptionSection} {DropZone}";
            var overlaySelector = $"{DescriptionSection} {Overlay}";
            var failedSelector = $"{DescriptionSection} {FailedNote}";
            var saveSelector = $"{DescriptionSection} .hatch-section-actions button:text-is(\"Save\")";

            using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

            // Case 4: oversize, first - while the draft is still clean, so Save
            // not appearing here means nothing, not that the refusal cleaned it up.
            await DragOversizeAsync(harness, deadline, "dragging an oversize file in", editorSelector, "dragenter", "big.bin", "application/octet-stream", AttachmentsController.MaxBytes + 1);
            await DragOversizeAsync(harness, deadline, "dragging an oversize file over", editorSelector, "dragover", "big.bin", "application/octet-stream", AttachmentsController.MaxBytes + 1);
            await DragOversizeAsync(harness, deadline, "dropping an oversize file", editorSelector, "drop", "big.bin", "application/octet-stream", AttachmentsController.MaxBytes + 1);

            // The note is visible the instant the drop lands, reading "Uploading…" -
            // the same class the refusal itself renders under - so it is the text
            // settling past that placeholder that says the request came back.
            await harness.WaitForFunctionAsync(deadline, "waiting for the upload note to settle", NoteSettledScript, FailedNote);
            var failedText = await harness.EvaluateOnAsync<string>(deadline, "reading the failed note", failedSelector, TextContentScript);
            Assert.Contains("25 MB", failedText);
            Assert.False(await harness.IsVisibleAsync(deadline, saveSelector), "Save is offered after an oversize drop was refused.");

            // Case 1: a PDF, dropped on the editor host. Overlay up on
            // dragenter, gone on dragleave - checked before the file ever lands.
            var pdfBase64 = Convert.ToBase64String(FakePdfBytes);
            await DragAsync(harness, deadline, "dragenter the PDF", editorSelector, "dragenter", "name.pdf", "application/pdf", pdfBase64);
            await harness.WaitForVisibleAsync(deadline, overlaySelector);

            await DragAsync(harness, deadline, "dragleave the PDF", editorSelector, "dragleave", "name.pdf", "application/pdf", pdfBase64);
            await harness.WaitForHiddenAsync(deadline, overlaySelector);

            await DragAsync(harness, deadline, "dragenter the PDF again", editorSelector, "dragenter", "name.pdf", "application/pdf", pdfBase64);
            await harness.WaitForVisibleAsync(deadline, overlaySelector);
            await DragAsync(harness, deadline, "dragover the PDF", editorSelector, "dragover", "name.pdf", "application/pdf", pdfBase64);
            await DragAsync(harness, deadline, "dropping the PDF", editorSelector, "drop", "name.pdf", "application/pdf", pdfBase64);

            await harness.WaitForFunctionAsync(deadline, "waiting for the editor to hold the attachment link", TextIncludesScript, new object[] { EditorHost, "/api/attachments/" });
            await harness.WaitForVisibleAsync(deadline, saveSelector);
            var editorText = await harness.EvaluateOnAsync<string>(deadline, "reading the editor for the attachment link", editorSelector, TextContentScript);
            var insertedAttachment = InsertedAttachment().Match(editorText);
            Assert.True(insertedAttachment.Success, $"The editor holds no [name.pdf](/api/attachments/{{id}}): {editorText}");

            await harness.ClickAsync(deadline, saveSelector);
            await harness.WaitForHiddenAsync(deadline, saveSelector);

            var issueAfterPdf = await http.GetAsync($"/api/issues/{key}");
            Assert.Equal(HttpStatusCode.OK, issueAfterPdf.StatusCode);
            var descriptionAfterPdf = JsonDocument.Parse(await issueAfterPdf.Content.ReadAsStringAsync()).RootElement.GetProperty("description").GetString()!;
            var savedAttachment = InsertedAttachment().Match(descriptionAfterPdf);
            Assert.True(savedAttachment.Success, $"The saved description holds no [name.pdf](/api/attachments/{{id}}): {descriptionAfterPdf}");
            var attachmentId = savedAttachment.Groups["id"].Value;

            var downloadedAttachment = await http.GetAsync($"/api/attachments/{attachmentId}");
            Assert.Equal(HttpStatusCode.OK, downloadedAttachment.StatusCode);
            Assert.Equal("application/pdf", downloadedAttachment.Content.Headers.ContentType?.MediaType);
            Assert.Equal("attachment", downloadedAttachment.Content.Headers.ContentDisposition?.DispositionType);
            Assert.Equal("name.pdf", downloadedAttachment.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
            Assert.Equal(FakePdfBytes, await downloadedAttachment.Content.ReadAsByteArrayAsync());

            // Case 2: a PNG, dropped in Preview - Save left the view there.
            // There is no editor host to target, only the zone itself.
            await DragAsync(harness, deadline, "dragenter the PNG", dropZoneSelector, "dragenter", "name.png", "image/png", OnePixelPng);
            await DragAsync(harness, deadline, "dragover the PNG", dropZoneSelector, "dragover", "name.png", "image/png", OnePixelPng);
            await DragAsync(harness, deadline, "dropping the PNG", dropZoneSelector, "drop", "name.png", "image/png", OnePixelPng);

            await harness.WaitForFunctionAsync(deadline, "waiting for the editor to hold the image link", TextIncludesScript, new object[] { EditorHost, "/api/images/" });
            await harness.WaitForVisibleAsync(deadline, saveSelector);
            var editorTextAfterPng = await harness.EvaluateOnAsync<string>(deadline, "reading the editor for the image link", editorSelector, TextContentScript);
            Assert.True(InsertedImage().IsMatch(editorTextAfterPng), $"The editor holds no ![name.png](/api/images/{{id}}): {editorTextAfterPng}");

            await harness.ClickAsync(deadline, saveSelector);

            await harness.WaitForVisibleAsync(deadline, $"{DescriptionSection} {Picture}");
            await harness.WaitForFunctionAsync(deadline, "waiting for the image to render", RenderedScript, Picture);

            // Case 3: a text drag carries no file, so the zone must leave it
            // alone - no overlay, and neither event cancelled. A one-shot check,
            // not a wait: a wait that times out and is swallowed would pass just
            // by outrunning the redraw it is meant to catch.
            var dragEnterResult = await DragAsync(harness, deadline, "dragenter a text drag", dropZoneSelector, "dragenter", null, null, null);
            var dragOverResult = await DragAsync(harness, deadline, "dragover a text drag", dropZoneSelector, "dragover", null, null, null);
            Assert.True(dragEnterResult, "The page cancelled dragenter for a drag that carries no file.");
            Assert.True(dragOverResult, "The page cancelled dragover for a drag that carries no file.");
            Assert.False(await harness.IsVisibleAsync(deadline, overlaySelector), "The drop overlay appeared for a drag that carries no file.");

            harness.AssertNoPageErrors();

            // Case 5: the New issue dialog wraps its own body in the same zone.
            // The modal panel is the zone's ancestor, not the zone itself - an
            // event dispatched on an ancestor never reaches a descendant's own
            // capture listener - so the drop targets .hatch-dropzone, found fresh
            // since the issue page's own zone left the DOM with the navigation.
            await harness.GoToAsync(deadline, "");
            await harness.WaitForFirstColumnAsync(deadline);
            await harness.ClickAsync(deadline, ".hatch-board-new");
            await harness.WaitForVisibleAsync(deadline, ModalPanel);
            await harness.WaitForVisibleAsync(deadline, EditorHost);

            await DragAsync(harness, deadline, "dragenter the PDF in the dialog", DropZone, "dragenter", "name.pdf", "application/pdf", pdfBase64);
            await DragAsync(harness, deadline, "dragover the PDF in the dialog", DropZone, "dragover", "name.pdf", "application/pdf", pdfBase64);
            await DragAsync(harness, deadline, "dropping the PDF in the dialog", DropZone, "drop", "name.pdf", "application/pdf", pdfBase64);

            await harness.WaitForFunctionAsync(deadline, "waiting for the dialog's editor to hold the attachment link", TextIncludesScript, new object[] { EditorHost, "/api/attachments/" });
            var dialogEditorText = await harness.EvaluateOnAsync<string>(deadline, "reading the dialog's editor for the attachment link", EditorHost, TextContentScript);
            Assert.True(InsertedAttachment().IsMatch(dialogEditorText), $"The dialog's editor holds no [name.pdf](/api/attachments/{{id}}): {dialogEditorText}");

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

    private static Task<bool> DragAsync(E2eHarness harness, Deadline deadline, string phase, string selector, string eventType, string? name, string? type, string? base64) =>
        harness.EvaluateOnAsync<bool>(deadline, phase, selector, DragScript, new object?[] { eventType, name, type, base64 });

    private static Task<bool> DragOversizeAsync(E2eHarness harness, Deadline deadline, string phase, string selector, string eventType, string name, string type, int size) =>
        harness.EvaluateOnAsync<bool>(deadline, phase, selector, DragOversizeScript, new object?[] { eventType, name, type, size });

    /// <summary>One project, one column and one empty-described issue, written to the host's own database. Returns the issue's key.</summary>
    private static async Task<string> SeedIssueAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
        var now = DateTimeOffset.UtcNow;

        var project = new EfHatchProject { Key = "DRP", Name = "Drop", CreatedAt = now };
        var status = new EfHatchStatus { Name = "todo", SortOrder = 10 };
        db.AddRange(project, status);
        await db.SaveChangesAsync();

        db.Issues.Add(new EfHatchIssue
        {
            ProjectId = project.Id,
            Number = 1,
            Type = "story",
            Title = "A description to drop a file onto",
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

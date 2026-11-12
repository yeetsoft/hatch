using System.Net;
using System.Text;
using System.Text.Json;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Microsoft.Extensions.DependencyInjection;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// The attachments list beside an issue's description, end to end in headless
/// Chromium: three files already linked into the description draw as four
/// rows - three named and sized, one <c>(deleted)</c> for a link nobody ever
/// stored - and removing a link from the draft drops its row before Save.
///
/// <para>Same needs as <see cref="DescriptionPasteTests"/>, whose skeleton this
/// follows, down to the budget. Unlike that case, the attachments and the
/// links are seeded straight into the description - nothing here uploads.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class AttachmentListTests
{
    private const int CaseBudgetSeconds = 90;

    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    /// <summary>
    /// <see cref="DescriptionPasteTests"/>'s own: <c>#description</c> is a
    /// storage key and never an element id (<c>IssueSection.tsx</c>), so it is
    /// found by its heading.
    /// </summary>
    private const string DescriptionSection = "details.hatch-issue-section:has(> summary:text-is(\"Description\"))";

    private const string EditorHost = ".hatch-md-editor";

    /// <summary>A real 1x1 PNG: <see cref="DescriptionPasteTests"/>'s own constant.</summary>
    private const string OnePixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

    /// <summary><see cref="AttachmentsControllerTests.Pdf"/>'s own bytes, copied: that constant is private, in a different partial.</summary>
    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<<>>\nendobj\n%%EOF\n");

    /// <summary>Runs in the page: each row of <c>.hatch-attachments li</c> as its anchor's text and href, or neither for a deleted row.</summary>
    private const string RowsScript = """
        () => Array.from(document.querySelectorAll('.hatch-attachments li')).map((li) => {
          const a = li.querySelector('a');
          return { anchorText: a ? a.textContent : null, href: a ? a.getAttribute('href') : null, liText: li.textContent };
        })
        """;

    /// <summary>Runs in the page: whether <c>.hatch-attachments li</c> has settled at <c>expected</c> rows.</summary>
    private const string RowCountScript = "(expected) => document.querySelectorAll('.hatch-attachments li').length === expected";

    /// <summary>
    /// Runs in the page against the editor host: its current text,
    /// reconstructed from Monaco's rendered lines - one <c>.view-line</c> div
    /// per logical line, joined with <c>\n</c>. Exact only because none of
    /// this case's seeded lines wrap at this harness's desktop viewport.
    /// Monaco renders every literal space as NBSP rather than a plain
    /// space, and a line it renders as empty holds a lone NBSP rather than
    /// nothing, to keep its height - both undone here.
    /// </summary>
    private const string EditorTextScript =
        "(host) => Array.from(host.querySelectorAll('.view-line')).map((l) => l.textContent === '\\u00A0' ? '' : l.textContent.replace(/\\u00A0/g, ' ')).join('\\n')";

    /// <summary>Runs against an element Playwright's own selector engine already found: whether it is no longer disabled.</summary>
    private const string ButtonEnabledScript = "(el) => !el.disabled";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheAttachmentsLinkedIntoADescription_DrawAsRowsAndTrackTheDraft()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("AttachmentList");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            var (key, image1Id, pdfId, image2Id) = await SeedIssueAsync(harness.Services);

            await harness.OpenAsync(deadline, baseUrl, $"/issues/{key}");
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForVisibleAsync(deadline, DescriptionSection);
            await harness.WaitForFunctionAsync(deadline, "waiting for the four attachment rows", RowCountScript, 4);

            var rows = await harness.EvaluateAsync<JsonElement>(deadline, "reading the attachment rows", RowsScript);
            var rowList = rows.EnumerateArray().ToList();
            Assert.Equal(4, rowList.Count);

            Assert.Equal("image.png", rowList[0].GetProperty("anchorText").GetString());
            Assert.EndsWith(image1Id.ToString(), rowList[0].GetProperty("href").GetString());

            Assert.Equal("report.pdf", rowList[1].GetProperty("anchorText").GetString());
            Assert.EndsWith(pdfId.ToString(), rowList[1].GetProperty("href").GetString());

            Assert.Equal("shot one.png", rowList[2].GetProperty("anchorText").GetString());
            Assert.EndsWith(image2Id.ToString(), rowList[2].GetProperty("href").GetString());

            Assert.Equal(JsonValueKind.Null, rowList[3].GetProperty("anchorText").ValueKind);
            Assert.Equal("(deleted)", rowList[3].GetProperty("liText").GetString());

            await harness.ClickAsync(deadline, $"{DescriptionSection} button:text-is(\"Edit\")");
            await harness.WaitForVisibleAsync(deadline, $"{DescriptionSection} {EditorHost}");
            await harness.ClickAsync(deadline, $"{DescriptionSection} {EditorHost}");

            await harness.SelectAllAsync(deadline);
            await harness.InsertTextAsync(
                deadline,
                $"![image](/api/images/{image1Id})\n" +
                $"![image](/api/images/{image2Id})\n" +
                "[gone](/api/attachments/00000000-0000-0000-0000-000000000000)");

            await harness.WaitForFunctionAsync(deadline, "waiting for the pdf's row to drop", RowCountScript, 3);

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
    /// Deleting an attachment strips its link and, on a clean draft, saves at
    /// once; dismissing the confirm leaves everything - including a dirty
    /// draft's unsaved typing - exactly as it was.
    /// </summary>
    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task DeletingAnAttachment_StripsItsLinkAndSavesACleanDraft_ButLeavesADirtyOneUntouched()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("AttachmentList");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            var (key, _, pdfId, _) = await SeedIssueAsync(harness.Services);

            await harness.OpenAsync(deadline, baseUrl, $"/issues/{key}");
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForVisibleAsync(deadline, DescriptionSection);
            await harness.WaitForFunctionAsync(deadline, "waiting for the four attachment rows", RowCountScript, 4);

            using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

            await harness.AcceptNextDialogAsync(deadline);
            await harness.ClickAsync(deadline, ".hatch-attachments li:has-text(\"report.pdf\") button:text-is(\"Delete\")");

            await harness.WaitForFunctionAsync(deadline, "waiting for the pdf's row to drop", RowCountScript, 3);

            // The row dropping proves only that the draft's own text stripped the
            // link - the autosave it also fires is a separate, fire-and-forget
            // request, so the stored description is polled for rather than
            // assumed to have landed by the time the row did.
            string? descriptionAfterDelete = null;
            for (var i = 0; i < 50; i++)
            {
                var issueAfterDelete = await http.GetAsync($"/api/issues/{key}");
                Assert.Equal(HttpStatusCode.OK, issueAfterDelete.StatusCode);
                descriptionAfterDelete = JsonDocument.Parse(await issueAfterDelete.Content.ReadAsStringAsync())
                    .RootElement.GetProperty("description").GetString();
                if (descriptionAfterDelete is not null && !descriptionAfterDelete.Contains(pdfId.ToString())) break;
                await Task.Delay(100);
            }
            Assert.DoesNotContain(pdfId.ToString(), descriptionAfterDelete);

            var deletedAttachment = await http.GetAsync($"/api/attachments/{pdfId}");
            Assert.Equal(HttpStatusCode.NotFound, deletedAttachment.StatusCode);

            await harness.ClickAsync(deadline, $"{DescriptionSection} button:text-is(\"Edit\")");
            await harness.WaitForVisibleAsync(deadline, $"{DescriptionSection} {EditorHost}");
            await harness.ClickAsync(deadline, $"{DescriptionSection} {EditorHost}");

            var textAfterDelete = await harness.EvaluateOnAsync<string>(
                deadline, "reading the draft after the pdf's link was stripped", EditorHost, EditorTextScript);

            var noted = $"{textAfterDelete}\n\nKeep this note.";
            await harness.SelectAllAsync(deadline);
            await harness.InsertTextAsync(deadline, noted);

            var saveSelector = $"{DescriptionSection} .hatch-section-actions button:text-is(\"Save\")";
            await harness.WaitForVisibleAsync(deadline, saveSelector);

            const string imageDeleteSelector = ".hatch-attachments li:has-text(\"image.png\") button:text-is(\"Delete\")";
            await harness.DismissNextDialogAsync(deadline);
            await harness.ClickAsync(deadline, imageDeleteSelector);

            // A dismissed confirm changes nothing, so there is no row-count edge
            // to wait on - the Delete button's own `disabled` clearing (set by
            // `loading` for the span of the ask) is the settle signal instead.
            // Polled rather than `WaitForFunctionAsync`, which evaluates with the
            // page's native `document.querySelector` and knows nothing of
            // Playwright's own `:has-text`/`:text-is`.
            var settled = false;
            for (var i = 0; i < 50 && !settled; i++)
            {
                settled = await harness.EvaluateOnAsync<bool>(deadline, "checking the dismissed delete settled", imageDeleteSelector, ButtonEnabledScript);
                if (!settled) await Task.Delay(100);
            }
            Assert.True(settled, "The image row's Delete button is still disabled after the dialog was dismissed.");

            await harness.WaitForFunctionAsync(deadline, "checking the row count held", RowCountScript, 3);
            var textAfterDismiss = await harness.EvaluateOnAsync<string>(
                deadline, "reading the draft after the dismissed delete", EditorHost, EditorTextScript);
            Assert.Equal(noted, textAfterDismiss);

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
    /// One project, one column and one issue whose description already links
    /// three stored attachments and one guid that was never stored. Returns
    /// the issue's key and the three stored ids, in link order.
    /// </summary>
    private static async Task<(string Key, Guid Image1Id, Guid PdfId, Guid Image2Id)> SeedIssueAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
        var now = DateTimeOffset.UtcNow;

        var pngBytes = Convert.FromBase64String(OnePixelPng);

        var image1 = new EfHatchAttachment
        {
            Id = Guid.NewGuid(),
            Bytes = pngBytes,
            ContentType = "image/png",
            FileName = "image.png",
            Size = pngBytes.Length,
            CreatedById = null,
            CreatedAt = now,
        };
        var image2 = new EfHatchAttachment
        {
            Id = Guid.NewGuid(),
            Bytes = pngBytes,
            ContentType = "image/png",
            FileName = "shot one.png",
            Size = pngBytes.Length,
            CreatedById = null,
            CreatedAt = now,
        };
        var pdf = new EfHatchAttachment
        {
            Id = Guid.NewGuid(),
            Bytes = PdfBytes,
            ContentType = "application/pdf",
            FileName = "report.pdf",
            Size = PdfBytes.Length,
            CreatedById = null,
            CreatedAt = now,
        };
        db.AddRange(image1, image2, pdf);

        var project = new EfHatchProject { Key = "ATL", Name = "Attachment List", CreatedAt = now };
        var status = new EfHatchStatus { Name = "todo", SortOrder = 10 };
        db.AddRange(project, status);
        await db.SaveChangesAsync();

        var description =
            $"![image](/api/images/{image1.Id})\n" +
            $"[report.pdf](/api/attachments/{pdf.Id})\n" +
            $"![image](/api/images/{image2.Id})\n" +
            $"[gone](/api/attachments/{Guid.NewGuid()})";

        db.Issues.Add(new EfHatchIssue
        {
            ProjectId = project.Id,
            Number = 1,
            Type = "story",
            Title = "A description holding attachments",
            Description = description,
            StatusId = status.Id,
            Rank = 1024,
            CreatedBy = "operator",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        return ($"{project.Key}-1", image1.Id, pdf.Id, image2.Id);
    }
}

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

            await harness.OpenAsync(deadline, baseUrl, $"/apps/hatch/issues/{key}");
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
                $"![image](/api/hatch/images/{image1Id})\n" +
                $"![image](/api/hatch/images/{image2Id})\n" +
                "[gone](/api/hatch/attachments/00000000-0000-0000-0000-000000000000)");

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
            $"![image](/api/hatch/images/{image1.Id})\n" +
            $"[report.pdf](/api/hatch/attachments/{pdf.Id})\n" +
            $"![image](/api/hatch/images/{image2.Id})\n" +
            $"[gone](/api/hatch/attachments/{Guid.NewGuid()})";

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

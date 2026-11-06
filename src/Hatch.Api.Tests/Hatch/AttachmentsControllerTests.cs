using System.Text;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// Attachments: what the upload accepts and how it types and names a file,
/// what the download serves, what the listing answers, what the delete removes.
/// Who may reach each route at the filter is held by <c>AdminSurfaceTests</c>,
/// so these test the actions and not the gate.
/// </summary>
public class AttachmentsControllerTests
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<<>>\nendobj\n%%EOF\n");

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task APdf_RoundTripsByteForByteAsADownloadUnderItsName()
    {
        var (controller, db) = New();

        var dto = Value(await Post(controller, Pdf, "report.pdf", "application/pdf"));

        Assert.Equal("report.pdf", dto.FileName);
        Assert.Equal("application/pdf", dto.ContentType);
        Assert.Equal(Pdf.Length, dto.Size);
        Assert.Equal(Now, dto.CreatedAt);
        Assert.Equal(dto.Id, (await db.Attachments.SingleAsync()).Id);

        var file = Assert.IsType<FileContentResult>(await controller.GetAttachment(dto.Id, default));

        Assert.Equal(Pdf, file.FileContents);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("report.pdf", file.FileDownloadName);
        var headers = controller.Response.Headers;
        Assert.Equal("nosniff", headers.XContentTypeOptions.ToString());
        Assert.Contains("immutable", headers.CacheControl.ToString());
        Assert.Contains("max-age=31536000", headers.CacheControl.ToString());
    }

    [Fact]
    public async Task APersonsUpload_RecordsWhoMadeIt()
    {
        var db = NewDb();
        var person = new EfPerson { Id = Guid.NewGuid(), Name = "Nathan", CreatedAt = Now, UpdatedAt = Now };
        var controller = NewController(db, new StubCaller { Person = person });

        await Post(controller, Pdf, "report.pdf", "application/pdf");

        Assert.Equal(person.Id, (await db.Attachments.SingleAsync()).CreatedById);
    }

    [Fact]
    public async Task AKeyCanReadAnAttachmentAPersonPosted()
    {
        var db = NewDb();
        var id = Value(await Post(NewController(db, program: false), Pdf, "report.pdf", "application/pdf")).Id;

        var file = Assert.IsType<FileContentResult>(await NewController(db, program: true).GetAttachment(id, default));

        Assert.Equal(Pdf, file.FileContents);
    }

    [Fact]
    public async Task APostFromAKey_Is403AndNothingIsStored()
    {
        var (controller, db) = New(program: true);

        var result = await Post(controller, Pdf, "report.pdf", "application/pdf");

        Assert.Equal(403, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        Assert.Empty(db.Attachments);
    }

    [Fact]
    public async Task ADeleteFromAKey_Is403AndRemovesNothing()
    {
        var db = NewDb();
        var id = Value(await Post(NewController(db, program: false), Pdf, "report.pdf", "application/pdf")).Id;

        var result = await NewController(db, program: true).DeleteAttachment(id, default);

        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Single(db.Attachments);
    }

    [Fact]
    public async Task APngOverTwoMegabytes_IsStoredAsPng()
    {
        var (controller, db) = New();
        var big = new byte[3 * 1024 * 1024];
        PngSignature.CopyTo(big, 0);

        var dto = Value(await Post(controller, big, "screenshot.png", "application/octet-stream"));

        Assert.Equal("image/png", dto.ContentType);
        Assert.Equal(big.Length, (await db.Attachments.SingleAsync()).Size);
    }

    [Fact]
    public async Task APngDeclaredAsSomethingElse_IsStoredUnderItsSniffedType()
    {
        var (controller, _) = New();

        var dto = Value(await Post(controller, [.. PngSignature, 0, 1], "pic.bin", "text/plain"));

        Assert.Equal("image/png", dto.ContentType);
    }

    [Fact]
    public async Task TextDeclaredAsHtml_IsStoredWithThatTypeAndServedAsADownload()
    {
        var (controller, _) = New();

        var dto = Value(await Post(controller, Encoding.UTF8.GetBytes("<script>alert(1)</script>"), "page.txt", "text/html"));
        var file = Assert.IsType<FileContentResult>(await controller.GetAttachment(dto.Id, default));

        Assert.Equal("text/html", dto.ContentType);
        Assert.Equal("text/html", file.ContentType);
        Assert.Equal("page.txt", file.FileDownloadName);
    }

    [Theory]
    [InlineData("not a type")]
    [InlineData("")]
    [InlineData("text/")]
    public async Task ABogusDeclaredType_BecomesOctetStream(string declared)
    {
        var (controller, _) = New();

        var dto = Value(await Post(controller, Pdf, "a.pdf", declared));

        Assert.Equal("application/octet-stream", dto.ContentType);
        Assert.IsType<FileContentResult>(await controller.GetAttachment(dto.Id, default));
    }

    [Fact]
    public async Task ADeclaredTypeOver255Characters_BecomesOctetStream()
    {
        var (controller, _) = New();

        var dto = Value(await Post(controller, Pdf, "a.pdf", "text/" + new string('x', 300)));

        Assert.Equal("application/octet-stream", dto.ContentType);
    }

    [Fact]
    public async Task AFileOver25Megabytes_IsRefusedAndNothingIsStored()
    {
        var (controller, db) = New();

        var result = await Post(controller, new byte[AttachmentsController.MaxBytes + 1], "big.bin", "application/octet-stream");

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("That file is larger than 25 MB.", bad.Value);
        Assert.Empty(db.Attachments);
    }

    [Fact]
    public async Task AFileOfExactly25Megabytes_IsStored()
    {
        var (controller, db) = New();

        Value(await Post(controller, new byte[AttachmentsController.MaxBytes], "edge.bin", "application/octet-stream"));

        Assert.Single(db.Attachments);
    }

    [Fact]
    public async Task AnEmptyFile_IsRefusedAndNothingIsStored()
    {
        var (controller, db) = New();

        var result = await Post(controller, [], "empty.txt", "text/plain");

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(db.Attachments);
    }

    [Fact]
    public async Task NoFile_IsRefusedAndNothingIsStored()
    {
        var (controller, db) = New();

        var result = await controller.PostAttachment(null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(db.Attachments);
    }

    [Theory]
    [InlineData("..\\x\\a.pdf", "a.pdf")]
    [InlineData("/etc/passwd", "passwd")]
    [InlineData("C:\\Users\\me\\a.pdf", "a.pdf")]
    [InlineData("", "attachment")]
    [InlineData("   ", "attachment")]
    [InlineData("dir/", "attachment")]
    public async Task ANameWithDirectories_IsStrippedAndABlankOneBecomesAttachment(string posted, string stored)
    {
        var (controller, _) = New();

        var dto = Value(await Post(controller, Pdf, posted, "application/pdf"));

        Assert.Equal(stored, dto.FileName);
    }

    [Fact]
    public async Task ALongName_IsTrimmedTo255()
    {
        var (controller, _) = New();

        var dto = Value(await Post(controller, Pdf, new string('n', 400) + ".pdf", "application/pdf"));

        Assert.Equal(255, dto.FileName.Length);
    }

    [Fact]
    public async Task AnUnknownId_Is404()
    {
        var (controller, _) = New();

        Assert.IsType<NotFoundResult>(await controller.GetAttachment(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task AnImageStoredByThePaste_CanAlsoBeDownloaded()
    {
        var (controller, db) = New();
        var id = Guid.NewGuid();
        db.Attachments.Add(new EfHatchAttachment
        {
            Id = id,
            Bytes = PngSignature,
            ContentType = "image/png",
            FileName = "image.png",
            Size = PngSignature.Length,
            CreatedAt = Now,
        });
        await db.SaveChangesAsync();

        var file = Assert.IsType<FileContentResult>(await controller.GetAttachment(id, default));

        Assert.Equal("image.png", file.FileDownloadName);
    }

    // ---- The listing ----

    [Fact]
    public async Task TwoRealIdsAndAnUnknownOne_AnswerTwoEntriesInTheOrderAsked()
    {
        var (controller, _) = New();
        var first = Value(await Post(controller, Pdf, "first.pdf", "application/pdf")).Id;
        var second = Value(await Post(controller, Pdf, "second.pdf", "application/pdf")).Id;

        var result = await controller.GetAttachments($"{second}, {Guid.NewGuid()} ,{first}", default);

        Assert.Equal([second, first], Value(result).Select(a => a.Id));
        Assert.Equal(["second.pdf", "first.pdf"], Value(result).Select(a => a.FileName));
    }

    [Fact]
    public async Task ADuplicatedOrUnparseableId_IsSkipped()
    {
        var (controller, _) = New();
        var id = Value(await Post(controller, Pdf, "a.pdf", "application/pdf")).Id;

        var result = await controller.GetAttachments($"{id},nonsense,{id},,", default);

        Assert.Equal([id], Value(result).Select(a => a.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" , ,")]
    public async Task NoIds_AnswerAnEmptyList(string? ids)
    {
        var (controller, _) = New();

        Assert.Empty(Value(await controller.GetAttachments(ids, default)));
    }

    [Fact]
    public async Task OneHundredAndOneIds_AreRefused()
    {
        var (controller, _) = New();
        var ids = string.Join(',', Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()));

        var result = await controller.GetAttachments(ids, default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task OneHundredIds_AreAnswered()
    {
        var (controller, _) = New();
        var ids = string.Join(',', Enumerable.Range(0, 100).Select(_ => Guid.NewGuid()));

        Assert.Empty(Value(await controller.GetAttachments(ids, default)));
    }

    [Fact]
    public void TheListingQuery_NeverSelectsTheBytes()
    {
        // Npgsql only to have SQL to read: nothing here opens a connection.
        using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql("Host=unused").Options);

        var sql = AttachmentsController.Metadata(db, [Guid.NewGuid()]).ToQueryString();

        Assert.DoesNotContain("Bytes", sql);
        Assert.Contains("FileName", sql);
    }

    // ---- The delete ----

    [Fact]
    public async Task ADelete_RemovesTheRowAndTheDownloadIsThenA404()
    {
        var (controller, db) = New();
        var id = Value(await Post(controller, Pdf, "a.pdf", "application/pdf")).Id;

        Assert.IsType<NoContentResult>(await controller.DeleteAttachment(id, default));

        Assert.Empty(db.Attachments);
        Assert.IsType<NotFoundResult>(await controller.GetAttachment(id, default));
    }

    [Fact]
    public async Task ADeleteOfAnUnknownId_Is404()
    {
        var (controller, _) = New();

        Assert.IsType<NotFoundResult>(await controller.DeleteAttachment(Guid.NewGuid(), default));
    }

    // ---- Harness ----

    private static HatchContext NewDb() =>
        new(new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static (AttachmentsController Controller, HatchContext Db) New(bool program = false)
    {
        var db = NewDb();
        return (NewController(db, program), db);
    }

    private static AttachmentsController NewController(HatchContext db, bool program) =>
        NewController(db, program
            ? new StubCaller
            {
                Key = new EfApiKey
                {
                    Name = "hatch",
                    Hash = [1],
                    Prefix = "hatch_ak_x",
                    Scopes = [ApiKeyScopes.Hatch],
                    CreatedAt = Now,
                },
            }
            : new StubCaller { Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now } });

    private static AttachmentsController NewController(HatchContext db, StubCaller caller) =>
        new(db, new FakeTimeProvider(Now), caller)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private static Task<ActionResult<AttachmentDto>> Post(AttachmentsController controller, byte[] bytes, string name, string declaredContentType)
    {
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = declaredContentType,
        };
        return controller.PostAttachment(file, CancellationToken.None);
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result?.GetType().Name}");

    /// <summary>Whoever is holding the phone: a person, or the key an agent carries.</summary>
    private sealed class StubCaller : ICallerIdentity
    {
        public EfPerson? Person { get; init; }

        public EfApiKey? Key { get; init; }

        public Task<EfAuthGrant?> GrantAsync(CancellationToken ct) => Task.FromResult<EfAuthGrant?>(null);

        public Task<Guid?> PersonIdAsync(CancellationToken ct) => Task.FromResult(Person?.Id);

        public Task<EfPerson?> PersonAsync(CancellationToken ct) => Task.FromResult(Person);

        public Task<EfApiKey?> ApiKeyAsync(CancellationToken ct) => Task.FromResult(Key);

        public Task<Actor?> LocalAsync(CancellationToken ct) => Task.FromResult<Actor?>(null);

        public Task<bool> IsProgramAsync(CancellationToken ct) => Task.FromResult(Key is not null);

        public Task<string> ActorNameAsync(CancellationToken ct) =>
            Task.FromResult(Person?.Name ?? Key?.Name ?? CallerIdentity.Unattributed);
    }
}

using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// Pasted images: what the read serves, and what it refuses. Who may reach
/// each route at the filter is held by <c>AdminSurfaceTests</c>, so these test
/// the actions and not the gate.
/// </summary>
public class ImagesControllerTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01];

    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AGet_ServesTheExactBytesWithTheStoredTypeAndImmutableCaching()
    {
        var (controller, db) = New();
        var id = await Seed(db, "image/png", Png);

        var file = Assert.IsType<FileContentResult>(await controller.GetImage(id, default));

        Assert.Equal(Png, file.FileContents);
        Assert.Equal("image/png", file.ContentType);
        var headers = controller.Response.Headers;
        Assert.Equal("nosniff", headers.XContentTypeOptions.ToString());
        Assert.Contains("immutable", headers.CacheControl.ToString());
        Assert.Contains("max-age=31536000", headers.CacheControl.ToString());
    }

    [Fact]
    public async Task AKeyCanReadAnImage()
    {
        var db = NewDb();
        var id = await Seed(db, "image/png", Png);

        var key = NewController(db);
        var file = Assert.IsType<FileContentResult>(await key.GetImage(id, default));

        Assert.Equal(Png, file.FileContents);
    }

    [Fact]
    public async Task AnUnknownId_Is404()
    {
        var (controller, _) = New();

        Assert.IsType<NotFoundResult>(await controller.GetImage(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task ARowStoredAsAPdf_Is404()
    {
        var (controller, db) = New();
        var id = await Seed(db, "application/pdf", [1, 2, 3]);

        Assert.IsType<NotFoundResult>(await controller.GetImage(id, default));
    }

    // ---- Harness ----

    private static HatchContext NewDb() =>
        new(new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static (ImagesController Controller, HatchContext Db) New()
    {
        var db = NewDb();
        return (NewController(db), db);
    }

    private static ImagesController NewController(HatchContext db) =>
        new(db) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

    private static async Task<Guid> Seed(HatchContext db, string contentType, byte[] bytes)
    {
        var id = Guid.NewGuid();
        db.Attachments.Add(new EfHatchAttachment
        {
            Id = id,
            Bytes = bytes,
            ContentType = contentType,
            FileName = "stored",
            Size = bytes.Length,
            CreatedAt = Now,
        });
        await db.SaveChangesAsync();
        return id;
    }
}

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
/// Pasted images: what the write accepts, what the read serves. Who may reach
/// each route at the filter is held by <c>AdminSurfaceTests</c>, so these test
/// the actions and not the gate.
/// </summary>
public class ImagesControllerTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01];

    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task APostedPng_ReturnsAnIdAndStoresOneRow()
    {
        var (controller, db) = New();

        var dto = Value(await Post(controller, Png, "image/png"));

        var row = await db.Images.SingleAsync();
        Assert.Equal(dto.Id, row.Id);
        Assert.Equal("image/png", row.ContentType);
        Assert.Equal(Png, row.Bytes);
        Assert.Equal(Now, row.CreatedAt);
    }

    [Fact]
    public async Task AGet_ServesTheExactBytesWithTheSniffedTypeAndImmutableCaching()
    {
        var (controller, _) = New();
        var id = Value(await Post(controller, Png, "application/octet-stream")).Id;

        var file = Assert.IsType<FileContentResult>(await controller.GetImage(id, default));

        Assert.Equal(Png, file.FileContents);
        Assert.Equal("image/png", file.ContentType);
        var headers = controller.Response.Headers;
        Assert.Equal("nosniff", headers.XContentTypeOptions.ToString());
        Assert.Contains("immutable", headers.CacheControl.ToString());
        Assert.Contains("max-age=31536000", headers.CacheControl.ToString());
    }

    [Fact]
    public async Task AKeyCanReadAnImageAPersonPosted()
    {
        var db = NewDb();
        var person = NewController(db, program: false);
        var id = Value(await Post(person, Png, "image/png")).Id;

        var key = NewController(db, program: true);
        var file = Assert.IsType<FileContentResult>(await key.GetImage(id, default));

        Assert.Equal(Png, file.FileContents);
    }

    [Fact]
    public async Task HtmlLabelledAsPng_IsRefusedAndNothingIsStored()
    {
        var (controller, db) = New();

        var result = await Post(controller, Encoding.UTF8.GetBytes("<script>alert(1)</script>"), "image/png");

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(PersonPhoto.UnsupportedError, bad.Value);
        Assert.Empty(db.Images);
    }

    [Fact]
    public async Task AnEmptyBody_IsRefused()
    {
        var (controller, db) = New();

        var result = await Post(controller, [], "image/png");

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(PersonPhoto.EmptyError, bad.Value);
        Assert.Empty(db.Images);
    }

    [Fact]
    public async Task APostFromAKey_Is403AndNothingIsStored()
    {
        var (controller, db) = New(program: true);

        var result = await Post(controller, Png, "image/png");

        Assert.Equal(403, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        Assert.Empty(db.Images);
    }

    [Fact]
    public async Task AnUnknownId_Is404()
    {
        var (controller, _) = New();

        Assert.IsType<NotFoundResult>(await controller.GetImage(Guid.NewGuid(), default));
    }

    // ---- Harness ----

    private static HatchContext NewDb() =>
        new(new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static (ImagesController Controller, HatchContext Db) New(bool program = false)
    {
        var db = NewDb();
        return (NewController(db, program), db);
    }

    private static ImagesController NewController(HatchContext db, bool program)
    {
        var caller = program
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
            : new StubCaller { Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now } };

        return new ImagesController(db, new FakeTimeProvider(Now), caller)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    private static Task<ActionResult<ImageDto>> Post(ImagesController controller, byte[] bytes, string declaredContentType)
    {
        controller.ControllerContext.HttpContext.Request.Body = new MemoryStream(bytes);
        controller.ControllerContext.HttpContext.Request.ContentType = declaredContentType;
        return controller.PostImage(CancellationToken.None);
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

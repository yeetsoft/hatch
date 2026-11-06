using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Images pasted into a description or a comment: one write, one read, both
/// shaped like the project logo's (<see cref="ProjectsController.GetLogo"/>).
/// </summary>
/// <remarks>
/// No class-level attribute, for the reason given on
/// <see cref="ProjectsController"/>: a method-level
/// <see cref="RequireRoleAttribute"/> replaces a class-level one, so each action
/// says who may reach it.
/// </remarks>
[ApiController]
[Route("api/hatch/images")]
public class ImagesController(HatchContext db, TimeProvider time, ICallerIdentity caller) : ControllerBase
{
    /// <summary>
    /// The content types <see cref="GetImage"/> will serve. A later ticket's
    /// <c>POST /attachments</c> can put other content types into
    /// <see cref="HatchContext.Attachments"/>, and this route stays image-only.
    /// </summary>
    private static readonly string[] ImageContentTypes = ["image/png", "image/jpeg", "image/gif", "image/webp"];

    /// <summary>
    /// Person-only, like the logo: an agent that wants a picture on a ticket
    /// has no clipboard, and widening this later is one attribute and one line
    /// out of the audit's person-only list.
    /// </summary>
    [HttpPost]
    [RequireRole(PersonRole.User)]
    [RequestSizeLimit(PersonPhoto.MaxBytes + 1024)]
    public async Task<ActionResult<ImageDto>> PostImage(CancellationToken ct)
    {
        if (await caller.IsProgramAsync(ct))
            return StatusCode(StatusCodes.Status403Forbidden, "images are pasted by a person, not uploaded by an agent");

        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();

        if (!PersonPhoto.TryDetectContentType(bytes, out var contentType, out var error)) return BadRequest(error);

        var image = new EfHatchAttachment
        {
            Id = Guid.NewGuid(),
            Bytes = bytes,
            ContentType = contentType,
            FileName = $"image.{ExtensionFor(contentType)}",
            Size = bytes.Length,
            CreatedAt = time.GetUtcNow(),
        };
        db.Attachments.Add(image);
        await db.SaveChangesAsync(ct);

        return new ImageDto(image.Id);
    }

    [HttpGet("{id:guid}")]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public async Task<IActionResult> GetImage(Guid id, CancellationToken ct)
    {
        var image = await db.Attachments.AsNoTracking()
            .Where(a => ImageContentTypes.Contains(a.ContentType))
            .FirstOrDefaultAsync(a => a.Id == id, ct);
        if (image is null) return NotFound();

        // The id is a fresh Guid per upload and the bytes under it never change.
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "private, max-age=31536000, immutable";

        return File(image.Bytes, image.ContentType);
    }

    private static string ExtensionFor(string contentType) => contentType switch
    {
        "image/png" => "png",
        "image/jpeg" => "jpg",
        "image/gif" => "gif",
        "image/webp" => "webp",
        _ => "bin",
    };
}

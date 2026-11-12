using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The image view of an attachment, for the markdown of a description or a
/// comment to name: one read, shaped like the project logo's
/// (<see cref="ProjectsController.GetLogo"/>). The write is
/// <see cref="AttachmentsController"/>'s.
/// </summary>
/// <remarks>
/// No class-level attribute, for the reason given on
/// <see cref="ProjectsController"/>: a method-level
/// <see cref="RequireRoleAttribute"/> replaces a class-level one, so each action
/// says who may reach it.
/// </remarks>
[ApiController]
[Route("api/images")]
public class ImagesController(HatchContext db) : ControllerBase
{
    /// <summary>
    /// The content types <see cref="GetImage"/> will serve. <c>POST /attachments</c>
    /// can put other content types into <see cref="HatchContext.Attachments"/>,
    /// and this route stays image-only.
    /// </summary>
    private static readonly string[] ImageContentTypes = ["image/png", "image/jpeg", "image/gif", "image/webp"];

    [HttpGet("{id:guid}")]
    [RequireRole(PersonRole.User, AcceptKey = true)]
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
}

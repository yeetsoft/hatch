using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Files kept against the work: one upload, one download, a listing by id and
/// a delete, over the same <see cref="HatchContext.Attachments"/> rows a pasted
/// image lives in (<see cref="ImagesController"/>).
/// </summary>
/// <remarks>
/// No class-level attribute, for the reason given on
/// <see cref="ImagesController"/>: a method-level
/// <see cref="RequireRoleAttribute"/> replaces a class-level one, so each action
/// says who may reach it.
/// </remarks>
[ApiController]
[Route("api/hatch/attachments")]
public class AttachmentsController(HatchContext db, TimeProvider time, ICallerIdentity caller) : ControllerBase
{
    /// <summary>The largest file an upload may carry.</summary>
    public const int MaxBytes = 25 * 1024 * 1024;

    /// <summary>The most ids one listing will answer for.</summary>
    public const int MaxIds = 100;

    private const int MaxNameLength = 255;

    private const string FallbackName = "attachment";

    private const string FallbackContentType = "application/octet-stream";

    /// <summary>
    /// How much of a file <see cref="PersonPhoto.TryDetectContentType"/> is
    /// shown. Its signatures need twelve bytes at most, and its two-megabyte
    /// rule only trips on a longer span, so a prefix is how a large screenshot
    /// is still recognised as one without touching <see cref="PersonPhoto"/>.
    /// </summary>
    private const int SniffBytes = 16;

    /// <summary>
    /// Person-only, like the image paste: an upload is somebody's file, and
    /// widening it to a key is one attribute and one line out of the audit's
    /// person-only list.
    /// </summary>
    /// <remarks>
    /// <paramref name="file"/> is nullable on purpose. Under
    /// <see cref="ApiControllerAttribute"/> a non-nullable parameter that is
    /// missing is answered with the framework's ProblemDetails, and the request
    /// with no file should get this action's own sentence instead.
    /// The size limit is the cap plus 64 KB, as the importer's is: a file in
    /// that margin reaches the sentence below, one far past it is aborted by
    /// the server.
    /// </remarks>
    [HttpPost]
    [RequireRole(PersonRole.User)]
    [RequestSizeLimit(MaxBytes + 64 * 1024)]
    public async Task<ActionResult<AttachmentDto>> PostAttachment([FromForm] IFormFile? file, CancellationToken ct)
    {
        if (await caller.IsProgramAsync(ct))
            return StatusCode(StatusCodes.Status403Forbidden, "files are attached by a person, not uploaded by an agent");

        if (file is null) return BadRequest("no file was uploaded");
        if (file.Length == 0) return BadRequest("that file is empty");
        if (file.Length > MaxBytes) return BadRequest($"That file is larger than {MaxBytes / (1024 * 1024)} MB.");

        using var buffer = new MemoryStream((int)file.Length);
        await using (var source = file.OpenReadStream()) await source.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();

        // The length was the client's claim; the bytes are what arrived.
        if (bytes.Length == 0) return BadRequest("that file is empty");
        if (bytes.Length > MaxBytes) return BadRequest($"That file is larger than {MaxBytes / (1024 * 1024)} MB.");

        var contentType = PersonPhoto.TryDetectContentType(bytes.AsSpan(0, Math.Min(bytes.Length, SniffBytes)), out var sniffed, out _)
            ? sniffed
            : DeclaredOrDefault(file.ContentType);

        var attachment = new EfHatchAttachment
        {
            Id = Guid.NewGuid(),
            Bytes = bytes,
            ContentType = contentType,
            FileName = NameOf(file.FileName),
            Size = bytes.Length,
            CreatedById = await caller.PersonIdAsync(ct),
            CreatedAt = time.GetUtcNow(),
        };
        db.Attachments.Add(attachment);
        await db.SaveChangesAsync(ct);

        return new AttachmentDto(attachment.Id, attachment.FileName, attachment.ContentType, attachment.Size, attachment.CreatedAt);
    }

    /// <summary>
    /// Always a download: the third argument to <see cref="ControllerBase.File(byte[], string, string)"/>
    /// is what makes the response <c>Content-Disposition: attachment</c>, so a
    /// file labelled <c>text/html</c> is saved and never rendered as a page.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public async Task<IActionResult> GetAttachment(Guid id, CancellationToken ct)
    {
        var attachment = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (attachment is null) return NotFound();

        // The id is a fresh Guid per upload and the bytes under it never change.
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "private, max-age=31536000, immutable";

        return File(attachment.Bytes, attachment.ContentType, attachment.FileName);
    }

    /// <summary>
    /// What a set of ids names, in the order they were asked for. An id that is
    /// unparseable, repeated or unknown is left out rather than refused.
    /// </summary>
    [HttpGet]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public async Task<ActionResult<IReadOnlyList<AttachmentDto>>> GetAttachments([FromQuery] string? ids, CancellationToken ct)
    {
        var entries = (ids ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (entries.Length > MaxIds) return BadRequest($"That is {entries.Length} ids - {MaxIds} at a time is the limit.");

        var asked = new List<Guid>(entries.Length);
        foreach (var entry in entries)
            if (Guid.TryParse(entry, out var id) && !asked.Contains(id)) asked.Add(id);

        if (asked.Count == 0) return new List<AttachmentDto>();

        var found = (await Metadata(db, asked).ToListAsync(ct)).ToDictionary(a => a.Id);

        return asked.Where(found.ContainsKey).Select(id => found[id]).ToList();
    }

    [HttpDelete("{id:guid}")]
    [RequireRole(PersonRole.User)]
    public async Task<IActionResult> DeleteAttachment(Guid id, CancellationToken ct)
    {
        if (await caller.IsProgramAsync(ct))
            return StatusCode(StatusCodes.Status403Forbidden, "files are removed by a person, not by an agent");

        var attachment = await db.Attachments.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (attachment is null) return NotFound();

        db.Attachments.Remove(attachment);
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>
    /// The listing's query, apart from the action so a test can read its SQL:
    /// it selects the columns of <see cref="AttachmentDto"/> and never
    /// <see cref="EfHatchAttachment.Bytes"/>.
    /// </summary>
    internal static IQueryable<AttachmentDto> Metadata(HatchContext db, IReadOnlyCollection<Guid> ids) =>
        db.Attachments.AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(a => new AttachmentDto(a.Id, a.FileName, a.ContentType, a.Size, a.CreatedAt));

    /// <summary>
    /// The name as the browser sent it, directories stripped as
    /// <see cref="ImportController.Preview"/> strips them, since a client may
    /// send a full path using either separator.
    /// </summary>
    private static string NameOf(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/')).Trim();
        if (name.Length > MaxNameLength) name = name[..MaxNameLength];
        return name.Length == 0 ? FallbackName : name;
    }

    /// <summary>
    /// The type the client declared, if it can be served. One that does not
    /// parse is not stored: <see cref="ControllerBase.File(byte[], string, string)"/>
    /// throws on it, which would turn every later download into a 500.
    /// </summary>
    private static string DeclaredOrDefault(string? declared) =>
        !string.IsNullOrWhiteSpace(declared) && declared.Length <= 255 && MediaTypeHeaderValue.TryParse(declared, out _)
            ? declared
            : FallbackContentType;
}

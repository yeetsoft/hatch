using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Models.People;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Controllers;

/// <summary>
/// CRUD for the household's people, plus the two endpoints that move a photo in
/// and out.
///
/// Written against <see cref="AppDbContext"/> directly rather than through a
/// service, matching <see cref="ZonesController"/>: this is admin CRUD over one
/// table, and the only logic worth a seam - what a name may contain, what an
/// upload may be - already has one in <see cref="PersonName"/> and
/// <see cref="PersonPhoto"/>, where it can be tested without an HTTP request in
/// the picture.
///
/// The link between a person and a session is *not* here. It is a column on the
/// grant, so it is written where grants are written
/// (<c>AuthController.LinkGrantPerson</c>) - one FK with one write path. What
/// this controller offers is the read of it, on <see cref="GetSessions"/>.
///
/// Every write here is guarded at Admin, and one of them is the reason the guard is
/// worth having at all: <c>Role</c> is set on this page, so an unguarded
/// PUT would let any enrolled device make itself an administrator, which turns
/// the whole boundary into a formality. The reads, photo included, ask only for
/// User - a name and a photo are what the family apps render next to a note -
/// so a Pending person, who has not been let in, sees none of it.
/// </summary>
[ApiController]
[Route("api/people")]
public class PeopleController(AppDbContext db, TimeProvider time) : ControllerBase
{
    [RequireRole(PersonRole.User)]
    [HttpGet]
    public async Task<IReadOnlyList<PersonDto>> GetAll(CancellationToken ct)
        => await db.People.AsNoTracking()
            // By name, because the People page is a list someone reads rather
            // than an ordering someone chose - there is no sort order column
            // here on purpose, and creation order is meaningless to a reader.
            .OrderBy(p => p.Name)
            .Select(ToDto)
            .ToListAsync(ct);

    [RequireRole(PersonRole.User)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PersonDto>> Get(Guid id, CancellationToken ct)
    {
        var person = await db.People.AsNoTracking().Where(p => p.Id == id).Select(ToDto).FirstOrDefaultAsync(ct);
        return person is null ? NotFound() : person;
    }

    [RequireAdmin]
    [HttpPost]
    public async Task<ActionResult<PersonDto>> Create(PersonCreateRequest request, CancellationToken ct)
    {
        var typed = request.Email?.Trim();
        var email = string.IsNullOrEmpty(typed) ? null : typed.ToLowerInvariant();
        if (email is not null && !IsPlausibleEmail(email)) return BadRequest("Enter a valid email address.");

        string name;
        if (email is not null && string.IsNullOrWhiteSpace(request.Name))
        {
            name = PersonName.FromEmail(typed, fallback: email);
        }
        else if (!PersonName.TryNormalize(request.Name, out name, out var error))
        {
            return BadRequest(error);
        }

        if (!PersonRoles.TryParse(request.Role, out var role)) return BadRequest(PersonRoles.Sentence);

        // Claimed or not: an address already on a signed-in person would
        // otherwise become a second person for one human. The unique index is
        // the backstop for a race; this is the tested path.
        if (email is not null && await db.ExternalIdentities.AnyAsync(
                i => i.Provider == EfExternalIdentity.GoogleProvider && i.Email.ToLower() == email, ct))
            return Conflict(AlreadyListedSentence(email));

        var now = time.GetUtcNow();
        var person = new EfPerson { Name = name, Role = role, CreatedAt = now, UpdatedAt = now };
        db.People.Add(person);
        if (email is not null)
        {
            db.ExternalIdentities.Add(new EfExternalIdentity
            {
                Provider = EfExternalIdentity.GoogleProvider,
                Subject = null,
                Email = email,
                Person = person,
                CreatedAt = now,
            });
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (email is not null)
        {
            return Conflict(AlreadyListedSentence(email));
        }

        // Two people may share a name and that is not an error - households
        // contain a Sam and a Sam, and the id is what anything actually keys
        // on. There is deliberately no unique index behind this.
        return CreatedAtAction(nameof(Get), new { id = person.Id }, new PersonDto(
            person.Id, person.Name, PersonRoles.ToWire(person.Role), person.CreatedAt, person.UpdatedAt, null, 0,
            email, email is null ? null : EfExternalIdentity.GoogleProvider, null));
    }

    private static string AlreadyListedSentence(string email) => $"{email} is already on this list.";

    /// <summary>
    /// Deliberately minimal: one <c>@</c>, something either side, no spaces.
    /// Whether the address is real is Google's to say at sign-in.
    /// </summary>
    private static bool IsPlausibleEmail(string email)
    {
        if (email.Length > 320 || email.Any(char.IsWhiteSpace)) return false;
        var at = email.IndexOf('@');
        return at > 0 && at == email.LastIndexOf('@') && at < email.Length - 1;
    }

    [RequireAdmin]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PersonDto>> Update(Guid id, PersonWriteRequest request, CancellationToken ct)
    {
        if (!PersonName.TryNormalize(request.Name, out var name, out var error)) return BadRequest(error);
        if (!PersonRoles.TryParse(request.Role, out var role)) return BadRequest(PersonRoles.Sentence);

        var person = await db.People.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (person is null) return NotFound();

        if (person.Role == PersonRole.Admin && role != PersonRole.Admin && await IsLastAdmin(id, ct))
            return Conflict(LastAdminSentence("demoted"));

        person.Name = name;
        person.Role = role;
        person.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);

        return await Get(id, ct);
    }

    /// <summary>
    /// Deletes the person. Their photo, their identities and their sessions go
    /// with them (cascade): an ownerless grant is refused by the role gate
    /// anyway, so a session that outlived its person would be a credential
    /// that reaches nothing. Refused with a 409 for the last Admin.
    /// </summary>
    [RequireAdmin]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var person = await db.People.FindAsync([id], ct);
        if (person is null) return NotFound();

        if (person.Role == PersonRole.Admin && await IsLastAdmin(id, ct))
            return Conflict(LastAdminSentence("deleted"));

        db.People.Remove(person);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Whether nobody but this person holds Admin - so a write that takes it
    /// from them would leave the install with no one who can manage people.
    /// A second Admin demoting themselves is fine; it is the last that is not.
    /// </summary>
    private Task<bool> IsLastAdmin(Guid id, CancellationToken ct)
        => db.People.AllAsync(p => p.Id == id || p.Role != PersonRole.Admin, ct);

    private static string LastAdminSentence(string verb)
        => $"The last Admin cannot be {verb}. Make someone else an Admin first.";

    // ---- Sessions (read-only here; the write lives on AuthController) ----

    /// <summary>
    /// Which enrolled devices are this person's. Empty is the normal answer for
    /// someone who has never held a tablet.
    ///
    /// Guarded despite being a read, for the same reason
    /// <c>AuthController.ListGrants</c> is: it is an inventory of credentials,
    /// sliced by owner rather than listed whole, and a boundary that stopped at
    /// the unsliced version would not be one.
    /// </summary>
    [RequireAdmin]
    [HttpGet("{id:guid}/sessions")]
    public async Task<ActionResult<IReadOnlyList<PersonSessionDto>>> GetSessions(Guid id, CancellationToken ct)
    {
        if (!await db.People.AnyAsync(p => p.Id == id, ct)) return NotFound();

        return await db.AuthGrants.AsNoTracking()
            .Where(g => g.PersonId == id)
            .OrderByDescending(g => g.CreatedAt)
            .Select(g => new PersonSessionDto(g.Id, g.Label, g.Kind, g.CreatedAt, g.LastSeenAt))
            .ToListAsync(ct);
    }

    // ---- Photo ----

    /// <summary>
    /// The bytes, with the type they were sniffed as at upload. Cached
    /// privately and revalidated by ETag rather than cached hard: the URL is
    /// stable across uploads on purpose (so an &lt;img src&gt; never has to be
    /// rebuilt), which means the freshness has to come from the validator.
    /// </summary>
    [RequireRole(PersonRole.User)]
    [HttpGet("{id:guid}/photo")]
    public async Task<IActionResult> GetPhoto(Guid id, CancellationToken ct)
    {
        var photo = await db.PersonPhotos.AsNoTracking().FirstOrDefaultAsync(p => p.PersonId == id, ct);
        if (photo is null) return NotFound();

        // nosniff, because this is user-supplied content served from the
        // install's own origin. The type was sniffed rather than taken on
        // trust (PersonPhoto), and this is the other half of that: the browser
        // is told not to second-guess it either.
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "private, max-age=0, must-revalidate";

        // The upload time *is* the version - a photo cannot change without it
        // changing - so it makes the ETag directly rather than hashing two
        // megabytes on every request to learn something already in the row.
        var etag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{photo.UpdatedAt.UtcTicks:x}\"");
        return File(photo.Bytes, photo.ContentType, lastModified: photo.UpdatedAt, entityTag: etag);
    }

    /// <summary>
    /// Replaces the photo. The body is the image itself rather than a multipart
    /// form: there is exactly one file and no fields beside it, so a multipart
    /// envelope would be ceremony around a single blob - and the browser sends
    /// a Blob as a raw body without any help.
    ///
    /// The request's own Content-Type is ignored entirely; see
    /// <see cref="PersonPhoto"/>.
    /// </summary>
    [RequireAdmin]
    [HttpPut("{id:guid}/photo")]
    [RequestSizeLimit(PersonPhoto.MaxBytes + 1024)]
    public async Task<ActionResult<PersonDto>> PutPhoto(Guid id, CancellationToken ct)
    {
        var person = await db.People.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (person is null) return NotFound();

        // The bound is the [RequestSizeLimit] above, which Kestrel enforces
        // *during* this read rather than after it - an over-large body aborts
        // with a 413 partway through instead of being buffered whole and then
        // measured. Which is the point: reading an unbounded request into
        // memory to find out how big it is, is the bug the cap exists to
        // prevent, and a check placed after this line would be that bug.
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();

        if (!PersonPhoto.TryDetectContentType(bytes, out var contentType, out var error)) return BadRequest(error);

        var now = time.GetUtcNow();
        var photo = await db.PersonPhotos.FirstOrDefaultAsync(p => p.PersonId == id, ct);
        if (photo is null)
        {
            db.PersonPhotos.Add(new EfPersonPhoto { PersonId = id, Bytes = bytes, ContentType = contentType, UpdatedAt = now });
        }
        else
        {
            photo.Bytes = bytes;
            photo.ContentType = contentType;
            photo.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return await Get(id, ct);
    }

    [RequireAdmin]
    [HttpDelete("{id:guid}/photo")]
    public async Task<IActionResult> DeletePhoto(Guid id, CancellationToken ct)
    {
        var photo = await db.PersonPhotos.FirstOrDefaultAsync(p => p.PersonId == id, ct);
        if (photo is null) return NotFound();

        db.PersonPhotos.Remove(photo);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// One projection, shared by every read, so the list and the single-item
    /// reads cannot drift. An expression rather than a method because it has to
    /// translate to SQL - the photo's presence is a join, not a load, and the
    /// session count is a subquery rather than a second round trip.
    /// </summary>
    private static readonly System.Linq.Expressions.Expression<Func<EfPerson, PersonDto>> ToDto =
        p => new PersonDto(
            p.Id,
            p.Name,
            p.Role == PersonRole.Admin ? "admin" : p.Role == PersonRole.User ? "user" : "pending",
            p.CreatedAt,
            p.UpdatedAt,
            p.Photo == null ? null : p.Photo.UpdatedAt,
            p.Grants.Count,
            p.Identities.OrderByDescending(i => i.LastSignInAt).Select(i => i.Email).FirstOrDefault(),
            p.Identities.OrderByDescending(i => i.LastSignInAt).Select(i => i.Provider).FirstOrDefault(),
            p.Identities.OrderByDescending(i => i.LastSignInAt).Select(i => (DateTimeOffset?)i.LastSignInAt).FirstOrDefault());
}

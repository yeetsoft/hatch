namespace Hatch.Api.Models.People;

/// <summary>
/// One person, as the admin People page sees them.
///
/// The photo is not here and will not be: it is bytes, it is served from its
/// own URL, and inlining even a base64 avatar into a list response makes the
/// list response the size of the photos. <paramref name="PhotoUpdatedAt"/> is
/// the substitute - it says both *whether* there is a photo and *which* photo,
/// which is what lets the client point an &lt;img&gt; at a stable URL and still
/// see a new upload the moment it lands.
/// </summary>
/// <param name="SessionCount">
/// How many enrolled devices are linked to this person. Included because it is
/// the answer to the only question the People list raises on its own - "can
/// this person actually get in?" - and computing it here costs one GROUP BY
/// against a table with a household's worth of rows in it.
/// </param>
/// <param name="Role">
/// What this person may reach: <c>pending</c>, <c>user</c> or <c>admin</c>.
/// Read openly on purpose: this list is what the family shell renders names and
/// faces from, and who may do what is not a secret in a household. See
/// EfPerson.Role.
/// </param>
/// <param name="Email">
/// The address the person's most recently used identity reported, or null for
/// someone with none - a person made by hand on this page has never signed in
/// anywhere. A label that follows the account, never a key.
/// </param>
/// <param name="Provider">The provider of that same identity (<c>google</c>), or null.</param>
/// <param name="LastSignInAt">When that identity last signed in, or null.</param>
public record PersonDto(
    Guid Id,
    string Name,
    string Role,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PhotoUpdatedAt,
    int SessionCount,
    string? Email = null,
    string? Provider = null,
    DateTimeOffset? LastSignInAt = null)
{
    public bool HasPhoto => PhotoUpdatedAt is not null;
}

/// <summary>
/// What the People page posts. Two fields, deliberately: everything else a
/// person will grow is additive, and a write model with room for fields that do
/// not exist yet is a write model nobody can read.
///
/// <paramref name="Name"/> is raw text - normalization is the server's job
/// (<see cref="Hatch.Api.Common.PersonName"/>), because a client that
/// normalizes is a client that can be replaced by one that does not.
///
/// <paramref name="Role"/> is required and has no default, because a default
/// is a demotion by omission: a client that only meant to rename someone would
/// silently reset their role. A write that names no role, or one that is not
/// <c>pending</c>, <c>user</c> or <c>admin</c>, is refused with a sentence.
/// </summary>
public record PersonWriteRequest(string? Name, string? Role);

/// <summary>The wire spelling of <see cref="Hatch.Api.Ef.PersonRole"/>: lowercase, and never a number.</summary>
public static class PersonRoles
{
    public const string Sentence = "Role must be one of: pending, user, admin.";

    public static string ToWire(Hatch.Api.Ef.PersonRole role) => role switch
    {
        Hatch.Api.Ef.PersonRole.Pending => "pending",
        Hatch.Api.Ef.PersonRole.User => "user",
        Hatch.Api.Ef.PersonRole.Admin => "admin",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    /// <summary>
    /// Case-insensitive, and deliberately not <c>Enum.TryParse</c>, which
    /// would accept "1" and "7".
    /// </summary>
    public static bool TryParse(string? value, out Hatch.Api.Ef.PersonRole role)
    {
        role = default;
        switch (value?.Trim().ToLowerInvariant())
        {
            case "pending": role = Hatch.Api.Ef.PersonRole.Pending; return true;
            case "user": role = Hatch.Api.Ef.PersonRole.User; return true;
            case "admin": role = Hatch.Api.Ef.PersonRole.Admin; return true;
            default: return false;
        }
    }
}

/// <summary>
/// One enrolled device on a person's row. A deliberately thinner view than
/// <c>AuthGrantDto</c>: the People page is answering "which devices are hers",
/// not offering to revoke them, and the fields that only make sense next to a
/// Revoke button belong on the page that has one.
/// </summary>
public record PersonSessionDto(
    Guid Id,
    string Label,
    Ef.AuthGrantKind Kind,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSeenAt);

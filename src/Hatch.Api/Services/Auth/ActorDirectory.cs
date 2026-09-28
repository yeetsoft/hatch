using Hatch.Api.Ef;
using Hatch.Api.Services.DeviceMapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Services.Auth;

/// <summary>
/// Somebody the house knows: a person, or an API key, told apart by
/// <see cref="Kind"/> and carrying the name each is called by.
/// </summary>
/// <remarks>
/// One record for both because the question every caller actually asks is "who
/// owns this", and a person and a key are the same kind of answer to it - the
/// same argument <see cref="ICallerIdentity.ActorNameAsync"/> makes for putting
/// both names in one audit column. The pair is what identifies one: two ids
/// from two tables can collide in principle, and the kind is what makes
/// <c>person:…</c> and <c>key:…</c> two different things rather than one
/// ambiguous guid.
/// </remarks>
public record Actor(string Kind, Guid Id, string Name);

/// <summary>The two kinds of actor, as they are written on the wire.</summary>
public static class ActorKind
{
    public const string Person = "person";
    public const string Key = "key";

    public static bool IsKnown(string? kind) => kind is Person or Key;
}

/// <summary>
/// Who is out there - everybody a thing in this house could belong to, and what
/// an id already written down means today.
///
/// It exists beside <see cref="ICallerIdentity"/> and for the same stated
/// reason: one implementation of a question a module needs answered. That
/// module is Hatch, whose issues carry an assignee (docs/hatch.md), and the
/// shape of the answer matters more than the answer because whatever asks next
/// inherits it.
/// </summary>
/// <remarks>
/// <para>The one rule the whole design rests on is <em>liveness</em>, and it
/// lives here rather than at each reader. An assignee resolves only to an
/// identity that still exists - a person row that is still there, or a key that
/// is there and not revoked - and an id that does not resolve reads as
/// <em>nobody</em>. Everywhere, at the same instant, without a sweeper.</para>
///
/// <para>That is deliberately not a foreign key with <c>ON DELETE SET NULL</c>,
/// and it could not have been. A module owns its own schema and may not point a
/// constraint at <c>public.People</c> (Modules/README.md), and revoking a key
/// keeps the row on purpose (<see cref="EfApiKey.RevokedAt"/>) so a cascade
/// would never have fired for half the cases anyway. A predicate every reader
/// applies cannot fail to run, cannot be half-migrated, and cannot leave a row
/// pointing at something that is gone.</para>
///
/// <para>The trade, said out loud: an issue assigned to a key that is later
/// revoked reads as unassigned without announcing it. The event trail still
/// names who it was, which is what answers "whose was this in March".</para>
/// </remarks>
public interface IActorDirectory
{
    /// <summary>
    /// Everybody who could be given something: every person, then every live
    /// key, each A→Z. The order is the directory's rather than the caller's so
    /// that a picker built from it is stable between reads.
    ///
    /// With the wall off the local person is first, ahead of the A→Z - you,
    /// then everybody else. See the implementation for why that is a prepend
    /// rather than a name-ordered merge.
    /// </summary>
    Task<IReadOnlyList<Actor>> LiveAsync(CancellationToken ct);

    /// <summary>
    /// What a <c>(kind, id)</c> already written down means today, or null - an
    /// unknown kind, a null id, a person who has been deleted, a key that has
    /// been revoked. Answered from the same list <see cref="LiveAsync"/> hands
    /// out, so "what does this mean" and "what may I pick" cannot disagree.
    /// </summary>
    Task<Actor?> ResolveAsync(string? kind, Guid? id, CancellationToken ct);

    /// <summary>
    /// Who is making this request, as an actor - the person holding the device,
    /// else the key it presented, else the local caller where the wall is off.
    /// Null is what is left: a request with no wall to authenticate it and no
    /// HTTP context behind it at all, which is background work.
    /// </summary>
    Task<Actor?> MeAsync(CancellationToken ct);

    /// <summary>
    /// Whose work this caller is doing - what a <c>--mine</c> dispatch pass
    /// reads to decide which tickets are its own. The signed-in person; else
    /// the calling key's owner, resolved live so a deleted person reads as
    /// nobody; else, where the wall is off, the local person - including a
    /// runner that named itself with <see cref="LocalCaller.RunnerHeader"/>,
    /// which is not this method's answer even though it is <see cref="MeAsync"/>'s.
    /// Null when none of those apply: a key that belongs to nobody, or the
    /// wall is up and nothing authenticated the request.
    /// </summary>
    /// <remarks>
    /// Not <see cref="MeAsync"/> under another name, and the difference is the
    /// whole reason this exists. <c>MeAsync</c> says who is <em>calling</em>,
    /// which is what an audit trail wants - a runner is itself, not a stand-in
    /// for whoever started it. This says whose tickets the caller works on,
    /// which for a keyless runner in local mode is the person at the machine,
    /// not the runner's own transient name - a runner is never assignable
    /// (see the liveness remarks above), so it could never be "its own"
    /// tickets in the first place.
    /// </remarks>
    Task<Actor?> PrincipalAsync(CancellationToken ct);
}

/// <summary>
/// Reads the directory once per request and remembers it. Scoped, so a board
/// read that resolves two hundred cards costs the same two queries as one that
/// resolves none - which is what lets every projection site ask without
/// thinking about it.
/// </summary>
public class ActorDirectory(
    AppDbContext db, ICallerIdentity caller, TimeProvider time,
    ISiteSettingsService settings, IOptions<AuthOptions> options) : IActorDirectory
{
    private IReadOnlyList<Actor>? live;

    public async Task<IReadOnlyList<Actor>> LiveAsync(CancellationToken ct)
    {
        if (live is not null) return live;

        var people = await db.People.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new Actor(ActorKind.Person, p.Id, p.Name))
            .ToListAsync(ct);

        // Revocation is a column and not a delete, so the predicate is here and
        // not in the absence of a row. Evaluated in memory against one clock
        // reading, so every row in a single answer is judged against the same
        // instant - EfApiKey.IsLive is the one definition of it.
        var now = time.GetUtcNow();
        var keys = await db.ApiKeys.AsNoTracking().OrderBy(k => k.Name).ToListAsync(ct);

        // Prepended rather than merged into the name order, for two reasons.
        // The database's collation decided the order of the rows it returned,
        // and inserting into that list in memory would be this app second-
        // guessing it with a different comparison. And "you" is not one name
        // among many on a picker: it is the one press that is always the same
        // press, which is why "Assign to me" exists at all.
        //
        // Only a person, never a runner. A runner is transient and self-named,
        // so nothing may be assigned to one - ResolveAsync answering null for
        // its id is the correct reading of an id that means nothing tomorrow.
        Actor[] me = await caller.LocalAsync(ct) is { Kind: ActorKind.Person } local ? [local] : [];

        return live =
        [
            .. me,
            .. people,
            .. keys.Where(k => k.IsLive(now)).Select(k => new Actor(ActorKind.Key, k.Id, k.Name)),
        ];
    }

    public async Task<Actor?> ResolveAsync(string? kind, Guid? id, CancellationToken ct)
    {
        if (kind is null || id is not { } wanted) return null;

        return (await LiveAsync(ct)).FirstOrDefault(a => a.Kind == kind && a.Id == wanted);
    }

    /// <summary>
    /// The person, the key, then the local caller - the same order
    /// <see cref="CallerIdentity.ActorNameAsync"/> takes, so "who am I" reads
    /// one way however it is asked.
    /// </summary>
    public async Task<Actor?> MeAsync(CancellationToken ct)
    {
        if (await caller.PersonAsync(ct) is { } person)
            return new Actor(ActorKind.Person, person.Id, person.Name);

        if (await caller.ApiKeyAsync(ct) is { } key)
            return new Actor(ActorKind.Key, key.Id, key.Name);

        return await caller.LocalAsync(ct);
    }

    /// <summary>
    /// The person, the key's live owner, then - where the wall is off - the
    /// local person by construction rather than by delegating to
    /// <see cref="ICallerIdentity.LocalAsync"/>, whose third lane answers a
    /// runner header with the runner's own transient name. See the interface
    /// remarks for why that answer is right for <see cref="MeAsync"/> and
    /// wrong for this.
    /// </summary>
    public async Task<Actor?> PrincipalAsync(CancellationToken ct)
    {
        if (await caller.PersonAsync(ct) is { } person)
            return new Actor(ActorKind.Person, person.Id, person.Name);

        if (await caller.ApiKeyAsync(ct) is { } key)
            return key.OwnerPersonId is { } ownerId ? await ResolveAsync(ActorKind.Person, ownerId, ct) : null;

        // LocalAsync's own gate already covers "the wall is off, there is a
        // request, and neither branch above answered" - reused here as the
        // presence check, and not for its answer: a runner header would make
        // it a key, and this wants the person regardless.
        if (await caller.LocalAsync(ct) is null) return null;

        var configured = (await settings.GetAsync(ct)).LocalPersonName;
        var name = LocalCaller.PersonNameOf(configured, options.Value.LocalPerson.Name);
        return new Actor(ActorKind.Person, LocalCaller.PersonId, name);
    }
}

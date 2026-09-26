using Hatch.Api.Ef;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Services.Auth;

/// <summary>What the role gate decided. Dormant and Person both mean "serve it"; they differ in whether anyone was checked.</summary>
public enum RoleOutcome
{
    /// <summary>Enforcement is switched off, so nothing was looked at. This is the default posture and the whole rollback.</summary>
    Dormant,

    /// <summary>The caller's device belongs to a person whose <see cref="EfPerson.Role"/> reaches the level asked for.</summary>
    Person,

    /// <summary>An API key carrying the scope this route is willing to accept. Not a person, and never an administrator - a key reaches exactly what its scopes name.</summary>
    Key,

    /// <summary>Refused. What that looks like is the caller's choice: a 404 for the admin app, a 403 for the API.</summary>
    Refused,
}

/// <summary>
/// One role decision, with the reason a refusal happened - the same shape
/// <see cref="AuthDecision"/> carries, and for the same reason: a refusal
/// nobody can explain is a support conversation with no starting point.
/// </summary>
public record RoleDecision(RoleOutcome Outcome, EfPerson? Person, string? Reason, EfApiKey? ApiKey = null)
{
    /// <summary>No credential at all. Reachable only where the wall itself does not enforce in-process - otherwise this request never got here.</summary>
    public const string NoGrant = "no_grant";

    /// <summary>An enrolled device nobody has claimed - the hallway tablet, or a phone whose owner was never linked on the Sessions page.</summary>
    public const string NoPerson = "no_person";

    /// <summary>A device belonging to a person who has not been let in yet. Refused at every level, whatever was asked for.</summary>
    public const string PendingApproval = "pending_approval";

    /// <summary>A device belonging to a person whose role is below the level this route asks for.</summary>
    public const string NotAdmin = "not_admin";

    /// <summary>
    /// An API key reaching a route that accepts no scope at all. Every route
    /// without an <c>AcceptScope</c> is one: minting credentials, revoking
    /// sessions, editing the house. Those are the operator's, and a key is not a person.
    /// </summary>
    public const string KeyNotAccepted = "key_not_accepted";

    /// <summary>An API key reaching a route that accepts a scope this key does not carry.</summary>
    public const string ScopeMismatch = "scope_mismatch";

    public bool IsAllowed => Outcome != RoleOutcome.Refused;

    public static readonly RoleDecision Dormant = new(RoleOutcome.Dormant, null, null);

    public static RoleDecision Allow(EfPerson person) => new(RoleOutcome.Person, person, null);

    public static RoleDecision AllowKey(EfApiKey key) => new(RoleOutcome.Key, null, null, key);

    public static RoleDecision Refuse(string reason) => new(RoleOutcome.Refused, null, reason);
}

/// <summary>
/// Whether the caller's role reaches what they asked for - the second
/// question the wall asks, after "is this device enrolled at all", and now also
/// the question that decides what an API key may reach.
///
/// It is deliberately a *separate* gate from <see cref="IAuthGate"/> rather
/// than a widening of it. The wall decides whether a request reaches the app,
/// runs on every request, and is enforced in two places (Traefik and in
/// process). This decides whether an already-authenticated request may proceed,
/// runs on the handful of routes that ask, and is enforced in one place, which
/// is this process. Folding the two together would put a person lookup on the
/// health probes and the media stream, which is exactly what the allow-list
/// exists to prevent.
/// </summary>
public interface IRoleGate
{
    /// <summary>
    /// Whether this gate refuses anything at all: exactly whether the wall is
    /// on (<see cref="AuthOptions.Enabled"/>). With the wall off there is no
    /// identity to read, and every route behaves as it always has.
    /// </summary>
    bool Enabled { get; }

    /// <summary>
    /// The decision for the current request. The three descriptive arguments
    /// are only ever the refusal log line's - the decision itself reads
    /// nothing but the caller's credential, <paramref name="minimum"/> and
    /// <paramref name="acceptScope"/>.
    /// </summary>
    /// <param name="minimum">
    /// The lowest role this route serves. A Pending person is refused whatever
    /// this is. It is never read for a key, which is decided on scope alone.
    /// </param>
    /// <param name="acceptScope">
    /// The scope this route is willing to accept from an API key, or null for
    /// the ordinary case: no key. It changes nothing for a person -
    /// a route that accepts a scope is not thereby open to a lower role.
    /// </param>
    Task<RoleDecision> EvaluateAsync(string? method, PathString path, string? clientIp, PersonRole minimum, string? acceptScope, CancellationToken ct);
}

/// <summary>
/// The one place a person's role is read. Everything that guards on it - the
/// operator apps' bundles (<see cref="Hatch.Api.Common.AdminAppMiddleware"/>)
/// and every <see cref="Hatch.Api.Common.RequireRoleAttribute"/> on the API -
/// comes through here, so "who may reach this" has one answer and one log
/// line rather than one per asker.
///
/// There is no switch for whether roles are enforced: whenever the wall is on,
/// they are. An upgrade cannot lock anyone out because the migration that
/// introduced roles made nobody Pending.
/// </summary>
public class RoleGate(
    ICallerIdentity caller,
    IOptions<AuthOptions> options,
    ILogger<RoleGate> logger) : IRoleGate
{
    private readonly AuthOptions options = options.Value;

    /// <summary>
    /// <c>Auth:Enabled</c> alone, because an install with no wall has no identity
    /// to read. It being false is the whole of local development and of
    /// <c>AUTH_MODE=none</c>: nobody is enrolled, no cookie is expected, and a
    /// guard that refused on that basis would make the admin app unreachable
    /// for every developer running `make run`.
    ///
    /// Deliberately *not* also gated on <c>Auth:EnforceInProcess</c>. That
    /// switch exists so Traefik can be the sole enforcer of the wall on exactly
    /// the annotated routes; this boundary has no proxy half at all, so tying
    /// it to that switch would mean the canary silently ignores an enforcement
    /// the operator asked for.
    /// </summary>
    public bool Enabled => options.Enabled;

    public async Task<RoleDecision> EvaluateAsync(string? method, PathString path, string? clientIp, PersonRole minimum, string? acceptScope, CancellationToken ct)
    {
        if (!Enabled) return RoleDecision.Dormant;

        // The key lane, decided before the person lane and never falling
        // through to it. A key has no person and never will, so a key that
        // fails here has to be refused rather than handed on to a check that
        // would refuse it again for the wrong reason - and, more importantly,
        // a key must not inherit the answer that a browser signed in on the
        // same machine would have got.
        if (await caller.ApiKeyAsync(ct) is { } key)
        {
            if (acceptScope is not { Length: > 0 })
                return Refuse(RoleDecision.KeyNotAccepted, method, path, clientIp, null);

            return key.HasScope(acceptScope)
                ? RoleDecision.AllowKey(key)
                : Refuse(RoleDecision.ScopeMismatch, method, path, clientIp, null);
        }

        var grant = await caller.GrantAsync(ct);
        if (grant is null) return Refuse(RoleDecision.NoGrant, method, path, clientIp, null);

        // The person, not the id: this is the one question in the app that
        // needs a column off the row rather than the key of it, which is why
        // ICallerIdentity carries PersonAsync at all.
        var person = await caller.PersonAsync(ct);
        if (person is null) return Refuse(RoleDecision.NoPerson, method, path, clientIp, grant);

        // Pending is refused before the comparison, so it is refused at every
        // level and with its own reason: "you are waiting to be let in" is a
        // different sentence on screen from "you are not an administrator".
        if (person.Role == PersonRole.Pending)
            return Refuse(RoleDecision.PendingApproval, method, path, clientIp, grant, person.Role);

        return person.Role >= minimum
            ? RoleDecision.Allow(person)
            : Refuse(RoleDecision.NotAdmin, method, path, clientIp, grant, person.Role);
    }

    /// <summary>
    /// Every refusal is a Warning, the same as the wall's, and carries the
    /// grant id rather than the label - free text an operator typed is a field
    /// nobody can filter on (docs/auth-architecture.md, "The rules that must
    /// not quietly change").
    ///
    /// It is worth more here than at the wall. A refusal at the wall is an
    /// un-enrolled device, which is ordinary; a refusal here is an enrolled
    /// household member reaching for something they cannot have, which is
    /// either a permission that wants granting or the first thing an operator
    /// would want to know about.
    /// </summary>
    private RoleDecision Refuse(string reason, string? method, PathString path, string? clientIp, EfAuthGrant? grant, PersonRole? role = null)
    {
        logger.LogWarning(
            "Access refused {Reason} for {Method} {Path} from {ClientIp} (grant {GrantId}, person {PersonId}, role {Role})",
            reason, method, path.Value, clientIp, grant?.Id, grant?.PersonId, role);

        return RoleDecision.Refuse(reason);
    }
}

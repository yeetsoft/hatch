using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Tests.Auth;

/// <summary>
/// The one place Person.Role is read. Two properties carry everything built on
/// top of it: the gate is inert exactly where the wall is off, and when it is
/// not inert it answers from the credential on the request and the level the
/// route asked for, and from nothing else.
///
/// The dormant case is the one worth breaking a build over. It is the whole of
/// local development and the whole of AUTH_MODE=none - a gate that started
/// deciding on its own would take the apps away from every developer running
/// `make run`.
/// </summary>
public class RoleGateTests
{
    [Fact]
    public async Task IsDormantWhereTheWallIsOff_ForEveryone()
    {
        // Nothing is looked at, not even the cookie - and a Pending person is
        // let through, because with no wall there is nobody to be Pending.
        var caller = new StubCallerIdentity(Grant(Person(PersonRole.Pending)));
        var gate = NewGate(caller, enabled: false);

        Assert.False(gate.Enabled);
        var decision = await gate.EvaluateAsync("GET", "/apps/admin/", null, PersonRole.Admin, acceptScope: null, default);

        Assert.Equal(RoleOutcome.Dormant, decision.Outcome);
        Assert.True(decision.IsAllowed);
        Assert.Equal(0, caller.Asked);
    }

    [Fact]
    public void IsEnabledWheneverTheWallIs()
    {
        Assert.True(NewGate(new StubCallerIdentity(null), enabled: true).Enabled);
    }

    /// <summary>
    /// Each role against each level. Pending is refused at both, with its own
    /// reason; User reaches User and not Admin; Admin reaches both.
    /// </summary>
    [Theory]
    [InlineData(PersonRole.Pending, PersonRole.User, RoleDecision.PendingApproval)]
    [InlineData(PersonRole.Pending, PersonRole.Admin, RoleDecision.PendingApproval)]
    [InlineData(PersonRole.User, PersonRole.User, null)]
    [InlineData(PersonRole.User, PersonRole.Admin, RoleDecision.NotAdmin)]
    [InlineData(PersonRole.Admin, PersonRole.User, null)]
    [InlineData(PersonRole.Admin, PersonRole.Admin, null)]
    public async Task AnswersAPersonsRoleAgainstTheLevelAsked(PersonRole role, PersonRole minimum, string? refusal)
    {
        var person = Person(role);
        var gate = NewGate(new StubCallerIdentity(Grant(person)));

        var decision = await gate.EvaluateAsync("DELETE", "/api/zones/x", "10.0.0.7", minimum, acceptScope: null, default);

        if (refusal is null)
        {
            Assert.Equal(RoleOutcome.Person, decision.Outcome);
            Assert.True(decision.IsAllowed);
            Assert.Same(person, decision.Person);
            Assert.Null(decision.Reason);
        }
        else
        {
            Assert.Equal(RoleOutcome.Refused, decision.Outcome);
            Assert.False(decision.IsAllowed);
            Assert.Equal(refusal, decision.Reason);
            Assert.Null(decision.Person);
        }
    }

    /// <summary>
    /// The hallway tablet, and every grant minted before people existed. It is
    /// a distinct reason from "not an admin" because the fix is different: this
    /// one is a dropdown on the Sessions page, not a role on a person.
    /// </summary>
    [Theory]
    [InlineData(PersonRole.User)]
    [InlineData(PersonRole.Admin)]
    public async Task RefusesAnEnrolledDeviceNobodyHasClaimed(PersonRole minimum)
    {
        var gate = NewGate(new StubCallerIdentity(Grant(person: null)));

        var decision = await gate.EvaluateAsync("POST", "/api/people", "10.0.0.7", minimum, acceptScope: null, default);

        Assert.Equal(RoleDecision.NoPerson, decision.Reason);
    }

    /// <summary>
    /// Reachable only where the wall does not enforce in process - the canary,
    /// or a pod reached directly inside the cluster. This gate does not depend
    /// on Auth:EnforceInProcess on purpose, so the role boundary still stands
    /// on exactly the configuration where the wall's own does not.
    /// </summary>
    [Fact]
    public async Task RefusesACallerWithNoCredentialAtAll()
    {
        var gate = NewGate(new StubCallerIdentity(null));

        var decision = await gate.EvaluateAsync("GET", "/apps/admin/", "10.0.0.7", PersonRole.User, acceptScope: null, default);

        Assert.Equal(RoleDecision.NoGrant, decision.Reason);
    }

    /// <summary>
    /// The key lane is decided on scope alone, so the level asked for changes
    /// nothing: the same answer at User and at Admin.
    /// </summary>
    [Theory]
    [InlineData(PersonRole.User)]
    [InlineData(PersonRole.Admin)]
    public async Task DecidesAKeyOnScopeAlone_WhateverTheLevel(PersonRole minimum)
    {
        var key = Key("Claude", ApiKeyScopes.Hatch);
        var gate = NewGate(new StubCallerIdentity(null, key));

        var accepted = await gate.EvaluateAsync("GET", "/api/hatch/board", null, minimum, ApiKeyScopes.Hatch, default);
        Assert.Equal(RoleOutcome.Key, accepted.Outcome);
        Assert.Same(key, accepted.ApiKey);

        var mismatch = await gate.EvaluateAsync("GET", "/api/x", null, minimum, "other", default);
        Assert.Equal(RoleDecision.ScopeMismatch, mismatch.Reason);

        var notAccepted = await gate.EvaluateAsync("GET", "/api/people/x/sessions", null, minimum, acceptScope: null, default);
        Assert.Equal(RoleDecision.KeyNotAccepted, notAccepted.Reason);
    }

    /// <summary>
    /// The gate asks for the grant once, and reads the person off the same
    /// answer. Asking twice would be harmless here and expensive in
    /// production, where a grant lookup writes LastSeenAt.
    /// </summary>
    [Fact]
    public async Task ResolvesTheCallerOnceForOneDecision()
    {
        var caller = new StubCallerIdentity(Grant(Person(PersonRole.Admin)));

        await NewGate(caller).EvaluateAsync("GET", "/apps/admin/", null, PersonRole.User, acceptScope: null, default);

        Assert.Equal(1, caller.Asked);
    }

    private static EfApiKey Key(string name, params string[] scopes) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Prefix = "hatch_ak_abc",
        Hash = new byte[AuthHash.Length],
        Scopes = scopes,
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    private static RoleGate NewGate(ICallerIdentity caller, bool enabled = true) =>
        new(caller, Options.Create(new AuthOptions { Enabled = enabled }), NullLogger<RoleGate>.Instance);

    private static EfPerson Person(PersonRole role) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Ada",
        Role = role,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    private static EfAuthGrant Grant(EfPerson? person) => new()
    {
        Id = Guid.NewGuid(),
        TokenHash = new byte[AuthHash.Length],
        Label = "Ada's iPhone",
        Kind = AuthGrantKind.Interactive,
        CreatedAt = DateTimeOffset.UnixEpoch,
        CookieIssuedAt = DateTimeOffset.UnixEpoch,
        PersonId = person?.Id,
        Person = person,
    };

    /// <summary>
    /// Answers with one grant, or none, and counts the asking - which is how a
    /// test proves the dormant path never looks at a credential at all.
    /// </summary>
    private sealed class StubCallerIdentity(EfAuthGrant? grant, EfApiKey? key = null) : ICallerIdentity
    {
        public int Asked { get; private set; }

        public Task<EfAuthGrant?> GrantAsync(CancellationToken ct)
        {
            Asked++;
            return Task.FromResult(grant);
        }

        public Task<Guid?> PersonIdAsync(CancellationToken ct) => Task.FromResult(grant?.PersonId);

        public Task<EfPerson?> PersonAsync(CancellationToken ct) => Task.FromResult(grant?.Person);

        /// <summary>Uncounted: <see cref="Asked"/> is about grant lookups, which are the expensive ones - they write LastSeenAt.</summary>
        public Task<EfApiKey?> ApiKeyAsync(CancellationToken ct) => Task.FromResult(key);

        /// <summary>Always nobody: RoleGate is only awake where the wall is up, and the local lane is only alive where it is down.</summary>
        public Task<Actor?> LocalAsync(CancellationToken ct) => Task.FromResult<Actor?>(null);

        public Task<bool> IsProgramAsync(CancellationToken ct) => Task.FromResult(key is not null);

        public Task<string> ActorNameAsync(CancellationToken ct) =>
            Task.FromResult(grant?.Person?.Name ?? key?.Name ?? CallerIdentity.Unattributed);
    }
}

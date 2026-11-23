using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using QuartzLogProvider = Quartz.Logging.LogProvider;

namespace Hatch.Api.Tests.Hatch;

/// <summary>A person made straight in the database, with the scope row that decides what they see.</summary>
internal sealed record WalledPerson(Guid Id, string Name, string Scope, PersonRole Role);

/// <summary>An API key, with the one secret it will ever show and the person who owns it, if any.</summary>
internal sealed record WalledKey(string Name, Guid? OwnerId, string Secret);

/// <summary>
/// Ids the seed does not hand back and a probe needs: a playbook and the
/// proposals on each side, a trunk build and a finding on each side, and two columns a
/// playbook can run between.
/// </summary>
/// <param name="ProposalsA">
/// Every pending proposal a caller scoped to project A reads. Deciding
/// proposals takes the exact pending set, so a probe naming only one would be
/// refused for the set and not for the project.
/// </param>
internal sealed record WalledIds(
    int PlaybookA, int PlaybookB, int[] ProposalsA, int[] ProposalsB, long TrunkBuildA, long TrunkBuildB,
    long FindingA, long FindingB, int Todo, int Done, int ChatWorkspaceId, int ChatRoomId);

/// <summary>
/// The real host with the wall up, over a migrated scratch database that holds
/// the scope seed, three people and the two projects they are scoped against.
/// </summary>
/// <remarks>
/// Built the way <see cref="MinimalStartupTests"/> builds its host: two scratch
/// databases, the migrate step, the factory, and Quartz's log provider cleared
/// on the way in and on the way out. Nothing is mocked. A person acts through a
/// real grant and a key through a real secret, so the role gate and the scope
/// filter run exactly as they do for a browser and for a runner.
///
/// <para>The wall works over plain HTTP here: <c>Auth:EnforceInProcess</c>
/// defaults to true, the https redirect is only added when configured, and
/// <see cref="AuthCookie.ReadAll"/> matches the cookie by name alone, so a
/// hand-set <c>Cookie</c> header on the test server is read.</para>
/// </remarks>
internal sealed class WalledHost : IAsyncDisposable
{
    public const string HiddenMarker = "hidden-marker";
    public const string VisibleMarker = "visible-marker";

    private readonly string hatch;
    private readonly string quartz;
    private readonly MinimalFactory factory;
    private readonly Deadline deadline;
    private readonly List<HttpClient> clients = [];

    private WalledHost(
        string hatch, string quartz, MinimalFactory factory, Deadline deadline, ScopeSeed seed, WalledIds ids,
        WalledPerson none, WalledPerson some, WalledPerson all)
    {
        this.hatch = hatch;
        this.quartz = quartz;
        this.factory = factory;
        this.deadline = deadline;
        Seed = seed;
        Ids = ids;
        None = none;
        Some = some;
        All = all;
    }

    public ScopeSeed Seed { get; }

    public WalledIds Ids { get; }

    /// <summary>A user whose scope is <c>none</c>.</summary>
    public WalledPerson None { get; }

    /// <summary>An admin who owns project A, scoped to project A alone.</summary>
    public WalledPerson Some { get; }

    /// <summary>A user whose scope is <c>all</c>.</summary>
    public WalledPerson All { get; }

    public static async Task<WalledHost> StartAsync(Deadline deadline, CapturedLogs logs)
    {
        var (hatch, quartz) = await deadline.BoundAsync("creating the scratch databases", MinimalDatabases.CreateAsync);

        // Quartz keeps its logging provider in a static bound to whichever host
        // built a scheduler first; see MinimalStartupTests for why it is cleared
        // on the way in and again on the way out.
        QuartzLogProvider.SetCurrentLogProvider(null);

        MinimalFactory? factory = null;
        try
        {
            await deadline.BoundAsync("migrating", () => MinimalDatabases.MigrateAsync(hatch));

            var (seed, ids, none, some, all) = await deadline.BoundAsync("seeding", () => SeedAsync(hatch));

            factory = new MinimalFactory(hatch, quartz, logs, walled: true);
            return new WalledHost(hatch, quartz, factory, deadline, seed, ids, none, some, all);
        }
        catch
        {
            if (factory is not null) await factory.DisposeAsync();
            QuartzLogProvider.SetCurrentLogProvider(null);
            await MinimalDatabases.DropAsync(hatch, quartz);
            throw;
        }
    }

    /// <summary>
    /// The seed through an unscoped context, then the people. B's issue, comment
    /// and dream carry <see cref="HiddenMarker"/> and A's carry
    /// <see cref="VisibleMarker"/>, so a body that holds one says whose text it
    /// is.
    /// </summary>
    private static async Task<(ScopeSeed, WalledIds, WalledPerson, WalledPerson, WalledPerson)> SeedAsync(string hatch)
    {
        var options = new DbContextOptionsBuilder<HatchContext>().UseNpgsql(hatch).Options;
        await using var db = new HatchContext(options);

        var seed = await ScopeSeed.SeedAsync(db);

        foreach (var (issueId, dreamId, marker) in new[]
        {
            (seed.IssueA, seed.DreamA, VisibleMarker),
            (seed.IssueB, seed.DreamB, HiddenMarker),
        })
        {
            var issue = await db.Issues.SingleAsync(i => i.Id == issueId);
            issue.Title = $"{issue.Title} {marker}";
            issue.Description = $"{marker} description";

            var comment = await db.Comments.FirstAsync(c => c.IssueId == issueId);
            comment.Body = $"{marker} comment";

            var dream = await db.Dreams.SingleAsync(d => d.Id == dreamId);
            dream.Body = $"{marker} dream";
        }

        await db.SaveChangesAsync();

        var todo = await db.Statuses.SingleAsync(s => s.Name == "todo");
        var done = await db.Statuses.SingleAsync(s => s.Name == "done");
        var playbooks = await db.Playbooks.AsNoTracking().Where(p => p.FromStatusId == todo.Id).ToListAsync();
        var playbookA = playbooks.Single(p => p.ProjectId == seed.ProjectA);
        var playbookB = playbooks.Single(p => p.ProjectId == seed.ProjectB);
        var playbookGlobal = playbooks.Single(p => p.ProjectId is null);

        var proposals = await db.PlaybookProposals.AsNoTracking().Include(p => p.Changes).ToListAsync();
        var proposalsA = proposals
            .Where(p => p.IssueId == seed.IssueA
                || (p.IssueId is null && p.Changes.Any(c => c.PlaybookId == playbookA.Id || c.PlaybookId == playbookGlobal.Id)))
            .Select(p => p.Id).Order().ToArray();
        var proposalsB = proposals.Where(p => p.IssueId == seed.IssueB).Select(p => p.Id).Order().ToArray();

        var trunkBuilds = await db.TrunkBuilds.AsNoTracking().ToListAsync();
        var findings = await db.GooseFindings.AsNoTracking().ToListAsync();

        // A workspace bound to both A and B, with one room chosen to both -
        // what ScopeRouteMatrixTests probes the chat project-naming routes
        // with. The context here reads and writes unbounded, as the rest of
        // this seed does.
        var chatNow = DateTimeOffset.UtcNow;
        var chatWorkspace = new EfHatchChatWorkspace
        {
            Provider = "fake", ExternalId = "T-MATRIX", Name = "matrix", Token = SecretObfuscator.Obfuscate("xoxb-matrix-seed"),
            CreatedAt = chatNow, UpdatedAt = chatNow,
        };
        db.ChatWorkspaces.Add(chatWorkspace);
        await db.SaveChangesAsync();

        db.ChatWorkspaceProjects.AddRange(
            new EfHatchChatWorkspaceProject { WorkspaceId = chatWorkspace.Id, ProjectId = seed.ProjectA, CreatedAt = chatNow },
            new EfHatchChatWorkspaceProject { WorkspaceId = chatWorkspace.Id, ProjectId = seed.ProjectB, CreatedAt = chatNow });

        var chatRoom = new EfHatchChatRoom
        {
            WorkspaceId = chatWorkspace.Id, ExternalId = "C-MATRIX", Name = "matrix-room", AllProjects = false,
            CreatedAt = chatNow, UpdatedAt = chatNow,
        };
        db.ChatRooms.Add(chatRoom);
        await db.SaveChangesAsync();

        db.ChatRoomProjects.AddRange(
            new EfHatchChatRoomProject { RoomId = chatRoom.Id, WorkspaceId = chatWorkspace.Id, ProjectId = seed.ProjectA },
            new EfHatchChatRoomProject { RoomId = chatRoom.Id, WorkspaceId = chatWorkspace.Id, ProjectId = seed.ProjectB });
        await db.SaveChangesAsync();

        var ids = new WalledIds(
            playbookA.Id, playbookB.Id, proposalsA, proposalsB,
            trunkBuilds.Single(t => t.Canonical == ScopeSeed.CanonicalA).Id,
            trunkBuilds.Single(t => t.Canonical == ScopeSeed.CanonicalB).Id,
            findings.Single(f => f.ProjectId == seed.ProjectA).Id,
            findings.Single(f => f.ProjectId == seed.ProjectB).Id,
            todo.Id, done.Id, chatWorkspace.Id, chatRoom.Id);

        // People straight in the database. A grant and a key are made later,
        // through the services, once the host is up.
        var now = DateTimeOffset.UtcNow;
        await using var app = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(hatch).Options);
        var none = new EfPerson { Name = "scope none", Role = PersonRole.User, CreatedAt = now, UpdatedAt = now };
        var some = new EfPerson { Name = "scope some", Role = PersonRole.Admin, CreatedAt = now, UpdatedAt = now };
        var all = new EfPerson { Name = "scope all", Role = PersonRole.User, CreatedAt = now, UpdatedAt = now };
        app.People.AddRange(none, some, all);
        await app.SaveChangesAsync();

        db.PersonScopes.AddRange(
            new EfHatchPersonScope { PersonId = none.Id, Mode = PersonScopeMode.None, UpdatedBy = "test", UpdatedAt = now },
            new EfHatchPersonScope { PersonId = some.Id, Mode = PersonScopeMode.Some, UpdatedBy = "test", UpdatedAt = now },
            new EfHatchPersonScope { PersonId = all.Id, Mode = PersonScopeMode.All, UpdatedBy = "test", UpdatedAt = now });
        db.PersonScopeProjects.Add(new EfHatchPersonScopeProject { PersonId = some.Id, ProjectId = seed.ProjectA });

        // An owner of A, so a write the scope allows is not refused for role.
        db.ProjectMembers.Add(new EfHatchProjectMember
        {
            ProjectId = seed.ProjectA, PersonId = some.Id, Role = ProjectMemberRole.Owner, CreatedAt = now,
        });
        await db.SaveChangesAsync();

        return (
            seed, ids,
            new WalledPerson(none.Id, none.Name, PersonScopeMode.None, none.Role),
            new WalledPerson(some.Id, some.Name, PersonScopeMode.Some, some.Role),
            new WalledPerson(all.Id, all.Name, PersonScopeMode.All, all.Role));
    }

    /// <summary>
    /// A client that sends the person's grant as the cookie the wall reads. It
    /// also says it is a browser (<c>Sec-Fetch-Mode</c>), which the playbook
    /// writes ask of a person: a person who did not would be refused for that
    /// and not for the project.
    /// </summary>
    public async Task<HttpClient> AsPersonAsync(WalledPerson person)
    {
        var client = await NewClientAsync();

        var token = await deadline.BoundAsync($"redeeming a grant for {person.Name}", async () =>
        {
            using var scope = factory.Services.CreateScope();
            var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();

            var invite = await auth.CreateInviteAsync(person.Name, person.Id, isBootstrap: false, CancellationToken.None);
            var redeemed = await auth.RedeemAsync(invite.Code, person.Name, "walled-host-test", null, CancellationToken.None);
            Assert.True(redeemed.Succeeded, $"redeeming a grant for {person.Name}: {redeemed.Error}");

            return redeemed.Token!;
        });

        var cookie = factory.Services.GetRequiredService<IOptions<AuthOptions>>().Value.CookieName;
        client.DefaultRequestHeaders.Add("Cookie", $"{cookie}={token}");
        client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "cors");
        return client;
    }

    /// <summary>Mints a key, owned by <paramref name="owner"/> or by nobody.</summary>
    public async Task<WalledKey> MintKeyAsync(string name, WalledPerson? owner)
    {
        await EnsureStartedAsync();

        return await deadline.BoundAsync($"minting key {name}", async () =>
        {
            using var scope = factory.Services.CreateScope();
            var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();

            var created = await auth.CreateApiKeyAsync(name, owner?.Id, CancellationToken.None);
            Assert.True(created is not null, $"key {name} was not minted: the name is taken");

            return new WalledKey(name, owner?.Id, created!.Secret);
        });
    }

    /// <summary>
    /// A person made straight in the database with no scope row, which reads as
    /// <c>all</c>. For a case about what happens to a person, and so to what
    /// they own, rather than about what they see.
    /// </summary>
    public async Task<WalledPerson> AddPersonAsync(string name, PersonRole role)
    {
        await EnsureStartedAsync();

        return await deadline.BoundAsync($"adding {name}", async () =>
        {
            var now = DateTimeOffset.UtcNow;
            await using var app = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(hatch).Options);
            var person = new EfPerson { Name = name, Role = role, CreatedAt = now, UpdatedAt = now };
            app.People.Add(person);
            await app.SaveChangesAsync();

            return new WalledPerson(person.Id, person.Name, PersonScopeMode.All, person.Role);
        });
    }

    /// <summary>A minted key's row as the database holds it now, read without tracking.</summary>
    public async Task<EfApiKey> KeyRowAsync(WalledKey key)
    {
        await EnsureStartedAsync();

        return await deadline.BoundAsync($"reading key {key.Name}", async () =>
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var hash = AuthTokens.Hash(key.Secret);
            return await db.ApiKeys.AsNoTracking().SingleAsync(k => k.Hash == hash);
        });
    }

    /// <summary>A client that sends the key as a bearer secret, as a runner does.</summary>
    public async Task<HttpClient> AsKeyAsync(WalledKey key) => await AsBearerAsync(key.Secret);

    /// <summary>A client that sends an arbitrary bearer secret - a key no row answers to, for instance.</summary>
    public async Task<HttpClient> AsBearerAsync(string secret)
    {
        var client = await NewClientAsync();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {secret}");
        return client;
    }

    /// <summary>Revokes a minted key, the way the API Keys page does, so a client still holding its secret is refused.</summary>
    public async Task RevokeAsync(WalledKey key)
    {
        await EnsureStartedAsync();

        await deadline.BoundAsync($"revoking key {key.Name}", async () =>
        {
            using var scope = factory.Services.CreateScope();
            var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var hash = AuthTokens.Hash(key.Secret);
            var row = await db.ApiKeys.AsNoTracking().SingleAsync(k => k.Hash == hash);
            Assert.True(await auth.RevokeApiKeyAsync(row.Id, CancellationToken.None));
        });
    }

    /// <summary>
    /// Builds and starts the host. Reading <c>Services</c> or creating a client
    /// blocks rather than awaiting, so Task.Run is what gives the deadline
    /// something to race - and nothing may touch <c>factory.Services</c> first.
    /// </summary>
    private Task EnsureStartedAsync() =>
        deadline.BoundAsync("starting the host", () => Task.Run(() => factory.Server));

    private async Task<HttpClient> NewClientAsync()
    {
        await EnsureStartedAsync();

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        clients.Add(client);
        return client;
    }

    /// <summary>
    /// On a budget of its own: the case's is likely spent by the time it is
    /// torn down, and a spent one would skip the drop and leave the pair on
    /// the server. A minute and a half, since each drop waits on a checkpoint
    /// and queues behind every other test's drops on one lock; thirty seconds
    /// was not enough on a slow disk.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var client in clients) client.Dispose();

        var teardown = new Deadline(TimeSpan.FromSeconds(90), () => "");
        try
        {
            await teardown.BoundAsync("stopping the host", () => factory.DisposeAsync().AsTask());
        }
        finally
        {
            QuartzLogProvider.SetCurrentLogProvider(null);
            await teardown.BoundAsync("dropping the scratch databases", () => MinimalDatabases.DropAsync(hatch, quartz));
        }
    }
}

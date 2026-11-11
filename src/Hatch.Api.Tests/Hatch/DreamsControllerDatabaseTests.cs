using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What a word matches and how it ranks among dreams - the half of
/// <see cref="DreamsController"/>'s read that lives in Postgres's full-text
/// machinery.
///
/// <para><b>These run against a real Postgres, and skip without one.</b> A
/// <c>SearchVector</c> is a generated column and <c>@@</c> is Postgres's own
/// operator; EF's in-memory provider has neither, so everything that does not
/// need them is in <see cref="DreamsControllerTests"/>. Point
/// <c>HATCH_TEST_DATABASE_URL</c> at a scratch database, or run
/// <c>make test-api-db</c>, which does it for you.</para>
///
/// <para>Unlike an issue search, a dream's query is an <b>any-word</b> match -
/// <see cref="Hatch.Api.Modules.Hatch.SearchQuery.ToTsQuery"/> called with
/// <c>anyWord: true</c> - so a dream missing every term is excluded outright
/// rather than merely ranked last.</para>
///
/// <para>The words used survive the <c>english</c> configuration unchanged and
/// are not stop words, so a bare word is a prefix that finds itself.</para>
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class DreamsControllerDatabaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    // ---- Ranking ----

    [SkippableFact]
    public async Task AFiveWordMatch_OutranksATwoWordMatch_AndAZeroWordMatchIsAbsentFromTheHitsEntirely()
    {
        await using var h = await NewAsync();

        var zero = await h.AddAsync("nothing related to any of this at all");
        var two = await h.AddAsync("alpha and bravo are mentioned here, nothing else");
        var five = await h.AddAsync("alpha bravo charlie delta echo, every single one of them");

        var result = await h.SearchAsync("alpha bravo charlie delta echo");

        Assert.Equal(2, result.Total);
        Assert.Equal([five.Id, two.Id], result.Dreams.Select(d => d.Id));
        Assert.DoesNotContain(zero.Id, result.Dreams.Select(d => d.Id));
    }

    // ---- Ties ----

    [SkippableTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("!!! ()")]
    public async Task TiesWithNoQOrAQThatReducesToNoTerms_OrderNewestFirst(string? q)
    {
        await using var h = await NewAsync();
        var oldest = await h.AddAsync("the oldest dream", Now);
        var middle = await h.AddAsync("the middle dream", Now.AddMinutes(1));
        var newest = await h.AddAsync("the newest dream", Now.AddMinutes(2));

        var result = await h.SearchAsync(q);

        Assert.Equal(3, result.Total);
        Assert.Equal([newest.Id, middle.Id, oldest.Id], result.Dreams.Select(d => d.Id));
    }

    // ---- Harness ----

    private static async Task<Harness> NewAsync()
    {
        Skip.IfNot(
            HatchDatabase.Available,
            "HATCH_TEST_DATABASE_URL is unset - a search vector is a Postgres generated column and a match is its " +
            "own operator, neither of which EF's in-memory provider has. Run `make test-api-db`, or point the " +
            "variable at a scratch database.");

        var connectionString = await HatchDatabase.PrepareAsync();

        return new Harness { ConnectionString = connectionString };
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly List<HatchContext> open = [];

        public required string ConnectionString { get; init; }

        /// <summary>A connection of its own, disposed with the harness.</summary>
        public HatchContext Connect()
        {
            var db = new HatchContext(
                new DbContextOptionsBuilder<HatchContext>().UseNpgsql(ConnectionString).Options);

            open.Add(db);
            return db;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var db in open) await db.DisposeAsync();
        }

        /// <summary>A dream, placed directly so the test chooses its own <see cref="EfHatchDream.CreatedAt"/>.</summary>
        public async Task<EfHatchDream> AddAsync(string body, DateTimeOffset? createdAt = null)
        {
            var db = Connect();
            var dream = new EfHatchDream
            {
                Body = body,
                Author = "operator",
                CreatedAt = createdAt ?? Now,
                UpdatedAt = createdAt ?? Now,
            };

            db.Dreams.Add(dream);
            await db.SaveChangesAsync();
            return dream;
        }

        /// <summary>
        /// A search through a context of its own, so nothing it returns was read
        /// through a tracker that already held the rows.
        /// </summary>
        public async Task<DreamsPageDto> SearchAsync(string? q)
        {
            var controller = new DreamsController(Connect(), new StubCallerIdentity(), new FakeTimeProvider(Now));

            return Value(await controller.GetDreams(q, null, 50, 0, default));
        }
    }

    private sealed class StubCallerIdentity : ICallerIdentity
    {
        public EfPerson? Person => null;

        public EfApiKey? Key => null;

        public Task<EfAuthGrant?> GrantAsync(CancellationToken ct) => Task.FromResult<EfAuthGrant?>(null);

        public Task<Guid?> PersonIdAsync(CancellationToken ct) => Task.FromResult<Guid?>(null);

        public Task<EfPerson?> PersonAsync(CancellationToken ct) => Task.FromResult<EfPerson?>(null);

        public Task<EfApiKey?> ApiKeyAsync(CancellationToken ct) => Task.FromResult<EfApiKey?>(null);

        public Actor? Local => null;

        public Task<Actor?> LocalAsync(CancellationToken ct) => Task.FromResult<Actor?>(null);

        public Task<bool> IsProgramAsync(CancellationToken ct) => Task.FromResult(false);

        public Task<string> ActorNameAsync(CancellationToken ct) => Task.FromResult(CallerIdentity.Unattributed);
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException("expected a value, got a refusal");
}

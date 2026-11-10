using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The judgement <see cref="RunnerSessions"/> makes: liveness on the same
/// terms as <see cref="RunnersTests"/> pins for a beat, the six bounds a
/// session answers to once a ceiling has had its say, and the two writes.
///
/// <para><b>The two writes run against a real Postgres, and skip without
/// one</b>, for the reason <see cref="MessageDeliveryTests"/> does: each is a
/// single conditional <c>UPDATE</c> or <c>DELETE</c>, and EF's in-memory
/// provider refuses both. Run <c>make test-api-db</c>.</para>
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class RunnerSessionsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    // ---- Liveness ----

    [Fact]
    public void AFreshSession_IsLive()
    {
        Assert.True(With().IsLive(Session(), Now));
    }

    [Fact]
    public void ASessionExactlyAtTheCutoff_IsLive()
    {
        Assert.True(With(60).IsLive(Session(lastSeenAt: Now.AddSeconds(-60)), Now));
    }

    [Fact]
    public void ASessionOneTickPastTheCutoff_IsNotLive()
    {
        Assert.False(With(60).IsLive(Session(lastSeenAt: Now.AddSeconds(-60).AddTicks(-1)), Now));
    }

    [Fact]
    public void AnEndedSession_IsNotLiveEvenFresh()
    {
        Assert.False(With().IsLive(Session(endedAt: Now), Now));
    }

    // ---- Effective bounds ----

    [Fact]
    public void TheSessionNarrower_Wins()
    {
        var bounds = RunnerSessions.Effective(Config(maxRuns: 10), Session(maxRuns: 5));

        Assert.Equal(5, bounds.MaxRuns.Value);
        Assert.Equal("session", bounds.MaxRuns.Source);
    }

    [Fact]
    public void TheCeilingNarrower_Wins()
    {
        var bounds = RunnerSessions.Effective(Config(maxRuns: 5), Session(maxRuns: 10));

        Assert.Equal(5, bounds.MaxRuns.Value);
        Assert.Equal("runner", bounds.MaxRuns.Source);
    }

    [Fact]
    public void OneSideAbsent_IsTheOtherSideUndisputed()
    {
        var sessionOnly = RunnerSessions.Effective(Config(maxRuns: null), Session(maxRuns: 10));
        Assert.Equal(10, sessionOnly.MaxRuns.Value);
        Assert.Equal("session", sessionOnly.MaxRuns.Source);

        var ceilingOnly = RunnerSessions.Effective(Config(maxRuns: 10), Session(maxRuns: null));
        Assert.Equal(10, ceilingOnly.MaxRuns.Value);
        Assert.Equal("runner", ceilingOnly.MaxRuns.Source);
    }

    [Fact]
    public void UnderOnlyOnTheSession_Wins()
    {
        var bounds = RunnerSessions.Effective(Config(under: null), Session(under: "HA-1"));

        Assert.Equal("HA-1", bounds.Under);
    }

    [Fact]
    public void UnderOnlyOnTheCeiling_Wins()
    {
        var bounds = RunnerSessions.Effective(Config(under: "HA-2"), Session(under: null));

        Assert.Equal("HA-2", bounds.Under);
    }

    [Fact]
    public void UnderOnNeither_IsNull()
    {
        var bounds = RunnerSessions.Effective(Config(under: null), Session(under: null));

        Assert.Null(bounds.Under);
    }

    [Fact]
    public void ATieBetweenSessionAndCeiling_ReportsSession()
    {
        var bounds = RunnerSessions.Effective(Config(maxRuns: 5), Session(maxRuns: 5));

        Assert.Equal(5, bounds.MaxRuns.Value);
        Assert.Equal("session", bounds.MaxRuns.Source);
    }

    [Fact]
    public void ATokenCapNeitherSideSets_FallsBackToTheSiteDefault()
    {
        var bounds = RunnerSessions.Effective(Config(maxTokens: null), Session(maxTokens: null), siteMaxTokens: 1_000);

        Assert.Equal(1_000, bounds.MaxTokens.Value);
        Assert.Equal("default", bounds.MaxTokens.Source);
    }

    [Fact]
    public void ATokenCapOnTheRunner_BeatsTheSiteDefault()
    {
        var bounds = RunnerSessions.Effective(Config(maxTokens: 500), Session(maxTokens: null), siteMaxTokens: 1_000);

        Assert.Equal(500, bounds.MaxTokens.Value);
        Assert.Equal("runner", bounds.MaxTokens.Source);
    }

    [Fact]
    public void UntilAt_TakesTheEarlierInstant()
    {
        var bounds = RunnerSessions.Effective(Config(untilAt: Now.AddHours(2)), Session(untilAt: Now.AddHours(1)));

        Assert.Equal(Now.AddHours(1), bounds.UntilAt.Value);
        Assert.Equal("session", bounds.UntilAt.Source);
    }

    // ---- Project ----

    [Fact]
    public void Project_CarriesTheEffectiveBoundsRatherThanTheSessionsOwn()
    {
        var dto = With().Project(Session(maxRuns: 10), Config(maxRuns: 5), null);

        Assert.Equal(5, dto.MaxRuns);
    }

    [Fact]
    public void Project_AClaimsChatterReplacesTheSessionsOwnLine()
    {
        var claim = (
            Key: "AER-1",
            Claim: new ClaimSnapshot(Guid.NewGuid(), "someone", "runner:here", Now, Now, "chatter from the claim", Now),
            Status: (string?)"in progress");

        var dto = With().Project(Session(line: "the session's own line"), null, claim);

        Assert.Equal("chatter from the claim", dto.Line);
        Assert.Equal("AER-1", dto.ClaimKey);
        Assert.Equal("in progress", dto.ClaimStatus);
    }

    [Fact]
    public void Project_WithNoClaim_KeepsTheSessionsOwnLine()
    {
        var dto = With().Project(Session(line: "the session's own line"), null, null);

        Assert.Equal("the session's own line", dto.Line);
        Assert.Null(dto.ClaimKey);
    }

    // ---- Instruct ----

    [Fact]
    public void Instruct_TheNarrowerConfigValue_Wins()
    {
        var dto = RunnerSessions.Instruct(Session(maxRuns: 10), Config(maxRuns: 5), "here:/checkouts/one", "Nathan", []);

        Assert.Equal(5, dto.MaxRuns);
    }

    [Fact]
    public void Instruct_WithNoConfig_TakesTheSessionsOwn()
    {
        var dto = RunnerSessions.Instruct(Session(maxRuns: 10), null, "here:/checkouts/one", "Nathan", []);

        Assert.Equal(10, dto.MaxRuns);
    }

    [Fact]
    public void Instruct_AnUnsetTokenCap_FallsBackToTheSiteDefault()
    {
        var dto = RunnerSessions.Instruct(
            Session(maxTokens: null), Config(maxTokens: null), "here:/checkouts/one", "Nathan", [], siteMaxTokens: 1_000);

        Assert.Equal(1_000, dto.MaxTokens);
    }

    [Fact]
    public void Instruct_TheConfigsTags_AreSortedOrdinally()
    {
        var config = Config();
        config.Tags = RunnerTagColumn.Write(["gpu", "fast"]);

        var dto = RunnerSessions.Instruct(Session(), config, "here:/checkouts/one", "Nathan", []);

        Assert.Equal(["fast", "gpu"], dto.Tags);
    }

    [Fact]
    public void Instruct_WithNoConfig_TagsAreEmpty()
    {
        var dto = RunnerSessions.Instruct(Session(), null, "here:/checkouts/one", "Nathan", []);

        Assert.Empty(dto.Tags!);
    }

    [Fact]
    public void Instruct_SetsTheRunnerNameAndTheSessionId()
    {
        var session = Session();
        session.Id = 42;

        var dto = RunnerSessions.Instruct(session, null, "here:/checkouts/one", "Nathan", []);

        Assert.Equal("here:/checkouts/one", dto.RunnerName);
        Assert.Equal(42, dto.SessionId);
    }

    // ---- MarkGoneAsync ----

    [SkippableFact]
    public async Task ASessionPastTheHorizon_ReadsNotLiveAndIsMarkedGoneOnceAndOnlyOnce()
    {
        var connectionString = await ConnectAsync();
        var keyId = Guid.NewGuid();

        await using var seed = Connect(connectionString);
        var id = await InsertAsync(seed, keyId, lastSeenAt: Now.AddMinutes(-5));

        await using var read = Connect(connectionString);
        var row = await read.RunnerSessions.SingleAsync(s => s.Id == id);
        Assert.False(With().IsLive(row, Now));

        await using var first = Connect(connectionString);
        Assert.Equal(1, await With().MarkGoneAsync(first, keyId, Now, default));

        await using var afterFirst = Connect(connectionString);
        Assert.Equal(Now, (await afterFirst.RunnerSessions.SingleAsync(s => s.Id == id)).EndedAt);

        await using var second = Connect(connectionString);
        Assert.Equal(0, await With().MarkGoneAsync(second, keyId, Now.AddMinutes(1), default));

        await using var afterSecond = Connect(connectionString);
        Assert.Equal(Now, (await afterSecond.RunnerSessions.SingleAsync(s => s.Id == id)).EndedAt);
    }

    [SkippableFact]
    public async Task MarkGoneAsync_IgnoresAFreshSessionAnAlreadyEndedOneAndAnotherKeysStaleOne()
    {
        var connectionString = await ConnectAsync();
        var keyId = Guid.NewGuid();
        var otherKeyId = Guid.NewGuid();

        await using var seed = Connect(connectionString);
        var freshId = await InsertAsync(seed, keyId, lastSeenAt: Now);
        var alreadyEndedId = await InsertAsync(seed, keyId, lastSeenAt: Now.AddMinutes(-5), endedAt: Now.AddMinutes(-1));
        var otherKeyStaleId = await InsertAsync(seed, otherKeyId, lastSeenAt: Now.AddMinutes(-5));

        await using var db = Connect(connectionString);
        Assert.Equal(0, await With().MarkGoneAsync(db, keyId, Now, default));

        await using var check = Connect(connectionString);
        Assert.Null((await check.RunnerSessions.SingleAsync(s => s.Id == freshId)).EndedAt);
        Assert.Equal(Now.AddMinutes(-1), (await check.RunnerSessions.SingleAsync(s => s.Id == alreadyEndedId)).EndedAt);
        Assert.Null((await check.RunnerSessions.SingleAsync(s => s.Id == otherKeyStaleId)).EndedAt);
    }

    [SkippableFact]
    public async Task MarkAllGoneAsync_EndsEveryPastHorizonSessionAcrossEveryKey()
    {
        var connectionString = await ConnectAsync();
        var keyId = Guid.NewGuid();
        var otherKeyId = Guid.NewGuid();

        await using var seed = Connect(connectionString);
        var staleId = await InsertAsync(seed, keyId, lastSeenAt: Now.AddMinutes(-5));
        var otherStaleId = await InsertAsync(seed, otherKeyId, lastSeenAt: Now.AddMinutes(-5));
        var freshId = await InsertAsync(seed, keyId, lastSeenAt: Now);

        await using var db = Connect(connectionString);
        Assert.Equal(2, await With().MarkAllGoneAsync(db, Now, default));

        await using var check = Connect(connectionString);
        Assert.Equal(Now, (await check.RunnerSessions.SingleAsync(s => s.Id == staleId)).EndedAt);
        Assert.Equal(Now, (await check.RunnerSessions.SingleAsync(s => s.Id == otherStaleId)).EndedAt);
        Assert.Null((await check.RunnerSessions.SingleAsync(s => s.Id == freshId)).EndedAt);
    }

    // ---- TrimHistoryAsync ----

    [SkippableFact]
    public async Task Trim_Keeps20AndDeletesOnlyTheOldestByEndedAt()
    {
        var connectionString = await ConnectAsync();
        var keyId = Guid.NewGuid();
        var otherKeyId = Guid.NewGuid();

        await using var seed = Connect(connectionString);
        var endedIds = new List<int>();
        for (var i = 0; i < 21; i++) endedIds.Add(await InsertAsync(seed, keyId, lastSeenAt: Now, endedAt: Now.AddMinutes(i)));

        var unendedId = await InsertAsync(seed, keyId, lastSeenAt: Now);
        var otherKeyEndedId = await InsertAsync(seed, otherKeyId, lastSeenAt: Now, endedAt: Now);

        await using var db = Connect(connectionString);
        Assert.Equal(1, await With().TrimHistoryAsync(db, keyId, default));

        await using var check = Connect(connectionString);
        var remaining = await check.RunnerSessions.Where(s => s.ApiKeyId == keyId && s.EndedAt != null)
            .Select(s => s.Id).ToListAsync();

        Assert.Equal(20, remaining.Count);
        Assert.DoesNotContain(endedIds[0], remaining);
        Assert.True(await check.RunnerSessions.AnyAsync(s => s.Id == unendedId));
        Assert.True(await check.RunnerSessions.AnyAsync(s => s.Id == otherKeyEndedId));
    }

    [SkippableFact]
    public async Task Trim_AnEqualEndedAtTiePicksTheLowerIdToDeleteFirst()
    {
        var connectionString = await ConnectAsync();
        var keyId = Guid.NewGuid();

        await using var seed = Connect(connectionString);
        // 19 rows newer than the tie, so the tie pair sits exactly at the
        // boundary the depth of 20 draws - the one place the tiebreak shows.
        for (var i = 0; i < 19; i++) await InsertAsync(seed, keyId, lastSeenAt: Now, endedAt: Now.AddMinutes(i + 1));
        var lowerId = await InsertAsync(seed, keyId, lastSeenAt: Now, endedAt: Now);
        var higherId = await InsertAsync(seed, keyId, lastSeenAt: Now, endedAt: Now);

        await using var db = Connect(connectionString);
        Assert.Equal(1, await With().TrimHistoryAsync(db, keyId, default));

        await using var check = Connect(connectionString);
        Assert.False(await check.RunnerSessions.AnyAsync(s => s.Id == lowerId));
        Assert.True(await check.RunnerSessions.AnyAsync(s => s.Id == higherId));
    }

    // ---- Helpers ----

    private static RunnerSessions With(int goneAfterSeconds = 90) =>
        new(new Runners(Options.Create(new HatchOptions { RunnerGoneAfterSeconds = goneAfterSeconds }), Options.Create(new AuthOptions()), new StubHatchRevision()));

    private static EfHatchRunnerSession Session(
        DateTimeOffset? lastSeenAt = null, DateTimeOffset? endedAt = null, string? line = null,
        string? under = null, int? maxRuns = null, decimal? maxSpend = null, long? maxTokens = null,
        long? maxTokensPerHour = null, DateTimeOffset? untilAt = null) => new()
    {
        ApiKeyId = Guid.NewGuid(),
        Kind = EfHatchRunner.LoopKind,
        State = EfHatchRunner.Running,
        StartedAt = Now,
        LastSeenAt = lastSeenAt ?? Now,
        EndedAt = endedAt,
        Line = line,
        Under = under,
        MaxRuns = maxRuns,
        MaxSpend = maxSpend,
        MaxTokens = maxTokens,
        MaxTokensPerHour = maxTokensPerHour,
        UntilAt = untilAt,
    };

    private static EfHatchRunnerConfig Config(
        string? under = null, int? maxRuns = null, decimal? maxSpend = null, long? maxTokens = null,
        long? maxTokensPerHour = null, DateTimeOffset? untilAt = null) => new()
    {
        ApiKeyId = Guid.NewGuid(),
        Under = under,
        MaxRuns = maxRuns,
        MaxSpend = maxSpend,
        MaxTokens = maxTokens,
        MaxTokensPerHour = maxTokensPerHour,
        UntilAt = untilAt,
        UpdatedAt = Now,
    };

    private static async Task<string> ConnectAsync()
    {
        Skip.IfNot(
            HatchDatabase.Available,
            "HATCH_TEST_DATABASE_URL is unset - MarkGoneAsync and TrimHistoryAsync are a conditional UPDATE and " +
            "DELETE, which EF's in-memory provider cannot execute. Run `make test-api-db`, or point the variable " +
            "at a scratch database.");

        return await HatchDatabase.PrepareAsync();
    }

    private static HatchContext Connect(string connectionString) =>
        new(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

    private static async Task<int> InsertAsync(HatchContext db, Guid apiKeyId, DateTimeOffset lastSeenAt, DateTimeOffset? endedAt = null)
    {
        var session = new EfHatchRunnerSession
        {
            ApiKeyId = apiKeyId,
            Kind = EfHatchRunner.LoopKind,
            State = EfHatchRunner.Running,
            StartedAt = Now,
            LastSeenAt = lastSeenAt,
            EndedAt = endedAt,
        };

        db.RunnerSessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }
}

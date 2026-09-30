using System.Text.Json;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// My own reading: two people's runners never answer for each other, the
/// freshest whole reading wins where somebody has two, staleness is
/// arithmetic against <c>UsageReadAt</c> and nothing else, and nothing shaped
/// like the account it came from crosses the wire.
///
/// <para><b>These run against a real Postgres, and skip without one</b> - see
/// <see cref="HatchDatabase"/>. Nothing here is a conditional <c>UPDATE</c>;
/// the database is used for the same reason every other rewritten controller
/// test in this file uses it now, rather than a stub HTTP client standing in
/// for an account nothing reaches any more.</para>
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class UtilizationControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [SkippableFact]
    public async Task EachPersonsRead_AnswersOnlyTheirOwnRunners()
    {
        await using var h = await NewAsync();
        var nathan = h.Actors.AddPerson("Nathan");
        var alice = h.Actors.AddPerson("Alice");
        await h.SeedAsync("here:/checkouts/one", nathan.Id, Now, ("session", 20));
        await h.SeedAsync("here:/checkouts/two", alice.Id, Now, ("session", 80));

        h.Actors.Principal = nathan;
        Assert.Equal(20, Assert.Single(Value(await h.Utilization.Get(default)).Limits).Percent);

        h.Actors.Principal = alice;
        Assert.Equal(80, Assert.Single(Value(await h.Utilization.Get(default)).Limits).Percent);
    }

    [SkippableFact]
    public async Task TheFreshestOfTwoRunners_WinsWhole()
    {
        await using var h = await NewAsync();
        var nathan = h.Actors.AddPerson("Nathan");
        h.Actors.Principal = nathan;
        await h.SeedAsync("here:/checkouts/one", nathan.Id, Now.AddMinutes(-3), ("session", 10));
        await h.SeedAsync("here:/checkouts/two", nathan.Id, Now.AddMinutes(-1), ("weekly", 55));

        var reading = Value(await h.Utilization.Get(default));

        // The freshest row's own windows, not a merge of the two rows' windows -
        // the earlier row's "session" reading does not survive alongside it.
        var window = Assert.Single(reading.Limits);
        Assert.Equal("weekly", window.Window);
        Assert.Equal(55, window.Percent);
    }

    [SkippableFact]
    public async Task APersonWithNoRunner_Answers204()
    {
        await using var h = await NewAsync();
        h.Actors.Principal = h.Actors.AddPerson("Nathan");

        Assert.IsType<NoContentResult>((await h.Utilization.Get(default)).Result);
    }

    [SkippableFact]
    public async Task ACallerWithNoPrincipalAtAll_Answers204()
    {
        await using var h = await NewAsync();

        Assert.IsType<NoContentResult>((await h.Utilization.Get(default)).Result);
    }

    [SkippableFact]
    public async Task ARunnerWithNoOwner_IsNeverAnyonesReading()
    {
        await using var h = await NewAsync();
        await h.SeedAsync("here:/checkouts/one", forPersonId: null, Now, ("session", 99));
        h.Actors.Principal = h.Actors.AddPerson("Nathan");

        Assert.IsType<NoContentResult>((await h.Utilization.Get(default)).Result);
    }

    [SkippableFact]
    public async Task AReadingInsideTheFreshnessWindow_IsOk()
    {
        await using var h = await NewAsync();
        h.Actors.Principal = h.Actors.AddPerson("Nathan");
        await h.SeedAsync("here:/checkouts/one", h.Actors.Principal.Id, Now.AddMinutes(-4), ("session", 10));

        Assert.Equal(UtilizationStates.Ok, Value(await h.Utilization.Get(default)).State);
    }

    [SkippableFact]
    public async Task AReadingOutsideTheFreshnessWindow_IsStaleAndCarriesTheInstant()
    {
        await using var h = await NewAsync();
        h.Actors.Principal = h.Actors.AddPerson("Nathan");
        var readAt = Now.AddMinutes(-10);
        await h.SeedAsync("here:/checkouts/one", h.Actors.Principal.Id, readAt, ("session", 10));

        var reading = Value(await h.Utilization.Get(default));
        Assert.Equal(UtilizationStates.Stale, reading.State);
        Assert.Equal(readAt, reading.ReadAt);
    }

    [SkippableTheory]
    [InlineData(74, UtilizationTones.Normal)]
    [InlineData(75, UtilizationTones.Warn)]
    [InlineData(89, UtilizationTones.Warn)]
    [InlineData(90, UtilizationTones.Danger)]
    public async Task Tone_IsDecidedAtEachBoundary(int percent, string tone)
    {
        await using var h = await NewAsync();
        h.Actors.Principal = h.Actors.AddPerson("Nathan");
        await h.SeedAsync("here:/checkouts/one", h.Actors.Principal.Id, Now, ("session", percent));

        Assert.Equal(tone, Assert.Single(Value(await h.Utilization.Get(default)).Limits).Tone);
    }

    /// <summary>
    /// The guard this test protected before the account it read from went
    /// away: criterion 10 says nothing vendor-shaped reaches the browser, and
    /// this is what fails loudly the day something does.
    /// </summary>
    [SkippableFact]
    public async Task CarriesNoVendorFieldName()
    {
        await using var h = await NewAsync();
        h.Actors.Principal = h.Actors.AddPerson("Nathan");
        await h.SeedAsync("here:/checkouts/one", h.Actors.Principal.Id, Now, ("session", 42));

        var reading = Value(await h.Utilization.Get(default));
        var json = JsonSerializer.Serialize(reading, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        foreach (var theirs in new[]
                 {
                     "resets_at", "is_active", "extra_usage", "used_credits", "monthly_limit",
                     "spend_limit_reached", "severity", "kind", "group", "scope", "display_name",
                     "five_hour", "seven_day", "utilization", "limit_dollars", "amount_minor", "credits",
                 })
        {
            Assert.DoesNotContain(theirs, json, StringComparison.OrdinalIgnoreCase);
        }

        // And the shape it does carry, so this test fails loudly if the answer
        // is renamed rather than merely cleaned of somebody else's names.
        foreach (var ours in new[] { "\"state\"", "\"readAt\"", "\"limits\"", "\"window\"", "\"label\"", "\"tone\"", "\"resetsAt\"" })
        {
            Assert.Contains(ours, json);
        }
    }

    /// <summary>
    /// HA-173: a fourth window and a scoped one named by the account both
    /// reach the browser as sent, and the vendor guard still holds with them
    /// in the reading.
    /// </summary>
    [SkippableFact]
    public async Task AnExtraRowAndANamedScopedRow_AreAnsweredAsSent()
    {
        await using var h = await NewAsync();
        h.Actors.Principal = h.Actors.AddPerson("Nathan");
        await h.SeedAsync(
            "here:/checkouts/one", h.Actors.Principal.Id, Now,
            new RunnerUsageWindowDto("extra", "Extra usage", 12, null),
            new RunnerUsageWindowDto("weeklyModel", "Weekly (Opus 5)", 88, null));

        var reading = Value(await h.Utilization.Get(default));

        Assert.Equal(2, reading.Limits.Count);
        var extra = reading.Limits.Single(l => l.Window == "extra");
        Assert.Equal("Extra usage", extra.Label);
        Assert.Equal(12, extra.Percent);
        var scoped = reading.Limits.Single(l => l.Window == "weeklyModel");
        Assert.Equal("Weekly (Opus 5)", scoped.Label);
        Assert.Equal(88, scoped.Percent);

        var json = JsonSerializer.Serialize(reading, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        foreach (var theirs in new[]
                 {
                     "resets_at", "is_active", "extra_usage", "used_credits", "monthly_limit",
                     "spend_limit_reached", "severity", "kind", "group", "scope", "display_name",
                     "five_hour", "seven_day", "utilization", "limit_dollars", "amount_minor", "credits",
                 })
        {
            Assert.DoesNotContain(theirs, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException("expected a value, got no content");

    // ---- Harness ----

    private sealed class Harness : IAsyncDisposable
    {
        public required string ConnectionString { get; init; }
        public required StubActorDirectory Actors { get; init; }
        public required TimeProvider Time { get; init; }

        private readonly List<HatchContext> open = [];

        public UtilizationController Utilization => new(Connect(), Actors, Time);

        private HatchContext Connect()
        {
            var db = new HatchContext(
                new DbContextOptionsBuilder<HatchContext>().UseNpgsql(ConnectionString).Options);
            open.Add(db);
            return db;
        }

        public Task SeedAsync(string name, Guid? forPersonId, DateTimeOffset readAt, (string Window, int Percent) window) =>
            SeedAsync(name, forPersonId, readAt, new RunnerUsageWindowDto(window.Window, window.Window, window.Percent, null));

        /// <summary>
        /// Whatever windows a test hands it, kept verbatim - unlike the tuple
        /// overload above, which always reuses the window's own name as its
        /// label. What a named scoped row or an <c>extra</c> row needs, since
        /// neither can be said with that shorthand.
        /// </summary>
        public async Task SeedAsync(
            string name, Guid? forPersonId, DateTimeOffset readAt, params RunnerUsageWindowDto[] windows)
        {
            var db = Connect();
            db.Runners.Add(new EfHatchRunner
            {
                Name = name,
                Kind = EfHatchRunner.LoopKind,
                FirstSeenAt = readAt,
                LastSeenAt = readAt,
                State = EfHatchRunner.Running,
                ForPersonId = forPersonId,
                UsageReadAt = readAt,
                Usage = JsonSerializer.Serialize(windows),
            });
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var db in open) await db.DisposeAsync();
        }
    }

    private static async Task<Harness> NewAsync()
    {
        Skip.IfNot(
            HatchDatabase.Available,
            "HATCH_TEST_DATABASE_URL is unset. Run `make test-api-db`, or point the variable at a scratch database.");

        var connectionString = await HatchDatabase.PrepareAsync();

        return new Harness
        {
            ConnectionString = connectionString,
            Actors = new StubActorDirectory(),
            Time = new FakeTimeProvider(Now),
        };
    }
}

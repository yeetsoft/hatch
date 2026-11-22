using Hatch.Api.Ef;
using Hatch.Api.Models.Auth;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Api.Tests.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The record of a runner's state changes: that every path which can change a
/// state writes exactly the row it should, that a beat which changes nothing
/// writes nothing, and that a state the clock ended is written at the instant
/// it ended - by the next call, with no job beside it.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class RunnerStateLogTests
{
    private const string Runner = "here:/checkouts/one";
    private const int Horizon = 60;
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    // ---- A heartbeat ----

    [Fact]
    public async Task ANewRunnersFirstBeat_WritesOneRowRunning_AndABeatThatChangesNothingWritesNone()
    {
        var h = await NewAsync();

        await h.BeatAsync(Runner);
        var first = Assert.Single(await h.RowsAsync(Runner));
        Assert.Equal((Now, "running", null), (first.At, first.State, first.Kind));

        h.Time.Advance(TimeSpan.FromSeconds(20));
        await h.BeatAsync(Runner);

        Assert.Single(await h.RowsAsync(Runner));
    }

    [Fact]
    public async Task ABeatThatReportsExhaustion_WritesOneRow()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner);

        h.Time.Advance(TimeSpan.FromSeconds(10));
        await h.BeatAsync(Runner, exhaustedUntil: Now.AddMinutes(5));

        var rows = await h.RowsAsync(Runner);
        Assert.Equal(["running", "exhausted"], rows.Select(r => r.State));
        Assert.Equal(Now.AddSeconds(10), rows[1].At);
    }

    [Fact]
    public async Task AnExhaustedThatRanOut_WritesItsSuccessorAtTheUntilInstant_NotAtTheNextBeat()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, exhaustedUntil: Now.AddSeconds(30));

        h.Time.Advance(TimeSpan.FromSeconds(45));
        await h.BeatAsync(Runner);

        var rows = await h.RowsAsync(Runner);
        Assert.Equal(["exhausted", "running"], rows.Select(r => r.State));
        Assert.Equal(Now, rows[0].At);
        Assert.Equal(Now.AddSeconds(30), rows[1].At);
    }

    [Fact]
    public async Task SilenceThenABeat_WritesGoneFromTheLimit_ThenTheStateFromTheBeat()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner);

        h.Time.Advance(TimeSpan.FromMinutes(5));
        await h.BeatAsync(Runner);

        var rows = await h.RowsAsync(Runner);
        Assert.Equal(["running", "gone", "running"], rows.Select(r => r.State));
        Assert.Equal(Now.AddSeconds(Horizon), rows[1].At);
        Assert.Equal(Now.AddMinutes(5), rows[2].At);
    }

    [Fact]
    public async Task ARunnerThatCameBackBeforeTheLimit_WritesNoGoneRow()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner);

        h.Time.Advance(TimeSpan.FromSeconds(Horizon - 1));
        await h.BeatAsync(Runner);

        Assert.Single(await h.RowsAsync(Runner));
    }

    [Fact]
    public async Task ARunnerRetiredByANewerBeat_ReadsGoneFromItsLastBeat()
    {
        var h = await NewAsync();
        await h.BeatAsync("here:/old", where: "here:/tree");

        h.Time.Advance(TimeSpan.FromMinutes(5));
        await h.BeatAsync("here:/new", where: "here:/tree");

        var old = await h.RowsAsync("here:/old");
        Assert.Equal(["running", "gone"], old.Select(r => r.State));
        Assert.Equal(Now, old[1].At);

        var fresh = Assert.Single(await h.RowsAsync("here:/new"));
        Assert.Equal("running", fresh.State);
    }

    // ---- The Runners controller's other writes ----

    [Fact]
    public async Task APatchOfTheState_WritesOneRow_AndAPatchOfABoundWritesNone()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner);

        h.Time.Advance(TimeSpan.FromSeconds(5));
        Assert.IsType<RunnerDto>(Value(await h.Runners.PatchRunner(Runner, new RunnerPatchRequest(MaxRuns: "5"), default)));
        Assert.Single(await h.RowsAsync(Runner));

        Value(await h.Runners.PatchRunner(Runner, new RunnerPatchRequest(State: "paused"), default));

        var rows = await h.RowsAsync(Runner);
        Assert.Equal(["running", "paused"], rows.Select(r => r.State));
        Assert.Equal(Now.AddSeconds(5), rows[1].At);
    }

    [Fact]
    public async Task APatchOfTheState_AfterSilence_SettlesBeforeItWrites()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner);

        h.Time.Advance(TimeSpan.FromMinutes(5));
        Value(await h.Runners.PatchRunner(Runner, new RunnerPatchRequest(State: "paused"), default));

        // Gone from the limit, then the beat row's own state: paused, on a row nobody has heard from.
        var rows = await h.RowsAsync(Runner);
        Assert.Equal(["running", "gone"], rows.Select(r => r.State));
        Assert.Equal(Now.AddSeconds(Horizon), rows[1].At);
    }

    [Fact]
    public async Task PatchingASession_WritesOneRow()
    {
        var h = await NewAsync();
        var key = await h.KeyAsync(Runner);
        var session = await h.SessionAsync(key.Id);
        await h.SeedRowAsync(Runner, "running");

        h.Time.Advance(TimeSpan.FromSeconds(5));
        await h.KeepAliveAsync(session);
        Value(await h.Runners.PatchRunnerSession(Runner, session.Id, new RunnerSessionPatchRequest("paused"), default));

        var rows = await h.RowsAsync(Runner);
        Assert.Equal(["running", "paused"], rows.Select(r => r.State));
        Assert.Equal(Now.AddSeconds(5), rows[1].At);
    }

    [Fact]
    public async Task EndingASession_WritesOneRow_Gone_AtOnce()
    {
        var h = await NewAsync();
        var key = await h.KeyAsync(Runner);
        var session = await h.SessionAsync(key.Id);
        await h.SeedRowAsync(Runner, "running");

        h.Time.Advance(TimeSpan.FromSeconds(5));
        await h.KeepAliveAsync(session);
        h.AsKey(key);
        Assert.IsType<NoContentResult>(await h.Runners.EndSession(session.Id, default));

        var rows = await h.RowsAsync(Runner);
        Assert.Equal(["running", "gone"], rows.Select(r => r.State));
        Assert.Equal(Now.AddSeconds(5), rows[1].At);

        // Ending it again changes nothing.
        Assert.IsType<NoContentResult>(await h.Runners.EndSession(session.Id, default));
        Assert.Equal(2, (await h.RowsAsync(Runner)).Count);
    }

    [Fact]
    public async Task LiftingABoardPause_WritesOneRow()
    {
        var h = await NewAsync();
        var key = await h.KeyAsync(Runner);
        var session = await h.SessionAsync(key.Id, where: Runner);
        session.AgentDownUntil = Now.AddMinutes(30);
        session.AgentDownKind = RunnerAgentDownKinds.BoardPaused;
        await h.Db.SaveChangesAsync();
        await h.SeedRowAsync(Runner, "agent-down", RunnerAgentDownKinds.BoardPaused);

        h.Time.Advance(TimeSpan.FromSeconds(5));
        await h.KeepAliveAsync(session);
        Assert.IsType<NoContentResult>(await h.Runners.DeleteRunnerSessionPause(Runner, session.Id, default));

        var rows = await h.RowsAsync(Runner);
        Assert.Equal(["agent-down", "running"], rows.Select(r => r.State));
        Assert.Equal(RunnerAgentDownKinds.BoardPaused, rows[0].Kind);
        Assert.Null(rows[1].Kind);
    }

    [Theory]
    [InlineData(true, "paused")]
    [InlineData(false, "stopping")]
    public async Task PausingOrStoppingEverySession_WritesOneRow_ForTheWholeKey(bool pause, string state)
    {
        var h = await NewAsync();
        var key = await h.KeyAsync(Runner);
        var one = await h.SessionAsync(key.Id, where: "here:/one");
        var two = await h.SessionAsync(key.Id, where: "here:/two");
        await h.SeedRowAsync(Runner, "running");

        h.Time.Advance(TimeSpan.FromSeconds(5));
        await h.KeepAliveAsync(one);
        await h.KeepAliveAsync(two);
        Assert.IsType<NoContentResult>(pause ? await h.Runners.PauseAll(Runner, default) : await h.Runners.StopAll(Runner, default));

        var rows = await h.RowsAsync(Runner);
        Assert.Equal(["running", state], rows.Select(r => r.State));
    }

    [Fact]
    public async Task ARunnerWithTwoSessionsInOneKey_IsOneRecord()
    {
        var h = await NewAsync();
        var key = await h.KeyAsync(Runner);
        var one = await h.SessionAsync(key.Id, where: "here:/one");
        var two = await h.SessionAsync(key.Id, where: "here:/two");
        await h.SeedRowAsync(Runner, "running");

        h.Time.Advance(TimeSpan.FromSeconds(5));
        await h.KeepAliveAsync(one);
        await h.KeepAliveAsync(two);

        // One session paused: the runner reads paused. The other too: still paused, no second row.
        Value(await h.Runners.PatchRunnerSession(Runner, one.Id, new RunnerSessionPatchRequest("paused"), default));
        Value(await h.Runners.PatchRunnerSession(Runner, two.Id, new RunnerSessionPatchRequest("paused"), default));
        Assert.Equal(["running", "paused"], (await h.RowsAsync(Runner)).Select(r => r.State));

        // Putting one back changes nothing - the other still holds it paused.
        Value(await h.Runners.PatchRunnerSession(Runner, one.Id, new RunnerSessionPatchRequest("running"), default));
        Assert.Equal(["running", "paused"], (await h.RowsAsync(Runner)).Select(r => r.State));

        Value(await h.Runners.PatchRunnerSession(Runner, two.Id, new RunnerSessionPatchRequest("running"), default));
        Assert.Equal(["running", "paused", "running"], (await h.RowsAsync(Runner)).Select(r => r.State));
    }

    [Fact]
    public async Task ARunnerWithNoHeartbeatAndNoSession_HasNoStateAndWritesNothing()
    {
        var h = await NewAsync();
        await h.KeyAsync(Runner);

        await new RunnerStateLog(h.Rule).RecordAsync(h.Db, h.Keys, Runner, Now, default);

        Assert.Empty(await h.Db.RunnerStates.ToListAsync());
    }

    [Fact]
    public async Task ARowIsNeverEarlierThanTheRowBeforeIt()
    {
        var h = await NewAsync();
        await h.SeedRowAsync(Runner, "paused", at: Now.AddMinutes(10));
        await h.BeatAsync(Runner);

        // The beat is older than the last row: its state differs, and it is written at that row's instant.
        var rows = await h.RowsAsync(Runner);
        Assert.Equal(["paused", "running"], rows.Select(r => r.State));
        Assert.All(rows, r => Assert.True(r.At >= Now.AddMinutes(10)));
    }

    // ---- With a database ----

    [SkippableFact]
    public async Task ASessionBeat_WritesOneRow_AndSilenceThenABeatWritesGoneFromTheLimit()
    {
        var h = await NewAsync(await ConnectAsync());
        var key = await h.KeyAsync(Runner);
        h.AsKey(key);

        Assert.IsType<RunnerInstructionDto>((await h.Runners.SessionHeartbeat(new RunnerHeartbeatRequest(Kind: "loop", Where: Runner), default)).Value);
        var first = Assert.Single(await h.RowsAsync(Runner));
        Assert.Equal("running", first.State);

        // A beat that changes nothing writes nothing.
        h.Time.Advance(TimeSpan.FromSeconds(20));
        await h.Runners.SessionHeartbeat(new RunnerHeartbeatRequest(Kind: "loop", Where: Runner), default);
        Assert.Single(await h.RowsAsync(Runner));

        h.Time.Advance(TimeSpan.FromMinutes(5));
        await h.Runners.SessionHeartbeat(new RunnerHeartbeatRequest(Kind: "loop", Where: Runner), default);

        var rows = await h.RowsAsync(Runner);
        Assert.Equal(["running", "gone", "running"], rows.Select(r => r.State));
        Assert.Equal(Now.AddSeconds(20 + Horizon), rows[1].At);
    }

    [SkippableFact]
    public async Task ASessionBeat_ThatRetiresARenamedRunner_ReadsItGoneFromItsLastBeat()
    {
        var h = await NewAsync(await ConnectAsync());
        var key = await h.KeyAsync(Runner);
        await h.BeatAsync("here:/old", where: Runner);
        h.AsKey(key);

        h.Time.Advance(TimeSpan.FromMinutes(5));
        await h.Runners.SessionHeartbeat(new RunnerHeartbeatRequest(Kind: "loop", Where: Runner), default);

        var old = await h.RowsAsync("here:/old");
        Assert.Equal(["running", "gone"], old.Select(r => r.State));
        Assert.Equal(Now, old[1].At);
    }

    // ---- Harness ----

    private static async Task<string> ConnectAsync()
    {
        Skip.IfNot(
            HatchDatabase.Available,
            "HATCH_TEST_DATABASE_URL is unset - the session heartbeat calls MarkGoneAsync and " +
            "TrimHistoryAsync, which EF's in-memory provider cannot execute. Run `make test-api-db`.");

        return await HatchDatabase.PrepareAsync();
    }

    private static T Value<T>(ActionResult<T> result) => result.Value ?? throw new InvalidOperationException("no value: " + result.Result);

    private static async Task<Harness> NewAsync(string? connectionString = null)
    {
        var options = connectionString is null
            ? new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options
            : new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options;
        var db = new HatchContext(options);
        db.Add(new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now });
        db.Add(new EfHatchStatus { Name = "inbox", SortOrder = 10 });
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var coreDb = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var keys = new AuthService(coreDb, Options.Create(new AuthOptions { Enabled = false }), time, NullLogger<AuthService>.Instance);
        var rule = new Runners(
            Options.Create(new HatchOptions { RunnerGoneAfterSeconds = Horizon }),
            Options.Create(new AuthOptions { Enabled = false }),
            new StubHatchRevision());
        var caller = new StubCallerIdentity
        {
            Person = new EfPerson { Id = Guid.NewGuid(), Name = "Nathan", Role = PersonRole.User, CreatedAt = Now, UpdatedAt = Now },
        };
        var actors = new StubActorDirectory();
        var claims = TestClaims.With();
        var sessions = new RunnerSessions(rule);

        var runners = new RunnersController(
            db, rule, claims, caller, actors, time, Options.Create(new AuthOptions { Enabled = false }), new StubSiteSettings(), keys,
            sessions, new Dispatch(db, actors, claims, time), new StubHatchRevision(), new StubRunnerBuilds(),
            new RunnerStateLog(rule), NullLogger<RunnersController>.Instance);

        return new Harness { Runners = runners, Db = db, Time = time, Keys = keys, Rule = rule, Caller = caller, CoreDb = coreDb };
    }

    private sealed class Harness
    {
        public required RunnersController Runners { get; init; }
        public required HatchContext Db { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required AuthService Keys { get; init; }
        public required global::Hatch.Api.Modules.Hatch.Runners Rule { get; init; }
        public required StubCallerIdentity Caller { get; init; }
        public required AppDbContext CoreDb { get; init; }

        public Task BeatAsync(string name, string? where = null, DateTimeOffset? exhaustedUntil = null) =>
            Runners.Heartbeat(
                name,
                new RunnerHeartbeatRequest(
                    Kind: "loop", Where: where, Exhausted: exhaustedUntil is null ? null : true, ExhaustedUntil: exhaustedUntil),
                default);

        public async Task<EfApiKey> KeyAsync(string name) =>
            (await Keys.CreateApiKeyAsync(name, null, default))!.Key;

        /// <summary>The caller becomes this key: a program, not a person.</summary>
        public void AsKey(EfApiKey key)
        {
            Caller.Person = null;
            Caller.ApiKey = key;
        }

        public async Task<EfHatchRunnerSession> SessionAsync(Guid keyId, string? where = null)
        {
            var session = new EfHatchRunnerSession
            {
                ApiKeyId = keyId, Kind = "loop", Where = where, State = "running", StartedAt = Now, LastSeenAt = Now,
            };
            Db.RunnerSessions.Add(session);
            await Db.SaveChangesAsync();

            // The control routes read as a person; the caller starts as one.
            return session;
        }

        /// <summary>The runner was heard from now - a beat nobody asked the log about.</summary>
        public async Task KeepAliveAsync(EfHatchRunnerSession session)
        {
            session.LastSeenAt = Time.GetUtcNow();
            await Db.SaveChangesAsync();
        }

        public async Task SeedRowAsync(string name, string state, string? kind = null, DateTimeOffset? at = null)
        {
            Db.RunnerStates.Add(new EfHatchRunnerState { Runner = name, At = at ?? Now, State = state, Kind = kind });
            await Db.SaveChangesAsync();
        }

        public async Task<List<EfHatchRunnerState>> RowsAsync(string name) =>
            await Db.RunnerStates.AsNoTracking().Where(s => s.Runner == name).OrderBy(s => s.At).ThenBy(s => s.Id).ToListAsync();
    }
}

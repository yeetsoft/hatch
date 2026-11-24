using Hatch.Api.Ef;
using Hatch.Api.Models.Auth;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Api.Tests.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The failure rows a loop session's heartbeat keeps: what opens one, what
/// refreshes it, what ends it, what leaves it alone, and that a runner gone
/// quiet reads no current failure while its rows stay as they were.
/// </summary>
/// <remarks>
/// Every case but the pure ones needs Postgres: a session beat calls
/// <c>MarkGoneAsync</c> and the unique index is the database's own.
/// </remarks>
[Collection(HatchDatabaseCollection.Name)]
public partial class RunnerFailuresTests
{
    private const string Name = "here:/checkouts/one";
    private const string Tree = "here:/tree";
    private const int Horizon = 60;
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    // ---- The forge ----

    [SkippableFact]
    public async Task AForgeThatFails_OpensOneRow_AndASecondBeatMovesItsLastReportedTimeAndReason()
    {
        var h = await NewAsync(await ConnectAsync());

        await h.BeatAsync(Forge("fails", "gh said: bad credentials"));
        var first = Assert.Single(await h.RowsAsync());
        Assert.Equal((RunnerFailureKinds.Forge, Tree, "gh said: bad credentials", Now, Now, (DateTimeOffset?)null),
            (first.Kind, first.Where, first.Why, first.BeganAt, first.LastReportedAt, first.EndedAt));

        h.Time.Advance(TimeSpan.FromSeconds(20));
        await h.BeatAsync(Forge("fails", "gh said: token expired"));

        var second = Assert.Single(await h.RowsAsync());
        Assert.Equal(first.Id, second.Id);
        Assert.Equal((Now, Now.AddSeconds(20), "gh said: token expired", (DateTimeOffset?)null),
            (second.BeganAt, second.LastReportedAt, second.Why, second.EndedAt));
    }

    [SkippableFact]
    public async Task AForgeThatWorks_EndsTheRowAtThatBeat()
    {
        var h = await NewAsync(await ConnectAsync());
        await h.BeatAsync(Forge("fails", "no"));

        h.Time.Advance(TimeSpan.FromSeconds(30));
        await h.BeatAsync(Forge("works"));

        var row = Assert.Single(await h.RowsAsync());
        Assert.Equal(Now.AddSeconds(30), row.EndedAt);
    }

    [SkippableFact]
    public async Task ABeatWithoutTheFact_LeavesItsRow_AndAOnceBeatLeavesEveryRow()
    {
        var h = await NewAsync(await ConnectAsync());
        await h.BeatAsync(Forge("fails", "no"));
        await h.BeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: Tree, Signing: "fails", SigningWhy: "no key"));
        var before = await h.RowsAsync();
        Assert.Equal(2, before.Count);

        h.Time.Advance(TimeSpan.FromSeconds(10));
        await h.BeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: Tree));

        // The beat said nothing of the forge: its row is the same row, untouched.
        var forge = (await h.RowsAsync()).Single(r => r.Kind == RunnerFailureKinds.Forge);
        Assert.Equal((Now, (DateTimeOffset?)null), (forge.LastReportedAt, forge.EndedAt));

        // A `once` beat sharing the checkout says "works" and touches nothing.
        h.Time.Advance(TimeSpan.FromSeconds(10));
        await h.BeatAsync(new RunnerHeartbeatRequest(Kind: "once", Where: Tree, Forge: "works", Signing: "off", AgentDown: false));

        Assert.All(await h.RowsAsync(), r => Assert.Null(r.EndedAt));
        Assert.Equal(before.Select(r => r.LastReportedAt), (await h.RowsAsync()).Select(r => r.LastReportedAt));
    }

    [SkippableFact]
    public async Task ASecondSessionAtTheSameCheckout_ContinuesTheOpenRow()
    {
        var h = await NewAsync(await ConnectAsync());
        await h.BeatAsync(Forge("fails", "no"));
        var first = Assert.Single(await h.RowsAsync());

        // The loop goes quiet past the horizon and a new one starts in the same checkout.
        h.Time.Advance(TimeSpan.FromMinutes(5));
        await h.BeatAsync(Forge("fails", "still no"));

        Assert.Equal(2, await h.Db.RunnerSessions.CountAsync());
        var row = Assert.Single(await h.RowsAsync());
        Assert.Equal((first.Id, Now, Now.AddMinutes(5), "still no"), (row.Id, row.BeganAt, row.LastReportedAt, row.Why));
    }

    [SkippableFact]
    public async Task TwoCheckoutsUnderOneKey_HoldTheirOwnRows_AndAnUnnamedOneIsEmptyAndNeverOpensTwice()
    {
        var h = await NewAsync(await ConnectAsync());

        await h.BeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: "here:/a", Forge: "fails", ForgeWhy: "a"));
        await h.BeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: "here:/b", Forge: "fails", ForgeWhy: "b"));
        await h.BeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Forge: "fails", ForgeWhy: "none"));
        await h.BeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Forge: "fails", ForgeWhy: "none again"));

        var rows = await h.RowsAsync();
        Assert.Equal(["here:/a", "here:/b", ""], rows.Select(r => r.Where));
        Assert.Equal(["a", "b", "none again"], rows.Select(r => r.Why));

        // One checkout mending does not end another's.
        await h.BeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: "here:/a", Forge: "works"));
        Assert.Equal(["here:/a"], (await h.RowsAsync()).Where(r => r.EndedAt is not null).Select(r => r.Where));
    }

    // ---- Signing ----

    [SkippableFact]
    public async Task SigningOff_OpensNothing_AndSigningThatFails_OpensSigning()
    {
        var h = await NewAsync(await ConnectAsync());

        await h.BeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: Tree, Signing: "off"));
        Assert.Empty(await h.RowsAsync());

        await h.BeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: Tree, Signing: "fails", SigningWhy: "gpg: no secret key\nsecond line"));

        var row = Assert.Single(await h.RowsAsync());
        Assert.Equal((RunnerFailureKinds.Signing, "gpg: no secret key"), (row.Kind, row.Why));

        await h.BeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: Tree, Signing: "off"));
        Assert.NotNull(Assert.Single(await h.RowsAsync()).EndedAt);
    }

    // ---- The agent ----

    [SkippableFact]
    public async Task SignedOutThenNotStarting_SwapsRows_AndAgentDownFalseEndsThem()
    {
        var h = await NewAsync(await ConnectAsync());

        await h.BeatAsync(Agent(true, "signed-out", "run claude login"));
        var open = Assert.Single(await h.RowsAsync());
        Assert.Equal((RunnerFailureKinds.AgentSignedOut, "run claude login"), (open.Kind, open.Why));

        h.Time.Advance(TimeSpan.FromSeconds(10));
        await h.BeatAsync(Agent(true, "not-starting", "exec failed"));

        var rows = await h.RowsAsync();
        Assert.Equal([RunnerFailureKinds.AgentSignedOut, RunnerFailureKinds.AgentNotStarting], rows.Select(r => r.Kind));
        Assert.Equal(Now.AddSeconds(10), rows[0].EndedAt);
        Assert.Null(rows[1].EndedAt);

        h.Time.Advance(TimeSpan.FromSeconds(10));
        await h.BeatAsync(Agent(false));

        var ended = await h.RowsAsync();
        Assert.Equal([Now.AddSeconds(10), Now.AddSeconds(20)], ended.Select(r => r.EndedAt!.Value));
    }

    [SkippableFact]
    public async Task AnAgentDownBeat_WithNoAgentDownFact_LeavesTheAgentRowsAlone()
    {
        var h = await NewAsync(await ConnectAsync());
        await h.BeatAsync(Agent(true, "signed-out", "x"));

        h.Time.Advance(TimeSpan.FromSeconds(10));
        await h.BeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: Tree));

        var row = Assert.Single(await h.RowsAsync());
        Assert.Equal((Now, (DateTimeOffset?)null), (row.LastReportedAt, row.EndedAt));
    }

    // ---- The board's own pause ----

    [SkippableFact]
    public async Task AStandingBoardPause_IgnoresAgentDownFalse_AndTheBeatAfterItPassesEndsTheRow()
    {
        var h = await NewAsync(await ConnectAsync());
        var key = await h.KeyAsync();
        await h.BeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: Tree));
        await h.PauseAsync(key, Now.AddMinutes(30));

        h.Time.Advance(TimeSpan.FromSeconds(10));
        await h.BeatAsync(Agent(false));

        // The session ignored the report, so the row is the pause's and still open.
        var row = Assert.Single(await h.RowsAsync());
        Assert.Equal((RunnerFailureKinds.BoardPaused, Now.AddSeconds(10), (DateTimeOffset?)null), (row.Kind, row.LastReportedAt, row.EndedAt));

        h.Time.Advance(TimeSpan.FromMinutes(31));
        await h.BeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: Tree));

        Assert.Equal(Now.AddSeconds(10 + 31 * 60), Assert.Single(await h.RowsAsync()).EndedAt);
    }

    [SkippableFact]
    public async Task TheBeatAfterTheLiftRoute_EndsTheBoardPausedRow()
    {
        var h = await NewAsync(await ConnectAsync());
        var key = await h.KeyAsync();
        await h.BeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: Tree));
        await h.PauseAsync(key, Now.AddMinutes(30));
        var session = await h.Db.RunnerSessions.AsNoTracking().SingleAsync();

        h.Time.Advance(TimeSpan.FromMinutes(1));
        h.AsPerson();
        Assert.IsType<NoContentResult>(await h.Runners.DeleteRunnerSessionPause(Name, session.Id, default));

        // The lift route does not touch the row: the next beat does.
        Assert.Null(Assert.Single(await h.RowsAsync()).EndedAt);

        h.AsKey(key);
        h.Time.Advance(TimeSpan.FromSeconds(5));
        await h.BeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: Tree));

        Assert.Equal(Now.AddSeconds(65), Assert.Single(await h.RowsAsync()).EndedAt);
    }

    [SkippableFact]
    public async Task ASessionSeededUnderAStandingPause_ContinuesTheBoardPausedRow()
    {
        var h = await NewAsync(await ConnectAsync());
        var key = await h.KeyAsync();
        await h.BeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: Tree));
        await h.PauseAsync(key, Now.AddMinutes(30));
        var opened = Assert.Single(await h.RowsAsync());

        // The loop restarts: the old session is ended, a new one inherits the pause.
        await h.Runners.EndSession((await h.Db.RunnerSessions.AsNoTracking().SingleAsync()).Id, default);
        h.Time.Advance(TimeSpan.FromSeconds(10));
        await h.BeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: Tree));

        var row = Assert.Single(await h.RowsAsync());
        Assert.Equal((opened.Id, Now, Now.AddSeconds(10), (DateTimeOffset?)null), (row.Id, row.BeganAt, row.LastReportedAt, row.EndedAt));
    }

    // ---- The database holds the rule ----

    [SkippableFact]
    public async Task ASecondOpenRowForOneKeyCheckoutAndKind_IsRefusedByTheDatabase()
    {
        var h = await NewAsync(await ConnectAsync());
        var key = await h.KeyAsync();

        h.Db.RunnerFailures.Add(Row(key.Id, Tree, RunnerFailureKinds.Forge));
        await h.Db.SaveChangesAsync();

        // Another checkout, another kind and an ended row are all fine.
        h.Db.RunnerFailures.Add(Row(key.Id, "here:/other", RunnerFailureKinds.Forge));
        h.Db.RunnerFailures.Add(Row(key.Id, Tree, RunnerFailureKinds.Signing));
        h.Db.RunnerFailures.Add(Row(key.Id, Tree, RunnerFailureKinds.Forge, ended: Now));
        await h.Db.SaveChangesAsync();

        h.Db.RunnerFailures.Add(Row(key.Id, Tree, RunnerFailureKinds.Forge));
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => h.Db.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task ABeatThatLosesTheRaceToOpenARow_SettlesAgainstTheWinnersRow()
    {
        var connection = await ConnectAsync();
        var h = await NewAsync(connection);
        var key = await h.KeyAsync();
        await h.BeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: Tree));

        // The winner commits from its own connection between this beat's read
        // of the open rows and its save, so the save meets the unique index.
        var raced = 0;
        var interceptor = new BeforeFirstSave(async () =>
        {
            raced++;
            await using var winner = h.Connect();
            winner.RunnerFailures.Add(Row(key.Id, Tree, RunnerFailureKinds.Forge));
            await winner.SaveChangesAsync();
        });
        await using var loser = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connection).AddInterceptors(interceptor).Options);
        var session = await loser.RunnerSessions.AsNoTracking().SingleAsync();

        await RunnerFailures.ReconcileAsync(
            loser, key.Id, key.Name, null, session, new RunnerHeartbeatRequest(Kind: "loop", Where: Tree, Forge: "fails", ForgeWhy: "later"),
            global::Hatch.Api.Tests.Chat.RunnerFailureMessagesTests.For(loser, h.Time), Now.AddSeconds(5), default);

        Assert.Equal(1, raced);
        var row = Assert.Single(await h.RowsAsync());
        Assert.Equal((Now, Now.AddSeconds(5), "later"), (row.BeganAt, row.LastReportedAt, row.Why));
    }

    /// <summary>Runs <paramref name="act"/> once, just before the first save goes to the database.</summary>
    private sealed class BeforeFirstSave(Func<Task> act) : SaveChangesInterceptor
    {
        private bool done;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!done)
            {
                done = true;
                await act();
            }

            return result;
        }
    }

    // ---- History ----

    [SkippableFact]
    public async Task TheFiftyFirstEndedRow_DeletesTheOldest()
    {
        var h = await NewAsync(await ConnectAsync());
        var key = await h.KeyAsync();
        var other = await h.KeyAsync("here:/checkouts/other");

        for (var i = 0; i < EfHatchRunnerFailure.History; i++)
            h.Db.RunnerFailures.Add(Row(key.Id, Tree, RunnerFailureKinds.Forge, ended: Now.AddMinutes(-100 + i)));
        // Another key's ended rows are not this key's to trim.
        h.Db.RunnerFailures.Add(Row(other.Id, Tree, RunnerFailureKinds.Forge, ended: Now.AddYears(-1)));
        await h.Db.SaveChangesAsync();

        await h.BeatAsync(Forge("fails", "no"));
        Assert.Equal(EfHatchRunnerFailure.History + 1, await h.Db.RunnerFailures.CountAsync(f => f.ApiKeyId == key.Id));

        h.Time.Advance(TimeSpan.FromSeconds(5));
        await h.BeatAsync(Forge("works"));

        var kept = await h.Db.RunnerFailures.AsNoTracking().Where(f => f.ApiKeyId == key.Id).OrderBy(f => f.EndedAt).ToListAsync();
        Assert.Equal(EfHatchRunnerFailure.History, kept.Count);
        Assert.Equal(Now.AddMinutes(-99), kept[0].EndedAt);
        Assert.Equal(Now.AddSeconds(5), kept[^1].EndedAt);
        Assert.Equal(1, await h.Db.RunnerFailures.CountAsync(f => f.ApiKeyId == other.Id));
    }

    // ---- A quiet runner ----

    [SkippableFact]
    public async Task AQuietRunner_ReadsNoCurrentRow_ItsRowsUntouched_AndItsNextBeatSettlesThem()
    {
        var h = await NewAsync(await ConnectAsync());
        await h.BeatAsync(Forge("fails", "no"));
        var key = h.Caller.ApiKey!;

        h.Time.Advance(TimeSpan.FromMinutes(5));

        var read = Assert.IsType<RunnerFailuresDto>((await h.Runners.GetRunnerFailures(Name, default)).Value);
        Assert.Empty(read.Current);
        Assert.Empty(read.Earlier);

        var runner = Assert.Single((await h.Runners.GetRunners(default)).Value!.ToList());
        Assert.Empty(runner.Failures);

        // Nothing swept it.
        var row = Assert.Single(await h.RowsAsync());
        Assert.Equal((Now, (DateTimeOffset?)null), (row.LastReportedAt, row.EndedAt));

        await h.BeatAsync(Forge("works"));

        Assert.Equal(Now.AddMinutes(5), Assert.Single(await h.RowsAsync()).EndedAt);
        var after = Assert.IsType<RunnerFailuresDto>((await h.Runners.GetRunnerFailures(Name, default)).Value);
        Assert.Empty(after.Current);
        Assert.Equal([RunnerFailureKinds.Forge], after.Earlier.Select(f => f.Kind));
        Assert.Equal(key.Id, row.ApiKeyId);
    }

    [SkippableFact]
    public async Task ARunnerBeingHeardFrom_CarriesItsCurrentRowsOnTheRunnerAndTheRoute()
    {
        var h = await NewAsync(await ConnectAsync());
        await h.BeatAsync(Forge("fails", "no"));
        h.Time.Advance(TimeSpan.FromSeconds(5));
        await h.BeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: Tree, Signing: "fails", SigningWhy: "no key"));

        var list = (await h.Runners.GetRunners(default)).Value!.ToList();
        var runner = Assert.Single(list);
        Assert.Equal([RunnerFailureKinds.Signing, RunnerFailureKinds.Forge], runner.Failures.Select(f => f.Kind));
        Assert.Equal((Tree, "no key", Now.AddSeconds(5)), (runner.Failures[0].Where, runner.Failures[0].Why, runner.Failures[0].BeganAt));

        var one = Assert.IsType<RunnerDto>((await h.Runners.GetRunner(Name, default)).Value);
        Assert.Equal(2, one.Failures.Count);

        var route = Assert.IsType<RunnerFailuresDto>((await h.Runners.GetRunnerFailures(Name, default)).Value);
        Assert.Equal(runner.Failures, route.Current);
    }

    // ---- The tickets a failure let go ----

    [SkippableFact]
    public async Task ARowCarriesTheKeysOfTheTicketsLinkedToIt_NewestFirst_OnCurrentAndEarlier()
    {
        var h = await NewAsync(await ConnectAsync());
        await h.BeatAsync(Forge("fails", "no"));
        await h.BeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: Tree, Signing: "fails", SigningWhy: "no key"));
        var project = await h.Db.Projects.SingleAsync();
        var status = await h.Db.Statuses.SingleAsync();

        var issues = new List<EfHatchIssue>();
        for (var number = 1; number <= RunnerFailures.MaxTickets + 3; number++)
            issues.Add(new EfHatchIssue
            {
                ProjectId = project.Id, Number = number, Type = "task", Title = "t", StatusId = status.Id, Rank = number,
                CreatedBy = "operator", CreatedAt = Now, UpdatedAt = Now,
            });
        h.Db.Issues.AddRange(issues);
        await h.Db.SaveChangesAsync();

        var forge = (await h.RowsAsync()).Single(r => r.Kind == RunnerFailureKinds.Forge);
        var signing = (await h.RowsAsync()).Single(r => r.Kind == RunnerFailureKinds.Signing);
        for (var i = 0; i < issues.Count; i++)
            h.Db.RunnerFailureIssues.Add(new EfHatchRunnerFailureIssue { FailureId = forge.Id, IssueId = issues[i].Id, At = Now.AddMinutes(i) });
        h.Db.RunnerFailureIssues.Add(new EfHatchRunnerFailureIssue { FailureId = signing.Id, IssueId = issues[0].Id, At = Now });
        await h.Db.SaveChangesAsync();

        var current = Assert.IsType<RunnerFailuresDto>((await h.Runners.GetRunnerFailures(Name, default)).Value).Current;
        Assert.Equal(["AER-1"], current.Single(f => f.Kind == RunnerFailureKinds.Signing).Tickets);
        var capped = current.Single(f => f.Kind == RunnerFailureKinds.Forge).Tickets;
        Assert.Equal(RunnerFailures.MaxTickets, capped.Count);
        Assert.Equal($"AER-{issues.Count}", capped[0]);

        var runner = Assert.Single((await h.Runners.GetRunners(default)).Value!.ToList());
        Assert.Equal(current.Select(f => f.Tickets.Count), runner.Failures.Select(f => f.Tickets.Count));

        h.Time.Advance(TimeSpan.FromSeconds(10));
        await h.BeatAsync(Forge("works"));

        var earlier = Assert.Single(Assert.IsType<RunnerFailuresDto>((await h.Runners.GetRunnerFailures(Name, default)).Value).Earlier);
        Assert.Equal(RunnerFailureKinds.Forge, earlier.Kind);
        Assert.Equal(RunnerFailures.MaxTickets, earlier.Tickets.Count);
        Assert.Equal($"AER-{issues.Count}", earlier.Tickets[0]);
    }

    [SkippableFact]
    public async Task ARowWithNoTickets_ReadsAnEmptyList()
    {
        var h = await NewAsync(await ConnectAsync());
        await h.BeatAsync(Forge("fails", "no"));

        var current = Assert.IsType<RunnerFailuresDto>((await h.Runners.GetRunnerFailures(Name, default)).Value).Current;
        Assert.Empty(Assert.Single(current).Tickets);
    }

    // ---- Harness ----

    private static RunnerHeartbeatRequest Forge(string word, string? why = null) =>
        new(Kind: "loop", Where: Tree, Forge: word, ForgeWhy: why);

    private static RunnerHeartbeatRequest Agent(bool down, string? kind = null, string? line = null) =>
        new(Kind: "loop", Where: Tree, AgentDown: down, AgentDownKind: kind, AgentDownLine: line,
            AgentDownUntil: down ? Now.AddHours(1) : null);

    private static EfHatchRunnerFailure Row(Guid keyId, string where, string kind, DateTimeOffset? ended = null) => new()
    {
        ApiKeyId = keyId, Where = where, Kind = kind, BeganAt = Now, LastReportedAt = Now, EndedAt = ended,
    };

    private static async Task<string> ConnectAsync()
    {
        Skip.IfNot(
            HatchDatabase.Available,
            "HATCH_TEST_DATABASE_URL is unset - the session heartbeat calls MarkGoneAsync and " +
            "TrimHistoryAsync, which EF's in-memory provider cannot execute, and the open-row index " +
            "is Postgres's own. Run `make test-api-db`.");

        return await HatchDatabase.PrepareAsync();
    }

    /// <param name="wall">Whether the wall stands, as the failure messages read it.</param>
    /// <param name="owner">The person the runner's key belongs to.</param>
    private static async Task<Harness> NewAsync(string connectionString, bool wall = false, Guid? owner = null)
    {
        var options = new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options;
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
        var messages = global::Hatch.Api.Tests.Chat.RunnerFailureMessagesTests.For(db, time, wall: wall, actors: actors);
        var claims = TestClaims.With();
        var sessions = new RunnerSessions(rule);

        var runners = new RunnersController(
            db, rule, claims, caller, actors, time, Options.Create(new AuthOptions { Enabled = false }), new StubSiteSettings(), keys,
            sessions, new Dispatch(db, actors, claims, time), new StubHatchRevision(), new StubRunnerBuilds(),
            new RunnerStateLog(rule), messages, NullLogger<RunnersController>.Instance);

        var harness = new Harness
        {
            Runners = runners, Db = db, Time = time, Keys = keys, Caller = caller, Options = options, Actors = actors, Messages = messages,
        };
        harness.AsKey(await harness.KeyAsync(owner: owner));
        return harness;
    }

    private sealed partial class Harness
    {
        public required RunnersController Runners { get; init; }
        public required HatchContext Db { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required AuthService Keys { get; init; }
        public required StubCallerIdentity Caller { get; init; }
        public required DbContextOptions<HatchContext> Options { get; init; }
        public required StubActorDirectory Actors { get; init; }
        public required RunnerFailureMessages Messages { get; init; }

        private EfPerson? person;

        /// <summary>A second connection to the same database, as a racing beat would use.</summary>
        public HatchContext Connect() => new(Options);

        /// <summary>The runner's key, minted on first ask and the same after.</summary>
        public async Task<EfApiKey> KeyAsync(string? name = null, Guid? owner = null)
        {
            name ??= Name;
            var existing = Caller.ApiKey is { } k && k.Name == name ? k : null;
            return existing ?? (await Keys.CreateApiKeyAsync(name, owner, default))!.Key;
        }

        public Task<ActionResult<RunnerInstructionDto>> BeatAsync(RunnerHeartbeatRequest request) =>
            Runners.SessionHeartbeat(request, default);

        /// <summary>The caller becomes this key: a program, not a person.</summary>
        public void AsKey(EfApiKey key)
        {
            person ??= Caller.Person;
            Caller.Person = null;
            Caller.ApiKey = key;
        }

        /// <summary>The caller becomes a person again, as the lift route needs.</summary>
        public void AsPerson()
        {
            Caller.Person = person;
            Caller.ApiKey = null;
        }

        /// <summary>
        /// Writes the board's pause the way a release does: on the live session, and
        /// the failure row beside it in the same save.
        /// </summary>
        public async Task PauseAsync(EfApiKey key, DateTimeOffset until)
        {
            var live = await Db.RunnerSessions.SingleAsync(s => s.ApiKeyId == key.Id && s.EndedAt == null);
            live.AgentDownUntil = until;
            live.AgentDownKind = RunnerAgentDownKinds.BoardPaused;
            live.AgentDownLine = "3 sessions in a row ended in error without spending a token";
            await RunnerFailures.OpenBoardPauseAsync(
                Db, key.Id, key.Name, key.OwnerPersonId, live.Where, live.AgentDownLine, Messages, Time.GetUtcNow(), default);
            await Db.SaveChangesAsync();
        }

        public async Task<List<EfHatchRunnerFailure>> RowsAsync() =>
            await Db.RunnerFailures.AsNoTracking().OrderBy(f => f.Id).ToListAsync();
    }
}

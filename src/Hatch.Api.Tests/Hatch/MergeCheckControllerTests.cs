using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What a runner may tell the board about a branch, what the board refuses to
/// keep, and the one rule the trail rests on: a verdict that repeats the stored
/// one writes nothing, and one that changes writes a line.
/// </summary>
public class MergeCheckControllerTests
{
    private const string Remote = "git@forge.example:acme/hatch.git";
    private const string Canonical = "forge.example/acme/hatch";

    private static readonly string TrunkSha = new('a', 40);
    private static readonly string BranchSha = new('b', 40);
    private static readonly string OtherSha = new('c', 40);

    // ---- Keeping one ----

    [Fact]
    public async Task ACleanVerdict_IsKeptAndComesBackOnTheIssue()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.PutAsync(issue.Key, Clean());

        var kept = Assert.Single((await h.ReadAsync(issue.Key)).MergeChecks!);
        Assert.Equal(MergeVerdicts.Clean, kept.Verdict);
        Assert.Equal(Canonical, kept.Canonical);
        Assert.Equal(Remote, kept.Remote);
        Assert.Equal("main", kept.Trunk);
        Assert.Equal(TrunkSha, kept.TrunkSha);
        Assert.Equal("ha-1-thing", kept.Branch);
        Assert.Equal(BranchSha, kept.BranchSha);
        Assert.Empty(kept.Files);
        Assert.Equal("runner-1", kept.Runner);
        Assert.Equal("Nathan", kept.CheckedBy);
    }

    [Fact]
    public async Task AnIssueNobodyHasChecked_CarriesNoVerdicts()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        Assert.Empty((await h.ReadAsync(issue.Key)).MergeChecks!);
    }

    [Fact]
    public async Task AConflictedVerdict_NamesItsFiles()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.PutAsync(issue.Key, Conflicted("a.txt", "dir/b.txt"));

        var kept = Assert.Single((await h.ReadAsync(issue.Key)).MergeChecks!);
        Assert.Equal(MergeVerdicts.Conflicted, kept.Verdict);
        Assert.Equal(["a.txt", "dir/b.txt"], kept.Files);
    }

    [Theory]
    [InlineData(MergeVerdicts.None)]
    [InlineData(MergeVerdicts.Ambiguous)]
    public async Task NoneAndAmbiguous_CarryNoBranchAndNoFiles(string verdict)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        // A runner that names a branch for a verdict that is not about one has
        // said nothing untrue, but the row must not mean it.
        await h.PutAsync(issue.Key, Clean() with { Verdict = verdict, Files = ["x"] });

        var kept = Assert.Single((await h.ReadAsync(issue.Key)).MergeChecks!);
        Assert.Equal(verdict, kept.Verdict);
        Assert.Null(kept.Branch);
        Assert.Null(kept.BranchSha);
        Assert.Empty(kept.Files);
    }

    [Fact]
    public async Task ASecondPutForTheSameRepository_ReplacesTheFirst()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.PutAsync(issue.Key, Clean());
        h.Time.Advance(TimeSpan.FromMinutes(5));
        await h.PutAsync(issue.Key, Clean() with { Remote = "https://forge.example/acme/hatch", TrunkSha = OtherSha });

        var kept = Assert.Single((await h.ReadAsync(issue.Key)).MergeChecks!);
        Assert.Equal(OtherSha, kept.TrunkSha);
        Assert.Equal(Now.AddMinutes(5), kept.CheckedAt);
        Assert.Equal(1, await h.Db.MergeChecks.CountAsync());
    }

    [Fact]
    public async Task TwoRepositoriesOnOneIssue_AreKeptApart()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.PutAsync(issue.Key, Clean());
        await h.PutAsync(issue.Key, Conflicted("x") with { Remote = "git@forge.example:acme/docs.git" });

        var kept = (await h.ReadAsync(issue.Key)).MergeChecks!;
        Assert.Equal(2, kept.Count);
        Assert.Equal(MergeVerdicts.Clean, kept.Single(k => k.Canonical == Canonical).Verdict);
        Assert.Equal(MergeVerdicts.Conflicted, kept.Single(k => k.Canonical == "forge.example/acme/docs").Verdict);
    }

    [Fact]
    public async Task AVerdictIsKeptForAnIssueInAnyColumn()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        // Taken at the end of an implementation increment, before anybody has
        // moved the ticket - so the column is not the route's business.
        Assert.IsType<MergeCheckDto>((await h.Check.PutMergeCheck(issue.Key, Clean(), default)).Value);
    }

    [Fact]
    public async Task ABatchOfIssues_ReadsItsVerdictsTogether()
    {
        var h = await NewAsync();
        var one = await h.FileAsync();
        var two = await h.FileAsync();
        await h.PutAsync(one.Key, Clean());
        await h.PutAsync(two.Key, Conflicted("y"));

        var rows = await IssueProjection.ToDtosAsync(
            h.Db, new StubActorDirectory(), await h.Db.Issues.ToListAsync(), TestClaims.With(), Now, default);

        Assert.Equal(MergeVerdicts.Clean, Assert.Single(rows.Values.Single(r => r.Key == one.Key).MergeChecks!).Verdict);
        Assert.Equal(MergeVerdicts.Conflicted, Assert.Single(rows.Values.Single(r => r.Key == two.Key).MergeChecks!).Verdict);
    }

    // ---- The trail ----

    [Fact]
    public async Task TheFirstVerdict_WritesAnEventWithNothingBefore()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.PutAsync(issue.Key, Conflicted("a.txt"));

        var e = Assert.Single(await h.EventsAsync(issue.Key));
        Assert.Equal("Nathan", e.Actor);
        Assert.Equal(Canonical, e.Payload!.Value.GetProperty("remote").GetString());
        Assert.Equal(JsonValueKind.Null, e.Payload!.Value.GetProperty("from").ValueKind);
        Assert.Equal(MergeVerdicts.Conflicted, e.Payload!.Value.GetProperty("to").GetProperty("verdict").GetString());
    }

    [Fact]
    public async Task AVerdictThatChanges_WritesAnEventWithBothSides()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue.Key, Conflicted("a.txt"));

        await h.PutAsync(issue.Key, Clean());

        var events = await h.EventsAsync(issue.Key);
        Assert.Equal(2, events.Count);
        var payload = events[1].Payload!.Value;
        Assert.Equal(MergeVerdicts.Conflicted, payload.GetProperty("from").GetProperty("verdict").GetString());
        Assert.Equal(MergeVerdicts.Clean, payload.GetProperty("to").GetProperty("verdict").GetString());
    }

    [Fact]
    public async Task DifferentFiles_AreAChange()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue.Key, Conflicted("a.txt"));

        await h.PutAsync(issue.Key, Conflicted("a.txt", "b.txt"));

        Assert.Equal(2, (await h.EventsAsync(issue.Key)).Count);
    }

    [Fact]
    public async Task AVerdictThatRepeatsTheStoredOne_WritesNoEvent_ButMovesTheShasTheTimeAndTheRunner()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue.Key, Clean());

        h.Time.Advance(TimeSpan.FromMinutes(10));
        await h.PutAsync(issue.Key, Clean() with { TrunkSha = OtherSha, BranchSha = OtherSha, Runner = "runner-2" });

        Assert.Single(await h.EventsAsync(issue.Key));
        var kept = Assert.Single((await h.ReadAsync(issue.Key)).MergeChecks!);
        Assert.Equal(OtherSha, kept.TrunkSha);
        Assert.Equal(OtherSha, kept.BranchSha);
        Assert.Equal("runner-2", kept.Runner);
        Assert.Equal(Now.AddMinutes(10), kept.CheckedAt);
    }

    [Fact]
    public async Task TheSameFilesInAnotherOrder_AreNotAChange()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue.Key, Conflicted("a.txt", "b.txt"));

        await h.PutAsync(issue.Key, Conflicted("b.txt", "a.txt"));

        Assert.Single(await h.EventsAsync(issue.Key));
    }

    // ---- What it refuses ----

    [Fact]
    public async Task AnIssueThatIsNotThere_IsNotFound()
    {
        var h = await NewAsync();

        Assert.IsType<NotFoundResult>((await h.Check.PutMergeCheck("AER-404", Clean(), default)).Result);
        Assert.IsType<NotFoundResult>((await h.Check.PutMergeCheck("nonsense", Clean(), default)).Result);
    }

    [Fact]
    public async Task ARemoteThatDoesNotCanonicalise_IsRefused() =>
        await RefusedAsync(Clean() with { Remote = "nonsense" }, "a remote is a URL");

    [Fact]
    public async Task AVerdictThatIsNotOneOfTheFour_IsRefused() =>
        await RefusedAsync(Clean() with { Verdict = "maybe" }, "a verdict is one of");

    [Fact]
    public async Task Conflicted_WithNoFiles_IsRefused() =>
        await RefusedAsync(Conflicted() with { Files = [] }, "names the files");

    [Fact]
    public async Task Conflicted_WithBlankFiles_IsRefused() =>
        await RefusedAsync(Conflicted() with { Files = ["  ", ""] }, "names the files");

    [Theory]
    [InlineData(MergeVerdicts.Clean)]
    [InlineData(MergeVerdicts.Conflicted)]
    public async Task ACleanOrConflictedVerdict_WithNoBranchSha_IsRefused(string verdict) =>
        await RefusedAsync(Clean() with { Verdict = verdict, BranchSha = null, Files = ["x"] }, "branch's full 40-character sha");

    [Fact]
    public async Task ABranchShaThatIsNotFull_IsRefused() =>
        await RefusedAsync(Clean() with { BranchSha = "abc1234" }, "branch's full 40-character sha");

    [Fact]
    public async Task ATrunkShaThatIsNotFull_IsRefused() =>
        await RefusedAsync(Clean() with { TrunkSha = "abc1234" }, "trunk's full 40-character sha");

    [Fact]
    public async Task ANoTrunk_IsRefused() =>
        await RefusedAsync(Clean() with { Trunk = " " }, "names the trunk");

    [Fact]
    public async Task ARunnerThatIsTooLong_IsRefused() =>
        await RefusedAsync(Clean() with { Runner = new string('r', ClaimRequest.MaxRunnerLength + 1) }, "a runner is at most");

    [Fact]
    public async Task ARefusedVerdict_WritesNothing()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.Check.PutMergeCheck(issue.Key, Conflicted() with { Files = [] }, default);

        Assert.Empty(await h.Db.MergeChecks.ToListAsync());
        Assert.Empty(await h.EventsAsync(issue.Key));
    }

    // ---- The gate ----

    [Fact]
    public void TheRoute_IsOpenToAHatchScopedKey()
    {
        var guard = typeof(IssueMergeCheckController)
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .Single();

        // A runner reports what git said. Nothing it writes here names a budget
        // or reserves work, so - unlike a playbook - a key may.
        Assert.Equal(ApiKeyScopes.Hatch, guard.AcceptScope);
    }

    // ---- Requests ----

    private static MergeCheckRequest Clean() =>
        new(Remote, "main", TrunkSha, MergeVerdicts.Clean, "ha-1-thing", BranchSha, null, "runner-1");

    private static MergeCheckRequest Conflicted(params string[] files) =>
        Clean() with { Verdict = MergeVerdicts.Conflicted, Files = files };

    private static async Task RefusedAsync(MergeCheckRequest request, string words)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var refused = Assert.IsType<BadRequestObjectResult>((await h.Check.PutMergeCheck(issue.Key, request, default)).Result);

        Assert.Contains(words, (string)refused.Value!);
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required IssueMergeCheckController Check { get; init; }
        public required IssuesController Issues { get; init; }
        public required IssueThreadController Thread { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }

        public async Task<IssueDto> FileAsync(string type = "task", string title = "a thing") =>
            Created(await Issues.CreateIssue(new IssueCreateRequest(ProjectId, type, title, null, null, null, null), default));

        public async Task<MergeCheckDto> PutAsync(string key, MergeCheckRequest request) =>
            Value(await Check.PutMergeCheck(key, request, default));

        public async Task<IssueDto> ReadAsync(string key) => Value(await Issues.GetIssue(key, default));

        /// <summary>The verdict's own events, oldest first.</summary>
        public async Task<IReadOnlyList<IssueEventDto>> EventsAsync(string key) =>
            Value(await Thread.GetEvents(key, default))
                .Where(e => e.Kind == EfHatchIssueEvent.MergeCheckChanged)
                .Reverse()
                .ToList();
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var hatch = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Add(hatch);
        db.Add(new EfHatchStatus { Name = "inbox", SortOrder = 10 });
        db.Add(new EfHatchStatus { Name = "done", SortOrder = 20, IsTerminal = true });
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var caller = new StubCallerIdentity { Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now } };
        var actors = new StubActorDirectory();

        return new Harness
        {
            Db = db,
            Check = new IssueMergeCheckController(db, caller, time),
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time),
            Thread = new IssueThreadController(db, caller, time),
            Time = time,
            ProjectId = hatch.Id,
        };
    }

    /// <summary>Whoever the test says is holding the phone. Their name is the audit actor.</summary>
    private sealed class StubCallerIdentity : ICallerIdentity
    {
        public EfPerson? Person { get; set; }

        public EfApiKey? Key { get; set; }

        public Task<EfAuthGrant?> GrantAsync(CancellationToken ct) => Task.FromResult<EfAuthGrant?>(null);

        public Task<Guid?> PersonIdAsync(CancellationToken ct) => Task.FromResult(Person?.Id);

        public Task<EfPerson?> PersonAsync(CancellationToken ct) => Task.FromResult(Person);

        public Task<EfApiKey?> ApiKeyAsync(CancellationToken ct) => Task.FromResult(Key);

        /// <summary>Local mode's third lane: the person at the machine, or a runner that named itself. Null is every install with a wall.</summary>
        public Actor? Local { get; set; }

        public Task<Actor?> LocalAsync(CancellationToken ct) => Task.FromResult(Local);

        public Task<bool> IsProgramAsync(CancellationToken ct) =>
            Task.FromResult(Key is not null || Local is { Kind: ActorKind.Key });

        public Task<string> ActorNameAsync(CancellationToken ct) =>
            Task.FromResult(Person?.Name ?? Key?.Name ?? Local?.Name ?? CallerIdentity.Unattributed);
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result?.GetType().Name ?? "nothing"}");

    private static T Created<T>(ActionResult<T> result) =>
        result.Result is CreatedAtActionResult created
            ? (T)created.Value!
            : throw new InvalidOperationException($"expected a created issue, got {result.Result?.GetType().Name ?? "nothing"}");
}

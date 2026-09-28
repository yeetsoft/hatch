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
/// What a runner may write about whether a branch merges, what the board
/// refuses to keep, and the two rules a verdict is kept by: one per repository,
/// and a repeat is not news.
/// </summary>
public class MergeCheckControllerTests
{
    private const string Remote = "git@forge.example:owner/repo.git";
    private const string TrunkSha = "1111111111111111111111111111111111111111";
    private const string BranchSha = "2222222222222222222222222222222222222222";

    // ---- The four verdicts ----

    [Fact]
    public async Task ACleanVerdict_IsKept()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var kept = Value(await h.PutAsync(issue, Clean()));

        Assert.Equal(MergeVerdicts.Clean, kept.Verdict);
        Assert.Equal("main", kept.Trunk);
        Assert.Equal(TrunkSha, kept.TrunkSha);
        Assert.Equal("ha-1-thing", kept.Branch);
        Assert.Equal(BranchSha, kept.BranchSha);
        Assert.Empty(kept.Files);
        Assert.Equal("box:/work/repo", kept.Runner);
        Assert.Equal("Nathan", kept.CheckedBy);
        Assert.Equal(Now, kept.CheckedAt);
        Assert.Equal(Remote, kept.Remote);
        Assert.Equal("forge.example/owner/repo", kept.Canonical);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HoldsTrunk_RoundTripsOnAKeptVerdict(bool holdsTrunk)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var kept = Value(await h.PutAsync(issue, Clean() with { HoldsTrunk = holdsTrunk }));

        Assert.Equal(holdsTrunk, kept.HoldsTrunk);
    }

    [Fact]
    public async Task AConflictedVerdict_NamesItsFiles()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var kept = Value(await h.PutAsync(issue, Conflicted("b.cs", "a.cs")));

        // Sorted, so the same conflict listed in another order is the same one.
        Assert.Equal(["a.cs", "b.cs"], kept.Files);
    }

    [Theory]
    [InlineData(MergeVerdicts.None)]
    [InlineData(MergeVerdicts.Ambiguous)]
    public async Task NoneAndAmbiguous_CarryNoBranchAndNoSha(string verdict)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        // Even when the runner sends them: HA-41's poll keeps what it saw for
        // these two in memory and does not ask the board for a fingerprint.
        var kept = Value(await h.PutAsync(issue, Clean() with { Verdict = verdict, Files = ["x.cs"], HoldsTrunk = true }));

        Assert.Equal(verdict, kept.Verdict);
        Assert.Null(kept.Branch);
        Assert.Null(kept.BranchSha);
        Assert.Empty(kept.Files);
        Assert.Null(kept.HoldsTrunk);
    }

    // ---- Every refusal ----

    [Fact]
    public async Task ARemoteThatDoesNotCanonicalise_IsRefused()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        Assert.Equal("a remote can't be empty", Reason(await h.PutAsync(issue, Clean() with { Remote = "  " })));
        Assert.Contains("remote", Reason(await h.PutAsync(issue, Clean() with { Remote = "nonsense" })));
        Assert.Empty(await h.Db.MergeChecks.ToListAsync());
    }

    [Theory]
    [InlineData("merged")]
    [InlineData("")]
    [InlineData("CLEAN")]
    public async Task AVerdictThatIsNotOneOfTheFour_IsRefused(string verdict)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var reason = Reason(await h.PutAsync(issue, Clean() with { Verdict = verdict }));

        Assert.Equal("a verdict is one of clean, conflicted, none, ambiguous", reason);
    }

    [Theory]
    [MemberData(nameof(NoFiles))]
    public async Task AConflictedVerdictWithNoFiles_IsRefused(IReadOnlyList<string>? files)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var reason = Reason(await h.PutAsync(issue, Conflicted() with { Files = files }));

        Assert.Equal("a conflicted verdict names the files that conflict", reason);
    }

    public static TheoryData<IReadOnlyList<string>?> NoFiles => new()
    {
        null,
        Array.Empty<string>(),
        new[] { "  ", "" },
    };

    [Theory]
    [InlineData(MergeVerdicts.Clean, null)]
    [InlineData(MergeVerdicts.Clean, " ")]
    [InlineData(MergeVerdicts.Conflicted, null)]
    public async Task ACleanOrConflictedVerdictWithNoBranchSha_IsRefused(string verdict, string? sha)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var reason = Reason(await h.PutAsync(issue, Conflicted("a.cs") with { Verdict = verdict, BranchSha = sha }));

        Assert.Equal($"a {verdict} verdict names the sha the branch stood at", reason);
    }

    [Fact]
    public async Task AVerdictWithNoTrunk_NoTrunkSha_OrNoRunner_IsRefused()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        Assert.Contains("trunk", Reason(await h.PutAsync(issue, Clean() with { Trunk = "" })));
        Assert.Contains("trunk", Reason(await h.PutAsync(issue, Clean() with { TrunkSha = "" })));
        Assert.Contains("runner", Reason(await h.PutAsync(issue, Clean() with { Runner = " " })));
        Assert.Contains("at most", Reason(await h.PutAsync(
            issue, Clean() with { Runner = new string('r', ClaimRequest.MaxRunnerLength + 1) })));
    }

    [Fact]
    public async Task AnIssueThatIsNotThere_IsNotFound()
    {
        var h = await NewAsync();

        Assert.IsType<NotFoundResult>((await h.Merge.PutMergeCheck("AER-404", Clean(), default)).Result);
        Assert.IsType<NotFoundResult>((await h.Merge.PutMergeCheck("nonsense", Clean(), default)).Result);
    }

    [Fact]
    public async Task TheIssuesColumn_IsNotChecked()
    {
        var h = await NewAsync();

        // Filed in the inbox and never moved: a verdict taken at the end of an
        // implementation increment arrives before anybody has moved the ticket.
        var issue = await h.FileAsync();

        Assert.Equal(MergeVerdicts.Clean, Value(await h.PutAsync(issue, Clean())).Verdict);
    }

    // ---- One verdict per issue per repository ----

    [Fact]
    public async Task ASecondPut_ReplacesTheFirst()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Clean());

        h.Time.Advance(TimeSpan.FromMinutes(5));
        await h.PutAsync(issue, Conflicted("a.cs") with { BranchSha = "3333" });

        var row = await h.Db.MergeChecks.SingleAsync();
        Assert.Equal(MergeVerdicts.Conflicted, row.Verdict);
        Assert.Equal("3333", row.BranchSha);
        Assert.Equal("a.cs", row.Files);
        Assert.Equal(Now.AddMinutes(5), row.CheckedAt);
    }

    [Fact]
    public async Task TheSameRepositorySpelledTwoWays_IsOneVerdict()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.PutAsync(issue, Clean() with { Remote = "git@forge.example:owner/repo.git" });
        await h.PutAsync(issue, Clean() with { Remote = "https://forge.example/owner/repo" });

        Assert.Single(await h.Db.MergeChecks.ToListAsync());
    }

    [Fact]
    public async Task TwoRepositoriesOnOneIssue_AreKeptApart()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.PutAsync(issue, Clean());
        await h.PutAsync(issue, Conflicted("a.cs") with { Remote = "git@forge.example:owner/other.git" });

        var checks = (await h.ReadAsync(issue.Key)).MergeChecks!;
        Assert.Equal(2, checks.Count);
        Assert.Equal(MergeVerdicts.Conflicted, checks.Single(c => c.Canonical == "forge.example/owner/other").Verdict);
        Assert.Equal(MergeVerdicts.Clean, checks.Single(c => c.Canonical == "forge.example/owner/repo").Verdict);
        Assert.Equal(2, (await h.EventsAsync(issue.Key)).Count);
    }

    [Fact]
    public async Task TwoIssuesOnOneRepository_AreKeptApart()
    {
        var h = await NewAsync();
        var one = await h.FileAsync();
        var two = await h.FileAsync();

        await h.PutAsync(one, Clean());
        await h.PutAsync(two, Conflicted("a.cs"));

        Assert.Equal(MergeVerdicts.Clean, (await h.ReadAsync(one.Key)).MergeChecks!.Single().Verdict);
        Assert.Equal(MergeVerdicts.Conflicted, (await h.ReadAsync(two.Key)).MergeChecks!.Single().Verdict);
    }

    // ---- The trail ----

    [Fact]
    public async Task AVerdictThatChangesTheStoredOne_WritesAnEventCarryingBothSides()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Clean());

        await h.PutAsync(issue, Conflicted("b.cs", "a.cs"));

        var events = await h.EventsAsync(issue.Key);
        Assert.Equal(2, events.Count);

        var e = events[1];
        Assert.Equal("Nathan", e.Actor);
        var payload = e.Payload!.Value;
        Assert.Equal("forge.example/owner/repo", payload.GetProperty("remote").GetString());
        Assert.Equal("clean", payload.GetProperty("from").GetProperty("verdict").GetString());
        Assert.Empty(payload.GetProperty("from").GetProperty("files").EnumerateArray());
        Assert.Equal("conflicted", payload.GetProperty("to").GetProperty("verdict").GetString());
        Assert.Equal(["a.cs", "b.cs"], payload.GetProperty("to").GetProperty("files").EnumerateArray().Select(f => f.GetString()));
    }

    [Fact]
    public async Task TheFirstVerdictForARepository_IsAChangeFromNothing()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.PutAsync(issue, Clean());

        var payload = Assert.Single(await h.EventsAsync(issue.Key)).Payload!.Value;
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("from").ValueKind);
        Assert.Equal("clean", payload.GetProperty("to").GetProperty("verdict").GetString());
    }

    [Fact]
    public async Task ADifferentSetOfFiles_IsAChangeEvenWhenTheVerdictIsNot()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Conflicted("a.cs"));

        await h.PutAsync(issue, Conflicted("a.cs", "b.cs"));

        Assert.Equal(2, (await h.EventsAsync(issue.Key)).Count);
    }

    [Fact]
    public async Task AVerdictThatRepeatsTheStoredOne_WritesNoEvent_ButRefreshesTheRest()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Conflicted("a.cs", "b.cs"));

        h.Time.Advance(TimeSpan.FromMinutes(10));
        h.Caller.Person = new EfPerson { Name = "Other", CreatedAt = Now, UpdatedAt = Now };

        // The same files in another order, on moved shas, from another runner.
        await h.PutAsync(issue, Conflicted("b.cs", "a.cs") with
        {
            TrunkSha = "4444",
            BranchSha = "5555",
            Runner = "elsewhere:/work/repo",
        });

        Assert.Single(await h.EventsAsync(issue.Key));

        var row = await h.Db.MergeChecks.SingleAsync();
        Assert.Equal("4444", row.TrunkSha);
        Assert.Equal("5555", row.BranchSha);
        Assert.Equal(Now.AddMinutes(10), row.CheckedAt);
        Assert.Equal("elsewhere:/work/repo", row.Runner);
        Assert.Equal("Other", row.CheckedBy);
    }

    [Fact]
    public async Task AVerdict_DoesNotMoveTheIssue()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        var was = await h.ReadAsync(issue.Key);

        h.Time.Advance(TimeSpan.FromHours(1));
        await h.PutAsync(issue, Conflicted("a.cs"));

        var now = await h.ReadAsync(issue.Key);
        Assert.Equal(was.UpdatedAt, now.UpdatedAt);
        Assert.Equal(was.StatusId, now.StatusId);
    }

    [Fact]
    public async Task HoldsTrunkAlone_WritesNoEvent()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Clean() with { HoldsTrunk = false });

        await h.PutAsync(issue, Clean() with { HoldsTrunk = true });

        Assert.Single(await h.EventsAsync(issue.Key));
        Assert.True((await h.Db.MergeChecks.SingleAsync()).HoldsTrunk);
    }

    // ---- What every issue carries ----

    [Fact]
    public async Task AnIssueWithNoVerdict_CarriesAnEmptyList()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        Assert.Empty((await h.ReadAsync(issue.Key)).MergeChecks!);
    }

    [Fact]
    public async Task AListOfIssues_ReadsItsVerdictsInOneBatch()
    {
        var h = await NewAsync();
        var one = await h.FileAsync();
        var two = await h.FileAsync();
        var three = await h.FileAsync();
        await h.PutAsync(one, Clean());
        await h.PutAsync(two, Conflicted("a.cs"));

        var dtos = await IssueProjection.ToDtosAsync(
            h.Db, new StubActorDirectory(),
            await h.Db.Issues.OrderBy(i => i.Number).ToListAsync(),
            TestClaims.With(), Now, default);

        var byKey = dtos.Values.ToDictionary(d => d.Key);
        Assert.Equal(MergeVerdicts.Clean, byKey[one.Key].MergeChecks!.Single().Verdict);
        Assert.Equal(MergeVerdicts.Conflicted, byKey[two.Key].MergeChecks!.Single().Verdict);
        Assert.Empty(byKey[three.Key].MergeChecks!);
    }

    [Fact]
    public async Task DeletingTheIssue_TakesItsVerdicts()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Clean());

        h.Db.Issues.Remove(await h.Db.Issues.SingleAsync());
        await h.Db.SaveChangesAsync();

        Assert.Empty(await h.Db.MergeChecks.ToListAsync());
    }

    // ---- The gate ----

    [Fact]
    public void WritingOne_IsOpenToAHatchScopedKey()
    {
        var guard = typeof(MergeCheckController)
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .Single();

        // The caller is a runner, and a verdict only a person could enter
        // would be one nobody ever entered.
        Assert.Equal(ApiKeyScopes.Hatch, guard.AcceptScope);
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    private static MergeCheckRequest Clean() =>
        new(Remote, "main", TrunkSha, MergeVerdicts.Clean, "ha-1-thing", BranchSha, null, "box:/work/repo");

    private static MergeCheckRequest Conflicted(params string[] files) =>
        Clean() with { Verdict = MergeVerdicts.Conflicted, Files = files };

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required MergeCheckController Merge { get; init; }
        public required IssuesController Issues { get; init; }
        public required IssueThreadController Thread { get; init; }
        public required StubCallerIdentity Caller { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }

        public async Task<IssueDto> FileAsync() =>
            Created(await Issues.CreateIssue(new IssueCreateRequest(ProjectId, "task", "a thing", null, null, null, null), default));

        public Task<ActionResult<MergeCheckDto>> PutAsync(IssueDto issue, MergeCheckRequest request) =>
            Merge.PutMergeCheck(issue.Key, request, default);

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
            Merge = new MergeCheckController(db, caller, time),
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time),
            Thread = new IssueThreadController(db, caller, time),
            Caller = caller,
            Time = time,
            ProjectId = hatch.Id,
        };
    }

    /// <summary>Whoever the test says is holding the phone. Their name is the audit actor.</summary>
    public sealed class StubCallerIdentity : ICallerIdentity
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
        result.Value ?? throw new InvalidOperationException($"expected a value, got {Reason(result.Result)}");

    private static T Created<T>(ActionResult<T> result) =>
        result.Result is CreatedAtActionResult created
            ? (T)created.Value!
            : result.Value ?? throw new InvalidOperationException($"expected a created value, got {Reason(result.Result)}");

    /// <summary>The plain-text reason on a refusal, and a failure if it was not one.</summary>
    private static string Reason(ActionResult<MergeCheckDto> result) => result.Result switch
    {
        BadRequestObjectResult o => o.Value?.ToString() ?? "",
        var other => throw new InvalidOperationException($"expected a refusal, got {other?.GetType().Name ?? "a value"}"),
    };
}

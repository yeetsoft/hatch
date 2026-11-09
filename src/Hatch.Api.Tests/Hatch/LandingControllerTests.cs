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
/// What a session or a runner may write about what a repository holds for an
/// issue's branch, what the board refuses to keep, and the two rules a
/// landing is kept by: one per repository, and a plain report never clears
/// what a poll already confirmed.
/// </summary>
public class LandingControllerTests
{
    private const string Remote = "git@forge.example:owner/repo.git";
    private const string Sha = "1111111111111111111111111111111111111111";
    private const string OtherSha = "2222222222222222222222222222222222222222";

    [Fact]
    public async Task AFirstWrite_CreatesARowAndAChangedEvent()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var kept = Value(await h.PutAsync(issue, Report()));

        Assert.Equal(Remote, kept.Remote);
        Assert.Equal("forge.example/owner/repo", kept.Canonical);
        Assert.Equal("main", kept.Trunk);
        Assert.Equal(Sha, kept.Sha);
        Assert.Null(kept.ContainedBy);
        Assert.Equal(Now, kept.RecordedAt);
        Assert.Null(kept.CheckedAt);

        var events = await h.EventsAsync(issue.Key);
        var payload = Assert.Single(events).Payload!.Value;
        Assert.Equal("forge.example/owner/repo", payload.GetProperty("remote").GetString());
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("from").ValueKind);
        Assert.Equal(Sha, payload.GetProperty("to").GetProperty("sha").GetString());
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("to").GetProperty("containedBy").ValueKind);
    }

    [Fact]
    public async Task ARepeatWithTheSameShaAndContainedBy_WritesNoSecondEvent()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Report() with { ContainedBy = Sha });

        h.Time.Advance(TimeSpan.FromMinutes(5));
        var kept = Value(await h.PutAsync(issue, Report() with { ContainedBy = Sha }));

        Assert.Single(await h.EventsAsync(issue.Key));
        Assert.Equal(Now.AddMinutes(5), kept.RecordedAt);
        Assert.Equal(Now.AddMinutes(5), kept.CheckedAt);
    }

    [Fact]
    public async Task AWriteThatChangesTheSha_WritesAnEventWithBothSides()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Report());

        await h.PutAsync(issue, Report() with { Sha = OtherSha });

        var events = await h.EventsAsync(issue.Key);
        Assert.Equal(2, events.Count);

        var payload = events[1].Payload!.Value;
        Assert.Equal(Sha, payload.GetProperty("from").GetProperty("sha").GetString());
        Assert.Equal(OtherSha, payload.GetProperty("to").GetProperty("sha").GetString());
    }

    [Fact]
    public async Task APlainReport_DoesNotClearWhatAPollAlreadyConfirmed()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Report() with { ContainedBy = Sha });

        h.Time.Advance(TimeSpan.FromMinutes(5));
        var kept = Value(await h.PutAsync(issue, Report() with { Trunk = null }));

        Assert.Equal(Sha, kept.ContainedBy);
        Assert.Single(await h.EventsAsync(issue.Key));
        Assert.Equal(Now, kept.CheckedAt);
        Assert.Equal(Now.AddMinutes(5), kept.RecordedAt);
    }

    [Fact]
    public async Task AWriteWithTheSameContainedByAsStored_WritesNoEventEvenWithTheSameSha()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Report() with { ContainedBy = Sha });

        await h.PutAsync(issue, Report() with { ContainedBy = Sha });

        Assert.Single(await h.EventsAsync(issue.Key));
    }

    [Fact]
    public async Task CheckedAt_IsNullUntilAWriteNamesContainedBy_AndRefreshesAfter()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Report());

        var beforePoll = (await h.ReadAsync(issue.Key)).Landings!.Single();
        Assert.Null(beforePoll.CheckedAt);

        h.Time.Advance(TimeSpan.FromMinutes(5));
        await h.PutAsync(issue, Report() with { ContainedBy = Sha });

        h.Time.Advance(TimeSpan.FromMinutes(5));
        var kept = Value(await h.PutAsync(issue, Report() with { ContainedBy = Sha }));

        Assert.Equal(Now.AddMinutes(10), kept.CheckedAt);
    }

    [Fact]
    public async Task ContainedByWithoutTrunk_IsRefused()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var reason = Reason(await h.PutAsync(issue, Report() with { Trunk = null, ContainedBy = Sha }));

        Assert.Equal("a landing that names what contains it must also name its trunk", reason);
    }

    [Fact]
    public async Task AFirstWriteWithNoTrunk_IsRefused()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var reason = Reason(await h.PutAsync(issue, Report() with { Trunk = null }));

        Assert.Equal("a landing names its trunk the first time it's recorded", reason);
        Assert.Empty(await h.Db.Landings.ToListAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("not-hex")]
    public async Task ANonHexOrEmptySha_IsRefused(string sha)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var reason = Reason(await h.PutAsync(issue, Report() with { Sha = sha }));

        Assert.Contains("sha", reason);
        Assert.Empty(await h.Db.Landings.ToListAsync());
    }

    [Fact]
    public async Task AnIssueThatIsNotThere_IsNotFound()
    {
        var h = await NewAsync();

        Assert.IsType<NotFoundResult>((await h.Landing.PutLanding("AER-404", Report(), default)).Result);
        Assert.IsType<NotFoundResult>((await h.Landing.PutLanding("nonsense", Report(), default)).Result);
    }

    [Fact]
    public async Task ARemoteThatDoesNotCanonicalise_IsRefused()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        Assert.Equal("a remote can't be empty", Reason(await h.PutAsync(issue, Report() with { Remote = "  " })));
        Assert.Contains("remote", Reason(await h.PutAsync(issue, Report() with { Remote = "nonsense" })));
        Assert.Empty(await h.Db.Landings.ToListAsync());
    }

    [Fact]
    public async Task TwoRepositoriesOnOneIssue_AreKeptApart()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.PutAsync(issue, Report());
        await h.PutAsync(issue, Report() with { Remote = "git@forge.example:owner/other.git", Sha = OtherSha });

        var landings = (await h.ReadAsync(issue.Key)).Landings!;
        Assert.Equal(2, landings.Count);
        Assert.Equal(Sha, landings.Single(l => l.Canonical == "forge.example/owner/repo").Sha);
        Assert.Equal(OtherSha, landings.Single(l => l.Canonical == "forge.example/owner/other").Sha);
        Assert.Equal(2, (await h.EventsAsync(issue.Key)).Count);
    }

    [Fact]
    public void WritingOne_IsOpenToAHatchScopedKey()
    {
        var guard = typeof(LandingController)
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .Single();

        Assert.True(guard.AcceptKey);
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    private static LandingWriteRequest Report() => new(Remote, Sha, "main", null, "box:/work/repo");

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required LandingController Landing { get; init; }
        public required IssuesController Issues { get; init; }
        public required IssueThreadController Thread { get; init; }
        public required StubCallerIdentity Caller { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }

        public async Task<IssueDto> FileAsync() =>
            Created(await Issues.CreateIssue(new IssueCreateRequest(ProjectId, "task", "a thing", null, null, null, null), default));

        public Task<ActionResult<LandingDto>> PutAsync(IssueDto issue, LandingWriteRequest request) =>
            Landing.PutLanding(issue.Key, request, default);

        public async Task<IssueDto> ReadAsync(string key) => Value(await Issues.GetIssue(key, default));

        /// <summary>The landing's own events, oldest first.</summary>
        public async Task<IReadOnlyList<IssueEventDto>> EventsAsync(string key) =>
            Value(await Thread.GetEvents(key, default))
                .Where(e => e.Kind == EfHatchIssueEvent.LandingChanged)
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
            Landing = new LandingController(db, caller, time),
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time, new ProjectAccess(db, actors)),
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
    private static string Reason(ActionResult<LandingDto> result) => result.Result switch
    {
        BadRequestObjectResult o => o.Value?.ToString() ?? "",
        var other => throw new InvalidOperationException($"expected a refusal, got {other?.GetType().Name ?? "a value"}"),
    };
}

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
/// The release note on an issue: who may write it (a person always, a key only
/// under the issue's live claim), what a write does (sets, trims, clears, is
/// capped), and the one trail entry a change leaves.
/// </summary>
public class IssueReleaseNoteControllerTests
{
    // ---- Setting and clearing it ----

    [Fact]
    public async Task ANote_IsSetTrimmedAndReadBack()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var set = await h.SetAsync(issue.Key, "  Now you can ship faster.\n");

        Assert.Equal("Now you can ship faster.", set.ReleaseNote);
        Assert.Equal("Now you can ship faster.", (await h.RowAsync(issue.Key)).ReleaseNote);
        Assert.Equal("Now you can ship faster.", (await h.ReadAsync(issue.Key)).ReleaseNote);
    }

    [Fact]
    public async Task AnIssueWithNoNote_ReadsNull()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        Assert.Null((await h.ReadAsync(issue.Key)).ReleaseNote);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n ")]
    [InlineData(null)]
    public async Task ABlankNote_ClearsIt(string? blank)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.SetAsync(issue.Key, "a note");

        var cleared = await h.SetAsync(issue.Key, blank);

        Assert.Null(cleared.ReleaseNote);
        Assert.Null((await h.RowAsync(issue.Key)).ReleaseNote);
    }

    [Fact]
    public async Task AnIssueThatIsNotThere_IsNotFound()
    {
        var h = await NewAsync();

        Assert.IsType<NotFoundResult>(
            (await h.Notes.PutIssueReleaseNote("AER-404", new ReleaseNoteRequest("x"), null, default)).Result);
        Assert.IsType<NotFoundResult>(
            (await h.Notes.PutIssueReleaseNote("nonsense", new ReleaseNoteRequest("x"), null, default)).Result);
    }

    [Fact]
    public void ThePatchRequest_HasNoWayToWriteIt()
    {
        Assert.Null(typeof(IssuePatchRequest).GetProperty("ReleaseNote"));
    }

    // ---- The cap ----

    [Fact]
    public async Task ANoteOfExactlyTheCap_Passes()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var set = await h.SetAsync(issue.Key, new string('a', ReleaseNoteRequest.MaxLength));

        Assert.Equal(ReleaseNoteRequest.MaxLength, set.ReleaseNote!.Length);
    }

    [Fact]
    public async Task ANoteOverTheCap_IsRefusedNamingItAndWritesNothing()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var refused = await h.Notes.PutIssueReleaseNote(
            issue.Key, new ReleaseNoteRequest(new string('a', ReleaseNoteRequest.MaxLength + 1)), null, default);

        var bad = Assert.IsType<BadRequestObjectResult>(refused.Result);
        Assert.Contains("2000", bad.Value!.ToString());
        Assert.Null((await h.RowAsync(issue.Key)).ReleaseNote);
        Assert.Empty(await h.EventsAsync(issue.Key));
    }

    [Fact]
    public async Task TheCap_IsMeasuredAfterTrimming()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var set = await h.SetAsync(issue.Key, "  " + new string('a', ReleaseNoteRequest.MaxLength) + "  ");

        Assert.Equal(ReleaseNoteRequest.MaxLength, set.ReleaseNote!.Length);
    }

    // ---- Who may write it ----

    [Fact]
    public async Task APerson_NeedsNoToken_EvenAgainstALiveClaim()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.ClaimAsync(issue.Key, Guid.NewGuid());

        var set = await h.SetAsync(issue.Key, "written by hand");

        Assert.Equal("written by hand", set.ReleaseNote);
    }

    [Fact]
    public async Task AKey_WithTheMatchingToken_MayWriteIt_AndIsTheActor()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        var token = Guid.NewGuid();
        await h.ClaimAsync(issue.Key, token);
        h.BecomeAKey();

        var set = await h.SetAsync(issue.Key, "written by a session", token);

        Assert.Equal("written by a session", set.ReleaseNote);
        var written = Assert.Single(await h.EventsAsync(issue.Key));
        Assert.Equal("a key", written.Actor);
    }

    [Fact]
    public async Task AKeylessRunnerThatNamedItself_IsHeldToTheSameRule()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        h.Caller.Person = null;
        h.Caller.Local = new Actor(ActorKind.Key, LocalCaller.RunnerIdFor("host:/src"), "host:/src");

        var refused = await h.Notes.PutIssueReleaseNote(issue.Key, new ReleaseNoteRequest("x"), Guid.NewGuid(), default);

        Assert.IsType<ConflictObjectResult>(refused.Result);
        Assert.Null((await h.RowAsync(issue.Key)).ReleaseNote);
    }

    [Fact]
    public async Task AKey_AgainstNoClaim_IsRefused()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        h.BecomeAKey();

        var refused = await h.Notes.PutIssueReleaseNote(issue.Key, new ReleaseNoteRequest("x"), Guid.NewGuid(), default);

        var conflict = Assert.IsType<ConflictObjectResult>(refused.Result);
        Assert.Contains($"{issue.Key} holds no live claim", conflict.Value!.ToString());
        Assert.Null((await h.RowAsync(issue.Key)).ReleaseNote);
    }

    [Fact]
    public async Task AKey_WithTheWrongToken_IsRefused()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.ClaimAsync(issue.Key, Guid.NewGuid());
        h.BecomeAKey();

        var refused = await h.Notes.PutIssueReleaseNote(issue.Key, new ReleaseNoteRequest("x"), Guid.NewGuid(), default);

        var conflict = Assert.IsType<ConflictObjectResult>(refused.Result);
        Assert.Contains($"{issue.Key}'s live claim does not match this token", conflict.Value!.ToString());
        Assert.Null((await h.RowAsync(issue.Key)).ReleaseNote);
    }

    [Fact]
    public async Task AKey_WithNoToken_AgainstALiveClaim_IsAMismatch()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.ClaimAsync(issue.Key, Guid.NewGuid());
        h.BecomeAKey();

        var refused = await h.Notes.PutIssueReleaseNote(issue.Key, new ReleaseNoteRequest("x"), null, default);

        var conflict = Assert.IsType<ConflictObjectResult>(refused.Result);
        Assert.Contains("does not match this token", conflict.Value!.ToString());
    }

    [Fact]
    public async Task AKey_AgainstALapsedClaim_IsToldThereIsNoLiveClaim_EvenWithTheRightToken()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        var token = Guid.NewGuid();
        await h.ClaimAsync(issue.Key, token, heartbeatAt: Now.AddSeconds(-TestClaims.Ttl - 1));
        h.BecomeAKey();

        var refused = await h.Notes.PutIssueReleaseNote(issue.Key, new ReleaseNoteRequest("x"), token, default);

        var conflict = Assert.IsType<ConflictObjectResult>(refused.Result);
        Assert.Contains("holds no live claim", conflict.Value!.ToString());
        Assert.Null((await h.RowAsync(issue.Key)).ReleaseNote);
    }

    // ---- The trail ----

    [Fact]
    public async Task AChange_WritesOneEntryWithBothSides_AndMovesUpdatedAt()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        var before = (await h.RowAsync(issue.Key)).UpdatedAt;
        h.Time.Advance(TimeSpan.FromMinutes(1));

        await h.SetAsync(issue.Key, "first");
        var afterFirst = (await h.RowAsync(issue.Key)).UpdatedAt;
        var single = Assert.Single(await h.EventsAsync(issue.Key));
        Assert.Equal((null, "first"), Sides(single));
        Assert.Equal("a person", single.Actor);
        Assert.True(afterFirst > before);
        Assert.Equal(h.Time.GetUtcNow(), afterFirst);

        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.SetAsync(issue.Key, "second");

        var events = await h.EventsAsync(issue.Key);
        Assert.Equal(2, events.Count);
        Assert.Equal(("first", "second"), Sides(events[1]));
        Assert.Equal(h.Time.GetUtcNow(), (await h.RowAsync(issue.Key)).UpdatedAt);
    }

    [Fact]
    public async Task Clearing_WritesAnEntryWhoseToIsNull()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.SetAsync(issue.Key, "a note");

        await h.SetAsync(issue.Key, "");

        var events = await h.EventsAsync(issue.Key);
        Assert.Equal(2, events.Count);
        Assert.Equal(("a note", null), Sides(events[1]));
    }

    [Theory]
    [InlineData("same")]
    [InlineData("  same  ")]
    public async Task WritingTheValueAlreadyHeld_WritesNoEntryAndLeavesUpdatedAt(string again)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.SetAsync(issue.Key, "same");
        var updated = (await h.RowAsync(issue.Key)).UpdatedAt;
        h.Time.Advance(TimeSpan.FromHours(1));

        await h.SetAsync(issue.Key, again);

        Assert.Single(await h.EventsAsync(issue.Key));
        Assert.Equal(updated, (await h.RowAsync(issue.Key)).UpdatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ClearingAnIssueWithNoNote_WritesNothing(string? blank)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        var updated = (await h.RowAsync(issue.Key)).UpdatedAt;
        h.Time.Advance(TimeSpan.FromHours(1));

        await h.SetAsync(issue.Key, blank);

        Assert.Empty(await h.EventsAsync(issue.Key));
        Assert.Equal(updated, (await h.RowAsync(issue.Key)).UpdatedAt);
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required StubCallerIdentity Caller { get; init; }
        public required IssueReleaseNoteController Notes { get; init; }
        public required IssuesController Issues { get; init; }
        public required IssueThreadController Thread { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }

        /// <summary>From here on the caller is a key, with no person behind it.</summary>
        public void BecomeAKey()
        {
            Caller.Person = null;
            Caller.Key = new EfApiKey
            {
                Name = "a key",
                Hash = [1],
                Prefix = "hatch_ak_x",
                Scopes = [ApiKeyScopes.Hatch],
                CreatedAt = Now,
            };
        }

        /// <summary>A claim written straight onto the row, live unless the heartbeat is given as older than the TTL.</summary>
        public async Task ClaimAsync(string key, Guid token, DateTimeOffset? heartbeatAt = null)
        {
            IssueKey.TryParse(key, out var projectKey, out var number);
            var issue = await Db.Issues.WithKey(projectKey, number).FirstAsync();
            issue.ClaimToken = token;
            issue.ClaimedBy = "a key";
            issue.ClaimedAt = Now;
            issue.ClaimHeartbeatAt = heartbeatAt ?? Now;
            await Db.SaveChangesAsync();
        }

        public async Task<IssueDto> FileAsync()
        {
            var created = await Issues.CreateIssue(new IssueCreateRequest(ProjectId, "epic", "the epic", null, null, null, null), default);
            return created.Result is CreatedAtActionResult c
                ? (IssueDto)c.Value!
                : created.Value ?? throw new InvalidOperationException($"expected a created issue, got {created.Result}");
        }

        public async Task<IssueDto> SetAsync(string key, string? note, Guid? heldToken = null) =>
            Value(await Notes.PutIssueReleaseNote(key, new ReleaseNoteRequest(note), heldToken, default));

        public async Task<IssueDto> ReadAsync(string key) => Value(await Issues.GetIssue(key, default));

        public async Task<EfHatchIssue> RowAsync(string key)
        {
            IssueKey.TryParse(key, out var projectKey, out var number);
            return await Db.Issues.AsNoTracking().WithKey(projectKey, number).FirstAsync();
        }

        /// <summary>The note's own events, oldest first.</summary>
        public async Task<IReadOnlyList<IssueEventDto>> EventsAsync(string key) =>
            Value(await Thread.GetEvents(key, default))
                .Where(e => e.Kind == EfHatchIssueEvent.ReleaseNoteChanged)
                .Reverse()
                .ToList();
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Add(project);
        db.AddRange(
            new EfHatchStatus { Name = "inbox", SortOrder = 10 },
            new EfHatchStatus { Name = "done", SortOrder = 50, IsTerminal = true });
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var caller = new StubCallerIdentity { Person = new EfPerson { Name = "a person", CreatedAt = Now, UpdatedAt = Now } };
        var actors = new StubActorDirectory();

        return new Harness
        {
            Db = db,
            Caller = caller,
            Notes = new IssueReleaseNoteController(db, actors, TestClaims.With(), caller, time),
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time, new ProjectAccess(db, actors)),
            Thread = new IssueThreadController(db, caller, time),
            Time = time,
            ProjectId = project.Id,
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

        public Actor? Local { get; set; }

        public Task<Actor?> LocalAsync(CancellationToken ct) => Task.FromResult(Local);

        public Task<bool> IsProgramAsync(CancellationToken ct) =>
            Task.FromResult(Key is not null || Local is { Kind: ActorKind.Key });

        public Task<string> ActorNameAsync(CancellationToken ct) =>
            Task.FromResult(Person?.Name ?? Key?.Name ?? Local?.Name ?? CallerIdentity.Unattributed);
    }

    private static (string? From, string? To) Sides(IssueEventDto e)
    {
        var payload = e.Payload!.Value;
        return (Side(payload, "from"), Side(payload, "to"));

        static string? Side(JsonElement payload, string name) =>
            payload.GetProperty(name) is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result}");
}

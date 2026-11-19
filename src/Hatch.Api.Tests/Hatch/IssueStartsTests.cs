using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// Who started an issue (HA-2481): written once an issue crosses
/// <see cref="Columns.SendBack"/> rightward, by every route that can move an
/// issue that far - the board move, PATCH, bulk and a hop - and read back on
/// <see cref="IssueDto.StartedBy"/> by name.
///
/// <para>The board here is inbox (10), todo (20), doing (30, ticked the
/// implementation column), done (40, terminal), none of it protected.
/// <see cref="Columns.SendBack"/> measures to "todo", so inbox is the one
/// column left of it - a move out of inbox is the only one of these tests'
/// moves that starts anything.</para>
/// </summary>
public class IssueStartsTests
{
    // ---- The board move ----

    [Fact]
    public async Task APersonsMove_OutOfInbox_RecordsThem()
    {
        var h = await NewAsync();
        var ada = h.Actors.AddPerson("Ada");
        h.AsPerson(ada);
        var issue = await h.FileAsync();

        await h.MoveAsync(issue.Key, h.TodoId);

        var row = await h.RowAsync(issue.Key);
        Assert.Equal(ada.Id, row.StartedBy);
        Assert.NotNull(row.StartedAt);
    }

    [Fact]
    public async Task AKeysMove_OutOfInbox_RecordsNobody_AndClearsAnEarlierPerson()
    {
        var h = await NewAsync();
        var ada = h.Actors.AddPerson("Ada");
        var issue = await h.FileAsync();
        var seeded = await h.RowAsync(issue.Key);
        seeded.StartedBy = ada.Id;
        seeded.StartedAt = Now.AddDays(-1);
        await h.Db.SaveChangesAsync();

        h.AsKey(h.Actors.AddKey("hatch-loop"));
        await h.MoveAsync(issue.Key, h.TodoId);

        var row = await h.RowAsync(issue.Key);
        Assert.Null(row.StartedBy);
        Assert.Equal(Now, row.StartedAt);
    }

    [Fact]
    public async Task ALaterMove_FromTodoToDoing_LeavesTheRecordAlone()
    {
        var h = await NewAsync();
        var ada = h.Actors.AddPerson("Ada");
        h.AsPerson(ada);
        var issue = await h.FileAsync();
        await h.MoveAsync(issue.Key, h.TodoId);
        var started = (await h.RowAsync(issue.Key)).StartedAt;

        h.AsPerson(h.Actors.AddPerson("Bea"));
        h.Time.Advance(TimeSpan.FromHours(1));
        await h.MoveAsync(issue.Key, h.DoingId);

        var row = await h.RowAsync(issue.Key);
        Assert.Equal(ada.Id, row.StartedBy);
        Assert.Equal(started, row.StartedAt);
    }

    [Fact]
    public async Task AMoveBackToInboxAndForwardAgain_RecordsTheNewMover()
    {
        var h = await NewAsync();
        h.AsPerson(h.Actors.AddPerson("Ada"));
        var issue = await h.FileAsync();
        await h.MoveAsync(issue.Key, h.TodoId);

        await h.MoveAsync(issue.Key, h.InboxId, fromStatusId: h.TodoId);

        var bea = h.Actors.AddPerson("Bea");
        h.AsPerson(bea);
        h.Time.Advance(TimeSpan.FromHours(1));
        await h.MoveAsync(issue.Key, h.TodoId, fromStatusId: h.InboxId);

        var row = await h.RowAsync(issue.Key);
        Assert.Equal(bea.Id, row.StartedBy);
    }

    // ---- PATCH and bulk record the caller the same way ----

    [Fact]
    public async Task Patch_OutOfInbox_RecordsTheCaller()
    {
        var h = await NewAsync();
        var ada = h.Actors.AddPerson("Ada");
        h.AsPerson(ada);
        var issue = await h.FileAsync();

        await h.PatchAsync(issue.Key, new IssuePatchRequest(null, null, null, h.TodoId, null, null, null, null, null, null));

        var row = await h.RowAsync(issue.Key);
        Assert.Equal(ada.Id, row.StartedBy);
    }

    [Fact]
    public async Task Bulk_OutOfInbox_RecordsTheCaller()
    {
        var h = await NewAsync();
        var ada = h.Actors.AddPerson("Ada");
        h.AsPerson(ada);
        var issue = await h.FileAsync();

        await h.Issues.BulkEdit(new IssueBulkEditRequest([issue.Key], StatusId: h.TodoId), default);

        var row = await h.RowAsync(issue.Key);
        Assert.Equal(ada.Id, row.StartedBy);
    }

    // ---- GET shows it by name ----

    [Fact]
    public async Task Get_ShowsStartedByByName()
    {
        var h = await NewAsync();
        var ada = h.Actors.AddPerson("Ada");
        h.AsPerson(ada);
        var issue = await h.FileAsync();
        await h.MoveAsync(issue.Key, h.TodoId);

        var read = await h.ReadAsync(issue.Key);

        Assert.Equal("Ada", read.StartedBy);
        Assert.NotNull(read.StartedAt);
    }

    [Fact]
    public async Task Get_AnIssueNeverStarted_ShowsNeitherField()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var read = await h.ReadAsync(issue.Key);

        Assert.Null(read.StartedBy);
        Assert.Null(read.StartedAt);
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required StubCallerIdentity Caller { get; init; }
        public required StubActorDirectory Actors { get; init; }
        public required IssuesController Issues { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }
        public required int InboxId { get; init; }
        public required int TodoId { get; init; }
        public required int DoingId { get; init; }
        public required int DoneId { get; init; }

        /// <summary>Who is holding the phone: a live person, for both <see cref="ICallerIdentity"/> and <see cref="IActorDirectory.MeAsync"/>.</summary>
        public void AsPerson(Actor person)
        {
            Caller.Person = new EfPerson { Id = person.Id, Name = person.Name, CreatedAt = Now, UpdatedAt = Now };
            Caller.Key = null;
            Actors.Me = person;
        }

        /// <summary>The same, for a key - the loop's own write, which SetterAuthority.CallerAsync reads as nobody.</summary>
        public void AsKey(Actor key)
        {
            Caller.Key = new EfApiKey { Id = key.Id, Name = key.Name, Prefix = "hatch_ak_", Hash = [], CreatedAt = Now };
            Caller.Person = null;
            Actors.Me = key;
        }

        public async Task<IssueDto> FileAsync(string type = "task", string title = "a thing") =>
            Created(await Issues.CreateIssue(new IssueCreateRequest(ProjectId, type, title, null, null, null, null), default));

        public async Task<IssueDto> ReadAsync(string key) => Value(await Issues.GetIssue(key, default));

        public async Task<IssueDto> PatchAsync(string key, IssuePatchRequest patch) =>
            Value(await Issues.PatchIssue(key, patch, default));

        public async Task<IssueDto> MoveAsync(string key, int statusId, int? fromStatusId = null) =>
            Value(await Issues.MoveIssue(key, new IssueMoveRequest(statusId, null, null, fromStatusId), default));

        /// <summary>The row itself, which is what the projection is read against.</summary>
        public async Task<EfHatchIssue> RowAsync(string key)
        {
            Db.ChangeTracker.Clear();
            IssueKey.TryParse(key, out var projectKey, out var number);
            return await Db.Issues.AsNoTracking().WithKey(projectKey, number).FirstAsync();
        }
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var hatch = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Add(hatch);
        var inbox = new EfHatchStatus { Name = "inbox", SortOrder = 10 };
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        var doing = new EfHatchStatus { Name = "doing", SortOrder = 30, IsImplementation = true };
        var done = new EfHatchStatus { Name = "done", SortOrder = 40, IsTerminal = true };
        db.AddRange(inbox, todo, doing, done);
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var caller = new StubCallerIdentity { Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now } };
        var actors = new StubActorDirectory();

        return new Harness
        {
            Db = db,
            Caller = caller,
            Actors = actors,
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time, new ProjectAccess(db, actors)),
            Time = time,
            ProjectId = hatch.Id,
            InboxId = inbox.Id,
            TodoId = todo.Id,
            DoingId = doing.Id,
            DoneId = done.Id,
        };
    }

    /// <summary>Whoever the test says is holding the phone. Their name is the audit actor; <see cref="StubActorDirectory.Me"/> is who <see cref="SetterAuthority.CallerAsync"/> reads.</summary>
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

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result}");

    private static T Created<T>(ActionResult<T> result) =>
        result.Result is CreatedAtActionResult created
            ? (T)created.Value!
            : result.Value ?? throw new InvalidOperationException($"expected a created value, got {result.Result}");
}

using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// A message being read: marked once, by whom, and never twice.
///
/// <para><b>These run against a real Postgres, and skip without one</b>, for
/// the reason <see cref="IssueClaimTests"/> does: the guarantee is the
/// <c>WHERE</c> clause of a conditional <c>UPDATE</c>, and EF's in-memory
/// provider refuses <c>ExecuteUpdateAsync</c>. Run <c>make test-api-db</c>.</para>
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class MessageDeliveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [SkippableFact]
    public async Task Delivering_MarksTheMessageAndNamesTheLiveClaimsRunner()
    {
        await using var h = await NewAsync();
        var key = await h.FileAsync();
        await h.TakeAsync(key, "somewhere:/checkouts/one");
        var sent = await h.SendAsync(key, "use the other table");

        h.Time.Advance(TimeSpan.FromSeconds(20));
        var delivered = Assert.Single(Value(await h.Messages.Deliver(key, null, default)));

        Assert.Equal(sent.Id, delivered.Id);
        Assert.Equal(Now.AddSeconds(20), delivered.DeliveredAt);
        Assert.Equal("somewhere:/checkouts/one", delivered.DeliveredTo);

        // And the thread says the same, from the row.
        var read = Assert.Single(Value(await h.Thread.GetComments(key, default)));
        Assert.Equal(delivered.DeliveredAt, read.DeliveredAt);
        Assert.Equal(delivered.DeliveredTo, read.DeliveredTo);
    }

    [SkippableFact]
    public async Task ASecondDelivery_ReturnsNothingAndWritesNoSecondEvent()
    {
        await using var h = await NewAsync();
        var key = await h.FileAsync();
        await h.SendAsync(key, "use the other table");

        Assert.Single(Value(await h.Messages.Deliver(key, null, default)));
        Assert.Empty(Value(await h.Messages.Deliver(key, null, default)));

        Assert.Equal(1, (await h.EventKindsAsync(key)).Count(k => k == EfHatchIssueEvent.MessageDelivered));
    }

    [SkippableFact]
    public async Task TwoDeliveriesRacing_ReturnTheMessageOnceBetweenThem()
    {
        await using var h = await NewAsync();
        var key = await h.FileAsync();
        await h.SendAsync(key, "use the other table");

        // Every pair is a race, so run a good many of them against fresh rows.
        for (var round = 0; round < 10; round++)
        {
            var again = await h.FileAsync();
            await h.SendAsync(again, "one");
            await h.SendAsync(again, "two");

            var results = await Task.WhenAll(
                h.Messages.Deliver(again, null, default),
                h.Messages.Deliver(again, null, default),
                h.Messages.Deliver(again, null, default));

            var ids = results.SelectMany(r => Value(r)).Select(c => c.Id).ToList();
            Assert.Equal(2, ids.Count);
            Assert.Equal(2, ids.Distinct().Count());
            Assert.Equal(2, (await h.EventKindsAsync(again)).Count(k => k == EfHatchIssueEvent.MessageDelivered));
        }
    }

    [SkippableFact]
    public async Task DeliveryByIds_LeavesTheOtherMessagesUnread()
    {
        await using var h = await NewAsync();
        var key = await h.FileAsync();
        var first = await h.SendAsync(key, "first");
        var second = await h.SendAsync(key, "second");

        var delivered = Assert.Single(Value(await h.Messages.Deliver(key, new MessageDeliverRequest([second.Id]), default)));

        Assert.Equal(second.Id, delivered.Id);
        var rest = Assert.Single(Value(await h.Messages.Deliver(key, null, default)));
        Assert.Equal(first.Id, rest.Id);
    }

    [SkippableFact]
    public async Task WithNoLiveClaim_TheMessageIsDeliveredToTheCaller()
    {
        await using var h = await NewAsync();
        var key = await h.FileAsync();
        await h.SendAsync(key, "use the other table");

        var delivered = Assert.Single(Value(await h.Messages.Deliver(key, null, default)));

        Assert.Equal("Nathan", delivered.DeliveredTo);
    }

    [SkippableFact]
    public async Task AnExpiredClaim_IsNotWhoWasHandedTheMessage()
    {
        await using var h = await NewAsync();
        var key = await h.FileAsync();
        await h.TakeAsync(key, "somewhere:/checkouts/one");
        await h.SendAsync(key, "use the other table");

        h.Time.Advance(TimeSpan.FromSeconds(TestClaims.Ttl + 1));
        var delivered = Assert.Single(Value(await h.Messages.Deliver(key, null, default)));

        Assert.Equal("Nathan", delivered.DeliveredTo);
    }

    [SkippableFact]
    public async Task NotesAndQuestions_AreNeverDelivered()
    {
        await using var h = await NewAsync();
        var key = await h.FileAsync();
        Value(await h.Thread.AddComment(key, new CommentCreateRequest("sha abc123"), default));
        Value(await h.Thread.AddComment(key, new CommentCreateRequest("per-node or global?", EfHatchComment.Question), default));

        Assert.Empty(Value(await h.Messages.Deliver(key, null, default)));

        // Not even when named: ids narrow the candidates, they never widen them.
        var ids = Value(await h.Thread.GetComments(key, default)).Select(c => c.Id).ToList();
        Assert.Empty(Value(await h.Messages.Deliver(key, new MessageDeliverRequest(ids), default)));
        Assert.All(Value(await h.Thread.GetComments(key, default)), c => Assert.Null(c.DeliveredAt));
    }

    [SkippableFact]
    public async Task ADeliveryOnOneIssue_NeverTouchesAnotherIssuesMessage()
    {
        await using var h = await NewAsync();
        var mine = await h.FileAsync();
        var theirs = await h.FileAsync();
        var elsewhere = await h.SendAsync(theirs, "not for you");

        Assert.Empty(Value(await h.Messages.Deliver(mine, new MessageDeliverRequest([elsewhere.Id]), default)));
        Assert.Single(Value(await h.Messages.Deliver(theirs, null, default)));
    }

    [SkippableFact]
    public async Task Delivery_WritesAnEventNamingTheCommentAndTheRunner()
    {
        await using var h = await NewAsync();
        var key = await h.FileAsync();
        await h.TakeAsync(key, "somewhere:/checkouts/one");
        var sent = await h.SendAsync(key, "use the other table");

        await h.Messages.Deliver(key, null, default);

        var events = Value(await h.Thread.GetEvents(key, default));
        var delivered = Assert.Single(events, e => e.Kind == EfHatchIssueEvent.MessageDelivered);
        Assert.Equal(sent.Id, delivered.Payload!.Value.GetProperty("commentId").GetInt64());
        Assert.Equal("somewhere:/checkouts/one", delivered.Payload!.Value.GetProperty("to").GetString());
        Assert.Contains(events, e => e.Kind == EfHatchIssueEvent.Messaged);
    }

    [SkippableFact]
    public async Task AnUnknownIssue_Is404()
    {
        await using var h = await NewAsync();

        Assert.IsType<NotFoundResult>((await h.Messages.Deliver("AER-99", null, default)).Result);
    }

    [SkippableFact]
    public async Task NothingToDeliver_IsAnEmptyListAndNotAnError()
    {
        await using var h = await NewAsync();
        var key = await h.FileAsync();

        Assert.Empty(Value(await h.Messages.Deliver(key, null, default)));
    }

    private sealed class Harness : IAsyncDisposable
    {
        public required string ConnectionString { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required StubCallerIdentity Caller { get; init; }
        public required int ProjectId { get; init; }
        public required int StatusId { get; init; }

        private readonly List<HatchContext> open = [];
        private int next = 1;

        // A context of its own per controller, as an HTTP request has: the
        // writes under test run outside the change tracker.
        public IssueMessagesController Messages => new(Connect(), TestClaims.With(), Caller, Time);

        public IssueClaimController Claims
        {
            get
            {
                var db = Connect();
                return new(db, TestClaims.With(), Caller, Time, TestClaims.Preemption(db, Time));
            }
        }

        public IssueThreadController Thread => new(Connect(), Caller, Time);

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

        public async Task<string> FileAsync()
        {
            var number = next++;
            var db = Connect();

            db.Issues.Add(new EfHatchIssue
            {
                ProjectId = ProjectId,
                Number = number,
                Type = "story",
                Title = "a thing to do",
                StatusId = StatusId,
                Rank = 1024 * number,
                CreatedBy = "operator",
                CreatedAt = Now,
                UpdatedAt = Now,
            });

            await db.SaveChangesAsync();
            return IssueKey.Format("AER", number);
        }

        public async Task TakeAsync(string key, string runner) =>
            Value(await Claims.TakeClaim(key, new ClaimRequest(runner), default));

        public async Task<CommentDto> SendAsync(string key, string body) =>
            Value(await Thread.AddComment(key, new CommentCreateRequest(body, EfHatchComment.Message), default));

        public async Task<IReadOnlyList<string>> EventKindsAsync(string key) =>
            Value(await Thread.GetEvents(key, default)).Select(e => e.Kind).ToList();
    }

    private static async Task<Harness> NewAsync()
    {
        Skip.IfNot(
            HatchDatabase.Available,
            "HATCH_TEST_DATABASE_URL is unset - delivery is a conditional UPDATE, which EF's in-memory " +
            "provider cannot execute. Run `make test-api-db`, or point the variable at a scratch database.");

        var connectionString = await HatchDatabase.PrepareAsync();

        await using var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var status = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        db.AddRange(project, status);
        await db.SaveChangesAsync();

        return new Harness
        {
            ConnectionString = connectionString,
            Time = new FakeTimeProvider(Now),
            Caller = new StubCallerIdentity
            {
                Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now },
            },
            ProjectId = project.Id,
            StatusId = status.Id,
        };
    }

    private sealed class StubCallerIdentity : ICallerIdentity
    {
        public EfPerson? Person { get; set; }

        public Task<EfAuthGrant?> GrantAsync(CancellationToken ct) => Task.FromResult<EfAuthGrant?>(null);

        public Task<Guid?> PersonIdAsync(CancellationToken ct) => Task.FromResult(Person?.Id);

        public Task<EfPerson?> PersonAsync(CancellationToken ct) => Task.FromResult(Person);

        public Task<EfApiKey?> ApiKeyAsync(CancellationToken ct) => Task.FromResult<EfApiKey?>(null);

        public Task<Actor?> LocalAsync(CancellationToken ct) => Task.FromResult<Actor?>(null);

        public Task<bool> IsProgramAsync(CancellationToken ct) => Task.FromResult(false);

        public Task<string> ActorNameAsync(CancellationToken ct) =>
            Task.FromResult(Person?.Name ?? CallerIdentity.Unattributed);
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result?.GetType().Name ?? "no result"}");
}

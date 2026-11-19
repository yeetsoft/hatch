using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// Shelving a tree into a deferred column and posting a dream from it: what
/// moves, what stays, what is written beside it, and what is refused before
/// anything is.
/// </summary>
public class IssueRemandControllerTests
{
    // ---- What a remand does ----

    [Fact]
    public async Task ARootWithThreeOpenDescendantsAndADoneTask_MovesTheThree_AndLeavesTheDoneOne()
    {
        var h = await NewAsync();

        var result = Created(await h.Controller.Remand(h.Root.Key, new DreamRemandRequest(h.Shelf.Id, [h.Story.Key, h.Task.Key, h.Other.Key, h.Done.Key]), default));

        Assert.Equal(new[] { h.Root.Key, h.Story.Key, h.Task.Key, h.Other.Key }.Order().ToArray(), result.Changed.Order().ToArray());
        Assert.Equal([h.Done.Key], result.Unchanged);
        Assert.Equal([h.Root.Key, h.Story.Key, h.Task.Key, h.Other.Key], result.Dream.RemandedFrom);

        h.Db.ChangeTracker.Clear();
        foreach (var moved in new[] { h.Root, h.Story, h.Task, h.Other })
        {
            Assert.Equal(h.Shelf.Id, (await h.Db.Issues.SingleAsync(i => i.Id == moved.Id)).StatusId);

            var kinds = await h.EventKindsAsync(moved.Id);
            Assert.Contains(EfHatchIssueEvent.Remanded, kinds);
            Assert.Contains(EfHatchIssueEvent.StatusChanged, kinds);
        }

        var done = await h.Db.Issues.SingleAsync(i => i.Id == h.Done.Id);
        Assert.Equal(h.Closed.Id, done.StatusId);
        Assert.DoesNotContain(EfHatchIssueEvent.Remanded, await h.EventKindsAsync(h.Done.Id));
    }

    // ---- Who started it (HA-2481): a remand always shelves into a deferred column, so it never crosses Columns.SendBack and never writes StartedBy ----

    [Fact]
    public async Task ARemand_LeavesAnAlreadyStartedIssuesStarterAlone()
    {
        var h = await NewAsync();
        var starter = h.Actors.AddPerson("Ada");
        var row = await h.Db.Issues.SingleAsync(i => i.Id == h.Root.Id);
        row.StartedBy = starter.Id;
        row.StartedAt = Now.AddDays(-1);
        await h.Db.SaveChangesAsync();

        await h.Controller.Remand(h.Root.Key, new DreamRemandRequest(h.Shelf.Id), default);

        h.Db.ChangeTracker.Clear();
        var after = await h.Db.Issues.SingleAsync(i => i.Id == h.Root.Id);
        Assert.Equal(starter.Id, after.StartedBy);
        Assert.Equal(Now.AddDays(-1), after.StartedAt);
    }

    [Fact]
    public async Task ARemand_OfAnIssueNeverStarted_WritesNoStarter()
    {
        var h = await NewAsync();

        await h.Controller.Remand(h.Root.Key, new DreamRemandRequest(h.Shelf.Id), default);

        h.Db.ChangeTracker.Clear();
        var after = await h.Db.Issues.SingleAsync(i => i.Id == h.Root.Id);
        Assert.Null(after.StartedBy);
        Assert.Null(after.StartedAt);
    }

    [Fact]
    public async Task TheDream_ReadsTheRootsTitleAndDescription_AndCarriesItsKeyProjectAndAuthor()
    {
        var h = await NewAsync();

        var dream = Created(await h.Controller.Remand(h.Root.Key, new DreamRemandRequest(h.Shelf.Id), default)).Dream;

        Assert.Equal("the root\n\nwhat the root is for", dream.Body);
        Assert.Equal(h.Root.Key, dream.Source);
        Assert.Equal(h.ProjectId, dream.ProjectId);
        Assert.Equal("a person", dream.Author);
        Assert.Empty(dream.WovenInto);
    }

    [Fact]
    public async Task ARootWithNoDescription_MakesADreamOfItsTitleAlone()
    {
        var h = await NewAsync();
        var bare = await h.SeedAsync("epic", "a bare root", h.Open, parentId: null, description: "");

        var dream = Created(await h.Controller.Remand(bare.Key, new DreamRemandRequest(h.Shelf.Id), default)).Dream;

        Assert.Equal("a bare root", dream.Body);
    }

    [Fact]
    public async Task APayload_NamesTheDreamAndTheColumn_AndSaysWhichIssueWasTheRoot()
    {
        var h = await NewAsync();

        var dream = Created(await h.Controller.Remand(h.Root.Key, new DreamRemandRequest(h.Shelf.Id, [h.Story.Key]), default)).Dream;

        var payloads = await h.RemandedPayloadsAsync();
        Assert.Equal(2, payloads.Count);
        Assert.All(payloads, p =>
        {
            Assert.Equal(dream.Id, p.GetProperty("dreamId").GetInt64());
            Assert.Equal("shelved", p.GetProperty("to").GetString());
            Assert.Equal(h.Shelf.Id, p.GetProperty("toId").GetInt32());
        });
        Assert.Equal(1, payloads.Count(p => p.GetProperty("root").GetBoolean()));
    }

    [Fact]
    public async Task NoKeys_ShelvesTheRootAlone()
    {
        var h = await NewAsync();

        var result = Created(await h.Controller.Remand(h.Root.Key, new DreamRemandRequest(h.Shelf.Id, []), default));

        Assert.Equal([h.Root.Key], result.Changed);
        Assert.Empty(result.Unchanged);
        Assert.Equal([h.Root.Key], result.Dream.RemandedFrom);
        h.Db.ChangeTracker.Clear();
        Assert.Equal(h.Open.Id, (await h.Db.Issues.SingleAsync(i => i.Id == h.Story.Id)).StatusId);
    }

    [Fact]
    public async Task ARootAlreadyInTheColumn_IsStillRemanded_ButMovesNowhere()
    {
        var h = await NewAsync();
        await h.Controller.Remand(h.Root.Key, new DreamRemandRequest(h.Shelf.Id), default);

        var second = Created(await h.Controller.Remand(h.Root.Key, new DreamRemandRequest(h.Shelf.Id), default));

        Assert.Empty(second.Changed);
        Assert.Equal([h.Root.Key], second.Unchanged);
        Assert.Equal([h.Root.Key], second.Dream.RemandedFrom);

        var kinds = await h.EventKindsAsync(h.Root.Id);
        Assert.Equal(2, kinds.Count(k => k == EfHatchIssueEvent.Remanded));
        Assert.Equal(1, kinds.Count(k => k == EfHatchIssueEvent.StatusChanged));
    }

    [Fact]
    public async Task ARootInAClosedColumn_IsRemandedAndMoved()
    {
        var h = await NewAsync();
        var closed = await h.SeedAsync("epic", "finished", h.Closed, parentId: null);

        var result = Created(await h.Controller.Remand(closed.Key, new DreamRemandRequest(h.Shelf.Id), default));

        Assert.Equal([closed.Key], result.Changed);
        h.Db.ChangeTracker.Clear();
        var moved = await h.Db.Issues.SingleAsync(i => i.Id == closed.Id);
        Assert.Equal(h.Shelf.Id, moved.StatusId);
        Assert.Null(moved.ClosedAt);
    }

    [Fact]
    public async Task TheMovedIssues_LandAtTheBottomOfTheColumn_InTheOrderTheyWereNamed()
    {
        var h = await NewAsync();

        Created(await h.Controller.Remand(h.Root.Key, new DreamRemandRequest(h.Shelf.Id, [h.Other.Key, h.Task.Key]), default));

        h.Db.ChangeTracker.Clear();
        var ranks = await h.Db.Issues.Where(i => i.StatusId == h.Shelf.Id).ToDictionaryAsync(i => i.Id, i => i.Rank);
        Assert.True(ranks[h.Root.Id] < ranks[h.Other.Id]);
        Assert.True(ranks[h.Other.Id] < ranks[h.Task.Id]);
    }

    [Fact]
    public async Task AnIssueWaitingOnTheRoot_IsToldOnce()
    {
        var h = await NewAsync();
        var waiting = await h.SeedAsync("task", "waits on the root", h.Open, parentId: null);
        h.Db.Dependencies.Add(new EfHatchIssueDependency { IssueId = waiting.Id, DependsOnId = h.Root.Id, CreatedAt = Now, CreatedBy = "a person" });
        await h.Db.SaveChangesAsync();

        Created(await h.Controller.Remand(h.Root.Key, new DreamRemandRequest(h.Shelf.Id, [h.Story.Key]), default));

        var told = await h.Db.Comments.Where(c => c.IssueId == waiting.Id && c.Body.Contains("has been deferred")).ToListAsync();
        Assert.Single(told);
    }

    // ---- What is refused ----

    [Fact]
    public async Task AKeyThatIsNotADescendant_RefusesTheWholeRequest_AndWritesNoDream()
    {
        var h = await NewAsync();

        var result = await h.Controller.Remand(h.Root.Key, new DreamRemandRequest(h.Shelf.Id, [h.Story.Key, h.Stranger.Key]), default);

        var refused = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal($"{h.Stranger.Key} is not under {h.Root.Key}", refused.Value);
        await h.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task AKeyThatNamesNothing_IsRefusedTheSameWay()
    {
        var h = await NewAsync();

        var result = await h.Controller.Remand(h.Root.Key, new DreamRemandRequest(h.Shelf.Id, ["AER-9999"]), default);

        Assert.Equal($"AER-9999 is not under {h.Root.Key}", Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        await h.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task AColumnThatIsNotDeferred_RefusesTheWholeRequest()
    {
        var h = await NewAsync();

        var result = await h.Controller.Remand(h.Root.Key, new DreamRemandRequest(h.Closed.Id, [h.Story.Key]), default);

        var refused = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("\"finished\" is not a deferred column", (string)refused.Value!);
        await h.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task AColumnThatDoesNotExist_RefusesTheWholeRequest()
    {
        var h = await NewAsync();

        var result = await h.Controller.Remand(h.Root.Key, new DreamRemandRequest(9999), default);

        Assert.Equal("there is no column 9999", Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        await h.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task AKeyThatNamesNoIssue_IsNotFound()
    {
        var h = await NewAsync();

        Assert.IsType<NotFoundResult>((await h.Controller.Remand("AER-9999", new DreamRemandRequest(h.Shelf.Id), default)).Result);
        Assert.IsType<NotFoundResult>((await h.Controller.Remand("nonsense", new DreamRemandRequest(h.Shelf.Id), default)).Result);
    }

    [Fact]
    public async Task MoreThanFiveHundredKeys_AreRefused()
    {
        var h = await NewAsync();
        var keys = Enumerable.Range(1000, 501).Select(n => $"AER-{n}").ToList();

        var result = await h.Controller.Remand(h.Root.Key, new DreamRemandRequest(h.Shelf.Id, keys), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        await h.AssertNothingWrittenAsync();
    }

    // ---- What the dream reads back ----

    [Fact]
    public async Task TheDreamList_ReadsRemandedFrom_AndFiltersOnIt()
    {
        var h = await NewAsync();
        var made = Created(await h.Controller.Remand(h.Root.Key, new DreamRemandRequest(h.Shelf.Id, [h.Task.Key, h.Story.Key]), default)).Dream;
        var unrelated = CreatedDream(await h.Dreams.AddDream(new DreamCreateRequest("nothing to do with it"), default));

        var all = Value(await h.Dreams.GetDreams(null, null, 50, 0, null, false, false, null, default));
        Assert.Equal([h.Root.Key, h.Story.Key, h.Task.Key], all.Dreams.Single(d => d.Id == made.Id).RemandedFrom);
        Assert.Empty(all.Dreams.Single(d => d.Id == unrelated.Id).RemandedFrom);

        foreach (var key in new[] { h.Root.Key, h.Story.Key, h.Task.Key })
        {
            var only = Value(await h.Dreams.GetDreams(null, null, 50, 0, null, false, false, key, default));
            Assert.Equal([made.Id], only.Dreams.Select(d => d.Id));
        }

        var none = Value(await h.Dreams.GetDreams(null, null, 50, 0, null, false, false, h.Other.Key, default));
        Assert.Empty(none.Dreams);

        var gone = Value(await h.Dreams.GetDreams(null, null, 50, 0, null, false, false, "AER-9999", default));
        Assert.Empty(gone.Dreams);

        var bad = await h.Dreams.GetDreams(null, null, 50, 0, null, false, false, "nonsense", default);
        Assert.IsType<BadRequestObjectResult>(bad.Result);
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>An issue the harness filed: its id and the key it is read by.</summary>
    private sealed record Seeded(long Id, string Key);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required IssueRemandController Controller { get; init; }
        public required DreamsController Dreams { get; init; }
        public required StubActorDirectory Actors { get; init; }
        public required int ProjectId { get; init; }
        public required EfHatchStatus Open { get; init; }
        public required EfHatchStatus Closed { get; init; }
        public required EfHatchStatus Shelf { get; init; }

        public Seeded Root { get; set; } = null!;
        public Seeded Story { get; set; } = null!;
        public Seeded Task { get; set; } = null!;
        public Seeded Other { get; set; } = null!;
        public Seeded Done { get; set; } = null!;
        public Seeded Stranger { get; set; } = null!;

        private int next;

        public async Task<Seeded> SeedAsync(string type, string title, EfHatchStatus status, long? parentId, string description = "")
        {
            var issue = new EfHatchIssue
            {
                ProjectId = ProjectId,
                Number = ++next,
                Type = type,
                Title = title,
                Description = description,
                StatusId = status.Id,
                ParentId = parentId,
                Rank = next * RankService.Gap,
                CreatedBy = "a person",
                CreatedAt = Now,
                UpdatedAt = Now,
            };
            Db.Issues.Add(issue);
            await Db.SaveChangesAsync();
            return new Seeded(issue.Id, $"AER-{issue.Number}");
        }

        public async Task<List<string>> EventKindsAsync(long issueId) =>
            await Db.IssueEvents.Where(e => e.IssueId == issueId).Select(e => e.Kind).ToListAsync();

        public async Task<List<JsonElement>> RemandedPayloadsAsync() =>
            (await Db.IssueEvents.Where(e => e.Kind == EfHatchIssueEvent.Remanded).Select(e => e.Payload).ToListAsync())
                .Select(p => JsonDocument.Parse(p!).RootElement.Clone())
                .ToList();

        /// <summary>A refused remand leaves no dream, no link row, no remanded event, and every issue where it was.</summary>
        public async Task AssertNothingWrittenAsync()
        {
            Db.ChangeTracker.Clear();
            Assert.Empty(await Db.Dreams.ToListAsync());
            Assert.Empty(await Db.DreamRemands.ToListAsync());
            Assert.Empty(await Db.IssueEvents.Where(e => e.Kind == EfHatchIssueEvent.Remanded).ToListAsync());
            Assert.All(await Db.Issues.Where(i => i.Id != Done.Id).ToListAsync(), i => Assert.Equal(Open.Id, i.StatusId));
        }
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Add(project);
        var open = new EfHatchStatus { Name = "working", SortOrder = 10 };
        var closed = new EfHatchStatus { Name = "finished", SortOrder = 20, IsTerminal = true };
        var shelf = new EfHatchStatus { Name = "shelved", SortOrder = 30, IsDeferred = true };
        db.AddRange(open, closed, shelf);
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var caller = new StubCallerIdentity { Person = new EfPerson { Name = "a person", CreatedAt = Now, UpdatedAt = Now } };
        var actors = new StubActorDirectory();

        var h = new Harness
        {
            Db = db,
            Controller = new IssueRemandController(db, actors, new RankService(db), caller, time),
            Dreams = new DreamsController(db, caller, time),
            Actors = actors,
            ProjectId = project.Id,
            Open = open,
            Closed = closed,
            Shelf = shelf,
        };

        h.Root = await h.SeedAsync("epic", "the root", open, parentId: null, description: "what the root is for");
        h.Story = await h.SeedAsync("story", "a story", open, h.Root.Id);
        h.Task = await h.SeedAsync("task", "a task under the story", open, h.Story.Id);
        h.Other = await h.SeedAsync("story", "another story", open, h.Root.Id);
        h.Done = await h.SeedAsync("task", "a finished task", closed, h.Story.Id);
        h.Stranger = await h.SeedAsync("epic", "somewhere else", open, parentId: null);

        return h;
    }

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

    private static DreamRemandResultDto Created(ActionResult<DreamRemandResultDto> result) =>
        result.Result is CreatedResult created
            ? (DreamRemandResultDto)created.Value!
            : throw new InvalidOperationException($"expected a 201, got {result.Result}");

    private static DreamDto CreatedDream(ActionResult<DreamDto> result) =>
        result.Result is CreatedResult created
            ? (DreamDto)created.Value!
            : throw new InvalidOperationException($"expected a 201, got {result.Result}");
}

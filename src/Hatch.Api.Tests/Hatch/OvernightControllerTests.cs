using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <c>GET /api/hatch/overnight</c> and <c>POST /api/hatch/overnight/seen</c>,
/// wired against the in-memory database - the two queries and the
/// <see cref="Rollup"/> call that feed <see cref="Overnight.NewestNight"/>,
/// which <see cref="OvernightTests"/> already pins on its own, plus the
/// person-only write and the mark it leaves behind.
/// </summary>
public class OvernightControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AStatusCalledSomethingOtherThanDone_StillLandsARow_BecauseItIsTheFlagThatCounts()
    {
        var h = await NewAsync();
        await h.LandAsync("task", "shipped under an odd name", h.Shipped, Now);

        var result = await h.GetOvernightAsync();

        Assert.Equal(["AER-1"], result.Rows.Select(r => r.Key));
    }

    [Fact]
    public async Task AnIssueCurrentlyNonTerminal_NeverAppears_EvenWithAnOldEventThatLooksTerminalIsh()
    {
        var h = await NewAsync();
        var issue = await h.LandAsync("task", "landed, then reopened", h.Shipped, Now - TimeSpan.FromHours(1));
        await h.MoveAsync(issue, h.InProgress, Now, payload: "{\"to\":\"shipped\"}");

        var result = await h.GetOvernightAsync();

        Assert.Empty(result.Rows);
    }

    [Fact]
    public async Task AStoryWithThreeLandedTasks_ReportsADescendantCountOfThree()
    {
        var h = await NewAsync();
        var story = await h.LandAsync("story", "the story", h.Shipped, Now);
        await h.LandAsync("task", "a", h.Shipped, Now, parentId: story.Id);
        await h.LandAsync("task", "b", h.Shipped, Now, parentId: story.Id);
        await h.LandAsync("task", "c", h.Shipped, Now, parentId: story.Id);

        var result = await h.GetOvernightAsync();

        var storyRow = Assert.Single(result.Rows, r => r.Key == "AER-1");
        Assert.Equal(3, storyRow.DescendantCount);
    }

    [Fact]
    public async Task NoIssuesAtAll_AnswersAnEmptyNight_NotARefusal()
    {
        var h = await NewAsync();

        var result = await h.GetOvernightAsync();

        Assert.Equal(new OvernightDto(null, null, 0, []), result);
    }

    [Fact]
    public async Task IssuesButNoneTerminal_AnswersAnEmptyNight_NotARefusal()
    {
        var h = await NewAsync();
        await h.FileAsync("task", "not landed", h.InProgress);

        var result = await h.GetOvernightAsync();

        Assert.Equal(new OvernightDto(null, null, 0, []), result);
    }

    [Fact]
    public async Task AReaderWithNoMarkAtAll_SeesEveryRowUnseen()
    {
        var h = await NewAsync();
        await h.LandAsync("task", "a", h.Shipped, Now);
        await h.LandAsync("task", "b", h.Shipped, Now);

        var result = await h.GetOvernightAsync();

        Assert.Equal(result.Count, result.Unseen);
    }

    [Fact]
    public async Task PostSeen_ThenGetOvernight_ReportsNothingUnseen()
    {
        var h = await NewAsync();
        await h.LandAsync("task", "a", h.Shipped, Now);

        await h.PostSeenAsync();
        var result = await h.GetOvernightAsync();

        Assert.Equal(0, result.Unseen);
    }

    [Fact]
    public async Task ALandingAfterTheMark_MakesUnseenNonzeroAgain()
    {
        var h = await NewAsync();
        await h.LandAsync("task", "a", h.Shipped, Now);
        await h.PostSeenAsync();

        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.LandAsync("task", "b", h.Shipped, Now + TimeSpan.FromMinutes(1));

        var result = await h.GetOvernightAsync();

        Assert.Equal(1, result.Unseen);
    }

    [Fact]
    public async Task PostSeen_IsRefusedToAKey()
    {
        var h = await NewAsync();
        h.Caller.Key = new EfApiKey
        {
            Name = "hatch",
            Hash = [1],
            Prefix = "hatch_ak_x",
            Scopes = [ApiKeyScopes.Hatch],
            CreatedAt = Now,
        };

        var refused = Assert.IsType<ObjectResult>(await h.Controller.PostSeen(CancellationToken.None));

        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Equal("marking the night as seen is the operator's, not an agent's", refused.Value);
    }

    [Fact]
    public async Task PostSeen_IsRefusedToAKeylessRunnerToo()
    {
        var h = await NewAsync();
        h.Caller.Local = new Actor(ActorKind.Key, LocalCaller.RunnerIdFor("host:/src"), "host:/src");

        var refused = Assert.IsType<ObjectResult>(await h.Controller.PostSeen(CancellationToken.None));

        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Equal("marking the night as seen is the operator's, not an agent's", refused.Value);
    }

    /// <summary>
    /// The wall off, nobody local either (HA-1425): not a program, so
    /// <see cref="OvernightController.PostSeen"/> gets past its own gate, and
    /// then finds no principal to mark the night seen for. A 403, not a
    /// null-reference throw.
    /// </summary>
    [Fact]
    public async Task PostSeen_WithNoPrincipalAtAll_IsRefused_NotThrown()
    {
        var h = await NewAsync();
        h.Actors.Principal = null;

        var refused = Assert.IsType<ObjectResult>(await h.Controller.PostSeen(CancellationToken.None));

        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Equal("marking the night as seen needs somebody signed in", refused.Value);
        Assert.Empty(await h.Db.OvernightSeen.ToListAsync());
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 10 };
        var inProgress = new EfHatchStatus { Name = "in progress", SortOrder = 20 };
        // Named deliberately unlike "Done" - IsTerminal is the flag the controller
        // reads, and this status proves it is not reading the name instead.
        var shipped = new EfHatchStatus { Name = "shipped", SortOrder = 30, IsTerminal = true };
        db.AddRange(project, todo, inProgress, shipped);
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var caller = new StubCaller();
        var actors = new StubActorDirectory();
        var person = actors.AddPerson("Ada");
        actors.Principal = person;

        return new Harness
        {
            Db = db,
            Controller = new OvernightController(db, caller, actors, time),
            Caller = caller,
            Actors = actors,
            Time = time,
            ProjectId = project.Id,
            InProgress = inProgress.Id,
            Shipped = shipped.Id,
        };
    }

    /// <summary>Whoever the test says is calling: a person, a key, or local mode's third lane.</summary>
    private sealed class StubCaller : ICallerIdentity
    {
        public EfPerson? Person { get; set; }

        public EfApiKey? Key { get; set; }

        public Actor? Local { get; set; }

        public Task<EfAuthGrant?> GrantAsync(CancellationToken ct) => Task.FromResult<EfAuthGrant?>(null);

        public Task<Guid?> PersonIdAsync(CancellationToken ct) => Task.FromResult(Person?.Id);

        public Task<EfPerson?> PersonAsync(CancellationToken ct) => Task.FromResult(Person);

        public Task<EfApiKey?> ApiKeyAsync(CancellationToken ct) => Task.FromResult(Key);

        public Task<Actor?> LocalAsync(CancellationToken ct) => Task.FromResult(Local);

        public Task<bool> IsProgramAsync(CancellationToken ct) =>
            Task.FromResult(Key is not null || Local is { Kind: ActorKind.Key });

        public Task<string> ActorNameAsync(CancellationToken ct) =>
            Task.FromResult(Person?.Name ?? Key?.Name ?? Local?.Name ?? CallerIdentity.Unattributed);
    }

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required OvernightController Controller { get; init; }
        public required StubCaller Caller { get; init; }
        public required StubActorDirectory Actors { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }
        public required int InProgress { get; init; }
        public required int Shipped { get; init; }

        private int next = 1;

        public async Task<EfHatchIssue> FileAsync(
            string type, string title, int statusId, long? parentId = null)
        {
            var issue = new EfHatchIssue
            {
                ProjectId = ProjectId,
                Number = next++,
                Type = type,
                Title = title,
                Description = "",
                StatusId = statusId,
                Rank = 1024,
                ParentId = parentId,
                CreatedBy = "operator",
                CreatedAt = Now,
                UpdatedAt = Now,
            };

            Db.Issues.Add(issue);
            await Db.SaveChangesAsync();
            return issue;
        }

        /// <summary>Files the issue already in <paramref name="statusId"/> and writes the one
        /// <c>status_changed</c> event <see cref="OvernightController"/> reads the landing instant
        /// off of - <see cref="FileAsync"/> alone sets the column but writes no event.</summary>
        public async Task<EfHatchIssue> LandAsync(
            string type, string title, int statusId, DateTimeOffset at, long? parentId = null)
        {
            var issue = await FileAsync(type, title, statusId, parentId);
            await EventAsync(issue, at);
            return issue;
        }

        /// <summary>Moves an already-filed issue to another status, with its own event - for a
        /// second move that leaves an earlier, terminal-looking event behind on the trail.</summary>
        public async Task MoveAsync(EfHatchIssue issue, int statusId, DateTimeOffset at, string payload = "{}")
        {
            issue.StatusId = statusId;
            await EventAsync(issue, at, payload);
        }

        private async Task EventAsync(EfHatchIssue issue, DateTimeOffset at, string payload = "{}")
        {
            Db.IssueEvents.Add(new EfHatchIssueEvent
            {
                IssueId = issue.Id,
                Actor = "operator",
                Kind = EfHatchIssueEvent.StatusChanged,
                Payload = payload,
                At = at,
            });

            await Db.SaveChangesAsync();
        }

        public async Task<OvernightDto> GetOvernightAsync() =>
            Value(await Controller.GetOvernight(CancellationToken.None));

        public async Task PostSeenAsync() =>
            Assert.IsType<NoContentResult>(await Controller.PostSeen(CancellationToken.None));
    }

    private static T Value<T>(Microsoft.AspNetCore.Mvc.ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result?.GetType().Name ?? "nothing"}");
}

using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The tags an issue's own row carries, as every read that draws the issue
/// reports them: a list, never null, empty where the row has none.
/// </summary>
public class IssueRunnerTagsControllerTests
{
    // ---- Reading it ----

    [Fact]
    public async Task AFiledIssue_ReadsNoOwnTags_NotNull_ThroughEveryRead()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");

        Assert.Empty((await h.ReadAsync(issue.Key)).RunnerTagsOwn);
        Assert.Empty((await h.CardAsync(issue.Key)).RunnerTagsOwn);
        Assert.Empty((await h.HitAsync(issue.Key)).RunnerTagsOwn);
    }

    [Fact]
    public async Task ARowWithTagsSetOnTheEntity_ReadsThemThroughEveryRead()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "a thing");
        await h.SetColumnAsync(issue.Key, ["gpu"]);

        Assert.Equal(new[] { "gpu" }, (await h.ReadAsync(issue.Key)).RunnerTagsOwn);
        Assert.Equal(new[] { "gpu" }, (await h.CardAsync(issue.Key)).RunnerTagsOwn);
        Assert.Equal(new[] { "gpu" }, (await h.HitAsync(issue.Key)).RunnerTagsOwn);
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required IssuesController Issues { get; init; }
        public required SearchController Search { get; init; }
        public required int ProjectId { get; init; }

        public async Task<IssueDto> FileAsync(string type, string title) =>
            Created(await Issues.CreateIssue(new IssueCreateRequest(ProjectId, type, title, null, null, null, null), default));

        public async Task<IssueDto> ReadAsync(string key) => Value(await Issues.GetIssue(key, default));

        /// <summary>The card the issue list draws for <paramref name="key"/>.</summary>
        public async Task<IssueCardDto> CardAsync(string key) =>
            Value(await Issues.SearchIssues(null, null, null, null, null, null, default)).Single(c => c.Key == key);

        /// <summary>The card the search draws for <paramref name="key"/>, with no query - the arm InMemory can run.</summary>
        public async Task<IssueCardDto> HitAsync(string key) =>
            Value(await Search.GetSearch(null, null, null, null, null, null, null, null, 50, 0, default))
                .Hits.Single(h => h.Card.Key == key).Card;

        /// <summary>Straight onto the row - nothing writes the column yet, so the test does.</summary>
        public async Task SetColumnAsync(string key, IReadOnlyList<string> tags)
        {
            IssueKey.TryParse(key, out var projectKey, out var number);
            var issue = await Db.Issues.WithKey(projectKey, number).FirstAsync();
            issue.RunnerTags = RunnerTagColumn.Write(tags);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Add(project);
        db.Add(new EfHatchStatus { Name = "inbox", SortOrder = 10 });
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var caller = new StubCallerIdentity { Person = new EfPerson { Name = "a person", CreatedAt = Now, UpdatedAt = Now } };
        var actors = new StubActorDirectory();

        return new Harness
        {
            Db = db,
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time, new ProjectAccess(db, actors)),
            Search = new SearchController(db, actors, TestClaims.With(), time),
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

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result}");

    private static T Created<T>(ActionResult<T> result) =>
        result.Result is CreatedAtActionResult created
            ? (T)created.Value!
            : result.Value ?? throw new InvalidOperationException($"expected a created value, got {result.Result}");
}

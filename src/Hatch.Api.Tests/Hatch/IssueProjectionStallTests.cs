using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// Nothing writes <see cref="EfHatchIssue.StalledAt"/>,
/// <see cref="EfHatchIssue.StalledWhy"/> or <see cref="EfHatchIssue.Held"/> yet
/// (that is HA-351's job), so this pins only that the two DTOs carry whatever
/// is already on the row.
/// </summary>
public class IssueProjectionStallTests
{
    [Fact]
    public async Task TheIssueDto_CarriesAllThree()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.MarkAsync(issue.Key, h.Now, "left where it was found", true);

        var read = Value(await h.Issues.GetIssue(issue.Key, default));

        Assert.Equal(h.Now, read.StalledAt);
        Assert.Equal("left where it was found", read.StalledWhy);
        Assert.True(read.Held);
        Assert.Equal(TestClaims.With().StallResumeSeconds, read.StallResumeSeconds);
    }

    [Fact]
    public async Task TheBoardCard_CarriesAllThree()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.MarkAsync(issue.Key, h.Now, "left where it was found", true);

        var board = Value(await h.Board.GetBoard(default));
        var card = board.Issues.Single(c => c.Key == issue.Key);

        Assert.Equal(h.Now, card.StalledAt);
        Assert.Equal("left where it was found", card.StalledWhy);
        Assert.True(card.Held);
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required IssuesController Issues { get; init; }
        public required BoardController Board { get; init; }
        public required int ProjectId { get; init; }
        public DateTimeOffset Now => IssueProjectionStallTests.Now;

        public async Task<IssueDto> FileAsync(string type = "task", string title = "a thing") =>
            Created(await Issues.CreateIssue(new IssueCreateRequest(ProjectId, type, title, null, null, null, null), default));

        /// <summary>
        /// Nothing sets these fields yet, so the test sets them directly on
        /// the tracked row rather than through a route that does not exist.
        /// </summary>
        public async Task MarkAsync(string key, DateTimeOffset stalledAt, string why, bool held)
        {
            IssueKey.TryParse(key, out var projectKey, out var number);
            var row = await Db.Issues.WithKey(projectKey, number).FirstAsync();
            row.StalledAt = stalledAt;
            row.StalledWhy = why;
            row.Held = held;
            await Db.SaveChangesAsync();
        }
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var hatch = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Add(hatch);
        var inbox = new EfHatchStatus { Name = "inbox", SortOrder = 10 };
        db.Add(inbox);
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var caller = new StubCallerIdentity { Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now } };
        var actors = new StubActorDirectory();

        return new Harness
        {
            Db = db,
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time),
            Board = new BoardController(db, actors, TestClaims.With(), time),
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

    private static string Reason(IActionResult? result) => result switch
    {
        ObjectResult o => o.Value?.ToString() ?? $"{o.StatusCode}",
        StatusCodeResult s => s.StatusCode.ToString(),
        null => "no result",
        _ => result.GetType().Name,
    };
}

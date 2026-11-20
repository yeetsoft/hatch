using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// An answer written by a key reads back as <c>"key"</c>, one written by a
/// person reads back as <c>"person"</c>, and a row with the column left
/// unset reads back as neither - from both the comment thread and the
/// question routes.
/// </summary>
public class CommentAuthorKindTests
{
    [Fact]
    public async Task AnAnswerWrittenByAKey_ReadsKeyFromThreadAndQuestions()
    {
        var h = await NewAsync();
        var key = await h.FileAsync();
        var id = await h.AskAsync(key);

        h.Caller.Local = new Actor(ActorKind.Key, Guid.NewGuid(), "a-runner");
        Created(await h.Thread.AddComment(key, new CommentCreateRequest("Take this one", "answer", id), default));

        Assert.Equal("key", Value(await h.Thread.GetComments(key, default)).Single(c => c.AnswersId == id).AuthorKind);
        var answers = Value(await h.Questions.GetIssueQuestions(key, false, default)).Single().Answers;
        Assert.Equal("key", answers.Single().AuthorKind);
    }

    [Fact]
    public async Task AnAnswerWrittenByAPerson_ReadsPersonFromThreadAndQuestions()
    {
        var h = await NewAsync();
        var key = await h.FileAsync();
        var id = await h.AskAsync(key);

        Created(await h.Thread.AddComment(key, new CommentCreateRequest("Take that one", "answer", id), default));

        Assert.Equal("person", Value(await h.Thread.GetComments(key, default)).Single(c => c.AnswersId == id).AuthorKind);
        var answers = Value(await h.Questions.GetIssueQuestions(key, false, default)).Single().Answers;
        Assert.Equal("person", answers.Single().AuthorKind);
    }

    [Fact]
    public async Task ARowWithTheColumnLeftUnset_ReadsNull()
    {
        var h = await NewAsync();
        var key = await h.FileAsync();
        var id = Created(await h.Thread.AddComment(key, new CommentCreateRequest("just a note"), default)).Id;

        var row = await h.Db.Comments.SingleAsync(c => c.Id == id);
        row.AuthorKind = null;
        await h.Db.SaveChangesAsync();

        Assert.Null(Value(await h.Thread.GetComments(key, default)).Single(c => c.Id == id).AuthorKind);
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required StubCallerIdentity Caller { get; init; }
        public required QuestionsController Questions { get; init; }
        public required IssuesController Issues { get; init; }
        public required IssueThreadController Thread { get; init; }
        public required int ProjectId { get; init; }

        public async Task<string> FileAsync() =>
            Created(await Issues.CreateIssue(new IssueCreateRequest(ProjectId, "task", "a thing", null, null, null, null), default)).Key;

        public async Task<long> AskAsync(string key) =>
            Created(await Thread.AddComment(key, new CommentCreateRequest("Which way?", "question"), default)).Id;
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Add(project);
        db.Add(new EfHatchStatus { Name = "inbox", SortOrder = 10 });
        db.Add(new EfHatchStatus { Name = "done", SortOrder = 20, IsTerminal = true });
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var caller = new StubCallerIdentity { Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now } };
        var actors = new StubActorDirectory();

        return new Harness
        {
            Db = db,
            Caller = caller,
            Questions = new QuestionsController(db),
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time, new ProjectAccess(db, actors)),
            Thread = new IssueThreadController(db, caller, time),
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

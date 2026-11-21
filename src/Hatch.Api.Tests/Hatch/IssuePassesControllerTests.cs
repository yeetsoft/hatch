using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// An ambition's passes, as data: one entry per work log row that ran from
/// the implementation column and was not dropped, with the verdicts and
/// comments timed inside it, and a loose entry for a verdict no row's window
/// holds.
/// </summary>
public class IssuePassesControllerTests
{
    [Fact]
    public async Task TwoPassRows_WithVerdictsInsideEach_AnswerTwoEntries_NewestFirst_EachWithItsOwnVerdictsShaAndComments()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var cap = await h.AddCapabilityAsync(ambition, "a", "a?");

        var row1 = await h.PassRowAsync(ambition, Hour(0), Hour(1));
        await h.VerdictAtAsync(cap, EfHatchCapabilityVerdict.DoesNotHold, "sha1", Hour(0).AddMinutes(30));
        var comment1 = await h.CommentAsync(ambition, "during the first pass", Hour(0).AddMinutes(20));

        var row2 = await h.PassRowAsync(ambition, Hour(2), Hour(3));
        await h.VerdictAtAsync(cap, EfHatchCapabilityVerdict.Holds, "sha2", Hour(2).AddMinutes(30));
        var comment2 = await h.CommentAsync(ambition, "during the second pass", Hour(2).AddMinutes(20));

        var passes = await h.GetAsync(ambition);

        Assert.Equal(2, passes.Length);
        var newer = passes[0];
        var older = passes[1];
        Assert.Equal(row2.Id, newer.RowId);
        Assert.Equal(row1.Id, older.RowId);

        Assert.Equal("sha2", newer.Sha);
        Assert.Equal("sha1", older.Sha);

        var newerVerdict = Assert.Single(newer.Verdicts);
        Assert.Equal(EfHatchCapabilityVerdict.Holds, newerVerdict.Verdict);
        Assert.True(newerVerdict.Changed);

        var olderVerdict = Assert.Single(older.Verdicts);
        Assert.Equal(EfHatchCapabilityVerdict.DoesNotHold, olderVerdict.Verdict);
        Assert.False(olderVerdict.Changed);

        Assert.Equal(comment2.Id, Assert.Single(newer.Comments).Id);
        Assert.Equal(comment1.Id, Assert.Single(older.Comments).Id);
    }

    [Fact]
    public async Task AnEpicRow_ReleasedWorked_AnswersWithTheFlagFalse_NoVerdicts_CarryingItsComment()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");

        var row = await h.PassRowAsync(ambition, Hour(0), Hour(1), type: "epic", to: h.Review);
        var comment = await h.CommentAsync(ambition, "an epic's own note", Hour(0).AddMinutes(10));

        var entry = Assert.Single(await h.GetAsync(ambition));

        Assert.Equal(row.Id, entry.RowId);
        Assert.False(entry.CanRecordVerdicts);
        Assert.Empty(entry.Verdicts);
        Assert.Equal(comment.Id, Assert.Single(entry.Comments).Id);
    }

    [Fact]
    public async Task AVerdictWithNoRowAround_AnswersAsAnEntryOfItsOwn_WithNoSession()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var cap = await h.AddCapabilityAsync(ambition, "a", "a?");

        await h.VerdictAtAsync(cap, EfHatchCapabilityVerdict.Holds, "abc", Hour(5));

        var entry = Assert.Single(await h.GetAsync(ambition));

        Assert.Null(entry.RowId);
        Assert.Null(entry.SessionId);
        Assert.Equal(Hour(5), entry.StartedAt);
        Assert.Equal(Hour(5), entry.EndedAt);
        Assert.Single(entry.Verdicts);
    }

    [Fact]
    public async Task ARowReleasedDropped_AndARowFromAnyOtherColumn_DoNotAppear()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");

        await h.PassRowAsync(ambition, Hour(0), Hour(1), outcome: ClaimOutcomes.Dropped);
        await h.PassRowAsync(ambition, Hour(2), Hour(3), from: h.Todo, to: h.Doing);

        Assert.Empty(await h.GetAsync(ambition));
    }

    [Fact]
    public async Task ARowNamingAChange_DoesNotAppear()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");

        await h.PassRowAsync(ambition, Hour(0), Hour(1), changeEventId: 1);

        Assert.Empty(await h.GetAsync(ambition));
    }

    [Fact]
    public async Task ARowReleasedWithNoOutcome_Counts()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");

        var row = await h.PassRowAsync(ambition, Hour(0), Hour(1), outcome: null);

        var entry = Assert.Single(await h.GetAsync(ambition));
        Assert.Equal(row.Id, entry.RowId);
    }

    [Fact]
    public async Task ACapabilityJudgedTwiceInOneRow_KeepsTheNewer()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var cap = await h.AddCapabilityAsync(ambition, "a", "a?");

        await h.PassRowAsync(ambition, Hour(0), Hour(1));
        await h.VerdictAtAsync(cap, EfHatchCapabilityVerdict.CannotTell, "first", Hour(0).AddMinutes(10));
        await h.VerdictAtAsync(cap, EfHatchCapabilityVerdict.Holds, "second", Hour(0).AddMinutes(20));

        var entry = Assert.Single(await h.GetAsync(ambition));
        var verdict = Assert.Single(entry.Verdicts);
        Assert.Equal(EfHatchCapabilityVerdict.Holds, verdict.Verdict);
        Assert.Equal("second", entry.Sha);
    }

    [Fact]
    public async Task VerdictsWithDifferingShas_LeaveTheEntrysShaNull()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var a = await h.AddCapabilityAsync(ambition, "a", "a?");
        var b = await h.AddCapabilityAsync(ambition, "b", "b?");

        await h.PassRowAsync(ambition, Hour(0), Hour(1));
        await h.VerdictAtAsync(a, EfHatchCapabilityVerdict.Holds, "one", Hour(0).AddMinutes(10));
        await h.VerdictAtAsync(b, EfHatchCapabilityVerdict.Holds, "two", Hour(0).AddMinutes(20));

        var entry = Assert.Single(await h.GetAsync(ambition));
        Assert.Null(entry.Sha);
    }

    [Fact]
    public async Task NoImplementationColumn_GivesLooseEntriesOnly()
    {
        var h = await NewAsync(withImplementation: false);
        var ambition = await h.FileAsync("ambition");
        var cap = await h.AddCapabilityAsync(ambition, "a", "a?");

        await h.VerdictAtAsync(cap, EfHatchCapabilityVerdict.Holds, "abc", Hour(1));

        var entry = Assert.Single(await h.GetAsync(ambition));
        Assert.Null(entry.RowId);
    }

    [Theory]
    [InlineData("task")]
    [InlineData("story")]
    [InlineData("epic")]
    public async Task AnIssueThatIsNotAnAmbition_Answers400_NamingItsType(string type)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync(type);

        var result = await h.Controller.GetPasses(issue, default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal($"only an ambition has passes - {issue} is a {type}", bad.Value);
    }

    [Fact]
    public async Task AnUnknownKey_Is404()
    {
        var h = await NewAsync();

        var result = await h.Controller.GetPasses("AER-999", default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ---- Harness ----

    private static DateTimeOffset Hour(int n) => Now.AddHours(n);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required IssuePassesController Controller { get; init; }
        public required IssuesController Issues { get; init; }
        public required int ProjectId { get; init; }
        public required int Todo { get; init; }
        public required int Doing { get; init; }
        public required int Review { get; init; }

        public async Task<string> FileAsync(string type)
        {
            var result = await Issues.CreateIssue(new IssueCreateRequest(ProjectId, type, $"a {type}", null, null, null, null), default);
            var created = result.Result is CreatedAtActionResult createdAt ? (IssueDto)createdAt.Value! : result.Value!;
            return created.Key;
        }

        public async Task<long> AddCapabilityAsync(string key, string statement, string howToTell)
        {
            var issue = await IssueAsync(key);
            var position = await Db.Capabilities.Where(c => c.IssueId == issue.Id).CountAsync() + 1;
            var cap = new EfHatchCapability
            {
                IssueId = issue.Id, Position = position, Statement = statement, HowToTell = howToTell,
                CreatedBy = "a person", CreatedAt = Now, UpdatedAt = Now,
            };
            Db.Capabilities.Add(cap);
            await Db.SaveChangesAsync();
            return cap.Id;
        }

        public async Task<EfHatchWorkLogEntry> PassRowAsync(
            string key, DateTimeOffset start, DateTimeOffset end, string type = "ambition",
            int? from = null, int? to = null, string? outcome = ClaimOutcomes.Worked, long? changeEventId = null)
        {
            var issue = await IssueAsync(key);
            var row = new EfHatchWorkLogEntry
            {
                IssueId = issue.Id, SessionId = Guid.NewGuid().ToString("N"), StartedAt = start, EndedAt = end,
                DurationMs = (long)(end - start).TotalMilliseconds, CreatedAt = end,
                IssueType = type, PlaybookFromStatusId = from ?? Doing, PlaybookToStatusId = to ?? Doing, Outcome = outcome,
                ChangeEventId = changeEventId, Title = "a pass", Summary = "what it did",
            };
            Db.WorkLog.Add(row);
            await Db.SaveChangesAsync();
            return row;
        }

        public async Task VerdictAtAsync(long capabilityId, string verdict, string sha, DateTimeOffset at)
        {
            Db.CapabilityVerdicts.Add(new EfHatchCapabilityVerdict
            {
                CapabilityId = capabilityId, Verdict = verdict, LookedAt = "the code", Sha = sha, Author = "a person", CreatedAt = at,
            });
            await Db.SaveChangesAsync();
        }

        public async Task<EfHatchComment> CommentAsync(string key, string body, DateTimeOffset at)
        {
            var issue = await IssueAsync(key);
            var comment = new EfHatchComment { IssueId = issue.Id, Author = "a person", Body = body, CreatedAt = at };
            Db.Comments.Add(comment);
            await Db.SaveChangesAsync();
            return comment;
        }

        public async Task<AmbitionPassDto[]> GetAsync(string key) => Value(await Controller.GetPasses(key, default));

        private async Task<EfHatchIssue> IssueAsync(string key)
        {
            Assert.True(IssueKey.TryParse(key, out var projectKey, out var number));
            return await Db.Issues.WithKey(projectKey, number).SingleAsync();
        }
    }

    private static async Task<Harness> NewAsync(bool withImplementation = true)
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Add(project);

        int todoId, doingId, reviewId;
        if (withImplementation)
        {
            var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
            var doing = new EfHatchStatus { Name = "doing", SortOrder = 30, IsImplementation = true };
            var review = new EfHatchStatus { Name = "review", SortOrder = 40 };
            var done = new EfHatchStatus { Name = "done", SortOrder = 90, IsTerminal = true };
            db.AddRange(todo, doing, review, done);
            await db.SaveChangesAsync();
            todoId = todo.Id;
            doingId = doing.Id;
            reviewId = review.Id;
        }
        else
        {
            // A board with no implementation column, and no way for the board's
            // own fallback measurement to find one either: one open column alone.
            var only = new EfHatchStatus { Name = "open", SortOrder = 10 };
            db.Add(only);
            await db.SaveChangesAsync();
            todoId = doingId = reviewId = only.Id;
        }

        var actors = new StubActorDirectory();
        var caller = new StubCallerIdentity { Person = new EfPerson { Name = "a person", CreatedAt = Now, UpdatedAt = Now } };
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Now);

        return new Harness
        {
            Db = db,
            Controller = new IssuePassesController(db),
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time, new ProjectAccess(db, actors)),
            ProjectId = project.Id,
            Todo = todoId,
            Doing = doingId,
            Review = reviewId,
        };
    }

    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

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
}

using System.Text;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="ChangelogFileController"/>: the put's validation, the board's own
/// judgement of the bytes, the one row per canonical and trunk, and the task a
/// gap files - modelled on <see cref="DependencyFileControllerTests"/>.
/// </summary>
public class ChangelogFileControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private const string Remote = "git@forge.example:owner/repo.git";
    private const string Canonical = "forge.example/owner/repo";
    private const string Trunk = "main";
    private const string Runner = "box:/work/repo";
    private const string Sha1 = "1111111111111111111111111111111111111111";
    private const string Sha2 = "2222222222222222222222222222222222222222";
    private const string Origin = "https://hatch.test";

    [Fact]
    public void TheController_IsOpenToAKey()
    {
        var guard = typeof(ChangelogFileController)
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .Single();

        Assert.True(guard.AcceptKey);
    }

    // ---- Every refusal ----

    [Fact]
    public async Task ABadRemote_IsRefused()
    {
        var h = await NewAsync();

        Assert.Equal("a remote can't be empty", Reason(await h.PutAsync(h.Request(null, Sha1) with { Remote = "  " })));
        Assert.Contains("remote", Reason(await h.PutAsync(h.Request(null, Sha1) with { Remote = "nonsense" })));
        Assert.Empty(await h.Db.ChangelogFiles.ToListAsync());
    }

    [Fact]
    public async Task AnEmptyOrOverlongTrunkShaOrRunner_IsRefused()
    {
        var h = await NewAsync();

        Assert.Equal("a changelog file names the trunk it is about", Reason(await h.PutAsync(h.Request(null, Sha1) with { Trunk = "" })));
        Assert.Contains("at most", Reason(await h.PutAsync(h.Request(null, Sha1) with { Trunk = new string('t', EfHatchChangelogFile.MaxRefLength + 1) })));
        Assert.Equal("a changelog file names the sha it is about", Reason(await h.PutAsync(h.Request(null, ""))));
        Assert.Contains("at most", Reason(await h.PutAsync(h.Request(null, new string('a', EfHatchChangelogFile.MaxShaLength + 1)))));
        Assert.Equal("a changelog file names the runner that took it", Reason(await h.PutAsync(h.Request(null, Sha1) with { Runner = " " })));
        Assert.Contains("at most", Reason(await h.PutAsync(h.Request(null, Sha1) with { Runner = new string('r', ClaimRequest.MaxRunnerLength + 1) })));
    }

    [Fact]
    public async Task ContentThatIsNotBase64_OrIsTooLarge_IsRefused()
    {
        var h = await NewAsync();

        Assert.Equal("content is not valid base64", Reason(await h.PutAsync(h.Request(null, Sha1) with { Content = "!!not base64!!" })));

        var big = Convert.ToBase64String(new byte[ChangelogFile.MaxFileBytes + 1]);
        Assert.Contains("at most 2 MB", Reason(await h.PutAsync(h.Request(null, Sha1) with { Content = big })));
        Assert.Empty(await h.Db.ChangelogFiles.ToListAsync());
    }

    [Fact]
    public async Task ARemoteNoProjectElects_IsNotFound_NamingTheProjectsPage()
    {
        var h = await NewAsync();

        var result = await h.PutAsync(h.Request(null, Sha1) with { Remote = "git@forge.example:owner/other.git" });

        var missing = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Contains("Projects page", missing.Value!.ToString());
        Assert.Empty(await h.Db.ChangelogFiles.ToListAsync());
    }

    [Fact]
    public async Task ARemoteWhoseBoxIsOff_IsNotFound()
    {
        var h = await NewAsync(elect: false);

        Assert.IsType<NotFoundObjectResult>((await h.PutAsync(h.Request(null, Sha1))).Result);
    }

    // ---- The judgement ----

    [Fact]
    public async Task TheBoardsOwnBytes_AreStoredCurrent_WithTheirHash_AndFileNothing()
    {
        var h = await NewAsync();
        await h.CloseAsync(1, 2, 3);

        var row = Value(await h.PutAsync(h.Request(await h.ProjectionAsync(), Sha1)));

        Assert.Equal(ChangelogFile.CurrentState, row.State);
        Assert.Null(row.Behind);
        Assert.Equal(64, row.Hash!.Length);
        Assert.Equal(Canonical, row.Canonical);
        Assert.Equal(Remote, row.Remote);
        Assert.Equal(Runner, row.Runner);
        Assert.Equal("Nathan", row.CheckedBy);
        Assert.Empty(await h.Db.ChangelogTasks.ToListAsync());
    }

    [Fact]
    public async Task ATrunkMissingTwoEntries_IsBehindByTwo_AndOneMissingIsBehindByOne()
    {
        var h = await NewAsync();
        await h.CloseAsync(1, 2, 3);
        var full = await h.ProjectionAsync();

        var two = Value(await h.PutAsync(h.Request(Cut(full, 1), Sha1)));
        Assert.Equal(ChangelogFile.BehindState, two.State);
        Assert.Equal(2, two.Behind);

        var one = Value(await h.PutAsync(h.Request(Cut(full, 2), Sha2)));
        Assert.Equal(1, one.Behind);
        Assert.Equal(Sha2, one.Sha);
    }

    [Fact]
    public async Task ATrunkWithNoFile_IsAbsent()
    {
        var h = await NewAsync();
        await h.CloseAsync(1);

        var row = Value(await h.PutAsync(h.Request(null, Sha1)));

        Assert.Equal(ChangelogFile.AbsentState, row.State);
        Assert.Null(row.Hash);
    }

    [Fact]
    public async Task AFileWithAnotherHeader_IsForeign_WithTheSentence()
    {
        var h = await NewAsync();
        await h.CloseAsync(1);

        var row = Value(await h.PutAsync(h.Request(Encoding.UTF8.GetBytes("# mine\nversion: 1\nentries: []\n"), Sha1)));

        Assert.Equal(ChangelogFile.ForeignState, row.State);
        Assert.Contains("at 1111111", row.Problem);
        Assert.Contains("move or remove it before Hatch will write there", row.Problem);
    }

    [Fact]
    public async Task APutIsOneRowPerCanonicalAndTrunk_ReplacedInPlace_AndAnotherTrunkIsAnotherRow()
    {
        var h = await NewAsync();
        await h.CloseAsync(1);

        var first = Value(await h.PutAsync(h.Request(null, Sha1)));
        var second = Value(await h.PutAsync(h.Request(await h.ProjectionAsync(), Sha2) with { Remote = "https://forge.example/owner/repo" }));
        await h.PutAsync(h.Request(null, Sha1) with { Trunk = "develop" });

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(ChangelogFile.CurrentState, second.State);
        Assert.Equal("https://forge.example/owner/repo", second.Remote);
        Assert.Equal(2, await h.Db.ChangelogFiles.CountAsync());
    }

    [Fact]
    public async Task AUrlThatIsNotHttp_IsDropped()
    {
        var h = await NewAsync();

        Assert.Equal("https://forge.example/file", Value(await h.PutAsync(h.Request(null, Sha1) with { Url = "https://forge.example/file" })).Url);
        Assert.Null(Value(await h.PutAsync(h.Request(null, Sha1) with { Url = "javascript:alert(1)" })).Url);
    }

    // ---- Reading them back ----

    [Fact]
    public async Task Get_AnswersThePath_EveryRow_AndTheElectedCanonicals()
    {
        var h = await NewAsync();
        await h.CloseAsync(1);
        await h.PutAsync(h.Request(null, Sha1));
        h.Db.ProjectRepositories.Add(new EfHatchProjectRepository
        {
            ProjectId = h.ProjectId, Remote = "https://forge.example/a/off.git", Canonical = "forge.example/a/off", Changelog = false, CreatedAt = Now,
        });
        await h.Db.SaveChangesAsync();

        var got = Value(await h.Controller.GetChangelogFiles(["git@forge.example:owner/repo.git", "https://forge.example/a/off.git", "git@forge.example:z/z.git"], default));

        Assert.Equal(".hatch/changelog.yml", got.Path);
        Assert.Equal([Canonical], got.Elected);
        Assert.Equal(Canonical, Assert.Single(got.Files).Canonical);
        Assert.Equal(["git@forge.example:owner/repo.git"], got.ElectedRemotes);
    }

    // ---- The task a gap files ----

    [Fact]
    public async Task Behind_FilesOneTask_FromTheNewestEntryTheFileLacks()
    {
        var h = await NewAsync();
        await h.CloseAsync(1, 2, 3);

        await h.PutAsync(h.Request(Cut(await h.ProjectionAsync(), 1), Sha1));

        var task = Assert.Single(await h.TasksAsync());
        Assert.Equal("Record AER-3 in the changelog", task.Title);
        Assert.Equal(h.Todo, task.StatusId);
        Assert.Equal(Canonical, (await h.Db.ChangelogTasks.SingleAsync()).Canonical);
        Assert.Single(await h.Db.IssueEvents.Where(e => e.Kind == EfHatchIssueEvent.ChangelogTaskFiled).ToListAsync());
    }

    [Fact]
    public async Task Absent_FilesOneTask_FromTheNewestEntry()
    {
        var h = await NewAsync();
        await h.CloseAsync(1, 2);

        await h.PutAsync(h.Request(null, Sha1));

        Assert.Equal("Record AER-2 in the changelog", Assert.Single(await h.TasksAsync()).Title);
    }

    [Fact]
    public async Task AnAbsentFile_WithNothingShipped_FilesNothing()
    {
        var h = await NewAsync();

        await h.PutAsync(h.Request(null, Sha1));

        Assert.Empty(await h.Db.ChangelogTasks.ToListAsync());
    }

    [Fact]
    public async Task ABehindFileWhoseGapIsOnlyExtras_IsFiledFromTheNewestEntryAtAll()
    {
        var h = await NewAsync();
        await h.CloseAsync(1, 2);
        var full = await h.ProjectionAsync();
        await h.CloseAsync(3);
        var withExtra = await h.ProjectionAsync();
        h.Db.Issues.Remove(await h.Db.Issues.SingleAsync(i => i.Number == 3));
        await h.Db.SaveChangesAsync();

        await h.PutAsync(h.Request(withExtra, Sha1));

        Assert.Equal("Record AER-2 in the changelog", Assert.Single(await h.TasksAsync()).Title);
        Assert.NotEqual(full, withExtra);
    }

    [Theory]
    [InlineData("todo")]
    [InlineData("doing")]
    [InlineData("review")]
    [InlineData("shelf")]
    public async Task ATaskOpenInAnyColumn_DeferredIncluded_HoldsTheFiling(string column)
    {
        var h = await NewAsync();
        await h.CloseAsync(1, 2);
        await h.PutAsync(h.Request(null, Sha1));
        var existing = Assert.Single(await h.TasksAsync());
        existing.StatusId = h.Column(column);
        await h.Db.SaveChangesAsync();

        await h.PutAsync(h.Request(null, Sha2));

        Assert.Single(await h.Db.ChangelogTasks.ToListAsync());
    }

    [Fact]
    public async Task ADoneTask_DoesNotHoldTheFiling()
    {
        var h = await NewAsync();
        await h.CloseAsync(1, 2);
        await h.PutAsync(h.Request(null, Sha1));
        var existing = Assert.Single(await h.TasksAsync());
        existing.StatusId = h.Done;
        await h.Db.SaveChangesAsync();

        await h.PutAsync(h.Request(null, Sha2));

        Assert.Equal(2, await h.Db.ChangelogTasks.CountAsync());
    }

    [Fact]
    public async Task ASecondPutAtTheSameState_WithTheFirstTaskOpen_FilesNone()
    {
        var h = await NewAsync();
        await h.CloseAsync(1, 2);

        await h.PutAsync(h.Request(null, Sha1));
        await h.PutAsync(h.Request(null, Sha1));

        Assert.Single(await h.Db.ChangelogTasks.ToListAsync());
    }

    [Fact]
    public async Task ATaskForAnotherRepository_DoesNotHoldThisOnesFiling()
    {
        var h = await NewAsync();
        await h.CloseAsync(1);
        var story = await h.Db.Issues.SingleAsync();
        h.Db.ChangelogTasks.Add(new EfHatchChangelogTask
        {
            Issue = new EfHatchIssue
            {
                ProjectId = h.ProjectId, Number = 90, Type = "task", Title = "other", Description = "", StatusId = h.Todo,
                Rank = 1, CreatedBy = "x", CreatedAt = Now, UpdatedAt = Now,
            },
            SourceIssueId = story.Id, Canonical = "forge.example/owner/other", CreatedAt = Now,
        });
        await h.Db.SaveChangesAsync();

        await h.PutAsync(h.Request(null, Sha1));

        Assert.Equal(2, await h.Db.ChangelogTasks.CountAsync());
    }

    [Fact]
    public async Task AForeignFile_FilesNothing()
    {
        var h = await NewAsync();
        await h.CloseAsync(1, 2);

        await h.PutAsync(h.Request(Encoding.UTF8.GetBytes("# mine\n"), Sha1));

        Assert.Empty(await h.Db.ChangelogTasks.ToListAsync());
    }

    [Fact]
    public async Task ACurrentFile_FilesNothing_EvenWithNoTaskOpen()
    {
        var h = await NewAsync();
        await h.CloseAsync(1, 2);

        await h.PutAsync(h.Request(await h.ProjectionAsync(), Sha1));

        Assert.Empty(await h.Db.ChangelogTasks.ToListAsync());
    }

    // ---- Support ----

    /// <summary>The file with only its first <paramref name="keep"/> entries - what a trunk that has not caught up holds.</summary>
    private static byte[] Cut(byte[] full, int keep)
    {
        var lines = Encoding.UTF8.GetString(full).Split('\n');
        var starts = lines.Select((l, i) => (l, i)).Where(x => x.l.StartsWith("  - key:", StringComparison.Ordinal)).Select(x => x.i).ToList();
        return Encoding.UTF8.GetBytes(string.Join('\n', lines.Take(starts[keep])) + "\n");
    }

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required ChangelogFileController Controller { get; init; }
        public required int ProjectId { get; init; }
        public required int Todo { get; init; }
        public required int Doing { get; init; }
        public required int Review { get; init; }
        public required int Shelf { get; init; }
        public required int Done { get; init; }

        public int Column(string name) => name switch
        {
            "todo" => Todo, "doing" => Doing, "review" => Review, "shelf" => Shelf, _ => throw new ArgumentException(name),
        };

        public Task<ActionResult<ChangelogFileDto>> PutAsync(ChangelogFileRequest request) => Controller.PutChangelogFile(request, default);

        public ChangelogFileRequest Request(byte[]? content, string sha) =>
            new(Remote, Trunk, sha, content is null ? null : Convert.ToBase64String(content), null, Runner);

        /// <summary>The board's own file for the elected repository right now.</summary>
        public async Task<byte[]> ProjectionAsync() =>
            Encoding.UTF8.GetBytes(Changelog.RenderFile((await ChangelogFiles.ProjectAsync(Db, Canonical, default))!, Origin));

        public async Task CloseAsync(params int[] numbers)
        {
            var project = await Db.Projects.SingleAsync(p => p.Id == ProjectId);
            foreach (var number in numbers)
            {
                project.NextIssueNumber = Math.Max(project.NextIssueNumber, number + 1);
                Db.Issues.Add(new EfHatchIssue
                {
                    ProjectId = ProjectId, Number = number, Type = "story", Title = $"Ships {number}", Description = "",
                    StatusId = Done, Rank = number, CreatedBy = "operator", CreatedAt = Now, UpdatedAt = Now,
                    ClosedAt = Now.AddDays(number - 10),
                });
            }

            await Db.SaveChangesAsync();
        }

        public Task<List<EfHatchIssue>> TasksAsync() =>
            Db.Issues.Where(i => Db.ChangelogTasks.Any(t => t.IssueId == i.Id)).OrderBy(i => i.Id).ToListAsync();
    }

    private static async Task<Harness> NewAsync(bool elect = true)
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        var doing = new EfHatchStatus { Name = "doing", SortOrder = 30, IsImplementation = true };
        var review = new EfHatchStatus { Name = "review", SortOrder = 40 };
        var shelf = new EfHatchStatus { Name = "shelf", SortOrder = 45, IsDeferred = true };
        var done = new EfHatchStatus { Name = "done", SortOrder = 50, IsTerminal = true };
        db.AddRange(project, todo, doing, review, shelf, done);
        await db.SaveChangesAsync();

        db.ProjectRepositories.Add(new EfHatchProjectRepository
        {
            ProjectId = project.Id, Remote = Remote, Canonical = Canonical, Changelog = elect, CreatedAt = Now,
        });
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var caller = new MergeCheckControllerTests.StubCallerIdentity { Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now } };
        var options = Options.Create(new HatchOptions { PublicBaseUrl = Origin });

        return new Harness
        {
            Db = db,
            Controller = new ChangelogFileController(db, new StubActorDirectory(), caller, time, options, new RankService(db), NullLogger<ChangelogFileController>.Instance),
            ProjectId = project.Id,
            Todo = todo.Id,
            Doing = doing.Id,
            Review = review.Id,
            Shelf = shelf.Id,
            Done = done.Id,
        };
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result?.GetType().Name}");

    private static string Reason(ActionResult<ChangelogFileDto> result) => result.Result switch
    {
        BadRequestObjectResult o => o.Value?.ToString() ?? "",
        var other => throw new InvalidOperationException($"expected a refusal, got {other?.GetType().Name ?? "a value"}"),
    };
}

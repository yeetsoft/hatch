using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="DreamShapeController"/>: the press that files a draft ambition
/// in the from-column of the playbook row flagged as shaping one. The title
/// and brief are <see cref="AmbitionShape"/>'s own and tested there; this file
/// is the route around them - validation order, the birth column, the dreams
/// recorded on the draft, and the refusals, each leaving the board untouched.
/// </summary>
public class DreamShapeControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    // ---- Filing ----

    [Fact]
    public async Task APerson_FilesADraftAmbitionInTheRowsFromColumn_WithTheDreamsRecorded()
    {
        var h = await NewAsync();
        var (shaping, _) = await AddShapingRowAsync(h);
        var dreamOne = await AddDreamAsync(h, "the first dream");
        var dreamTwo = await AddDreamAsync(h, "the second dream");
        var image = await AddAttachmentAsync(h, "sketch.png", "image/png", h.ProjectId);
        var video = await AddAttachmentAsync(h, "walkthrough.mp4", "video/mp4", null);

        var result = await h.Controller.PostShape(
            new DreamShapeRequest(h.ProjectId, "  a board for ideas\nmore detail  ", [dreamOne, dreamTwo, dreamOne], [image, video]), default);

        var dto = Created(result);
        Assert.Equal("ambition", dto.Type);
        Assert.Equal("a board for ideas", dto.Title);
        Assert.StartsWith("This ambition is a draft that has not been shaped yet.", dto.Description);
        Assert.Contains("> a board for ideas\n> more detail", dto.Description);
        Assert.Contains($"![sketch.png](/api/images/{image})", dto.Description);
        Assert.Contains($"[walkthrough.mp4](/api/attachments/{video})", dto.Description);
        Assert.DoesNotContain("the first dream", dto.Description);

        var issue = await h.Db.Issues.Include(i => i.Events).SingleAsync();
        Assert.Equal(shaping.Id, issue.StatusId);
        Assert.Null(issue.ParentId);
        Assert.Equal(PriorityLevels.Normal, issue.Priority);
        Assert.Equal(PreApprovalLevels.None, issue.PreApproval);

        var threads = await h.Db.DreamThreads.Where(t => t.IssueId == issue.Id).Select(t => t.DreamId).ToListAsync();
        Assert.Equal(new[] { dreamOne, dreamTwo }.OrderBy(x => x), threads.OrderBy(x => x));
        Assert.Contains(issue.Events, e => e.Kind == EfHatchIssueEvent.DreamsWoven);
    }

    [Fact]
    public async Task WordsAlone_FileADraftWithNoDreamsRecorded()
    {
        var h = await NewAsync();
        await AddShapingRowAsync(h);

        var dto = Created(await h.Controller.PostShape(new DreamShapeRequest(h.ProjectId, "just words"), default));

        Assert.Equal("just words", dto.Title);
        Assert.Empty(await h.Db.DreamThreads.ToListAsync());
    }

    [Fact]
    public async Task ADreamAlone_FilesADraftTitledDraftAmbition()
    {
        var h = await NewAsync();
        await AddShapingRowAsync(h);
        var dream = await AddDreamAsync(h, "a dream");

        var dto = Created(await h.Controller.PostShape(new DreamShapeRequest(h.ProjectId, DreamIds: [dream]), default));

        Assert.Equal("Draft ambition", dto.Title);
        Assert.DoesNotContain("In the person's words", dto.Description);
        Assert.Single(await h.Db.DreamThreads.ToListAsync());
    }

    [Fact]
    public async Task AFileAlone_FilesADraft_AndAFileWithNoProjectIsAllowed()
    {
        var h = await NewAsync();
        await AddShapingRowAsync(h);
        var file = await AddAttachmentAsync(h, "notes.pdf", "application/pdf", null);

        var dto = Created(await h.Controller.PostShape(new DreamShapeRequest(h.ProjectId, AttachmentIds: [file]), default));

        Assert.Equal("Draft ambition", dto.Title);
        Assert.Contains($"[notes.pdf](/api/attachments/{file})", dto.Description);
    }

    [Fact]
    public async Task AProjectsOwnFlaggedRow_WinsOverTheBoards()
    {
        var h = await NewAsync();
        var (boardColumn, done) = await AddShapingRowAsync(h);
        var own = new EfHatchStatus { Name = "own shaping", SortOrder = 5 };
        h.Db.Add(own);
        await h.Db.SaveChangesAsync();
        AddRow(h, own.Id, done.Id, "ambition", shapes: true, projectId: h.ProjectId);
        await h.Db.SaveChangesAsync();

        Created(await h.Controller.PostShape(new DreamShapeRequest(h.ProjectId, "words"), default));

        var issue = await h.Db.Issues.SingleAsync();
        Assert.Equal(own.Id, issue.StatusId);
        Assert.NotEqual(boardColumn.Id, issue.StatusId);
    }

    [Fact]
    public async Task AnotherProjectsRow_IsNotUsed()
    {
        var h = await NewAsync();
        var other = new EfHatchProject { Key = "OTH", Name = "Other", CreatedAt = Now };
        h.Db.Add(other);
        var from = new EfHatchStatus { Name = "other shaping", SortOrder = 5 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 20 };
        h.Db.AddRange(from, done);
        await h.Db.SaveChangesAsync();
        AddRow(h, from.Id, done.Id, "ambition", shapes: true, projectId: other.Id);
        await h.Db.SaveChangesAsync();

        var result = await h.Controller.PostShape(new DreamShapeRequest(h.ProjectId, "words"), default);

        Assert.Equal(400, ((ObjectResult)result.Result!).StatusCode);
    }

    [Fact]
    public async Task AFlaggedRowThatNamesAnotherType_IsNotAShapingPressForAnAmbition()
    {
        var h = await NewAsync();
        var from = new EfHatchStatus { Name = "shaping", SortOrder = 5 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 20 };
        h.Db.AddRange(from, done);
        await h.Db.SaveChangesAsync();
        AddRow(h, from.Id, done.Id, "epic", shapes: true);
        await h.Db.SaveChangesAsync();

        var result = await h.Controller.PostShape(new DreamShapeRequest(h.ProjectId, "words"), default);

        Assert.Equal(400, ((ObjectResult)result.Result!).StatusCode);
    }

    // ---- Refusals ----

    [Fact]
    public async Task AKey_IsRefusedWithStatus403AndOneSentence_AndFilesNothing()
    {
        var h = await NewAsync(program: true);
        await AddShapingRowAsync(h);

        var result = await h.Controller.PostShape(new DreamShapeRequest(h.ProjectId, "words"), default);

        Assert.Equal(403, ((ObjectResult)result.Result!).StatusCode);
        Assert.Equal("shaping an ambition is a person's to press, not an agent's", Reason(result.Result));
        Assert.Empty(await h.Db.Issues.ToListAsync());
    }

    [Fact]
    public async Task ANullProject_Is400AndFilesNothing()
    {
        var h = await NewAsync();
        await AddShapingRowAsync(h);

        var result = await h.Controller.PostShape(new DreamShapeRequest(null, "words"), default);

        Assert.Equal(400, ((ObjectResult)result.Result!).StatusCode);
        Assert.Equal("shaping an ambition needs a project - an ambition needs a home", Reason(result.Result));
        Assert.Empty(await h.Db.Issues.ToListAsync());
    }

    [Fact]
    public async Task AnUnknownProject_Is404AndFilesNothing()
    {
        var h = await NewAsync();
        await AddShapingRowAsync(h);

        var result = await h.Controller.PostShape(new DreamShapeRequest(h.ProjectId + 1000, "words"), default);

        Assert.Equal(404, ((ObjectResult)result.Result!).StatusCode);
        Assert.Empty(await h.Db.Issues.ToListAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n ")]
    public async Task NoWordsNoDreamAndNoFile_Is400AndFilesNothing(string? words)
    {
        var h = await NewAsync();
        await AddShapingRowAsync(h);

        var result = await h.Controller.PostShape(new DreamShapeRequest(h.ProjectId, words, [], []), default);

        Assert.Equal(400, ((ObjectResult)result.Result!).StatusCode);
        Assert.Equal("give words, a dream or a file to shape an ambition from", Reason(result.Result));
        Assert.Empty(await h.Db.Issues.ToListAsync());
    }

    [Fact]
    public async Task AnUnknownDreamId_Is400NamingIt_AndFilesNothing()
    {
        var h = await NewAsync();
        await AddShapingRowAsync(h);
        var known = await AddDreamAsync(h, "a real dream");

        var result = await h.Controller.PostShape(new DreamShapeRequest(h.ProjectId, "words", [999, known]), default);

        Assert.Equal(400, ((ObjectResult)result.Result!).StatusCode);
        Assert.Equal("there is no dream 999", Reason(result.Result));
        Assert.Empty(await h.Db.Issues.ToListAsync());
    }

    [Fact]
    public async Task AnUnknownAttachmentId_Is400NamingIt_AndFilesNothing()
    {
        var h = await NewAsync();
        await AddShapingRowAsync(h);
        var missing = Guid.NewGuid();

        var result = await h.Controller.PostShape(new DreamShapeRequest(h.ProjectId, "words", AttachmentIds: [missing]), default);

        Assert.Equal(400, ((ObjectResult)result.Result!).StatusCode);
        Assert.Equal($"there is no file {missing}", Reason(result.Result));
        Assert.Empty(await h.Db.Issues.ToListAsync());
    }

    [Fact]
    public async Task AFileLinkedToAnotherProject_Is400NamingIt_AndFilesNothing()
    {
        var h = await NewAsync();
        await AddShapingRowAsync(h);
        var other = new EfHatchProject { Key = "OTH", Name = "Other", CreatedAt = Now };
        h.Db.Add(other);
        await h.Db.SaveChangesAsync();
        var file = await AddAttachmentAsync(h, "theirs.png", "image/png", other.Id);

        var result = await h.Controller.PostShape(new DreamShapeRequest(h.ProjectId, "words", AttachmentIds: [file]), default);

        Assert.Equal(400, ((ObjectResult)result.Result!).StatusCode);
        Assert.Equal($"file {file} belongs to another project", Reason(result.Result));
        Assert.Empty(await h.Db.Issues.ToListAsync());
    }

    [Fact]
    public async Task NoShapingRow_Is400NamingTheProjectAndThePlaybooksPage_AndFilesNothing()
    {
        var h = await NewAsync();
        var from = new EfHatchStatus { Name = "breakdown", SortOrder = 10 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 20 };
        h.Db.AddRange(from, done);
        await h.Db.SaveChangesAsync();
        AddRow(h, from.Id, done.Id, "ambition", shapes: false);
        await h.Db.SaveChangesAsync();

        var result = await h.Controller.PostShape(new DreamShapeRequest(h.ProjectId, "words"), default);

        Assert.Equal(400, ((ObjectResult)result.Result!).StatusCode);
        Assert.Equal($"{h.ProjectKey} has no playbook row that shapes an ambition - add one on the Playbooks page first", Reason(result.Result));
        Assert.Empty(await h.Db.Issues.ToListAsync());
    }

    // ---- Harness ----

    private sealed class Harness
    {
        public required DreamShapeController Controller { get; init; }
        public required HatchContext Db { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }
        public required string ProjectKey { get; init; }
    }

    private static async Task<Harness> NewAsync(bool program = false)
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Add(project);
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);

        var actors = new StubActorDirectory();
        var nathan = actors.AddPerson("Nathan");

        var caller = program
            ? new StubCaller
            {
                Key = new EfApiKey
                {
                    Name = "hatch",
                    Hash = [1],
                    Prefix = "hatch_ak_x",
                    CreatedAt = Now,
                },
            }
            : new StubCaller { Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now } };

        if (!program)
        {
            actors.Principal = nathan;
            actors.Me = nathan;
        }

        return new Harness
        {
            Controller = new DreamShapeController(db, time, caller, actors, new RankService(db), TestClaims.With())
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            },
            Db = db,
            Time = time,
            ProjectId = project.Id,
            ProjectKey = project.Key,
        };
    }

    /// <summary>A board-wide row flagged as shaping an ambition, from a fresh column, for every test that only needs *some* home.</summary>
    private static async Task<(EfHatchStatus Shaping, EfHatchStatus Done)> AddShapingRowAsync(Harness h)
    {
        var shaping = new EfHatchStatus { Name = "shaping", SortOrder = 10 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 20 };
        h.Db.AddRange(shaping, done);
        await h.Db.SaveChangesAsync();

        AddRow(h, shaping.Id, done.Id, "ambition", shapes: true);
        await h.Db.SaveChangesAsync();

        return (shaping, done);
    }

    private static void AddRow(Harness h, int fromStatusId, int toStatusId, string types, bool shapes, int? projectId = null) =>
        h.Db.Add(new EfHatchPlaybook
        {
            FromStatusId = fromStatusId,
            ToStatusId = toStatusId,
            Types = types,
            Shape = EfHatchPlaybook.AnyShape,
            Prompt = "shape it",
            Model = "sonnet",
            Effort = "high",
            ShapesAmbition = shapes,
            ProjectId = projectId,
            CreatedAt = Now,
            UpdatedAt = Now,
        });

    /// <summary>
    /// Straight through a <see cref="DreamsController"/>, never by
    /// constructing <see cref="EfHatchDream"/> directly - its
    /// <c>SearchVector</c> is an <c>NpgsqlTsVector</c>, which the in-memory
    /// provider cannot materialise.
    /// </summary>
    private static async Task<long> AddDreamAsync(Harness h, string body)
    {
        var dreams = new DreamsController(h.Db, new StubCaller { Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now } }, h.Time);
        var result = await dreams.AddDream(new DreamCreateRequest(body), default);
        var created = (CreatedResult)result.Result!;
        return ((DreamDto)created.Value!).Id;
    }

    private static async Task<Guid> AddAttachmentAsync(Harness h, string name, string contentType, int? projectId)
    {
        var attachment = new EfHatchAttachment
        {
            Id = Guid.NewGuid(),
            Bytes = [1, 2, 3],
            ContentType = contentType,
            FileName = name,
            Size = 3,
            ProjectId = projectId,
            CreatedAt = Now,
        };
        h.Db.Add(attachment);
        await h.Db.SaveChangesAsync();
        return attachment.Id;
    }

    private static IssueDto Created(ActionResult<IssueDto> result) =>
        result.Result is CreatedResult created
            ? (IssueDto)created.Value!
            : result.Value ?? throw new InvalidOperationException($"expected a created value, got {Reason(result.Result)}");

    private static string? Reason(IActionResult? result) => result switch
    {
        ObjectResult o => o.Value?.ToString(),
        _ => null,
    };

    /// <summary>Whoever is holding the phone: a person, or the key an agent carries.</summary>
    private sealed class StubCaller : ICallerIdentity
    {
        public EfPerson? Person { get; init; }

        public EfApiKey? Key { get; init; }

        public Task<EfAuthGrant?> GrantAsync(CancellationToken ct) => Task.FromResult<EfAuthGrant?>(null);

        public Task<Guid?> PersonIdAsync(CancellationToken ct) => Task.FromResult(Person?.Id);

        public Task<EfPerson?> PersonAsync(CancellationToken ct) => Task.FromResult(Person);

        public Task<EfApiKey?> ApiKeyAsync(CancellationToken ct) => Task.FromResult(Key);

        public Task<Actor?> LocalAsync(CancellationToken ct) => Task.FromResult<Actor?>(null);

        public Task<bool> IsProgramAsync(CancellationToken ct) => Task.FromResult(Key is not null);

        public Task<string> ActorNameAsync(CancellationToken ct) =>
            Task.FromResult(Person?.Name ?? Key?.Name ?? CallerIdentity.Unattributed);
    }
}

using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What a runner may say about the merge train having brought a branch
/// current: it is kept on the issue, it is news every time, and it is refused
/// for a row the train has no business with.
/// </summary>
public class BroughtCurrentControllerTests
{
    private const string TrunkSha = "1111111111111111111111111111111111111111";

    [Fact]
    public async Task AMarkOnARowInAMergesColumn_KeepsBothFieldsAndWritesOneEvent()
    {
        var h = await NewAsync(mergesPullRequest: true);

        Assert.IsType<NoContentResult>(await h.PutAsync(h.Key, Request() with { TrunkSha = $"  {TrunkSha} " }));

        var row = h.Db.Issues.Single();
        Assert.Equal(Now, row.BroughtCurrentAt);
        Assert.Equal(TrunkSha, row.BroughtCurrentTrunkSha);

        var e = Assert.Single(await h.EventsAsync());
        Assert.Equal("Nathan", e.Actor);
        var payload = e.Payload!.Value;
        Assert.Equal("main", payload.GetProperty("trunk").GetString());
        Assert.Equal(TrunkSha, payload.GetProperty("trunkSha").GetString());
        Assert.Equal("host:/checkout", payload.GetProperty("runner").GetString());
    }

    [Fact]
    public async Task ARepeat_IsNewsAgain_AndMovesTheMark()
    {
        var h = await NewAsync(mergesPullRequest: true);
        await h.PutAsync(h.Key, Request());

        h.Time.Advance(TimeSpan.FromMinutes(5));
        Assert.IsType<NoContentResult>(await h.PutAsync(h.Key, Request()));

        Assert.Equal(2, (await h.EventsAsync()).Count);
        Assert.Equal(Now.AddMinutes(5), h.Db.Issues.Single().BroughtCurrentAt);
    }

    [Fact]
    public async Task ARowOutsideAMergesColumn_Is409AndLeavesTheRowAlone()
    {
        var h = await NewAsync(mergesPullRequest: false);

        Assert.IsType<ConflictObjectResult>(await h.PutAsync(h.Key, Request()));

        var row = h.Db.Issues.Single();
        Assert.Null(row.BroughtCurrentAt);
        Assert.Null(row.BroughtCurrentTrunkSha);
        Assert.Empty(await h.EventsAsync());
    }

    [Fact]
    public async Task AnUnknownKey_Is404()
    {
        var h = await NewAsync(mergesPullRequest: true);

        Assert.IsType<NotFoundResult>(await h.PutAsync("AER-999", Request()));
        Assert.IsType<NotFoundResult>(await h.PutAsync("not a key", Request()));
    }

    [Fact]
    public async Task ABlankTrunkShaOrTrunk_Is400()
    {
        var h = await NewAsync(mergesPullRequest: true);

        Assert.IsType<BadRequestObjectResult>(await h.PutAsync(h.Key, Request() with { TrunkSha = " " }));
        Assert.IsType<BadRequestObjectResult>(await h.PutAsync(h.Key, Request() with { Trunk = "" }));
        Assert.Null(h.Db.Issues.Single().BroughtCurrentAt);
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private static BroughtCurrentRequest Request() => new("main", TrunkSha, "host:/checkout");

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required BroughtCurrentController Controller { get; init; }
        public required IssueThreadController Thread { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required string Key { get; init; }

        public Task<IActionResult> PutAsync(string key, BroughtCurrentRequest request) =>
            Controller.PutBroughtCurrent(key, request, default);

        public async Task<IReadOnlyList<IssueEventDto>> EventsAsync()
        {
            var events = await Thread.GetEvents(Key, default);
            return (events.Value ?? throw new InvalidOperationException("expected events"))
                .Where(e => e.Kind == EfHatchIssueEvent.BroughtCurrent)
                .Reverse()
                .ToList();
        }
    }

    private static async Task<Harness> NewAsync(bool mergesPullRequest)
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var status = new EfHatchStatus { Name = "approved", SortOrder = 10, MergesPullRequest = mergesPullRequest };
        db.Add(project);
        db.Add(status);
        await db.SaveChangesAsync();

        db.Issues.Add(new EfHatchIssue
        {
            ProjectId = project.Id,
            Number = 1,
            Type = "task",
            Title = "a thing",
            Description = "",
            StatusId = status.Id,
            Rank = 1024,
            CreatedBy = "operator",
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var caller = new MergeCheckControllerTests.StubCallerIdentity
        {
            Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now },
        };

        return new Harness
        {
            Db = db,
            Controller = new BroughtCurrentController(db, caller, time),
            Thread = new IssueThreadController(db, caller, time),
            Time = time,
            Key = "AER-1",
        };
    }
}

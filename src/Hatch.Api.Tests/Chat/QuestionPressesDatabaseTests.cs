using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Api.Services.Chat;
using Hatch.Api.Tests.Hatch;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Tests.Chat;

/// <summary>
/// The half of a press the database owns: two presses at once leave one answer, and
/// a press that fails part-way leaves nothing. The in-memory provider takes no lock,
/// so only a real one can show it. Skips without a Postgres; run it with
/// <c>make test-api-db</c>.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class QuestionPressesDatabaseTests
{
    [SkippableFact]
    public async Task TwoPressesAtOnce_LeaveOneAnswer_AndTheOtherIsCase8()
    {
        var connection = await PrepareAsync();
        await using var seedDb = Open(connection);
        var appOptions = AppOptions();
        await using var appDb = new AppDbContext(appOptions);
        var w = await PressWorld.SeedAsync(seedDb, appDb);

        // A second person, linked to a second member, so the two presses are two people's.
        var grace = new EfPerson { Name = "Grace", Role = PersonRole.User, CreatedAt = PressWorld.Now, UpdatedAt = PressWorld.Now };
        appDb.People.Add(grace);
        await appDb.SaveChangesAsync();
        await new ChatOutboxSeed(seedDb, PressWorld.Now).LinkAsync(w.WorkspaceId, grace.Id, "U2");

        var outcomes = await RaceAsync(connection, appOptions, w.WorkspaceId, w.Press(0, "U1"), w.Press(1, "U2"));

        Assert.Single(outcomes, o => o.Case == PressCase.Answered);
        var loser = Assert.Single(outcomes, o => o.Case == PressCase.AlreadyAnswered);

        await using var read = Open(connection);
        var answer = await read.Comments.SingleAsync(c => c.Kind == EfHatchComment.Answer);
        Assert.Equal(loser.AnsweredBy, answer.Author);
        Assert.Equal(loser.AnswerBody, answer.Body);
        Assert.Equal(1, await read.IssueEvents.CountAsync(e => e.Kind == EfHatchIssueEvent.Answered));
        Assert.All(await read.ChatMessages.ToListAsync(), m => Assert.Equal(2, m.Revision));
    }

    [SkippableFact]
    public async Task ThePressDeliveredTwiceAtOnce_LeavesOneAnswer()
    {
        var connection = await PrepareAsync();
        await using var seedDb = Open(connection);
        var appOptions = AppOptions();
        await using var appDb = new AppDbContext(appOptions);
        var w = await PressWorld.SeedAsync(seedDb, appDb);

        var outcomes = await RaceAsync(connection, appOptions, w.WorkspaceId, w.Press(), w.Press());

        Assert.Single(outcomes, o => o.Case == PressCase.Answered);
        Assert.Single(outcomes, o => o.Case == PressCase.AlreadyAnswered);
        await using var read = Open(connection);
        Assert.Equal(1, await read.Comments.CountAsync(c => c.Kind == EfHatchComment.Answer));
    }

    [SkippableFact]
    public async Task APressWhoseSaveFails_LeavesNoAnswer_NoEvent_AndNoEdit()
    {
        var connection = await PrepareAsync();
        await using var db = Open(connection);
        var appOptions = AppOptions();
        await using var appDb = new AppDbContext(appOptions);
        var w = await PressWorld.SeedAsync(db, appDb);
        await using var before = Open(connection);
        var snapshot = await PressWorld.SnapshotAsync(before);

        // A row that breaks a key, in the same unit of work as the answer: its room does not exist.
        db.ChatRoomProjects.Add(new EfHatchChatRoomProject { RoomId = 1_000_000, WorkspaceId = w.WorkspaceId, ProjectId = w.ProjectId });

        await Assert.ThrowsAsync<DbUpdateException>(() => PressWorld.Presses(db, appDb).AnswerAsync(w.WorkspaceId, w.Press(), default));

        await using var read = Open(connection);
        Assert.Equal(snapshot, await PressWorld.SnapshotAsync(read));

        // And the lock went with it: the same press, made cleanly, answers.
        await using var again = Open(connection);
        Assert.Equal(PressCase.Answered, (await PressWorld.Presses(again, appDb).AnswerAsync(w.WorkspaceId, w.Press(), default)).Case);
    }

    /// <summary>Each press on its own context, as two requests would be, started together.</summary>
    private static async Task<PressOutcome[]> RaceAsync(string connection, DbContextOptions<AppDbContext> appOptions, int workspaceId, params ChatPress[] presses)
    {
        var gate = new TaskCompletionSource();
        var tasks = presses.Select(async press =>
        {
            // One context of each per press: a context is not safe to share between two requests.
            await using var db = Open(connection);
            await using var app = new AppDbContext(appOptions);
            await gate.Task;
            return await PressWorld.Presses(db, app).AnswerAsync(workspaceId, press, default);
        }).ToArray();

        // Let every context open before any press starts.
        await Task.Delay(200);
        gate.SetResult();

        return await Task.WhenAll(tasks);
    }

    private static DbContextOptions<AppDbContext> AppOptions() =>
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private static async Task<string> PrepareAsync()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset - the lock is the database's. Run `make test-api-db`.");

        return await HatchDatabase.PrepareAsync();
    }

    private static HatchContext Open(string connection) =>
        new(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connection).Options);
}

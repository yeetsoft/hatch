using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Chat;
using Hatch.Api.Tests.Auth;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Tests.Chat;

/// <summary>
/// A question is asked, its message carries buttons, a linked person presses one, and the
/// message is edited to show their answer with no buttons. The send job, the press handler and
/// the content builder are the real ones; only the chat service is the fake.
/// </summary>
public class QuestionButtonsEndToEndTests
{
    [Fact]
    public async Task APressOnAButton_AnswersTheQuestion_AndTheEditedMessageHasNoButtons()
    {
        var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var appDb = RosterSeed.NewAppDb();
        var world = await PressWorld.SeedAsync(db, appDb);

        // The world has delivered its messages already. Start over: nothing sent, and a token that opens the buttons.
        foreach (var row in await db.ChatMessages.ToListAsync())
        {
            row.ExternalId = null;
            row.SentRevision = 0;
        }

        var workspace = await db.ChatWorkspaces.SingleAsync();
        workspace.AppToken = "sealed";
        await db.SaveChangesAsync();

        var h = new ChatSendJobTests.Harness(db) { Project = world.ProjectId };
        await h.SealAsync(world.WorkspaceId);
        h.Time.SetUtcNow(PressWorld.Now.AddMinutes(1));

        await h.TickAsync();

        // Asked: both rooms got the message, with a button for each option and one to accept the recommended.
        Assert.NotEmpty(h.Provider.Sends);
        Assert.All(h.Provider.Sends, s => Assert.Equal("post", s.Kind));
        var asked = h.Provider.Sends[0].Content;
        Assert.Equal(
            [
                ChatPressAction.WriteRecommended(world.QuestionId),
                ChatPressAction.Write(world.QuestionId, 0),
                ChatPressAction.Write(world.QuestionId, 1),
            ],
            asked.Actions!.Select(a => a.Id));
        Assert.Equal(["Accept recommended", "leaf-weighted", "child-weighted"], asked.Actions!.Select(a => a.Label));

        // A linked person presses the second option.
        // It presses the message the fake said it posted in the first room.
        var posted = await db.ChatMessages.AsNoTracking().SingleAsync(m => m.RoomId == world.RoomOne);
        var press = new ChatPress(
            PressWorld.Member, PressWorld.ChannelOne, posted.ExternalId!, ChatPressAction.Write(world.QuestionId, 1), null,
            "https://hooks.example/reply-address");
        var outcome = await PressWorld.Presses(db, appDb).AnswerAsync(world.WorkspaceId, press, default);
        Assert.Equal(PressCase.Answered, outcome.Case);

        var before = h.Provider.Sends.Count;
        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.TickAsync();
        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.TickAsync();

        // Answered: the edit says who answered with what, and the buttons are gone.
        var edits = h.Provider.Sends.Skip(before).ToList();
        Assert.NotEmpty(edits);
        Assert.All(edits, e =>
        {
            Assert.Equal("update", e.Kind);
            Assert.Null(e.Content.Actions);
            Assert.Contains("Ada answered: child-weighted", e.Content.Lines);
        });
    }
}

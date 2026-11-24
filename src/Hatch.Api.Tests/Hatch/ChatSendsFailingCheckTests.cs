using Hatch.Api.Modules.Hatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="ChatSendsFailingCheck"/> - the <c>chat-sends-failing</c> check,
/// HA-2777. Pinned here: nothing before the bound and a finding after it, the
/// sentence naming the workspace or room and what the chat service said but no
/// project, a workspace and a room each getting their own finding, the finding
/// closing when a send works or the workspace or room is removed, and the
/// three-way guard on its own setting. The reconciler is
/// <c>GooseFindingsTests</c>'s. All of it runs on fake time.
/// </summary>
public class ChatSendsFailingCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Name_IsChatSendsFailing()
    {
        Assert.Equal("chat-sends-failing", NewHarness().Check.Name);
    }

    [Fact]
    public async Task AWorkspaceFailingFiftyNineMinutes_OpensNothing_AndAtSixtyOpensOneFindingNamingNoProject()
    {
        var h = NewHarness();
        var workspace = await h.FailingWorkspaceAsync(Now - TimeSpan.FromMinutes(59), "ratelimited");
        await h.WaitingAsync(workspace.Id, 1, 2);

        await h.SweepAsync();
        Assert.Empty(await h.Db.GooseFindings.ToListAsync());

        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.SweepAsync();

        var finding = await h.Db.GooseFindings.SingleAsync();
        Assert.Equal("chat-sends-failing", finding.Check);
        Assert.Equal($"workspace:{workspace.Id}", finding.Subject);
        Assert.Null(finding.ProjectId);
        Assert.Null(finding.ClosedAt);
        Assert.Contains("workspace Acme", finding.Sentence);
        Assert.Contains("60 minutes", finding.Sentence);
        Assert.Contains("ratelimited", finding.Sentence);

        var evidence = GooseFindingEvidence.Read(finding.Evidence);
        Assert.Contains(evidence, e => e.Label == "Workspace" && e.Value == "workspace Acme");
        Assert.Contains(evidence, e => e.Label == "Messages waiting" && e.Value == "2");
    }

    [Fact]
    public async Task ARoomAndAWorkspace_EachGetTheirOwnFinding_TheRoomNamingItsWorkspace()
    {
        var h = NewHarness();
        var workspace = await h.FailingWorkspaceAsync(Now - TimeSpan.FromMinutes(90), "service_unavailable");
        var room = await h.FailingRoomAsync(workspace.Id, "releases", Now - TimeSpan.FromMinutes(75), "not_in_channel");

        await h.SweepAsync();

        var findings = (await h.Db.GooseFindings.ToListAsync()).OrderBy(f => f.Subject, StringComparer.Ordinal).ToList();
        Assert.Equal([$"room:{room.Id}", $"workspace:{workspace.Id}"], findings.Select(f => f.Subject));

        var roomFinding = findings[0];
        Assert.Contains("room releases in workspace Acme", roomFinding.Sentence);
        Assert.Contains("75 minutes", roomFinding.Sentence);
        Assert.Contains("not_in_channel", roomFinding.Sentence);
        Assert.Null(roomFinding.ProjectId);
    }

    [Fact]
    public async Task ASendThatWorks_ClosesTheFinding()
    {
        var h = NewHarness();
        var workspace = await h.FailingWorkspaceAsync(Now - TimeSpan.FromMinutes(70), "ratelimited");
        await h.SweepAsync();
        Assert.Null((await h.Db.GooseFindings.SingleAsync()).ClosedAt);

        workspace.SendFailingSince = null;
        await h.Db.SaveChangesAsync();
        await h.SweepAsync();

        Assert.NotNull((await h.Db.GooseFindings.SingleAsync()).ClosedAt);
        Assert.Empty(await h.Db.GooseSightings.ToListAsync());
    }

    [Fact]
    public async Task RemovingTheRoomOrTheWorkspace_ClosesItsFinding()
    {
        var h = NewHarness();
        var workspace = await h.FailingWorkspaceAsync(Now - TimeSpan.FromMinutes(70), "ratelimited");
        var room = await h.FailingRoomAsync(workspace.Id, "releases", Now - TimeSpan.FromMinutes(70), "not_in_channel");
        await h.SweepAsync();
        Assert.Equal(2, await h.Db.GooseFindings.CountAsync(f => f.ClosedAt == null));

        h.Db.ChatRooms.Remove(room);
        await h.Db.SaveChangesAsync();
        await h.SweepAsync();
        Assert.Equal(1, await h.Db.GooseFindings.CountAsync(f => f.ClosedAt == null));

        h.Db.ChatWorkspaces.Remove(workspace);
        await h.Db.SaveChangesAsync();
        await h.SweepAsync();
        Assert.Equal(0, await h.Db.GooseFindings.CountAsync(f => f.ClosedAt == null));
    }

    [Fact]
    public async Task ZeroMinutes_TurnsTheCheckOff_AndClosesWhatIsOpen()
    {
        var h = NewHarness();
        await h.FailingWorkspaceAsync(Now - TimeSpan.FromMinutes(70), "ratelimited");
        await h.SweepAsync();
        Assert.Null((await h.Db.GooseFindings.SingleAsync()).ClosedAt);

        h.Check = h.BuildCheck(0);
        await h.SweepAsync();

        Assert.NotNull((await h.Db.GooseFindings.SingleAsync()).ClosedAt);
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(0, 0)]
    [InlineData(-1, 60)]
    public void ChatSendBound_ZeroIsOff_AndANegativeFallsBackToTheDefault(int configured, int expectedMinutes)
    {
        Assert.Equal(
            TimeSpan.FromMinutes(expectedMinutes),
            GooseSettings.ChatSendBound(new HatchOptions { GooseChatSendMinutes = configured }));
    }

    [Fact]
    public void ChatSendBound_DefaultsToSixtyMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(60), GooseSettings.ChatSendBound(new HatchOptions()));
    }

    private static Harness NewHarness() => new();

    private sealed class Harness
    {
        public HatchContext Db { get; } =
            new(new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        public FakeTimeProvider Time { get; } = new(Now);

        public ChatSendsFailingCheck Check { get; set; }

        private int nextWorkspace = 1;

        public Harness() => Check = BuildCheck(null);

        public ChatSendsFailingCheck BuildCheck(int? minutes) =>
            new(Db, Options.Create(minutes is { } m ? new HatchOptions { GooseChatSendMinutes = m } : new HatchOptions()), Time);

        public async Task SweepAsync()
        {
            await Check.RunAsync(CancellationToken.None);
            await Db.SaveChangesAsync();
        }

        public async Task<EfHatchChatWorkspace> FailingWorkspaceAsync(DateTimeOffset since, string said)
        {
            var n = nextWorkspace++;
            var workspace = new EfHatchChatWorkspace
            {
                Provider = "slack", ExternalId = $"T{n}", Name = n == 1 ? "Acme" : $"Acme{n}", Token = "sealed",
                SendFailingSince = since, LastRefusal = said, LastRefusalAt = Now, CreatedAt = Now, UpdatedAt = Now,
            };
            Db.ChatWorkspaces.Add(workspace);
            await Db.SaveChangesAsync();

            return workspace;
        }

        public async Task<EfHatchChatRoom> FailingRoomAsync(int workspaceId, string name, DateTimeOffset since, string said)
        {
            var room = new EfHatchChatRoom
            {
                WorkspaceId = workspaceId, ExternalId = $"C-{name}", Name = name,
                SendFailingSince = since, LastRefusal = said, CreatedAt = Now, UpdatedAt = Now,
            };
            Db.ChatRooms.Add(room);
            await Db.SaveChangesAsync();

            return room;
        }

        /// <summary>Queues <paramref name="sent"/> messages already sent and <paramref name="waiting"/> still waiting, into a room that is not itself failing.</summary>
        public async Task WaitingAsync(int workspaceId, int sent, int waiting)
        {
            var room = await FailingRoomAsync(workspaceId, "queued", Now, "x");
            room.SendFailingSince = null;
            for (var i = 0; i < sent + waiting; i++)
            {
                Db.ChatMessages.Add(new EfHatchChatMessage
                {
                    RoomId = room.Id, WorkspaceId = workspaceId, Kind = "problem", Subject = $"finding:{i}", Content = "{}",
                    QueuedAt = Now, PendingSince = Now, SentRevision = i < sent ? 1 : 0,
                });
            }

            await Db.SaveChangesAsync();
        }
    }
}

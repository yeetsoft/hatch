using Hatch.Api.Modules.Hatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="ChatListenFailingCheck"/> - the <c>chat-listen-failing</c> check,
/// HA-2807. Pinned here: nothing before the bound and a finding after it, the
/// sentence naming the workspace and what Slack last said but no token, one
/// finding per failing workspace, no finding with no app-level token, the
/// finding closing when the connection opens or the workspace is removed, and
/// the three-way guard on its own setting. The reconciler is
/// <c>GooseFindingsTests</c>'s. All of it runs on fake time.
/// </summary>
public class ChatListenFailingCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Name_IsChatListenFailing()
    {
        Assert.Equal("chat-listen-failing", NewHarness().Check.Name);
    }

    [Fact]
    public async Task AWorkspaceFailingFiftyNineMinutes_OpensNothing_AndAtSixtyOpensOneFindingNamingNoProject()
    {
        var h = NewHarness();
        var workspace = await h.FailingWorkspaceAsync(Now - TimeSpan.FromMinutes(59), "ratelimited");

        await h.SweepAsync();
        Assert.Empty(await h.Db.GooseFindings.ToListAsync());

        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.SweepAsync();

        var finding = await h.Db.GooseFindings.SingleAsync();
        Assert.Equal("chat-listen-failing", finding.Check);
        Assert.Equal($"workspace:{workspace.Id}", finding.Subject);
        Assert.Null(finding.ProjectId);
        Assert.Null(finding.ClosedAt);
        Assert.Contains("workspace Acme", finding.Sentence);
        Assert.Contains("since 2026-11-23 11:01 UTC", finding.Sentence);
        Assert.Contains("ratelimited", finding.Sentence);

        var evidence = GooseFindingEvidence.Read(finding.Evidence);
        Assert.Contains(evidence, e => e.Label == "Workspace" && e.Value == "workspace Acme");
    }

    [Fact]
    public async Task TwoWorkspacesFailingPastTheBound_EachGetTheirOwnFinding()
    {
        var h = NewHarness();
        var one = await h.FailingWorkspaceAsync(Now - TimeSpan.FromMinutes(90), "service_unavailable");
        var two = await h.FailingWorkspaceAsync(Now - TimeSpan.FromMinutes(75), "not_authed");

        await h.SweepAsync();

        var findings = (await h.Db.GooseFindings.ToListAsync()).OrderBy(f => f.Subject, StringComparer.Ordinal).ToList();
        Assert.Equal([$"workspace:{one.Id}", $"workspace:{two.Id}"], findings.Select(f => f.Subject));
    }

    [Fact]
    public async Task AWorkspaceWithNoAppToken_NeverOpensAFinding()
    {
        var h = NewHarness();
        var workspace = await h.FailingWorkspaceAsync(Now - TimeSpan.FromMinutes(90), "not_authed");
        workspace.AppToken = null;
        await h.Db.SaveChangesAsync();

        await h.SweepAsync();

        Assert.Empty(await h.Db.GooseFindings.ToListAsync());
    }

    [Fact]
    public async Task AConnectionThatOpens_ClosesTheFinding()
    {
        var h = NewHarness();
        var workspace = await h.FailingWorkspaceAsync(Now - TimeSpan.FromMinutes(70), "ratelimited");
        await h.SweepAsync();
        Assert.Null((await h.Db.GooseFindings.SingleAsync()).ClosedAt);

        workspace.ListenFailingSince = null;
        await h.Db.SaveChangesAsync();
        await h.SweepAsync();

        Assert.NotNull((await h.Db.GooseFindings.SingleAsync()).ClosedAt);
    }

    [Fact]
    public async Task RemovingTheWorkspace_ClosesItsFinding()
    {
        var h = NewHarness();
        var workspace = await h.FailingWorkspaceAsync(Now - TimeSpan.FromMinutes(70), "ratelimited");
        await h.SweepAsync();
        Assert.Equal(1, await h.Db.GooseFindings.CountAsync(f => f.ClosedAt == null));

        h.Db.ChatWorkspaces.Remove(workspace);
        await h.Db.SaveChangesAsync();
        await h.SweepAsync();

        Assert.Equal(0, await h.Db.GooseFindings.CountAsync(f => f.ClosedAt == null));
    }

    [Fact]
    public async Task RemovingTheAppToken_ClosesTheFinding()
    {
        var h = NewHarness();
        var workspace = await h.FailingWorkspaceAsync(Now - TimeSpan.FromMinutes(70), "ratelimited");
        await h.SweepAsync();
        Assert.Equal(1, await h.Db.GooseFindings.CountAsync(f => f.ClosedAt == null));

        workspace.AppToken = null;
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

    [Fact]
    public async Task ASweepLater_DoesNotRestartTheClock()
    {
        var h = NewHarness();
        await h.FailingWorkspaceAsync(Now - TimeSpan.FromMinutes(70), "ratelimited");
        await h.SweepAsync();
        var began = (await h.Db.GooseFindings.SingleAsync()).BeganAt;

        h.Time.Advance(TimeSpan.FromMinutes(5));
        await h.SweepAsync();

        Assert.Equal(began, (await h.Db.GooseFindings.SingleAsync()).BeganAt);
    }

    [Fact]
    public async Task TheSentenceAndEvidence_NeverCarryTheAppToken()
    {
        var h = NewHarness();
        await h.FailingWorkspaceAsync(Now - TimeSpan.FromMinutes(70), "ratelimited", appToken: "xapp-secret");
        await h.SweepAsync();

        var finding = await h.Db.GooseFindings.SingleAsync();
        Assert.DoesNotContain("xapp-secret", finding.Sentence);
        Assert.DoesNotContain("xapp-secret", finding.Evidence);
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(0, 0)]
    [InlineData(-1, 60)]
    public void ChatListenBound_ZeroIsOff_AndANegativeFallsBackToTheDefault(int configured, int expectedMinutes)
    {
        Assert.Equal(
            TimeSpan.FromMinutes(expectedMinutes),
            GooseSettings.ChatListenBound(new HatchOptions { GooseChatListenMinutes = configured }));
    }

    [Fact]
    public void ChatListenBound_DefaultsToSixtyMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(60), GooseSettings.ChatListenBound(new HatchOptions()));
    }

    private static Harness NewHarness() => new();

    private sealed class Harness
    {
        public HatchContext Db { get; } =
            new(new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        public FakeTimeProvider Time { get; } = new(Now);

        public ChatListenFailingCheck Check { get; set; }

        private int nextWorkspace = 1;

        public Harness() => Check = BuildCheck(null);

        public ChatListenFailingCheck BuildCheck(int? minutes) =>
            new(Db, Options.Create(minutes is { } m ? new HatchOptions { GooseChatListenMinutes = m } : new HatchOptions()), Time);

        public async Task SweepAsync()
        {
            await Check.RunAsync(CancellationToken.None);
            await Db.SaveChangesAsync();
        }

        public async Task<EfHatchChatWorkspace> FailingWorkspaceAsync(DateTimeOffset since, string said, string appToken = "sealed")
        {
            var n = nextWorkspace++;
            var workspace = new EfHatchChatWorkspace
            {
                Provider = "slack", ExternalId = $"T{n}", Name = n == 1 ? "Acme" : $"Acme{n}", Token = "sealed",
                AppToken = appToken, ListenFailingSince = since, ListenLastRefusal = said, CreatedAt = Now, UpdatedAt = Now,
            };
            Db.ChatWorkspaces.Add(workspace);
            await Db.SaveChangesAsync();

            return workspace;
        }
    }
}

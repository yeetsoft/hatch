using System.Text.Json;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services;
using Hatch.Api.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The goose says what it saw and files nothing: the sweep, with all six real
/// checks over a board that gives them something to say, creates no issue,
/// writes no issue event, moves no issue and stores no bug on a finding. Only a
/// person's press on <see cref="GooseFindingBugController"/> does (HA-2516).
/// </summary>
public class GooseNeverFilesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ManySweepsOverOpenFindingsOfEveryCheck_FileNothingAndMoveNothing()
    {
        var h = new Harness();
        await h.SeedAsync();

        var before = await h.SnapshotAsync();
        Assert.Equal(2, before.IssueCount);

        for (var i = 0; i < 25; i++)
        {
            await h.Job.RunAsync(CancellationToken.None);
            h.Time.Advance(TimeSpan.FromMinutes(1));
        }

        var after = await h.SnapshotAsync();
        Assert.Equal(before.IssueCount, after.IssueCount);
        Assert.Equal(before.IssueEventCount, after.IssueEventCount);
        Assert.Equal(before.Issues, after.Issues);

        // Every finding the sweeps opened or held, of every check, still has no bug.
        Assert.Equal(6, after.Checks.Count);
        Assert.All(after.Bugs.Values, bug => Assert.Null(bug));

        // The sweeps really did read and refresh them: no check threw, and the
        // two whose condition stays true opened a finding and still hold it.
        Assert.All(await h.Db.GooseRuns.ToListAsync(), run => Assert.DoesNotContain(GooseEntry.Read(run.Entries), e => e.Failed));
        var open = await h.Db.GooseFindings.Where(f => f.ClosedAt == null).Select(f => f.Check).ToListAsync();
        Assert.Contains("questions-waiting", open);
        Assert.Contains("claim-repeating", open);
    }

    private sealed record Snapshot(
        int IssueCount, int IssueEventCount, IReadOnlyList<(long Id, int StatusId, long Rank)> Issues,
        IReadOnlyDictionary<long, long?> Bugs, IReadOnlySet<string> Checks);

    private sealed class Harness
    {
        public HatchContext Db { get; } = new(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        public FakeTimeProvider Time { get; } = new(Now);

        public GooseJob Job { get; }

        public Harness()
        {
            var actors = new StubActorDirectory();
            var claims = TestClaims.With();
            var coreDb = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var keys = new AuthService(
                coreDb, Options.Create(new AuthOptions { Enabled = false }), Time, NullLogger<AuthService>.Instance);
            var dispatch = new Dispatch(Db, actors, claims, Time);
            var runners = new Runners(
                Options.Create(new HatchOptions()), Options.Create(new AuthOptions { Enabled = false }), new StubRevision());
            var work = new ReadyWork(dispatch, Db, runners, claims, Time);
            var options = Options.Create(new HatchOptions());

            IGooseCheck[] checks =
            [
                new TrainReadyCheck(Db, dispatch, runners, keys, options, Time),
                new ReadyUnclaimedCheck(Db, work, options, Time),
                new ReadyNoRunnerCheck(Db, work, options, Time),
                new RepeatClaimsCheck(Db, options, Time),
                new QuestionsWaitingCheck(Db, options, Time),
                new StandingStillCheck(Db, work, options, Time),
                new ChatSendsFailingCheck(Db, options, Time),
                new ChatListenFailingCheck(Db, options, Time),
            ];
            Job = new GooseJob(Db, checks, new FreeLock(), options, Time, NullLogger<GooseJob>.Instance);
        }

        /// <summary>
        /// A board the two cheap checks have something to say about for real (an old
        /// question, a ticket claimed again and again), and a hand-written open
        /// finding for each of the other four, so every check has one open.
        /// </summary>
        public async Task SeedAsync()
        {
            var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
            var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
            Db.AddRange(project, todo);
            await Db.SaveChangesAsync();

            var asked = Issue(project.Id, 1, todo.Id);
            var claimed = Issue(project.Id, 2, todo.Id);
            Db.AddRange(asked, claimed);
            await Db.SaveChangesAsync();

            Db.Comments.Add(new EfHatchComment
            {
                IssueId = asked.Id, Author = "runner", Body = "Which way?", Kind = EfHatchComment.Question,
                CreatedAt = Now - TimeSpan.FromHours(30),
            });

            var holder = "someone at here:/one";
            for (var i = 0; i < 5; i++)
            {
                var at = Now - TimeSpan.FromMinutes(5 - i);
                Db.IssueEvents.Add(Event(claimed.Id, EfHatchIssueEvent.ClaimTaken, new { from = (string?)null, to = holder }, at));
                Db.IssueEvents.Add(Event(claimed.Id, EfHatchIssueEvent.ClaimReleased, new { from = holder, to = (string?)null }, at));
            }

            foreach (var check in new[] { "train-ready", "ready-unclaimed", "ready-no-runner", "standing-still" })
            {
                Db.GooseFindings.Add(new EfHatchGooseFinding
                {
                    Check = check, Subject = "AER", Token = "AER-1", ProjectId = project.Id,
                    BeganAt = Now - TimeSpan.FromHours(1), OpenedAt = Now - TimeSpan.FromMinutes(30),
                    Sentence = "Something has stood.", Evidence = "[]",
                });
            }

            await Db.SaveChangesAsync();
        }

        public async Task<Snapshot> SnapshotAsync()
        {
            var issues = await Db.Issues.AsNoTracking().OrderBy(i => i.Id).Select(i => new { i.Id, i.StatusId, i.Rank }).ToListAsync();
            var findings = await Db.GooseFindings.AsNoTracking().OrderBy(f => f.Id).ToListAsync();
            return new Snapshot(
                issues.Count,
                await Db.IssueEvents.CountAsync(),
                issues.Select(i => (i.Id, i.StatusId, i.Rank)).ToList(),
                findings.ToDictionary(f => f.Id, f => f.BugIssueId),
                findings.Select(f => f.Check).ToHashSet());
        }

        private static EfHatchIssue Issue(int projectId, int number, int statusId) => new()
        {
            ProjectId = projectId, Number = number, Type = "story", Title = $"issue {number}", StatusId = statusId,
            Rank = number, CreatedBy = "operator", CreatedAt = Now, UpdatedAt = Now,
        };

        private static EfHatchIssueEvent Event(long issueId, string kind, object payload, DateTimeOffset at) => new()
        {
            IssueId = issueId, Actor = "Hatch", Kind = kind, Payload = JsonSerializer.Serialize(payload), At = at,
        };
    }

    /// <summary>The advisory lock, which in-memory EF has no way to take, always free.</summary>
    private sealed class FreeLock : IGooseLock
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(HatchContext db, CancellationToken ct) =>
            Task.FromResult<IAsyncDisposable?>(new Held());

        private sealed class Held : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class StubRevision : IHatchRevision
    {
        public string Revision => "dev";
        public int Sequence => 0;
        public DateTimeOffset? BuiltAt => null;
        public bool IsDevelopment => true;
    }
}

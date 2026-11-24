using System.Globalization;
using Hatch.Api.Modules.Hatch;
using Hatch.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// A board for counting reads against, built at two sizes of history.
///
/// <para>Both sizes hold the same issues and the same thirty days of recent
/// work: the same moves, claims, questions and sessions, at the same moments.
/// They differ only in what happened <i>before</i> that window - six times the
/// moves, sessions and comments in the large one. A read that is bounded by the
/// window costs the same at both sizes; a read that is not, grows. That
/// difference is the whole instrument.</para>
///
/// <para>Shared by the report read test and, by design, the dispatch read test,
/// so the two pin their reads against the same board.</para>
/// </summary>
internal static class ReadCostSeed
{
    /// <summary>One size of history. Everything inside the window is the same in every size.</summary>
    /// <param name="Name">What the failure message calls it.</param>
    /// <param name="PairsPerIssue">How many times each issue went to review and back to in progress before the window.</param>
    /// <param name="OldSessions">Work-log sessions before the window, spread over every issue.</param>
    /// <param name="OldComments">Comments before the window, spread over every issue.</param>
    public sealed record Size(string Name, int PairsPerIssue, int OldSessions, int OldComments);

    /// <summary>
    /// 440 moves, 200 sessions and 100 comments from before the window: each
    /// issue's first three moves, and the twenty closing ones.
    /// </summary>
    public static readonly Size Small = new("small", PairsPerIssue: 0, OldSessions: 200, OldComments: 100);

    /// <summary>
    /// 2,440 moves (five trips to review and back for every issue), 1,200
    /// sessions and 600 comments from before the window: five and a half times
    /// the moves, and six times the rest.
    /// </summary>
    public static readonly Size Large = new("large", PairsPerIssue: 5, OldSessions: 1200, OldComments: 600);

    // ---- What is the same at both sizes ----

    /// <summary>Issues: ten epics, thirty stories, 140 tasks and twenty bugs, over two projects.</summary>
    public const int Issues = 200;

    public const int Epics = 10;
    public const int Stories = 30;
    public const int Bugs = 20;

    /// <summary>The window every report is read over: the thirty days ending <c>Now</c>.</summary>
    public const int WindowDays = 30;

    /// <summary>Claim taken and released events inside the window: 75 of each.</summary>
    public const int ClaimPairs = 75;

    /// <summary>Questions inside the window, each answered.</summary>
    public const int QuestionPairs = 60;

    /// <summary>
    /// Sessions inside the window. Thirty day buckets and a leaderboard limit of
    /// twenty are both far smaller, so a read that returns a row per session
    /// shows it.
    /// </summary>
    public const int WindowSessions = 300;

    /// <summary>
    /// How many different paths the sessions' reads name: one, on one session in
    /// five. The leaderboard's top reads are at most this many rows, and a read
    /// that returned every session's list would return far more.
    /// </summary>
    public const int DistinctReadPaths = 1;

    /// <summary>The key of the first project, the one the scoped board health read is asked for.</summary>
    public const string KeyA = "AER";

    /// <summary>The key of the second project, which holds a fifth of the root issues.</summary>
    public const string KeyB = "BEE";

    /// <summary>The person whose events the activity report reads.</summary>
    public const string Actor = "bo";

    /// <summary>What the seed made, by name, so a test can read ids off it.</summary>
    public sealed record Board(int ProjectA, int ProjectB, int Doing, int Review);

    /// <summary>
    /// Empties the database, writes the board at this size, and runs
    /// <c>VACUUM (ANALYZE)</c> so the planner has the same statistics every time.
    /// Returns the connection string. Ids restart, so both sizes share them.
    /// </summary>
    public static async Task<(string ConnectionString, Board Board)> SeedAsync(Size size, DateTimeOffset now)
    {
        var connectionString = await HatchDatabase.PrepareAsync();

        await using var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        // One connection for all of it, so the flush at the end publishes
        // every counter the seed moved before a read is measured.
        await db.Database.OpenConnectionAsync();

        var windowStart = now.AddDays(-WindowDays);

        var a = new EfHatchProject { Key = KeyA, Name = "Alpha", CreatedAt = now.AddDays(-120) };
        var b = new EfHatchProject { Key = KeyB, Name = "Beta", CreatedAt = now.AddDays(-120) };
        var inbox = new EfHatchStatus { Name = "inbox", SortOrder = 10 };
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        var doing = new EfHatchStatus { Name = "doing", SortOrder = 30, IsWip = true, IsImplementation = true };
        var review = new EfHatchStatus { Name = "review", SortOrder = 35, IsWip = true };
        var done = new EfHatchStatus { Name = "done", SortOrder = 40, IsTerminal = true };
        var later = new EfHatchStatus { Name = "later", SortOrder = 50, IsDeferred = true };
        db.AddRange(a, b, inbox, todo, doing, review, done, later);
        await db.SaveChangesAsync();

        var names = new Dictionary<int, string>
        {
            [inbox.Id] = "inbox", [todo.Id] = "todo", [doing.Id] = "doing",
            [review.Id] = "review", [done.Id] = "done", [later.Id] = "later",
        };

        // ---- Issues ----
        // The first ten are epics, then stories, tasks and bugs. A story's parent
        // is an epic, a task's a story, a bug's an epic or nothing.
        var issues = new List<EfHatchIssue>();
        var plans = new List<Plan>();
        var numbers = new Dictionary<int, int>();

        for (var i = 0; i < Issues; i++)
        {
            var type = i < Epics ? "epic" : i < Epics + Stories ? "story" : i < Issues - Bugs ? "task" : "bug";
            var parent = type switch
            {
                "story" => issues[(i - Epics) % Epics],
                "task" => issues[Epics + (i - Epics - Stories) % Stories],
                "bug" => i % 2 == 0 ? issues[i % Epics] : null,
                _ => null,
            };

            var project = parent is not null ? parent.ProjectId : i % 5 == 0 ? b.Id : a.Id;
            numbers[project] = numbers.GetValueOrDefault(project) + 1;

            var plan = Plan.For(i, type, now, windowStart);
            var final = plan.Final switch
            {
                "doing" => doing.Id, "review" => review.Id, "todo" => todo.Id, "inbox" => inbox.Id,
                "later" => later.Id, _ => done.Id,
            };

            var issue = new EfHatchIssue
            {
                ProjectId = project, Number = numbers[project], Type = type, Title = $"{type} {i}",
                StatusId = final, ParentId = parent?.Id, Rank = 1024L * (i + 1),
                ClosedAt = plan.ClosedAt, CreatedBy = Actor, CreatedAt = now.AddDays(-110), UpdatedAt = now,
            };

            issues.Add(issue);
            plans.Add(plan);

            // A parent's id is needed by its children, so each tier is written
            // before the next is built.
            if (i + 1 == Epics || i + 1 == Epics + Stories)
            {
                db.Issues.AddRange(issues.Where(x => x.Id == 0));
                await db.SaveChangesAsync();
            }
        }

        db.Issues.AddRange(issues.Where(x => x.Id == 0));
        await db.SaveChangesAsync();

        // ---- Moves ----
        var events = new List<EfHatchIssueEvent>();
        EfHatchIssueEvent Move(EfHatchIssue issue, int from, int to, DateTimeOffset at) => new()
        {
            IssueId = issue.Id, Actor = Actor, Kind = EfHatchIssueEvent.StatusChanged, At = at,
            Payload = string.Create(CultureInfo.InvariantCulture,
                $"{{\"from\":\"{names[from]}\",\"to\":\"{names[to]}\",\"fromId\":{from},\"toId\":{to}}}"),
        };

        for (var i = 0; i < Issues; i++)
        {
            var issue = issues[i];
            var plan = plans[i];

            events.Add(new EfHatchIssueEvent
            {
                IssueId = issue.Id, Actor = Actor, Kind = EfHatchIssueEvent.Created, At = issue.CreatedAt,
            });

            // Before the window: inbox, to do, in progress, then pairs of
            // in progress and review. Always ends in progress - or, for an
            // issue that closed before the window, in done. The pairs are what
            // scale; the first three are the same at every size.
            var path = new List<int> { inbox.Id, todo.Id, doing.Id };
            for (var p = 0; p < size.PairsPerIssue; p++) path.AddRange([review.Id, doing.Id]);
            if (plan.ClosedBeforeWindow) path.AddRange([review.Id, done.Id]);

            var spacing = TimeSpan.FromDays(55.0 / (path.Count - 1));
            for (var j = 1; j < path.Count; j++)
                events.Add(Move(issue, path[j - 1], path[j], now.AddDays(-100) + j * spacing + TimeSpan.FromHours(i % 50)));

            // Inside the window: out to review and back, then wherever it ends.
            if (plan.ClosedBeforeWindow) continue;

            int[] window = plan.Final == "doing" ? [doing.Id, review.Id, doing.Id] : [doing.Id, review.Id, doing.Id, plan.FinalId(names)];
            for (var j = 1; j < window.Length; j++)
                events.Add(Move(issue, window[j - 1], window[j], windowStart.AddDays(1 + (j - 1) * 7) + TimeSpan.FromHours((i % 100) * 1.5)));
        }

        // ---- Claims ----
        for (var c = 0; c < ClaimPairs; c++)
        {
            var issue = issues[Epics + Stories + c % (Issues - Epics - Stories)];
            var at = windowStart.AddHours(5 + c * 9);
            events.Add(new EfHatchIssueEvent
            {
                IssueId = issue.Id, Actor = Actor, Kind = EfHatchIssueEvent.ClaimTaken, At = at,
                Payload = "{\"runner\":\"runner-1\"}",
            });
            events.Add(new EfHatchIssueEvent
            {
                IssueId = issue.Id, Actor = Actor, Kind = EfHatchIssueEvent.ClaimReleased, At = at.AddMinutes(40),
                Payload = "{\"runner\":\"runner-1\"}",
            });
        }

        db.IssueEvents.AddRange(events);
        await db.SaveChangesAsync();

        // ---- Questions ----
        for (var q = 0; q < QuestionPairs; q++)
        {
            var issue = issues[Epics + q % (Issues - Epics)];
            var at = windowStart.AddHours(12 + q * 11);
            var asked = new EfHatchComment
            {
                IssueId = issue.Id, Author = Actor, Body = "which way?", Kind = EfHatchComment.Question, CreatedAt = at,
            };
            db.Comments.Add(asked);
            await db.SaveChangesAsync();

            db.Comments.Add(new EfHatchComment
            {
                IssueId = issue.Id, Author = "ann", Body = "that way", Kind = EfHatchComment.Answer,
                AnswersId = asked.Id, CreatedAt = at.AddMinutes(30 + q),
            });
        }

        // ---- Comments before the window ----
        for (var n = 0; n < size.OldComments; n++)
            db.Comments.Add(new EfHatchComment
            {
                IssueId = issues[n % Issues].Id, Author = "ann", Body = "a note", Kind = EfHatchComment.Note,
                CreatedAt = now.AddDays(-100).AddHours(n),
            });

        // ---- Sessions ----
        var session = 0;
        EfHatchWorkLogEntry Session(EfHatchIssue issue, DateTimeOffset at, int n)
        {
            var coded = n % 9 == 0;
            return new EfHatchWorkLogEntry
            {
                IssueId = issue.Id, SessionId = $"s{++session}", StartedAt = at, EndedAt = at.AddMinutes(20),
                DurationMs = 1_200_000, Title = $"session {n}", IsError = n % 13 == 0, Turns = 10,
                Outcome = n % 7 == 0 ? "ok" : null,
                PlaybookKey = coded ? "task:implement" : null,
                PlaybookFromStatusId = coded ? todo.Id : null, PlaybookToStatusId = coded ? doing.Id : null,
                Reads = n % 5 == 0 ? WorkLogRollup.WriteReads([new WorkLogReadDto("docs/hatch.md", 2)]) : null,
                InputTokens = 1000, OutputTokens = 200, CreatedAt = at.AddMinutes(20),
            };
        }

        // In runs on a few issues rather than spread one-per-issue, so a bucket
        // read answers with far fewer than one row per session (see HA-2789):
        // 300 sessions over fifteen-session runs on eight issues is about fifty
        // (issue, day) pairs, not three hundred.
        for (var n = 0; n < WindowSessions; n++)
            db.WorkLog.Add(Session(issues[Epics + (n / 15) % 8], windowStart.AddHours(2 + n * 2.3), n));

        for (var n = 0; n < size.OldSessions; n++)
            db.WorkLog.Add(Session(issues[n % Issues], now.AddDays(-100).AddHours(n), n + 1000));

        await db.SaveChangesAsync();

        // Statistics for the planner, and the visibility map for index-only
        // scans, both settled now. Left to autovacuum, the second is set
        // whenever it gets round to it, and an index-only scan counts fewer
        // rows once it has - so the numbers would move between runs.
        await db.Database.ExecuteSqlRawAsync("VACUUM (ANALYZE)");

        // The seed's own counters, published now and not in the middle of the
        // first read.
        await ReadCounter.FlushAsync(db.Database.GetDbConnection());

        return (connectionString, new Board(a.Id, b.Id, doing.Id, review.Id));
    }

    /// <summary>Where an issue ends up, and when it closed, worked out from its place in the line.</summary>
    private sealed record Plan(string Final, DateTimeOffset? ClosedAt, bool ClosedBeforeWindow)
    {
        public int FinalId(Dictionary<int, string> names) => names.Single(n => n.Value == Final).Key;

        /// <summary>
        /// About a fifth close: half of those before the window and half
        /// inside it. Three epics close, one before. The rest are spread over
        /// every open column, a few of them deferred.
        /// </summary>
        public static Plan For(int i, string type, DateTimeOffset now, DateTimeOffset windowStart)
        {
            var closes = type == "epic" ? i is 3 or 6 or 9 : i % 5 == 4;
            if (!closes)
            {
                var open = (i % 5) switch { 0 => "doing", 1 => "review", 2 => "todo", _ => "inbox" };
                return new Plan(i % 11 == 0 ? "later" : open, null, false);
            }

            var before = type == "epic" ? i == 9 : (i / 5) % 2 == 1;

            // The window's last move, as the move loop above places it.
            return before
                ? new Plan("done", now.AddDays(-45 - i % 20), true)
                : new Plan("done", windowStart.AddDays(15) + TimeSpan.FromHours((i % 100) * 1.5), false);
        }
    }
}

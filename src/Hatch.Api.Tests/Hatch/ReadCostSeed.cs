using System.Globalization;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
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
    /// <param name="OldLevels">Pre-approval changes before the window, for every issue.</param>
    /// <param name="OldRecords">Pull request record changes before the window, for every issue.</param>
    /// <param name="OldRunnerStates">State rows before the window, for each of the seeded runners.</param>
    /// <param name="OldVerdicts">Verdicts, and the <c>capability_judged</c> events beside them, before the window, for each capability of the ambition <see cref="AddAmbitionAsync"/> adds.</param>
    public sealed record Size(
        string Name, int PairsPerIssue, int OldSessions, int OldComments,
        int OldLevels = 1, int OldRecords = 1, int OldRunnerStates = 2, int OldVerdicts = 3);

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
    public static readonly Size Large = new("large", PairsPerIssue: 5, OldSessions: 1200, OldComments: 600,
        OldLevels: 5, OldRecords: 5, OldRunnerStates: 500, OldVerdicts: 15);

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

    /// <summary>Pre-approval changes and pull request records inside the window, on this many issues each.</summary>
    public const int WindowLevelIssues = 60;

    /// <summary>The runners the seed gives a beat and a state record, named like the claim payloads' runner.</summary>
    public const int Runners = 3;

    /// <summary>State rows each runner has inside the window.</summary>
    public const int WindowRunnerStates = 6;

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
    public sealed record Board(int ProjectA, int ProjectB, int Doing, int Review, int Inbox, int Todo, int Done, int Later);

    /// <summary>
    /// Empties the database, writes the board at this size, and runs
    /// <c>VACUUM (ANALYZE)</c> so the planner has the same statistics every time.
    /// Returns the connection string. Ids restart, so both sizes share them.
    ///
    /// <para><paramref name="issues"/> is how many issues the board holds, in the
    /// same tiers at every count: a twentieth of them epics, three twentieths
    /// stories, a tenth bugs, the rest tasks. At the default that is 10, 30, 20
    /// and 140. Everything else - the window, the history, the questions and
    /// sessions - is the same at every count.</para>
    ///
    /// <para><paramref name="extend"/> runs after the board is written and before
    /// the statistics are settled, with the issues in the order they were made
    /// (epics first, then stories, tasks and bugs). It is for a test that needs
    /// more on the board than a report reads: playbooks, dependencies, runners.</para>
    /// </summary>
    public static async Task<(string ConnectionString, Board Board)> SeedAsync(
        Size size, DateTimeOffset now, int issues = Issues,
        Func<HatchContext, Board, IReadOnlyList<EfHatchIssue>, Task>? extend = null)
    {
        var total = issues;
        var epics = total / 20;
        var stories = total * 3 / 20;
        var bugs = total / 10;

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
        // The first twentieth are epics, then stories, tasks and bugs. A story's parent
        // is an epic, a task's a story, a bug's an epic or nothing.
        var all = new List<EfHatchIssue>();
        var plans = new List<Plan>();
        var numbers = new Dictionary<int, int>();

        for (var i = 0; i < total; i++)
        {
            var type = i < epics ? "epic" : i < epics + stories ? "story" : i < total - bugs ? "task" : "bug";
            var parent = type switch
            {
                "story" => all[(i - epics) % epics],
                "task" => all[epics + (i - epics - stories) % stories],
                "bug" => i % 2 == 0 ? all[i % epics] : null,
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

            all.Add(issue);
            plans.Add(plan);

            // A parent's id is needed by its children, so each tier is written
            // before the next is built.
            if (i + 1 == epics || i + 1 == epics + stories)
            {
                db.Issues.AddRange(all.Where(x => x.Id == 0));
                await db.SaveChangesAsync();
            }
        }

        db.Issues.AddRange(all.Where(x => x.Id == 0));
        await db.SaveChangesAsync();

        // ---- Moves ----
        var events = new List<EfHatchIssueEvent>();
        EfHatchIssueEvent Move(EfHatchIssue issue, int from, int to, DateTimeOffset at) => new()
        {
            IssueId = issue.Id, Actor = Actor, Kind = EfHatchIssueEvent.StatusChanged, At = at,
            Payload = string.Create(CultureInfo.InvariantCulture,
                $"{{\"from\":\"{names[from]}\",\"to\":\"{names[to]}\",\"fromId\":{from},\"toId\":{to}}}"),
        };

        for (var i = 0; i < total; i++)
        {
            var issue = all[i];
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
            var issue = all[epics + stories + c % (total - epics - stories)];
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

        // ---- Pre-approval changes and pull request records ----
        // Before the window, each issue has size.OldLevels of each, the last
        // one ending in a level (or a recorded request) the window opens on.
        // Inside it, a few issues change again, the same at every size.
        string Level(int n) => n % 3 == 0 ? PreApprovalLevels.NoneName : n % 3 == 1 ? PreApprovalLevels.ExpressName : PreApprovalLevels.PreApprovedName;
        EfHatchIssueEvent Change(EfHatchIssue issue, string kind, string from, string to, DateTimeOffset at) => new()
        {
            IssueId = issue.Id, Actor = Actor, Kind = kind, At = at,
            Payload = string.Create(CultureInfo.InvariantCulture, $"{{\"from\":\"{from}\",\"to\":\"{to}\"}}"),
        };

        for (var i = epics; i < total; i++)
        {
            var issue = all[i];
            for (var n = 0; n < size.OldLevels; n++)
                events.Add(Change(issue, EfHatchIssueEvent.PreApprovalChanged, Level(n), Level(n + 1),
                    now.AddDays(-95) + TimeSpan.FromHours(n * 7) + TimeSpan.FromMinutes(i)));
            for (var n = 0; n < size.OldRecords; n++)
                events.Add(Change(issue, EfHatchIssueEvent.PullRequestChanged,
                    n % 2 == 0 ? "" : "https://forge.invalid/pr/" + i, n % 2 == 0 ? "https://forge.invalid/pr/" + i : "",
                    now.AddDays(-90) + TimeSpan.FromHours(n * 5) + TimeSpan.FromMinutes(i)));

            if (i - epics >= WindowLevelIssues) continue;
            var inside = windowStart.AddDays(2) + TimeSpan.FromHours(i * 3);
            events.Add(Change(issue, EfHatchIssueEvent.PreApprovalChanged, Level(size.OldLevels), Level(size.OldLevels + 1), inside));
            events.Add(Change(issue, EfHatchIssueEvent.PullRequestChanged,
                size.OldRecords % 2 == 0 ? "" : "https://forge.invalid/pr/" + i,
                size.OldRecords % 2 == 0 ? "https://forge.invalid/pr/" + i : "", inside.AddHours(1)));
        }

        db.IssueEvents.AddRange(events);
        await db.SaveChangesAsync();

        // ---- Runners ----
        // A beat with no key (AdoptedAt unset), so the lanes show it, and a state
        // record: the same few rows in the window at every size, more before it
        // in the large one.
        for (var r = 1; r <= Runners; r++)
        {
            var name = $"runner-{r}";
            db.LegacyRunners.Add(new EfHatchRunner
            {
                Name = name, Kind = EfHatchRunner.LoopKind, FirstSeenAt = now.AddDays(-100), LastSeenAt = now.AddMinutes(-r),
                State = EfHatchRunner.Running,
            });

            for (var n = 0; n < size.OldRunnerStates; n++)
                db.RunnerStates.Add(new EfHatchRunnerState
                {
                    Runner = name, At = now.AddDays(-100) + TimeSpan.FromHours(n * 3 + r),
                    State = n % 2 == 0 ? RunnerStateRule.Running : RunnerStateRule.Paused,
                });

            for (var n = 0; n < WindowRunnerStates; n++)
                db.RunnerStates.Add(new EfHatchRunnerState
                {
                    Runner = name, At = windowStart.AddDays(1) + TimeSpan.FromHours(n * 20 + r),
                    State = n % 2 == 0 ? RunnerStateRule.Running : RunnerStateRule.Paused,
                });
        }

        await db.SaveChangesAsync();

        // ---- Questions ----
        for (var q = 0; q < QuestionPairs; q++)
        {
            var issue = all[epics + q % (total - epics)];
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
                IssueId = all[n % total].Id, Author = "ann", Body = "a note", Kind = EfHatchComment.Note,
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
            db.WorkLog.Add(Session(all[epics + (n / 15) % 8], windowStart.AddHours(2 + n * 2.3), n));

        for (var n = 0; n < size.OldSessions; n++)
            db.WorkLog.Add(Session(all[n % total], now.AddDays(-100).AddHours(n), n + 1000));

        await db.SaveChangesAsync();

        var board = new Board(a.Id, b.Id, doing.Id, review.Id, inbox.Id, todo.Id, done.Id, later.Id);
        if (extend is not null)
        {
            await extend(db, board, all);
            await db.SaveChangesAsync();
        }

        // Statistics for the planner, and the visibility map for index-only
        // scans, both settled now. Left to autovacuum, the second is set
        // whenever it gets round to it, and an index-only scan counts fewer
        // rows once it has - so the numbers would move between runs.
        await db.Database.ExecuteSqlRawAsync("VACUUM (ANALYZE)");

        // The seed's own counters, published now and not in the middle of the
        // first read.
        await ReadCounter.FlushAsync(db.Database.GetDbConnection());

        return (connectionString, board);
    }

    /// <summary>Capabilities on the ambition <see cref="AddAmbitionAsync" /> adds.</summary>
    public const int AmbitionCapabilities = 3;

    /// <summary>
    /// One ambition under the first project, for the read of what changed under it. Passed to
    /// <see cref="SeedAsync"/> as <c>extend</c>, so the reads that do not know of ambitions keep the
    /// board they were pinned on.
    ///
    /// <para>It has three capabilities and two epics, both filed inside the window, and inside the
    /// window: one capability that comes to hold, one that holds and stops, an open hold question on
    /// one epic and a merge question on the other that a key answered. Before the window each
    /// capability has <see cref="Size.OldVerdicts"/> verdicts and as many events, none of them
    /// holding, so the read has history to ignore and the same four lines to draw at both sizes.</para>
    /// </summary>
    public static async Task AddAmbitionAsync(HatchContext db, Board board, IReadOnlyList<EfHatchIssue> issues, Size size, DateTimeOffset now)
    {
        var windowStart = now.AddDays(-WindowDays);
        var number = issues.Where(i => i.ProjectId == board.ProjectA).Max(i => i.Number);

        EfHatchIssue Issue(string type, string title, int status, long? parent, DateTimeOffset at) => new()
        {
            ProjectId = board.ProjectA, Number = ++number, Type = type, Title = title, StatusId = status, ParentId = parent,
            Rank = 1024L * (number + 1), CreatedBy = Actor, CreatedAt = at, UpdatedAt = at,
        };

        var ambition = Issue("ambition", "the ambition", board.Todo, null, now.AddDays(-110));
        db.Issues.Add(ambition);
        await db.SaveChangesAsync();

        var firstEpic = Issue("epic", "the first epic", board.Doing, ambition.Id, windowStart.AddDays(3));
        var secondEpic = Issue("epic", "the second epic", board.Todo, ambition.Id, windowStart.AddDays(4));
        db.Issues.AddRange(firstEpic, secondEpic);
        await db.SaveChangesAsync();

        var capabilities = Enumerable.Range(1, AmbitionCapabilities)
            .Select(p => new EfHatchCapability
            {
                IssueId = ambition.Id, Position = p, Statement = $"capability {p}", HowToTell = "look",
                CreatedBy = Actor, CreatedAt = now.AddDays(-110), UpdatedAt = now.AddDays(-110),
            })
            .ToList();
        db.Capabilities.AddRange(capabilities);
        await db.SaveChangesAsync();

        db.CapabilityLinks.AddRange(
            new EfHatchCapabilityLink { CapabilityId = capabilities[0].Id, IssueId = firstEpic.Id, Actor = Actor, At = windowStart.AddDays(3) },
            new EfHatchCapabilityLink { CapabilityId = capabilities[1].Id, IssueId = firstEpic.Id, Actor = Actor, At = windowStart.AddDays(3) },
            new EfHatchCapabilityLink { CapabilityId = capabilities[2].Id, IssueId = secondEpic.Id, Actor = Actor, At = windowStart.AddDays(4) });

        // A verdict and the event the route writes beside it.
        void Judge(EfHatchCapability capability, string verdict, DateTimeOffset at)
        {
            db.CapabilityVerdicts.Add(new EfHatchCapabilityVerdict
            {
                CapabilityId = capability.Id, Verdict = verdict, LookedAt = "the page", Sha = "abc1234", Author = Actor, CreatedAt = at,
            });
            db.IssueEvents.Add(new EfHatchIssueEvent
            {
                IssueId = ambition.Id, Actor = Actor, Kind = EfHatchIssueEvent.CapabilityJudged, At = at,
                Payload = string.Create(CultureInfo.InvariantCulture,
                    $"{{\"id\":{capability.Id},\"position\":{capability.Position},\"verdict\":\"{verdict}\",\"sha\":\"abc1234\",\"author\":\"{Actor}\"}}"),
            });
        }

        // None of the old ones holds, so a hold inside the window is news.
        foreach (var capability in capabilities)
            for (var n = 0; n < size.OldVerdicts; n++)
                Judge(capability, n % 2 == 0 ? EfHatchCapabilityVerdict.DoesNotHold : EfHatchCapabilityVerdict.CannotTell,
                    now.AddDays(-100) + TimeSpan.FromHours(n * 3) + TimeSpan.FromMinutes(capability.Position));

        Judge(capabilities[0], EfHatchCapabilityVerdict.Holds, windowStart.AddDays(5));
        Judge(capabilities[1], EfHatchCapabilityVerdict.Holds, windowStart.AddDays(6));
        Judge(capabilities[1], EfHatchCapabilityVerdict.DoesNotHold, windowStart.AddDays(7));

        foreach (var epic in new[] { firstEpic, secondEpic })
            db.IssueEvents.Add(new EfHatchIssueEvent { IssueId = epic.Id, Actor = Actor, Kind = EfHatchIssueEvent.Created, At = epic.CreatedAt });

        db.Comments.Add(new EfHatchComment
        {
            IssueId = firstEpic.Id, Author = Actor, Body = "which way?", Kind = EfHatchComment.Question, CreatedAt = windowStart.AddDays(8),
        });
        var merged = new EfHatchComment
        {
            IssueId = secondEpic.Id, Author = Actor, Body = "which store?", Kind = EfHatchComment.Question,
            Level = QuestionLevels.MergeName, CreatedAt = windowStart.AddDays(9),
        };
        db.Comments.Add(merged);
        await db.SaveChangesAsync();

        db.Comments.Add(new EfHatchComment
        {
            IssueId = secondEpic.Id, Author = Actor, AuthorKind = ActorKind.Key, Body = "the first store",
            Kind = EfHatchComment.Answer, AnswersId = merged.Id, CreatedAt = windowStart.AddDays(9).AddHours(1),
        });
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

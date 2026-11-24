using System.Text.Json;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The timeline and cumulative flow fold a range, and read only the moves that
/// range needs (<see cref="BoardHealthTrail.ReadAsync"/> with a closed window).
/// This proves the cut is no more than a cut: over generated trails and many
/// windows, each fold is run once on every move there is and once on the
/// bounded read, and the two answers are equal.
///
/// <para>The trails are seeded, so a failure names a window that can be run
/// again. They hold the cases the cut could get wrong: an issue that never moved;
/// one whose first move is after the window; two moves at one instant, before
/// the window and inside it; a move into a column since deleted; an old event
/// with names and no ids; an event whose id and time disagree in order; and
/// windows that start or end exactly on a move.</para>
///
/// <para>Postgres, because the read is built of correlated probes and a payload
/// column that EF's in-memory provider does not run the way the database does.
/// Skipped without <c>HATCH_TEST_DATABASE_URL</c>; <c>make test-api-db</c>
/// sets it.</para>
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class ReportTrailEquivalenceTests
{
    private static readonly DateTimeOffset Base = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>When the cumulative flow fold is told it is: inside the generated history, so some windows run past it.</summary>
    private static readonly DateTimeOffset Now = Base.AddDays(70);

    private const int Windows = 43;

    [SkippableFact]
    public async Task ABoundedRead_FoldsToTheSameAnswers_AsEveryMove_OverManyWindows()
    {
        Skip.IfNot(
            HatchDatabase.Available,
            "HATCH_TEST_DATABASE_URL is unset - the read is built of probes that need a real Postgres. " +
            "Run `make test-api-db`, or point the variable at a scratch database that may be dropped.");

        var connectionString = await HatchDatabase.PrepareAsync();
        var rng = new Random(2784);

        await using var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var instants = await SeedAsync(db, rng);

        var statuses = await db.Statuses.AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync();
        var rows = await LoadRowsAsync(db);

        var everyMove = (await db.IssueEvents.AsNoTracking()
                .Where(e => e.Kind == EfHatchIssueEvent.StatusChanged)
                .OrderBy(e => e.IssueId).ThenBy(e => e.At).ThenBy(e => e.Id)
                .Select(e => new { e.IssueId, e.Id, e.At, e.Payload })
                .ToListAsync())
            .Select(e => TimelineReport.ParseChange(e.IssueId, e.Id, e.At, e.Payload))
            .OfType<TimelineReport.Change>()
            .ToList();

        Assert.True(everyMove.Count > 150, $"the trails hold {everyMove.Count} moves, fewer than a few hundred");

        var first = rows.Min(r => r.CreatedAt);
        var windows = WindowsOf(rng, instants, first);
        Assert.Equal(Windows, windows.Count);

        var projectKeys = rows.Select(r => r.ProjectKey).Distinct().Order().ToList();
        var projectIds = rows.Select(r => r.ProjectId).Distinct().Order().ToList();
        var failures = new List<string>();

        for (var w = 0; w < windows.Count; w++)
        {
            var (start, end) = windows[w];

            // The timeline reads from the clamped start, as the route does, and some
            // windows narrow the roots by a project, which the read does not see.
            var since = RangeBounds.ClampStart(start, end, first);
            IReadOnlyList<int>? onlyProjects = w % 5 == 3 ? [projectIds[0]] : null;

            var timelineWhole = TimelineReport.Fold(rows, everyMove, statuses, since, end, onlyProjects, null);
            var timelineBounded = TimelineReport.Fold(
                rows, await BoundedAsync(db, since, end, null), statuses, since, end, onlyProjects, null);
            Compare(failures, "timeline", w, start, end, timelineWhole, timelineBounded);

            // Cumulative flow reads from the same clamped start, for the scope the
            // fold narrows its rows to, and is handed the raw start.
            var scope = w % 4 == 1 ? new HashSet<string> { projectKeys[0] } : null;
            var flowWhole = CumulativeFlowReport.Fold(rows, everyMove, statuses, start, end, Now, scope);
            var flowBounded = CumulativeFlowReport.Fold(
                rows, await BoundedAsync(db, since, end, scope), statuses, start, end, Now, scope);
            Compare(failures, "cumulative flow", w, start, end, flowWhole, flowBounded);
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [SkippableFact]
    public async Task AClosedWindow_AndOlderOnlyIf_AreRefusedTogether()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        await Assert.ThrowsAsync<ArgumentException>(() => BoardHealthTrail.ReadAsync(
            db, [EfHatchIssueEvent.StatusChanged], Base, null, null, default,
            olderOnlyIf: EfHatchIssueEvent.StatusChanged, until: Base.AddDays(1)));
    }

    /// <summary>
    /// The flow report reads its window alone (<see cref="FlowRead"/>). The same
    /// windows, with a project filter on some and a type filter on others, are
    /// folded once on every move, release, session and question there is and once
    /// on what the read returns, and the two answers are equal.
    /// </summary>
    [SkippableFact]
    public async Task TheFlowRead_AnswersWhatEveryRowWould_OverManyWindows()
    {
        Skip.IfNot(
            HatchDatabase.Available,
            "HATCH_TEST_DATABASE_URL is unset - the read is built of probes that need a real Postgres. " +
            "Run `make test-api-db`, or point the variable at a scratch database that may be dropped.");

        var connectionString = await HatchDatabase.PrepareAsync();
        var rng = new Random(2785);

        await using var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var instants = await SeedAsync(db, rng);
        await SeedFlowAsync(db, rng, instants);

        var statuses = await db.Statuses.AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync();
        var rows = await LoadRowsAsync(db);

        // Everything there is, as the route read it before the window was bounded.
        var events = (await db.IssueEvents.AsNoTracking()
                .Where(e => e.Kind == EfHatchIssueEvent.StatusChanged || e.Kind == EfHatchIssueEvent.ClaimReleased)
                .OrderBy(e => e.IssueId).ThenBy(e => e.At).ThenBy(e => e.Id)
                .Select(e => new { e.IssueId, e.Id, e.Kind, e.At, e.Payload })
                .ToListAsync())
            .Select(e => new FlowReport.Event(e.IssueId, e.Id, e.Kind, e.At, e.Payload))
            .ToList();
        var sessions = (await db.WorkLog.AsNoTracking()
                .Select(w => new { w.IssueId, w.EndedAt, w.IsError })
                .ToListAsync())
            .Select(w => new FlowReport.Session(w.IssueId, w.EndedAt, w.IsError))
            .ToList();
        var questions = await QuestionTrail.ReadAsync(db, null, null, default);

        Assert.True(events.Count(e => e.Kind == EfHatchIssueEvent.ClaimReleased) > 40, "too few releases seeded");
        Assert.True(sessions.Count > 80, "too few sessions seeded");
        Assert.True(questions.Count > 40, "too few questions seeded");

        var first = rows.Min(r => r.CreatedAt);
        var windows = WindowsOf(rng, instants, first);
        Assert.Equal(Windows, windows.Count);

        var projectIds = rows.Select(r => r.ProjectId).Distinct().Order().ToList();
        var failures = new List<string>();

        // What the answers hold across the windows, so a seed that left a count at nothing is noticed.
        long dropped = 0, sessionCount = 0, errors = 0, asked = 0, answered = 0, lapsed = 0, dwelled = 0;

        // The control: the same fold on the cut without the walk back. It has to be wrong
        // somewhere, or the hand-written stays do not reach a window and nothing is proved.
        var withoutWalkBack = 0;

        for (var w = 0; w < windows.Count; w++)
        {
            var (rawStart, end) = windows[w];
            var start = RangeBounds.ClampStart(rawStart, end, first);

            IReadOnlyList<int>? onlyProjects = w % 3 == 1 ? [projectIds[0]] : null;
            var type = w % 4 == 2 ? "task" : w % 7 == 3 ? "story" : null;

            var whole = FlowReport.Fold(rows, events, questions, sessions, statuses, start, end, onlyProjects, type);
            var read = await FlowRead.ReadAsync(db, rows, statuses, start, end, onlyProjects, type, default);
            Compare(failures, "flow", w, start, end, whole, read);

            dropped += whole.Window.Dropped;
            sessionCount += whole.Window.Sessions;
            errors += whole.Window.SessionErrors;
            asked += whole.Window.QuestionsAsked;
            answered += whole.Window.QuestionsAnswered;
            lapsed += whole.Window.QuestionsLapsed;
            dwelled += whole.Columns.Sum(c => c.Dwelled);

            var cut = (await BoardHealthTrail.ReadAsync(
                    db, [EfHatchIssueEvent.StatusChanged], start, null, null, default, until: end, wholeInstant: true))
                .Select(e => new FlowReport.Event(e.IssueId, e.Id, e.Kind, e.At, e.Payload))
                .ToList();
            var uncorrected = FlowReport.Fold(rows, cut, questions, sessions, statuses, start, end, onlyProjects, type);
            if (JsonSerializer.Serialize(uncorrected.Columns) != JsonSerializer.Serialize(whole.Columns)) withoutWalkBack++;
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));

        Assert.True(dropped > 0 && sessionCount > 0 && errors > 0 && asked > 0 && answered > 0 && lapsed > 0 && dwelled > 0,
            $"a count is nothing in every window: dropped {dropped}, sessions {sessionCount}, errors {errors}, " +
            $"asked {asked}, answered {answered}, lapsed {lapsed}, dwelled {dwelled}");
        Assert.True(withoutWalkBack > 0, "no window needed the walk back, so the hand-written stays prove nothing");
    }

    /// <summary>
    /// Releases (some dropped), sessions (some errors) and questions, spread over
    /// the same stretch of time as the moves, some on a move's own instant.
    /// </summary>
    private static async Task SeedFlowAsync(HatchContext db, Random rng, List<DateTimeOffset> instants)
    {
        var issues = await db.Issues.AsNoTracking().ToListAsync();

        DateTimeOffset Time() => rng.Next(3) == 0
            ? instants[rng.Next(instants.Count)]
            : Base.AddMinutes(rng.Next(-40 * 24 * 60, 130 * 24 * 60));

        EfHatchIssue AnIssue() => issues[rng.Next(issues.Count)];

        // Releases: dropped, worked, an outcome that only looks like dropped, and none.
        string?[] payloads =
        [
            "{\"outcome\":\"dropped\",\"runner\":\"r1\"}", "{\"outcome\":\"dropped\"}", "{\"outcome\":\"worked\"}",
            "{\"outcome\":\"Dropped\"}", "{\"outcome\":3}", "{\"runner\":\"r1\"}", null,
        ];
        for (var n = 0; n < 160; n++)
            db.IssueEvents.Add(new EfHatchIssueEvent
            {
                IssueId = AnIssue().Id, Actor = "ann", Kind = EfHatchIssueEvent.ClaimReleased, At = Time(),
                Payload = payloads[rng.Next(payloads.Length)],
            });

        for (var n = 0; n < 200; n++)
        {
            var ended = Time();
            db.WorkLog.Add(new EfHatchWorkLogEntry
            {
                IssueId = AnIssue().Id, SessionId = $"s{n}", StartedAt = ended.AddMinutes(-20), EndedAt = ended,
                DurationMs = 1_200_000, IsError = rng.Next(4) == 0, Turns = 3, CreatedAt = ended,
            });
        }

        await db.SaveChangesAsync();

        // Questions of every kind, in no order: asked before, in and after a window, answered
        // by a person, lapsed, lapsed and then answered, and answered more than once.
        for (var n = 0; n < 120; n++)
        {
            var issue = AnIssue();
            var askedAt = Time();
            var question = new EfHatchComment
            {
                IssueId = issue.Id, Author = "ann", Body = "which way?", Kind = EfHatchComment.Question, CreatedAt = askedAt,
            };
            db.Comments.Add(question);
            await db.SaveChangesAsync();

            void Answer(string author, DateTimeOffset at, bool lapse = false)
            {
                db.Comments.Add(new EfHatchComment
                {
                    IssueId = issue.Id, Author = author, Body = "that way", Kind = EfHatchComment.Answer,
                    AnswersId = question.Id, CreatedAt = at,
                });
                if (lapse)
                    db.IssueEvents.Add(new EfHatchIssueEvent
                    {
                        IssueId = issue.Id, Actor = author, Kind = EfHatchIssueEvent.Answered, At = at,
                        Payload = $"{{\"questionId\":{question.Id},\"lapsed\":true}}",
                    });
            }

            var later = askedAt.AddMinutes(rng.Next(1, 60 * 24 * 20));
            var latest = later.AddMinutes(rng.Next(1, 60 * 24 * 20));
            switch (n % 6)
            {
                case 0: break; // never answered
                case 1: Answer("bo", later); break;
                case 2: Answer("Hatch", later, lapse: true); break;
                case 3: Answer("Hatch", later, lapse: true); Answer("bo", latest); break; // lapsed, then a person
                case 4: Answer("bo", later); Answer("cy", latest); break; // two people
                default: Answer("Hatch", later, lapse: true); Answer("bo", later.AddMinutes(1)); Answer("cy", latest); break;
            }

            await db.SaveChangesAsync();
        }
    }

    private static async Task<List<TimelineReport.Change>> BoundedAsync(
        HatchContext db, DateTimeOffset since, DateTimeOffset until, HashSet<string>? scope) =>
        (await BoardHealthTrail.ReadAsync(
                db, [EfHatchIssueEvent.StatusChanged], since, scope, null, default, until: until))
            .Select(e => TimelineReport.ParseChange(e.IssueId, e.Id, e.At, e.Payload))
            .OfType<TimelineReport.Change>()
            .ToList();

    private static void Compare<T>(
        List<string> failures, string report, int window, DateTimeOffset start, DateTimeOffset end, T whole, T bounded)
    {
        var (a, b) = (JsonSerializer.Serialize(whole), JsonSerializer.Serialize(bounded));
        if (a != b)
            failures.Add($"{report}, window {window} ({start:O} to {end:O}) differs:{Environment.NewLine}  whole:   {a}{Environment.NewLine}  bounded: {b}");
    }

    /// <summary>The windows: the awkward ones by hand, the rest from the moves' own instants and from the clock.</summary>
    private static List<(DateTimeOffset Start, DateTimeOffset End)> WindowsOf(
        Random rng, List<DateTimeOffset> instants, DateTimeOffset first)
    {
        var all = instants.Order().ToList();
        var (earliest, latest) = (all[0], all[^1]);

        var windows = new List<(DateTimeOffset, DateTimeOffset)>
        {
            (first.AddDays(-200), first.AddDays(-150)),          // before everything
            (latest.AddDays(30), latest.AddDays(60)),            // after everything
            (earliest.AddDays(-5), latest.AddDays(5)),           // all of it
            (all[10], all[10].AddHours(1)),                      // an hour, starting on a move
            (all[20].AddHours(-1), all[20]),                     // an hour, ending on a move
            (all[30], all[30].AddMinutes(1)),                    // one minute, on a move
            (Base.AddDays(35), Base.AddDays(70)),                // after the moves into a column from itself, over the exits
            (Base.AddDays(32), Base.AddDays(57)),
            (Base.AddDays(41), Base.AddDays(70)),
        };

        // From a day-and-hour grid with the moves' own instants mixed in, so a
        // window often starts or ends exactly on a move.
        DateTimeOffset Pick() => rng.Next(3) == 0
            ? Base.AddHours(rng.Next(-40 * 24, 130 * 24))
            : all[rng.Next(all.Count)];

        while (windows.Count < Windows)
        {
            var (a, b) = (Pick(), Pick());
            if (a == b) b = a.AddHours(rng.Next(1, 400));
            windows.Add(a < b ? (a, b) : (b, a));
        }

        return windows;
    }

    private static async Task<List<TimelineReport.Row>> LoadRowsAsync(HatchContext db) =>
        (await db.Issues.AsNoTracking()
                .Select(i => new
                {
                    i.Id, i.ParentId, i.ProjectId, ProjectKey = i.Project!.Key, i.Number, i.Type, i.StatusId, i.Rank,
                    i.CreatedAt, i.CreatedBy,
                })
                .ToListAsync())
            .Select(i => new TimelineReport.Row(
                i.Id, i.ParentId, i.ProjectId, i.ProjectKey, i.Number, i.Type, i.StatusId, i.Rank, i.CreatedAt, i.CreatedBy))
            .ToList();

    /// <summary>The instants of every move written, for building windows on.</summary>
    private static async Task<List<DateTimeOffset>> SeedAsync(HatchContext db, Random rng)
    {
        var projects = new[]
        {
            new EfHatchProject { Key = "AA", Name = "A", CreatedAt = Base },
            new EfHatchProject { Key = "BB", Name = "B", CreatedAt = Base },
        };
        var inbox = new EfHatchStatus { Name = "inbox", SortOrder = 10 };
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        var doing = new EfHatchStatus { Name = "doing", SortOrder = 30, IsWip = true, IsImplementation = true };
        var review = new EfHatchStatus { Name = "review", SortOrder = 35, IsWip = true };
        var done = new EfHatchStatus { Name = "done", SortOrder = 40, IsTerminal = true };
        var later = new EfHatchStatus { Name = "later", SortOrder = 50, IsDeferred = true };
        var gone = new EfHatchStatus { Name = "gone", SortOrder = 33, IsWip = true };
        db.AddRange(projects);
        db.AddRange(inbox, todo, doing, review, done, later, gone);
        await db.SaveChangesAsync();

        EfHatchStatus[] columns = [inbox, todo, doing, review, done, later, gone];

        // ---- Issues: epics, stories under them, tasks under those, and loose tasks ----
        var numbers = new Dictionary<int, int>();
        var rank = 0L;

        EfHatchIssue Issue(string type, EfHatchProject project, long? parentId, DateTimeOffset createdAt, EfHatchStatus at) => new()
        {
            ProjectId = project.Id, Number = numbers[project.Id] = numbers.GetValueOrDefault(project.Id) + 1,
            Type = type, Title = type, Description = "", StatusId = at.Id, Rank = ++rank, ParentId = parentId,
            CreatedBy = rng.Next(2) == 0 ? "ann" : "bo", CreatedAt = createdAt, UpdatedAt = createdAt,
        };

        DateTimeOffset Created() => Base.AddHours(rng.Next(-60 * 24, 30 * 24));

        var epics = Enumerable.Range(0, 4).Select(i => Issue("epic", projects[i % 2], null, Created(), doing)).ToList();
        db.Issues.AddRange(epics);
        await db.SaveChangesAsync();

        var stories = Enumerable.Range(0, 12)
            .Select(i => Issue("story", projects[rng.Next(2)], epics[i % epics.Count].Id, Created(), doing)).ToList();
        db.Issues.AddRange(stories);
        await db.SaveChangesAsync();

        var tasks = Enumerable.Range(0, 36)
            .Select(i => Issue("task", projects[rng.Next(2)], i % 9 == 8 ? epics[i % epics.Count].Id : stories[i % stories.Count].Id, Created(), doing))
            .ToList();
        var loose = Enumerable.Range(0, 8).Select(i => Issue("task", projects[i % 2], null, Created(), doing)).ToList();
        db.Issues.AddRange(tasks);
        db.Issues.AddRange(loose);
        await db.SaveChangesAsync();

        var everyIssue = epics.Concat(stories).Concat(tasks).Concat(loose).ToList();

        // Five issues for the flow report's stays, written without the generator so its
        // draws are the ones the other trails always had. Each is created before any window.
        var hand = Enumerable.Range(0, 5).Select(n => new EfHatchIssue
        {
            ProjectId = projects[n % 2].Id, Number = numbers[projects[n % 2].Id] = numbers[projects[n % 2].Id] + 1,
            Type = "task", Title = "task", Description = "", StatusId = doing.Id, Rank = ++rank,
            CreatedBy = "ann", CreatedAt = Base.AddDays(-30 - n), UpdatedAt = Base.AddDays(-30 - n),
        }).ToList();
        db.Issues.AddRange(hand);
        await db.SaveChangesAsync();
        var (intoItself, flicker, flickerBack, firstThenItself, firstItself) = (hand[0], hand[1], hand[2], hand[3], hand[4]);

        // ---- Moves ----
        var instants = new List<DateTimeOffset>();

        // Written as IssueEventPayload.StatusChanged writes it, or, for an old event, with names alone.
        string Payload(EfHatchStatus? from, EfHatchStatus to, bool namesOnly) =>
            JsonSerializer.Serialize(namesOnly
                ? new Dictionary<string, object?> { ["from"] = from?.Name, ["to"] = to.Name }
                : IssueEventPayload.StatusChanged(from, to));

        EfHatchIssueEvent Move(EfHatchIssue issue, EfHatchStatus? from, EfHatchStatus to, DateTimeOffset at, bool namesOnly = false)
        {
            instants.Add(at);
            return new EfHatchIssueEvent
            {
                IssueId = issue.Id, Actor = "ann", Kind = EfHatchIssueEvent.StatusChanged, At = at,
                Payload = Payload(from, to, namesOnly),
            };
        }

        // Hours on a grid, so two issues, and sometimes one issue, move at one instant.
        var generated = new List<EfHatchIssueEvent>();
        foreach (var issue in everyIssue.Skip(1).Take(everyIssue.Count - 7))
        {
            var from = columns[rng.Next(5)];
            var at = issue.CreatedAt;

            // A fifth of the issues move once, the rest a handful of times.
            var count = rng.Next(5) == 0 ? 1 : rng.Next(2, 9);
            for (var n = 0; n < count; n++)
            {
                at = at.AddHours(rng.Next(0, 24 * 14));
                var to = columns[rng.Next(columns.Length)];
                generated.Add(Move(issue, n == 0 && rng.Next(4) == 0 ? null : from, to, at, namesOnly: rng.Next(12) == 0 && to != gone));
                from = to;
            }
        }

        // Written in a shuffled order, so ids and times disagree all over the trail.
        var shuffled = generated.OrderBy(_ => rng.Next()).ToList();

        // A kind that is not a move, which the read must leave alone.
        var noise = everyIssue.Select(i => new EfHatchIssueEvent
        {
            IssueId = i.Id, Actor = "ann", Kind = EfHatchIssueEvent.Created, At = i.CreatedAt,
        });

        // The trails the cut could get wrong, written by hand on the last issues.
        // The first issue never moves; the last six are written by hand and get no generated moves.
        var (a, b, c, d, e, f) = (loose[^1], loose[^2], loose[^3], loose[^4], loose[^5], loose[^6]);
        var day = (int n) => Base.AddDays(n);
        var planted = new List<EfHatchIssueEvent>
        {
            // First move after every window that ends before day 125.
            Move(a, todo, doing, day(125)),

            // Two moves at one instant, before the windows' starts and inside them.
            Move(b, inbox, todo, day(20)), Move(b, todo, doing, day(20)),
            Move(b, doing, review, day(50)), Move(b, review, doing, day(50)),
            Move(b, doing, done, day(65)),

            // Into a column since deleted, and out of it.
            Move(c, todo, gone, day(15)), Move(c, gone, doing, day(40)), Move(c, doing, gone, day(60)),

            // An event from before ids were written: names only.
            Move(d, todo, doing, day(10), namesOnly: true), Move(d, doing, review, day(35), namesOnly: true),

            // Higher id, earlier time: the later move is written first.
            Move(e, doing, review, day(52)), Move(e, todo, doing, day(51)), Move(e, review, done, day(53)),

            // Moves at the instants windows start and end on.
            Move(f, inbox, todo, day(30)), Move(f, todo, doing, day(45)),

            // A move from a column into itself, after the stay began and before a window
            // that starts after it: the stay began on day 10, not at the creation.
            Move(intoItself, todo, doing, day(10)), Move(intoItself, doing, doing, day(30)), Move(intoItself, doing, review, day(55)),

            // Two moves at one instant that leave a stay of no length: doing, review, doing.
            // The stay in doing began on day 20 and is one stay.
            Move(flicker, inbox, todo, day(5)), Move(flicker, todo, doing, day(20)),
            Move(flicker, doing, review, day(40)), Move(flicker, review, doing, day(40)), Move(flicker, doing, done, day(60)),

            // The same two moves the other way round, so the one written last leaves the
            // issue in review again: the stay in review began on day 8.
            Move(flickerBack, inbox, review, day(8)), Move(flickerBack, review, doing, day(25)),
            Move(flickerBack, doing, review, day(25)), Move(flickerBack, review, done, day(58)),

            // A stay merging back to the issue's first move.
            Move(firstThenItself, doing, review, day(12)), Move(firstThenItself, review, review, day(30)), Move(firstThenItself, review, done, day(55)),

            // A move from a column into itself as the issue's first move: the stay is the creation's.
            Move(firstItself, review, review, day(30)), Move(firstItself, review, done, day(56)),
        };

        db.IssueEvents.AddRange(noise);
        await db.SaveChangesAsync();
        db.IssueEvents.AddRange(shuffled);
        await db.SaveChangesAsync();
        db.IssueEvents.AddRange(planted);
        await db.SaveChangesAsync();

        // The column the issue's status names must exist when it is read, and the one
        // the trails moved into must not.
        db.Statuses.Remove(gone);
        await db.SaveChangesAsync();

        // Its current column is where it last moved to, where it moved at all.
        var last = (await db.IssueEvents.AsNoTracking()
                .Where(x => x.Kind == EfHatchIssueEvent.StatusChanged)
                .ToListAsync())
            .GroupBy(x => x.IssueId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.At).ThenBy(x => x.Id).Last());
        var byId = columns.Where(x => x != gone).ToDictionary(x => x.Name, x => x.Id);
        foreach (var issue in await db.Issues.ToListAsync())
        {
            if (last.TryGetValue(issue.Id, out var move)
                && JsonDocument.Parse(move.Payload!).RootElement.GetProperty("to").GetString() is { } name
                && byId.TryGetValue(name, out var statusId))
                issue.StatusId = statusId;
            else
                issue.StatusId = doing.Id;
        }

        await db.SaveChangesAsync();

        return instants.Distinct().ToList();
    }
}

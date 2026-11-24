using System.Globalization;
using System.Text.RegularExpressions;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Api.Tests.Auth;
using Hatch.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// A scan reads the columns it judges by and nothing else (HA-2795): no
/// statement of it selects a description or the search vector, except one that
/// reads the text of the rows whose brief could be over a playbook's limit, and
/// the route that answers with descriptions reads them for itself.
///
/// <para>On Postgres, because the thing under test is the SQL, and because it is
/// the only place where <c>length()</c> and <see cref="string.Length"/> can
/// disagree: Postgres counts characters and .NET counts UTF-16 units, so a
/// character outside the Basic Multilingual Plane is one and two.</para>
///
/// <para>It points at <c>HATCH_TEST_DATABASE_URL</c>, which is dropped and
/// recreated, so it must never name a database anybody uses.</para>
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class ScanReadsNoDescriptionTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    private const string SkipReason =
        "HATCH_TEST_DATABASE_URL is unset - the counts come from a real Postgres. Run `make test-api-db`, " +
        "or point the variable at a scratch database that may be dropped.";

    /// <summary>U+1F600: one character to Postgres, two UTF-16 units to .NET.</summary>
    private const string Emoji = "\U0001F600";

    private const int Limit = 1000;

    /// <summary>The rows, by title: what each holds, so a test says what it expects of each.</summary>
    private static readonly (string Title, string Description, bool InDoing)[] Rows =
    [
        ("short", new string('a', 10), false),                                   // 2n = 20: never over
        ("mid", new string('a', 600), false),                                    // 2n = 1,200, 600 units: its text is read, and is not over
        ("over", new string('a', 1001), false),                                  // one unit over
        ("emoji at", string.Concat(Enumerable.Repeat(Emoji, 500)), false),       // 500 characters, 1,000 units: at the limit. 2n = 1,000, so never read
        ("emoji over", string.Concat(Enumerable.Repeat(Emoji, 500)) + "x", false), // 501 characters, 1,001 units: one over
        ("emoji big", string.Concat(Enumerable.Repeat(Emoji, 600)), false),      // 600 characters, 1,200 units
        ("send at", string.Concat(Enumerable.Repeat(Emoji, 500)), true),         // in progress, at the limit: not sent back
        ("send over", string.Concat(Enumerable.Repeat(Emoji, 500)) + "x", true), // in progress, one over: sent back
    ];

    private static string Sentence(int length) =>
        $"its brief is {length.ToString("N0", CultureInfo.InvariantCulture)} characters, " +
        $"and this move is spawned on at most {Limit.ToString("N0", CultureInfo.InvariantCulture)} - cut it into tasks first";

    // ---- The SQL ----

    [SkippableFact]
    public async Task AScan_ReadsADescriptionOnlyForTheRowsWhoseBriefCouldBeOverTheLimit()
    {
        Skip.IfNot(HatchDatabase.Available, SkipReason);
        var seeded = await SeedAsync(Limit);

        var tracked = -1;
        var measured = await ReadCounter.MeasureAsync(seeded.ConnectionString, async db =>
        {
            await ScanAsync(db);
            tracked = db.ChangeTracker.Entries<EfHatchIssue>().Count();
        });

        // Read only where 2n is over the limit: mid, over, emoji over, emoji big, send over.
        var needed = Rows.Count(r => 2 * r.Description.EnumerateRunes().Count() > Limit);
        Assert.Equal(5, needed);
        AssertDescriptionsRead(measured.Statements, expectedRows: needed);

        // Nothing a fold does to a row can be written back.
        Assert.Equal(0, tracked);
    }

    [SkippableFact]
    public async Task AScanOnABoardWithNoBriefLimit_ReadsNoDescriptionAtAll()
    {
        Skip.IfNot(HatchDatabase.Available, SkipReason);
        var seeded = await SeedAsync(briefLimit: null);

        var measured = await ReadCounter.MeasureAsync(seeded.ConnectionString, ScanAsync);

        AssertDescriptionsRead(measured.Statements, expectedRows: 0);
    }

    [SkippableFact]
    public async Task ARepoSourcedProjectsFileRowWithASmallerLimit_ReadsTheTextOfTheRowsItCouldFold()
    {
        Skip.IfNot(HatchDatabase.Available, SkipReason);

        // The table holds no limit at all, so only the file's own row can put a
        // description in reach of a fold - and it is not known until the project is resolved.
        var seeded = await SeedAsync(briefLimit: null, fileLimit: 100);

        Scan? scan = null;
        var measured = await ReadCounter.MeasureAsync(seeded.ConnectionString, async db => scan = await ScanAsync(db));

        // 60 characters: 2n = 120 is over 100, so the text is read, and 60 is not over. 101 is.
        AssertDescriptionsRead(measured.Statements, expectedRows: 2);
        var rows = scan!.Rows.Where(r => r.Issue.Project!.Key == "REP").ToDictionary(r => r.Issue.Title);
        Assert.Null(rows["file mid"].Blocked);
        Assert.Equal(
            "its brief is 101 characters, and this move is spawned on at most 100 - cut it into tasks first",
            rows["file over"].Blocked);
        Assert.Equal(0, scan.Rows.Count(r => r.Issue.Project!.Key == "AER" && r.Blocked is not null));
    }

    // ---- The sentence ----

    [SkippableFact]
    public async Task TheBriefLimitSentence_CountsUtf16UnitsAsItAlwaysDid_NotCharacters()
    {
        Skip.IfNot(HatchDatabase.Available, SkipReason);
        var seeded = await SeedAsync(Limit);

        Scan? scan = null;
        await ReadCounter.MeasureAsync(seeded.ConnectionString, async db => scan = await ScanAsync(db));
        var byTitle = scan!.Rows.Where(r => r.Issue.Project!.Key == "AER").ToDictionary(r => r.Issue.Title);

        // Under, at and over, in ASCII.
        Assert.Null(byTitle["short"].Blocked);
        Assert.Null(byTitle["mid"].Blocked);
        Assert.Equal(Sentence(1001), byTitle["over"].Blocked);

        // The same, outside the Basic Multilingual Plane: 500 characters are 1,000 units, so at the limit.
        Assert.Null(byTitle["emoji at"].Blocked);
        Assert.Equal(Sentence(1001), byTitle["emoji over"].Blocked);
        Assert.Equal(Sentence(1200), byTitle["emoji big"].Blocked);
        Assert.Equal(BlockedKinds.Other, byTitle["emoji over"].BlockedKind);
    }

    [SkippableFact]
    public async Task TheSendBackFold_AgreesWithTheSentence_AtTheLimitAndOverIt()
    {
        Skip.IfNot(HatchDatabase.Available, SkipReason);
        var seeded = await SeedAsync(Limit);

        Scan? scan = null;
        await ReadCounter.MeasureAsync(seeded.ConnectionString, async db => scan = await ScanAsync(db));
        var byTitle = scan!.Rows.Where(r => r.Issue.Project!.Key == "AER").ToDictionary(r => r.Issue.Title);

        Assert.False(byTitle["send at"].Hop);
        Assert.True(byTitle["send over"].Hop);
        Assert.Equal(HopKinds.SendBack, byTitle["send over"].HopKind);
    }

    // ---- The routes ----

    [SkippableFact]
    public async Task TheQueue_ByDefault_AnswersEveryDescription_FromOneReadOfItsOwn()
    {
        Skip.IfNot(HatchDatabase.Available, SkipReason);
        var seeded = await SeedAsync(briefLimit: null);

        IReadOnlyList<QueueEntryDto>? queue = null;
        var measured = await ReadCounter.MeasureAsync(seeded.ConnectionString, async db =>
        {
            using var scope = ScanTally.Begin("GET /api/work/queue");
            queue = (await NewController(db).GetQueue()).Value;
        });

        Assert.NotNull(queue);
        Assert.Equal(Rows.Length, queue.Count);
        foreach (var entry in queue)
            Assert.Equal(seeded.Descriptions[entry.Issue.Key], entry.Issue.Description);
        Assert.Contains(queue, e => e.Issue.Description.Contains(Emoji, StringComparison.Ordinal));

        // The scan read none, and one statement read every description at once.
        var readers = measured.Statements.Where(IsTextRead).ToList();
        var reader = Assert.Single(readers);
        Assert.Equal(queue.Count, reader.Rows);
    }

    [SkippableFact]
    public async Task TheQueue_WithDescriptionsFalse_LeavesEveryDescriptionEmpty_AndReadsNone()
    {
        Skip.IfNot(HatchDatabase.Available, SkipReason);
        var seeded = await SeedAsync(briefLimit: null);

        IReadOnlyList<QueueEntryDto>? queue = null;
        var measured = await ReadCounter.MeasureAsync(seeded.ConnectionString, async db =>
        {
            using var scope = ScanTally.Begin("GET /api/work/queue");
            queue = (await NewController(db).GetQueue(descriptions: false)).Value;
        });

        Assert.NotNull(queue);
        Assert.Equal(Rows.Length, queue.Count);
        Assert.All(queue, entry => Assert.Equal("", entry.Issue.Description));
        AssertDescriptionsRead(measured.Statements, expectedRows: 0);
    }

    [SkippableFact]
    public async Task TheQueue_WithDescriptionsFalse_StillSaysWhyABriefIsTooLong()
    {
        Skip.IfNot(HatchDatabase.Available, SkipReason);
        var seeded = await SeedAsync(Limit);

        IReadOnlyList<QueueEntryDto>? queue = null;
        await ReadCounter.MeasureAsync(seeded.ConnectionString, async db =>
        {
            using var scope = ScanTally.Begin("GET /api/work/queue");
            queue = (await NewController(db).GetQueue(descriptions: false)).Value;
        });

        var over = queue!.Single(e => e.Issue.Title == "emoji over" && e.Issue.Key.StartsWith("AER-", StringComparison.Ordinal));
        Assert.Equal(Sentence(1001), over.Blocked);
        Assert.Equal("", over.Issue.Description);
    }

    [SkippableFact]
    public async Task Next_AnswersTheFullDescriptionOfTheOneIssueItReturns()
    {
        Skip.IfNot(HatchDatabase.Available, SkipReason);
        var seeded = await SeedAsync(briefLimit: null);

        WorkDto? work = null;
        var measured = await ReadCounter.MeasureAsync(seeded.ConnectionString, async db =>
        {
            using var scope = ScanTally.Begin("GET /api/work/next");
            work = (await NewController(db).GetNextWork()).Value;
        });

        Assert.NotNull(work);
        Assert.Equal(seeded.Descriptions[work.Issue.Key], work.Issue.Description);
        Assert.NotEqual("", work.Issue.Description);

        // Its own read, of one row.
        var reader = Assert.Single(measured.Statements.Where(IsTextRead));
        Assert.Equal(1, reader.Rows);
    }

    // ---- What the SQL may say ----

    /// <summary><c>length(i."Description")</c>, the one place the candidate read names the column.</summary>
    private static readonly Regex LengthOfDescription = new(
        @"length\(\w+\.""Description""\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>A statement that selects the text: the column outside <c>length()</c>.</summary>
    private static bool IsTextRead(ReadCounter.Statement s) =>
        LengthOfDescription.Replace(s.Sql, "").Contains("\"Description\"", StringComparison.Ordinal);

    /// <summary>
    /// No statement selects the search vector. No statement selects the text, bar
    /// one that selects only an id and the text, and which returned
    /// <paramref name="expectedRows"/> rows - or none, where that is zero.
    /// </summary>
    private static void AssertDescriptionsRead(IReadOnlyList<ReadCounter.Statement> statements, int expectedRows)
    {
        foreach (var statement in statements)
            Assert.DoesNotContain("SearchVector", statement.Sql);

        var readers = statements.Where(IsTextRead).ToList();
        if (expectedRows == 0)
        {
            Assert.True(readers.Count == 0,
                "a statement selects a description:" + Environment.NewLine + string.Join(Environment.NewLine + "--" + Environment.NewLine, readers.Select(r => r.Sql)));
            return;
        }

        var reader = Assert.Single(readers);
        var selected = reader.Sql[..reader.Sql.IndexOf("FROM", StringComparison.Ordinal)];
        Assert.Matches(@"^SELECT\s+\w+\.""Id"",\s+\w+\.""Description""\s*$", selected.Trim());
        Assert.Equal(expectedRows, reader.Rows);
    }

    // ---- Calling ----

    private static Task<Scan> ScanAsync(HatchContext db) =>
        new Dispatch(db, new StubActorDirectory(), TestClaims.With(), new FakeTimeProvider(Now))
            .ScanAsync(new ScanCaller("test"), 0, null, null, RepositoryDeclaration.Undeclared, false, default);

    private static WorkController NewController(HatchContext db)
    {
        var time = new FakeTimeProvider(Now);
        var authOptions = Options.Create(new AuthOptions());
        var runners = new Runners(Options.Create(new HatchOptions()), authOptions, new StubHatchRevision());

        // The keys are checked against an in-memory database, which the counter does not see.
        var coreDb = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var keys = new AuthService(coreDb, authOptions, time, Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthService>.Instance);

        return new WorkController(
            db, new StubActorDirectory(), TestClaims.With(), time, Options.Create(new HatchOptions()),
            new RankService(db), new StubCallerIdentity(), runners, keys, new RunnerSessions(runners));
    }

    // ---- The board ----

    private sealed record Seeded(string ConnectionString, IReadOnlyDictionary<string, string> Descriptions);

    /// <summary>
    /// Project AER holds <see cref="Rows"/>: the to do ones in to do, the others in progress,
    /// with a playbook for each column that carries <paramref name="briefLimit"/>.
    /// With <paramref name="fileLimit"/>, project REP is repo-sourced, and its file row carries
    /// that limit; REP holds two stories in to do, one under it and one over.
    /// </summary>
    private static async Task<Seeded> SeedAsync(int? briefLimit, int? fileLimit = null)
    {
        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var inbox = new EfHatchStatus { Name = "inbox", SortOrder = 10 };
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        var doing = new EfHatchStatus { Name = "doing", SortOrder = 30, IsWip = true, IsImplementation = true };
        var review = new EfHatchStatus { Name = "review", SortOrder = 35, IsWip = true };
        var done = new EfHatchStatus { Name = "done", SortOrder = 40, IsTerminal = true };
        db.AddRange(inbox, todo, doing, review, done);

        var aer = new EfHatchProject { Key = "AER", Name = "Alpha", CreatedAt = Now.AddDays(-90) };
        db.Projects.Add(aer);

        EfHatchProject? rep = null;
        EfHatchProjectRepository? repository = null;
        if (fileLimit is not null)
        {
            const string remote = "https://forge.test/acme/repo.git";
            var (canonical, _) = RemoteIdentity.Canonical(remote);
            repository = new EfHatchProjectRepository { Remote = remote, Canonical = canonical!, BaseBranch = "main", SortOrder = 0, CreatedAt = Now.AddDays(-90) };
            rep = new EfHatchProject { Key = "REP", Name = "Repo sourced", CreatedAt = Now.AddDays(-90), RepoSourced = true, Repositories = [repository] };
            db.Projects.Add(rep);
        }

        await db.SaveChangesAsync();

        EfHatchPlaybook Playbook(int from, int to) => new()
        {
            FromStatusId = from, ToStatusId = to, Types = "", Shape = "any", Prompt = "do the thing", Model = "sonnet", Effort = "high",
            BriefLimit = briefLimit, DeniedTools = EfHatchPlaybook.WriteDeniedTools(null), CreatedAt = Now, UpdatedAt = Now,
        };

        db.Playbooks.AddRange(Playbook(todo.Id, doing.Id), Playbook(doing.Id, review.Id));

        var descriptions = new Dictionary<string, string>();
        var number = 0;
        var rank = 0L;

        void File(EfHatchProject project, string title, string description, EfHatchStatus status, ref int n)
        {
            n++;
            db.Issues.Add(new EfHatchIssue
            {
                ProjectId = project.Id, Number = n, Type = "story", Title = title, Description = description, StatusId = status.Id,
                Rank = 1024L * ++rank, CreatedBy = "bo", CreatedAt = Now.AddDays(-30), UpdatedAt = Now.AddDays(-1),
            });
            descriptions[$"{project.Key}-{n}"] = description;
        }

        foreach (var (title, description, inDoing) in Rows) File(aer, title, description, inDoing ? doing : todo, ref number);

        if (rep is not null)
        {
            var n = 0;
            File(rep, "file mid", new string('a', 60), todo, ref n);
            File(rep, "file over", new string('a', 101), todo, ref n);

            db.PlaybookFiles.Add(new EfHatchPlaybookFile
            {
                Remote = repository!.Remote, Canonical = repository.Canonical, Trunk = "main", Sha = new string('a', 40),
                State = EfHatchPlaybookFile.ParsedState, Hash = "hash",
                Rows = EfHatchPlaybookFile.WriteRows(
                [
                    new PlaybookFileRow(todo.Id, doing.Id, [], "any", "do the thing", "sonnet", "high", null, fileLimit, [], false),
                ]),
                CheckedAt = Now, Runner = "host:/checkout", CheckedBy = "runner",
            });
        }

        await db.SaveChangesAsync();
        return new Seeded(connectionString, descriptions);
    }
}

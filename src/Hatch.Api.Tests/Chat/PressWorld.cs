using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Api.Services.Chat;
using Hatch.Api.Tests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Chat;

/// <summary>
/// One project, one workspace with two question rooms, one issue and one question
/// whose message has been "sent" to both rooms, plus a linked person: what a press
/// on a question needs, on any provider. A press is built by hand; nothing calls Slack.
/// </summary>
internal sealed class PressWorld
{
    public static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    public const string Member = "U1";
    public const string ChannelOne = "C1";
    public const string ChannelTwo = "C2";
    public const string MessageOne = "1700000000.000100";
    public const string MessageTwo = "1700000000.000200";

    public static readonly QuestionOptionDto[] DefaultOptions =
    [
        new("leaf-weighted", Recommended: true),
        new("child-weighted"),
    ];

    public int ProjectId { get; init; }
    public int WorkspaceId { get; init; }
    public int RoomOne { get; init; }
    public int RoomTwo { get; init; }
    public long IssueId { get; init; }
    public long QuestionId { get; init; }
    public Guid Person { get; init; }
    public string PersonName { get; init; } = "";
    public string IssueKey => "HA-1";

    /// <summary>The press a button on the first room's message makes, choosing the option at <paramref name="place"/>.</summary>
    public ChatPress Press(int place = 0, string member = Member) =>
        new(member, ChannelOne, MessageOne, ChatPressAction.Write(QuestionId, place), null, "https://hooks.example/reply-address");

    public ChatPress PressRecommended(string member = Member) =>
        new(member, ChannelOne, MessageOne, ChatPressAction.WriteRecommended(QuestionId), null, "https://hooks.example/reply-address");

    public static QuestionPresses Presses(HatchContext db, AppDbContext appDb, bool wallOn = false)
    {
        var options = Options.Create(new AuthOptions { Enabled = wallOn });
        var time = new FakeTimeProvider(Now);
        var links = new ChatLinks(Options.Create(new HatchOptions()));
        var answers = new QuestionAnswers(db, time, new ChatOutbox(db, time), links);

        return new QuestionPresses(db, RosterSeed.For(db, appDb, wallOn), answers, options);
    }

    /// <summary>
    /// Seeds the world. <paramref name="level"/> and <paramref name="terminal"/> make a look back
    /// when they are <c>merge</c> and true. The person is in <paramref name="appDb"/> and linked
    /// to <see cref="Member"/> in the workspace.
    /// </summary>
    public static async Task<PressWorld> SeedAsync(
        HatchContext db, AppDbContext appDb, string level = QuestionLevels.HoldName, bool terminal = false,
        IReadOnlyList<QuestionOptionDto>? options = null, string personName = "Ada", bool link = true)
    {
        var seed = new ChatOutboxSeed(db, Now);
        var project = await seed.ProjectAsync("HA");
        var workspace = await seed.WorkspaceAsync("TACME", project);
        var roomOne = await seed.RoomAsync(workspace, ChannelOne, questions: true);
        var roomTwo = await seed.RoomAsync(workspace, ChannelTwo, questions: true);

        var person = new EfPerson { Name = personName, Role = PersonRole.User, CreatedAt = Now, UpdatedAt = Now };
        appDb.People.Add(person);
        await appDb.SaveChangesAsync();
        if (link) await seed.LinkAsync(workspace, person.Id, Member);

        var inProgress = new EfHatchStatus { Name = "In Progress", SortOrder = 20 };
        var done = new EfHatchStatus { Name = "Done", SortOrder = 40, IsTerminal = true };
        db.Statuses.AddRange(inProgress, done);
        await db.SaveChangesAsync();

        var issue = new EfHatchIssue
        {
            ProjectId = project, Number = 1, Type = "story", Title = "Rank weighting", Description = "",
            StatusId = terminal ? done.Id : inProgress.Id, Rank = 1024,
            CreatedBy = "operator", CreatedAt = Now, UpdatedAt = Now,
        };
        db.Issues.Add(issue);
        await db.SaveChangesAsync();

        var stored = options ?? DefaultOptions;
        var question = new EfHatchComment
        {
            IssueId = issue.Id, Author = "hatch-agent", AuthorKind = ActorKind.Key, Body = "leaf-weighted or child-weighted?",
            Kind = EfHatchComment.Question, Level = level, Options = Questions.WriteOptions(stored), CreatedAt = Now,
        };
        db.Comments.Add(question);
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var content = QuestionChatContent.Build(
            new ChatLinks(Options.Create(new HatchOptions())), "HA-1", issue.Title, question.Body,
            Questions.ReadOptions(question.Options), level);
        await new ChatOutbox(db, time).QueueAsync(ChatMessageKind.Question, $"comment:{question.Id}", project, content, default);
        await db.SaveChangesAsync();

        // The sender has delivered both: each row knows the service's id for its message.
        foreach (var row in await db.ChatMessages.ToListAsync())
        {
            row.ExternalId = row.RoomId == roomOne ? MessageOne : MessageTwo;
            row.SentRevision = row.Revision;
        }
        await db.SaveChangesAsync();

        return new PressWorld
        {
            ProjectId = project, WorkspaceId = workspace, RoomOne = roomOne, RoomTwo = roomTwo,
            IssueId = issue.Id, QuestionId = question.Id, Person = person.Id, PersonName = personName,
        };
    }

    /// <summary>What a press may change, counted: comments, events and each queued row's revision.</summary>
    public static async Task<string> SnapshotAsync(HatchContext db) =>
        $"comments={await db.Comments.CountAsync()};events={await db.IssueEvents.CountAsync()};revisions="
        + string.Join(",", (await db.ChatMessages.IgnoreQueryFilters().AsNoTracking().OrderBy(m => m.Id).Select(m => m.Revision).ToListAsync()));
}

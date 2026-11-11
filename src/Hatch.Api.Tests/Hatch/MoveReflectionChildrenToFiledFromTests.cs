using System.Text.Json;
using System.Text.RegularExpressions;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Modules.Hatch.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="MoveReflectionChildrenToFiledFrom"/> against real rows.
/// <see cref="HatchDatabase"/> builds its scratch schema from the model and
/// never runs a migration's <c>Up()</c>, so these lift the one SQL operation out
/// of it and run that exact text. The facts that read the SQL alone need no
/// database; the rest skip where <c>HATCH_TEST_DATABASE_URL</c> is unset.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class MoveReflectionChildrenToFiledFromTests
{
    private static readonly DateTimeOffset T0 = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_migration_is_one_statement_over_the_issues_table_and_Down_does_nothing()
    {
        Assert.Contains("hatch.\"Issues\"", MoveSql);
        Assert.Empty(new Probe().Backward);
    }

    [Fact]
    public void The_selector_is_the_type_reflection_and_names_no_issue_project_or_installation()
    {
        Assert.Contains("p.\"Type\" = 'reflection'", MoveSql);

        // The board's actor name is the one literal in the text; take it out
        // and nothing else may look like a key, or hold a number, or a name.
        var withoutActor = MoveSql.Replace("'the board'", "");
        Assert.DoesNotMatch(new Regex(@"[A-Z]{2,}-\d+"), withoutActor);
        Assert.DoesNotMatch(new Regex(@"\b(IN|=)\s*\(?\s*\d+"), withoutActor);
        Assert.Equal(ReflectionCadenceJob.Actor, "the board");
    }

    [SkippableFact]
    public async Task Both_children_of_a_reflection_are_linked_and_unparented_and_a_story_under_an_epic_is_untouched()
    {
        await using var db = await OpenAsync();
        var w = await SeedAsync(db);

        await RunAsync(db);

        db.ChangeTracker.Clear();
        var byId = await db.Issues.AsNoTracking().ToDictionaryAsync(i => i.Id);

        foreach (var child in new[] { w.OpenChild, w.ClosedChild })
        {
            Assert.Equal(w.Reflection.Id, byId[child.Id].FiledFromId);
            Assert.Null(byId[child.Id].ParentId);
            Assert.Equal(child.StatusId, byId[child.Id].StatusId);
            Assert.Equal(child.ClosedAt, byId[child.Id].ClosedAt);
        }

        Assert.Equal(w.Epic.Id, byId[w.Story.Id].ParentId);
        Assert.Null(byId[w.Story.Id].FiledFromId);
        Assert.Equal(w.Story.UpdatedAt, byId[w.Story.Id].UpdatedAt);

        Assert.Null(byId[w.Reflection.Id].ParentId);
        Assert.Null(byId[w.Reflection.Id].FiledFromId);
        Assert.Equal(w.Reflection.UpdatedAt, byId[w.Reflection.Id].UpdatedAt);
        Assert.Null(byId[w.Epic.Id].ParentId);
    }

    [SkippableFact]
    public async Task Each_moved_child_gets_the_two_events_a_parent_change_and_a_link_change_write_by_the_board()
    {
        await using var db = await OpenAsync();
        var w = await SeedAsync(db);

        await RunAsync(db);

        foreach (var child in new[] { w.OpenChild, w.ClosedChild })
        {
            var events = await EventsAsync(db, child.Id);
            Assert.All(events, e => Assert.Equal(ReflectionCadenceJob.Actor, e.Actor));
            Assert.Equal(
                new[] { EfHatchIssueEvent.FiledFromChanged, EfHatchIssueEvent.ParentChanged },
                events.Select(e => e.Kind).OrderBy(k => k, StringComparer.Ordinal).ToArray());
            Assert.Equal((null, "RA-1"), FromTo(events, EfHatchIssueEvent.FiledFromChanged));
            Assert.Equal(("RA-1", null), FromTo(events, EfHatchIssueEvent.ParentChanged));
        }

        Assert.Empty(await EventsAsync(db, w.Story.Id));
        Assert.Empty(await EventsAsync(db, w.Reflection.Id));
        Assert.Empty(await EventsAsync(db, w.Epic.Id));
    }

    [SkippableFact]
    public async Task Running_Up_twice_writes_nothing_the_second_time()
    {
        await using var db = await OpenAsync();
        await SeedAsync(db);

        await RunAsync(db);
        var once = await SnapshotAsync(db);
        await RunAsync(db);
        var twice = await SnapshotAsync(db);

        Assert.Equal(once.Issues, twice.Issues);
        Assert.Equal(once.Events, twice.Events);
        Assert.Equal(4, once.Events.Count);
    }

    [SkippableFact]
    public async Task A_reflection_in_every_project_is_reached_and_each_event_carries_its_own_projects_key()
    {
        await using var db = await OpenAsync();
        var w = await SeedAsync(db);

        var other = new EfHatchProject { Key = "RB", Name = "Other", CreatedAt = T0 };
        db.Projects.Add(other);
        await db.SaveChangesAsync();
        var reflection = Issue(other, w.Todo, 7, "reflection", null);
        db.Issues.Add(reflection);
        await db.SaveChangesAsync();
        var child = Issue(other, w.Todo, 8, "task", reflection.Id);
        db.Issues.Add(child);
        await db.SaveChangesAsync();

        await RunAsync(db);

        db.ChangeTracker.Clear();
        var moved = await db.Issues.AsNoTracking().SingleAsync(i => i.Id == child.Id);
        Assert.Equal(reflection.Id, moved.FiledFromId);
        Assert.Null(moved.ParentId);

        var events = await EventsAsync(db, child.Id);
        Assert.Equal((null, "RB-7"), FromTo(events, EfHatchIssueEvent.FiledFromChanged));
        Assert.Equal(("RB-7", null), FromTo(events, EfHatchIssueEvent.ParentChanged));
    }

    [SkippableFact]
    public async Task A_child_already_filed_from_another_issue_records_that_issues_key_as_from()
    {
        await using var db = await OpenAsync();
        var w = await SeedAsync(db);

        var elsewhere = Issue(w.Project, w.Todo, 20, "story", null);
        db.Issues.Add(elsewhere);
        await db.SaveChangesAsync();
        var child = Issue(w.Project, w.Todo, 21, "task", w.Reflection.Id);
        child.FiledFromId = elsewhere.Id;
        db.Issues.Add(child);
        await db.SaveChangesAsync();

        await RunAsync(db);

        db.ChangeTracker.Clear();
        var moved = await db.Issues.AsNoTracking().SingleAsync(i => i.Id == child.Id);
        Assert.Equal(w.Reflection.Id, moved.FiledFromId);
        Assert.Null(moved.ParentId);
        Assert.Equal(("RA-20", "RA-1"), FromTo(await EventsAsync(db, child.Id), EfHatchIssueEvent.FiledFromChanged));
    }

    [SkippableFact]
    public async Task A_child_already_filed_from_its_reflection_loses_its_parent_and_writes_only_the_parent_event()
    {
        await using var db = await OpenAsync();
        var w = await SeedAsync(db);

        var child = Issue(w.Project, w.Todo, 22, "task", w.Reflection.Id);
        child.FiledFromId = w.Reflection.Id;
        db.Issues.Add(child);
        await db.SaveChangesAsync();

        await RunAsync(db);

        db.ChangeTracker.Clear();
        var moved = await db.Issues.AsNoTracking().SingleAsync(i => i.Id == child.Id);
        Assert.Equal(w.Reflection.Id, moved.FiledFromId);
        Assert.Null(moved.ParentId);
        Assert.Equal(
            new[] { EfHatchIssueEvent.ParentChanged },
            (await EventsAsync(db, child.Id)).Select(e => e.Kind).ToArray());
    }

    private sealed record World(
        EfHatchProject Project,
        EfHatchStatus Todo,
        EfHatchIssue Reflection,
        EfHatchIssue OpenChild,
        EfHatchIssue ClosedChild,
        EfHatchIssue Epic,
        EfHatchIssue Story);

    /// <summary>
    /// A reflection (RA-1) with an open child and a closed one, and an epic
    /// (RA-4) with a story under it - the shape the ticket's acceptance names.
    /// </summary>
    private static async Task<World> SeedAsync(HatchContext db)
    {
        var project = new EfHatchProject { Key = "RA", Name = "Reflected", CreatedAt = T0 };
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 10 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 20, IsTerminal = true };
        db.AddRange(project, todo, done);
        await db.SaveChangesAsync();

        var reflection = Issue(project, todo, 1, "reflection", null);
        var epic = Issue(project, todo, 4, "epic", null);
        db.Issues.AddRange(reflection, epic);
        await db.SaveChangesAsync();

        var open = Issue(project, todo, 2, "task", reflection.Id);
        var closed = Issue(project, done, 3, "task", reflection.Id);
        closed.ClosedAt = T0.AddDays(3);
        var story = Issue(project, todo, 5, "story", epic.Id);
        db.Issues.AddRange(open, closed, story);
        await db.SaveChangesAsync();

        return new World(project, todo, reflection, open, closed, epic, story);
    }

    private static EfHatchIssue Issue(EfHatchProject project, EfHatchStatus status, int number, string type, long? parentId) => new()
    {
        ProjectId = project.Id,
        Number = number,
        Type = type,
        Title = $"Issue {number}",
        StatusId = status.Id,
        ParentId = parentId,
        Rank = number * 1024,
        CreatedBy = "operator",
        CreatedAt = T0,
        UpdatedAt = T0.AddDays(1),
    };

    private static async Task<List<EfHatchIssueEvent>> EventsAsync(HatchContext db, long issueId)
    {
        db.ChangeTracker.Clear();
        return await db.IssueEvents.AsNoTracking().Where(e => e.IssueId == issueId).ToListAsync();
    }

    private static (string? From, string? To) FromTo(IEnumerable<EfHatchIssueEvent> events, string kind)
    {
        using var doc = JsonDocument.Parse(events.Single(e => e.Kind == kind).Payload!);
        string? Read(string name) => doc.RootElement.GetProperty(name) is { ValueKind: not JsonValueKind.Null } v ? v.GetString() : null;
        return (Read("from"), Read("to"));
    }

    private sealed record Snapshot(
        IReadOnlyList<(long Id, long? ParentId, long? FiledFromId, DateTimeOffset UpdatedAt)> Issues,
        IReadOnlyList<(long IssueId, string Kind, string? Payload, DateTimeOffset At)> Events);

    private static async Task<Snapshot> SnapshotAsync(HatchContext db)
    {
        db.ChangeTracker.Clear();
        var issues = await db.Issues.AsNoTracking().OrderBy(i => i.Id)
            .Select(i => new { i.Id, i.ParentId, i.FiledFromId, i.UpdatedAt }).ToListAsync();
        var events = await db.IssueEvents.AsNoTracking().OrderBy(e => e.Id)
            .Select(e => new { e.IssueId, e.Kind, e.Payload, e.At }).ToListAsync();
        return new Snapshot(
            issues.Select(i => (i.Id, i.ParentId, i.FiledFromId, i.UpdatedAt)).ToList(),
            events.Select(e => (e.IssueId, e.Kind, e.Payload, e.At)).ToList());
    }

    private static async Task RunAsync(HatchContext db)
    {
        await db.Database.ExecuteSqlRawAsync(MoveSql);
    }

    private static async Task<HatchContext> OpenAsync()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        return new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);
    }

    private static string MoveSql => new Probe().Forward.OfType<SqlOperation>().Single().Sql;

    private sealed class Probe : MoveReflectionChildrenToFiledFrom
    {
        public IReadOnlyList<MigrationOperation> Forward => Operations(Up);

        public IReadOnlyList<MigrationOperation> Backward => Operations(Down);

        private static IReadOnlyList<MigrationOperation> Operations(Action<MigrationBuilder> step)
        {
            var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
            step(builder);
            return builder.Operations;
        }
    }
}

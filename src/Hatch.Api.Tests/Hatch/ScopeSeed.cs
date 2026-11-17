using Hatch.Api.Modules.Hatch;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// Two projects with a row in every table the scope filters, shared by the
/// in-memory and the Postgres lane so both read one set of expectations.
/// </summary>
/// <remarks>
/// Project A and project B each own two issues and one of everything that hangs
/// off an issue. A playbook, a dream and an attachment with no project stand
/// beside them, and so do three repositories: one bound by A, one by B, and
/// one bound by both.
/// </remarks>
internal sealed record ScopeSeed(int ProjectA, int ProjectB, long IssueA, long IssueB, long DreamA, long DreamB)
{
    public const string CanonicalA = "host/a";
    public const string CanonicalB = "host/b";
    public const string CanonicalShared = "host/shared";

    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch;

    /// <summary>One filtered table and what each kind of caller should count in it.</summary>
    public sealed record Probe(string Name, Func<HatchContext, Task<int>> Count, int All, int OfOneProject, int OfNone)
    {
        /// <summary>
        /// What a bounded caller holding both projects counts: everything, bar a
        /// row that belongs to nobody, which only an unbounded caller reads.
        /// </summary>
        public int OfBoth { get; init; } = All;
    }

    /// <summary>
    /// Every table that carries a filter. <c>OfOneProject</c> is what a caller
    /// scoped to project A counts, <c>OfNone</c> what a caller scoped to nothing
    /// counts - not zero where a row belongs to no project.
    /// </summary>
    public static readonly IReadOnlyList<Probe> Probes =
    [
        new("Projects", db => db.Projects.CountAsync(), 2, 1, 0),
        new("Issues", db => db.Issues.CountAsync(), 4, 2, 0),
        new("ProjectRepositories", db => db.ProjectRepositories.CountAsync(), 4, 2, 0),
        new("ProjectMembers", db => db.ProjectMembers.CountAsync(), 2, 1, 0),
        new("ProjectLogos", db => db.ProjectLogos.CountAsync(), 2, 1, 0),
        new("Playbooks", db => db.Playbooks.CountAsync(), 3, 2, 1),
        new("Dreams", db => db.Dreams.CountAsync(), 3, 2, 1),
        new("Attachments", db => db.Attachments.CountAsync(), 3, 2, 1),
        new("Comments", db => db.Comments.CountAsync(), 2, 1, 0),
        new("IssueEvents", db => db.IssueEvents.CountAsync(), 2, 1, 0),
        new("WorkLog", db => db.WorkLog.CountAsync(), 2, 1, 0),
        new("MergeChecks", db => db.MergeChecks.CountAsync(), 2, 1, 0),
        new("BuildChecks", db => db.BuildChecks.CountAsync(), 2, 1, 0),
        new("Landings", db => db.Landings.CountAsync(), 2, 1, 0),
        new("Verdicts", db => db.Verdicts.CountAsync(), 2, 1, 0),
        new("Contributions", db => db.Contributions.CountAsync(), 2, 1, 0),
        new("RunnerOmissions", db => db.RunnerOmissions.CountAsync(), 2, 1, 0),
        new("Dependencies", db => db.Dependencies.CountAsync(), 2, 1, 0),
        new("DreamThreads", db => db.DreamThreads.CountAsync(), 2, 1, 0),
        new("DreamRemands", db => db.DreamRemands.CountAsync(), 2, 1, 0),
        new("ChangelogTasks", db => db.ChangelogTasks.CountAsync(), 2, 1, 0),

        // Five proposals: one on each project's issue, two with no issue that
        // edit a project playbook and a global one, and one with neither - nobody's,
        // so a bounded caller never reads it.
        new("PlaybookProposals", db => db.PlaybookProposals.CountAsync(), 5, 3, 1) { OfBoth = 4 },
        new("PlaybookProposalChanges", db => db.PlaybookProposalChanges.CountAsync(), 4, 3, 1),

        // Three canonicals: A's, B's and one both bind.
        new("TrunkBuilds", db => db.TrunkBuilds.CountAsync(), 3, 2, 0),
        new("PlaybookFiles", db => db.PlaybookFiles.CountAsync(), 3, 2, 0),
        new("DependencyFiles", db => db.DependencyFiles.CountAsync(), 3, 2, 0),
        new("ChangelogFiles", db => db.ChangelogFiles.CountAsync(), 3, 2, 0),
    ];

    /// <summary>A context that belongs to a request whose scope resolved to <paramref name="visible"/>.</summary>
    public static HatchContext Request(DbContextOptions<HatchContext> options, VisibleProjects? visible)
    {
        var request = new RequestScope(new FixedAccessor(new DefaultHttpContext())) { Current = visible };
        return new HatchContext(options, request);
    }

    public static VisibleProjects Only(params int[] ids) => new(false, ids.ToHashSet());

    public static async Task<ScopeSeed> SeedAsync(HatchContext db)
    {
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 90, IsTerminal = true };
        db.AddRange(todo, done);

        var a = new EfHatchProject { Key = "AAA", Name = "A", CreatedAt = At };
        var b = new EfHatchProject { Key = "BBB", Name = "B", CreatedAt = At };
        db.AddRange(a, b);
        await db.SaveChangesAsync();

        var playbookA = Playbook(todo, done, a.Id);
        var playbookB = Playbook(todo, done, b.Id);
        var playbookGlobal = Playbook(todo, done, null);
        db.AddRange(playbookA, playbookB, playbookGlobal);

        var dreamA = new EfHatchDream { Body = "dream a", Author = "test", CreatedAt = At, UpdatedAt = At, ProjectId = a.Id };
        var dreamB = new EfHatchDream { Body = "dream b", Author = "test", CreatedAt = At, UpdatedAt = At, ProjectId = b.Id };
        var dreamGlobal = new EfHatchDream { Body = "dream", Author = "test", CreatedAt = At, UpdatedAt = At };
        db.AddRange(dreamA, dreamB, dreamGlobal);

        var attachmentA = Attachment(a.Id);
        var attachmentB = Attachment(b.Id);
        var attachmentGlobal = Attachment(null);
        db.AddRange(attachmentA, attachmentB, attachmentGlobal);
        await db.SaveChangesAsync();

        var issues = new Dictionary<int, (EfHatchIssue One, EfHatchIssue Two)>();
        foreach (var project in new[] { a, b })
        {
            var one = Issue(project.Id, 1, todo);
            var two = Issue(project.Id, 2, todo);
            db.AddRange(one, two);
            issues[project.Id] = (one, two);
        }

        await db.SaveChangesAsync();

        foreach (var (project, playbook, dream, canonical) in new[]
        {
            (a, playbookA, dreamA, CanonicalA),
            (b, playbookB, dreamB, CanonicalB),
        })
        {
            var one = issues[project.Id].One;
            var two = issues[project.Id].Two;

            db.ProjectRepositories.AddRange(
                new EfHatchProjectRepository { ProjectId = project.Id, Remote = canonical, Canonical = canonical, CreatedAt = At },
                new EfHatchProjectRepository { ProjectId = project.Id, Remote = CanonicalShared, Canonical = CanonicalShared, CreatedAt = At });
            db.ProjectMembers.Add(new EfHatchProjectMember { ProjectId = project.Id, PersonId = Guid.NewGuid(), Role = "member", CreatedAt = At });
            db.ProjectLogos.Add(new EfHatchProjectLogo { ProjectId = project.Id, Bytes = [1], ContentType = "image/png", UpdatedAt = At });

            db.Comments.Add(new EfHatchComment { IssueId = one.Id, Author = "test", Body = "hello", CreatedAt = At });
            db.IssueEvents.Add(new EfHatchIssueEvent { IssueId = one.Id, Actor = "test", Kind = EfHatchIssueEvent.Created, At = At });
            db.WorkLog.Add(new EfHatchWorkLogEntry
            {
                IssueId = one.Id, SessionId = $"s-{project.Key}", StartedAt = At, EndedAt = At, DurationMs = 0, CreatedAt = At,
            });
            db.MergeChecks.Add(new EfHatchMergeCheck
            {
                IssueId = one.Id, Remote = canonical, Canonical = canonical, Trunk = "main", TrunkSha = "abc",
                Verdict = "clean", CheckedAt = At, Runner = "r", CheckedBy = "test",
            });
            db.BuildChecks.Add(new EfHatchBuildCheck
            {
                IssueId = one.Id, Remote = canonical, Canonical = canonical, Branch = "x", Sha = "abc", ShaSince = At,
                Verdict = "pass", CheckedAt = At, Runner = "r", CheckedBy = "test", Failing = "[]",
            });
            db.Landings.Add(new EfHatchLanding
            {
                IssueId = one.Id, Remote = canonical, Canonical = canonical, Trunk = "main", Sha = "abc", RecordedAt = At, RecordedBy = "test",
            });
            db.Verdicts.Add(new EfHatchVerdict
            {
                IssueId = one.Id, PlaybookId = playbook.Id, StatusId = todo.Id, Verdict = "pass", Author = "test",
                ClaimToken = Guid.NewGuid(), CreatedAt = At,
            });
            db.Contributions.Add(new EfHatchContribution
            {
                IssueId = one.Id, AuthorLogin = "someone", AuthorAssociation = "NONE", FiledAt = At, FiledBy = "test",
            });
            db.RunnerOmissions.Add(new EfHatchIssueRunnerOmission { IssueId = one.Id, RunnerName = "r", By = "test", At = At });
            db.Dependencies.Add(new EfHatchIssueDependency { IssueId = one.Id, DependsOnId = two.Id, CreatedBy = "test", CreatedAt = At });
            db.DreamThreads.Add(new EfHatchDreamThread { DreamId = dream.Id, IssueId = one.Id, Actor = "test", At = At });
            db.DreamRemands.Add(new EfHatchDreamRemand { DreamId = dream.Id, IssueId = one.Id, Actor = "test", At = At });
            db.ChangelogTasks.Add(new EfHatchChangelogTask { IssueId = one.Id, Canonical = canonical, CreatedAt = At });
        }

        foreach (var canonical in new[] { CanonicalA, CanonicalB, CanonicalShared })
        {
            db.TrunkBuilds.Add(new EfHatchTrunkBuild
            {
                Remote = canonical, Canonical = canonical, Trunk = "main", Sha = "abc", ShaSince = At,
                Verdict = "pass", CheckedAt = At, Runner = "r", CheckedBy = "test", Failing = "[]",
            });
            db.PlaybookFiles.Add(new EfHatchPlaybookFile
            {
                Remote = canonical, Canonical = canonical, Trunk = "main", Sha = "abc", State = "current",
                CheckedAt = At, Runner = "r", CheckedBy = "test", Rows = "[]",
            });
            db.DependencyFiles.Add(new EfHatchDependencyFile
            {
                Remote = canonical, Canonical = canonical, Trunk = "main", Sha = "abc", State = "current",
                CheckedAt = At, Runner = "r", CheckedBy = "test", Entries = "[]",
            });
            db.ChangelogFiles.Add(new EfHatchChangelogFile
            {
                Remote = canonical, Canonical = canonical, Trunk = "main", Sha = "abc", State = "current",
                CheckedAt = At, Runner = "r", CheckedBy = "test",
            });
        }

        await db.SaveChangesAsync();

        // The proposals, each with the changes it carries.
        var onIssueA = Proposal(issues[a.Id].One.Id, playbookA);
        var onIssueB = Proposal(issues[b.Id].One.Id, playbookB);
        var onPlaybookA = Proposal(null, playbookA);
        var onPlaybookGlobal = Proposal(null, playbookGlobal);
        var nobodys = Proposal(null, null);
        db.AddRange(onIssueA, onIssueB, onPlaybookA, onPlaybookGlobal, nobodys);
        await db.SaveChangesAsync();

        return new ScopeSeed(a.Id, b.Id, issues[a.Id].One.Id, issues[b.Id].One.Id, dreamA.Id, dreamB.Id);
    }

    private static EfHatchPlaybook Playbook(EfHatchStatus from, EfHatchStatus to, int? projectId) => new()
    {
        FromStatusId = from.Id, ToStatusId = to.Id, Types = "story", Shape = EfHatchPlaybook.AnyShape,
        Model = "m", Effort = "low", CreatedAt = At, UpdatedAt = At, ProjectId = projectId,
    };

    private static EfHatchAttachment Attachment(int? projectId) => new()
    {
        Id = Guid.NewGuid(), Bytes = [1], ContentType = "application/octet-stream", FileName = "f", Size = 1, CreatedAt = At, ProjectId = projectId,
    };

    private static EfHatchIssue Issue(int projectId, int number, EfHatchStatus status) => new()
    {
        ProjectId = projectId, Number = number, Type = "story", Title = $"issue {projectId}-{number}",
        StatusId = status.Id, Rank = number * 1000, CreatedBy = "test", CreatedAt = At, UpdatedAt = At,
    };

    private static EfHatchPlaybookProposal Proposal(long? issueId, EfHatchPlaybook? playbook)
    {
        var proposal = new EfHatchPlaybookProposal
        {
            Reason = "why", Author = "test", State = EfHatchPlaybookProposal.Pending, CreatedAt = At, IssueId = issueId,
        };

        if (playbook is not null)
            proposal.Changes.Add(new EfHatchPlaybookProposalChange
            {
                ProposalId = 0, PlaybookId = playbook.Id, Before = "{}", Patch = "{}", BasedOnUpdatedAt = At,
            });

        return proposal;
    }

    /// <summary>
    /// The real accessor holds its context in an AsyncLocal, which a value set
    /// inside an async helper loses on the way back to the test.
    /// </summary>
    internal sealed class FixedAccessor(HttpContext? context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }
}

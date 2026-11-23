using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Hatch's slice of the Hatch database: the <c>hatch</c> schema, its own
/// migration history, fifty tables.
/// </summary>
public class HatchContext : DbContext, IModuleContext
{
    public const string Schema = "hatch";

    private readonly RequestScope? request;

    /// <summary>
    /// A context with no scope at all, unbounded explicitly: what the tests,
    /// the design-time factory and anything else that builds one by hand get.
    /// A second constructor rather than an optional parameter because
    /// <see cref="ModuleDesignTimeFactory{TContext}"/> builds it by reflection
    /// with the options alone.
    /// </summary>
    public HatchContext(DbContextOptions<HatchContext> options) : base(options)
    {
    }

    /// <summary>
    /// A context that belongs to a request, or to a job with no request: it
    /// reads <paramref name="request"/> for the caller's scope. With a request
    /// behind it and no scope resolved, its first query on a filtered table
    /// throws - see <see cref="RequestScope.Effective"/>.
    /// </summary>
    public HatchContext(DbContextOptions<HatchContext> options, RequestScope? request) : base(options)
    {
        this.request = request;
    }

    // What the query filters below read. Instance members, because EF turns a
    // member of the context into a parameter it re-reads at each execution,
    // which is how one cached model serves every caller. They are the only way
    // a filter reaches the scope, and neither does I/O: the answer was put on
    // the request by ScopeResolutionFilter before the action ran.
    private VisibleProjects Scope => request?.Effective() ?? VisibleProjects.Everything;
    private bool Unbounded => Scope.Unbounded;
    private int[] Visible => Scope.Ids;

    /// <summary>
    /// Whether this context reads every project - the same answer the query
    /// filters open with. For the one reader that needs to know whether a
    /// second, scoped read would add anything: <see cref="PriorityTree.ForReaderAsync"/>.
    /// </summary>
    public bool ScopeUnbounded => Unbounded;

    public DbSet<EfHatchProject> Projects => Set<EfHatchProject>();
    public DbSet<EfHatchProjectRepository> ProjectRepositories => Set<EfHatchProjectRepository>();
    public DbSet<EfHatchProjectMember> ProjectMembers => Set<EfHatchProjectMember>();
    public DbSet<EfHatchPersonScope> PersonScopes => Set<EfHatchPersonScope>();
    public DbSet<EfHatchPersonScopeProject> PersonScopeProjects => Set<EfHatchPersonScopeProject>();
    public DbSet<EfHatchPersonHome> PersonHomes => Set<EfHatchPersonHome>();
    public DbSet<EfHatchProjectLogo> ProjectLogos => Set<EfHatchProjectLogo>();
    public DbSet<EfHatchAttachment> Attachments => Set<EfHatchAttachment>();
    public DbSet<EfHatchStatus> Statuses => Set<EfHatchStatus>();
    public DbSet<EfHatchIssue> Issues => Set<EfHatchIssue>();
    public DbSet<EfHatchComment> Comments => Set<EfHatchComment>();
    public DbSet<EfHatchDream> Dreams => Set<EfHatchDream>();
    public DbSet<EfHatchIssueEvent> IssueEvents => Set<EfHatchIssueEvent>();
    public DbSet<EfHatchPlaybook> Playbooks => Set<EfHatchPlaybook>();
    public DbSet<EfHatchLocalModel> LocalModels => Set<EfHatchLocalModel>();
    public DbSet<EfHatchPlaybookProposal> PlaybookProposals => Set<EfHatchPlaybookProposal>();
    public DbSet<EfHatchPlaybookProposalChange> PlaybookProposalChanges => Set<EfHatchPlaybookProposalChange>();
    public DbSet<EfHatchWipLimit> WipLimits => Set<EfHatchWipLimit>();
    public DbSet<EfHatchIssueDependency> Dependencies => Set<EfHatchIssueDependency>();
    public DbSet<EfHatchIssueRunnerOmission> RunnerOmissions => Set<EfHatchIssueRunnerOmission>();
    public DbSet<EfHatchDreamThread> DreamThreads => Set<EfHatchDreamThread>();
    public DbSet<EfHatchDreamRemand> DreamRemands => Set<EfHatchDreamRemand>();
    public DbSet<EfHatchCapability> Capabilities => Set<EfHatchCapability>();
    public DbSet<EfHatchCapabilityLink> CapabilityLinks => Set<EfHatchCapabilityLink>();
    public DbSet<EfHatchCapabilityVerdict> CapabilityVerdicts => Set<EfHatchCapabilityVerdict>();
    public DbSet<EfHatchChangeOutcome> ChangeOutcomes => Set<EfHatchChangeOutcome>();
    public DbSet<EfHatchWorkLogEntry> WorkLog => Set<EfHatchWorkLogEntry>();
    public DbSet<EfHatchMergeCheck> MergeChecks => Set<EfHatchMergeCheck>();
    public DbSet<EfHatchBuildCheck> BuildChecks => Set<EfHatchBuildCheck>();
    public DbSet<EfHatchLanding> Landings => Set<EfHatchLanding>();
    public DbSet<EfHatchContribution> Contributions => Set<EfHatchContribution>();
    public DbSet<EfHatchTrunkBuild> TrunkBuilds => Set<EfHatchTrunkBuild>();
    public DbSet<EfHatchChangelogTask> ChangelogTasks => Set<EfHatchChangelogTask>();
    public DbSet<EfHatchPlaybookFile> PlaybookFiles => Set<EfHatchPlaybookFile>();
    public DbSet<EfHatchDependencyFile> DependencyFiles => Set<EfHatchDependencyFile>();
    public DbSet<EfHatchChangelogFile> ChangelogFiles => Set<EfHatchChangelogFile>();
    public DbSet<EfHatchVerdict> Verdicts => Set<EfHatchVerdict>();

    /// <summary>The goose's record of its sweeps. Belongs to no project, so it has no query filter.</summary>
    public DbSet<EfHatchGooseRun> GooseRuns => Set<EfHatchGooseRun>();

    /// <summary>When a <see cref="FindingCheck"/>'s condition began, before it stands long enough to open a <see cref="GooseFindings"/> row.</summary>
    public DbSet<EfHatchGooseSighting> GooseSightings => Set<EfHatchGooseSighting>();

    /// <summary>The goose's stored findings - see <see cref="EfHatchGooseFinding"/>.</summary>
    public DbSet<EfHatchGooseFinding> GooseFindings => Set<EfHatchGooseFinding>();

    /// <summary>
    /// The processes that have spoken to this Hatch lately, and what the board
    /// would like each to do next. Keyed on the name a runner already calls
    /// itself by, and related to nothing: a runner is an identity a checkout
    /// computes, not a row that belongs to an issue.
    /// </summary>
    public DbSet<EfHatchRunner> LegacyRunners => Set<EfHatchRunner>();

    /// <summary>
    /// What the board has chosen for each runner key, as against
    /// <see cref="LegacyRunners"/>, which is what the processes report. Keyed by the
    /// API key's id, with no foreign key into the core schema.
    /// </summary>
    public DbSet<EfHatchRunnerConfig> RunnerConfigs => Set<EfHatchRunnerConfig>();
    public DbSet<EfHatchRunnerSession> RunnerSessions => Set<EfHatchRunnerSession>();

    /// <summary>Every change of a runner's state, with its instant - see <see cref="EfHatchRunnerState"/>.</summary>
    public DbSet<EfHatchRunnerState> RunnerStates => Set<EfHatchRunnerState>();

    /// <summary>What a runner could not do, one row per failure - see <see cref="EfHatchRunnerFailure"/>.</summary>
    public DbSet<EfHatchRunnerFailure> RunnerFailures => Set<EfHatchRunnerFailure>();

    /// <summary>The tickets let go for a runner failure - see <see cref="EfHatchRunnerFailureIssue"/>.</summary>
    public DbSet<EfHatchRunnerFailureIssue> RunnerFailureIssues => Set<EfHatchRunnerFailureIssue>();

    /// <summary>The chat workspaces Hatch is connected to - see <see cref="EfHatchChatWorkspace"/>.</summary>
    public DbSet<EfHatchChatWorkspace> ChatWorkspaces => Set<EfHatchChatWorkspace>();

    /// <summary>The projects a chat workspace is bound to - see <see cref="EfHatchChatWorkspaceProject"/>.</summary>
    public DbSet<EfHatchChatWorkspaceProject> ChatWorkspaceProjects => Set<EfHatchChatWorkspaceProject>();

    /// <summary>The channels set up as rooms - see <see cref="EfHatchChatRoom"/>.</summary>
    public DbSet<EfHatchChatRoom> ChatRooms => Set<EfHatchChatRoom>();

    /// <summary>The projects a room is scoped to - see <see cref="EfHatchChatRoomProject"/>.</summary>
    public DbSet<EfHatchChatRoomProject> ChatRoomProjects => Set<EfHatchChatRoomProject>();

    /// <summary>
    /// EF warns, at every start, that <c>PersonScopeProjects</c> is the required
    /// end of a relationship to a filtered <c>Projects</c>. It is the one table
    /// left unfiltered on purpose: the people-and-scopes routes read and rewrite
    /// it, and a filter would let an admin whose own scope is narrowed drop other
    /// people's entries for projects they cannot see. The warning is silenced
    /// because the guard is <c>QueryFilterModelAuditTests</c>, which names that
    /// table with the reason and fails on any other that reaches a project bare.
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.ConfigureWarnings(w =>
            w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        var issue = modelBuilder.Entity<EfHatchIssue>();

        // Dollars and cents, the scale MaxSpend has.
        issue.Property(i => i.BudgetUsd).HasPrecision(18, 2);

        // Restrict, not cascade: a project or a status is deleted through an
        // endpoint that first refuses while anything still points at it (409
        // with a reason). Cascade here would turn that considered refusal into
        // a delete that silently takes the board's contents with it - and the
        // guard is the kind of thing a future code path can forget, while the
        // constraint is not.
        issue.HasOne(i => i.Project)
            .WithMany(p => p.Issues)
            .HasForeignKey(i => i.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        issue.HasOne(i => i.Status)
            .WithMany()
            .HasForeignKey(i => i.StatusId)
            .OnDelete(DeleteBehavior.Restrict);

        // Deleting an epic orphans its stories rather than deleting them.
        // Losing a parent is an outdent; losing an epic should not quietly take
        // eleven stories off the board.
        // An issue belongs to a person, or to a key, or to nobody - never to
        // both. The controller clears one column when it writes the other and
        // refuses a request naming both with a sentence, so this is the backstop
        // and never the error a caller sees. Tests run on the in-memory
        // provider, which ignores check constraints; the controller guard is
        // therefore the one under test, which is the honest split.
        issue.ToTable(t => t.HasCheckConstraint(
            "CK_Issues_OneAssignee",
            "num_nonnulls(\"AssigneePersonId\", \"AssigneeApiKeyId\") <= 1"));

        issue.HasOne(i => i.Parent)
            .WithMany(i => i.Children)
            .HasForeignKey(i => i.ParentId)
            .OnDelete(DeleteBehavior.SetNull);

        issue.HasOne(i => i.FiledFrom)
            .WithMany(i => i.Filed)
            .HasForeignKey(i => i.FiledFromId)
            .OnDelete(DeleteBehavior.SetNull);
        issue.HasIndex(i => i.FiledFromId);

        // Mirrors EfHatchProject.RunnerTags' own default below, for the same
        // reason: EnsureCreated must leave the same empty-array default a
        // migrated row carries.
        issue.Property(i => i.RunnerTags).HasColumnType("jsonb").HasDefaultValue("[]");

        modelBuilder.Entity<EfHatchStatus>(e =>
        {
            // Mirrors EfHatchProject.RunnerTags' own default below, for the same
            // reason: EnsureCreated must leave the same empty-array default a
            // migrated row carries.
            e.Property(s => s.RunnerTags).HasColumnType("jsonb").HasDefaultValue("[]");
        });

        // Comments and events, on the other hand, have no meaning without their
        // issue - and this is the accepted MVP gap written down in the plan: a
        // deleted issue takes its audit trail with it.
        modelBuilder.Entity<EfHatchComment>(e =>
        {
            e.HasOne(c => c.Issue)
                .WithMany(i => i.Comments)
                .HasForeignKey(c => c.IssueId)
                .OnDelete(DeleteBehavior.Cascade);

            // An answer points at the question it settles, on the same issue.
            //
            // NoAction rather than the Restrict used everywhere else, and the
            // difference is load-bearing here: deleting an issue cascades to
            // every comment on it, question and answer together, and PostgreSQL
            // checks a RESTRICT immediately - the answer row would be seen
            // pointing at a question mid-delete and the whole delete would fail.
            // NO ACTION is checked once the statement is finished, by which
            // point both rows have gone and there is nothing to complain about.
            e.HasOne(c => c.Answers)
                .WithMany(c => c.AnsweredBy)
                .HasForeignKey(c => c.AnswersId)
                .OnDelete(DeleteBehavior.NoAction);

            // jsonb for the reason the event payload is - see
            // EfHatchComment.Options.
            e.Property(c => c.Options).HasColumnType("jsonb");
        });

        modelBuilder.Entity<EfHatchIssueEvent>(e =>
        {
            e.HasOne(x => x.Issue)
                .WithMany(i => i.Events)
                .HasForeignKey(x => x.IssueId)
                .OnDelete(DeleteBehavior.Cascade);

            // jsonb rather than text: the payload shape differs per kind, and
            // the day someone asks "which issues moved out of review twice"
            // that is a query against this column rather than a migration.
            e.Property(x => x.Payload).HasColumnType("jsonb");
        });

        // Restrict for the same reason issues restrict: a column is deleted
        // through an endpoint that refuses while anything still points at it,
        // and cascade here would let deleting "review" quietly take the
        // instructions for reaching it as well.
        modelBuilder.Entity<EfHatchPlaybook>(e =>
        {
            e.HasOne(p => p.FromStatus)
                .WithMany()
                .HasForeignKey(p => p.FromStatusId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(p => p.ToStatus)
                .WithMany()
                .HasForeignKey(p => p.ToStatusId)
                .OnDelete(DeleteBehavior.Restrict);

            // Same argument as FromStatus/ToStatus above: a project is deleted
            // through an endpoint that refuses while a playbook still names it,
            // and cascade here would let that deletion quietly take the row
            // with it instead.
            e.HasOne(p => p.Project)
                .WithMany()
                .HasForeignKey(p => p.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            // Matches the PlaybookShape migration's own defaultValue, so a
            // database built straight from this model - EnsureCreated, as the
            // test suite's own database does - gets the same column default a
            // sequential migration run leaves behind, and an INSERT that never
            // names Shape still gets one.
            e.Property(p => p.Shape).HasDefaultValue(EfHatchPlaybook.AnyShape);

            // Matches AddPlaybookIsCheck's own defaultValue, for the same
            // reason as Shape above: EnsureCreated must leave the same column
            // default a sequential migration run would.
            e.Property(p => p.IsCheck).HasDefaultValue(false);

            // Matches AddPlaybookAnswersQuestions' own defaultValue, for the
            // same reason as IsCheck above.
            e.Property(p => p.AnswersQuestions).HasDefaultValue(false);

            // Matches AddPlaybookWorksOutChanges' own defaultValue, for the
            // same reason as AnswersQuestions above.
            e.Property(p => p.WorksOutChanges).HasDefaultValue(false);

            // Matches AddPlaybookShapesAmbition's own defaultValue, for the
            // same reason as WorksOutChanges above.
            e.Property(p => p.ShapesAmbition).HasDefaultValue(false);

            // jsonb rather than text, the same reason EfHatchComment.Options is:
            // the stored shape is a list, not a scalar, and the day somebody
            // asks "which rows deny Bash" that is a query against this column
            // rather than a migration.
            e.Property(p => p.DeniedTools).HasColumnType("jsonb");
        });

        // SetNull on the ticket: a proposal is a record of what was asked for,
        // and it outlives the issue that prompted it.
        modelBuilder.Entity<EfHatchPlaybookProposal>(e =>
        {
            e.HasOne(p => p.Issue)
                .WithMany()
                .HasForeignKey(p => p.IssueId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // A change has no meaning without its proposal, so deleting the
        // proposal takes them. The playbook is the other way round: deleting
        // one leaves the change standing with a null PlaybookId, because Before
        // still says what it was and the proposal should still read afterwards.
        modelBuilder.Entity<EfHatchPlaybookProposalChange>(e =>
        {
            e.HasOne(c => c.Proposal)
                .WithMany(p => p.Changes)
                .HasForeignKey(c => c.ProposalId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(c => c.Playbook)
                .WithMany()
                .HasForeignKey(c => c.PlaybookId)
                .OnDelete(DeleteBehavior.SetNull);

            // jsonb for the reason EfHatchPlaybook.DeniedTools is: the stored
            // shape is a document, and "which proposals touch the prompt" is a
            // query against these columns rather than a migration.
            e.Property(c => c.Before).HasColumnType("jsonb");
            e.Property(c => c.Patch).HasColumnType("jsonb");
        });

        // Cascade from both ends, which is the one place in this module where
        // that is obviously right: an edge has no meaning without either issue,
        // so deleting either takes the edges pointing at it in both directions.
        // No inverse navigation on the issue for either end - two collections
        // told apart only by name, and nothing reads them.
        modelBuilder.Entity<EfHatchIssueDependency>(e =>
        {
            e.HasOne(d => d.Issue)
                .WithMany()
                .HasForeignKey(d => d.IssueId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(d => d.DependsOn)
                .WithMany()
                .HasForeignKey(d => d.DependsOnId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // An edge has no meaning without either row, so deleting either takes
        // it, in both directions. No inverse navigation on the dream or the
        // issue for either end - two collections told apart only by name, and
        // nothing reads them.
        modelBuilder.Entity<EfHatchDreamThread>(e =>
        {
            e.HasKey(t => new { t.DreamId, t.IssueId });

            e.HasOne(t => t.Dream)
                .WithMany()
                .HasForeignKey(t => t.DreamId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(t => t.Issue)
                .WithMany()
                .HasForeignKey(t => t.IssueId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Cascade both ways, as for a thread: a remand means nothing without
        // the dream or the issue. Deleting the dream removes the rows and moves
        // no issue; deleting an issue removes its row and leaves the dream.
        modelBuilder.Entity<EfHatchDreamRemand>(e =>
        {
            e.HasKey(r => new { r.DreamId, r.IssueId });

            e.HasOne(r => r.Dream)
                .WithMany()
                .HasForeignKey(r => r.DreamId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(r => r.Issue)
                .WithMany()
                .HasForeignKey(r => r.IssueId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Cascade: an omission means nothing once the issue it names is gone.
        modelBuilder.Entity<EfHatchIssueRunnerOmission>(e =>
        {
            e.HasKey(o => new { o.IssueId, o.RunnerName });

            e.HasOne(o => o.Issue)
                .WithMany(i => i.RunnerOmissions)
                .HasForeignKey(o => o.IssueId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Two places, because a spend cap is dollars and cents and the default
        // scale would refuse the number an operator types into the box.
        modelBuilder.Entity<EfHatchRunner>().Property(r => r.MaxSpend).HasPrecision(18, 2);
        // The same scale CostUsd has: what a runner reports having spent is
        // priced per token, and the CLI rounds it for display.
        modelBuilder.Entity<EfHatchRunner>().Property(r => r.Spent).HasPrecision(18, 8);

        modelBuilder.Entity<EfHatchRunnerConfig>(e =>
        {
            // Mirrors EfHatchProject.RunnerTags' own default below, for the same
            // reason: EnsureCreated must leave the same empty-array default a
            // migrated row carries.
            e.Property(c => c.Tags).HasColumnType("jsonb").HasDefaultValue("[]");
            e.Property(c => c.RefusedModels).HasColumnType("jsonb").HasDefaultValue("[]");
            e.Property(c => c.StatusMode).HasDefaultValue("all");
            e.Property(c => c.StatusIds).HasColumnType("jsonb").HasDefaultValue("[]");
            e.Property(c => c.Preferred).HasDefaultValue(false);
            // The same scale EfHatchRunner.MaxSpend has.
            e.Property(c => c.MaxSpend).HasPrecision(18, 2);
        });

        modelBuilder.Entity<EfHatchRunnerSession>().Property(s => s.MaxSpend).HasPrecision(18, 2);
        modelBuilder.Entity<EfHatchRunnerSession>().Property(s => s.Spent).HasPrecision(18, 8);
        modelBuilder.Entity<EfHatchRunnerSession>().Property(s => s.Usage).HasColumnType("jsonb");

        // jsonb for the reason EfHatchWorkLogEntry.ModelUsage below is.
        modelBuilder.Entity<EfHatchRunner>().Property(r => r.Usage).HasColumnType("jsonb");

        // Cascade with the issue, the way comments and events do and for the
        // same reason: a meter reading for a ticket nobody kept is a row about
        // nothing. What the account spent in total is a separate record and
        // does not depend on this one surviving.
        modelBuilder.Entity<EfHatchWorkLogEntry>(e =>
        {
            e.HasOne(w => w.Issue)
                .WithMany(i => i.WorkLog)
                .HasForeignKey(w => w.IssueId)
                .OnDelete(DeleteBehavior.Cascade);

            // Eight places, because a short session costs a fraction of a cent
            // and the default scale would round a night of them to nothing.
            e.Property(w => w.CostUsd).HasPrecision(18, 8);

            // jsonb for the reason the event payload is - see
            // EfHatchWorkLogEntry.ModelUsage.
            e.Property(w => w.ModelUsage).HasColumnType("jsonb");
            e.Property(w => w.Reads).HasColumnType("jsonb");

            // Read by the dispatcher for the rows that name a question; no foreign key,
            // so a deleted comment never breaks the log.
            e.HasIndex(w => w.QuestionId);

            // Read by AmbitionChanges for the rows that name a change; no
            // foreign key, for the same reason.
            e.HasIndex(w => w.ChangeEventId);
        });

        // Matches AddProjectLanding's own defaultValue, for the same reason as
        // IsCheck above: EnsureCreated must leave the same column default a
        // sequential migration run would. RunnerTags' default is mirrored for
        // the same reason: an empty array, not null, is what a fresh
        // EnsureCreated row and a migrated one must agree on.
        modelBuilder.Entity<EfHatchProject>(e =>
        {
            e.Property(p => p.Landing).HasDefaultValue(global::Hatch.Contracts.Landings.PullRequest);
            e.Property(p => p.ChecksRequired).HasDefaultValue(true);
            e.Property(p => p.OnePullRequest).HasDefaultValue(false);
            e.Property(p => p.OnePullRequestPolicy).HasDefaultValue(global::Hatch.Contracts.OnePullRequestPolicies.Optional);
            e.Property(p => p.Priority).HasDefaultValue(global::Hatch.Contracts.ProjectPriorities.Normal);

            // At most one project binds the tracker's own repository. The
            // controller clears the old holder first; this is what makes a race
            // between two writers fail instead of leaving two.
            e.Property(p => p.BindsTracker).HasDefaultValue(false);
            e.HasIndex(p => p.BindsTracker).IsUnique().HasFilter("\"BindsTracker\" = true");
            e.Property(p => p.RunnerTags).HasColumnType("jsonb").HasDefaultValue("[]");
            e.Property(p => p.TrainStalledDrivers).HasColumnType("jsonb").HasDefaultValue("[]");
            e.Property(p => p.StoriesOnEpics).HasDefaultValue(false);
            e.Property(p => p.CheckApprovesMerge).HasDefaultValue(false);
            e.Property(p => p.NoCodeCloses).HasDefaultValue(false);
            e.Property(p => p.RunnersWorkContributions).HasDefaultValue(false);
            e.Property(p => p.FilesContributions).HasDefaultValue(false);
            e.Property(p => p.AllowsUnsignedCommits).HasDefaultValue(true);

            e.Property(p => p.AutoHealBugEnabled).HasDefaultValue(false);
            e.Property(p => p.AutoHealBugPriority).HasDefaultValue(global::Hatch.Contracts.PriorityLevels.Emergency);
            e.Property(p => p.AutoHealBugPreApproval).HasDefaultValue(global::Hatch.Contracts.PreApprovalLevels.Express);

            // Restrict for the same reason EfHatchPlaybook.FromStatus/ToStatus
            // are: a column is deleted through an endpoint that refuses while
            // anything still points at it, and cascade here would let
            // deleting a column quietly take this setting with it instead.
            // No navigation property either direction - the same one-sided
            // shape EfHatchProjectLogo's FK below uses.
            e.HasOne<EfHatchStatus>().WithMany().HasForeignKey(p => p.AutoHealBugStatusId).OnDelete(DeleteBehavior.Restrict);

            e.HasOne(p => p.ReflectNowIssue)
                .WithMany()
                .HasForeignKey(p => p.ReflectNowIssueId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Cascade with the project: a project can only be deleted empty of
        // issues, and its bindings are not a reason to keep it around, so they
        // go with it rather than blocking the delete the way issues do.
        modelBuilder.Entity<EfHatchProjectRepository>(e =>
        {
            e.HasOne(r => r.Project)
                .WithMany(p => p.Repositories)
                .HasForeignKey(r => r.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Cascade off the project for the same reason: a logo means nothing
        // without the project it is on. No inverse navigation - nothing walks
        // from a project to its logo through EF, the read path is a plain
        // query (the same unread-from-one-side style EfHatchIssueDependency's
        // two edges already use).
        modelBuilder.Entity<EfHatchProjectLogo>()
            .HasOne(l => l.Project)
            .WithOne()
            .HasForeignKey<EfHatchProjectLogo>(l => l.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // Cascade off the project, same reasoning as its repository bindings:
        // a project's memberships are not a reason to keep it around. No
        // inverse navigation - the read path is a plain query against
        // db.ProjectMembers, the same shape EfHatchProjectLogo above uses.
        modelBuilder.Entity<EfHatchProjectMember>()
            .HasOne(m => m.Project)
            .WithMany()
            .HasForeignKey(m => m.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // A deleted project leaves every chosen list it was on, the same
        // cascade ProjectMembers takes. No inverse navigation, for the same reason.
        modelBuilder.Entity<EfHatchPersonScopeProject>()
            .HasOne(m => m.Project)
            .WithMany()
            .HasForeignKey(m => m.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // A workspace's bound projects: cascades off the workspace and off the
        // project, the same reasoning as PersonScopeProjects. The pair is also
        // an alternate key, which ChatRoomProjects' composite foreign key below
        // points at.
        modelBuilder.Entity<EfHatchChatWorkspaceProject>(e =>
        {
            e.HasAlternateKey(p => new { p.WorkspaceId, p.ProjectId });

            e.HasOne(p => p.Workspace)
                .WithMany()
                .HasForeignKey(p => p.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(p => p.Project)
                .WithMany()
                .HasForeignKey(p => p.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EfHatchChatRoom>()
            .HasOne(r => r.Workspace)
            .WithMany()
            .HasForeignKey(r => r.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        // A room's chosen projects: RoomId cascades off the room, and the pair
        // (WorkspaceId, ProjectId) is a composite foreign key into
        // ChatWorkspaceProjects' own pair, cascading - unbinding a project from
        // the workspace takes it out of every room that had chosen it, so a
        // race cannot leave a chosen project that is not bound.
        modelBuilder.Entity<EfHatchChatRoomProject>(e =>
        {
            e.HasOne(p => p.Room)
                .WithMany()
                .HasForeignKey(p => p.RoomId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne<EfHatchChatWorkspaceProject>()
                .WithMany()
                .HasForeignKey(p => new { p.WorkspaceId, p.ProjectId })
                .HasPrincipalKey(w => new { w.WorkspaceId, w.ProjectId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        // A verdict is about an issue and means nothing without it, the same
        // as its events: deleting the issue takes them.
        modelBuilder.Entity<EfHatchMergeCheck>(e =>
        {
            e.HasOne(m => m.Issue)
                .WithMany(i => i.MergeChecks)
                .HasForeignKey(m => m.IssueId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // The same for the build's verdict. jsonb for the failing checks, for
        // the reason EfHatchComment.Options is.
        modelBuilder.Entity<EfHatchBuildCheck>(e =>
        {
            e.HasOne(b => b.Issue)
                .WithMany(i => i.BuildChecks)
                .HasForeignKey(b => b.IssueId)
                .OnDelete(DeleteBehavior.Cascade);

            e.Property(b => b.Failing).HasColumnType("jsonb");
        });

        modelBuilder.Entity<EfHatchLanding>(e =>
        {
            e.HasOne(l => l.Issue)
                .WithMany(i => i.Landings)
                .HasForeignKey(l => l.IssueId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EfHatchContribution>(e =>
        {
            e.HasOne(c => c.Issue)
                .WithOne(i => i.Contribution)
                .HasForeignKey<EfHatchContribution>(c => c.IssueId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Cascade with the issue, the same as MergeChecks/BuildChecks above.
        // Restrict on Playbook/Status, the same as EfHatchPlaybook's own
        // FromStatus/ToStatus: a referenced playbook or status refuses
        // deletion rather than take a kept verdict with it.
        modelBuilder.Entity<EfHatchVerdict>(e =>
        {
            e.HasOne(v => v.Issue)
                .WithMany(i => i.Verdicts)
                .HasForeignKey(v => v.IssueId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(v => v.Playbook)
                .WithMany()
                .HasForeignKey(v => v.PlaybookId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(v => v.Status)
                .WithMany()
                .HasForeignKey(v => v.StatusId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // A trunk build is nobody's issue, so it does not cascade with one -
        // SetNull instead, the way an outdent orphans a story rather than
        // deleting it: the bug that was filed for this outage outlives the row
        // that attached it.
        modelBuilder.Entity<EfHatchTrunkBuild>(e =>
        {
            e.HasOne(t => t.BugIssue)
                .WithMany()
                .HasForeignKey(t => t.BugIssueId)
                .OnDelete(DeleteBehavior.SetNull);

            e.Property(t => t.Failing).HasColumnType("jsonb");
        });

        // The record goes with the task, but outlives the issue that filed it:
        // SetNull, so a deleted source leaves the task out of the changelog file.
        modelBuilder.Entity<EfHatchChangelogTask>(e =>
        {
            e.HasOne(t => t.Issue)
                .WithMany()
                .HasForeignKey(t => t.IssueId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(t => t.SourceIssue)
                .WithMany()
                .HasForeignKey(t => t.SourceIssueId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // A dream outlives the project it leaned toward - SetNull, the same
        // shape as EfHatchTrunkBuild.BugIssueId above.
        modelBuilder.Entity<EfHatchDream>(e =>
        {
            e.HasOne(d => d.Project)
                .WithMany()
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // A sighting is deleted outright once its condition is gone (FindingCheck.RunAsync),
        // never orphaned by a deleted project - SetNull, the same shape as
        // EfHatchDream.ProjectId above.
        modelBuilder.Entity<EfHatchGooseSighting>(e =>
        {
            e.HasOne(s => s.Project)
                .WithMany()
                .HasForeignKey(s => s.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // A finding outlives the project its condition was about, the same
        // shape as EfHatchDream.ProjectId above, and outlives the bug filed
        // from it too - SetNull, the same shape as EfHatchTrunkBuild.BugIssueId.
        // At most one finding is open (ClosedAt null) for a given Check and
        // Subject: a filtered unique index, the same shape as EfHatchProject's
        // own BindsTracker above.
        modelBuilder.Entity<EfHatchGooseFinding>(e =>
        {
            e.HasOne(f => f.Project)
                .WithMany()
                .HasForeignKey(f => f.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasOne(f => f.BugIssue)
                .WithMany()
                .HasForeignKey(f => f.BugIssueId)
                .OnDelete(DeleteBehavior.SetNull);

            e.Property(f => f.Evidence).HasColumnType("jsonb");

            e.HasIndex(f => new { f.Check, f.Subject }).IsUnique().HasFilter("\"ClosedAt\" IS NULL");
        });

        // At most one open row for a key, checkout and kind - the database
        // holds it, so two racing beats cannot both open one.
        modelBuilder.Entity<EfHatchRunnerFailure>(e =>
            e.HasIndex(f => new { f.ApiKeyId, f.Where, f.Kind }).IsUnique().HasFilter("\"EndedAt\" IS NULL"));

        // A release links its ticket to the failure it named, once. Either end
        // going takes the link with it.
        modelBuilder.Entity<EfHatchRunnerFailureIssue>(e =>
        {
            e.HasIndex(l => new { l.FailureId, l.IssueId }).IsUnique();

            e.HasOne<EfHatchRunnerFailure>()
                .WithMany()
                .HasForeignKey(l => l.FailureId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(l => l.Issue)
                .WithMany()
                .HasForeignKey(l => l.IssueId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // An attachment that names a project is that project's - cascading,
        // not the SetNull a dream's own FK above is: there is no meaning to
        // an attachment surviving the deletion of the project it was
        // uploaded for, the same lean EfHatchProjectRepository's own FK takes.
        modelBuilder.Entity<EfHatchAttachment>(e =>
        {
            e.HasOne(a => a.Project)
                .WithMany()
                .HasForeignKey(a => a.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EfHatchCapability>(e =>
        {
            e.HasOne(c => c.Issue)
                .WithMany()
                .HasForeignKey(c => c.IssueId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(c => new { c.IssueId, c.Position });
        });

        // Cascade both ways, as for a thread: a link means nothing without the
        // epic or the capability. Deleting either removes the row alone.
        modelBuilder.Entity<EfHatchCapabilityLink>(e =>
        {
            e.HasKey(l => new { l.CapabilityId, l.IssueId });

            e.HasOne(l => l.Capability)
                .WithMany()
                .HasForeignKey(l => l.CapabilityId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(l => l.Issue)
                .WithMany()
                .HasForeignKey(l => l.IssueId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EfHatchCapabilityVerdict>(e =>
        {
            e.HasOne(v => v.Capability)
                .WithMany()
                .HasForeignKey(v => v.CapabilityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Cascade both ways, as EfHatchCapabilityLink's FKs are: an outcome
        // means nothing without the ambition it changed or the epic it is
        // about. Unlike that link, this row keeps its own Id - two outcomes
        // can share an ambition and an epic across different changes - so the
        // pair is a unique index, not the primary key.
        modelBuilder.Entity<EfHatchChangeOutcome>(e =>
        {
            e.HasOne(o => o.Issue)
                .WithMany()
                .HasForeignKey(o => o.IssueId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(o => o.Epic)
                .WithMany()
                .HasForeignKey(o => o.EpicId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(o => new { o.IssueId, o.ChangeEventId, o.EpicId }).IsUnique();
        });

        modelBuilder.Entity<EfHatchPlaybookFile>(e =>
        {
            e.Property(f => f.Rows).HasColumnType("jsonb");
        });

        modelBuilder.Entity<EfHatchDependencyFile>(e =>
        {
            e.Property(f => f.Entries).HasColumnType("jsonb");
        });

        // ---- Scope: no row that belongs to a project leaves this context for a
        // caller whose scope does not hold that project. One place, so the model
        // audit can fail on an entity that reaches a project and says nothing.
        //
        // Every predicate opens with Unbounded, so a caller with everything - and
        // a job, a test, the design-time factory - reads exactly what it did
        // before. A hidden project reads as one that does not exist.
        //
        // Through-issue predicates are written on the issue's own ProjectId
        // rather than leaning on the Issue filter being applied to the
        // navigation: the rule should read the same whichever way EF composes
        // it.
        //
        // Deliberately unfiltered: Statuses, WipLimits, PersonHomes,
        // the runner tables (RunnerFailures among them), ChatWorkspaces and
        // ChatRooms (neither names a project), PersonScopes and
        // PersonScopeProjects (the resolver reads them), and the core schema.
        modelBuilder.Entity<EfHatchProject>().HasQueryFilter(e => Unbounded || Visible.Contains(e.Id));
        modelBuilder.Entity<EfHatchIssue>().HasQueryFilter(e => Unbounded || Visible.Contains(e.ProjectId));
        modelBuilder.Entity<EfHatchProjectRepository>().HasQueryFilter(e => Unbounded || Visible.Contains(e.ProjectId));
        modelBuilder.Entity<EfHatchProjectMember>().HasQueryFilter(e => Unbounded || Visible.Contains(e.ProjectId));
        modelBuilder.Entity<EfHatchProjectLogo>().HasQueryFilter(e => Unbounded || Visible.Contains(e.ProjectId));
        modelBuilder.Entity<EfHatchChatWorkspaceProject>().HasQueryFilter(e => Unbounded || Visible.Contains(e.ProjectId));
        modelBuilder.Entity<EfHatchChatRoomProject>().HasQueryFilter(e => Unbounded || Visible.Contains(e.ProjectId));

        // A global playbook, a dream leaning toward no project, and an
        // unlinked attachment are visible to everyone.
        modelBuilder.Entity<EfHatchPlaybook>().HasQueryFilter(e =>
            Unbounded || e.ProjectId == null || Visible.Contains(e.ProjectId.Value));
        modelBuilder.Entity<EfHatchDream>().HasQueryFilter(e =>
            Unbounded || e.ProjectId == null || Visible.Contains(e.ProjectId.Value));
        modelBuilder.Entity<EfHatchAttachment>().HasQueryFilter(e =>
            Unbounded || e.ProjectId == null || Visible.Contains(e.ProjectId.Value));

        modelBuilder.Entity<EfHatchComment>().HasQueryFilter(e => Unbounded || Visible.Contains(e.Issue!.ProjectId));
        modelBuilder.Entity<EfHatchIssueEvent>().HasQueryFilter(e => Unbounded || Visible.Contains(e.Issue!.ProjectId));
        modelBuilder.Entity<EfHatchWorkLogEntry>().HasQueryFilter(e => Unbounded || Visible.Contains(e.Issue!.ProjectId));
        modelBuilder.Entity<EfHatchMergeCheck>().HasQueryFilter(e => Unbounded || Visible.Contains(e.Issue!.ProjectId));
        modelBuilder.Entity<EfHatchBuildCheck>().HasQueryFilter(e => Unbounded || Visible.Contains(e.Issue!.ProjectId));
        modelBuilder.Entity<EfHatchLanding>().HasQueryFilter(e => Unbounded || Visible.Contains(e.Issue!.ProjectId));
        modelBuilder.Entity<EfHatchVerdict>().HasQueryFilter(e => Unbounded || Visible.Contains(e.Issue!.ProjectId));
        modelBuilder.Entity<EfHatchContribution>().HasQueryFilter(e => Unbounded || Visible.Contains(e.Issue!.ProjectId));
        modelBuilder.Entity<EfHatchIssueRunnerOmission>().HasQueryFilter(e => Unbounded || Visible.Contains(e.Issue!.ProjectId));
        modelBuilder.Entity<EfHatchRunnerFailureIssue>().HasQueryFilter(e => Unbounded || Visible.Contains(e.Issue!.ProjectId));
        modelBuilder.Entity<EfHatchIssueDependency>().HasQueryFilter(e => Unbounded || Visible.Contains(e.Issue!.ProjectId));
        modelBuilder.Entity<EfHatchDreamThread>().HasQueryFilter(e => Unbounded || Visible.Contains(e.Issue!.ProjectId));
        modelBuilder.Entity<EfHatchCapability>().HasQueryFilter(e => Unbounded || Visible.Contains(e.Issue!.ProjectId));
        modelBuilder.Entity<EfHatchCapabilityLink>().HasQueryFilter(e => Unbounded || Visible.Contains(e.Issue!.ProjectId));
        modelBuilder.Entity<EfHatchCapabilityVerdict>().HasQueryFilter(e => Unbounded || Visible.Contains(e.Capability!.Issue!.ProjectId));
        modelBuilder.Entity<EfHatchDreamRemand>().HasQueryFilter(e => Unbounded || Visible.Contains(e.Issue!.ProjectId));
        modelBuilder.Entity<EfHatchChangelogTask>().HasQueryFilter(e => Unbounded || Visible.Contains(e.Issue!.ProjectId));
        modelBuilder.Entity<EfHatchChangeOutcome>().HasQueryFilter(e => Unbounded || Visible.Contains(e.Issue!.ProjectId));

        // Unlike EfHatchPlaybook/EfHatchDream/EfHatchAttachment above, a
        // finding or a sighting with no project is nobody's - a check that
        // names no project (none exists yet) is read only by a caller whose
        // scope is everything, not by every caller the way a global playbook is.
        modelBuilder.Entity<EfHatchGooseSighting>().HasQueryFilter(e =>
            Unbounded || (e.ProjectId != null && Visible.Contains(e.ProjectId.Value)));
        modelBuilder.Entity<EfHatchGooseFinding>().HasQueryFilter(e =>
            Unbounded || (e.ProjectId != null && Visible.Contains(e.ProjectId.Value)));

        // A proposal belongs to its issue; with no issue, to the playbooks its
        // changes edit. One with neither is nobody's, and only an unbounded
        // caller reads it.
        //
        // The changes are read with IgnoreQueryFilters, and that is the one
        // opt-out inside the model itself: a change's filter reads its proposal
        // and the proposal's filter reads its changes, and EF applies each
        // target's filter without a guard against the loop - it overflows the
        // stack. The playbook rule is written out here instead, so nothing
        // hidden is let through by the opt-out.
        modelBuilder.Entity<EfHatchPlaybookProposal>().HasQueryFilter(e =>
            Unbounded
            || (e.IssueId != null
                ? Visible.Contains(e.Issue!.ProjectId)
                : PlaybookProposalChanges.IgnoreQueryFilters().Any(c => c.ProposalId == e.Id && c.Playbook != null
                    && (c.Playbook.ProjectId == null || Visible.Contains(c.Playbook.ProjectId.Value)))));
        modelBuilder.Entity<EfHatchPlaybookProposalChange>().HasQueryFilter(e =>
            Unbounded
            || (e.Proposal!.IssueId != null
                ? Visible.Contains(e.Proposal.Issue!.ProjectId)
                : e.Playbook != null && (e.Playbook.ProjectId == null || Visible.Contains(e.Playbook.ProjectId.Value))));

        // Keyed on a repository rather than a project: visible when any
        // visible project binds that canonical.
        modelBuilder.Entity<EfHatchTrunkBuild>().HasQueryFilter(e =>
            Unbounded || ProjectRepositories.Any(r => r.Canonical == e.Canonical && Visible.Contains(r.ProjectId)));
        modelBuilder.Entity<EfHatchPlaybookFile>().HasQueryFilter(e =>
            Unbounded || ProjectRepositories.Any(r => r.Canonical == e.Canonical && Visible.Contains(r.ProjectId)));
        modelBuilder.Entity<EfHatchDependencyFile>().HasQueryFilter(e =>
            Unbounded || ProjectRepositories.Any(r => r.Canonical == e.Canonical && Visible.Contains(r.ProjectId)));
        modelBuilder.Entity<EfHatchChangelogFile>().HasQueryFilter(e =>
            Unbounded || ProjectRepositories.Any(r => r.Canonical == e.Canonical && Visible.Contains(r.ProjectId)));


        // Search documents, each a STORED generated tsvector column. These
        // carry a weight per source column, which HasGeneratedTsVectorColumn
        // has no overload to express, so the SQL is spelled out rather than
        // built from a lambda.
        //
        // Guarded by provider because a tsvector has no InMemory equivalent,
        // and this module's unit tests run on InMemory.
        if (Database.IsNpgsql())
        {
            modelBuilder.Entity<EfHatchIssue>()
                .Property(i => i.SearchVector)
                .HasComputedColumnSql(
                    """setweight(to_tsvector('english', "Title"), 'A') || setweight(to_tsvector('english', "Description"), 'B') || setweight(to_tsvector('english', "PreviousTitles"), 'B')""",
                    stored: true);
            modelBuilder.Entity<EfHatchIssue>()
                .HasIndex(i => i.SearchVector)
                .HasMethod("GIN");

            modelBuilder.Entity<EfHatchComment>()
                .Property(c => c.SearchVector)
                .HasComputedColumnSql(
                    """setweight(to_tsvector('english', "Body"), 'C')""",
                    stored: true);
            modelBuilder.Entity<EfHatchComment>()
                .HasIndex(c => c.SearchVector)
                .HasMethod("GIN");

            modelBuilder.Entity<EfHatchWorkLogEntry>()
                .Property(w => w.SearchVector)
                .HasComputedColumnSql(
                    """setweight(to_tsvector('english', coalesce("Title", '')), 'D') || setweight(to_tsvector('english', coalesce("Summary", '')), 'D')""",
                    stored: true);
            modelBuilder.Entity<EfHatchWorkLogEntry>()
                .HasIndex(w => w.SearchVector)
                .HasMethod("GIN");

            modelBuilder.Entity<EfHatchDream>()
                .Property(d => d.SearchVector)
                .HasComputedColumnSql(
                    """setweight(to_tsvector('english', "Body"), 'A')""",
                    stored: true);
            modelBuilder.Entity<EfHatchDream>()
                .HasIndex(d => d.SearchVector)
                .HasMethod("GIN");
        }
        else
        {
            modelBuilder.Entity<EfHatchIssue>().Ignore(i => i.SearchVector);
            modelBuilder.Entity<EfHatchComment>().Ignore(c => c.SearchVector);
            modelBuilder.Entity<EfHatchWorkLogEntry>().Ignore(w => w.SearchVector);
            modelBuilder.Entity<EfHatchDream>().Ignore(d => d.SearchVector);
        }

        base.OnModelCreating(modelBuilder);
    }
}

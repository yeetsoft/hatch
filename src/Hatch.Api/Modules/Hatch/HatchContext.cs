using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Hatch's slice of the Hatch database: the <c>hatch</c> schema, its own
/// migration history, twenty-three tables.
/// </summary>
public class HatchContext(DbContextOptions<HatchContext> options) : DbContext(options), IModuleContext
{
    public const string Schema = "hatch";

    public DbSet<EfHatchProject> Projects => Set<EfHatchProject>();
    public DbSet<EfHatchProjectRepository> ProjectRepositories => Set<EfHatchProjectRepository>();
    public DbSet<EfHatchProjectMember> ProjectMembers => Set<EfHatchProjectMember>();
    public DbSet<EfHatchOvernightSeen> OvernightSeen => Set<EfHatchOvernightSeen>();
    public DbSet<EfHatchProjectLogo> ProjectLogos => Set<EfHatchProjectLogo>();
    public DbSet<EfHatchImage> Images => Set<EfHatchImage>();
    public DbSet<EfHatchStatus> Statuses => Set<EfHatchStatus>();
    public DbSet<EfHatchIssue> Issues => Set<EfHatchIssue>();
    public DbSet<EfHatchComment> Comments => Set<EfHatchComment>();
    public DbSet<EfHatchIssueEvent> IssueEvents => Set<EfHatchIssueEvent>();
    public DbSet<EfHatchPlaybook> Playbooks => Set<EfHatchPlaybook>();
    public DbSet<EfHatchPlaybookProposal> PlaybookProposals => Set<EfHatchPlaybookProposal>();
    public DbSet<EfHatchPlaybookProposalChange> PlaybookProposalChanges => Set<EfHatchPlaybookProposalChange>();
    public DbSet<EfHatchWipLimit> WipLimits => Set<EfHatchWipLimit>();
    public DbSet<EfHatchIssueDependency> Dependencies => Set<EfHatchIssueDependency>();
    public DbSet<EfHatchWorkLogEntry> WorkLog => Set<EfHatchWorkLogEntry>();
    public DbSet<EfHatchMergeCheck> MergeChecks => Set<EfHatchMergeCheck>();
    public DbSet<EfHatchBuildCheck> BuildChecks => Set<EfHatchBuildCheck>();
    public DbSet<EfHatchLanding> Landings => Set<EfHatchLanding>();
    public DbSet<EfHatchTrunkBuild> TrunkBuilds => Set<EfHatchTrunkBuild>();
    public DbSet<EfHatchPlaybookFile> PlaybookFiles => Set<EfHatchPlaybookFile>();
    public DbSet<EfHatchVerdict> Verdicts => Set<EfHatchVerdict>();

    /// <summary>
    /// The processes that have spoken to this Hatch lately, and what the board
    /// would like each to do next. Keyed on the name a runner already calls
    /// itself by, and related to nothing: a runner is an identity a checkout
    /// computes, not a row that belongs to an issue.
    /// </summary>
    public DbSet<EfHatchRunner> Runners => Set<EfHatchRunner>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        var issue = modelBuilder.Entity<EfHatchIssue>();

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

        // Two places, because a spend cap is dollars and cents and the default
        // scale would refuse the number an operator types into the box.
        modelBuilder.Entity<EfHatchRunner>().Property(r => r.MaxSpend).HasPrecision(18, 2);
        // The same scale CostUsd has: what a runner reports having spent is
        // priced per token, and the CLI rounds it for display.
        modelBuilder.Entity<EfHatchRunner>().Property(r => r.Spent).HasPrecision(18, 8);

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

            // At most one project binds the tracker's own repository. The
            // controller clears the old holder first; this is what makes a race
            // between two writers fail instead of leaving two.
            e.Property(p => p.BindsTracker).HasDefaultValue(false);
            e.HasIndex(p => p.BindsTracker).IsUnique().HasFilter("\"BindsTracker\" = true");
            e.Property(p => p.RunnerTags).HasColumnType("jsonb").HasDefaultValue("[]");
            e.Property(p => p.StoriesOnEpics).HasDefaultValue(false);
            e.Property(p => p.CheckApprovesMerge).HasDefaultValue(false);

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

        modelBuilder.Entity<EfHatchPlaybookFile>(e =>
        {
            e.Property(f => f.Rows).HasColumnType("jsonb");
        });

        // Search documents, the same STORED-generated-column shape
        // StorageContext.OnModelCreating uses for Item.SearchVector - except
        // these three carry a weight per source column, which
        // HasGeneratedTsVectorColumn has no overload to express, so the SQL is
        // spelled out rather than built from a lambda.
        //
        // Guarded by provider for the same reason as Storage's: a tsvector has
        // no InMemory equivalent, and this module's unit tests run on InMemory.
        if (Database.IsNpgsql())
        {
            modelBuilder.Entity<EfHatchIssue>()
                .Property(i => i.SearchVector)
                .HasComputedColumnSql(
                    """setweight(to_tsvector('english', "Title"), 'A') || setweight(to_tsvector('english', "Description"), 'B')""",
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
        }
        else
        {
            modelBuilder.Entity<EfHatchIssue>().Ignore(i => i.SearchVector);
            modelBuilder.Entity<EfHatchComment>().Ignore(c => c.SearchVector);
            modelBuilder.Entity<EfHatchWorkLogEntry>().Ignore(w => w.SearchVector);
        }

        base.OnModelCreating(modelBuilder);
    }
}

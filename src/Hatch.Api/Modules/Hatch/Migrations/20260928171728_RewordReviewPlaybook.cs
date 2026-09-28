using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// The stock review playbook - the row whose two ends are the review column -
    /// now covers a failing build as well as a merge conflict.
    /// </summary>
    /// <remarks>
    /// <para>An issue in review is dispatched when its branch conflicts with the
    /// trunk or when the build on its tip has failed, and both are answered by the
    /// one playbook: the runner brings the facts (a <c>## The conflict</c> or a
    /// <c>## The failing build</c> section) and the playbook says what to do with
    /// them. The seeded text was written for a conflict alone, and says the merge
    /// is in progress, which is false for a build dispatch - the reworded text
    /// points at the branch section for the state of the tree instead.</para>
    ///
    /// <para>Only a row that still says <see cref="SeedConflictPlaybook.Stock"/> is
    /// rewritten, whole-prompt and byte-identical, the way
    /// <see cref="PlaybookBranchStep"/> is: an edited prompt is somebody's wording
    /// and is not touched, and the operator is told on the ticket what to change.
    /// A fresh install runs the seed and then this, so both get the new text. The
    /// prompt is a literal here and not a reference, because a migration is frozen
    /// text and an edit to a constant elsewhere must not change what this one
    /// did.</para>
    /// </remarks>
    public partial class RewordReviewPlaybook : Migration
    {
        /// <summary>The prompt as it is now, and so as a row this migration touched still is.</summary>
        public const string Stock = """
            You are working on a branch that is already up for review, because something has
            stopped it merging or stopped its build: either the trunk moved after its pull
            request opened and the two no longer merge, or the build on its tip failed. The
            sections below say which. The runner has checked the branch out with the trunk
            merged in; the branch section says what state the tree is in, and that is the
            state you start from.

            - If there is a conflict section, the merge with the trunk is in progress and has
              conflicts. Resolve each conflicted file so that both sides' intent survives. The
              trunk's side is merged and reviewed work; the branch's side is what the pull
              request is for. Where the two cannot both stand, say why on the ticket and stop.
            - If there is a failing build section, read each failing check and its log
              excerpt. Reproduce the failure with the repository's own commands where you can:
              some tests only run against a database, and the excerpt is then the only account
              of the failure there is. Fix the cause. A check that is flaky rather than broken
              is still yours to report on the ticket, not to paper over.
            - Fix that and whatever it broke - and nothing else. This is not the increment to
              improve the change.
            - Build and test with the repository's own commands until they are green.
            - Commit and push to the same branch. Never rebase, never force-push, and never
              push to the trunk.
            - Comment the new sha on the ticket, and what was wrong.
            - Leave the ticket in the column it is in.
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Dollar-quoted, so neither prompt needs a character escaped, and
            // compared whole: "byte-identical" is the guard.
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "Prompt" = $new${{Stock}}$new$,
                       "UpdatedAt" = now()
                 WHERE "FromStatusId" = "ToStatusId" AND "Prompt" = $old${{SeedConflictPlaybook.Stock}}$old$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The mirror of the guard: only a row that still says exactly what
            // this migration wrote is put back.
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "Prompt" = $old${{SeedConflictPlaybook.Stock}}$old$,
                       "UpdatedAt" = now()
                 WHERE "FromStatusId" = "ToStatusId" AND "Prompt" = $new${{Stock}}$new$;
                """);
        }
    }
}

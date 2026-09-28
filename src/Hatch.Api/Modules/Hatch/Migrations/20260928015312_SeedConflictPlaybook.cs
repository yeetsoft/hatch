using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// The stock conflict playbook: what an agent is told when a pull request in
    /// review has stopped merging cleanly.
    /// </summary>
    /// <remarks>
    /// <para>A playbook whose two ends are the review column is the conflict
    /// playbook (<c>Columns.Target</c>). It is seeded for the reason the other
    /// seven are: a feature that does nothing until somebody fills in a table
    /// ships broken. An operator who deletes the row turns conflict work off, and
    /// nothing puts it back - which is why this only inserts where no row from
    /// the review column to itself exists, and why <see cref="Down"/> only
    /// removes one that still says the stock text.</para>
    ///
    /// <para>The review column is measured in SQL the way
    /// <c>Columns.AwaitingReview</c> measures it: the non-deferred column
    /// immediately left of the first terminal one, counted in the board's own
    /// order. An install with no terminal column has a review column that is where
    /// work ends and is not dispatched, so it gets no row. The prompt is a
    /// literal here and not a reference: a migration is frozen text, and an edit
    /// to a constant somewhere else must not change what this one did.</para>
    /// </remarks>
    public partial class SeedConflictPlaybook : Migration
    {
        /// <summary>The prompt as it is seeded, and so as an unedited row still is.</summary>
        public const string Stock = """
            You are resolving merge conflicts on a branch that is already up for review.
            The trunk moved after its pull request opened, and the two no longer merge. The
            runner has checked the branch out and started merging the trunk into it: the
            merge is in progress, and the conflict section below names the files, the
            branch and both shas.

            - Resolve each conflicted file so that both sides' intent survives. The trunk's
              side is merged and reviewed work; the branch's side is what the pull request
              is for. Where the two cannot both stand, say why on the ticket and stop.
            - Fix the conflict and whatever the merge broke - a build error, a failing test
              - and nothing else. This is not the increment to improve the change.
            - Build and test with the repository's own commands until they are green.
            - Commit the merge and push it to the same branch. Never rebase, never
              force-push, and never push to the trunk.
            - Comment the new sha on the ticket.
            - Leave the ticket in the column it is in.
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Dollar-quoted, because the prompt is prose and prose has
            // apostrophes in it.
            migrationBuilder.Sql(
                $$"""
                WITH board AS (
                    SELECT "Id", "IsTerminal", ROW_NUMBER() OVER (ORDER BY "SortOrder", "Id") AS n
                      FROM hatch."Statuses"
                     WHERE NOT "IsDeferred"
                ),
                review AS (
                    SELECT b."Id"
                      FROM board b
                     WHERE b.n = (SELECT MIN(n) FROM board WHERE "IsTerminal") - 1
                )
                INSERT INTO hatch."Playbooks"
                    ("FromStatusId", "ToStatusId", "Types", "Prompt", "Model", "Effort", "CreatedAt", "UpdatedAt")
                SELECT r."Id", r."Id", '', $prompt${{Stock}}$prompt$, 'sonnet', 'high', now(), now()
                  FROM review r
                 WHERE NOT EXISTS (
                    SELECT 1 FROM hatch."Playbooks" p
                     WHERE p."FromStatusId" = r."Id" AND p."ToStatusId" = r."Id");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The mirror of the guard: only a row still saying exactly what this
            // migration wrote goes. An edited one is somebody's wording.
            migrationBuilder.Sql(
                $$"""
                DELETE FROM hatch."Playbooks"
                 WHERE "FromStatusId" = "ToStatusId" AND "Prompt" = $prompt${{Stock}}$prompt$;
                """);
        }
    }
}

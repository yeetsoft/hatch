using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class SeedConflictPlaybook : Migration
    {
        /// <summary>
        /// The prompt the seeded row carries, frozen here: a migration is text
        /// that ran once, and <see cref="Down"/> recognises an unedited row by
        /// comparing against it. Nothing else reads this - the test holds its own
        /// copy of the same words, so an edit to one that forgets the other fails
        /// a test rather than a rollback.
        /// </summary>
        private const string StockPrompt = """
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
            // The conflict playbook: a row from the review column to itself,
            // for every type. Seeded for the reason the rest of the matrix is
            // (20260903204217_Playbooks.cs): a feature that does nothing until
            // somebody fills in a table ships broken.
            //
            // The review column is measured the way Columns.AwaitingReview
            // measures it, and the way the Playbooks migration placed it: the
            // non-deferred column immediately left of the first terminal one,
            // or the rightmost non-deferred column on a board with no terminal
            // column. Not named - an operator renames columns.
            //
            // Only where no row from the review column to itself exists, of any
            // types. An operator who deleted the row turned conflict work off,
            // and a later migration does not put it back - this one runs once,
            // and the guard is for an install that wrote its own first.
            migrationBuilder.Sql($$"""
            INSERT INTO hatch."Playbooks"
                ("FromStatusId", "ToStatusId", "Types", "Prompt", "Model", "Effort", "CreatedAt", "UpdatedAt")
            SELECT r."Id", r."Id", '', $prompt${{StockPrompt}}$prompt$, 'sonnet', 'high', now(), now()
            FROM (
                SELECT b."Id"
                FROM (
                    SELECT "Id", "IsTerminal",
                           ROW_NUMBER() OVER (ORDER BY "SortOrder", "Id") AS pos
                    FROM hatch."Statuses"
                    WHERE NOT "IsDeferred"
                ) b
                WHERE b.pos = COALESCE(
                    (SELECT MIN(t.pos) - 1 FROM (
                        SELECT "IsTerminal", ROW_NUMBER() OVER (ORDER BY "SortOrder", "Id") AS pos
                        FROM hatch."Statuses"
                        WHERE NOT "IsDeferred"
                    ) t WHERE t."IsTerminal"),
                    (SELECT COUNT(*) FROM hatch."Statuses" WHERE NOT "IsDeferred"))
            ) r
            WHERE NOT EXISTS (
                SELECT 1 FROM hatch."Playbooks" p
                WHERE p."FromStatusId" = r."Id" AND p."ToStatusId" = r."Id");
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only a row still carrying the stock words: one an operator has
            // edited is theirs, and taking it out on a rollback would be
            // deleting their work. Only the review column may name itself, so
            // "from equals to" finds the row without measuring the board again.
            migrationBuilder.Sql($$"""
            DELETE FROM hatch."Playbooks"
            WHERE "FromStatusId" = "ToStatusId" AND "Types" = ''
              AND "Prompt" = $prompt${{StockPrompt}}$prompt$;
            """);
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// The epic's implementation-to-review row stops asking "is every child
    /// finished" and starts asking "does the trunk now do what the epic
    /// promised" - the same shift <see cref="SeedCloseoutPlaybook"/> made for a
    /// story or a task, one altitude up.
    /// </summary>
    /// <remarks>
    /// <para>Before this migration the row seeded for an epic on this transition
    /// (<c>Playbooks.cs</c>, 20260903204217) only checked that every child had
    /// closed, and left the verification itself - whether what shipped is what
    /// the epic's description promised - to the operator reading the diff by
    /// eye. <see cref="Stock"/> is that verification: it checks each acceptance
    /// criterion against the trunk, comments the verdict, and either moves the
    /// epic on or files what is missing as a child under it. It never closes the
    /// epic and never moves anything into a terminal column - only the operator
    /// decides that something shipped (CLAUDE.md, "What only the operator
    /// does").</para>
    ///
    /// <para><see cref="Up"/> runs in two guarded, independent steps. The first
    /// rewords a row that still says <see cref="Seeded"/> byte for byte - the
    /// same guard <see cref="RewordReviewPlaybook"/> uses - and lifts its model
    /// and effort from <c>sonnet</c>/<c>medium</c> to <c>opus</c>/<c>high</c>
    /// only where it finds them still at that default; a retuned row is left at
    /// whatever the operator set. The second seeds the row where none covers an
    /// epic on this transition at all, the way <see cref="SeedCloseoutPlaybook"/>
    /// seeds its closeout row: the implementation and review columns are
    /// measured, not named, with the same three CTEs, and an install with no
    /// terminal column has no review column and this step writes nothing - the
    /// same behaviour those two already-shipped migrations commit to, even
    /// though <see cref="Columns.AwaitingReview"/> itself falls back to the
    /// rightmost column at runtime when there is no terminal one. That mismatch
    /// predates this migration and is not this migration's to resolve; matching
    /// its two siblings keeps one convention across all three rather than
    /// introducing a third.</para>
    ///
    /// <para>The insert's guard is <see cref="EfHatchPlaybook.Covers"/> spelled
    /// in SQL: a row already covers this seed where its <c>Types</c> is empty or
    /// lists <c>epic</c>, and its <c>Shape</c> is <c>any</c> or <c>parent</c>. An
    /// epic only ever reaches this transition with at least one child - a
    /// childless one is folded earlier - so it is always the <c>parent</c> shape,
    /// and a row scoped to <c>leaf</c> never covers it however wide its
    /// <c>Types</c>. The shape half of the guard is new here: <c>PlaybookShape</c>
    /// (20260930171416) landed after this row was first seeded, so
    /// <see cref="SeedConflictPlaybook"/>, which predates the column, did not
    /// need it.</para>
    ///
    /// <para><see cref="Down"/> is the mirror of the reword, and the same
    /// statement undoes both halves of <see cref="Up"/>: whichever row ended up
    /// saying <see cref="Stock"/>, whether this migration reworded it or
    /// inserted it, is put back to saying <see cref="Seeded"/>, with its model
    /// and effort dropped back to <c>sonnet</c>/<c>medium</c> only where they are
    /// still exactly <c>opus</c>/<c>high</c>. <see cref="Down"/> cannot tell a
    /// reworded row from an inserted one - the insert's own guard already sees
    /// the reword's effect, since both run in one <see cref="Up"/> - and putting
    /// the old text back rather than deleting is the less harmful of the two
    /// mistakes either way, the same call <see cref="RewordReviewPlaybook"/>
    /// already made. Both prompts are literals here, not references to a
    /// constant declared elsewhere: a migration is frozen text, and an edit to a
    /// constant somewhere else must not change what this one did.</para>
    /// </remarks>
    public partial class VerifyEpicPlaybook : Migration
    {
        /// <summary>The prompt as the seed wrote it, and so as an unedited row still is.</summary>
        public const string Seeded = """
            You are deciding whether an epic is finished, not finishing it.

            - Read the epic, its children, and what has already happened to it.
            - If any child is unfinished, the epic is not ready for review. Comment naming
              the ones outstanding and what each is waiting on, leave the epic where it is,
              and stop. A sentence a person can act on is worth more than motion on the
              board.
            - If they are all finished, comment the summary a reviewer needs: what the epic
              delivered, what was cut and why, and anything you are unsure about.

            Write no implementation code in this increment. A child that still needs work is
            a child to work in its own increment.
            """;

        /// <summary>The verification prompt this migration rewords or seeds a row to carry.</summary>
        public const string Stock = """
            You are verifying an epic: checking what its description promised against what the
            trunk now does. Every story filed under it has shipped or been shelved, and this is
            the last look before the operator decides whether to close it. You do not close it.

            - Read the epic, its children and what has already happened to it - its comments
              and its events. A previous run may have been aborted partway; continue it rather
              than starting again.
            - Check each acceptance criterion in the epic's description against the trunk as
              origin has it, one at a time. Read the repository; where a criterion is testable,
              build and run the tests with the repository's own commands. A criterion you could
              not check is one to say out loud you could not check, not one to count as holding.
            - Comment the verdict on the epic: each criterion, whether it holds, and the
              evidence - a path, a test, a command and what it printed.
            - A criterion that a child still open, or a child in a deferred column, was filed
              for is not a gap. Name that child rather than filing its work a second time:
              shelving work is the operator's call, and it has been made.
            - If every criterion holds, or the only ones that do not were shelved with a
              deferred child, move the epic to the column named at the end of this prompt, and
              say in the verdict which criteria were shelved.
            - Otherwise file each gap under the epic - one story or bug per gap, with parentKey
              set to the epic, saying what is wrong or missing, with acceptance criteria
              somebody else could check - and leave the epic in the column it is in. The verdict
              names each issue you filed and the criterion it answers. A gap you cannot turn
              into a ticket is a question: ask it, and stop.
            - Never move the epic, or anything under it, into a terminal column. Only the
              operator decides that something shipped.

            Write no code, make no commit and push nothing. If the branch section names a
            branch to cut, you need none: nothing you do changes the tree.
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Reword a row that still says exactly what the seed wrote.
            // Guarded on Prompt alone, as PlaybookBranchStep and
            // RewordReviewPlaybook are - the text appears on no other row.
            // Model and effort only lift off the seed's own default, so a
            // retuned row is left at whatever the operator set.
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "Prompt" = $new${{Stock}}$new$,
                       "Model"  = CASE WHEN "Model" = 'sonnet' AND "Effort" = 'medium' THEN 'opus' ELSE "Model" END,
                       "Effort" = CASE WHEN "Model" = 'sonnet' AND "Effort" = 'medium' THEN 'high' ELSE "Effort" END,
                       "UpdatedAt" = now()
                 WHERE "Prompt" = $old${{Seeded}}$old$;
                """);

            // 2) Seed the row where none covers an epic on this transition at
            // all - measuring the review and implementation columns the way
            // SeedCloseoutPlaybook measures them, and guarding the insert on
            // EfHatchPlaybook.Covers("epic", isParent: true) spelled in SQL. On
            // a board with no terminal column, or one too short to have both
            // columns, review (and so impl) is empty and this writes nothing.
            migrationBuilder.Sql(
                $$"""
                WITH board AS (
                    SELECT "Id", "IsTerminal", ROW_NUMBER() OVER (ORDER BY "SortOrder", "Id") AS n
                      FROM hatch."Statuses"
                     WHERE NOT "IsDeferred"
                ),
                review AS (
                    SELECT b."Id", b.n
                      FROM board b
                     WHERE b.n = (SELECT MIN(n) FROM board WHERE "IsTerminal") - 1
                ),
                impl AS (
                    SELECT b."Id"
                      FROM board b, review r
                     WHERE b.n = r.n - 1
                )
                INSERT INTO hatch."Playbooks"
                    ("FromStatusId", "ToStatusId", "Types", "Prompt", "Model", "Effort", "CreatedAt", "UpdatedAt")
                SELECT i."Id", r."Id", 'epic', $prompt${{Stock}}$prompt$, 'opus', 'high', now(), now()
                  FROM impl i, review r
                 WHERE NOT EXISTS (
                    SELECT 1 FROM hatch."Playbooks" p
                     WHERE p."FromStatusId" = i."Id" AND p."ToStatusId" = r."Id"
                       AND (p."Types" = '' OR 'epic' = ANY(string_to_array(p."Types", ',')))
                       AND (p."Shape" = 'any' OR p."Shape" = 'parent'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The mirror of the reword, and what undoes the insert too: Down
            // cannot tell which of the two produced a row that now says Stock,
            // so every such row is put back to Seeded, with model and effort
            // dropped only where they are still exactly what step 1 set.
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "Prompt" = $old${{Seeded}}$old$,
                       "Model"  = CASE WHEN "Model" = 'opus' AND "Effort" = 'high' THEN 'sonnet' ELSE "Model" END,
                       "Effort" = CASE WHEN "Model" = 'opus' AND "Effort" = 'high' THEN 'medium' ELSE "Effort" END,
                       "UpdatedAt" = now()
                 WHERE "Prompt" = $new${{Stock}}$new$;
                """);
        }
    }
}
